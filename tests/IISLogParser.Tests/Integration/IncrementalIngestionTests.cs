using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T026 - quickstart escenario 3: al reprocesar, solo entran las lineas nuevas.
/// </summary>
public class IncrementalIngestionTests
{
    [Fact]
    public void LineasAgregadas_SonLasUnicasQueSeIngestan()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);

        workspace.AppendLines(
            "W3SVC1",
            "u_ex260903.log",
            TempWorkspace.DataLine("00:00:10"),
            TempWorkspace.DataLine("00:00:11"),
            TempWorkspace.DataLine("00:00:12"));

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(3, report.RecordsIngested);
        Assert.Equal(4, workspace.CountRows());
    }

    [Fact]
    public void MarcaDeProgreso_AvanzaHastaElFinalDelArchivo()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);

        var size = new FileInfo(workspace.FilePath("W3SVC1", "u_ex260903.log")).Length;
        var ingested = workspace.QueryScalar<long>("SELECT bytes_ingested FROM file_progress;");

        Assert.Equal(size, ingested);
    }

    [Fact]
    public void RegistrosPrevios_NoSeTocan()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);
        var firstId = workspace.QueryScalar<long>("SELECT id FROM log_entries;");

        workspace.AppendLines("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("00:00:10"));
        Harness.RunSnapshot(workspace);

        Assert.Equal(firstId, workspace.QueryScalar<long>("SELECT MIN(id) FROM log_entries;"));
    }
}
