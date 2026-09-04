using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T046 - FR-016: un archivo del dia que aparece despues del arranque se detecta sin
/// reiniciar el proceso.
/// </summary>
public class ContinuousDiscoveryTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ArchivoCreadoDespuesDelArranque_SeDetectaEnElCicloSiguiente()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine("00:05:00"));
        var report = harness.Cycle();

        Assert.Equal(1, report.RecordsIngested);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void SitioCreadoDespuesDelArranque_SeDetecta()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        workspace.WriteLog("W3SVC9", "u_ex260903.log", TempWorkspace.DataLine("00:05:00"));
        harness.Cycle();

        Assert.Contains("W3SVC9", workspace.QueryStrings("SELECT DISTINCT site_id FROM log_entries;"));
    }

    [Fact]
    public void SinArchivosDelDia_ElCicloTerminaSinError()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");

        using var harness = new ContinuousHarness(workspace, Clock());
        var report = harness.Cycle();

        Assert.Equal(0, report.FilesProcessed);
        Assert.Equal(0, report.RecordsIngested);
    }
}
