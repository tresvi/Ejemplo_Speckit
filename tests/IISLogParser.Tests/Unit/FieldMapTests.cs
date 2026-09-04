using IISLogParser.Parsing;

namespace IISLogParser.Tests.Unit;

/// <summary>
/// T010 — la directiva #Fields: es la única fuente confiable del mapa de columnas.
/// Puede aparecer más de una vez en el mismo archivo (D-005).
/// </summary>
public class FieldMapTests
{
    [Fact]
    public void MapaVacio_EsElEstadoInicial()
    {
        Assert.True(FieldMap.Empty.IsEmpty);
        Assert.Equal(0, FieldMap.Empty.Count);
    }

    [Fact]
    public void DirectivaDeCampos_SeReconoce()
    {
        Assert.True(FieldMap.IsFieldsDirective("#Fields: date time cs-method"));
        Assert.True(FieldMap.IsFieldsDirective("#fields: date time"));
        Assert.False(FieldMap.IsFieldsDirective("#Software: Microsoft IIS"));
        Assert.False(FieldMap.IsFieldsDirective("2026-09-03 00:00:02 GET"));
    }

    [Fact]
    public void DirectivaDeCampos_ProduceLasColumnasEnOrden()
    {
        var map = FieldMap.Parse("#Fields: date time cs-method cs-uri-stem");

        Assert.False(map.IsEmpty);
        Assert.Equal(4, map.Count);
        Assert.Equal("date", map[0]);
        Assert.Equal("time", map[1]);
        Assert.Equal("cs_method", map[2]);
        Assert.Equal("cs_uri_stem", map[3]);
    }

    [Theory]
    [InlineData("s-ip", "s_ip")]
    [InlineData("sc-win32-status", "sc_win32_status")]
    [InlineData("cs(User-Agent)", "cs_user_agent")]
    [InlineData("cs(Referer)", "cs_referer")]
    [InlineData("cs(Cookie)", "cs_cookie")]
    [InlineData("TIME-TAKEN", "time_taken")]
    public void NombresDeCampo_SeNormalizan(string raw, string expected)
    {
        var map = FieldMap.Parse($"#Fields: {raw}");

        Assert.Equal(expected, map[0]);
    }

    [Fact]
    public void NombreOriginal_SeConservaJuntoAlNormalizado()
    {
        // Los campos desconocidos se preservan en extra_fields con el nombre que
        // el archivo declaro, no con el normalizado.
        var map = FieldMap.Parse("#Fields: X-Forwarded-For");

        Assert.Equal("X-Forwarded-For", map.OriginalName(0));
    }

    [Fact]
    public void EspaciosMultiples_NoProducenColumnasVacias()
    {
        var map = FieldMap.Parse("#Fields:   date    time  ");

        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void DirectivaSinCampos_ProduceUnMapaVacio()
    {
        var map = FieldMap.Parse("#Fields:");

        Assert.True(map.IsEmpty);
    }
}
