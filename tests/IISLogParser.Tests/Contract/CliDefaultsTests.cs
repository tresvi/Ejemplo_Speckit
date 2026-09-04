using IISLogParser.Cli;
using IISLogParser.Diagnostics;

namespace IISLogParser.Tests.Contract;

/// <summary>
/// T060 y T061 — la historia P3: qué pasa cuando el operador solo quiere dejarlo
/// andando. FR-018 y FR-019.
/// </summary>
public class CliDefaultsTests
{
    private static string Startup(string[] args)
    {
        var options = CliParser.Parse(args).Options!;
        var writer = new StringWriter();
        new OperatorReport(writer).WriteStartup(options, @"C:\ops\iislogs.db");
        return writer.ToString();
    }

    [Fact]
    public void SinArgumentos_ElReporteDiceModoContinuo()
    {
        Assert.Contains("continuous", Startup([]), StringComparison.Ordinal);
    }

    [Fact]
    public void SinArgumentos_ElReporteDiceIntervaloDeDiezSegundos()
    {
        Assert.Contains("10 s", Startup([]), StringComparison.Ordinal);
    }

    [Fact]
    public void IntervaloExplicito_SobreescribeElReportado()
    {
        var startup = Startup(["--poll-interval", "30"]);

        Assert.Contains("30 s", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("10 s", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void ModoSnapshotSinIntervalo_NoReportaIntervalo()
    {
        var startup = Startup(["--mode", "snapshot"]);

        Assert.Contains("snapshot", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("Poll interval", startup, StringComparison.Ordinal);
    }

    [Fact]
    public void ModoSnapshotConIntervaloExplicito_LoReportaComoIgnorado()
    {
        var startup = Startup(["--mode", "snapshot", "--poll-interval", "45"]);

        Assert.Contains("45 s", startup, StringComparison.Ordinal);
        Assert.Contains("ignorado en modo snapshot", startup, StringComparison.Ordinal);
    }
}
