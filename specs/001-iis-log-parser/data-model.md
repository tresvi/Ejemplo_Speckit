# Phase 1 — Data Model: IISLogParser

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Fecha**: 2026-09-03

Modelo derivado de la sección *Key Entities* de la spec y de las decisiones D-006, D-007 y D-012
de [research.md](./research.md). El esquema SQL literal es el contrato en
[contracts/database-schema.md](./contracts/database-schema.md); acá van las entidades, sus reglas
de validación y sus transiciones de estado.

---

## Entidad: Registro de petición (`log_entries`)

La tabla única donde conviven los registros de todos los sitios. Una fila = una línea de petición
válida de un archivo de log.

### Identidad

| Componente | Origen | Nota |
|---|---|---|
| `site_id` | Nombre de la carpeta del sitio | `W3SVC1`, `W3SVC2`, … |
| `source_file` | Ruta del archivo **relativa a la raíz de logs** | `W3SVC1/u_ex260903.log` |
| `line_offset` | Offset en bytes del **primer byte de la línea** | Entero ≥ 0 |

La terna es un índice **único**. Toda inserción usa `INSERT OR IGNORE`: reinsertar una identidad
existente es una operación sin efecto y no cuenta como registro nuevo (FR-011).

`source_file` se normaliza a separador `/` y minúsculas del nombre de archivo tal como está en
disco, para que la identidad sea estable entre corridas y entre máquinas.

### Campos del esquema fijo

Mapeados desde los nombres de campo W3C declarados en la directiva `#Fields:`.

| Columna | Campo W3C | Tipo | Nulo | Regla |
|---|---|---|---|---|
| `timestamp_utc` | `date` + `time` | TEXT | Sí | ISO-8601 `YYYY-MM-DDTHH:MM:SS`. Nulo si el archivo no registra fecha u hora. |
| `s_ip` | `s-ip` | TEXT | Sí | |
| `cs_method` | `cs-method` | TEXT | Sí | |
| `cs_uri_stem` | `cs-uri-stem` | TEXT | Sí | |
| `cs_uri_query` | `cs-uri-query` | TEXT | Sí | |
| `s_port` | `s-port` | INTEGER | Sí | Entero; si no parsea, va a `extra_fields` y se cuenta el registro igual. |
| `cs_username` | `cs-username` | TEXT | Sí | |
| `c_ip` | `c-ip` | TEXT | Sí | |
| `cs_version` | `cs-version` | TEXT | Sí | |
| `cs_user_agent` | `cs(User-Agent)` | TEXT | Sí | El parser normaliza la forma `cs(X)`. |
| `cs_referer` | `cs(Referer)` | TEXT | Sí | |
| `cs_cookie` | `cs(Cookie)` | TEXT | Sí | |
| `cs_host` | `cs-host` | TEXT | Sí | |
| `sc_status` | `sc-status` | INTEGER | Sí | |
| `sc_substatus` | `sc-substatus` | INTEGER | Sí | |
| `sc_win32_status` | `sc-win32-status` | INTEGER | Sí | |
| `sc_bytes` | `sc-bytes` | INTEGER | Sí | |
| `cs_bytes` | `cs-bytes` | INTEGER | Sí | |
| `time_taken` | `time-taken` | INTEGER | Sí | Milisegundos, tal como IIS lo escribe. |
| `s_sitename` | `s-sitename` | TEXT | Sí | Campo del log; **no** sustituye a `site_id`, que viene de la carpeta. |
| `s_computername` | `s-computername` | TEXT | Sí | |

Todas las columnas del esquema fijo admiten nulo: un sitio puede no registrar un campo, y eso no
debe impedir la ingesta del resto del registro (FR-009b).

### Campos extra

| Columna | Tipo | Nulo | Regla |
|---|---|---|---|
| `extra_fields` | TEXT | Sí | Objeto JSON con los pares `campo: valor` que el archivo declaró y el esquema fijo no contempla (FR-009a). Nulo cuando no hay ninguno. |

Ningún dato de una línea válida se descarta: lo que no encuentra columna, va acá.

### Metadatos

| Columna | Tipo | Nulo | Regla |
|---|---|---|---|
| `id` | INTEGER | No | Clave primaria autoincremental. Sin significado de negocio. |
| `ingested_at_utc` | TEXT | No | Instante de ingesta, ISO-8601 UTC. Distinto de `timestamp_utc`, que es cuándo ocurrió la petición. |

### Reglas de validación

- **V-01**: una línea sin `#Fields:` previo en el archivo no puede interpretarse → se rechaza y se
  cuenta (FR-005). No se adivina el esquema.
- **V-02**: una línea con menos valores que columnas declaradas → se rechaza y se cuenta.
- **V-03**: una línea con más valores que columnas declaradas → se rechaza y se cuenta. El formato
  W3C no admite valores sin campo.
- **V-04**: el valor `-` significa campo vacío → se persiste como nulo, no como el literal `-`.
- **V-05**: un valor que no convierte al tipo de su columna numérica no invalida el registro: la
  columna queda nula y el par campo/valor original se preserva en `extra_fields`. Se prefiere
  ingestar con una anomalía preservada antes que perder la fila entera.
- **V-06**: las líneas que empiezan con `#` son directivas o comentarios y nunca se ingestan
  (FR-004).

---

## Entidad: Marca de progreso (`file_progress`)

Una fila por archivo de log conocido. Es lo que hace posible la ingesta incremental, la
reanudación tras un reinicio y la detección de reemplazo.

| Columna | Tipo | Nulo | Regla |
|---|---|---|---|
| `source_file` | TEXT | No | Clave primaria. Misma normalización que en `log_entries`. |
| `site_id` | TEXT | No | Redundante con la ruta, pero permite consultar progreso por sitio sin parsear rutas. |
| `bytes_ingested` | INTEGER | No | Offset del byte siguiente al último salto de línea **confirmado**. Nunca apunta a media línea (D-004). |
| `last_seen_size` | INTEGER | No | Tamaño del archivo en la última pasada. Base de la detección de cambio y de truncamiento. |
| `updated_at_utc` | TEXT | No | ISO-8601 UTC. |

### Transiciones de estado

| Estado actual | Evento | Transición |
|---|---|---|
| Sin fila | Archivo descubierto | Se crea con `bytes_ingested = 0`, `last_seen_size = tamaño actual`. Se ingesta desde el principio. |
| `bytes_ingested = B` | Tamaño actual > `B` | Se leen los bytes desde `B`; se ingestan las líneas completas; `bytes_ingested` avanza al último salto de línea confirmado. |
| `bytes_ingested = B` | Tamaño actual = `B` | Sin cambio. No se escribe nada. |
| `bytes_ingested = B` | Tamaño actual < `B` | **Archivo reemplazado o truncado**: se borran todas las filas de `log_entries` con ese `source_file`, `bytes_ingested` vuelve a 0 y se reingesta desde el principio (FR-011a). |
| Cualquiera | Archivo ilegible | Se omite; la fila **no se modifica**. En continuo se reintenta el ciclo siguiente (FR-026). |

**Invariante crítico**: la actualización de `file_progress` y la inserción de las filas de
`log_entries` correspondientes ocurren en la **misma transacción** (D-007). De ahí se siguen las
dos garantías que la spec exige y que ningún otro mecanismo del diseño provee: nunca se pierde un
registro confirmado y nunca se duplica uno.

---

## Entidad: Sitio

No se persiste como tabla propia. Es la carpeta `W3SVC<n>` bajo la raíz de logs, y su identificador
viaja desnormalizado en cada fila de `log_entries` y `file_progress`.

**Regla de reconocimiento**: una subcarpeta es un sitio si su nombre encaja con `^W3SVC\d+$`
(comparación sin distinguir mayúsculas). Cualquier otra subcarpeta se ignora y se reporta como
omitida, sin ser un error.

**Por qué desnormalizado**: la spec pide explícitamente una tabla única con un campo que identifique
el sitio. Una tabla de sitios agregaría un join a toda consulta analítica sin aportar ningún dato
que la carpeta no tenga ya.

---

## Entidad: Mapa de campos (`FieldMap`, en memoria)

No se persiste. Es el estado que el parser mantiene mientras recorre un archivo: la lista ordenada
de columnas declarada por la última directiva `#Fields:` vista.

| Transición | Efecto |
|---|---|
| Inicio de archivo | Mapa vacío. Toda línea de datos previa a un `#Fields:` se rechaza (V-01). |
| Se lee `#Fields: a b c` | El mapa pasa a `[a, b, c]`. |
| Se lee otro `#Fields:` a mitad de archivo | El mapa se reemplaza; las líneas siguientes usan el nuevo (FR-003, D-005). |

En una reanudación desde un offset intermedio, el mapa se reconstruye leyendo las directivas del
archivo desde el principio —solo las líneas `#`, sin ingestar datos— antes de reanudar en el
offset confirmado. Sin esto, reanudar a mitad de archivo dejaría el parser sin mapa y rechazaría
todas las líneas restantes.

---

## Entidad: Reporte de ingesta (`IngestReport`, en memoria)

No se persiste. Acumula los contadores que exige el FR-022 y determina el código de salida
(FR-023).

| Campo | Regla |
|---|---|
| `FilesProcessed` | Archivos efectivamente leídos en el ciclo o la ejecución. |
| `RecordsIngested` | Filas realmente insertadas. Las descartadas por identidad duplicada **no** cuentan. |
| `LinesRejected` | Líneas no interpretables (V-01 a V-03). |
| `SkippedPaths` | Archivos y carpetas omitidos por ilegibilidad, con su causa. |

**Regla de código de salida**: `SkippedPaths` no vacío en modo snapshot ⇒ código distinto de cero,
aunque todo lo demás haya salido bien (FR-023). Un snapshot incompleto no puede reportarse como
exitoso.
