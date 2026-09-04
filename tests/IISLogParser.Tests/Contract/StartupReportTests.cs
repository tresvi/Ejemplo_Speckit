using IISLogParser.Cli;
using IISLogParser.Diagnostics;
using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace IISLogParser.Tests.Contract;

/// <summary>
/// T063 — el bloque de configuración efectiva del FR-021, y T062 — una invocación
/// rechazada no deja rastro en disco (FR-020).
/// </summary>
public class StartupReportTests
{
    [Fact]
    public void BloqueDeArranque_NombraLosCuatroValoresEfectivos()
    {
        var options = new CliOptions(RunMode.Continuous, 10, @"D:\logs", "iislogs.db", false);
        var writer = new StringWriter();

        new OperatorReport(writer).WriteStartup(options, @"D:\db\iislogs.db");
        var startup = writer.ToString();

        Assert.Contains("IISLogParser", startup, StringComparison.Ordinal);
        Assert.Contains("Modo", startup, StringComparison.Ordinal);
        Assert.Contains("continuous", startup, StringComparison.Ordinal);
        Assert.Contains(@"D:\logs", startup, StringComparison.Ordinal);
        Assert.Contains(@"D:\db\iislogs.db", startup, StringComparison.Ordinal);
        Assert.Contains("10 s", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void BloqueDeArranque_ReportaLaRutaResueltaDeLaBase()
    {
        // El operador tiene que ver donde quedo la base, no el valor relativo que paso.
        var options = new CliOptions(RunMode.Snapshot, 10, @"D:\logs", "iislogs.db", false);
        var writer = new StringWriter();

        new OperatorReport(writer).WriteStartup(options, @"C:\trabajo\iislogs.db");

        Assert.Contains(@"C:\trabajo\iislogs.db", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ArgumentosInvalidos_NoCreanLaBaseDeDatos()
    {
        using var workspace = new TempWorkspace();
        var writer = new StringWriter();
        var app = new Application(new FileSystem(), writer, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(
            ["--mode", "turbo", "--logs-path", workspace.LogsPath, "--database", workspace.DatabasePath],
            CancellationToken.None);

        Assert.Equal((int)ExitCode.InvalidConfiguration, exitCode);
        Assert.False(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public void IntervaloInvalido_NoCreaLaBaseDeDatos()
    {
        using var workspace = new TempWorkspace();
        var writer = new StringWriter();
        var app = new Application(new FileSystem(), writer, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(
            ["--poll-interval", "0", "--logs-path", workspace.LogsPath, "--database", workspace.DatabasePath],
            CancellationToken.None);

        Assert.Equal((int)ExitCode.InvalidConfiguration, exitCode);
        Assert.False(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public void CarpetaDeLogsInexistente_NoCreaLaBaseDeDatos()
    {
        using var workspace = new TempWorkspace();
        var writer = new StringWriter();
        var app = new Application(new FileSystem(), writer, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(
            [
                "--mode", "snapshot",
                "--logs-path", Path.Combine(workspace.Root, "no-existe"),
                "--database", workspace.DatabasePath,
            ],
            CancellationToken.None);

        Assert.Equal((int)ExitCode.InvalidConfiguration, exitCode);
        Assert.False(File.Exists(workspace.DatabasePath));
    }

    [Fact]
    public void Help_NoCreaLaBaseDeDatosYSaleConCero()
    {
        using var workspace = new TempWorkspace();
        var writer = new StringWriter();
        var app = new Application(new FileSystem(), writer, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(["--help", "--database", workspace.DatabasePath], CancellationToken.None);

        Assert.Equal((int)ExitCode.Success, exitCode);
        Assert.False(File.Exists(workspace.DatabasePath));
        Assert.Contains("--poll-interval", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotValido_EmiteElBloqueDeArranqueAntesDelResumen()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var writer = new StringWriter();
        var app = new Application(new FileSystem(), writer, new DiagnosticLog(NullLogger.Instance));

        var exitCode = app.Run(
            ["--mode", "snapshot", "--logs-path", workspace.LogsPath, "--database", workspace.DatabasePath],
            CancellationToken.None);

        var output = writer.ToString();
        var startupIndex = output.IndexOf("Carpeta de logs", StringComparison.Ordinal);
        var summaryIndex = output.IndexOf("Archivos:", StringComparison.Ordinal);

        Assert.Equal((int)ExitCode.Success, exitCode);
        Assert.True(startupIndex >= 0);
        Assert.True(startupIndex < summaryIndex);
    }
}
