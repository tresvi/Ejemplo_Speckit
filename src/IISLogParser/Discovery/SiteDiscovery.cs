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

/// <summary>
/// La carpeta raíz de logs no pudo recorrerse. Existe como tipo propio porque el
/// tratamiento correcto depende del modo: en snapshot es una configuración inviable
/// (código 1), y en modo continuo es una omisión del ciclo que se reintenta. Sin este
/// tipo, la excepción original se confundía con un fallo del almacén o escapaba sin
/// manejar.
/// </summary>
public sealed class LogsRootUnavailableException(string logsRoot, Exception innerException)
    : Exception($"No se pudo recorrer la carpeta de logs '{logsRoot}': {innerException.Message}", innerException)
{
    public string LogsRoot { get; } = logsRoot;
}

public interface ISiteDiscovery
{
    /// <exception cref="LogsRootUnavailableException">La raíz no pudo enumerarse.</exception>
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

        List<string> directories;

        try
        {
            directories = [.. fileSystem.EnumerateDirectories(logsRoot).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // La raiz puede volverse ilegible entre el chequeo de existencia y la
            // enumeracion, o desaparecer a mitad de una corrida larga si es un recurso
            // de red. Quien llama decide que hacer segun el modo.
            throw new LogsRootUnavailableException(logsRoot, ex);
        }

        foreach (var directory in directories)
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
