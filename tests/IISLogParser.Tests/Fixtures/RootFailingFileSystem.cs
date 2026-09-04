using IISLogParser.Ingestion;

namespace IISLogParser.Tests.Fixtures;

/// <summary>
/// Falla al enumerar la carpeta raíz de logs. Es el caso que ningún fake anterior
/// cubría: los existentes inyectan el fallo a nivel de archivo o de carpeta de sitio,
/// nunca sobre la raíz, que es justo donde la excepción se escapaba sin manejar.
/// </summary>
public sealed class RootFailingFileSystem(IFileSystem inner, string logsRoot, Exception error)
    : IFileSystem
{
    /// <summary>Permite apagar el fallo para verificar el reintento del ciclo siguiente.</summary>
    public bool Failing { get; set; } = true;

    public bool DirectoryExists(string path) => inner.DirectoryExists(path);

    public bool FileExists(string path) => inner.FileExists(path);

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        if (Failing && string.Equals(path, logsRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw error;
        }

        return inner.EnumerateDirectories(path);
    }

    public IEnumerable<string> EnumerateFiles(string path) => inner.EnumerateFiles(path);

    public long GetFileSize(string path) => inner.GetFileSize(path);

    public Stream OpenRead(string path) => inner.OpenRead(path);
}
