using System.Text;
using IISLogParser.Parsing;
using Microsoft.Data.Sqlite;

namespace IISLogParser.Storage;

/// <summary>
/// Creación idempotente del esquema (contracts/database-schema.md). Se ejecuta en
/// cada arranque: toda sentencia lleva <c>IF NOT EXISTS</c> (FR-010).
/// </summary>
public static class SchemaInitializer
{
    public const int SchemaVersion = 1;

    /// <summary>Columnas del esquema fijo declaradas como INTEGER en el DDL.</summary>
    private static readonly HashSet<string> IntegerColumns =
    [
        "s_port",
        "sc_status",
        "sc_substatus",
        "sc_win32_status",
        "sc_bytes",
        "cs_bytes",
        "time_taken",
    ];

    public static void Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, "PRAGMA synchronous = NORMAL;");
        Execute(connection, BuildLogEntriesDdl());
        Execute(
            connection,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_log_entries_identity
                ON log_entries (site_id, source_file, line_offset);
            """);
        Execute(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_log_entries_site_time
                ON log_entries (site_id, timestamp_utc);
            """);
        Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS file_progress (
                source_file      TEXT    PRIMARY KEY,
                site_id          TEXT    NOT NULL,
                bytes_ingested   INTEGER NOT NULL,
                last_seen_size   INTEGER NOT NULL,
                updated_at_utc   TEXT    NOT NULL
            );
            """);
        Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version          INTEGER NOT NULL
            );
            """);

        SeedSchemaVersion(connection);
    }

    /// <summary>
    /// El DDL de <c>log_entries</c> se deriva de <see cref="LogColumns.Names"/> para que
    /// agregar una columna al esquema fijo sea un solo cambio y no dos que pueden
    /// desincronizarse.
    /// </summary>
    private static string BuildLogEntriesDdl()
    {
        var ddl = new StringBuilder();
        ddl.AppendLine("CREATE TABLE IF NOT EXISTS log_entries (");
        ddl.AppendLine("    id               INTEGER PRIMARY KEY AUTOINCREMENT,");
        ddl.AppendLine("    site_id          TEXT    NOT NULL,");
        ddl.AppendLine("    source_file      TEXT    NOT NULL,");
        ddl.AppendLine("    line_offset      INTEGER NOT NULL,");

        foreach (var column in LogColumns.Names)
        {
            var type = IntegerColumns.Contains(column) ? "INTEGER" : "TEXT";
            ddl.Append("    ").Append(column).Append(' ').Append(type).AppendLine(",");
        }

        ddl.AppendLine("    extra_fields     TEXT,");
        ddl.AppendLine("    ingested_at_utc  TEXT    NOT NULL");
        ddl.AppendLine(");");

        return ddl.ToString();
    }

    private static void SeedSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_version;";
        var count = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        if (count > 0)
        {
            return;
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO schema_version (version) VALUES ($version);";
        insert.Parameters.AddWithValue("$version", SchemaVersion);
        insert.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
