using System.Globalization;

namespace IISLogParser.Diagnostics;

/// <summary>
/// Reporte legible para el operador, a stdout. Es la otra mitad de D-012: acá va lo que
/// una persona lee, no lo que un colector consulta.
/// </summary>
public sealed class OperatorReport(TextWriter output)
{
    private readonly TextWriter _output = output;

    /// <summary>
    /// Resumen de un ciclo o de una ejecución (FR-022). Un ciclo sin novedades emite
    /// igual su línea con ceros: el silencio no debe ser ambiguo respecto de un proceso
    /// colgado.
    /// </summary>
    public void WriteSummary(int files, int newRecords, int rejectedLines, int skippedPaths)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        _output.WriteLine(
            $"[{timestamp}] Archivos: {files} | Nuevos: {newRecords} | " +
            $"Rechazadas: {rejectedLines} | Omitidos: {skippedPaths}");
    }

    /// <summary>Detalle de una omisión, antes de la línea de resumen.</summary>
    public void WriteSkipped(string path, string reason) =>
        _output.WriteLine($"  OMITIDO  {path} — {reason}");

    /// <summary>
    /// Detalle de una línea rechazada. Incluye el contenido íntegro, sin enmascarar:
    /// ningún campo de los logs de IIS se clasifica como dato personal en este proyecto
    /// (decisión registrada en la fase de clarificación).
    /// </summary>
    public void WriteRejectedLine(string site, string file, long offset, string content, string reason) =>
        _output.WriteLine($"  RECHAZADA  {site} {file}:{offset} — {reason} — {content}");

    public void WriteError(string message) => _output.WriteLine($"ERROR: {message}");

    public void WriteLine(string line) => _output.WriteLine(line);
}
