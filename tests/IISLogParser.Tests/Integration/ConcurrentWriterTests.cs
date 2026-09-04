using System.Text;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T031 - quickstart escenario 10 y FR-006. IIS mantiene el archivo del dia abierto
/// para escritura: si el lector no concede FileShare.Write, o falla o bloquea al
/// servidor (D-003).
/// </summary>
public class ConcurrentWriterTests
{
    [Fact]
    public void IngestaConEscritorAbierto_NoFalla()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        // Un escritor con los mismos permisos que usa IIS.
        using var writer = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        var (report, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(1, report.RecordsIngested);
        Assert.Empty(report.SkippedPaths);
    }

    [Fact]
    public void EscritorSiguePudiendoEscribirDuranteLaIngesta()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using var writer = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        Harness.RunSnapshot(workspace);

        var bytes = Encoding.UTF8.GetBytes(TempWorkspace.DataLine("00:00:10") + "\n");
        writer.Write(bytes, 0, bytes.Length);
        writer.Flush();

        Assert.True(new FileInfo(path).Length > 0);
    }

    [Fact]
    public void LineasEscritasDurante_SeIngestanEnLaCorridaSiguiente()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            Harness.RunSnapshot(workspace);

            var bytes = Encoding.UTF8.GetBytes(TempWorkspace.DataLine("00:00:10") + "\n");
            writer.Write(bytes, 0, bytes.Length);
            writer.Flush();
        }

        var (second, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(1, second.RecordsIngested);
        Assert.Equal(2, workspace.CountRows());
    }
}
