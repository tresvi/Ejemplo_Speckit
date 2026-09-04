using IISLogParser.Parsing;

namespace IISLogParser.Tests.Unit;

/// <summary>
/// T011 — reglas V-01 a V-06 de contracts/w3c-log-format.md.
/// </summary>
public class W3CLineParserTests
{
    private static readonly FieldMap StandardMap = FieldMap.Parse(
        "#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip " +
        "cs(User-Agent) sc-status sc-substatus sc-win32-status time-taken");

    private const string StandardLine =
        "2026-09-03 00:00:02 10.0.0.4 GET /index.html - 80 - 10.0.0.99 Mozilla/5.0 200 0 0 15";

    [Fact]
    public void LineaValida_ProduceUnRegistro()
    {
        var outcome = W3CLineParser.Parse(StandardLine, StandardMap);

        Assert.Null(outcome.RejectionReason);
        Assert.NotNull(outcome.Record);
    }

    [Fact]
    public void LineaValida_MapeaCadaValorASuColumna()
    {
        var record = W3CLineParser.Parse(StandardLine, StandardMap).Record!;

        Assert.Equal("10.0.0.4", record["s_ip"]);
        Assert.Equal("GET", record["cs_method"]);
        Assert.Equal("/index.html", record["cs_uri_stem"]);
        Assert.Equal("80", record["s_port"]);
        Assert.Equal("10.0.0.99", record["c_ip"]);
        Assert.Equal("Mozilla/5.0", record["cs_user_agent"]);
        Assert.Equal("200", record["sc_status"]);
        Assert.Equal("15", record["time_taken"]);
    }

    [Fact]
    public void OrdenDeColumnasDistinto_SeRespetaLaCabecera()
    {
        // El mismo dato con las columnas al reves: el mapeo lo manda la cabecera,
        // nunca la posicion fija (FR-003).
        var map = FieldMap.Parse("#Fields: cs-method s-ip");
        var record = W3CLineParser.Parse("POST 10.0.0.7", map).Record!;

        Assert.Equal("POST", record["cs_method"]);
        Assert.Equal("10.0.0.7", record["s_ip"]);
    }

    [Fact]
    public void FechaYHora_SeCombinanEnUnaMarcaIso8601()
    {
        var record = W3CLineParser.Parse(StandardLine, StandardMap).Record!;

        Assert.Equal("2026-09-03T00:00:02", record["timestamp_utc"]);
    }

    [Fact]
    public void GuionMedio_SignificaCampoVacio()
    {
        // V-04: cs-uri-query y cs-username vienen en "-" en la linea estandar.
        var record = W3CLineParser.Parse(StandardLine, StandardMap).Record!;

        Assert.Null(record["cs_uri_query"]);
        Assert.Null(record["cs_username"]);
    }

    [Fact]
    public void CamposDesconocidos_VanALaBolsaDeExtras()
    {
        // FR-009a: nada de una linea valida se descarta.
        var map = FieldMap.Parse("#Fields: cs-method X-Forwarded-For custom-field");
        var record = W3CLineParser.Parse("GET 203.0.113.9 abc", map).Record!;

        Assert.Equal("GET", record["cs_method"]);
        Assert.Equal("203.0.113.9", record.Extras["X-Forwarded-For"]);
        Assert.Equal("abc", record.Extras["custom-field"]);
    }

    [Fact]
    public void CamposDesconocidosVacios_NoEnsucianLosExtras()
    {
        var map = FieldMap.Parse("#Fields: cs-method X-Forwarded-For");
        var record = W3CLineParser.Parse("GET -", map).Record!;

        Assert.Empty(record.Extras);
    }

    [Fact]
    public void ValorNoNumericoEnColumnaEntera_NoInvalidaElRegistro()
    {
        // V-05: se prefiere una fila con una anomalia preservada antes que perder
        // la peticion entera.
        var map = FieldMap.Parse("#Fields: cs-method sc-status");
        var outcome = W3CLineParser.Parse("GET doscientos", map);

        Assert.Null(outcome.RejectionReason);
        Assert.Null(outcome.Record!["sc_status"]);
        Assert.Equal("doscientos", outcome.Record.Extras["sc-status"]);
    }

    [Fact]
    public void LineaSinMapaDeCampos_SeRechaza()
    {
        // V-01: no se adivina el esquema.
        var outcome = W3CLineParser.Parse(StandardLine, FieldMap.Empty);

        Assert.Null(outcome.Record);
        Assert.NotNull(outcome.RejectionReason);
    }

    [Fact]
    public void LineaConMenosValoresQueColumnas_SeRechaza()
    {
        // V-02
        var outcome = W3CLineParser.Parse("2026-09-03 00:00:02 10.0.0.4", StandardMap);

        Assert.Null(outcome.Record);
        Assert.NotNull(outcome.RejectionReason);
    }

    [Fact]
    public void LineaConMasValoresQueColumnas_SeRechaza()
    {
        // V-03
        var outcome = W3CLineParser.Parse(StandardLine + " sobrante", StandardMap);

        Assert.Null(outcome.Record);
        Assert.NotNull(outcome.RejectionReason);
    }

    [Theory]
    [InlineData("#Software: Microsoft Internet Information Services 10.0")]
    [InlineData("#Version: 1.0")]
    [InlineData("#Date: 2026-09-03 00:00:02")]
    public void LineasDeComentario_SeReconocenComoNoDeDatos(string line)
    {
        // V-06
        Assert.True(W3CLineParser.IsCommentOrDirective(line));
    }

    [Fact]
    public void LineaDeDatos_NoSeConfundeConComentario()
    {
        Assert.False(W3CLineParser.IsCommentOrDirective(StandardLine));
    }

    [Fact]
    public void LineaEnBlanco_NoProduceRegistroNiRechazo()
    {
        var outcome = W3CLineParser.Parse("   ", StandardMap);

        Assert.Null(outcome.Record);
        Assert.Null(outcome.RejectionReason);
    }
}
