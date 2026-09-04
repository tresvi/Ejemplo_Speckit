using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace IISLogParser.Diagnostics;

/// <summary>
/// Formateador del log de diagnóstico: un objeto JSON por línea, con los campos
/// obligatorios <c>ts</c>, <c>level</c> y <c>event</c> del contrato (contracts/cli.md).
/// El formateador propio existe porque el contrato fija esos nombres de campo, y el
/// formateador JSON que trae la consola emite los suyos.
/// </summary>
public sealed class JsonEventFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "iislogparser-json";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("level", logEntry.LogLevel.ToString());
            writer.WriteString("event", logEntry.EventId.Name ?? "unnamed");

            if (logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> fields)
            {
                foreach (var (key, value) in fields)
                {
                    if (string.Equals(key, "{OriginalFormat}", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    WriteField(writer, key, value);
                }
            }

            if (logEntry.Exception is not null)
            {
                writer.WriteString("exception", logEntry.Exception.GetType().Name);
                writer.WriteString("exception_message", logEntry.Exception.Message);
            }

            writer.WriteEndObject();
        }

        textWriter.WriteLine(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static void WriteField(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(key);
                break;
            case long l:
                writer.WriteNumber(key, l);
                break;
            case int i:
                writer.WriteNumber(key, i);
                break;
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            default:
                writer.WriteString(key, value.ToString());
                break;
        }
    }
}
