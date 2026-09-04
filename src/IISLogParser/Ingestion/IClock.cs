namespace IISLogParser.Ingestion;

/// <summary>
/// Reloj inyectable. Sin él, testear el cambio de día exigiría esperar a la medianoche
/// real (D-013).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <inheritdoc />
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Espera entre ciclos. Inyectable para que la suite ejecute N ciclos de forma
/// determinista en vez de esperar segundos reales.
/// </summary>
public interface ICycleWaiter
{
    void Wait(TimeSpan interval, CancellationToken cancellationToken);
}

/// <summary>
/// Espera real, interrumpible: la cancelación corta la espera en el acto, de modo que
/// Ctrl+C no queda pendiente hasta el final del intervalo.
/// </summary>
public sealed class CancellableWaiter : ICycleWaiter
{
    public static readonly CancellableWaiter Instance = new();

    private CancellableWaiter()
    {
    }

    public void Wait(TimeSpan interval, CancellationToken cancellationToken) =>
        cancellationToken.WaitHandle.WaitOne(interval);
}
