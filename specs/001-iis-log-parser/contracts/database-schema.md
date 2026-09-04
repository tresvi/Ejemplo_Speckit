# Contrato: Esquema de la base de datos

**Feature**: [../spec.md](../spec.md) | **Modelo**: [../data-model.md](../data-model.md)

Esquema SQLite que la herramienta crea y sobre el que escribe. Es un contrato hacia afuera: quien
consulte estos datos depende de estos nombres y tipos. Verificado por
`tests/IISLogParser.Tests/Contract/DatabaseSchemaContractTests.cs`.

---

## Creación

Toda sentencia usa `IF NOT EXISTS`: la inicialización es idempotente y se ejecuta en cada arranque
(FR-010).

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous  = NORMAL;

CREATE TABLE IF NOT EXISTS log_entries (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Identidad
    site_id          TEXT    NOT NULL,
    source_file      TEXT    NOT NULL,
    line_offset      INTEGER NOT NULL,

    -- Momento de la petición
    timestamp_utc    TEXT,

    -- Campos W3C del esquema fijo
    s_ip             TEXT,
    cs_method        TEXT,
    cs_uri_stem      TEXT,
    cs_uri_query     TEXT,
    s_port           INTEGER,
    cs_username      TEXT,
    c_ip             TEXT,
    cs_version       TEXT,
    cs_user_agent    TEXT,
    cs_referer       TEXT,
    cs_cookie        TEXT,
    cs_host          TEXT,
    sc_status        INTEGER,
    sc_substatus     INTEGER,
    sc_win32_status  INTEGER,
    sc_bytes         INTEGER,
    cs_bytes         INTEGER,
    time_taken       INTEGER,
    s_sitename       TEXT,
    s_computername   TEXT,

    -- Lo que el esquema fijo no contempla
    extra_fields     TEXT,

    -- Metadato de ingesta
    ingested_at_utc  TEXT    NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_log_entries_identity
    ON log_entries (site_id, source_file, line_offset);

CREATE INDEX IF NOT EXISTS ix_log_entries_site_time
    ON log_entries (site_id, timestamp_utc);

CREATE TABLE IF NOT EXISTS file_progress (
    source_file      TEXT    PRIMARY KEY,
    site_id          TEXT    NOT NULL,
    bytes_ingested   INTEGER NOT NULL,
    last_seen_size   INTEGER NOT NULL,
    updated_at_utc   TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS schema_version (
    version          INTEGER NOT NULL
);
```

`schema_version` arranca en `1`. Existe para que una versión futura pueda migrar sin adivinar con
qué esquema fue escrita la base.

---

## Operaciones del contrato

### Inserción de registros

```sql
INSERT OR IGNORE INTO log_entries (site_id, source_file, line_offset, timestamp_utc, ...)
VALUES (@site_id, @source_file, @line_offset, @timestamp_utc, ...);
```

`INSERT OR IGNORE` sobre el índice único es el mecanismo de idempotencia (FR-011). El contador de
registros nuevos usa las filas realmente afectadas, no la cantidad de líneas parseadas.

### Avance de la marca de progreso

```sql
INSERT INTO file_progress (source_file, site_id, bytes_ingested, last_seen_size, updated_at_utc)
VALUES (@source_file, @site_id, @bytes, @size, @now)
ON CONFLICT(source_file) DO UPDATE SET
    bytes_ingested = excluded.bytes_ingested,
    last_seen_size = excluded.last_seen_size,
    updated_at_utc = excluded.updated_at_utc;
```

### Reemplazo de archivo (FR-011a)

```sql
DELETE FROM log_entries WHERE source_file = @source_file;
```

Seguido del reinicio de la marca a `bytes_ingested = 0`, **en la misma transacción**.

---

## Invariantes

- **I-01** — *Atomicidad datos/progreso*: toda transacción que inserte filas en `log_entries`
  actualiza `file_progress` para ese archivo, y viceversa. Nunca una sin la otra. Es lo que impide
  perder o duplicar registros ante una caída (D-007).
- **I-02** — *Identidad*: no existen dos filas con la misma terna
  `(site_id, source_file, line_offset)`. Garantizado por el índice único.
- **I-03** — *Progreso en frontera de línea*: `bytes_ingested` siempre apunta al byte siguiente a
  un salto de línea, nunca a media línea (D-004).
- **I-04** — *Consistencia tras reemplazo*: si `bytes_ingested = 0` para un archivo, no existe
  ninguna fila de `log_entries` con ese `source_file`.
- **I-05** — *Rutas relativas*: `source_file` es siempre relativa a la raíz de logs, con separador
  `/`. Nunca una ruta absoluta.

## Compatibilidad

Los nombres de columna siguen los nombres de campo W3C de IIS, sustituyendo `-` y los paréntesis de
`cs(X)` por `_`. Esa correspondencia es deliberada: quien conoce los logs de IIS puede consultar la
tabla sin diccionario.
