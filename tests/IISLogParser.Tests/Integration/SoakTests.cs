using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T080 — verificación del SC-006 (24 h de ejecución sostenida sin degradación de memoria
/// ni pérdida de registros).
///
/// El soak real NO corre en la suite por defecto: 24 horas de reloj romperían el
/// Principio III, que exige una suite determinista y ejecutable en cualquier momento. Se
/// resuelve en dos piezas complementarias:
///
/// 1. **Proxy determinista** (siempre corre): miles de ciclos comprimidos, con el reloj
///    inyectado, verificando que ni la memoria retenida ni el trabajo por ciclo crecen
///    con la cantidad de ciclos. Eso es lo que un soak buscaría detectar.
/// 2. **Soak real** (opt-in): se habilita con la variable de entorno
///    <c>IISLOGPARSER_SOAK_MINUTES</c>. El procedimiento completo está documentado en
///    quickstart.md.
/// </summary>
public class SoakTests
{
    private const string SoakVariable = "IISLOGPARSER_SOAK_MINUTES";

    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void MilesDeCiclos_NoDegradanLaMemoriaRetenida()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);

        for (var i = 0; i < 2_000; i++)
        {
            harness.Cycle();
        }

        var growthMb = (GC.GetTotalMemory(forceFullCollection: true) - baseline) / (1024.0 * 1024.0);

        Assert.True(growthMb < 32, $"La memoria retenida crecio {growthMb:F1} MB tras 2.000 ciclos.");
    }

    [Fact]
    public void MilesDeCiclosSinCambios_NoReingestanNada()
    {
        // Perder o duplicar registros a lo largo de una corrida larga se manifestaria
        // aca: el conteo tiene que quedar clavado.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));

        using var harness = new ContinuousHarness(workspace, Clock());
        harness.Cycle();

        for (var i = 0; i < 1_000; i++)
        {
            Assert.Equal(0, harness.Cycle().RecordsIngested);
        }

        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void CruceDeVariosDias_NoPierdeNiDuplicaRegistros()
    {
        // Comprime en segundos lo que en produccion son varios dias de rotacion.
        using var workspace = new TempWorkspace();
        var clock = Clock();
        using var harness = new ContinuousHarness(workspace, clock);

        var expected = 0;

        for (var day = 3; day <= 12; day++)
        {
            var date = $"2026-09-{day:00}";
            var fileName = $"u_ex2609{day:00}.log";

            workspace.WriteLog("W3SVC1", fileName, TempWorkspace.DataLine(date: date));
            expected++;
            harness.Cycle();

            workspace.AppendLines("W3SVC1", fileName, TempWorkspace.DataLine("23:59:59", date: date));
            expected++;
            harness.Cycle();

            clock.AdvanceDays(1);
        }

        Assert.Equal(expected, workspace.CountRows());
    }

    [Fact]
    public void SoakReal_SoloCorreCuandoSeLoPideExplicitamente()
    {
        var configured = Environment.GetEnvironmentVariable(SoakVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            // Sin la variable, el test documenta el procedimiento y no consume tiempo.
            // Habilitarlo: IISLOGPARSER_SOAK_MINUTES=1440 dotnet test --filter SoakReal
            return;
        }

        Assert.True(
            int.TryParse(configured, out var minutes) && minutes > 0,
            $"{SoakVariable} debe ser un entero de minutos mayor que cero.");

        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(minutes));
        using var harness = new ContinuousHarness(workspace, Clock());

        harness.Runner.Run(harness.LogsPath, TimeSpan.FromSeconds(10), cancellation.Token);

        Assert.Equal(1, workspace.CountRows());
    }
}
