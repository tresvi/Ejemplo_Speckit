using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T047 — FR-016 y escenario 4 de la historia P2. El cambio de día se resuelve solo:
/// "el archivo de hoy" simplemente es otro, y el de ayer sigue vigilado mientras le
/// queden bytes pendientes (D-009).
/// </summary>
public class DayRolloverTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 23, 59, 0, TimeSpan.Zero));

    [Fact]
    public void AlCambiarElDia_SeVigilaElArchivoNuevo()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("23:59:01"));

        var clock = Clock();
        using var harness = new ContinuousHarness(workspace, clock);
        harness.Cycle();

        clock.AdvanceDays(1);
        workspace.WriteLog("W3SVC1", "u_ex260904.log", TempWorkspace.DataLine("00:00:01", date: "2026-09-04"));
        var report = harness.Cycle();

        Assert.Equal(1, report.RecordsIngested);
        Assert.Contains(
            "W3SVC1/u_ex260904.log",
            workspace.QueryStrings("SELECT DISTINCT source_file FROM log_entries;"));
    }

    [Fact]
    public void AlCambiarElDia_NoSePierdeLaColaDelArchivoAnterior()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("23:59:01"));

        var clock = Clock();
        using var harness = new ContinuousHarness(workspace, clock);
        harness.Cycle();

        // IIS escribe las ultimas lineas del dia justo antes de rotar.
        workspace.AppendLines(
            "W3SVC1",
            "u_ex260903.log",
            TempWorkspace.DataLine("23:59:58"),
            TempWorkspace.DataLine("23:59:59"));
        clock.AdvanceDays(1);
        workspace.WriteLog("W3SVC1", "u_ex260904.log", TempWorkspace.DataLine("00:00:01", date: "2026-09-04"));

        var report = harness.Cycle();

        Assert.Equal(3, report.RecordsIngested);
        Assert.Equal(
            3,
            workspace.QueryScalar<long>(
                "SELECT COUNT(*) FROM log_entries WHERE source_file = 'W3SVC1/u_ex260903.log';"));
    }

    [Fact]
    public void ArchivoDelDiaAnteriorYaCompleto_DejaDeVigilarse()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("23:59:01"));

        var clock = Clock();
        using var harness = new ContinuousHarness(workspace, clock);
        harness.Cycle();

        clock.AdvanceDays(1);
        workspace.WriteLog("W3SVC1", "u_ex260904.log", TempWorkspace.DataLine("00:00:01", date: "2026-09-04"));
        harness.Cycle();

        var report = harness.Cycle();

        Assert.Equal(1, report.FilesProcessed);
    }
}
