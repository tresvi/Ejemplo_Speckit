# Ejemplo_Speckit

Repositorio de desarrollo dirigido por especificación (Spec Kit). El trabajo se organiza por
features bajo `specs/`, y cada fase deja su propio commit.

## Reglas que mandan sobre todo lo demás

La **constitución del proyecto** está en [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
(v1.0.0). Ante conflicto entre este archivo y la constitución, manda la constitución. Léela antes
de escribir código.

Lo que más te va a afectar en el día a día:

- **Test-First (no negociable)**: se escribe el test, se lo ve fallar, recién entonces se
  implementa. Ninguna tarea de implementación empieza sin su test en rojo.
- **Suite determinista**: sin red, sin credenciales de proveedor. Si un test las necesita, el
  defecto está en el test.
- **Logs estructurados**, en los bordes: límites del sistema, decisiones y fallos. El flujo interno
  paso a paso NO se instrumenta.
- **Un commit por fase de Spec Kit** sobre la rama de la feature.
- **Merge a `main`**: tests en verde y `/speckit-converge` sin deriva.

## Feature en curso

`001-iis-log-parser` — herramienta de consola que parsea logs de IIS a una tabla única de SQLite,
en modo *snapshot* o *continuous*. El directorio de la feature es la fuente de verdad:

- [`specs/001-iis-log-parser/spec.md`](specs/001-iis-log-parser/spec.md) — qué y por qué
- [`specs/001-iis-log-parser/plan.md`](specs/001-iis-log-parser/plan.md) — cómo, y estructura de código
- [`specs/001-iis-log-parser/research.md`](specs/001-iis-log-parser/research.md) — decisiones técnicas D-001..D-013
- [`specs/001-iis-log-parser/data-model.md`](specs/001-iis-log-parser/data-model.md) — entidades y reglas
- [`specs/001-iis-log-parser/contracts/`](specs/001-iis-log-parser/contracts/) — CLI, esquema de base, formato W3C
- [`specs/001-iis-log-parser/quickstart.md`](specs/001-iis-log-parser/quickstart.md) — escenarios de validación

## Stack

- **.NET 10** (`net10.0`), C#. SDK verificado: 10.0.302.
- **SQLite** vía `Microsoft.Data.Sqlite`, sin ORM.
- **Logging**: `Microsoft.Extensions.Logging` con formateador JSON de consola.
- **Tests**: xUnit.
- Código en `src/IISLogParser`, tests en `tests/IISLogParser.Tests`.

## Cómo correr

```bash
dotnet build -c Debug
```

```bash
dotnet test
```

```bash
dotnet publish src/IISLogParser -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Qué NO hacer

- **NO** escribir código de producción sin un test fallando primero.
- **NO** abrir los archivos de log sin `FileShare.ReadWrite | FileShare.Delete`: bloquearía a IIS.
- **NO** confirmar una línea sin salto de línea final. La marca de progreso nunca apunta a media
  línea.
- **NO** separar la escritura de filas de la actualización de la marca de progreso: van en la misma
  transacción, o se pierden o duplican registros ante una caída.
- **NO** consultar antes de insertar para evitar duplicados: la idempotencia la da el índice único
  `(site_id, source_file, line_offset)` con `INSERT OR IGNORE`.
- **NO** emitir un evento de log por línea parseada. Solo bordes, decisiones y fallos.
- **NO** devolver código de salida 0 en un snapshot que omitió archivos: eso es código 2.
- **NO** parsear por posición fija de columna. El mapa lo declara la directiva `#Fields:`, y puede
  cambiar a mitad de archivo.
- **NO** convertir zonas horarias: las marcas temporales se guardan como IIS las escribió.
- **NO** agregar dependencias fuera de las listadas sin justificarlo contra la constitución.
- **NO** ampliar el alcance de la v1: no hay consulta, reportes, retención, purga ni ingesta
  remota.
