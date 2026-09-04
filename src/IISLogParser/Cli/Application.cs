using IISLogParser.Diagnostics;
using IISLogParser.Discovery;
using IISLogParser.Ingestion;
using IISLogParser.Storage;
using Microsoft.Data.Sqlite;

namespace IISLogParser.Cli;

/// <summary>
/// Composición de la corrida. Vive separada de <c>Program</c> para que los tests de
/// contrato puedan ejercitar la superficie completa —incluida la garantía de que una
/// invocación rechazada no crea la base— sin lanzar un proceso.
/// </summary>
public sealed class Application(
    IFileSystem fileSystem,
    TextWriter stdout,
    DiagnosticLog diagnostics,
    Func<string, ILogStore>? storeFactory = null)
{
    private readonly Func<string, ILogStore> _storeFactory =
        storeFactory ?? (path => new SqliteLogStore(path));

    public int Run(string[] args, CancellationToken cancellationToken)
    {
        var report = new OperatorReport(stdout);
        var parsed = CliParser.Parse(args);

        if (parsed.HelpRequested)
        {
            report.WriteLine(HelpText.Usage);
            return (int)ExitCode.Success;
        }

        if (parsed.Error is not null)
        {
            // FR-020: se informa la causa y no se toca el almacen.
            report.WriteError(parsed.Error);
            return (int)ExitCode.InvalidConfiguration;
        }

        var options = parsed.Options!;

        if (!fileSystem.DirectoryExists(options.LogsPath))
        {
            report.WriteError($"La carpeta de logs no existe o no es legible: {options.LogsPath}");
            return (int)ExitCode.InvalidConfiguration;
        }

        // FR-020: la validacion completa ocurre antes de tocar el almacen, de modo que
        // una invocacion rechazada no deja ningun archivo de base creado.
        var databasePath = Path.GetFullPath(options.DatabasePath);
        report.WriteStartup(options, databasePath);
        diagnostics.Startup(
            options.Mode.ToString().ToLowerInvariant(),
            options.LogsPath,
            databasePath,
            options.PollIntervalSeconds);

        try
        {
            using var store = _storeFactory(databasePath);
            store.Initialize();

            return options.Mode switch
            {
                RunMode.Snapshot => RunSnapshot(options, store, report, cancellationToken),
                _ => RunContinuous(options, store, report, cancellationToken),
            };
        }
        catch (SqliteException ex)
        {
            report.WriteError($"Fallo del almacen de datos: {ex.Message}");
            diagnostics.FatalError("store_failure", ex);
            return (int)ExitCode.StoreFailure;
        }
        catch (IOException ex)
        {
            report.WriteError($"Fallo del almacen de datos: {ex.Message}");
            diagnostics.FatalError("store_failure", ex);
            return (int)ExitCode.StoreFailure;
        }
    }

    private int RunSnapshot(
        CliOptions options,
        ILogStore store,
        OperatorReport report,
        CancellationToken cancellationToken)
    {
        var runner = new SnapshotRunner(
            new SiteDiscovery(fileSystem),
            new FileIngestor(fileSystem, store, diagnostics, report),
            fileSystem,
            diagnostics,
            report);

        var result = runner.Run(options.LogsPath, cancellationToken);
        diagnostics.Shutdown("snapshot_completed");

        return (int)result.ToExitCode();
    }

    private int RunContinuous(
        CliOptions options,
        ILogStore store,
        OperatorReport report,
        CancellationToken cancellationToken)
    {
        var runner = new ContinuousRunner(
            new SiteDiscovery(fileSystem),
            new FileIngestor(fileSystem, store, diagnostics, report),
            fileSystem,
            store,
            diagnostics,
            report,
            SystemClock.Instance);

        runner.Run(options.LogsPath, TimeSpan.FromSeconds(options.PollIntervalSeconds), cancellationToken);

        // FR-017: el cierre ordenado es exito, no error.
        return (int)ExitCode.Success;
    }
}
