namespace IISLogParser.Cli;

/// <summary>Modo de ejecución de la herramienta.</summary>
public enum RunMode
{
    /// <summary>Procesa todo lo que encuentra y termina.</summary>
    Snapshot,

    /// <summary>Queda vigilando los logs del día hasta que el operador interrumpe.</summary>
    Continuous,
}

/// <summary>Valores por defecto del contrato de CLI (contracts/cli.md).</summary>
public static class CliDefaults
{
    public const RunMode Mode = RunMode.Continuous;
    public const int PollIntervalSeconds = 10;
    public const string LogsPath = @"C:\inetpub\logs\LogFiles";
    public const string DatabasePath = "iislogs.db";
}

/// <summary>Configuración efectiva con la que arranca una corrida.</summary>
/// <param name="PollIntervalExplicit">
/// El operador pasó <c>--poll-interval</c> a mano. Solo se usa para el reporte de
/// configuración: en modo snapshot el valor se acepta y se informa como ignorado.
/// </param>
public sealed record CliOptions(
    RunMode Mode,
    int PollIntervalSeconds,
    string LogsPath,
    string DatabasePath,
    bool PollIntervalExplicit);
