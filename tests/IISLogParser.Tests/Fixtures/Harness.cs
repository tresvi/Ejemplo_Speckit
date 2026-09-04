using IISLogParser.Diagnostics;
using IISLogParser.Discovery;
using IISLogParser.Ingestion;
using IISLogParser.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace IISLogParser.Tests.Fixtures;

/// <summary>
/// Composición mínima para los tests de ingesta: el mismo cableado que arma el
/// programa, con stdout capturado en memoria y el sistema de archivos sustituible.
/// </summary>
public static class Harness
{
    public static (IngestReport Report, string Output) RunSnapshot(
        TempWorkspace workspace,
        IFileSystem? fileSystem = null)
    {
        var stdout = new StringWriter();
        var report = RunSnapshot(workspace, stdout, fileSystem);
        return (report, stdout.ToString());
    }

    public static IngestReport RunSnapshot(
        TempWorkspace workspace,
        TextWriter stdout,
        IFileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? new FileSystem();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var diagnostics = new DiagnosticLog(NullLogger.Instance);
        var operatorReport = new OperatorReport(stdout);
        var ingestor = new FileIngestor(fs, store, diagnostics, operatorReport);
        var runner = new SnapshotRunner(new SiteDiscovery(fs), ingestor, fs, diagnostics, operatorReport);

        return runner.Run(workspace.LogsPath, CancellationToken.None);
    }
}

/// <summary>
/// Sistema de archivos que falla al abrir una ruta concreta. Reproducir un fallo de
/// permisos real de forma portable es frágil; la frontera de E/S existe justamente para
/// que el test pueda inyectar el error que quiere observar (D-013).
/// </summary>
public sealed class FailingFileSystem(IFileSystem inner, string failingPathFragment, string message)
    : IFileSystem
{
    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public bool FileExists(string path) => inner.FileExists(path);

    public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(path);

    public IEnumerable<string> EnumerateFiles(string path)
    {
        if (Matches(path))
        {
            throw new UnauthorizedAccessException(message);
        }

        return inner.EnumerateFiles(path);
    }

    public long GetFileSize(string path) => inner.GetFileSize(path);

    public Stream OpenRead(string path)
    {
        if (Matches(path))
        {
            throw new UnauthorizedAccessException(message);
        }

        return inner.OpenRead(path);
    }

    private bool Matches(string path) =>
        path.Contains(failingPathFragment, StringComparison.OrdinalIgnoreCase);
}
