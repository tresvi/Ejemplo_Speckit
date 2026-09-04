using System.Globalization;

namespace IISLogParser.Parsing;

/// <summary>Resultado de parsear una línea: un registro, un rechazo, o ninguno (línea en blanco).</summary>
public sealed record ParseOutcome(LogRecord? Record, string? RejectionReason)
{
    public static readonly ParseOutcome Skipped = new(null, null);

    public static ParseOutcome Parsed(LogRecord record) => new(record, null);

    public static ParseOutcome Rejected(string reason) => new(null, reason);
}

/// <summary>
/// Parseo de una línea de datos W3C contra el mapa de columnas vigente.
/// Reglas V-01 a V-06 de contracts/w3c-log-format.md.
/// </summary>
public static class W3CLineParser
{
    private const string EmptyValue = "-";
    private const string DateField = "date";
    private const string TimeField = "time";

    /// <summary>Toda línea que empieza con '#' es directiva o comentario (V-06).</summary>
    public static bool IsCommentOrDirective(string line) =>
        line.StartsWith('#');

    public static ParseOutcome Parse(string line, FieldMap map)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(map);

        if (string.IsNullOrWhiteSpace(line))
        {
            return ParseOutcome.Skipped;
        }

        if (map.IsEmpty)
        {
            // V-01: sin cabecera no hay mapa, y adivinarlo seria inventar.
            return ParseOutcome.Rejected("Linea de datos previa a cualquier directiva #Fields:.");
        }

        var values = line.Split(' ');

        if (values.Length < map.Count)
        {
            // V-02
            return ParseOutcome.Rejected(
                $"La linea tiene {values.Length} valores y la cabecera declara {map.Count} columnas.");
        }

        if (values.Length > map.Count)
        {
            // V-03: el formato W3C no admite valores sin campo.
            return ParseOutcome.Rejected(
                $"La linea tiene {values.Length} valores y la cabecera declara solo {map.Count} columnas.");
        }

        var record = new LogRecord();
        string? date = null;
        string? time = null;

        for (var i = 0; i < map.Count; i++)
        {
            var value = values[i];
            var isEmpty = value.Length == 0 || string.Equals(value, EmptyValue, StringComparison.Ordinal);
            var field = map[i];

            if (string.Equals(field, DateField, StringComparison.Ordinal))
            {
                date = isEmpty ? null : value;
                continue;
            }

            if (string.Equals(field, TimeField, StringComparison.Ordinal))
            {
                time = isEmpty ? null : value;
                continue;
            }

            var columnIndex = LogColumns.IndexOf(field);

            if (columnIndex < 0)
            {
                // FR-009a: lo que no encuentra columna se preserva con su nombre original.
                if (!isEmpty)
                {
                    record.AddExtra(map.OriginalName(i), value);
                }

                continue;
            }

            if (isEmpty)
            {
                // V-04: el guion medio es campo vacio, no el literal "-".
                continue;
            }

            if (LogColumns.IsInteger(columnIndex) && !IsInteger(value))
            {
                // V-05: se prefiere una fila con la anomalia preservada antes que
                // perder la peticion entera.
                record.AddExtra(map.OriginalName(i), value);
                continue;
            }

            record.SetValue(columnIndex, value);
        }

        if (date is not null && time is not null)
        {
            record.SetValue(LogColumns.IndexOf("timestamp_utc"), $"{date}T{time}");
        }
        else if (date is not null)
        {
            record.AddExtra(DateField, date);
        }
        else if (time is not null)
        {
            record.AddExtra(TimeField, time);
        }

        return ParseOutcome.Parsed(record);
    }

    private static bool IsInteger(string value) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
}
