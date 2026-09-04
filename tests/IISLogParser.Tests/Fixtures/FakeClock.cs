using IISLogParser.Ingestion;

namespace IISLogParser.Tests.Fixtures;

/// <summary>
/// Reloj inyectable. Sin el, testear el cambio de dia exigiria esperar a la medianoche
/// y el modo continuo exigiria esperas reales: la suite seria lenta y susceptible a
/// fallos intermitentes (D-013).
/// </summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public void AdvanceDays(int days) => UtcNow = UtcNow.AddDays(days);
}

/// <summary>Espera que no espera: cuenta los ciclos y cancela al llegar al limite.</summary>
public sealed class CountingWaiter(int maxWaits, CancellationTokenSource cancellation) : ICycleWaiter
{
    public int Waits { get; private set; }

    public List<string> Trace { get; } = [];

    public void Wait(TimeSpan interval, CancellationToken cancellationToken)
    {
        Waits++;
        Trace.Add($"wait:{interval.TotalSeconds}");

        if (Waits >= maxWaits)
        {
            cancellation.Cancel();
        }
    }
}
