using System.Text;
using Microsoft.Data.Sqlite;

namespace IISLogParser.Tests.Fixtures;

/// <summary>
/// T021 — árbol de logs sintético y base de datos, ambos en un directorio temporal
/// propio de cada test. Es la sustitución del sistema de archivos real que hace que
/// la suite sea determinista y no necesite IIS, red ni credenciales (Principio III).
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    private const string StandardFields =
        "#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip " +
        "cs(User-Agent) sc-status sc-substatus sc-win32-status time-taken";

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "iislogparser-tests", Guid.NewGuid().ToString("N"));
        LogsPath = Path.Combine(Root, "logs");
        DatabasePath = Path.Combine(Root, "iislogs.db");
        Directory.CreateDirectory(LogsPath);
    }

    public string Root { get; }

    public string LogsPath { get; }

    public string DatabasePath { get; }

    public string ConnectionString => $"Data Source={DatabasePath}";

    /// <summary>Cabecera W3C completa, tal como la escribe IIS al abrir un archivo.</summary>
    public static IReadOnlyList<string> StandardHeader =>
    [
        "#Software: Microsoft Internet Information Services 10.0",
        "#Version: 1.0",
        "#Date: 2026-09-03 00:00:02",
        StandardFields,
    ];

    /// <summary>Una línea de petición válida contra la cabecera estándar.</summary>
    public static string DataLine(
        string time = "00:00:02",
        string method = "GET",
        string stem = "/index.html",
        string status = "200",
        string date = "2026-09-03") =>
        $"{date} {time} 10.0.0.4 {method} {stem} - 80 - 10.0.0.99 Mozilla/5.0 {status} 0 0 15";

    public string SitePath(string siteId) => Path.Combine(LogsPath, siteId);

    public string CreateSite(string siteId)
    {
        var path = SitePath(siteId);
        Directory.CreateDirectory(path);
        return path;
    }

    public string FilePath(string siteId, string fileName) => Path.Combine(SitePath(siteId), fileName);

    /// <summary>Crea un archivo de log con cabecera estándar y las líneas indicadas.</summary>
    public string WriteLog(string siteId, string fileName, params string[] dataLines)
    {
        CreateSite(siteId);
        var path = FilePath(siteId, fileName);
        var content = new StringBuilder();

        foreach (var header in StandardHeader)
        {
            content.Append(header).Append('\n');
        }

        foreach (var line in dataLines)
        {
            content.Append(line).Append('\n');
        }

        File.WriteAllText(path, content.ToString(), new UTF8Encoding(false));
        return path;
    }

    /// <summary>Agrega líneas completas al final de un archivo existente.</summary>
    public void AppendLines(string siteId, string fileName, params string[] dataLines)
    {
        var content = new StringBuilder();
        foreach (var line in dataLines)
        {
            content.Append(line).Append('\n');
        }

        File.AppendAllText(FilePath(siteId, fileName), content.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Agrega texto crudo: sirve para dejar una última línea sin terminador.</summary>
    public void AppendRaw(string siteId, string fileName, string text) =>
        File.AppendAllText(FilePath(siteId, fileName), text, new UTF8Encoding(false));

    /// <summary>Reemplaza el archivo por uno más corto, como haría una rotación inesperada.</summary>
    public void ReplaceWithShorter(string siteId, string fileName, params string[] dataLines)
    {
        File.Delete(FilePath(siteId, fileName));
        WriteLog(siteId, fileName, dataLines);
    }

    public long CountRows(string table = "log_entries") =>
        QueryScalar<long>($"SELECT COUNT(*) FROM {table};");

    public T QueryScalar<T>(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = command.ExecuteScalar();
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public List<string> QueryStrings(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return values;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Un archivo todavía tomado por el sistema operativo no debe hacer fallar
            // un test que ya paso: el directorio temporal lo limpia el sistema.
        }
    }
}
