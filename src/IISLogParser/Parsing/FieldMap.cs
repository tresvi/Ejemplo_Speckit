using System.Text;

namespace IISLogParser.Parsing;

/// <summary>
/// Mapa de columnas declarado por la directiva <c>#Fields:</c> de un archivo de log.
/// Es la única fuente confiable del orden y la selección de campos: cada sitio de IIS
/// elige los suyos, y la directiva puede reaparecer a mitad de archivo cuando se cambia
/// la configuración de logging sin reiniciar el sitio (D-005).
/// </summary>
public sealed class FieldMap
{
    private const string DirectivePrefix = "#Fields:";

    private readonly string[] _normalized;
    private readonly string[] _original;

    private FieldMap(string[] normalized, string[] original)
    {
        _normalized = normalized;
        _original = original;
    }

    /// <summary>Estado inicial de todo archivo: sin columnas conocidas.</summary>
    public static FieldMap Empty { get; } = new([], []);

    public int Count => _normalized.Length;

    public bool IsEmpty => _normalized.Length == 0;

    /// <summary>Nombre normalizado de la columna (por ejemplo <c>cs_user_agent</c>).</summary>
    public string this[int index] => _normalized[index];

    /// <summary>
    /// Nombre tal como lo declaró el archivo. Los campos desconocidos se preservan en
    /// <c>extra_fields</c> con este nombre, no con el normalizado.
    /// </summary>
    public string OriginalName(int index) => _original[index];

    public static bool IsFieldsDirective(string line) =>
        line.StartsWith(DirectivePrefix, StringComparison.OrdinalIgnoreCase);

    public static FieldMap Parse(string directive)
    {
        ArgumentNullException.ThrowIfNull(directive);

        var payload = IsFieldsDirective(directive)
            ? directive[DirectivePrefix.Length..]
            : directive;

        var original = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (original.Length == 0)
        {
            return Empty;
        }

        var normalized = new string[original.Length];
        for (var i = 0; i < original.Length; i++)
        {
            normalized[i] = Normalize(original[i]);
        }

        return new FieldMap(normalized, original);
    }

    /// <summary>
    /// <c>cs(User-Agent)</c> a <c>cs_user_agent</c>, <c>sc-win32-status</c> a
    /// <c>sc_win32_status</c>. La correspondencia con los nombres W3C es deliberada:
    /// quien conoce los logs de IIS consulta la tabla sin diccionario.
    /// </summary>
    private static string Normalize(string field)
    {
        var builder = new StringBuilder(field.Length);

        foreach (var c in field)
        {
            switch (c)
            {
                case '-':
                case '(':
                    builder.Append('_');
                    break;
                case ')':
                    break;
                default:
                    builder.Append(char.ToLowerInvariant(c));
                    break;
            }
        }

        return builder.ToString();
    }
}
