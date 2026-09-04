using System.Globalization;
using System.Text;
using System.Text.Json;
using IISLogParser.Parsing;
using Microsoft.Data.Sqlite;

namespace IISLogParser.Storage;

/// <summary>
/// Almacén SQLite. Sin ORM: la carga de trabajo es inserción masiva secuencial más una
/// consulta de progreso, y el control transaccional es el mecanismo central de
/// correctitud, no un detalle a delegar (D-002, D-007).
/// </summary>
public sealed class SqliteLogStore : ILogStore
{
    private readonly SqliteConnection _connection;
    private readonly string _insertSql;

    public SqliteLogStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());

        _connection.Open();
        _insertSql = BuildInsertSql();
    }

    /// <summary>Modo de journal efectivo. Se expone para el test de contrato de D-008.</summary>
    public string JournalMode
    {
        get
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            return (string)command.ExecuteScalar()!;
        }
    }

    public void Initialize() => SchemaInitializer.Apply(_connection);

    public FileProgress? GetProgress(string sourceFile)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_file, site_id, bytes_ingested, last_seen_size
            FROM file_progress
            WHERE source_file = $source_file;
            """;
        command.Parameters.AddWithValue("$source_file", sourceFile);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProgress(reader) : null;
    }

    public IReadOnlyList<FileProgress> GetAllProgress()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_file, site_id, bytes_ingested, last_seen_size
            FROM file_progress;
            """;

        using var reader = command.ExecuteReader();
        var all = new List<FileProgress>();
        while (reader.Read())
        {
            all.Add(ReadProgress(reader));
        }

        return all;
    }

    public int CommitBatch(IReadOnlyList<LogRecord> records, FileProgress progress)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(progress);

        // I-01: filas y marca de progreso viajan juntas. Si esto se separa, una caida
        // entre ambas escrituras pierde o duplica registros, y ningun test posterior
        // puede detectarlo.
        using var transaction = _connection.BeginTransaction();

        var inserted = 0;

        if (records.Count > 0)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = _insertSql;

            var siteId = command.Parameters.Add("$site_id", SqliteType.Text);
            var sourceFile = command.Parameters.Add("$source_file", SqliteType.Text);
            var lineOffset = command.Parameters.Add("$line_offset", SqliteType.Integer);
            var extras = command.Parameters.Add("$extra_fields", SqliteType.Text);
            var ingestedAt = command.Parameters.Add("$ingested_at_utc", SqliteType.Text);

            var columnParameters = new SqliteParameter[LogColumns.Count];
            for (var i = 0; i < LogColumns.Count; i++)
            {
                columnParameters[i] = command.Parameters.Add(
                    "$" + LogColumns.Names[i],
                    LogColumns.IsInteger(i) ? SqliteType.Integer : SqliteType.Text);
            }

            // Un comando preparado reutilizado en todo el lote: es lo que sostiene el
            // objetivo de rendimiento del SC-002 (D-008).
            command.Prepare();

            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            foreach (var record in records)
            {
                siteId.Value = record.SiteId;
                sourceFile.Value = record.SourceFile;
                lineOffset.Value = record.LineOffset;
                extras.Value = record.HasExtras
                    ? JsonSerializer.Serialize(record.Extras)
                    : DBNull.Value;
                ingestedAt.Value = now;

                for (var i = 0; i < LogColumns.Count; i++)
                {
                    var value = record.ValueAt(i);
                    columnParameters[i].Value = value is null ? DBNull.Value : value;
                }

                inserted += command.ExecuteNonQuery();
            }
        }

        WriteProgress(transaction, progress);
        transaction.Commit();

        return inserted;
    }

    public void ResetFile(string sourceFile, string siteId, long newSize)
    {
        using var transaction = _connection.BeginTransaction();

        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM log_entries WHERE source_file = $source_file;";
            delete.Parameters.AddWithValue("$source_file", sourceFile);
            delete.ExecuteNonQuery();
        }

        WriteProgress(transaction, new FileProgress(sourceFile, siteId, BytesIngested: 0, newSize));
        transaction.Commit();
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }

    private void WriteProgress(SqliteTransaction transaction, FileProgress progress)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO file_progress (source_file, site_id, bytes_ingested, last_seen_size, updated_at_utc)
            VALUES ($source_file, $site_id, $bytes_ingested, $last_seen_size, $updated_at_utc)
            ON CONFLICT(source_file) DO UPDATE SET
                bytes_ingested = excluded.bytes_ingested,
                last_seen_size = excluded.last_seen_size,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$source_file", progress.SourceFile);
        command.Parameters.AddWithValue("$site_id", progress.SiteId);
        command.Parameters.AddWithValue("$bytes_ingested", progress.BytesIngested);
        command.Parameters.AddWithValue("$last_seen_size", progress.LastSeenSize);
        command.Parameters.AddWithValue(
            "$updated_at_utc",
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static FileProgress ReadProgress(SqliteDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3));

    /// <summary>
    /// INSERT OR IGNORE sobre el indice unico: es el mecanismo de idempotencia (FR-011).
    /// Se arma desde <see cref="LogColumns.Names"/> para no repetir la lista de columnas.
    /// </summary>
    private static string BuildInsertSql()
    {
        var columns = new StringBuilder("site_id, source_file, line_offset");
        var parameters = new StringBuilder("$site_id, $source_file, $line_offset");

        foreach (var column in LogColumns.Names)
        {
            columns.Append(", ").Append(column);
            parameters.Append(", $").Append(column);
        }

        columns.Append(", extra_fields, ingested_at_utc");
        parameters.Append(", $extra_fields, $ingested_at_utc");

        return $"INSERT OR IGNORE INTO log_entries ({columns}) VALUES ({parameters});";
    }
}
