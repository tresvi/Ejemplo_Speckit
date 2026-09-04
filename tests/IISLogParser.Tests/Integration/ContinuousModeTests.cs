using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T044 y T045 - el ciclo ingesta lo nuevo y nada mas; un ciclo sin cambios no escribe.
/// </summary>
public class ContinuousModeTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void LineasAgregadas_AparecenEnElCicloSiguiente()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        workspace.AppendLines(
            "W3SVC1",
            "u_ex260903.log",
            TempWorkspace.DataLine("00:10:01"),
            TempWorkspace.DataLine("00:10:02"),
            TempWorkspace.DataLine("00:10:03"));

        var report = harness.Cycle();

        Assert.Equal(3, report.RecordsIngested);
        Assert.Equal(4, workspace.CountRows());
    }

    [Fact]
    public void LineasAgregadas_SeAtribuyenASuSitio()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        workspace.AppendLines("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine("00:10:01"));
        harness.Cycle();

        Assert.Equal(
            2,
            workspace.QueryScalar<long>("SELECT COUNT(*) FROM log_entries WHERE site_id = 'W3SVC1';"));
        Assert.Equal(
            1,
            workspace.QueryScalar<long>("SELECT COUNT(*) FROM log_entries WHERE site_id = 'W3SVC2';"));
    }

    [Fact]
    public void CicloSinCambios_NoEscribeNingunRegistro()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        var second = harness.Cycle();
        var third = harness.Cycle();

        Assert.Equal(0, second.RecordsIngested);
        Assert.Equal(0, third.RecordsIngested);
        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void CicloSinNovedades_IgualEmiteSuLineaDeResumen()
    {
        // El silencio no debe ser ambiguo respecto de un proceso colgado.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();
        harness.Cycle();

        var summaries = harness.Output.ToString()
            .Split('\n')
            .Count(l => l.Contains("Archivos:", StringComparison.Ordinal));

        Assert.Equal(2, summaries);
    }
}
