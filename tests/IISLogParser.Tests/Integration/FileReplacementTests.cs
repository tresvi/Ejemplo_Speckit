using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T028 - quickstart escenario 5, FR-011a e invariante I-04. Sin el borrado previo,
/// los offsets del contenido viejo colisionarian con los del nuevo y el INSERT OR
/// IGNORE descartaria filas legitimas.
/// </summary>
public class FileReplacementTests
{
    [Fact]
    public void ArchivoMasCorto_PurgaLasFilasViejasYReingesta()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog(
            "W3SVC1",
            "u_ex260903.log",
            TempWorkspace.DataLine("00:00:01"),
            TempWorkspace.DataLine("00:00:02"),
            TempWorkspace.DataLine("00:00:03"),
            TempWorkspace.DataLine("00:00:04"));

        Harness.RunSnapshot(workspace);
        Assert.Equal(4, workspace.CountRows());

        workspace.ReplaceWithShorter("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("23:59:59", stem: "/nuevo"));
        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(1, report.RecordsIngested);
        Assert.Equal(1, workspace.CountRows());
        Assert.Equal("/nuevo", workspace.QueryStrings("SELECT cs_uri_stem FROM log_entries;")[0]);
    }

    [Fact]
    public void ArchivoReemplazado_NoDejaRestosDelContenidoAnterior()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(stem: "/viejo"), TempWorkspace.DataLine(stem: "/viejo"));
        Harness.RunSnapshot(workspace);

        workspace.ReplaceWithShorter("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(stem: "/nuevo"));
        Harness.RunSnapshot(workspace);

        Assert.Empty(workspace.QueryStrings("SELECT cs_uri_stem FROM log_entries WHERE cs_uri_stem = '/viejo';"));
    }

    [Fact]
    public void ArchivoReemplazado_NoTocaLasFilasDeOtrosArchivos()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260902.log", TempWorkspace.DataLine(date: "2026-09-02"));
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));
        Harness.RunSnapshot(workspace);

        workspace.ReplaceWithShorter("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("23:59:59"));
        Harness.RunSnapshot(workspace);

        Assert.Equal(
            1,
            workspace.QueryScalar<long>("SELECT COUNT(*) FROM log_entries WHERE source_file = 'W3SVC1/u_ex260902.log';"));
    }
}
