using IISLogParser.Cli;
using IISLogParser.Diagnostics;
using IISLogParser.Discovery;
using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T072, T073 y T074 — hallazgos F1, F2 y F3 de la convergencia. La carpeta raíz puede
/// volverse ilegible entre el chequeo de existencia y la enumeración, o desaparecer a
/// mitad de una corrida larga si es un recurso de red. Eso no puede terminar en un stack
/// trace, ni matar el proceso continuo, ni reportarse como un fallo del almacén.
/// </summary>
public class LogsRootFailureTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    private static int RunApplication(TempWorkspace workspace, IFileSystem fs, TextWriter stdout) =>
        new Application(fs, stdout, new DiagnosticLog(NullLogger.Instance)).Run(
            ["--mode", "snapshot", "--logs-path", workspace.LogsPath, "--database", workspace.DatabasePath],
            CancellationToken.None);

    [Fact]
    public void RaizIlegibleEnSnapshot_TerminaConCodigoUnoYNoConUnaExcepcion()
    {
        // F1: hasta la convergencia, esta excepcion escapaba de todos los catch.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new UnauthorizedAccessException("acceso denegado a la raiz"));
        var stdout = new StringWriter();

        var exitCode = RunApplication(workspace, fs, stdout);

        Assert.Equal((int)ExitCode.InvalidConfiguration, exitCode);
        Assert.Contains("acceso denegado a la raiz", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RaizIlegiblePorEsDeRed_NoSeReportaComoFalloDeAlmacen()
    {
        // F3: una IOException sobre la carpeta de logs caia en el catch del almacen y
        // salia con codigo 3 diciendo "Fallo del almacen de datos".
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new IOException("el recurso de red no responde"));
        var stdout = new StringWriter();

        var exitCode = RunApplication(workspace, fs, stdout);

        Assert.Equal((int)ExitCode.InvalidConfiguration, exitCode);
        Assert.DoesNotContain("almacen", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RaizIlegible_NombraLaCarpetaEnElMensaje()
    {
        using var workspace = new TempWorkspace();
        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new UnauthorizedAccessException("denegado"));
        var stdout = new StringWriter();

        RunApplication(workspace, fs, stdout);

        Assert.Contains(workspace.LogsPath, stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DescubrimientoDeSitios_TraduceElFalloDeRaizAUnTipoPropio()
    {
        using var workspace = new TempWorkspace();
        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new UnauthorizedAccessException("denegado"));

        var exception = Assert.Throws<LogsRootUnavailableException>(
            () => new SiteDiscovery(fs).DiscoverSites(workspace.LogsPath));

        Assert.Equal(workspace.LogsPath, exception.LogsRoot);
        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }

    [Fact]
    public void RaizIlegibleEnModoContinuo_NoMataElCiclo()
    {
        // F2: el bucle tiene que sobrevivir y reintentar, no propagar la excepcion.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new IOException("el recurso de red no responde"));
        using var harness = new ContinuousHarness(workspace, Clock(), fs);

        var report = harness.Cycle();

        Assert.Single(report.SkippedPaths);
        Assert.Equal(0, report.FilesProcessed);
    }

    [Fact]
    public void RaizQueVuelve_SeIngestaEnElCicloSiguiente()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new IOException("el recurso de red no responde"));
        using var harness = new ContinuousHarness(workspace, Clock(), fs);

        harness.Cycle();
        fs.Failing = false;
        var second = harness.Cycle();

        Assert.Empty(second.SkippedPaths);
        Assert.Equal(1, second.RecordsIngested);
        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void RaizIlegibleEnModoContinuo_SeReportaConSuCausa()
    {
        using var workspace = new TempWorkspace();
        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new IOException("el recurso de red no responde"));
        using var harness = new ContinuousHarness(workspace, Clock(), fs);

        harness.Cycle();

        Assert.Contains("OMITIDO", harness.Output.ToString(), StringComparison.Ordinal);
        Assert.Contains("no responde", harness.Output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BucleContinuoConRaizCaida_SigueCorriendoTodosLosCiclos()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new RootFailingFileSystem(
            new FileSystem(), workspace.LogsPath, new UnauthorizedAccessException("denegado"));
        using var cancellation = new CancellationTokenSource();
        var waiter = new CountingWaiter(maxWaits: 3, cancellation);

        using var harness = new ContinuousHarness(workspace, Clock(), fs);
        harness.Runner.Run(harness.LogsPath, TimeSpan.FromSeconds(10), cancellation.Token, waiter);

        Assert.Equal(3, waiter.Waits);
    }
}
