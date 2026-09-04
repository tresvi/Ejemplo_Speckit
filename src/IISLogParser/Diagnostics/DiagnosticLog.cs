using System.Collections;
using Microsoft.Extensions.Logging;

namespace IISLogParser.Diagnostics;

/// <summary>
/// Log estructurado a stderr. Se instrumentan <b>solo</b> límites del sistema, decisiones
/// y fallos: el Principio IV prohíbe explícitamente instrumentar el flujo interno paso a
/// paso, y un evento por línea parseada enterraría la señal bajo ruido.
/// </summary>
public sealed class DiagnosticLog(ILogger logger)
{
    private readonly ILogger _logger = logger;

    public void Startup(string mode, string logsPath, string databasePath, int pollIntervalSeconds) =>
        Emit(LogLevel.Information, "startup",
            ("mode", mode),
            ("logs_path", logsPath),
            ("database", databasePath),
            ("poll_interval_seconds", pollIntervalSeconds));

    public void FileOpened(string site, string file, long fromOffset) =>
        Emit(LogLevel.Debug, "file_opened",
            ("site", site), ("file", file), ("from_offset", fromOffset));

    public void FileReplaced(string site, string file, long previousOffset, long currentSize) =>
        Emit(LogLevel.Warning, "file_replaced",
            ("site", site), ("file", file),
            ("previous_offset", previousOffset), ("current_size", currentSize));

    public void FieldMapChanged(string site, string file, long offset, int columnCount) =>
        Emit(LogLevel.Information, "field_map_changed",
            ("site", site), ("file", file), ("offset", offset), ("columns", columnCount));

    public void BatchCommitted(string site, string file, long offset, int rows) =>
        Emit(LogLevel.Information, "batch_committed",
            ("site", site), ("file", file), ("offset", offset), ("rows", rows));

    public void PathSkipped(string path, string reason) =>
        Emit(LogLevel.Warning, "path_skipped", ("path", path), ("reason", reason));

    public void CycleCompleted(int files, int rows, int rejected, int skipped) =>
        Emit(LogLevel.Information, "cycle_completed",
            ("files", files), ("rows", rows), ("rejected", rejected), ("skipped", skipped));

    public void Shutdown(string reason) =>
        Emit(LogLevel.Information, "shutdown", ("reason", reason));

    public void FatalError(string message, Exception? exception = null) =>
        _logger.Log(
            LogLevel.Error,
            new EventId(0, "fatal_error"),
            new EventState([new KeyValuePair<string, object?>("message", message)]),
            exception,
            static (state, _) => state.ToString());

    private void Emit(LogLevel level, string eventName, params (string Key, object? Value)[] fields)
    {
        if (!_logger.IsEnabled(level))
        {
            return;
        }

        var pairs = new KeyValuePair<string, object?>[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            pairs[i] = new KeyValuePair<string, object?>(fields[i].Key, fields[i].Value);
        }

        _logger.Log(level, new EventId(0, eventName), new EventState(pairs), null,
            static (state, _) => state.ToString());
    }

    private sealed class EventState(KeyValuePair<string, object?>[] fields)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => fields.Length;

        public KeyValuePair<string, object?> this[int index] => fields[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)fields).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => fields.GetEnumerator();

        public override string ToString() => string.Join(' ', fields.Select(f => $"{f.Key}={f.Value}"));
    }
}
