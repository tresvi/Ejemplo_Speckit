using IISLogParser.Parsing;
using IISLogParser.Storage;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Contract;

/// <summary>
/// T015 — contrato del esquema (contracts/database-schema.md). Quien consulte estos
/// datos depende de estos nombres y tipos: un cambio acá es un cambio de contrato.
/// </summary>
public class DatabaseSchemaContractTests
{
    [Fact]
    public void Inicializacion_CreaLasTresTablasDelContrato()
    {
        using var workspace = new TempWorkspace();
        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        var tables = workspace.QueryStrings(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");

        Assert.Contains("log_entries", tables);
        Assert.Contains("file_progress", tables);
        Assert.Contains("schema_version", tables);
    }

    [Fact]
    public void TablaDeRegistros_TieneLasColumnasDeIdentidadYMetadatos()
    {
        using var workspace = new TempWorkspace();
        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        var columns = workspace.QueryStrings("SELECT name FROM pragma_table_info('log_entries');");

        Assert.Contains("id", columns);
        Assert.Contains("site_id", columns);
        Assert.Contains("source_file", columns);
        Assert.Contains("line_offset", columns);
        Assert.Contains("extra_fields", columns);
        Assert.Contains("ingested_at_utc", columns);
    }

    [Fact]
    public void TablaDeRegistros_TieneUnaColumnaPorCadaCampoDelEsquemaFijo()
    {
        using var workspace = new TempWorkspace();
        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        var columns = workspace.QueryStrings("SELECT name FROM pragma_table_info('log_entries');");

        foreach (var expected in LogColumns.Names)
        {
            Assert.Contains(expected, columns);
        }
    }

    [Fact]
    public void TablaDeProgreso_TieneLasColumnasDelContrato()
    {
        using var workspace = new TempWorkspace();
        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        var columns = workspace.QueryStrings("SELECT name FROM pragma_table_info('file_progress');");

        Assert.Contains("source_file", columns);
        Assert.Contains("site_id", columns);
        Assert.Contains("bytes_ingested", columns);
        Assert.Contains("last_seen_size", columns);
        Assert.Contains("updated_at_utc", columns);
    }

    [Fact]
    public void IndiceUnicoDeIdentidad_Existe()
    {
        // I-02: no pueden existir dos filas con la misma terna.
        using var workspace = new TempWorkspace();
        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        var indexes = workspace.QueryStrings(
            "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='log_entries';");

        Assert.Contains("ux_log_entries_identity", indexes);

        var unique = workspace.QueryScalar<long>(
            "SELECT \"unique\" FROM pragma_index_list('log_entries') WHERE name='ux_log_entries_identity';");

        Assert.Equal(1, unique);
    }

    [Fact]
    public void IndiceDeConsultaPorSitioYTiempo_Existe()
    {
        using var workspace = new TempWorkspace();
        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        var indexes = workspace.QueryStrings(
            "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='log_entries';");

        Assert.Contains("ix_log_entries_site_time", indexes);
    }

    [Fact]
    public void Inicializacion_EsIdempotente()
    {
        // FR-010: se ejecuta en cada arranque y no puede fallar en el segundo.
        using var workspace = new TempWorkspace();

        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
            store.Initialize();
        }

        using (var store = new SqliteLogStore(workspace.DatabasePath))
        {
            store.Initialize();
        }

        Assert.Equal(1, workspace.QueryScalar<long>("SELECT COUNT(*) FROM schema_version;"));
    }

    [Fact]
    public void VersionDeEsquema_ArrancaEnUno()
    {
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        Assert.Equal(1, workspace.QueryScalar<long>("SELECT version FROM schema_version;"));
    }

    [Fact]
    public void ModoDeJournal_EsWal()
    {
        // D-008: permite consultar la tabla mientras el modo continuo ingesta.
        using var workspace = new TempWorkspace();
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        Assert.Equal("wal", store.JournalMode, ignoreCase: true);
    }
}
