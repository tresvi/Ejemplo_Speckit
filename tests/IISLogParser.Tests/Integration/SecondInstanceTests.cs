using IISLogParser.Parsing;
using IISLogParser.Storage;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T075 — caso borde "Segunda instancia". La spec asume una sola instancia por almacén,
/// pero exige que una segunda no corrompa los datos ni duplique registros. Lo que lo
/// garantiza no es una suposición: es el índice único de identidad más WAL (D-006, D-008).
/// </summary>
public class SecondInstanceTests
{
    private const string Site = "W3SVC1";
    private const string File = "W3SVC1/u_ex260903.log";

    private static LogRecord Record(long offset)
    {
        var map = FieldMap.Parse("#Fields: date time cs-method sc-status");
        var record = W3CLineParser.Parse("2026-09-03 00:00:02 GET 200", map).Record!;
        record.SiteId = Site;
        record.SourceFile = File;
        record.LineOffset = offset;
        return record;
    }

    [Fact]
    public void DosInstanciasSobreElMismoArchivo_NoDuplicanRegistros()
    {
        using var workspace = new TempWorkspace();

        using var first = new SqliteLogStore(workspace.DatabasePath);
        first.Initialize();
        using var second = new SqliteLogStore(workspace.DatabasePath);
        second.Initialize();

        var insertedByFirst = first.CommitBatch(
            [Record(0), Record(100)],
            new FileProgress(File, Site, 200, 200));

        // La segunda instancia intenta ingestar el mismo tramo.
        var insertedBySecond = second.CommitBatch(
            [Record(0), Record(100)],
            new FileProgress(File, Site, 200, 200));

        Assert.Equal(2, insertedByFirst);
        Assert.Equal(0, insertedBySecond);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void DosInstanciasConTramosDistintos_ConvergenSinPerderNada()
    {
        using var workspace = new TempWorkspace();

        using var first = new SqliteLogStore(workspace.DatabasePath);
        first.Initialize();
        using var second = new SqliteLogStore(workspace.DatabasePath);
        second.Initialize();

        first.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 300));
        second.CommitBatch([Record(100), Record(200)], new FileProgress(File, Site, 300, 300));

        Assert.Equal(3, workspace.CountRows());
        Assert.Equal(300, second.GetProgress(File)!.BytesIngested);
    }

    [Fact]
    public void SegundaInicializacionDelEsquema_NoCorrompeNada()
    {
        using var workspace = new TempWorkspace();

        using var first = new SqliteLogStore(workspace.DatabasePath);
        first.Initialize();
        first.CommitBatch([Record(0)], new FileProgress(File, Site, 100, 100));

        using var second = new SqliteLogStore(workspace.DatabasePath);
        second.Initialize();

        Assert.Equal(1, workspace.CountRows());
        Assert.Equal(1, workspace.QueryScalar<long>("SELECT COUNT(*) FROM schema_version;"));
        Assert.Equal("ok", workspace.QueryStrings("PRAGMA integrity_check;")[0]);
    }

    [Fact]
    public void SnapshotsConcurrentesSobreElMismoArbol_NoDuplicanFilas()
    {
        // Dos corridas completas contra la misma base, como haria un operador que
        // ejecuta la herramienta dos veces sin darse cuenta.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));

        Harness.RunSnapshot(workspace);
        Harness.RunSnapshot(workspace);

        Assert.Equal(2, workspace.CountRows());
        Assert.Equal("ok", workspace.QueryStrings("PRAGMA integrity_check;")[0]);
    }
}
