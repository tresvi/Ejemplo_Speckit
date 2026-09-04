using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T078 — los logs de referencia de <c>Fixtures/SampleLogs/</c> existen y se ingestan.
/// Cubren formas que el generador programático de <see cref="TempWorkspace"/> no
/// produce: agentes de usuario con '+' en lugar de espacios, cambio de cabecera a mitad
/// de archivo y campos fuera del esquema fijo.
/// </summary>
public class SampleLogsTests
{
    [Fact]
    public void LogEstandarDeReferencia_SeIngestaCompleto()
    {
        using var workspace = new TempWorkspace();
        workspace.CopySampleLog("W3SVC1", "standard.log", "u_ex260903.log");

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(3, report.RecordsIngested);
        Assert.Equal(0, report.LinesRejected);
    }

    [Fact]
    public void AgenteDeUsuarioConMas_SeConservaTalComoIISLoEscribe()
    {
        // IIS reemplaza los espacios por '+' dentro de un valor: por eso el conteo de
        // valores por espacio simple es fiable.
        using var workspace = new TempWorkspace();
        workspace.CopySampleLog("W3SVC1", "standard.log", "u_ex260903.log");

        Harness.RunSnapshot(workspace);

        var agents = workspace.QueryStrings("SELECT DISTINCT cs_user_agent FROM log_entries;");

        Assert.Contains("Mozilla/5.0+(Windows+NT+10.0)", agents);
        Assert.Contains("curl/8.4.0", agents);
    }

    [Fact]
    public void CamposNumericos_SePersistenComoEnteros()
    {
        using var workspace = new TempWorkspace();
        workspace.CopySampleLog("W3SVC1", "standard.log", "u_ex260903.log");

        Harness.RunSnapshot(workspace);

        // 404.0 con win32-status 2 (ERROR_FILE_NOT_FOUND) es exactamente lo que IIS
        // escribe para un recurso inexistente: el sub-estado es 0 y el 2 va en win32.
        Assert.Equal(
            1,
            workspace.QueryScalar<long>(
                "SELECT COUNT(*) FROM log_entries WHERE sc_status = 404 AND sc_substatus = 0 AND sc_win32_status = 2;"));
        Assert.Equal(
            1284,
            workspace.QueryScalar<long>("SELECT sc_bytes FROM log_entries WHERE cs_uri_stem = '/index.html';"));
    }

    [Fact]
    public void CambioDeCabeceraEnArchivoDeReferencia_SeRespeta()
    {
        using var workspace = new TempWorkspace();
        workspace.CopySampleLog("W3SVC1", "fields-change.log", "u_ex260903.log");

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(2, report.RecordsIngested);
        Assert.Equal(0, report.LinesRejected);
        Assert.Equal(
            1,
            workspace.QueryScalar<long>("SELECT COUNT(*) FROM log_entries WHERE cs_uri_stem = '/despues.html';"));
    }

    [Fact]
    public void CampoFueraDelEsquemaFijo_QuedaEnLaBolsaDeExtras()
    {
        using var workspace = new TempWorkspace();
        workspace.CopySampleLog("W3SVC1", "fields-change.log", "u_ex260903.log");

        Harness.RunSnapshot(workspace);

        var extras = workspace.QueryStrings(
            "SELECT extra_fields FROM log_entries WHERE cs_uri_stem = '/despues.html';")[0];

        Assert.Contains("X-Forwarded-For", extras, StringComparison.Ordinal);
        Assert.Contains("203.0.113.9", extras, StringComparison.Ordinal);
    }

    [Fact]
    public void LineaMalformadaEnArchivoDeReferencia_NoDetieneElResto()
    {
        using var workspace = new TempWorkspace();
        workspace.CopySampleLog("W3SVC1", "with-rejects.log", "u_ex260903.log");

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(2, report.RecordsIngested);
        Assert.Equal(1, report.LinesRejected);
    }
}
