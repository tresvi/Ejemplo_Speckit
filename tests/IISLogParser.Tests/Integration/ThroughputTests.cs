using System.Diagnostics;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T066 — SC-002 exige al menos 50.000 registros por minuto. El diseño de lotes
/// transaccionales con comando preparado deberia quedar holgadamente por debajo; si no
/// lo hace, el problema esta en el tamaño de lote o en los pragmas (D-008), no en el
/// parseo.
/// </summary>
public class ThroughputTests
{
    private const int Lines = 100_000;

    // SC-002 traducido a esta escala: 100.000 registros a 50.000/minuto son 2 minutos.
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    [Fact]
    public void IngestaMasiva_SuperaElPisoDeRendimientoDelContrato()
    {
        using var workspace = new TempWorkspace();

        var lines = new string[Lines];
        for (var i = 0; i < Lines; i++)
        {
            lines[i] = TempWorkspace.DataLine(
                time: TimeSpan.FromSeconds(i % 86_400).ToString("hh\\:mm\\:ss"),
                stem: $"/recurso/{i}");
        }

        workspace.WriteLog("W3SVC1", "u_ex260903.log", lines);

        var stopwatch = Stopwatch.StartNew();
        var (report, _) = Harness.RunSnapshot(workspace);
        stopwatch.Stop();

        Assert.Equal(Lines, report.RecordsIngested);
        Assert.Equal(Lines, workspace.CountRows());
        Assert.True(
            stopwatch.Elapsed < Budget,
            $"La ingesta tardo {stopwatch.Elapsed.TotalSeconds:F1} s para {Lines} registros; " +
            $"el piso del SC-002 admite hasta {Budget.TotalSeconds:F0} s.");
    }

    [Fact]
    public void IngestaMasiva_NoCreceEnMemoriaConElTamanoDelArchivo()
    {
        // La lectura es en streaming y los lotes acotan lo que vive en memoria: el pico
        // no puede escalar con el tamano del archivo.
        using var workspace = new TempWorkspace();

        var lines = new string[Lines];
        for (var i = 0; i < Lines; i++)
        {
            lines[i] = TempWorkspace.DataLine(stem: $"/recurso/{i}");
        }

        workspace.WriteLog("W3SVC1", "u_ex260903.log", lines);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        Harness.RunSnapshot(workspace);

        var after = GC.GetTotalMemory(forceFullCollection: true);
        var growthMb = (after - before) / (1024.0 * 1024.0);

        Assert.True(growthMb < 64, $"La memoria retenida crecio {growthMb:F1} MB tras ingestar {Lines} lineas.");
    }
}
