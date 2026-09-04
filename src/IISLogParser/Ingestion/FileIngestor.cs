using System.Text;
using IISLogParser.Diagnostics;
using IISLogParser.Parsing;
using IISLogParser.Storage;

namespace IISLogParser.Ingestion;

/// <summary>
/// Lectura incremental de un archivo de log. Tres reglas sostienen todo lo demás:
/// se lee desde el offset confirmado, solo se consumen líneas terminadas en salto de
/// línea, y cada lote escribe filas y marca de progreso en la misma transacción
/// (D-004, D-007).
/// </summary>
public sealed class FileIngestor(
    IFileSystem fileSystem,
    ILogStore store,
    DiagnosticLog diagnostics,
    OperatorReport report,
    int batchSize = 5000)
{
    private const byte LineFeed = (byte)'\n';

    /// <summary>
    /// Mapa de columnas vigente por archivo, entre llamadas. Sin este caché, reanudar
    /// obligaría a re-escanear el prefijo del archivo en cada ciclo del modo continuo,
    /// que sobre un archivo de gigabytes es inaceptable.
    /// </summary>
    private readonly Dictionary<string, FieldMap> _fieldMaps = new(StringComparer.OrdinalIgnoreCase);

    public FileIngestResult Ingest(
        string siteId,
        string absolutePath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        long size;
        try
        {
            size = fileSystem.GetFileSize(absolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileIngestResult.Skipped(ex.Message);
        }

        var progress = store.GetProgress(relativePath);
        var startOffset = progress?.BytesIngested ?? 0;

        if (progress is not null && size < progress.BytesIngested)
        {
            // FR-011a: sin el borrado previo, los offsets del contenido viejo
            // colisionarian con los del nuevo y el INSERT OR IGNORE descartaria
            // filas legitimas.
            diagnostics.FileReplaced(siteId, relativePath, progress.BytesIngested, size);
            store.ResetFile(relativePath, siteId, size);
            _fieldMaps.Remove(relativePath);
            startOffset = 0;
        }

        if (startOffset == size)
        {
            return new FileIngestResult(0, 0, null);
        }

        try
        {
            return Read(siteId, absolutePath, relativePath, startOffset, size, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileIngestResult.Skipped(ex.Message);
        }
    }

    private FileIngestResult Read(
        string siteId,
        string absolutePath,
        string relativePath,
        long startOffset,
        long size,
        CancellationToken cancellationToken)
    {
        var map = ResolveFieldMap(absolutePath, relativePath, startOffset);

        diagnostics.FileOpened(siteId, relativePath, startOffset);

        using var stream = fileSystem.OpenRead(absolutePath);
        stream.Seek(startOffset, SeekOrigin.Begin);

        var buffer = new byte[64 * 1024];
        var pending = new MemoryStream();
        var batch = new List<LogRecord>(batchSize);

        var position = startOffset;
        var lineStart = startOffset;
        var confirmedOffset = startOffset;
        var newRecords = 0;
        var rejected = 0;

        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            var scanned = 0;

            while (scanned < read)
            {
                var newline = Array.IndexOf(buffer, LineFeed, scanned, read - scanned);

                if (newline < 0)
                {
                    pending.Write(buffer, scanned, read - scanned);
                    position += read - scanned;
                    break;
                }

                pending.Write(buffer, scanned, newline - scanned);
                position += newline - scanned + 1;

                var line = Decode(pending);
                pending.SetLength(0);

                ProcessLine(
                    line, siteId, relativePath, lineStart, ref map, batch, ref rejected);

                // I-03: el offset confirmado siempre queda en frontera de linea.
                confirmedOffset = position;
                lineStart = position;
                scanned = newline + 1;

                if (batch.Count >= batchSize)
                {
                    newRecords += Commit(siteId, relativePath, batch, confirmedOffset, size);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        // El resto del bufer es una linea a medio escribir: no se confirma, y se
        // releera entera cuando el escritor la termine (D-004).
        newRecords += Commit(siteId, relativePath, batch, confirmedOffset, size);

        return new FileIngestResult(newRecords, rejected, null);
    }

    private void ProcessLine(
        string line,
        string siteId,
        string relativePath,
        long lineStart,
        ref FieldMap map,
        List<LogRecord> batch,
        ref int rejected)
    {
        if (W3CLineParser.IsCommentOrDirective(line))
        {
            if (FieldMap.IsFieldsDirective(line))
            {
                map = FieldMap.Parse(line);
                _fieldMaps[relativePath] = map;
                diagnostics.FieldMapChanged(siteId, relativePath, lineStart, map.Count);
            }

            return;
        }

        var outcome = W3CLineParser.Parse(line, map);

        if (outcome.RejectionReason is not null)
        {
            rejected++;
            report.WriteRejectedLine(siteId, relativePath, lineStart, line, outcome.RejectionReason);
            return;
        }

        if (outcome.Record is null)
        {
            return;
        }

        outcome.Record.SiteId = siteId;
        outcome.Record.SourceFile = relativePath;
        outcome.Record.LineOffset = lineStart;
        batch.Add(outcome.Record);
    }

    private int Commit(
        string siteId,
        string relativePath,
        List<LogRecord> batch,
        long confirmedOffset,
        long size)
    {
        var inserted = store.CommitBatch(batch, new FileProgress(relativePath, siteId, confirmedOffset, size));
        diagnostics.BatchCommitted(siteId, relativePath, confirmedOffset, inserted);
        batch.Clear();
        return inserted;
    }

    /// <summary>
    /// Reanudar a mitad de archivo sin el mapa de columnas dejaría al parser rechazando
    /// todas las líneas restantes. Cuando no está en caché, se re-escanean las
    /// directivas del prefijo —solo las líneas que empiezan con '#'— sin ingestar datos.
    /// </summary>
    private FieldMap ResolveFieldMap(string absolutePath, string relativePath, long startOffset)
    {
        if (_fieldMaps.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var map = FieldMap.Empty;

        if (startOffset > 0)
        {
            using var stream = fileSystem.OpenRead(absolutePath);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            long consumed = 0;
            while (consumed < startOffset && reader.ReadLine() is { } line)
            {
                consumed += Encoding.UTF8.GetByteCount(line) + 1;

                if (FieldMap.IsFieldsDirective(line))
                {
                    map = FieldMap.Parse(line);
                }
            }
        }

        _fieldMaps[relativePath] = map;
        return map;
    }

    private static string Decode(MemoryStream pending)
    {
        var text = Encoding.UTF8.GetString(pending.GetBuffer(), 0, (int)pending.Length);
        return text.TrimEnd('\r').TrimStart('﻿');
    }
}
