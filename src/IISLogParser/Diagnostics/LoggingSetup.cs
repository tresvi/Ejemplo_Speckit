using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace IISLogParser.Diagnostics;

/// <summary>
/// Arma el log de diagnóstico. Va a stderr, separado del reporte legible que el
/// operador lee en stdout: un mismo flujo sirviendo a las dos cosas termina sirviendo
/// mal a ambas, y separar por descriptor permite redirigir el JSON a un colector sin
/// ensuciar la salida humana (D-012).
/// </summary>
public static class LoggingSetup
{
    public static ILoggerFactory Create(LogLevel minimumLevel = LogLevel.Information) =>
        LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(minimumLevel)
                .AddConsole(options =>
                {
                    options.FormatterName = JsonEventFormatter.FormatterName;

                    // Todo el log estructurado sale por stderr.
                    options.LogToStandardErrorThreshold = LogLevel.Trace;
                })
                .AddConsoleFormatter<JsonEventFormatter, ConsoleFormatterOptions>();
        });

    public static DiagnosticLog CreateDiagnosticLog(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new DiagnosticLog(factory.CreateLogger("IISLogParser"));
    }
}
