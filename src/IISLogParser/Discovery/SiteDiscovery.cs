using System.Text.RegularExpressions;
using IISLogParser.Ingestion;

namespace IISLogParser.Discovery;

/// <summary>Una carpeta de sitio y el identificador que le da su nombre.</summary>
public sealed record SiteFolder(string SiteId, string Path);

/// <summary>Una ruta que no se procesó, con el motivo.</summary>
public sealed record SkippedPath(string Path, string Reason);

/// <summary>
/// Sitios encontrados y carpetas ajenas. Las ajenas se informan pero no son un error:
/// la carpeta de logs de IIS convive con subcarpetas que no son sitios.
/// </summary>
public sealed record DiscoveryResult(IReadOnlyList<SiteFolder> Sites, IReadOnlyList<SkippedPath> Ignored);

public interface ISiteDiscovery
{
    DiscoveryResult DiscoverSites(string logsRoot);
}

/// <inheritdoc />
public sealed partial class SiteDiscovery(IFileSystem fileSystem) : ISiteDiscovery
{
    [GeneratedRegex(@"^W3SVC\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SiteFolderPattern();

    public DiscoveryResult DiscoverSites(string logsRoot)
    {
        var sites = new List<SiteFolder>();
        var ignored = new List<SkippedPath>();

        foreach (var directory in fileSystem.EnumerateDirectories(logsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = System.IO.Path.GetFileName(directory);

            if (SiteFolderPattern().IsMatch(name))
            {
                sites.Add(new SiteFolder(name, directory));
            }
            else
            {
                ignored.Add(new SkippedPath(directory, "no es una carpeta de sitio de IIS"));
            }
        }

        return new DiscoveryResult(sites, ignored);
    }
}
