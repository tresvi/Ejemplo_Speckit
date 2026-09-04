using IISLogParser.Diagnostics;
using IISLogParser.Discovery;
using IISLogParser.Storage;

namespace IISLogParser.Ingestion;

/// <summary>
/// Modo continuo: ejecuta un ciclo completo y recién entonces espera el <i>Poll
/// Interval</i>. Los ciclos nunca se solapan; esperar después del ciclo, en vez de
/// disparar por reloj, impide que se acumulen cuando uno tarda más que el intervalo
/// (D-009, FR-015).
/// </summary>
public sealed class ContinuousRunner(
    ISiteDiscovery discovery,
    FileIngestor ingestor,
    IFileSystem fileSystem,
    ILogStore store,
    DiagnosticLog diagnostics,
    OperatorReport report,
    IClock clock)
{
    private readonly WatchSetResolver _watchSet = new(fileSystem, discovery, store);

    /// <summary>Puntos de observación para verificar que los ciclos no se solapan.</summary>
    public event Action? CycleStarted;

    public event Action? CycleCompleted;

    public void Run(
        string logsRoot,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        ICycleWaiter? waiter = null)
    {
        var cycleWaiter = waiter ?? CancellableWaiter.Instance;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                RunCycle(logsRoot, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            cycleWaiter.Wait(pollInterval, cancellationToken);
        }

        diagnostics.Shutdown("cancelled");
    }

    /// <summary>
    /// Un ciclo: resuelve el conjunto vigilado e ingesta lo que creció. El primer ciclo
    /// no es especial — ingesta el archivo del día desde su marca de progreso, o desde
    /// el principio si no la tiene, que es exactamente el backfill del FR-014a.
    /// </summary>
    public IngestReport RunCycle(string logsRoot, CancellationToken cancellationToken)
    {
        CycleStarted?.Invoke();

        var result = new IngestReport();
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        WatchSet watched;

        try
        {
            watched = _watchSet.Resolve(logsRoot, today);
        }
        catch (LogsRootUnavailableException ex)
        {
            // Un recurso de red que parpadea no puede matar un proceso pensado para
            // correr 24 horas: se reporta la omision y el ciclo siguiente reintenta
            // (FR-026, SC-006).
            Skip(result, logsRoot, ex.InnerException?.Message ?? ex.Message);
            watched = new WatchSet([], []);
        }

        foreach (var skipped in watched.Skipped)
        {
            Skip(result, skipped.Path, skipped.Reason);
        }

        foreach (var file in watched.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = ingestor.Ingest(file.SiteId, file.AbsolutePath, file.RelativePath, cancellationToken);

            if (outcome.WasSkipped)
            {
                // FR-026: se reintenta solo en el ciclo siguiente, porque el archivo
                // sigue en el conjunto vigilado.
                Skip(result, file.RelativePath, outcome.SkipReason!);
                continue;
            }

            result.AddFile(outcome);
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

        CycleCompleted?.Invoke();

        return result;
    }

    private void Skip(IngestReport result, string path, string reason)
    {
        result.AddSkip(path, reason);
        report.WriteSkipped(path, reason);
        diagnostics.PathSkipped(path, reason);
    }
}
