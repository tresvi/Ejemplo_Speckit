namespace IISLogParser.Ingestion;

/// <summary>
/// Frontera delgada de E/S. Existe por testabilidad concreta y demostrable —simular un
/// archivo ilegible de forma portable— y no por abstracción especulativa (D-013).
/// </summary>
public interface IFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    IEnumerable<string> EnumerateDirectories(string path);

    IEnumerable<string> EnumerateFiles(string path);

    long GetFileSize(string path);

    /// <summary>
    /// Abre el archivo para lectura sin impedir que IIS siga escribiendo en él ni que la
    /// rotación lo reemplace (FR-006, D-003).
    /// </summary>
    Stream OpenRead(string path);
}

/// <inheritdoc />
public sealed class FileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);

    public IEnumerable<string> EnumerateFiles(string path) => Directory.EnumerateFiles(path);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public Stream OpenRead(string path) =>
        new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            // ReadWrite deja escribir a IIS; Delete deja que la rotacion se lleve el
            // archivo mientras lo leemos. Sin ambos, el parser bloquea al servidor.
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
}
