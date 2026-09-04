using IISLogParser.Parsing;

namespace IISLogParser.Storage;

/// <summary>
/// Marca de progreso de un archivo de log. <paramref name="BytesIngested"/> apunta
/// siempre al byte siguiente a un salto de línea confirmado, nunca a media línea (I-03).
/// </summary>
public sealed record FileProgress(
    string SourceFile,
    string SiteId,
    long BytesIngested,
    long LastSeenSize);

/// <summary>
/// Almacén de registros. La interfaz existe por testabilidad concreta: permite
/// verificar el contrato de almacenamiento y simular un fallo del almacén sin
/// romper una base real.
/// </summary>
public interface ILogStore : IDisposable
{
    /// <summary>Crea el esquema si no existe. Idempotente (FR-010).</summary>
    void Initialize();

    FileProgress? GetProgress(string sourceFile);

    IReadOnlyList<FileProgress> GetAllProgress();

    /// <summary>
    /// Escribe las filas y la marca de progreso en una <b>única</b> transacción (I-01).
    /// Devuelve la cantidad de filas realmente insertadas: las descartadas por identidad
    /// duplicada no cuentan como registros nuevos (FR-011).
    /// </summary>
    int CommitBatch(IReadOnlyList<LogRecord> records, FileProgress progress);

    /// <summary>
    /// Borra todas las filas del archivo y reinicia su progreso en cero, en una sola
    /// transacción. Es el camino de FR-011a para un archivo truncado o reemplazado.
    /// </summary>
    void ResetFile(string sourceFile, string siteId, long newSize);
}
