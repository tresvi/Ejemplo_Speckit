# Feature Specification: IISLogParser — Ingesta de logs de IIS a base de datos

**Feature Branch**: `001-iis-log-parser`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "Quiero desarrollar un aplicativo de consola que al invocarlo vaya a la carpeta de los logs de IIS y parsee todos los archivos de logs encontrados. Al resultado del parseo, lo coloque en una tabla, en una base de datos SQLite. En principio, todos los registros de todas las carpetas que corresponden a cada sitio, deberían ir en una misma tabla. Por esto, la tabla deberá tener un campo que identifique el sitio (W3SVC1, W3SVC2, etc). Pero no solo eso, tambien debe: El programa tendrá dos modos: el modo snapshot, que cuando lo ejecute levantará todos los logs que encuentre y luego finalizará; y el modo continous, que lo dejará en ejecución continua y quedará vigilando los logs que encuentre el día de hoy. La revisión de si un log cambió (es decir, si tiene líneas nuevas) cada un intervalo de segundos llamado Poll Interval. En caso de haber cambiado, debe actualizar la tabla de logs con los logs nuevos. El programa se llamará IISLogParser. Si lo ejecuto sin ningún parámetro, el programa se ejecutará en modo continous, con un Poll interval por default de 10 segundos."

## Clarifications

### Session 2026-09-03

- Q: ¿Qué regla de identidad determina que dos filas son el mismo registro (base de la
  idempotencia del FR-011)? → A: Clave única `(sitio, archivo, offset de la línea)`. Si el archivo
  se reemplaza o trunca, se borran las filas ya ingestadas de ese archivo y se reingesta completo.
- Q: ¿Dónde se preservan los campos que un sitio registra y que no están contemplados en el modelo
  de datos? → A: Esquema fijo con columnas tipadas para los campos habituales de IIS, más una
  columna adicional que preserva los campos no contemplados como pares clave-valor.
- Q: ¿Cómo se trata la PII presente en los logs (IP de cliente, usuario) en la salida diagnóstica,
  dado el Principio IV de la constitución? → A: Ningún dato de los logs de IIS se clasifica como
  PII en este proyecto. Se ingestan y se reportan tal como aparecen, sin enmascarado y sin
  recortar el contenido en la salida diagnóstica.
- Q: En modo continuo, ¿desde dónde empieza a leer el archivo del día al arrancar? → A: Ingesta el
  archivo del día de cada sitio completo desde el principio, respetando la marca de progreso si ya
  existe, y luego sigue en modo incremental. No arrastra el histórico de días anteriores.
- Q: ¿Qué hace el sistema cuando un archivo o una carpeta de sitio resulta ilegible durante la
  corrida? → A: Lo omite, continúa con el resto, lo reporta explícitamente y, en modo snapshot,
  termina con código de salida distinto de cero. En modo continuo lo reintenta en el ciclo
  siguiente, sin abortar el proceso.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Carga completa de los logs existentes (Priority: P1)

Un operador de infraestructura necesita analizar el tráfico histórico de todos los sitios
alojados en un servidor IIS. Hoy esa información vive dispersa en cientos de archivos de texto,
uno por sitio y por día. El operador ejecuta `IISLogParser` en modo *snapshot*: la herramienta
recorre la carpeta de logs de IIS, detecta cada carpeta de sitio (`W3SVC1`, `W3SVC2`, ...),
parsea todos los archivos que encuentra y deja cada petición como una fila en una única tabla
consultable, con el identificador del sitio en cada fila. Al terminar, el proceso finaliza y
reporta cuántos archivos y cuántos registros procesó.

**Why this priority**: Es el núcleo de la propuesta de valor. Sin la ingesta de los archivos
existentes no hay nada que consultar, y el modo continuo no tiene sobre qué construir. Entregada
sola, ya reemplaza el análisis manual con editores de texto.

**Independent Test**: Se puede probar de punta a punta apuntando la herramienta a una carpeta de
logs de prueba con varios sitios y varios días, ejecutando el modo snapshot, y verificando que la
tabla resultante contiene exactamente una fila por línea de petición de cada archivo, con el sitio
correcto en cada una.

**Acceptance Scenarios**:

1. **Given** una carpeta de logs con las carpetas `W3SVC1` y `W3SVC2`, cada una con dos archivos
   de log, **When** el operador ejecuta la herramienta en modo snapshot, **Then** la tabla de
   destino contiene todos los registros de los cuatro archivos y cada fila indica el sitio del que
   proviene.
2. **Given** una carpeta de logs vacía o sin carpetas de sitio, **When** el operador ejecuta el
   modo snapshot, **Then** el proceso termina sin error, informando que no encontró archivos que
   procesar.
3. **Given** un snapshot ya ejecutado sobre una carpeta, **When** el operador vuelve a ejecutar el
   mismo snapshot sin que los archivos hayan cambiado, **Then** no se agregan registros duplicados
   y el reporte final indica cero registros nuevos.
4. **Given** un archivo de log que declara sus columnas en su cabecera de formato, **When** se lo
   parsea, **Then** cada valor queda en el campo que la cabecera le asigna, independientemente del
   orden de las columnas en ese archivo.
5. **Given** un archivo con una línea malformada entre líneas válidas, **When** se lo parsea,
   **Then** las líneas válidas se ingestan, la línea malformada se descarta y se reporta como
   rechazada sin abortar el procesamiento del archivo.
6. **Given** una carpeta de logs donde uno de los archivos no puede leerse, **When** el operador
   ejecuta el modo snapshot, **Then** el resto de los archivos se ingesta completo, la omisión se
   reporta con su causa y el proceso termina con código de salida distinto de cero.

---

### User Story 2 - Seguimiento continuo de los logs del día (Priority: P2)

El mismo operador quiere ver la actividad del servidor casi en tiempo real, sin volver a ejecutar
la herramienta a mano. Ejecuta `IISLogParser` en modo *continuous*: el proceso queda vivo, revisa
cada *Poll Interval* segundos los archivos de log correspondientes al día en curso de cada sitio,
y cuando alguno creció, ingesta únicamente las líneas nuevas. El proceso se detiene solo cuando el
operador lo interrumpe.

**Why this priority**: Convierte una foto en un flujo. Es lo que permite usar los datos para
monitoreo y no solo para análisis forense, pero depende de que la ingesta y el modelo de datos de
la P1 ya existan.

**Independent Test**: Se puede probar arrancando el modo continuo sobre una carpeta de prueba,
agregando líneas al archivo del día de un sitio mientras el proceso corre, y verificando que
dentro de un intervalo de sondeo esas líneas —y solo esas— aparecen en la tabla.

**Acceptance Scenarios**:

1. **Given** la herramienta corriendo en modo continuo, **When** se agregan tres líneas nuevas al
   archivo del día de `W3SVC1`, **Then** en el siguiente ciclo de sondeo aparecen exactamente esas
   tres filas nuevas en la tabla, atribuidas a `W3SVC1`.
2. **Given** la herramienta corriendo en modo continuo, **When** ningún archivo cambia durante un
   ciclo, **Then** no se escribe ningún registro nuevo.
3. **Given** la herramienta corriendo en modo continuo, **When** un sitio genera su primer log del
   día en un archivo que antes no existía, **Then** ese archivo se detecta y se ingesta sin
   necesidad de reiniciar el proceso.
4. **Given** la herramienta corriendo en modo continuo, **When** cambia el día y el servidor
   empieza a escribir en un archivo nuevo, **Then** el seguimiento pasa al archivo del día nuevo
   sin perder las últimas líneas del archivo del día anterior.
5. **Given** la herramienta corriendo en modo continuo, **When** el operador la interrumpe,
   **Then** el proceso termina de forma ordenada, sin dejar registros a medio escribir y sin
   perder la marca de hasta dónde había leído cada archivo.
6. **Given** un almacén vacío y un archivo del día que ya acumula 500 peticiones, **When** se
   arranca el modo continuo, **Then** esas 500 peticiones quedan ingestadas antes del primer ciclo
   de sondeo, y los archivos de días anteriores permanecen sin ingestar.
7. **Given** la herramienta detenida durante dos horas con registros ingestados hasta ese punto,
   **When** se la vuelve a arrancar, **Then** ingesta únicamente las líneas escritas durante la
   detención, sin duplicar ninguna de las anteriores.

---

### User Story 3 - Ejecución por defecto sin parámetros (Priority: P3)

Un operador que solo quiere "dejarlo andando" ejecuta `IISLogParser` sin ningún argumento. La
herramienta arranca en modo continuo con un intervalo de sondeo de 10 segundos, informando en
pantalla la configuración efectiva con la que quedó corriendo.

**Why this priority**: Es una comodidad de uso sobre capacidades que ya existen en P1 y P2. Vale
por sí sola porque define el comportamiento del caso más frecuente, pero no aporta capacidad
nueva.

**Independent Test**: Se puede probar ejecutando el binario sin argumentos y verificando que la
configuración efectiva reportada es modo continuo e intervalo de 10 segundos.

**Acceptance Scenarios**:

1. **Given** ningún argumento en la línea de comandos, **When** se ejecuta la herramienta,
   **Then** arranca en modo continuo con un intervalo de sondeo de 10 segundos y lo informa.
2. **Given** un intervalo de sondeo explícito de 30 segundos, **When** se ejecuta la herramienta,
   **Then** el intervalo efectivo es 30 segundos y no el valor por defecto.
3. **Given** un intervalo de sondeo inválido (cero, negativo o no numérico), **When** se ejecuta la
   herramienta, **Then** el proceso no arranca, informa el valor inválido y termina con un código
   de salida de error.

---

### Edge Cases

- **Archivo abierto por el servidor web**: el archivo del día está siendo escrito mientras se lee.
  La lectura debe convivir con esa escritura sin bloquear al servidor ni fallar.
- **Última línea incompleta**: un archivo termina en una línea a medio escribir. Esa línea no se
  ingesta hasta estar completa, y no se pierde cuando se completa.
- **Archivo truncado o reemplazado**: el tamaño de un archivo disminuye respecto de la última
  lectura. Se eliminan los registros ya ingestados de ese archivo, se reinicia su marca de
  progreso y se vuelve a leer desde el principio (FR-011a).
- **Cambio de formato dentro del mismo archivo**: aparece una nueva declaración de columnas a mitad
  de archivo. Las líneas posteriores se interpretan con el nuevo conjunto de columnas.
- **Columnas desconocidas**: un sitio registra un campo que no existe en el esquema fijo. El
  registro se ingesta igual y el campo desconocido se preserva en la columna de extras, sin
  alterar el esquema de la tabla (FR-009a).
- **Carpetas ajenas**: la carpeta de logs contiene subcarpetas que no corresponden a sitios. Se
  ignoran y se reportan como omitidas.
- **Carpeta raíz de logs inexistente o sin permisos de lectura**: el proceso no arranca, informa la
  causa y termina con código de error.
- **Archivo o carpeta de sitio ilegible durante la corrida**: se omite, se reporta con su causa y
  el procesamiento continúa. En snapshot, la corrida termina con código distinto de cero; en
  continuo, se reintenta en el ciclo siguiente (FR-024 a FR-026).
- **Segunda instancia**: dos instancias apuntan al mismo almacén de datos. La segunda no debe
  corromper los datos ni duplicar registros.
- **Ciclo más lento que el intervalo**: procesar un ciclo tarda más que el *Poll Interval*. Los
  ciclos no se solapan ni se acumulan indefinidamente.
- **Almacén no escribible o disco lleno**: el error se informa con claridad y no se pierde la marca
  de progreso ya confirmada.

## Requirements *(mandatory)*

### Functional Requirements

**Descubrimiento y parseo**

- **FR-001**: El sistema MUST recorrer la carpeta raíz de logs de IIS y descubrir cada subcarpeta
  de sitio, tomando el nombre de la carpeta (`W3SVC1`, `W3SVC2`, ...) como identificador del
  sitio.
- **FR-002**: El sistema MUST permitir indicar la carpeta raíz de logs por parámetro, y MUST usar
  la ubicación estándar de IIS cuando no se indique ninguna.
- **FR-003**: El sistema MUST parsear los archivos de log en formato extendido W3C, interpretando
  las columnas de cada archivo según la declaración de campos presente en su cabecera.
- **FR-004**: El sistema MUST ignorar las líneas de comentario y cabecera del archivo, y no
  registrarlas como peticiones.
- **FR-005**: El sistema MUST descartar las líneas que no puedan interpretarse, contarlas y
  reportarlas al finalizar el ciclo o la ejecución, sin interrumpir el procesamiento del resto del
  archivo. El reporte de una línea rechazada MUST identificar su sitio, archivo y posición, y MUST
  incluir su contenido tal como aparece en el archivo, sin enmascarado ni recorte.
- **FR-006**: El sistema MUST leer los archivos sin impedir que el servidor web siga escribiendo en
  ellos.

**Persistencia**

- **FR-007**: El sistema MUST almacenar los registros de todos los sitios en una única tabla del
  almacén de datos.
- **FR-008**: Cada registro almacenado MUST incluir el identificador del sitio del que proviene.
- **FR-009**: Cada registro almacenado MUST incluir la referencia al archivo de origen y la marca
  temporal de la petición.
- **FR-009a**: La tabla de registros MUST tener un esquema fijo, con una columna tipada por cada
  campo habitual de IIS. Los campos que un archivo declare y que no correspondan a ninguna de esas
  columnas MUST preservarse en una columna adicional, como pares campo-valor asociados a esa misma
  fila. Ningún dato presente en una línea válida puede descartarse.
- **FR-009b**: Los campos del esquema fijo que un sitio no registre MUST quedar vacíos en la fila,
  sin impedir la ingesta del resto del registro.
- **FR-010**: El sistema MUST crear el almacén de datos y su estructura de tablas en el primer
  arranque si aún no existen, sin intervención manual.
- **FR-011**: El sistema MUST ser idempotente: reprocesar un archivo ya ingestado, total o
  parcialmente, MUST NOT producir registros duplicados. La identidad de un registro MUST estar
  dada por la combinación `(sitio, archivo de origen, posición de la línea dentro del archivo)`;
  un intento de ingestar una línea cuya identidad ya existe MUST descartarse silenciosamente y no
  contarse como registro nuevo.
- **FR-011a**: Cuando el sistema detecta que un archivo fue truncado o reemplazado, MUST eliminar
  todos los registros ya ingestados de ese archivo antes de reingestarlo desde el principio, de
  modo que la tabla refleje el contenido actual del archivo sin duplicados ni restos del
  contenido anterior.
- **FR-012**: El sistema MUST registrar de forma persistente hasta qué punto leyó cada archivo, de
  modo que un reinicio retome desde ahí y no desde el principio.

**Modos de ejecución**

- **FR-013**: El sistema MUST ofrecer un modo *snapshot* que procese todos los archivos encontrados
  en todos los sitios y luego finalice.
- **FR-014**: El sistema MUST ofrecer un modo *continuous* que permanezca en ejecución vigilando
  los archivos de log correspondientes al día en curso de cada sitio.
- **FR-014a**: Al arrancar en modo *continuous*, el sistema MUST ingestar el archivo del día de
  cada sitio desde el principio si no existe marca de progreso para ese archivo, y desde la marca
  registrada si ya existe. MUST NOT ingestar los archivos de días anteriores: ese es el trabajo
  del modo *snapshot*.
- **FR-015**: En modo *continuous*, el sistema MUST revisar cada *Poll Interval* segundos si los
  archivos vigilados crecieron, e ingestar únicamente las líneas nuevas.
- **FR-016**: En modo *continuous*, el sistema MUST detectar archivos del día que aparecen después
  del arranque, y MUST pasar a vigilar los archivos del día siguiente cuando cambia la fecha, sin
  requerir reinicio.
- **FR-017**: El sistema MUST terminar de forma ordenada ante una interrupción del operador,
  confirmando lo ya ingestado y su marca de progreso.

**Interfaz de línea de comandos**

- **FR-018**: Ejecutado sin argumentos, el sistema MUST arrancar en modo *continuous* con un *Poll
  Interval* de 10 segundos.
- **FR-019**: El sistema MUST permitir elegir el modo y el valor del *Poll Interval* por argumento
  de línea de comandos.
- **FR-020**: El sistema MUST rechazar argumentos inválidos —modo desconocido, intervalo no
  positivo o no numérico, carpeta inexistente— informando la causa y terminando con un código de
  salida distinto de cero, sin escribir nada en el almacén de datos.
- **FR-021**: El sistema MUST informar al arrancar la configuración efectiva: modo, carpeta de
  logs, almacén de destino e intervalo de sondeo.
- **FR-022**: El sistema MUST reportar el resultado de cada ciclo o ejecución: archivos procesados,
  registros nuevos ingestados, líneas rechazadas y archivos o carpetas omitidos.
- **FR-023**: El sistema MUST devolver código de salida cero cuando el modo *snapshot* completa su
  trabajo sin omisiones, y distinto de cero cuando termina por un error que impidió la ingesta o
  cuando omitió al menos un archivo o carpeta de sitio.

**Tolerancia a fallos parciales**

- **FR-024**: Cuando un archivo o una carpeta de sitio resulta ilegible durante la corrida, el
  sistema MUST omitirlo y continuar procesando el resto. MUST NOT abortar la ejecución completa
  por un fallo localizado.
- **FR-025**: El sistema MUST reportar cada omisión de forma explícita, identificando el archivo o
  la carpeta y la causa, e incluir el total de omisiones en el reporte del ciclo o de la
  ejecución.
- **FR-026**: En modo *continuous*, un archivo o carpeta omitido MUST reintentarse en el ciclo de
  sondeo siguiente, sin intervención manual y sin detener el proceso.

### Source of Truth & Human Review

N/A — esta feature no invoca ningún modelo. Los archivos de log de IIS son la única fuente de
datos, y el sistema los transcribe sin ninguna interpretación derivada de un modelo.

### Key Entities

- **Sitio**: unidad de agrupación que corresponde a una carpeta de logs de IIS. Se identifica por
  el nombre de la carpeta (`W3SVC1`, `W3SVC2`, ...). Su identificador viaja en cada registro.
- **Archivo de log**: archivo de texto perteneciente a un sitio y a una fecha. Sus atributos
  relevantes para el sistema son su ruta, su tamaño y la posición hasta la que ya fue leído.
- **Registro de petición**: una línea de petición HTTP parseada; es la fila de la tabla única. Se
  identifica de forma única por `(sitio, archivo de origen, posición de la línea dentro del
  archivo)`. Atributos: identificador del sitio, archivo de origen, posición dentro del archivo,
  marca temporal, y los campos de la petición declarados por el log (IP de cliente, método, recurso, cadena de consulta, puerto,
  usuario, código de estado y subestado, bytes enviados y recibidos, tiempo de respuesta, agente
  de usuario, referer, entre otros), más una bolsa de campos extras para lo que el archivo declare
  y el esquema fijo no contemple.
- **Marca de progreso**: por cada archivo de log, el punto hasta el cual el sistema ya ingestó
  contenido. Es lo que hace posible la ingesta incremental y la idempotencia.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Un operador obtiene los logs de todos los sitios de un servidor consultables en una
  sola tabla, con una única ejecución y sin pasos manuales previos.
- **SC-002**: La ingesta inicial procesa al menos 50.000 registros por minuto en hardware de
  servidor estándar.
- **SC-003**: En modo continuo, una petición registrada por el servidor queda disponible para
  consulta dentro de los dos intervalos de sondeo posteriores a su escritura en el log.
- **SC-004**: Ejecutar la ingesta dos veces sobre el mismo conjunto de archivos produce exactamente
  la misma cantidad de registros que ejecutarla una vez.
- **SC-005**: El 100% de las líneas de petición válidas de un archivo de referencia quedan
  ingestadas, y el 100% de las líneas descartadas quedan contabilizadas en el reporte.
- **SC-006**: El proceso en modo continuo sostiene 24 horas de ejecución ininterrumpida sin
  degradación de memoria y sin pérdida de registros.
- **SC-007**: Un único archivo ilegible no reduce en más de ese archivo el volumen ingestado: el
  100% de los archivos legibles restantes se procesa igual, y la corrida queda señalada como
  incompleta.
- **SC-008**: Un operador que ejecuta la herramienta por primera vez, sin leer documentación,
  obtiene datos ingestándose correctamente ejecutándola sin argumentos.

## Assumptions

- **Restricción declarada por el usuario**: el almacén de datos es una base de datos SQLite, con
  una tabla única para los registros de todos los sitios. Esta decisión viene dada en el pedido y
  no se re-evalúa en esta especificación.
- **Nombre del ejecutable**: `IISLogParser`, según lo indicado en el pedido.
- **Carpeta de logs por defecto**: la ubicación estándar de IIS (`C:\inetpub\logs\LogFiles`),
  sobreescribible por parámetro.
- **Ubicación del almacén**: un archivo de base de datos en el directorio de trabajo, con nombre
  por defecto y sobreescribible por parámetro.
- **Formato de los logs**: formato extendido W3C, el predeterminado de IIS. Otros formatos (NCSA,
  IIS nativo, binario centralizado) quedan fuera de alcance de esta versión.
- **Rotación diaria**: se asume la rotación diaria de IIS (un archivo por sitio y por día) para
  determinar cuál es "el log de hoy" en modo continuo.
- **Definición de "cambió"**: un archivo cambió cuando su tamaño creció respecto de la marca de
  progreso registrada; una disminución de tamaño se interpreta como archivo reemplazado.
- **Esquema de campos**: el esquema de la tabla es fijo y cubre el conjunto de campos habituales de
  IIS; los campos que un sitio no registre quedan vacíos, y los no contemplados se preservan como
  pares campo-valor en una columna de extras (FR-009a, FR-009b). El esquema no se modifica en
  caliente.
- **Clasificación de PII (decisión del propietario del proyecto)**: ningún campo de los logs de
  IIS —incluidas la IP de cliente y el nombre de usuario— se clasifica como dato personal a
  efectos de esta feature. Se ingestan y se reportan tal como aparecen, sin enmascarado. La
  restricción de PII de la constitución se cumple de forma trivial porque no hay PII declarada en
  el conjunto de datos.
- **Zona horaria**: las marcas temporales se conservan tal como IIS las escribe (UTC), sin
  conversión.
- **Ejecución local**: la herramienta corre en el mismo servidor que aloja los logs, con permisos
  de lectura sobre la carpeta de logs. La ingesta remota queda fuera de alcance.
- **Sin retención ni purga**: esta versión solo ingesta. Borrado, archivado o rotación de los datos
  ya ingestados quedan fuera de alcance.
- **Sin consulta ni reportes**: la herramienta deja los datos disponibles en la tabla; consultarlos
  es tarea de otras herramientas.
- **Una instancia por almacén**: se asume una única instancia escribiendo sobre el mismo archivo de
  base de datos a la vez.
