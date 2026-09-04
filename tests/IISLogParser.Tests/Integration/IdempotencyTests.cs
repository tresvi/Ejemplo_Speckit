using IISLogParser.Cli;
using IISLogParser.Tests.Fixtures;

namespace IISLogParser.Tests.Integration;

/// <summary>
/// T025 - quickstart escenario 2 y SC-004: ejecutar dos veces produce exactamente la
/// misma cantidad de registros que ejecutarla una vez.
/// </summary>
public class IdempotencyTests
{
    [Fact]
    public void SegundaCorridaIdentica_NoAgregaRegistros()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine(), TempWorkspace.DataLine("00:00:03"));
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine("00:01:00"));

        var (first, _) = Harness.RunSnapshot(workspace);
        var rowsAfterFirst = workspace.CountRows();

        var (second, _) = Harness.RunSnapshot(workspace);

        Assert.Equal(3, first.RecordsIngested);
        Assert.Equal(0, second.RecordsIngested);
        Assert.Equal(rowsAfterFirst, workspace.CountRows());
        Assert.Equal(ExitCode.Success, second.ToExitCode());
    }

    [Fact]
    public void TercerayCuartaCorrida_TampocoAgreganNada()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        Harness.RunSnapshot(workspace);
        Harness.RunSnapshot(workspace);
        Harness.RunSnapshot(workspace);
        Harness.RunSnapshot(workspace);

        Assert.Equal(1, workspace.CountRows());
    }

    [Fact]
    public void PeticionesIdenticasEnDistintaLinea_NoSeColapsan()
    {
        // La identidad es (sitio, archivo, offset): dos peticiones identicas reales
        // son dos filas, no una.
        using var workspace = new TempWorkspace();
        var line = TempWorkspace.DataLine();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", line, line, line);

        Harness.RunSnapshot(workspace);

        Assert.Equal(3, workspace.CountRows());
    }
}
