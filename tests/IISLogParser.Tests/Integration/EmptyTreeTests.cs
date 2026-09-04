using IISLogParser.Cli;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T077 — US1/AC2 pide que una carpeta vacía informe "que no encontró archivos que
/// procesar". Una línea de resumen con ceros lo dice de forma implícita; el escenario
/// pide decirlo.
/// </summary>
public class EmptyTreeTests
{
    [Fact]
    public void CarpetaSinSitios_InformaQueNoEncontroArchivos()
    {
        using var workspace = new TempWorkspace();

        var (report, output) = Harness.RunSnapshot(workspace);

        Assert.Equal(0, report.FilesProcessed);
        Assert.Equal(ExitCode.Success, report.ToExitCode());
        Assert.Contains("No se encontro", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SitiosSinArchivos_InformanQueNoEncontroArchivos()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");
        workspace.CreateSite("W3SVC2");

        var (_, output) = Harness.RunSnapshot(workspace);

        Assert.Contains("No se encontro", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConArchivosProcesados_NoAparecatElMensajeDeVacio()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var (_, output) = Harness.RunSnapshot(workspace);

        Assert.DoesNotContain("No se encontro", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MensajeDeVacio_NoRompeElConteoDeResumenes()
    {
        using var workspace = new TempWorkspace();

        var (_, output) = Harness.RunSnapshot(workspace);

        var summaries = output.Split('\n').Count(l => l.Contains("Archivos:", StringComparison.Ordinal));

        Assert.Equal(1, summaries);
    }
}
