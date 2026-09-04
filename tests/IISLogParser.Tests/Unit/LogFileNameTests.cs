using IISLogParser.Discovery;

namespace IISLogParser.Tests.Unit;

/// <summary>
/// T041 - la fecha de un archivo se deriva de su nombre. Un nombre que no encaja no
/// puede considerarse "de hoy": no hay forma confiable de fecharlo (D-005).
/// </summary>
public class LogFileNameTests
{
    [Theory]
    [InlineData("u_ex260903.log", 2026, 9, 3)]
    [InlineData("ex260903.log", 2026, 9, 3)]
    [InlineData("U_EX251231.LOG", 2025, 12, 31)]
    [InlineData("u_ex000101.log", 2000, 1, 1)]
    public void NombresDeRotacionDiaria_ProducenSuFecha(string name, int year, int month, int day)
    {
        Assert.True(LogFileName.TryGetDate(name, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("u_ex26090312.log")]
    [InlineData("cualquiera.log")]
    [InlineData("u_ex2609.log")]
    [InlineData("u_ex260903.txt")]
    [InlineData("u_exABCDEF.log")]
    public void NombresQueNoEncajan_NoProducenFecha(string name)
    {
        Assert.False(LogFileName.TryGetDate(name, out _));
    }

    [Fact]
    public void FechaInvalida_NoSeAcepta()
    {
        Assert.False(LogFileName.TryGetDate("u_ex261332.log", out _));
    }

    [Fact]
    public void RutaCompleta_SeResuelvePorElNombreDeArchivo()
    {
        Assert.True(LogFileName.TryGetDate(@"C:\logs\W3SVC1\u_ex260903.log", out var date));
        Assert.Equal(new DateOnly(2026, 9, 3), date);
    }
}
