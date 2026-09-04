using IISLogParser.Cli;

namespace IISLogParser.Tests.Unit;

/// <summary>
/// T007 — reglas de análisis de argumentos definidas en contracts/cli.md.
/// CliParser es una función pura: no toca el sistema de archivos. La existencia
/// de --logs-path se valida más arriba, en la composición del programa.
/// </summary>
public class CliParserTests
{
    [Fact]
    public void SinArgumentos_UsaModoContinuoConIntervaloDeDiezSegundos()
    {
        var result = CliParser.Parse([]);

        Assert.Null(result.Error);
        Assert.NotNull(result.Options);
        Assert.Equal(RunMode.Continuous, result.Options!.Mode);
        Assert.Equal(10, result.Options.PollIntervalSeconds);
        Assert.False(result.Options.PollIntervalExplicit);
    }

    [Fact]
    public void SinArgumentos_UsaLasRutasPorDefectoDelContrato()
    {
        var result = CliParser.Parse([]);

        Assert.Equal(CliDefaults.LogsPath, result.Options!.LogsPath);
        Assert.Equal(CliDefaults.DatabasePath, result.Options.DatabasePath);
    }

    [Theory]
    [InlineData("snapshot", RunMode.Snapshot)]
    [InlineData("SNAPSHOT", RunMode.Snapshot)]
    [InlineData("Continuous", RunMode.Continuous)]
    public void ModoExplicito_SeAceptaSinDistinguirMayusculas(string value, RunMode expected)
    {
        var result = CliParser.Parse(["--mode", value]);

        Assert.Null(result.Error);
        Assert.Equal(expected, result.Options!.Mode);
    }

    [Fact]
    public void IntervaloExplicito_SobreescribeElValorPorDefecto()
    {
        var result = CliParser.Parse(["--poll-interval", "30"]);

        Assert.Null(result.Error);
        Assert.Equal(30, result.Options!.PollIntervalSeconds);
        Assert.True(result.Options.PollIntervalExplicit);
    }

    [Fact]
    public void RutasExplicitas_SobreescribenLosValoresPorDefecto()
    {
        var result = CliParser.Parse(["--logs-path", @"D:\logs", "--database", @"D:\db\iis.db"]);

        Assert.Null(result.Error);
        Assert.Equal(@"D:\logs", result.Options!.LogsPath);
        Assert.Equal(@"D:\db\iis.db", result.Options.DatabasePath);
    }

    [Fact]
    public void IntervaloEnModoSnapshot_SeAceptaYSeMarcaComoExplicito()
    {
        // El contrato dice: se acepta y se ignora, informándolo en el reporte.
        var result = CliParser.Parse(["--mode", "snapshot", "--poll-interval", "45"]);

        Assert.Null(result.Error);
        Assert.Equal(RunMode.Snapshot, result.Options!.Mode);
        Assert.True(result.Options.PollIntervalExplicit);
    }

    [Fact]
    public void Help_SeDetectaEnCualquierPosicion()
    {
        var result = CliParser.Parse(["--mode", "snapshot", "--help"]);

        Assert.True(result.HelpRequested);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ModoDesconocido_EsUnError()
    {
        var result = CliParser.Parse(["--mode", "turbo"]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
        Assert.Contains("turbo", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("diez")]
    [InlineData("1.5")]
    [InlineData("")]
    public void IntervaloInvalido_EsUnError(string value)
    {
        var result = CliParser.Parse(["--poll-interval", value]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void OpcionDesconocida_EsUnError()
    {
        var result = CliParser.Parse(["--turbo"]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
        Assert.Contains("--turbo", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ArgumentoPosicional_EsUnError()
    {
        var result = CliParser.Parse([@"C:\logs"]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("--mode")]
    [InlineData("--poll-interval")]
    [InlineData("--logs-path")]
    [InlineData("--database")]
    public void OpcionSinValor_EsUnError(string option)
    {
        var result = CliParser.Parse([option]);

        Assert.Null(result.Options);
        Assert.NotNull(result.Error);
        Assert.Contains(option, result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void OpcionRepetida_TomaLaUltimaAparicion()
    {
        var result = CliParser.Parse(["--poll-interval", "20", "--poll-interval", "40"]);

        Assert.Null(result.Error);
        Assert.Equal(40, result.Options!.PollIntervalSeconds);
    }
}
