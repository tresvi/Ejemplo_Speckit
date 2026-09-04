using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T043 - FR-014a y quickstart escenario 7: al arrancar se ingesta el archivo del dia
/// completo, y los dias anteriores se dejan en paz.
/// </summary>
public class ContinuousBackfillTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ArchivoDelDiaConContenido_SeIngestaEnElPrimerCiclo()
    {
        using var workspace = new TempWorkspace();
        var lines = Enumerable.Range(0, 50).Select(i => TempWorkspace.DataLine($"00:0{i / 10}:{i % 10:00}")).ToArray();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", lines);

        using var harness = new ContinuousHarness(workspace, Clock());
        var report = harness.Cycle();

        Assert.Equal(50, report.RecordsIngested);
        Assert.Equal(50, workspace.CountRows());
    }

    [Fact]
    public void ArchivosDeDiasAnteriores_NoSeIngestan()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260901.log", TempWorkspace.DataLine(date: "2026-09-01"));
        workspace.WriteLog("W3SVC1", "u_ex260902.log", TempWorkspace.DataLine(date: "2026-09-02"));
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        Assert.Equal(1, workspace.CountRows());
        Assert.Equal(
            "W3SVC1/u_ex260903.log",
            workspace.QueryStrings("SELECT DISTINCT source_file FROM log_entries;")[0]);
    }

    [Fact]
    public void ReanudacionTrasDetencion_IngestaSoloLoEscritoDurante()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using (var first = new ContinuousHarness(workspace, Clock()))
        {
            first.Cycle();
        }

        workspace.AppendLines("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("02:00:00"), TempWorkspace.DataLine("02:00:01"));

        using var second = new ContinuousHarness(workspace, Clock());
        var report = second.Cycle();

        Assert.Equal(2, report.RecordsIngested);
        Assert.Equal(3, workspace.CountRows());
    }
}
