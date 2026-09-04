using System.Text.Json;
using IISLogParser.Diagnostics;
using IISLogParser.Ingestion;
using IISLogParser.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace IISLogParser.Tests.Contract;

/// <summary>
/// T067 — contrato del log estructurado y Principio IV de la constitución. Lo que se
/// verifica no es solo que el JSON sea válido: es que el flujo interno paso a paso
/// <b>no</b> se instrumente. Un evento por línea parseada enterraría la señal bajo
/// ruido, y el principio lo prohíbe explícitamente.
/// </summary>
public class StructuredLogContractTests
{
    /// <summary>Captura la salida del formateador sin pasar por la consola real.</summary>
    private static (List<JsonDocument> Events, IngestReport Report) RunCapturing(TempWorkspace workspace)
    {
        var captured = new StringWriter();

        using var factory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new CapturingProvider(captured)));

        var diagnostics = new DiagnosticLog(factory.CreateLogger("IISLogParser"));
        var stdout = new StringWriter();
        var report = Harness.RunSnapshotWithDiagnostics(workspace, stdout, diagnostics);

        factory.Dispose();

        var events = captured.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line))
            .ToList();

        return (events, report);
    }

    [Fact]
    public void CadaLinea_EsUnObjetoJsonConLosCamposObligatorios()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var (events, _) = RunCapturing(workspace);

        Assert.NotEmpty(events);

        foreach (var evt in events)
        {
            Assert.True(evt.RootElement.TryGetProperty("ts", out _));
            Assert.True(evt.RootElement.TryGetProperty("level", out _));
            Assert.True(evt.RootElement.TryGetProperty("event", out _));
        }
    }

    [Fact]
    public void SeInstrumentanLosBordes_NoElFlujoLineaALinea()
    {
        using var workspace = new TempWorkspace();
        var lines = Enumerable.Range(0, 200).Select(i => TempWorkspace.DataLine(stem: $"/r/{i}")).ToArray();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", lines);

        var (events, report) = RunCapturing(workspace);

        Assert.Equal(200, report.RecordsIngested);

        // 200 peticiones ingestadas, pero el log tiene un puñado de eventos de borde.
        // Si esto creciera con la cantidad de lineas, seria justo lo que el Principio IV
        // prohibe.
        Assert.True(
            events.Count < 20,
            $"El log emitio {events.Count} eventos para 200 lineas: el flujo interno se esta instrumentando.");
    }

    [Fact]
    public void EventosDeBorde_EstanPresentes()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());

        var (events, _) = RunCapturing(workspace);
        var names = events.Select(e => e.RootElement.GetProperty("event").GetString()).ToList();

        Assert.Contains("file_opened", names);
        Assert.Contains("batch_committed", names);
        Assert.Contains("cycle_completed", names);
    }

    [Fact]
    public void OmisionDeRuta_ProduceSuPropioEvento()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.WriteLog("W3SVC2", "u_ex260903.log", TempWorkspace.DataLine());

        var captured = new StringWriter();
        using var factory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new CapturingProvider(captured)));

        var diagnostics = new DiagnosticLog(factory.CreateLogger("IISLogParser"));
        Harness.RunSnapshotWithDiagnostics(
            workspace,
            new StringWriter(),
            diagnostics,
            new FailingFileSystem(new FileSystem(), "W3SVC2", "acceso denegado"));

        factory.Dispose();

        Assert.Contains("path_skipped", captured.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CambioDeMapaDeCampos_ProduceSuPropioEvento()
    {
        using var workspace = new TempWorkspace();
        workspace.WriteLog("W3SVC1", "u_ex260903.log", TempWorkspace.DataLine());
        workspace.AppendLines("W3SVC1", "u_ex260903.log", "#Fields: cs-method cs-uri-stem", "POST /api");

        var (events, _) = RunCapturing(workspace);
        var names = events.Select(e => e.RootElement.GetProperty("event").GetString()).ToList();

        Assert.Contains("field_map_changed", names);
    }

    private sealed class CapturingProvider(TextWriter writer) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(writer);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(TextWriter writer) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var entry = new Microsoft.Extensions.Logging.Abstractions.LogEntry<TState>(
                    logLevel, "IISLogParser", eventId, state, exception, formatter);

                new JsonEventFormatter().Write(in entry, null, writer);
            }
        }
    }
}
