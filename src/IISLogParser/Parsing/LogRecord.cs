namespace IISLogParser.Parsing;

/// <summary>
/// Columnas del esquema fijo, en el orden en que viven en la tabla
/// (contracts/database-schema.md). El orden importa: el almacén arma su comando
/// preparado a partir de este arreglo, de modo que agregar una columna acá y en el
/// DDL alcanza para que viaje hasta la base.
/// </summary>
public static class LogColumns
{
    public static readonly string[] Names =
    [
        "timestamp_utc",
        "s_ip",
        "cs_method",
        "cs_uri_stem",
        "cs_uri_query",
        "s_port",
        "cs_username",
        "c_ip",
        "cs_version",
        "cs_user_agent",
        "cs_referer",
        "cs_cookie",
        "cs_host",
        "sc_status",
        "sc_substatus",
        "sc_win32_status",
        "sc_bytes",
        "cs_bytes",
        "time_taken",
        "s_sitename",
        "s_computername",
    ];

    /// <summary>Columnas que la base declara como INTEGER.</summary>
    private static readonly HashSet<string> IntegerColumns =
    [
        "s_port",
        "sc_status",
        "sc_substatus",
        "sc_win32_status",
        "sc_bytes",
        "cs_bytes",
        "time_taken",
    ];

    private static readonly Dictionary<string, int> IndexByName =
        Names.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);

    public static int Count => Names.Length;

    /// <summary>Índice de la columna, o -1 si el campo no pertenece al esquema fijo.</summary>
    public static int IndexOf(string normalizedField) =>
        IndexByName.TryGetValue(normalizedField, out var index) ? index : -1;

    public static bool IsInteger(int index) => IntegerColumns.Contains(Names[index]);
}

/// <summary>
/// Una línea de petición parseada. Los tres componentes de identidad
/// (<see cref="SiteId"/>, <see cref="SourceFile"/>, <see cref="LineOffset"/>) los
/// completa el ingestor, que es quien sabe de qué archivo y de qué posición vino.
/// </summary>
public sealed class LogRecord
{
    private readonly string?[] _values = new string?[LogColumns.Count];
    private Dictionary<string, string>? _extras;

    public string SiteId { get; set; } = string.Empty;

    public string SourceFile { get; set; } = string.Empty;

    public long LineOffset { get; set; }

    /// <summary>Valor de una columna del esquema fijo, o null si el sitio no la registra.</summary>
    public string? this[string column]
    {
        get
        {
            var index = LogColumns.IndexOf(column);
            return index < 0 ? null : _values[index];
        }
    }

    public string? ValueAt(int columnIndex) => _values[columnIndex];

    /// <summary>Campos que el archivo declaró y el esquema fijo no contempla (FR-009a).</summary>
    public IReadOnlyDictionary<string, string> Extras =>
        _extras ?? (IReadOnlyDictionary<string, string>)EmptyExtras;

    public bool HasExtras => _extras is { Count: > 0 };

    private static readonly Dictionary<string, string> EmptyExtras = [];

    internal void SetValue(int columnIndex, string? value) => _values[columnIndex] = value;

    internal void AddExtra(string originalFieldName, string value)
    {
        _extras ??= new Dictionary<string, string>(StringComparer.Ordinal);
        _extras[originalFieldName] = value;
    }
}
