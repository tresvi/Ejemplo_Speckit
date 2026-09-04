using IISLogParser.Cli;
using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T030 - quickstart escenario 8, FR-023 a FR-025 y SC-007. Un snapshot que omitio
/// archivos y devuelve cero es una trampa: un planificador lo da por bueno.
/// </summary>
public class PartialFailureTests
{
    [Fact]
    public void ArchivoIlegible_SeOmiteYElRestoSeIngestaCompleto()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new FailingFileSystem(new FileSystem(), "W3SVC2", "acceso denegado");
        var (report, _) = Harness.RunSnapshot(workspace, fs);

        Assert.Equal(2, report.RecordsIngested);
        Assert.Single(report.SkippedPaths);
    }

    [Fact]
    public void ArchivoIlegible_ProduceCodigoDeSalidaDos()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new FailingFileSystem(new FileSystem(), "W3SVC2", "acceso denegado");
        var (report, _) = Harness.RunSnapshot(workspace, fs);

        Assert.Equal(ExitCode.CompletedWithSkips, report.ToExitCode());
    }

    [Fact]
    public void OmisionSeReportaConSuCausa()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new FailingFileSystem(new FileSystem(), "W3SVC2", "acceso denegado");
        var (_, output) = Harness.RunSnapshot(workspace, fs);

        Assert.Contains("OMITIDO", output, StringComparison.Ordinal);
        Assert.Contains("acceso denegado", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OmisionSeCuentaEnElResumen()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new FailingFileSystem(new FileSystem(), "W3SVC2", "acceso denegado");
        var (_, output) = Harness.RunSnapshot(workspace, fs);

        Assert.Contains("Omitidos: 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CarpetaDeSitioIlegible_SeOmiteSinAbortarElRecorrido()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.CreateSite("W3SVC2");

        var fs = new FailingFileSystem(new FileSystem(), "W3SVC2", "carpeta bloqueada");
        var (report, _) = Harness.RunSnapshot(workspace, fs);

        Assert.Equal(1, report.RecordsIngested);
        Assert.Single(report.SkippedPaths);
        Assert.Equal(ExitCode.CompletedWithSkips, report.ToExitCode());
    }

    [Fact]
    public void SinOmisiones_ElCodigoEsCero()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Empty(report.SkippedPaths);
        Assert.Equal(ExitCode.Success, report.ToExitCode());
    }
}
