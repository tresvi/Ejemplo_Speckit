using IISLogParser.Parsing;
using IISLogParser.Storage;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T017 — atomicidad del lote, deduplicación por identidad y avance de la marca de
/// progreso. Estos invariantes (I-01 a I-04) son lo que hace imposible perder o
/// duplicar registros ante una caída.
/// </summary>
public class LogStoreTests
{
    private const string Site = "W3SVC1";
    private const string File = "W3SVC1/u_ex260903.log";

    private static LogRecord Record(long offset, string method = "GET")
    {
        var map = FieldMap.Parse("#Fields: date time cs-method sc-status");
        var record = W3CLineParser.Parse($"2026-09-03 00:00:0{offset % 10} {method} 200", map).Record!;
        record.SiteId = Site;
        record.SourceFile = File;
        record.LineOffset = offset;
        return record;
    }

    [Fact]
    public void LoteConfirmado_EscribeLasFilasYLaMarcaDeProgreso()
    {
        // I-01: nunca una sin la otra.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var inserted = store.CommitBatch(
            [Record(0), Record(100), Record(200)],
            new FileProgress(File, Site, BytesIngested: 300, LastSeenSize: 300));

        Assert.Equal(3, inserted);
        Assert.Equal(3, workspace.CountRows());

        var progress = store.GetProgress(File);
        Assert.NotNull(progress);
        Assert.Equal(300, progress!.BytesIngested);
        Assert.Equal(300, progress.LastSeenSize);
    }

    [Fact]
    public void IdentidadRepetida_SeDescartaSinContarComoNueva()
    {
        // FR-011: reinsertar una identidad existente es una operacion sin efecto.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        store.CommitBatch([Record(0), Record(100)], new FileProgress(File, Site, 200, 200));
        var inserted = store.CommitBatch(
            [Record(0), Record(100)],
            new FileProgress(File, Site, 200, 200));

        Assert.Equal(0, inserted);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void LoteMixto_CuentaSoloLasFilasRealmenteInsertadas()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        store.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));
        var inserted = store.CommitBatch(
            [Record(0), Record(100), Record(200)],
            new FileProgress(File, Site, 300, 300));

        Assert.Equal(2, inserted);
        Assert.Equal(3, workspace.CountRows());
    }

    [Fact]
    public void MismoOffsetEnDistintoSitio_SonRegistrosDistintos()
    {
        // La identidad es la terna completa, no el offset solo.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var otherSite = Record(0);
        otherSite.SiteId = "W3SVC2";
        otherSite.SourceFile = "W3SVC2/u_ex260903.log";

        store.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));
        var inserted = store.CommitBatch(
            [otherSite],
            new FileProgress("W3SVC2/u_ex260903.log", "W3SVC2", 100, 100));

        Assert.Equal(1, inserted);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void MarcaDeProgreso_SeActualizaEnLugarDeDuplicarse()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        store.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));
        store.CommitBatch([Record(100)], new FileProgress(File, Site, 200, 200));

        Assert.Equal(1, workspace.QueryScalar<long>("SELECT COUNT(*) FROM file_progress;"));
        Assert.Equal(200, store.GetProgress(File)!.BytesIngested);
    }

    [Fact]
    public void ArchivoSinMarca_DevuelveNull()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        Assert.Null(store.GetProgress("W3SVC9/inexistente.log"));
    }

    [Fact]
    public void ReinicioDeArchivo_BorraSusFilasYPoneElProgresoEnCero()
    {
        // FR-011a e I-04: si bytes_ingested es 0, no queda ninguna fila de ese archivo.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        store.CommitBatch([Record(0), Record(100)], new FileProgress(File, Site, 200, 200));
        store.ResetFile(File, Site, newSize: 50);

        Assert.Equal(0, workspace.CountRows());
        Assert.Equal(0, store.GetProgress(File)!.BytesIngested);
        Assert.Equal(50, store.GetProgress(File)!.LastSeenSize);
    }

    [Fact]
    public void ReinicioDeArchivo_NoTocaLasFilasDeOtrosArchivos()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var other = Record(0);
        other.SourceFile = "W3SVC1/u_ex260902.log";

        store.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));
        store.CommitBatch([other], new FileProgress("W3SVC1/u_ex260902.log", Site, 100, 100));

        store.ResetFile(File, Site, newSize: 0);

        Assert.Equal(1, workspace.CountRows());
        Assert.Equal(
            "W3SVC1/u_ex260902.log",
            workspace.QueryStrings("SELECT source_file FROM log_entries;")[0]);
    }

    [Fact]
    public void CamposExtra_SePersistenComoJson()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var map = FieldMap.Parse("#Fields: cs-method X-Forwarded-For");
        var record = W3CLineParser.Parse("GET 203.0.113.9", map).Record!;
        record.SiteId = Site;
        record.SourceFile = File;
        record.LineOffset = 0;

        store.CommitBatch([record], new FileProgress(File, Site, 100, 100));

        var extras = workspace.QueryStrings("SELECT extra_fields FROM log_entries;")[0];
        Assert.Contains("X-Forwarded-For", extras, StringComparison.Ordinal);
        Assert.Contains("203.0.113.9", extras, StringComparison.Ordinal);
    }

    [Fact]
    public void SinCamposExtra_LaColumnaQuedaNula()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        store.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));

        Assert.Equal(
            1,
            workspace.QueryScalar<long>("SELECT COUNT(*) FROM log_entries WHERE extra_fields IS NULL;"));
    }

    [Fact]
    public void LoteVacio_IgualAvanzaLaMarcaDeProgreso()
    {
        // Un tramo de archivo que solo tiene comentarios no inserta filas, pero el
        // progreso tiene que avanzar igual o se releeria para siempre.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var inserted = store.CommitBatch([], new FileProgress(File, Site, 512, 512));

        Assert.Equal(0, inserted);
        Assert.Equal(512, store.GetProgress(File)!.BytesIngested);
    }

    [Fact]
    public void ProgresoDeTodosLosArchivos_SePuedeConsultar()
    {
        // El conjunto vigilado del modo continuo lo necesita para saber que archivos
        // tienen bytes pendientes.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        store.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));
        store.CommitBatch([], new FileProgress("W3SVC2/u_ex260903.log", "W3SVC2", 50, 90));

        var all = store.GetAllProgress();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.SourceFile == "W3SVC2/u_ex260903.log" && p.LastSeenSize == 90);
    }
}
