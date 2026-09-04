# Phase 0 — Research: IISLogParser

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Fecha**: 2026-09-03

Este documento resuelve las incógnitas técnicas del `Technical Context` del plan. Cada decisión
indica su fundamento y las alternativas descartadas.

---

## D-001 — Plataforma y lenguaje

**Decisión**: Aplicación de consola en C# sobre .NET 10 (`net10.0`), publicable como ejecutable
único `IISLogParser.exe` para Windows x64.

**Fundamento**:

- El destino es un servidor Windows con IIS (supuesto de ejecución local en la spec). .NET es el
  runtime de primera clase de esa plataforma y no exige instalar nada ajeno al stack de Microsoft.
- Verificado en el entorno de desarrollo: `dotnet --version` reporta **10.0.302**, y el SDK 10
  está instalado. No hay que gestionar una instalación nueva.
- Precedente en el ecosistema del autor: el otro proyecto del repositorio (`docs/AGENTS_inicial.md`)
  ya usa .NET 10 y SQLite. Reutilizar stack reduce la carga cognitiva y el tooling.
- La publicación *self-contained* de archivo único permite dejar el binario en el servidor sin
  instalar el runtime, que es lo habitual en servidores de producción.

**Alternativas consideradas**:

- **PowerShell**: nativo del servidor y sin compilación, pero el rendimiento de parseo línea a
  línea es un orden de magnitud peor, y la disciplina de tests del Principio III es mucho más
  costosa de sostener.
- **Go / Rust**: excelente para el perfil de ingesta, pero introduce un stack ajeno al repositorio
  sin ganancia proporcional; el cuello de botella real es E/S de disco, no CPU.
- **Python**: buen parseo, pero exige runtime instalado en el servidor y complica el empaquetado.

---

## D-002 — Acceso a la base de datos

**Decisión**: `Microsoft.Data.Sqlite` (ADO.NET), sin ORM.

**Fundamento**:

- La carga de trabajo es inserción masiva secuencial más una consulta de progreso. Un ORM no
  aporta nada a ese perfil y sí agrega sobrecarga por fila y una capa de traducción que oscurece
  el control transaccional, que acá es el mecanismo central de correctitud (ver D-007).
- `Microsoft.Data.Sqlite` es el proveedor de primera parte, con soporte directo de comandos
  parametrizados reutilizables, que es lo que hace viable el rendimiento del SC-002.
- El esquema es fijo y pequeño; las migraciones automáticas de un ORM son innecesarias.

**Alternativas consideradas**:

- **EF Core + SQLite**: cómodo para CRUD, contraproducente para ingesta masiva; su rastreo de
  cambios haría falta desactivarlo en cada operación.
- **Dapper**: capa fina razonable, pero para dos sentencias parametrizadas no justifica una
  dependencia más.

---

## D-003 — Lectura de archivos sin bloquear a IIS

**Decisión**: abrir cada archivo con `FileStream` en modo lectura declarando
`FileShare.ReadWrite | FileShare.Delete`.

**Fundamento**:

- IIS mantiene el archivo del día abierto para escritura. Si el proceso lector no concede
  `FileShare.Write`, la apertura falla o —peor— IIS no puede seguir escribiendo. Incluir `Delete`
  además permite que el archivo se rote o borre mientras se lo lee, sin que la rotación falle por
  culpa del parser.
- Satisface FR-006 de forma directa y verificable con un test que mantenga un escritor abierto.

**Alternativas consideradas**:

- **Copiar el archivo antes de parsearlo**: elimina la contención, pero duplica E/S y espacio en
  disco sobre archivos que pueden pesar gigabytes, y abre una ventana de inconsistencia.
- **`FileSystemWatcher` en lugar de sondeo**: la spec fija explícitamente un modelo de sondeo por
  *Poll Interval*; además `FileSystemWatcher` es notoriamente poco confiable sobre escrituras
  continuas y no elimina la necesidad de leer por offset.

---

## D-004 — Lectura incremental y línea parcial

**Decisión**: leer por offset de bytes. Solo se ingestan líneas terminadas en salto de línea; el
resto del búfer (línea a medio escribir) se descarta de esa pasada y el offset confirmado queda en
el byte siguiente al último salto de línea completo.

**Fundamento**:

- IIS escribe la línea y su terminador en operaciones que el lector puede sorprender a mitad de
  camino. Confirmar una línea sin terminador ingestaría un registro truncado y, peor, dejaría el
  offset en un punto que impediría releer el resto de esa línea cuando se complete.
- El offset del inicio de cada línea es además el tercer componente de la clave de identidad
  (ver D-006), así que la lectura por offset no es un detalle de rendimiento sino el fundamento de
  la idempotencia.

**Alternativas consideradas**:

- **Contar líneas en lugar de bytes**: exige releer el archivo entero para reposicionarse tras un
  reinicio; el offset de byte es O(1).
- **Ingestar la línea parcial y actualizarla después**: obliga a un `UPDATE` sobre una fila ya
  escrita y rompe la simplicidad de "insertar y olvidar".

---

## D-005 — Formato W3C extendido: parseo

**Decisión**: parseo dirigido por la directiva `#Fields:`. Las líneas que empiezan con `#` son
directivas o comentarios y nunca se ingestan. Una nueva directiva `#Fields:` a mitad de archivo
reemplaza el mapa de columnas para las líneas siguientes. Los valores se separan por espacio
simple y el valor `-` significa campo vacío.

**Fundamento**:

- Cada sitio de IIS elige qué campos registrar y en qué orden; la cabecera es la única fuente
  confiable de ese mapeo. Parsear por posición fija rompería en cuanto un sitio tenga otra
  selección de campos (FR-003).
- IIS reescribe la directiva `#Fields:` cuando se cambia la configuración de logging sin reiniciar
  el sitio, de modo que el cambio a mitad de archivo es un caso real, no teórico.
- Una línea de datos que aparezca antes de cualquier `#Fields:` no puede interpretarse: se cuenta
  como rechazada (FR-005), no se adivina el esquema.

**Nombres de archivo**: la rotación diaria produce `u_exYYMMDD.log` cuando IIS usa UTC para el
nombre y la rotación (predeterminado), o `exYYMMDD.log` cuando se configura hora local. Ambos
patrones se reconocen y de ellos se deriva la fecha del archivo. Un archivo cuyo nombre no encaje
en ningún patrón conocido se ingesta igual en modo *snapshot*, pero no puede considerarse "de hoy"
en modo continuo, porque no hay forma confiable de fecharlo por nombre.

**Alternativas consideradas**:

- **Expresión regular por línea**: más lento y más frágil que un `Split` por espacios sobre un
  mapa de columnas precomputado.
- **Asumir el conjunto de campos predeterminado de IIS**: falla ante cualquier sitio personalizado
  y contradice FR-003.

---

## D-006 — Identidad del registro e idempotencia

**Decisión**: índice único sobre `(site_id, source_file, line_offset)`, con `INSERT OR IGNORE`
para toda inserción. `source_file` se guarda como ruta relativa a la carpeta raíz de logs.

**Fundamento**:

- Traslada la garantía del FR-011 al motor de base de datos: reprocesar una línea ya ingestada es
  una operación sin efecto, sin necesidad de consultar antes de insertar. Es también lo que hace
  que el descarte sea "silencioso y no contado como registro nuevo", como pide el FR-011: el
  contador de nuevos usa las filas realmente afectadas.
- La ruta relativa hace que la base sea portable entre servidores y que los tests puedan usar
  directorios temporales sin ensuciar los datos con rutas absolutas de la máquina de desarrollo.
- El caso de archivo reemplazado (FR-011a) se resuelve con un `DELETE` por `source_file` dentro de
  la misma transacción que reinicia el progreso: sin ese borrado, los offsets del contenido viejo
  colisionarían con los del contenido nuevo y el `INSERT OR IGNORE` descartaría filas legítimas.

**Alternativas consideradas**:

- **Hash del contenido de la línea**: descartado en la fase de clarificación; colapsa peticiones
  idénticas legítimas.
- **Consultar antes de insertar**: una consulta por línea destruye el objetivo de rendimiento.

---

## D-007 — Atomicidad de datos y progreso

**Decisión**: cada lote de ingesta escribe las filas **y** actualiza la marca de progreso del
archivo dentro de una única transacción. Lotes de 5.000 líneas o fin de archivo, lo que ocurra
primero.

**Fundamento**:

- Es el mecanismo que hace ciertos a la vez el FR-012 (reanudar donde quedó), el FR-017 (cierre
  ordenado) y el SC-006 (24 h sin perder registros). Si datos y progreso se confirmaran por
  separado, una caída entre ambos produciría duplicados (progreso viejo, filas nuevas) o pérdida
  (progreso nuevo, filas no escritas).
- El tamaño de lote acota la memoria y el trabajo perdido ante una caída, sin pagar el costo de
  una transacción por línea.

**Alternativas consideradas**:

- **Una transacción por línea**: correcto pero inaceptablemente lento.
- **Una transacción por archivo**: en un archivo histórico de gigabytes, mantiene una transacción
  abierta demasiado tiempo y consume memoria sin cota.

---

## D-008 — Configuración de SQLite

**Decisión**: `journal_mode=WAL`, `synchronous=NORMAL`, comandos preparados reutilizados dentro de
cada lote.

**Fundamento**:

- WAL permite que un lector externo (el analista consultando la tabla) trabaje mientras la
  herramienta ingesta, que es exactamente el escenario del modo continuo.
- `NORMAL` evita un `fsync` por transacción manteniendo la durabilidad frente a caída del proceso
  —el modo de fallo que importa acá—; solo un corte de energía podría perder la última
  transacción, y el diseño de D-007 hace que ese caso se recupere releyendo desde la última marca
  confirmada, sin duplicar.
- Reutilizar el comando preparado dentro del lote es lo que sostiene el SC-002 con holgura.

---

## D-009 — Ciclo del modo continuo

**Decisión**: bucle secuencial con `PeriodicTimer`: se ejecuta el ciclo completo y recién entonces
se espera el *Poll Interval*. Los ciclos nunca se solapan. En cada ciclo se recalcula el conjunto
vigilado, que es la unión de: (a) el archivo del día de cada sitio, y (b) todo archivo con bytes
pendientes respecto de su marca de progreso.

**Fundamento**:

- La regla (a) cubre el archivo de hoy y hace que el cambio de día se resuelva solo, sin lógica de
  medianoche: al día siguiente, "el archivo de hoy" simplemente es otro.
- La regla (b) es la que evita perder la cola del archivo del día anterior: aunque deje de ser "el
  de hoy", sigue teniendo bytes pendientes y por lo tanto sigue en el conjunto vigilado hasta que
  se lo termine de leer. Esto satisface el escenario 4 de la historia P2 sin ningún caso especial.
- Esperar *después* del ciclo, en vez de disparar por reloj, impide que los ciclos se acumulen
  cuando uno tarda más que el intervalo.

**Alternativas consideradas**:

- **Un hilo por sitio**: concurrencia innecesaria sobre un único archivo de base de datos con un
  solo escritor; agrega contención sin acelerar la E/S.
- **Temporizador disparando en paralelo**: produce solapamiento, justo el caso borde que la spec
  pide evitar.

---

## D-010 — Apagado ordenado

**Decisión**: `CancellationToken` propagado por todo el ciclo, disparado desde
`Console.CancelKeyPress` y `AppDomain.ProcessExit`. La cancelación se atiende entre lotes, nunca en
medio de uno: el lote en curso se confirma y recién entonces se sale.

**Fundamento**: FR-017 exige que no queden registros a medio escribir ni se pierda la marca de
progreso. Como datos y progreso viajan en la misma transacción (D-007), atender la cancelación en
el borde del lote es suficiente y no requiere lógica de compensación.

---

## D-011 — Análisis de la línea de comandos

**Decisión**: analizador propio, mínimo, sin dependencias externas. Opciones: `--mode`,
`--poll-interval`, `--logs-path`, `--database`, `--help`.

**Fundamento**:

- Son cuatro opciones con validación trivial. Una biblioteca de parseo agregaría una dependencia y
  su superficie de comportamiento a un componente que debe ser exhaustivamente testeable y
  determinista (FR-020 exige rechazos precisos con código de salida).
- Mantener el análisis en código propio permite testear el mapeo argumentos → configuración como
  una función pura, sin proceso ni E/S.

**Alternativa considerada**: `System.CommandLine`, que regala `--help` y validación; descartada por
no justificar una dependencia para cuatro opciones, y por atarse a la superficie de una API que
todavía se mueve entre versiones.

---

## D-012 — Registro estructurado (Principio IV de la constitución)

**Decisión**: dos salidas distintas y separadas.

1. **Reporte al operador** (stdout): texto legible, con la configuración efectiva al arrancar
   (FR-021) y el resumen por ciclo o ejecución (FR-022).
2. **Log de diagnóstico** (stderr): JSON por línea, vía `Microsoft.Extensions.Logging` con el
   formateador JSON de consola. Se instrumentan únicamente límites del sistema (arranque, apertura
   y cierre de archivo, confirmación de lote), decisiones (archivo reemplazado, cambio de mapa de
   campos, omisión) y fallos. No se instrumenta el flujo interno línea a línea.

**Fundamento**:

- El Principio IV exige logs estructurados y consultables, y prohíbe explícitamente instrumentar el
  flujo interno paso a paso. Separar el reporte humano del log estructurado evita el error habitual
  de intentar que un mismo flujo sirva a las dos cosas y termine sirviendo mal a ambas.
- Separar por descriptor (stdout / stderr) permite redirigir el JSON a un colector sin ensuciar la
  salida que lee el operador, y viceversa.
- La parte del Principio IV sobre identificador de modelo, versión de prompt y fuente consultada es
  **N/A**: esta feature no invoca ningún modelo.
- La prohibición de PII en logs se cumple de forma trivial: según la decisión registrada en la fase
  de clarificación, ningún campo de los logs de IIS se clasifica como dato personal en este
  proyecto. La prohibición de secretos sí se sostiene: no hay credenciales en este flujo.

---

## D-013 — Estrategia de tests (Principio III)

**Decisión**: xUnit, con tres niveles y cero acceso a red o credenciales.

- **Unitarios**: parser W3C, analizador de CLI, resolución de "archivo de hoy", detección de
  truncamiento. Funciones puras sobre datos en memoria.
- **Integración**: árboles de directorios sintéticos creados en un directorio temporal por test,
  con archivos de log de referencia, y una base SQLite en archivo temporal. Cubren idempotencia
  (SC-004), ingesta incremental, reemplazo de archivo y omisión por archivo ilegible.
- **Contrato**: la superficie de CLI (códigos de salida, formato del reporte) y el esquema de la
  base, contra `contracts/`.

**Fundamento**:

- El Principio III exige una suite determinista ejecutable sin credenciales ni red. Nada en esta
  feature necesita ninguna de las dos cosas: la única dependencia externa es el sistema de
  archivos, que se sustituye por directorios temporales.
- El modo continuo se testea inyectando el reloj y el disparador de ciclo, de modo que un test
  ejecute N ciclos determinísticamente en vez de esperar segundos reales. Sin esa inyección, la
  suite sería lenta y susceptible a fallos intermitentes.

**Nota sobre archivos ilegibles en tests**: reproducir un fallo de permisos de forma portable es
frágil. La ingesta accede al sistema de archivos por una interfaz delgada, de modo que el test
sustituye esa interfaz por una que lanza el error deseado. La interfaz existe por testabilidad, no
por abstracción especulativa.

---

## Incógnitas restantes

Ninguna. Todo `NEEDS CLARIFICATION` del `Technical Context` quedó resuelto en este documento.
