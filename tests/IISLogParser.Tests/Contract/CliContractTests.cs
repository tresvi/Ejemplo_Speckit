using IISLogParser.Cli;

namespace IISLogParser.Tests.Contract;

/// <summary>
/// T008 — contrato de la superficie de CLI: códigos de salida y texto de ayuda,
/// según contracts/cli.md. Los códigos 2 y 3 se ejercitan de punta a punta en
/// los tests de las historias US1 y US2; acá se fija su valor numérico, que es
/// lo que un planificador externo consume.
/// </summary>
public class CliContractTests
{
    [Fact]
    public void CodigosDeSalida_TienenLosValoresDelContrato()
    {
        Assert.Equal(0, (int)ExitCode.Success);
        Assert.Equal(1, (int)ExitCode.InvalidConfiguration);
        Assert.Equal(2, (int)ExitCode.CompletedWithSkips);
        Assert.Equal(3, (int)ExitCode.StoreFailure);
    }

    [Fact]
    public void Help_NoProduceOpcionesNiError()
    {
        var result = CliParser.Parse(["--help"]);

        Assert.True(result.HelpRequested);
        Assert.Null(result.Error);
        Assert.Null(result.Options);
    }

    [Fact]
    public void TextoDeAyuda_NombraTodasLasOpcionesDelContrato()
    {
        var help = HelpText.Usage;

        Assert.Contains("--mode", help, StringComparison.Ordinal);
        Assert.Contains("--poll-interval", help, StringComparison.Ordinal);
        Assert.Contains("--logs-path", help, StringComparison.Ordinal);
        Assert.Contains("--database", help, StringComparison.Ordinal);
        Assert.Contains("--help", help, StringComparison.Ordinal);
    }

    [Fact]
    public void TextoDeAyuda_DocumentaLosValoresPorDefecto()
    {
        var help = HelpText.Usage;

        Assert.Contains("snapshot", help, StringComparison.Ordinal);
        Assert.Contains("continuous", help, StringComparison.Ordinal);
        Assert.Contains("10", help, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguracionInvalida_MapeaAlCodigoUno()
    {
        var result = CliParser.Parse(["--mode", "turbo"]);

        Assert.NotNull(result.Error);
        Assert.Equal(ExitCode.InvalidConfiguration, result.ToExitCode());
    }

    [Fact]
    public void ConfiguracionValida_NoMapeaAUnCodigoDeError()
    {
        var result = CliParser.Parse(["--mode", "snapshot"]);

        Assert.Equal(ExitCode.Success, result.ToExitCode());
    }
}
