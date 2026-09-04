using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T049 — FR-026: en modo continuo, un archivo omitido se reintenta en el ciclo
/// siguiente, sin intervención manual y sin detener el proceso.
/// </summary>
public class SkippedRetryTests
{
    private static FakeClock Clock() => new(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero));

    /// <summary>Falla solo mientras <see cref="Failing"/> esté encendido.</summary>
    private sealed class ToggleableFileSystem(IFileSystem inner, string fragment) : IFileSystem
    {
        public bool Failing { get; set; } = true;

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public bool FileExists(string path) => inner.FileExists(path);

        public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(path);

        public IEnumerable<string> EnumerateFiles(string path) => inner.EnumerateFiles(path);

        public long GetFileSize(string path) => inner.GetFileSize(path);

        public Stream OpenRead(string path)
        {
            if (Failing && path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("archivo bloqueado");
            }

            return inner.OpenRead(path);
        }
    }

    [Fact]
    public void ArchivoOmitido_SeReintentaEnElCicloSiguiente()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new ToggleableFileSystem(new FileSystem(), "W3SVC2");
        using var harness = new ContinuousHarness(workspace, Clock(), fs);

        var first = harness.Cycle();
        Assert.Single(first.SkippedPaths);
        Assert.Equal(1, workspace.CountRows());

        fs.Failing = false;
        var second = harness.Cycle();

        Assert.Empty(second.SkippedPaths);
        Assert.Equal(1, second.RecordsIngested);
        Assert.Equal(2, workspace.CountRows());
    }

    [Fact]
    public void ArchivoOmitido_NoDetieneElProceso()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new ToggleableFileSystem(new FileSystem(), "W3SVC2");
        using var cancellation = new CancellationTokenSource();
        using var harness = new ContinuousHarness(workspace, Clock(), fs);

        harness.Runner.Run(
            harness.LogsPath,
            TimeSpan.FromSeconds(10),
            cancellation.Token,
            new CountingWaiter(3, cancellation));

        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void OmisionSeReportaEnCadaCiclo()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var fs = new ToggleableFileSystem(new FileSystem(), "W3SVC1");
        using var harness = new ContinuousHarness(workspace, Clock(), fs);

        harness.Cycle();
        harness.Cycle();

        var skips = harness.Output.ToString()
            .Split('\n')
            .Count(l => l.Contains("OMITIDO", StringComparison.Ordinal));

        Assert.Equal(2, skips);
    }
}
