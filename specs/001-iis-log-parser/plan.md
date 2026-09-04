# Implementation Plan: IISLogParser

**Branch**: `001-iis-log-parser` | **Date**: 2026-09-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-iis-log-parser/spec.md`

## Summary

Herramienta de consola que recorre la carpeta de logs de IIS, parsea los archivos en formato W3C
extendido de cada sitio y vuelca cada petición como una fila en una tabla única de SQLite, con el
identificador del sitio en cada fila. Opera en dos modos: *snapshot* (procesa todo lo encontrado y
termina) y *continuous* (queda vigilando los archivos del día y los archivos con contenido
pendiente, sondeando cada *Poll Interval* segundos). Sin argumentos, arranca en modo continuo con
un intervalo de 10 segundos.

El enfoque técnico se apoya en tres decisiones que sostienen las garantías de la spec: lectura
incremental por **offset de bytes** confirmando solo líneas completas; identidad de registro
`(sitio, archivo, offset)` con índice único, que traslada la idempotencia al motor de base de
datos; y **una única transacción por lote que escribe filas y marca de progreso a la vez**, que es
lo que hace imposible perder o duplicar registros ante una caída. El detalle y las alternativas
descartadas están en [research.md](./research.md).

## Technical Context

**Language/Version**: C# sobre .NET 10 (`net10.0`). SDK 10.0.302 verificado en el entorno.

**Primary Dependencies**: `Microsoft.Data.Sqlite` (acceso a datos, sin ORM);
`Microsoft.Extensions.Logging` + formateador JSON de consola (log estructurado, Principio IV).
Análisis de la línea de comandos propio, sin dependencia externa (D-011).

**Storage**: SQLite, archivo único. Dos tablas: `log_entries` (tabla única de registros, todos los
sitios) y `file_progress` (marca de progreso por archivo). Esquema en
[contracts/database-schema.md](./contracts/database-schema.md).

**Testing**: xUnit. Tres niveles: unitario (parser, CLI, resolución de fecha), integración
(árboles de directorios y base SQLite en temporales) y contrato (superficie de CLI y esquema).
Suite determinista, sin red y sin credenciales.

**Target Platform**: Windows Server con IIS (ejecución local sobre la carpeta de logs). Publicación
como ejecutable único autocontenido `IISLogParser.exe`.

**Project Type**: Aplicación de consola (CLI), proyecto único más proyecto de tests.

**Performance Goals**: SC-002 exige ≥50.000 registros por minuto (≈834/s). El diseño de lotes
transaccionales con comando preparado apunta a un orden de magnitud por encima de ese piso; el
límite práctico es la E/S de disco, no el parseo.

**Constraints**: la lectura no debe impedir la escritura de IIS (`FileShare.ReadWrite | Delete`);
memoria acotada e independiente del tamaño del archivo (lectura en streaming, lotes de 5.000
líneas); los ciclos del modo continuo no se solapan.

**Scale/Scope**: decenas de sitios por servidor, archivos históricos de orden gigabyte, ingesta
acumulada de millones de filas en una sola tabla.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Gates derived from `.specify/memory/constitution.md` v1.0.0. Mark each PASS / FAIL / N-A.
Any FAIL must be justified in Complexity Tracking below, or the design must change.

- [x] **I. Cero Alucinación** — **N-A**. La feature no invoca ningún modelo. La spec lo declara
      explícitamente en su sección *Source of Truth & Human Review*, con justificación: los
      archivos de log son la única fuente de datos y el sistema los transcribe sin interpretación
      derivada de un modelo. Conforme al ámbito de aplicación de la constitución, no se inventa
      estructura para satisfacer este principio.
- [x] **II. Aislamiento de la Capa de IA** — **N-A**. No hay llamadas a modelos, ni SDK de
      proveedor, ni prompts en el diseño.
- [x] **III. Test-First** — **PASS**. El plan ordena test antes de implementación en cada unidad
      (ver *Orden de construcción*). La suite es determinista y corre sin red ni credenciales: la
      única dependencia externa es el sistema de archivos, sustituido por directorios temporales,
      y el reloj y el disparador de ciclo se inyectan para que el modo continuo se teste sin
      esperas reales (D-013).
- [x] **IV. Trazabilidad** — **PASS**. Log estructurado en JSON por línea a stderr, separado del
      reporte legible al operador en stdout (D-012). Se instrumentan solo límites, decisiones y
      fallos; el flujo línea a línea no se instrumenta, como exige el principio. Sin secretos en
      el flujo. La cláusula de modelo/prompt/fuente es N-A por ausencia de modelo. La restricción
      de PII se cumple porque el propietario del proyecto declaró, en la fase de clarificación,
      que ningún campo de los logs de IIS se clasifica como dato personal en este proyecto; la
      decisión está registrada en `## Clarifications` y en *Assumptions* de la spec.
- [x] **V. Flujo de Spec Kit** — **PASS con deuda pendiente**. El plan se compromete a un commit
      por fase sobre `001-iis-log-parser`, y a exigir tests en verde más `/speckit-converge` sin
      deriva antes del merge a `main`. **Deuda abierta**: las fases `specify` y `clarify` ya
      produjeron artefactos que todavía no están commiteados. Deben cerrarse en sus propios
      commits antes de avanzar a `/speckit-tasks`; de lo contrario el historial no reflejará las
      fases y el principio quedará incumplido de hecho.

**Re-evaluación post-diseño (Phase 1)**: sin cambios. El diseño no introdujo ninguna llamada a
modelo, ninguna dependencia con credenciales y ningún componente que exija acceso a red. La tabla
*Complexity Tracking* queda vacía: no hay violaciones que justificar.

## Project Structure

### Documentation (this feature)

```text
specs/001-iis-log-parser/
├── plan.md              # Este archivo
├── spec.md              # Especificación (fases specify + clarify)
├── research.md          # Phase 0 — decisiones técnicas D-001..D-013
├── data-model.md        # Phase 1 — entidades, campos, reglas de validación
├── quickstart.md        # Phase 1 — guía de validación ejecutable
├── checklists/
│   └── requirements.md  # Checklist de calidad de la spec
├── contracts/
│   ├── cli.md           # Contrato de la interfaz de línea de comandos
│   ├── database-schema.md  # Contrato del esquema de la base de datos
│   └── w3c-log-format.md   # Contrato del formato de entrada que se acepta
└── tasks.md             # Phase 2 — generado por /speckit-tasks, NO por este comando
```

### Source Code (repository root)

```text
src/
└── IISLogParser/
    ├── IISLogParser.csproj
    ├── Program.cs                    # Composición y punto de entrada
    ├── Cli/
    │   ├── CliOptions.cs             # Configuración efectiva (modo, intervalo, rutas)
    │   ├── CliParser.cs              # Argumentos → CliOptions | error (función pura)
    │   ├── ExitCode.cs               # Códigos de salida del contrato
    │   └── HelpText.cs
    ├── Discovery/
    │   ├── ISiteDiscovery.cs
    │   ├── SiteDiscovery.cs          # Carpetas W3SVC<n> bajo la raíz; reporta omitidas
    │   ├── LogFileName.cs            # u_exYYMMDD.log / exYYMMDD.log → fecha
    │   └── WatchSetResolver.cs       # Archivo de hoy + archivos con bytes pendientes
    ├── Parsing/
    │   ├── FieldMap.cs               # Directiva #Fields: → mapa de columnas
    │   ├── W3CLineParser.cs          # Línea + FieldMap → LogRecord | rechazo
    │   └── LogRecord.cs
    ├── Storage/
    │   ├── ILogStore.cs
    │   ├── SqliteLogStore.cs         # Lotes transaccionales, INSERT OR IGNORE
    │   ├── SchemaInitializer.cs      # Creación idempotente del esquema
    │   └── FileProgress.cs
    ├── Ingestion/
    │   ├── IFileSystem.cs            # Frontera delgada de E/S (testabilidad, D-013)
    │   ├── FileSystem.cs
    │   ├── FileIngestor.cs           # Lectura por offset, líneas completas, lotes
    │   ├── SnapshotRunner.cs
    │   ├── ContinuousRunner.cs       # Bucle sin solapamiento, reloj inyectado
    │   └── IngestReport.cs           # Contadores del reporte (FR-022)
    └── Diagnostics/
        ├── OperatorReport.cs         # Salida legible a stdout (FR-021, FR-022)
        └── LoggingSetup.cs           # Log estructurado JSON a stderr (D-012)

tests/
└── IISLogParser.Tests/
    ├── IISLogParser.Tests.csproj
    ├── Unit/
    │   ├── CliParserTests.cs
    │   ├── FieldMapTests.cs
    │   ├── W3CLineParserTests.cs
    │   ├── LogFileNameTests.cs
    │   └── WatchSetResolverTests.cs
    ├── Integration/
    │   ├── SnapshotIngestionTests.cs
    │   ├── IdempotencyTests.cs
    │   ├── IncrementalIngestionTests.cs
    │   ├── FileReplacementTests.cs
    │   ├── ContinuousModeTests.cs
    │   └── PartialFailureTests.cs
    ├── Contract/
    │   ├── CliContractTests.cs
    │   └── DatabaseSchemaContractTests.cs
    └── Fixtures/
        ├── TempLogTree.cs            # Árbol de directorios sintético por test
        └── SampleLogs/               # Archivos de log de referencia
```

**Structure Decision**: proyecto único de CLI más proyecto de tests, bajo `src/` y `tests/` en la
raíz del repositorio. No hay frontend, servicio web ni componente móvil que justifiquen una
estructura multi-proyecto. La separación interna por carpetas (`Discovery`, `Parsing`, `Storage`,
`Ingestion`) sigue las cuatro responsabilidades que la spec distingue explícitamente, de modo que
cada requisito funcional cae en un único lugar. Las dos interfaces del diseño (`IFileSystem`,
`ILogStore`) existen por testabilidad concreta y demostrable —simular un archivo ilegible y
verificar el contrato de almacenamiento—, no por abstracción especulativa.

### Desviaciones de estructura, justificadas

Registradas al cerrar el hallazgo F8 de `/speckit-converge`. Ninguna cambia el diseño; todas son
decisiones de ubicación que aparecieron al implementar.

| Desviación | Por qué |
|---|---|
| `Cli/Application.cs` no estaba previsto | La composición vivía implícita en `Program.cs`. Separarla permite que los tests de contrato ejerciten la superficie completa —incluida la garantía de que una invocación rechazada no crea la base— sin lanzar un proceso. |
| `Diagnostics/DiagnosticLog.cs` y `Diagnostics/JsonEventFormatter.cs` no estaban previstos | El contrato de CLI fija los nombres de campo `ts`/`level`/`event`, y el formateador JSON que trae la consola emite los suyos. `JsonEventFormatter` los traduce; `DiagnosticLog` concentra los nueve eventos del contrato para que ningún llamador invente uno nuevo. |
| `Ingestion/IClock.cs` no estaba previsto | El plan pedía inyectar reloj y disparador de ciclo (D-013) sin nombrar dónde. Viven juntos porque ambos existen por la misma razón: que la suite no espere tiempo real. |
| `ISiteDiscovery`, `FileProgress` y `FileSystem` viven dentro de otros archivos | Cada uno tiene menos de treinta líneas y no se entiende separado de su implementación o de su interfaz. Un archivo por tipo habría sido ceremonia sin lector. |
| `Discovery/LogsRootUnavailableException` no estaba previsto | Surgió del hallazgo F1: el tratamiento correcto de una raíz ilegible depende del modo —configuración inviable en snapshot, omisión reintentable en continuo—, y sin un tipo propio la excepción se confundía con un fallo del almacén. |

## Orden de construcción (Test-First, Principio III)

Cada paso empieza por su test, se verifica en rojo, y recién entonces se implementa. El orden
sigue la prioridad de las historias de usuario para que cada tramo entregue valor comprobable.

1. **Esqueleto y contrato de CLI** — `CliParser`, `ExitCode`, `HelpText`. Cubre FR-018 a FR-021 y
   la historia P3 completa. Es el tramo más barato y fija la superficie externa.
2. **Parseo W3C** — `FieldMap`, `W3CLineParser`. Cubre FR-003 a FR-005. Funciones puras: la mayor
   densidad de casos borde con el menor costo de test.
3. **Esquema y almacenamiento** — `SchemaInitializer`, `SqliteLogStore`. Cubre FR-007 a FR-012.
   Incluye el test de idempotencia (SC-004) antes de que exista ingesta real.
4. **Descubrimiento de sitios y archivos** — `SiteDiscovery`, `LogFileName`. Cubre FR-001, FR-002.
5. **Ingesta de un archivo** — `FileIngestor`: offset, línea parcial, truncamiento, lotes.
   Cubre FR-006, FR-011a, y el corazón de D-004 y D-007.
6. **Modo snapshot** — `SnapshotRunner`. Cierra la historia P1 (FR-013, FR-022, FR-023).
7. **Tolerancia a fallo parcial** — omisión, reporte y código de salida. Cubre FR-024 a FR-026.
8. **Modo continuo** — `WatchSetResolver`, `ContinuousRunner`, apagado ordenado. Cierra la
   historia P2 (FR-014, FR-014a, FR-015, FR-016, FR-017).
9. **Log estructurado** — `LoggingSetup`, instrumentación en los bordes ya construidos
   (Principio IV).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

Sin violaciones. Las cinco puertas resultan PASS o N-A, y ninguna decisión de diseño requiere
justificación de complejidad.
