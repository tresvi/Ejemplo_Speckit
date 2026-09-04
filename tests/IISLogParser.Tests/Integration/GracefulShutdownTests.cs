using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T048 — FR-017. Como datos y progreso viajan en la misma transacción, atender la
/// cancelación en el borde del lote alcanza: no hace falta lógica de compensación.
/// </summary>
public class GracefulShutdownTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void BucleCancelado_TerminaSinExcepcion()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var cancellation = new CancellationTokenSource();
        var waiter = new CountingWaiter(maxWaits: 3, cancellation);

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Runner.Run(harness.LogsPath, TimeSpan.FromSeconds(10), cancellation.Token, waiter);

        Assert.Equal(3, waiter.Waits);
    }

    [Fact]
    public void TrasLaCancelacion_LoIngestadoQuedaConfirmado()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));

        using var cancellation = new CancellationTokenSource();
        var waiter = new CountingWaiter(maxWaits: 1, cancellation);

        using (var harness = new ContinuousHarness(workspace, Clock()))
        {
            harness.Runner.Run(harness.LogsPath, TimeSpan.FromSeconds(10), cancellation.Token, waiter);
        }

        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void CorridaPosteriorAlCierre_NoDuplicaNada()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var cancellation = new CancellationTokenSource();
        var waiter = new CountingWaiter(maxWaits: 1, cancellation);

        using (var harness = new ContinuousHarness(workspace, Clock()))
        {
            harness.Runner.Run(harness.LogsPath, TimeSpan.FromSeconds(10), cancellation.Token, waiter);
        }

        using var second = new ContinuousHarness(workspace, Clock());
        var report = second.Cycle();

        Assert.Equal(0, report.RecordsIngested);
        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void CancelacionAntesDelPrimerCiclo_NoEscribeNada()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Runner.Run(
            harness.LogsPath,
            TimeSpan.FromSeconds(10),
            cancellation.Token,
            new CountingWaiter(99, cancellation));

        Assert.Equal(0, workspace.CountRows());
    }
}
