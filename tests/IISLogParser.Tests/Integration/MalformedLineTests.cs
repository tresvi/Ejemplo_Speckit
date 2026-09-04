using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T029 - quickstart escenario 6 y FR-005: la linea mala se descarta y se cuenta,
/// pero no aborta el procesamiento del archivo.
/// </summary>
public class MalformedLineTests
{
    [Fact]
    public void LineaConMenosValores_SeRechazaYLasValidasSeIngestan()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog(
            "W3SVC1",
            "u_ex260903.log",
            TempWorkspace.DataLine("00:00:01"),
            "2026-09-03 00:00:02 incompleta",
            TempWorkspace.DataLine("00:00:03"));

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(2, report.RecordsIngested);
        Assert.Equal(1, report.LinesRejected);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void LineaRechazada_SeReportaConSuContenidoIntegro()
    {
        // FR-005: ningun campo de los logs se clasifica como PII en este proyecto,
        // asi que el contenido va entero, sin enmascarar.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", "2026-09-03 00:00:02 incompleta");

        var (_, output) = Harness.RunSnapshot(workspace);

        Assert.Contains("incompleta", output, StringComparison.Ordinal);
        Assert.Contains("W3SVC1/u_ex260903.log", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LineaConMasValores_SeRechaza()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine() + " sobrante");

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(0, report.RecordsIngested);
        Assert.Equal(1, report.LinesRejected);
    }

    [Fact]
    public void LineaDeDatosSinCabecera_SeRechaza()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");
        System.IO.File.WriteAllText(
            workspace.FilePath("W3SVC1", "u_ex260903.log"),
            "2026-09-03 00:00:02 10.0.0.4 GET /a.html\n");

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(0, report.RecordsIngested);
        Assert.Equal(1, report.LinesRejected);
    }

    [Fact]
    public void CambioDeCabeceraAMitadDeArchivo_SeRespeta()
    {
        // FR-003: IIS reescribe #Fields: cuando cambia la configuracion de logging
        // sin reiniciar el sitio.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.AppendLines(
            "W3SVC1",
            "u_ex260903.log",
            "#Fields: cs-method cs-uri-stem",
            "POST /api/items");

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(2, report.RecordsIngested);
        Assert.Equal(0, report.LinesRejected);
        Assert.Equal(
            1,
            workspace.QueryScalar<long>("SELECT COUNT(*) FROM log_entries WHERE cs_method = 'POST';"));
    }
}
