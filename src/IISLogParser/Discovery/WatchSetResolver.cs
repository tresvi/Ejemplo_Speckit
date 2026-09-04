using IISLogParser.Ingestion;
using IISLogParser.Storage;

namespace IISLogParser.Discovery;

/// <summary>Un archivo del conjunto vigilado.</summary>
public sealed record WatchedFile(string SiteId, string AbsolutePath, string RelativePath);

/// <summary>Conjunto vigilado de un ciclo, más las rutas que no pudieron inspeccionarse.</summary>
public sealed record WatchSet(IReadOnlyList<WatchedFile> Files, IReadOnlyList<SkippedPath> Skipped);

/// <summary>
/// Resuelve qué archivos mira el modo continuo en cada ciclo: el archivo del día de cada
/// sitio, unido a todo archivo con bytes pendientes respecto de su marca de progreso.
///
/// La primera regla hace que el cambio de día se resuelva solo —al día siguiente, "el
/// archivo de hoy" simplemente es otro—. La segunda es la que evita perder la cola del
/// archivo del día anterior: aunque deje de ser el de hoy, sigue teniendo bytes
/// pendientes y por lo tanto sigue vigilado hasta terminar de leerse (D-009).
/// </summary>
public sealed class WatchSetResolver(
    IFileSystem fileSystem,
    ISiteDiscovery discovery,
    ILogStore store)
{
    public WatchSet Resolve(string logsRoot, DateOnly today)
    {
        var files = new List<WatchedFile>();
        var skipped = new List<SkippedPath>();

        var progressByFile = store.GetAllProgress()
            .ToDictionary(p => p.SourceFile, StringComparer.OrdinalIgnoreCase);

        foreach (var site in discovery.DiscoverSites(logsRoot).Sites)
        {
            List<string> siteFiles;

            try
            {
                siteFiles = [.. fileSystem.EnumerateFiles(site.Path).Order(StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedPath(SnapshotRunner.RelativePath(logsRoot, site.Path), ex.Message));
                continue;
            }

            foreach (var file in siteFiles)
            {
                var relativePath = SnapshotRunner.RelativePath(logsRoot, file);

                if (ShouldWatch(file, relativePath, today, progressByFile))
                {
                    files.Add(new WatchedFile(site.SiteId, file, relativePath));
                }
            }
        }

        return new WatchSet(files, skipped);
    }

    private bool ShouldWatch(
        string absolutePath,
        string relativePath,
        DateOnly today,
        Dictionary<string, FileProgress> progressByFile)
    {
        if (LogFileName.TryGetDate(absolutePath, out var fileDate) && fileDate == today)
        {
            return true;
        }

        if (!progressByFile.TryGetValue(relativePath, out var progress))
        {
            // Sin marca de progreso y sin ser el archivo de hoy: es historico, y el
            // historico es trabajo del modo snapshot (FR-014a).
            return false;
        }

        try
        {
            return fileSystem.GetFileSize(absolutePath) != progress.BytesIngested;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no se puede ni medir, se vigila igual: el intento de lectura del ciclo
            // lo reportara como omitido con su causa real.
            return true;
        }
    }
}
