using System.Globalization;

namespace IISLogParser.Cli;

/// <summary>Resultado del análisis de argumentos: opciones, error o pedido de ayuda.</summary>
public sealed record CliParseResult(CliOptions? Options, string? Error, bool HelpRequested)
{
    public static CliParseResult Success(CliOptions options) => new(options, null, false);

    public static CliParseResult Failure(string error) => new(null, error, false);

    public static CliParseResult Help() => new(null, null, true);

    /// <summary>
    /// Un error de argumentos es siempre código 1. El resto de los códigos los
    /// determina la corrida, no el análisis.
    /// </summary>
    public ExitCode ToExitCode() =>
        Error is null ? ExitCode.Success : ExitCode.InvalidConfiguration;
}

/// <summary>
/// Análisis de la línea de comandos. Función pura: no consulta el sistema de
/// archivos ni el entorno. La existencia de la carpeta de logs se valida en la
/// composición del programa, donde hay un <c>IFileSystem</c> para preguntarle.
/// </summary>
public static class CliParser
{
    private const string ModeOption = "--mode";
    private const string PollIntervalOption = "--poll-interval";
    private const string LogsPathOption = "--logs-path";
    private const string DatabaseOption = "--database";
    private const string HelpOption = "--help";

    public static CliParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Any(a => string.Equals(a, HelpOption, StringComparison.OrdinalIgnoreCase)))
        {
            return CliParseResult.Help();
        }

        var mode = CliDefaults.Mode;
        var pollInterval = CliDefaults.PollIntervalSeconds;
        var logsPath = CliDefaults.LogsPath;
        var databasePath = CliDefaults.DatabasePath;
        var pollIntervalExplicit = false;

        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];

            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                return CliParseResult.Failure(
                    $"Argumento posicional no esperado: '{option}'. Todas las opciones llevan el prefijo '--'.");
            }

            if (i + 1 >= args.Length)
            {
                return CliParseResult.Failure($"La opcion '{option}' requiere un valor.");
            }

            var value = args[++i];

            switch (option.ToLowerInvariant())
            {
                case ModeOption:
                    if (!TryParseMode(value, out mode))
                    {
                        return CliParseResult.Failure(
                            $"Modo desconocido: '{value}'. Valores validos: snapshot, continuous.");
                    }

                    break;

                case PollIntervalOption:
                    if (!TryParsePollInterval(value, out pollInterval))
                    {
                        return CliParseResult.Failure(
                            $"Intervalo de sondeo invalido: '{value}'. Debe ser un entero mayor que cero.");
                    }

                    pollIntervalExplicit = true;
                    break;

                case LogsPathOption:
                    logsPath = value;
                    break;

                case DatabaseOption:
                    databasePath = value;
                    break;

                default:
                    return CliParseResult.Failure($"Opcion desconocida: '{option}'.");
            }
        }

        return CliParseResult.Success(
            new CliOptions(mode, pollInterval, logsPath, databasePath, pollIntervalExplicit));
    }

    private static bool TryParseMode(string value, out RunMode mode) =>
        Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode);

    private static bool TryParsePollInterval(string value, out int seconds)
    {
        seconds = CliDefaults.PollIntervalSeconds;

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed <= 0)
        {
            return false;
        }

        seconds = parsed;
        return true;
    }
}
