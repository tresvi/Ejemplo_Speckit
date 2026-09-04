using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T050 — los ciclos nunca se solapan. Esperar después del ciclo, en vez de disparar
/// por reloj, impide que se acumulen cuando uno tarda más que el intervalo (D-009).
/// </summary>
public class CycleOverlapTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    private sealed class TracingWaiter(List<string> trace, int maxWaits, CancellationTokenSource cancellation)
        : ICycleWaiter
    {
        private int _waits;

        public void Wait(TimeSpan interval, CancellationToken cancellationToken)
        {
            trace.Add("wait");

            if (++_waits >= maxWaits)
            {
                cancellation.Cancel();
            }
        }
    }

    [Fact]
    public void LaEsperaOcurreDespuesDelCiclo_NoAntes()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var trace = new List<string>();
        using var cancellation = new CancellationTokenSource();

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Runner.CycleCompleted += () => trace.Add("cycle");
        harness.Runner.Run(
            harness.LogsPath,
            TimeSpan.FromSeconds(10),
            cancellation.Token,
            new TracingWaiter(trace, maxWaits: 3, cancellation));

        Assert.Equal(["cycle", "wait", "cycle", "wait", "cycle", "wait"], trace);
    }

    [Fact]
    public void CadaCicloTerminaAntesDeQueEmpieceElSiguiente()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var concurrent = 0;
        var maxConcurrent = 0;
        using var cancellation = new CancellationTokenSource();

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Runner.CycleStarted += () =>
        {
            concurrent++;
            maxConcurrent = Math.Max(maxConcurrent, concurrent);
        };
        harness.Runner.CycleCompleted += () => concurrent--;

        harness.Runner.Run(
            harness.LogsPath,
            TimeSpan.FromSeconds(10),
            cancellation.Token,
            new CountingWaiter(5, cancellation));

        Assert.Equal(1, maxConcurrent);
    }
}
