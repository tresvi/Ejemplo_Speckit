namespace IISLogParser.Cli;

/// <summary>
/// Códigos de salida del contrato (contracts/cli.md). Un planificador que reciba
/// <see cref="Success"/> debe poder asumir que la corrida fue completa: por eso
/// <see cref="CompletedWithSkips"/> existe como código propio y nunca se colapsa a cero.
/// </summary>
public enum ExitCode
{
    /// <summary>Snapshot completo sin omisiones, cierre ordenado del modo continuo, o --help.</summary>
    Success = 0,

    /// <summary>Argumentos inválidos o carpeta de logs inaccesible. No se escribió nada.</summary>
    InvalidConfiguration = 1,

    /// <summary>El recorrido terminó, pero se omitió al menos un archivo o carpeta.</summary>
    CompletedWithSkips = 2,

    /// <summary>El almacén no pudo crearse, abrirse o escribirse.</summary>
    StoreFailure = 3,
}
