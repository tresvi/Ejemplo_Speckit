using IISLogParser.Diagnostics;
using IISLogParser.Discovery;

namespace IISLogParser.Ingestion;

/// <summary>
/// Modo snapshot: recorre todos los sitios y todos sus archivos, y termina (FR-013).
/// Un fallo localizado no aborta el recorrido: se omite, se reporta y la corrida queda
/// señalada como incompleta (FR-024, FR-025).
/// </summary>
public sealed class SnapshotRunner(
    ISiteDiscovery discovery,
    FileIngestor ingestor,
    IFileSystem fileSystem,
    DiagnosticLog diagnostics,
    OperatorReport report)
{
    public IngestReport Run(string logsRoot, CancellationToken cancellationToken)
    {
        var result = new IngestReport();

        var discovered = discovery.DiscoverSites(logsRoot);

        foreach (var ignored in discovered.Ignored)
        {
            // Una carpeta ajena no es un error: se informa y no afecta el codigo de salida.
            report.WriteSkipped(RelativePath(logsRoot, ignored.Path), ignored.Reason);
        }

        foreach (var site in discovered.Sites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IngestSite(logsRoot, site, result, cancellationToken);
        }

        report.WriteSummary(
            result.FilesProcessed,
            result.RecordsIngested,
            result.LinesRejected,
            result.SkippedPaths.Count);

        diagnostics.CycleCompleted(
            result.FilesProcessed,
            result.RecordsIngested,
            result.LinesRejected,
            result.SkippedPaths.Count);

        return result;
    }

    private void IngestSite(
        string logsRoot,
        SiteFolder site,
        IngestReport result,
        CancellationToken cancellationToken)
    {
        List<string> files;

        try
        {
            files = [.. fileSystem.EnumerateFiles(site.Path).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip(result, RelativePath(logsRoot, site.Path), ex.Message);
            return;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = RelativePath(logsRoot, file);
            var outcome = ingestor.Ingest(site.SiteId, file, relativePath, cancellationToken);

            if (outcome.WasSkipped)
            {
                Skip(result, relativePath, outcome.SkipReason!);
                continue;
            }

            result.AddFile(outcome);
        }
    }

    private void Skip(IngestReport result, string path, string reason)
    {
        result.AddSkip(path, reason);
        report.WriteSkipped(path, reason);
        diagnostics.PathSkipped(path, reason);
    }

    /// <summary>I-05: las rutas viajan relativas a la raíz y con separador '/'.</summary>
    public static string RelativePath(string logsRoot, string path) =>
        Path.GetRelativePath(logsRoot, path).Replace('\\', '/');
}
