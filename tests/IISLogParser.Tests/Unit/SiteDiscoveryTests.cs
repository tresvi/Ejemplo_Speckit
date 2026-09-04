using IISLogParser.Discovery;
using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Unit;

/// <summary>
/// T023 — una subcarpeta es un sitio si su nombre encaja con ^W3SVC\d+$. Cualquier
/// otra se ignora y se reporta como omitida, sin ser un error (data-model.md).
/// </summary>
public class SiteDiscoveryTests
{
    private static SiteDiscovery Discovery() => new(new FileSystem());

    [Fact]
    public void CarpetasW3SVC_SeReconocenComoSitios()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");
        workspace.CreateSite("W3SVC2");
        workspace.CreateSite("W3SVC10");

        var result = Discovery().DiscoverSites(workspace.LogsPath);

        Assert.Equal(3, result.Sites.Count);
        Assert.Contains(result.Sites, s => s.SiteId == "W3SVC1");
        Assert.Contains(result.Sites, s => s.SiteId == "W3SVC10");
    }

    [Fact]
    public void CarpetasAjenas_SeIgnoranYSeReportan()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC1");
        Directory.CreateDirectory(Path.Combine(workspace.LogsPath, "Temp"));
        Directory.CreateDirectory(Path.Combine(workspace.LogsPath, "FTPSVC1"));

        var result = Discovery().DiscoverSites(workspace.LogsPath);

        Assert.Single(result.Sites);
        Assert.Equal(2, result.Ignored.Count);
        Assert.Contains(result.Ignored, p => p.Path.EndsWith("Temp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("w3svc1")]
    [InlineData("W3SVC001")]
    public void NombreDeSitio_SeReconoceSinDistinguirMayusculas(string folder)
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite(folder);

        var result = Discovery().DiscoverSites(workspace.LogsPath);

        Assert.Single(result.Sites);
    }

    [Theory]
    [InlineData("W3SVC")]
    [InlineData("W3SVC1a")]
    [InlineData("XW3SVC1")]
    public void NombreQueNoEncaja_NoEsUnSitio(string folder)
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite(folder);

        var result = Discovery().DiscoverSites(workspace.LogsPath);

        Assert.Empty(result.Sites);
        Assert.Single(result.Ignored);
    }

    [Fact]
    public void IdentificadorDeSitio_EsElNombreDeLaCarpeta()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateSite("W3SVC7");

        var site = Discovery().DiscoverSites(workspace.LogsPath).Sites[0];

        Assert.Equal("W3SVC7", site.SiteId);
        Assert.Equal(workspace.SitePath("W3SVC7"), site.Path);
    }

    [Fact]
    public void CarpetaVacia_NoEsUnError()
    {
        using var workspace = new TempWorkspace();

        var result = Discovery().DiscoverSites(workspace.LogsPath);

        Assert.Empty(result.Sites);
        Assert.Empty(result.Ignored);
    }
}
