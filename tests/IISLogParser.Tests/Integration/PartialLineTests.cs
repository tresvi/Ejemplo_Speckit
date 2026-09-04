using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T027 - quickstart escenario 4 e invariante I-03. Es el escenario que rompe una
/// implementacion ingenua que lea hasta fin de archivo.
/// </summary>
public class PartialLineTests
{
    [Fact]
    public void LineaSinTerminador_NoSeIngesta()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.AppendRaw("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("00:00:10"));

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(1, report.RecordsIngested);
        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void LineaCompletadaDespues_SeIngestaExactamenteUnaVez()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.AppendRaw("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("00:00:10"));

        Harness.RunSnapshot(workspace);

        workspace.AppendRaw("W3SVC1", "u_ex260903.log", "\n");
        var (second, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(1, second.RecordsIngested);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void MarcaDeProgreso_NuncaApuntaAMediaLinea()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        var completeSize = new FileInfo(workspace.FilePath("W3SVC1", "u_ex260903.log")).Length;

        workspace.AppendRaw("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("00:00:10"));
        Harness.RunSnapshot(workspace);

        Assert.Equal(completeSize, workspace.QueryScalar<long>("SELECT bytes_ingested FROM file_progress;"));
    }

    [Fact]
    public void CabeceraIncompleta_NoBloqueaLaIngestaPosterior()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");
        System.IO.File.WriteAllText(
            workspace.FilePath("W3SVC1", "u_ex260903.log"),
            "#Software: IIS\n#Fields: date time cs-method");

        var (first, _) = Harness.RunSnapshot(workspace);
        Assert.Equal(0, first.RecordsIngested);

        workspace.AppendRaw("W3SVC1", "u_ex260903.log", "\n2026-09-03 00:00:02 GET\n");
        var (second, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(1, second.RecordsIngested);
    }
}
