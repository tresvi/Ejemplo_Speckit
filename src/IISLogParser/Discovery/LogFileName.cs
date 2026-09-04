using System.Globalization;
using System.Text.RegularExpressions;

namespace IISLogParser.Discovery;

/// <summary>
/// Fecha de un archivo de log derivada de su nombre. IIS usa <c>u_exYYMMDD.log</c>
/// cuando nombra y rota en UTC (predeterminado) y <c>exYYMMDD.log</c> con hora local.
/// Un nombre que no encaja no puede fecharse de forma confiable, y por lo tanto no
/// puede considerarse "de hoy" (D-005).
/// </summary>
public static partial class LogFileName
{
    [GeneratedRegex(@"^u?_?ex(\d{6})\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DailyPattern();

    public static bool TryGetDate(string path, out DateOnly date)
    {
        date = default;

        var name = Path.GetFileName(path);
        var match = DailyPattern().Match(name);

        if (!match.Success)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            match.Groups[1].Value,
            "yyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
