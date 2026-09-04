using IISLogParser.Cli;
using IISLogParser.Diagnostics;
using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace IISLogParser.Tests.Contract;

/// <summary>
/// T076 — el código de salida 3 del contrato existía pero ningún test lo ejercitaba.
/// </summary>
public class StoreFailureTests
{
    [Fact]
    public void AlmacenQueNoSePuedeAbrir_TerminaConCodigoTres()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        // Un directorio que no existe: SQLite no puede crear el archivo ahi.
        var unreachable = Path.Combine(workspace.Root, "no-existe", "iislogs.db");
        var stdout = new StringWriter();
        var app = new Application(new FileSystem(), stdout, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(
            ["--mode", "snapshot", "--logs-path", workspace.LogsPath, "--database", unreachable],
            CancellationToken.None);

        Assert.Equal((int)ExitCode.StoreFailure, exitCode);
    }

    [Fact]
    public void FalloDeAlmacen_SeInformaConUnMensajeClaro()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var unreachable = Path.Combine(workspace.Root, "no-existe", "iislogs.db");
        var stdout = new StringWriter();
        var app = new Application(new FileSystem(), stdout, new DiagnosticLog(NullLogger.Instance));

        app.Run(
            ["--mode", "snapshot", "--logs-path", workspace.LogsPath, "--database", unreachable],
            CancellationToken.None);

        Assert.Contains("almacen", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FalloDeAlmacen_NoSeConfundeConFalloDeConfiguracion()
    {
        // El codigo 3 y el codigo 1 significan cosas distintas para un planificador.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var unreachable = Path.Combine(workspace.Root, "no-existe", "iislogs.db");
        var stdout = new StringWriter();
        var app = new Application(new FileSystem(), stdout, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(
            ["--mode", "snapshot", "--logs-path", workspace.LogsPath, "--database", unreachable],
            CancellationToken.None);

        Assert.NotEqual((int)ExitCode.InvalidConfiguration, exitCode);
    }
}
