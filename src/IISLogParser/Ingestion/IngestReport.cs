using IISLogParser.Cli;
using IISLogParser.Discovery;

namespace IISLogParser.Ingestion;

/// <summary>Resultado de ingestar un archivo.</summary>
public sealed record FileIngestResult(int NewRecords, int RejectedLines, string? SkipReason)
{
    public bool WasSkipped => SkipReason is not null;

    public static FileIngestResult Skipped(string reason) => new(0, 0, reason);
}

/// <summary>
/// Contadores del reporte (FR-022) y regla de código de salida (FR-023).
/// </summary>
public sealed class IngestReport
{
    private readonly List<SkippedPath> _skipped = [];

    public int FilesProcessed { get; private set; }

    /// <summary>
    /// Filas realmente insertadas. Las descartadas por identidad duplicada no cuentan
    /// como registros nuevos (FR-011).
    /// </summary>
    public int RecordsIngested { get; private set; }

    public int LinesRejected { get; private set; }

    public IReadOnlyList<SkippedPath> SkippedPaths => _skipped;

    public void AddFile(FileIngestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.WasSkipped)
        {
            return;
        }

        FilesProcessed++;
        RecordsIngested += result.NewRecords;
        LinesRejected += result.RejectedLines;
    }

    public void AddSkip(string path, string reason) => _skipped.Add(new SkippedPath(path, reason));

    /// <summary>
    /// Un recorrido con omisiones no puede reportarse como exitoso: un planificador que
    /// reciba cero debe poder asumir que la corrida fue completa (FR-023).
    /// </summary>
    public ExitCode ToExitCode() =>
        _skipped.Count > 0 ? ExitCode.CompletedWithSkips : ExitCode.Success;

    /// <summary>Acumula el resultado de un ciclo dentro del total de la ejecución.</summary>
    public void Reset()
    {
        FilesProcessed = 0;
        RecordsIngested = 0;
        LinesRejected = 0;
        _skipped.Clear();
    }
}
