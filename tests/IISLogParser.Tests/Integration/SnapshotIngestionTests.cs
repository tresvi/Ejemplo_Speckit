using IISLogParser.Cli;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T024 — quickstart escenario 1: una corrida, todos los sitios, una tabla.
/// </summary>
public class SnapshotIngestionTests
{
    [Fact]
    public void Snapshot_IngestaTodosLosArchivosDeTodosLosSitios()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260902.log", TempWorkspace.DataLine(date: "2026-09-02"));
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine("00:01:00"));

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(3, report.FilesProcessed);
        Assert.Equal(4, report.RecordsIngested);
        Assert.Equal(4, workspace.CountRows());
    }

    [Fact]
    public void CadaFila_LlevaElIdentificadorDeSuSitio()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);

        var sites = workspace.QueryStrings("SELECT DISTINCT site_id FROM log_entries ORDER BY site_id;");

        Assert.Equal(["W3SVC1", "W3SVC2"], sites);
    }

    [Fact]
    public void RutaDeOrigen_EsRelativaALaRaizYUsaBarraNormal()
    {
        // I-05: nunca una ruta absoluta, para que la base sea portable entre servidores.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);

        var sourceFile = workspace.QueryStrings("SELECT DISTINCT source_file FROM log_entries;")[0];

        Assert.Equal("W3SVC1/u_ex260903.log", sourceFile);
    }

    [Fact]
    public void LineasDeCabecera_NoSeIngestanComoPeticiones()
    {
        // FR-004: el archivo tiene cuatro lineas de cabecera y una de datos.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);

        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void CarpetaVacia_TerminaSinErrorYSinArchivos()
    {
        using var workspace = new TempWorkspace();

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(0, report.FilesProcessed);
        Assert.Equal(0, report.RecordsIngested);
        Assert.Equal(ExitCode.Success, report.ToExitCode());
    }

    [Fact]
    public void CarpetaAjena_SeReportaComoIgnoradaYNoEsUnError()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        Directory.CreateDirectory(Path.Combine(workspace.LogsPath, "Temp"));

        var (report, output) = Harness.RunSnapshot(workspace);

        Assert.Equal(ExitCode.Success, report.ToExitCode());
        Assert.Contains("Temp", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MarcaTemporal_SePersisteComoIso8601()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);

        Assert.Equal(
            "2026-09-03T00:00:02",
            workspace.QueryStrings("SELECT timestamp_utc FROM log_entries;")[0]);
    }

    [Fact]
    public void ResumenFinal_SeEmiteUnaSolaVez()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var (_, output) = Harness.RunSnapshot(workspace);

        var summaryLines = output
            .Split('\n')
            .Count(l => l.Contains("Archivos:", StringComparison.Ordinal));

        Assert.Equal(1, summaryLines);
    }
}
