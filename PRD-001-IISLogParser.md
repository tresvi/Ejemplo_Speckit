# PRD-001: IISLogParser — herramienta de consola que consolida los logs de IIS de un servidor en una base SQLite consultable

## Contexto y Problema

IIS escribe sus logs como archivos de texto plano en formato W3C extendido, separados en una
subcarpeta por sitio (`W3SVC1`, `W3SVC2`, …) y rotados —por defecto— una vez por día. Un
servidor con varios sitios y algunos meses de operación acumula cientos de archivos.

Responder preguntas operativas básicas sobre ese material hoy implica trabajo manual: abrir
archivos, hacer `grep` o `findstr`, o importar a Excel. Preguntas como "cuántos 500 devolvió el
sitio 2 ayer entre las 14 y las 16", "qué URL concentra los requests más lentos" o "de qué IP
vino el pico de las 03:00" no tienen respuesta rápida, y correlacionar entre sitios es
directamente inviable porque cada sitio vive en su propia carpeta.

El problema se agrava cuando el incidente está ocurriendo: para ver la actividad reciente hay
que abrir el archivo del día y buscar el final a mano, una y otra vez.

**Personas**

- **Administrador de IIS / SysAdmin.** Opera el servidor Windows, tiene privilegios de
  administrador local. Necesita diagnosticar incidentes en curso o recientes (picos de 5xx,
  requests lentos, tráfico anómalo) sobre uno o varios sitios, sin instalar ni mantener una
  plataforma de observabilidad completa.
- **Desarrollador / analista.** Trabaja sobre el mismo servidor. Necesita consultar el
  histórico con SQL para análisis puntuales (tráfico por endpoint, distribución de códigos de
  estado, user agents, latencias) sin depender de que el sysadmin le extraiga archivos.

## Objetivos

- **O-1.** Convertir los logs de IIS de un servidor en una base SQLite consultable con SQL
  estándar, sin pasos manuales de preparación.
- **O-2.** Permitir observar la actividad reciente de forma continua, sin abrir archivos de log.
- **O-3.** Poder ejecutar la herramienta repetidamente sobre el mismo servidor sin duplicar
  registros ni reprocesar lo ya leído.
- **O-4.** Operar sin infraestructura ni instalación: un ejecutable de consola y un archivo `.db`.

## Requerimientos Funcionales

### Descubrimiento y parseo

- **RF-01.** El sistema debe recorrer la carpeta raíz de logs de IIS y descubrir las subcarpetas
  de sitio con formato `W3SVC<N>`.
- **RF-02.** El sistema debe determinar el conjunto y el orden de las columnas de cada archivo
  leyendo su directiva `#Fields`.
- **RF-03.** El sistema debe reinterpretar el conjunto de columnas cuando aparece una nueva
  directiva `#Fields` en medio de un archivo ya iniciado.
- **RF-04.** El sistema debe excluir de la inserción toda línea que comience con `#`.
- **RF-05.** El sistema debe almacenar cada entrada de log en una única tabla común a todos los
  sitios, con un campo que identifique el sitio de origen (`W3SVC1`, `W3SVC2`, …).
- **RF-06.** El sistema debe resolver el nombre configurado del sitio en IIS a partir del ID de
  la carpeta y almacenarlo junto al identificador.
- **RF-07.** El sistema debe unificar los campos `date` y `time` del log en un único timestamp
  UTC en formato ISO-8601.
- **RF-08.** El sistema debe normalizar a `NULL` todo campo cuyo valor en el log sea `-`.
- **RF-09.** El sistema debe almacenar en una columna JSON los campos declarados en `#Fields`
  que no tengan columna propia en el esquema.

### Lectura incremental

- **RF-10.** El sistema debe abrir cada archivo de log en modo de lectura compartida, sin
  impedir que IIS mantenga su handle de escritura sobre él.
- **RF-11.** El sistema debe registrar, por cada archivo procesado, el offset en bytes hasta la
  última línea completa leída.
- **RF-12.** El sistema debe descartar la línea final incompleta de un archivo y no avanzar el
  offset más allá del último salto de línea.
- **RF-13.** El sistema debe reanudar la lectura de cada archivo desde su offset almacenado.
- **RF-14.** El sistema debe reprocesar un archivo desde el offset cero cuando su tamaño actual
  es menor que el offset almacenado.

### Modos de ejecución

- **RF-15.** El sistema debe ejecutar el modo `snapshot` procesando todos los archivos
  pendientes y finalizando.
- **RF-16.** El sistema debe ejecutar el modo `continuous` realizando primero una pasada
  completa de backfill y entrando luego en un ciclo de polling.
- **RF-17.** El sistema debe re-escanear la carpeta raíz en cada ciclo de polling para detectar
  archivos y sitios aparecidos después del arranque.
- **RF-18.** El sistema debe identificar los archivos con contenido nuevo comparando su tamaño
  actual contra el último tamaño observado.
- **RF-19.** El sistema debe restringir la vigilancia a los archivos cuya fecha de última
  escritura esté dentro de las últimas 48 horas.
- **RF-20.** El sistema debe finalizar de forma ordenada ante una señal de interrupción
  (Ctrl+C), confirmando el lote en curso y sus offsets antes de salir.

### Interfaz de línea de comandos

- **RF-21.** El sistema debe ejecutarse en modo `continuous` con un poll interval de 10 segundos
  cuando se lo invoca sin parámetros.
- **RF-22.** El sistema debe aceptar un parámetro `--poll-interval` que defina los segundos
  entre ciclos de polling.
- **RF-23.** El sistema debe aceptar un parámetro `--sites` que restrinja el procesamiento a una
  lista de sitios indicada.
- **RF-24.** El sistema debe aceptar un parámetro `--retention-days` que elimine de la base los
  registros cuyo timestamp sea anterior a N días.
- **RF-25.** El sistema debe informar al inicio de la ejecución el modo activo, la carpeta de
  logs, la ruta de la base de datos y la forma de detener el proceso.

### Manejo de condiciones de error

- **RF-26.** El sistema debe informar la falta de permisos de lectura sobre la carpeta de logs y
  finalizar con código de salida distinto de cero.
- **RF-27.** El sistema debe detectar que el servidor está configurado con logging centralizado
  W3C, informarlo y finalizar sin procesar.

## Requerimientos No Funcionales

- **RNF-01.** El sistema debe insertar los registros en lotes de al menos 1.000 filas dentro de
  una única transacción SQLite.
- **RNF-02.** El sistema debe procesar al menos 20.000 líneas de log por segundo en modo
  snapshot sobre disco SSD local.
- **RNF-03.** La base debe operar con `journal_mode=WAL`, permitiendo consultas de lectura
  concurrentes mientras el proceso escribe.
- **RNF-04.** El proceso no debe superar los 200 MB de memoria residente con independencia del
  tamaño de los archivos procesados.
- **RNF-05.** Un ciclo de polling sin cambios no debe superar los 500 ms con 100 archivos bajo
  vigilancia.
- **RNF-06.** El sistema debe distribuirse como un único ejecutable autocontenido, sin requerir
  la instalación previa de un runtime.
- **RNF-07.** El sistema debe procesar archivos de log codificados en UTF-8 y en ANSI.
- **RNF-08.** Una consulta filtrada por sitio y rango de fechas debe responder en menos de 1
  segundo sobre una tabla de 10 millones de filas.

## Criterios de Aceptación

- **AC-01 (RF-01).** Dado un directorio raíz que contiene las subcarpetas `W3SVC1`, `W3SVC2` y
  `Otros`, cuando se ejecuta el modo snapshot, entonces el sistema procesa archivos de `W3SVC1`
  y `W3SVC2` y no procesa ninguno de `Otros`.
- **AC-02 (RF-02).** Dado un archivo cuyo `#Fields` declara las columnas en orden
  `date time c-ip cs-method sc-status`, cuando se lo parsea, entonces el valor de `c-ip` de cada
  fila insertada coincide con el tercer token de su línea de origen.
- **AC-03 (RF-03).** Dado un archivo con 10 líneas bajo un `#Fields` de 5 columnas seguidas de
  un nuevo `#Fields` de 7 columnas y 10 líneas más, cuando se lo parsea, entonces se insertan 20
  filas y las 10 últimas tienen poblados los 2 campos adicionales.
- **AC-04 (RF-04).** Dado un archivo con 4 líneas de directiva (`#Software`, `#Version`,
  `#Date`, `#Fields`) y 3 líneas de datos, cuando se lo parsea, entonces la tabla contiene
  exactamente 3 filas.
- **AC-05 (RF-05).** Dados archivos en `W3SVC1` y `W3SVC2`, cuando se ejecuta el modo snapshot,
  entonces todas las filas quedan en la misma tabla y `SELECT DISTINCT site_id` devuelve
  exactamente `W3SVC1` y `W3SVC2`.
- **AC-06 (RF-06).** Dado un IIS donde el sitio de ID 1 se llama `Default Web Site`, cuando se
  procesan los logs de `W3SVC1`, entonces las filas resultantes tienen `site_name` igual a
  `Default Web Site`.
- **AC-07 (RF-07).** Dada una línea con `date` = `2026-09-01` y `time` = `13:45:01`, cuando se
  la inserta, entonces su campo `ts_utc` vale exactamente `2026-09-01T13:45:01Z`.
- **AC-08 (RF-08).** Dada una línea cuyo campo `cs-username` vale `-`, cuando se la inserta,
  entonces la columna `cs_username` de esa fila es `NULL` y no la cadena `-`.
- **AC-09 (RF-09).** Dado un archivo cuyo `#Fields` incluye `x-forwarded-for`, campo sin columna
  propia en el esquema, cuando se lo parsea, entonces la columna `extra` de cada fila contiene
  un JSON con la clave `x-forwarded-for` y su valor de la línea.
- **AC-10 (RF-10).** Dado un archivo abierto por otro proceso con un handle de escritura activo,
  cuando el sistema lo procesa, entonces la lectura se completa sin error y el otro proceso
  puede seguir escribiendo en él.
- **AC-11 (RF-11).** Dado un archivo de 5.000 bytes terminado en salto de línea, cuando termina
  su procesamiento, entonces el offset almacenado para ese archivo es 5.000.
- **AC-12 (RF-12).** Dado un archivo cuyos últimos 40 bytes son una línea sin salto de línea
  final, cuando se lo procesa, entonces esa línea no genera ninguna fila y el offset almacenado
  es igual al tamaño del archivo menos 40.
- **AC-13 (RF-13).** Dado un archivo ya procesado por completo, cuando se ejecuta el modo
  snapshot por segunda vez sin que el archivo haya cambiado, entonces la cantidad de filas en la
  tabla es idéntica a la de la primera ejecución.
- **AC-14 (RF-14).** Dado un archivo con offset almacenado de 5.000 bytes que es reemplazado por
  otro de 800 bytes con el mismo nombre, cuando se lo procesa, entonces el sistema inserta las
  filas correspondientes a los 800 bytes completos y deja el offset en 800.
- **AC-15 (RF-15).** Dado un conjunto de archivos pendientes, cuando se ejecuta con
  `--mode snapshot`, entonces el proceso finaliza con código de salida 0 y no queda en ejecución.
- **AC-16 (RF-16).** Dada una base vacía y archivos históricos de 3 días, cuando se ejecuta con
  `--mode continuous`, entonces antes del primer ciclo de polling la tabla ya contiene las filas
  de los 3 días y el proceso continúa en ejecución.
- **AC-17 (RF-17).** Dado un proceso en modo continuous en ejecución, cuando se crea una carpeta
  `W3SVC9` con un archivo de log, entonces dentro de los 2 ciclos de polling siguientes sus
  filas aparecen en la tabla.
- **AC-18 (RF-18).** Dado un proceso en modo continuous, cuando se agregan 5 líneas completas al
  archivo vigilado, entonces dentro de los 2 ciclos de polling siguientes la tabla contiene 5
  filas nuevas y ninguna repetida.
- **AC-19 (RF-19).** Dado un directorio con un archivo modificado hace 10 horas y otro
  modificado hace 72 horas, cuando corre un ciclo de polling, entonces el sistema consulta el
  tamaño del primero y no el del segundo.
- **AC-20 (RF-20).** Dado un proceso en modo continuous con un lote parcial en memoria, cuando
  se envía Ctrl+C, entonces el proceso finaliza con código 0, las filas del lote están en la
  tabla y el offset almacenado coincide con la última línea insertada.
- **AC-21 (RF-21).** Dado el ejecutable invocado sin ningún argumento, cuando arranca, entonces
  informa modo `continuous` y poll interval `10` segundos, y permanece en ejecución.
- **AC-22 (RF-22).** Dado el ejecutable invocado con `--poll-interval 30`, cuando corre en modo
  continuous, entonces el tiempo transcurrido entre el inicio de dos ciclos consecutivos es de
  30 segundos ± 1 segundo.
- **AC-23 (RF-23).** Dado un directorio con `W3SVC1`, `W3SVC2` y `W3SVC3`, cuando se ejecuta con
  `--sites W3SVC1,W3SVC3`, entonces `SELECT DISTINCT site_id` devuelve exactamente `W3SVC1` y
  `W3SVC3`.
- **AC-24 (RF-24).** Dada una base con registros de los últimos 120 días, cuando se ejecuta con
  `--retention-days 90`, entonces al finalizar no existe ninguna fila con `ts_utc` anterior a 90
  días respecto de la fecha de ejecución.
- **AC-25 (RF-25).** Dado el ejecutable invocado en cualquier modo, cuando arranca, entonces la
  primera salida por consola incluye el modo, la ruta de la carpeta de logs, la ruta del archivo
  de base de datos y la instrucción para detenerlo.
- **AC-26 (RF-26).** Dado un usuario sin permiso de lectura sobre la carpeta de logs, cuando
  ejecuta el programa, entonces la consola muestra un mensaje que nombra la carpeta inaccesible
  y el proceso finaliza con código de salida distinto de 0.
- **AC-27 (RF-27).** Dado un servidor con logging centralizado W3C activo, cuando se ejecuta el
  programa, entonces la consola informa que esa configuración no está soportada, no se inserta
  ninguna fila y el proceso finaliza con código de salida distinto de 0.
- **AC-28 (RNF-01).** Dado el procesamiento de 10.000 líneas, cuando se cuentan las
  transacciones SQLite abiertas, entonces son 10 o menos.
- **AC-29 (RNF-02).** Dado un archivo de 1.000.000 de líneas en disco SSD local, cuando se
  ejecuta el modo snapshot, entonces el procesamiento completo termina en 50 segundos o menos.
- **AC-30 (RNF-03).** Dado un proceso en modo continuous escribiendo en la base, cuando otro
  cliente ejecuta `SELECT COUNT(*)` sobre la tabla de registros, entonces la consulta devuelve
  un resultado sin error de bloqueo.
- **AC-31 (RNF-04).** Dado un archivo de log de 2 GB, cuando se lo procesa en modo snapshot,
  entonces la memoria residente máxima del proceso no supera los 200 MB.
- **AC-32 (RNF-05).** Dados 100 archivos bajo vigilancia sin cambios, cuando se ejecuta un ciclo
  de polling, entonces el ciclo completo tarda 500 ms o menos.
- **AC-33 (RNF-06).** Dada una máquina Windows sin el runtime instalado, cuando se copia y
  ejecuta el binario publicado, entonces el programa arranca sin error de dependencias faltantes.
- **AC-34 (RNF-07).** Dado un archivo de log codificado en UTF-8 y otro en ANSI, ambos con
  caracteres acentuados en `cs-uri-stem`, cuando se los procesa, entonces los valores
  almacenados conservan los acentos en los dos casos.
- **AC-35 (RNF-08).** Dada una tabla con 10.000.000 de filas, cuando se ejecuta una consulta
  filtrada por `site_id` y un rango de `ts_utc` de un día, entonces devuelve el resultado en
  menos de 1 segundo.

## Fuera de Alcance

- **Logging centralizado W3C de IIS** (carpeta única con `s-sitename` como campo). Se detecta y
  se informa, pero no se procesa.
- **Formatos de log distintos de W3C extendido**: NCSA, IIS nativo, ODBC logging.
- **Logs que no sean de sitios web de IIS**: HTTPERR (`System32\LogFiles\HTTPERR`), FTP
  (`MSFTPSVC*`), SMTP.
- **Ejecución fuera del servidor IIS**: procesar copias de logs en otra máquina o carpetas
  arbitrarias sin IIS presente.
- **Interfaz gráfica o web** de consulta o visualización. La consulta se hace con cualquier
  cliente SQLite.
- **Alertas, notificaciones o umbrales** sobre el contenido de los logs.
- **Envío o replicación a plataformas externas** (Elasticsearch, Splunk, Application Insights).
- **Motores de base de datos distintos de SQLite.**
- **Instalación y gestión como servicio de Windows.** El modo continuous corre en primer plano.
- **Autenticación, usuarios o roles dentro de la herramienta.**
- **Enriquecimiento de datos**: geolocalización de IPs, parseo de user agents a
  navegador/SO, resolución DNS inversa.

## Riesgos y Dependencias

### Riesgos

| Riesgo | Mitigación |
|---|---|
| HTTP.sys bufferea la escritura de logs: las líneas no llegan al archivo en el instante del request, sino en ráfagas. Con poll interval de 10 s, la mayoría de los ciclos no verá cambios y después llegarán muchas líneas juntas. | Documentar que el poll interval gobierna la frecuencia de chequeo, no la latencia del dato. No prometer "tiempo real". Usar `netsh http flush logbuffer` en los tests de integración. |
| Volumen: un sitio con tráfico alto genera millones de filas por día; la base puede llegar a decenas de GB. | `--retention-days` (RF-24) y creación de índices después de la carga masiva en modo snapshot. |
| El conjunto de columnas `#Fields` es configurable por sitio y puede cambiar a mitad de archivo. Un parser posicional fijo produciría datos corruptos en silencio. | RF-02 y RF-03: mapeo por nombre, siempre. Fixture de test con `#Fields` cambiante (AC-03). |
| IIS mantiene un handle de escritura sobre el archivo del día; una apertura sin compartir falla o interfiere. | RF-10: apertura con lectura y escritura compartidas. |
| La rotación puede ser diaria, horaria o por tamaño, y la fecha del nombre puede estar en UTC mientras el reloj del servidor está en horario local. Derivar "el archivo de hoy" del nombre es frágil. | RF-18 y RF-19: la vigilancia se basa en tamaño y fecha de modificación, nunca en el nombre del archivo. |
| Un job externo de limpieza puede mover, comprimir o borrar archivos ya registrados, dejando offsets huérfanos. | Riesgo aceptado: los registros ya insertados se conservan; las filas de control de archivos inexistentes quedan inertes. |
| Leer una línea a medio escribir inserta una fila corrupta. | RF-12: solo se procesa hasta el último salto de línea. |
| Otra herramienta con la base abierta puede bloquear la escritura. | RNF-03: modo WAL. |

### Dependencias

- **Privilegios de administrador local** sobre el servidor Windows, para leer
  `C:\inetpub\logs\LogFiles` y la configuración de IIS.
- **IIS configurado con formato de log W3C extendido** y logging por sitio (no centralizado).
- **Acceso a la configuración de IIS** (`applicationHost.config` o su API de administración)
  para resolver el nombre del sitio exigido por RF-06.
- **Runtime y stack de implementación**: a definir.
