using System.Text;
using IISLogParser.Cli;
using IISLogParser.Diagnostics;
using IISLogParser.Ingestion;

Console.OutputEncoding = Encoding.UTF8;

using var loggerFactory = LoggingSetup.Create();
var diagnostics = LoggingSetup.CreateDiagnosticLog(loggerFactory);

using var cancellation = new CancellationTokenSource();

// FR-017: el apagado es ordenado. Se cancela el token y el ciclo en curso confirma
// su lote antes de salir; nunca se aborta el proceso a mitad de una transaccion.
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var application = new Application(new FileSystem(), Console.Out, diagnostics);

try
{
    return application.Run(args, cancellation.Token);
}
catch (OperationCanceledException)
{
    diagnostics.Shutdown("cancelled");
    return (int)ExitCode.Success;
}
