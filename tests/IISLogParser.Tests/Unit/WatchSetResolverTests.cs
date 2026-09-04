using IISLogParser.Discovery;
using IISLogParser.Ingestion;
using IISLogParser.Storage;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Unit;

/// <summary>
/// T042 - el conjunto vigilado es la union de dos reglas: el archivo del dia de cada
/// sitio, y todo archivo con bytes pendientes. La segunda es la que impide perder la
/// cola del archivo del dia anterior cuando cambia la fecha (D-009).
/// </summary>
public class WatchSetResolverTests
{
    private static readonly DateOnly Today = new(2026, 9, 3);

    private static WatchSetResolver Resolver(SqliteLogStore store)
    {
        var fs = new FileSystem();
        return new WatchSetResolver(fs, new SiteDiscovery(fs), store);
    }

    [Fact]
    public void ArchivoDelDia_EntraAlConjuntoVigilado()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var result = Resolver(store).Resolve(workspace.LogsPath, Today);

        Assert.Single(result.Files);
        Assert.Equal("W3SVC1/u_ex260903.log", result.Files[0].RelativePath);
    }

    [Fact]
    public void ArchivoDeDiasAnteriores_NoEntraSiNoTienePendientes()
    {
        // FR-014a: el historico es trabajo del modo snapshot, no del continuo.
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260902.log", TempWorkspace.DataLine(date: "2026-09-02"));
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var size = new FileInfo(workspace.FilePath("W3SVC1", "u_ex260902.log")).Length;
        store.CommitBatch([], new FileProgress("W3SVC1/u_ex260902.log", "W3SVC1", size, size));

        var result = Resolver(store).Resolve(workspace.LogsPath, Today);

        Assert.Single(result.Files);
        Assert.Equal("W3SVC1/u_ex260903.log", result.Files[0].RelativePath);
    }

    [Fact]
    public void ArchivoDeAyerConPendientes_SigueVigilado()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260902.log", TempWorkspace.DataLine(date: "2026-09-02"));
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        // Leido a medias: quedan bytes por ingestar.
        store.CommitBatch([], new FileProgress("W3SVC1/u_ex260902.log", "W3SVC1", 10, 10));

        var result = Resolver(store).Resolve(workspace.LogsPath, Today);

        Assert.Contains(result.Files, f => f.RelativePath == "W3SVC1/u_ex260902.log");
    }

    [Fact]
    public void ArchivoDeAyerSinMarcaDeProgreso_NoEntra()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260902.log", TempWorkspace.DataLine(date: "2026-09-02"));
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var result = Resolver(store).Resolve(workspace.LogsPath, Today);

        Assert.Empty(result.Files);
    }

    [Fact]
    public void VariosSitios_AportanCadaUnoSuArchivoDelDia()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var result = Resolver(store).Resolve(workspace.LogsPath, Today);

        Assert.Equal(2, result.Files.Count);
    }

    [Fact]
    public void CarpetasAjenas_NoAportanArchivos()
    {
        using var workspace = new TempWorkspace();
        var foreign = Path.Combine(workspace.LogsPath, "Temp");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "u_ex260903.log"), "#Fields: date\n");
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        var result = Resolver(store).Resolve(workspace.LogsPath, Today);

        Assert.Empty(result.Files);
    }

    [Fact]
    public void NombreNoReconocible_SoloEntraSiTienePendientes()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");
        File.WriteAllText(workspace.FilePath("W3SVC1", "raro.log"), "#Fields: date time\n");
        using var store = new SqliteLogStore(workspace.DatabasePath);
        store.Initialize();

        Assert.Empty(Resolver(store).Resolve(workspace.LogsPath, Today).Files);

        store.CommitBatch([], new FileProgress("W3SVC1/raro.log", "W3SVC1", 3, 3));

        Assert.Single(Resolver(store).Resolve(workspace.LogsPath, Today).Files);
    }
}
