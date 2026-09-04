namespace IISLogParser.Cli;

/// <summary>Texto de uso que imprime <c>--help</c>.</summary>
public static class HelpText
{
    public static string Usage =>
        $"""
        IISLogParser — ingesta los logs de IIS de todos los sitios en una tabla unica.

        Uso:
          IISLogParser [--mode <snapshot|continuous>] [--poll-interval <segundos>]
                       [--logs-path <ruta>] [--database <ruta>] [--help]

        Opciones:
          --mode            snapshot procesa todo y termina; continuous queda vigilando
                            los logs del dia. Por defecto: continuous
          --poll-interval   Segundos entre revisiones en modo continuous. Entero mayor
                            que cero. Por defecto: {CliDefaults.PollIntervalSeconds}
          --logs-path       Carpeta raiz de logs de IIS.
                            Por defecto: {CliDefaults.LogsPath}
          --database        Archivo de base de datos de destino.
                            Por defecto: {CliDefaults.DatabasePath}
          --help            Muestra esta ayuda y termina.

        Codigos de salida:
          0  Exito.
          1  Configuracion invalida; no se escribio nada.
          2  Recorrido completo pero con archivos o carpetas omitidos.
          3  Fallo del almacen de datos.
        """;
}
