# Quickstart — Validación de IISLogParser

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contratos**: [contracts/](./contracts/)

Guía de validación de punta a punta. Cada escenario prueba una garantía concreta de la spec y dice
qué esperar. No contiene código de implementación: eso vive en `tasks.md` y en la fase de
implementación.

---

## Prerrequisitos

- SDK de .NET 10 (verificado en el entorno: `10.0.302`).
- No hace falta IIS ni un servidor real: los escenarios usan árboles de logs sintéticos.
- No hace falta red ni credenciales de ningún tipo (Principio III de la constitución).

```bash
dotnet --version
```

## Compilar y testear

```bash
dotnet build -c Debug
```

```bash
dotnet test
```

La suite debe pasar entera sin conexión a red. Si un test necesita red o credenciales, es un
defecto del test, no del entorno.

## Publicar el ejecutable

```bash
dotnet publish src/IISLogParser -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

---

## Preparar un árbol de logs de prueba

Estructura mínima que ejercita sitios múltiples y días múltiples:

```text
sandbox/logs/
├── W3SVC1/
│   ├── u_ex260902.log      # ayer
│   └── u_ex260903.log      # hoy
├── W3SVC2/
│   └── u_ex260903.log      # hoy
└── Temp/                   # carpeta ajena: debe ignorarse y reportarse como omitida
```

Cada archivo arranca con la cabecera W3C y sus líneas de datos según
[contracts/w3c-log-format.md](./contracts/w3c-log-format.md).

---

## Escenario 1 — Snapshot completo (historia P1)

```bash
IISLogParser --mode snapshot --logs-path ./sandbox/logs --database ./sandbox/iislogs.db
```

**Esperado**:

- Reporte de configuración al arrancar, con los cuatro valores efectivos.
- Línea final del tipo `Archivos: 3 | Nuevos: N | Rechazadas: 0 | Omitidos: 0`.
- Código de salida `0`.
- La tabla `log_entries` contiene exactamente una fila por línea de datos de los tres archivos.
- `SELECT DISTINCT site_id FROM log_entries` devuelve `W3SVC1` y `W3SVC2`, y nada más.
- La carpeta `Temp` aparece reportada como ignorada, sin ser un error.

**Verifica**: FR-001, FR-002, FR-007, FR-008, FR-013, SC-001.

## Escenario 2 — Idempotencia (SC-004)

Ejecutar el escenario 1 **dos veces seguidas**, sin tocar los archivos.

**Esperado**:

- La segunda corrida reporta `Nuevos: 0`.
- `SELECT COUNT(*) FROM log_entries` da el mismo número que tras la primera corrida.
- Código de salida `0`.

**Verifica**: FR-011, SC-004. Es la prueba de que la clave `(sitio, archivo, offset)` funciona.

## Escenario 3 — Ingesta incremental

Agregar tres líneas al final de `W3SVC1/u_ex260903.log` y volver a correr el snapshot.

**Esperado**: `Nuevos: 3`, y el conteo total crece exactamente en 3. Los registros previos no se
tocan.

**Verifica**: FR-012, y que la lectura por offset no reprocesa lo ya leído.

## Escenario 4 — Línea parcial

Escribir en el archivo del día una línea **sin salto de línea final** y correr el snapshot.

**Esperado**: esa línea **no** se ingesta. Al completarla con su salto de línea y volver a correr,
sí se ingesta, entera y una sola vez.

**Verifica**: el invariante I-03 y la decisión D-004. Es el escenario que rompe una implementación
ingenua que lea hasta fin de archivo.

## Escenario 5 — Archivo reemplazado (FR-011a)

Reemplazar `W3SVC2/u_ex260903.log` por un archivo **más corto**, con contenido distinto, y correr
el snapshot.

**Esperado**:

- Las filas viejas de ese archivo desaparecen de `log_entries`.
- Se ingesta el contenido nuevo completo, sin duplicados ni restos del anterior.
- Se cumple el invariante I-04.

**Verifica**: FR-011a, transición de estado de `file_progress`.

## Escenario 6 — Línea malformada

Insertar entre líneas válidas una línea con menos valores que columnas declaradas.

**Esperado**: `Rechazadas: 1`; todas las líneas válidas del archivo se ingestan igual; el proceso
no aborta; el reporte identifica sitio, archivo, offset y el contenido íntegro de la línea.

**Verifica**: FR-005, reglas V-02 y V-03.

## Escenario 7 — Modo continuo, arranque con backfill (FR-014a)

Con base de datos vacía y un archivo del día que ya tiene contenido:

```bash
IISLogParser --logs-path ./sandbox/logs --database ./sandbox/iislogs.db
```

(sin más argumentos, para validar de paso los valores por defecto)

**Esperado**:

- El reporte de arranque muestra `Modo: continuous` y `Poll interval: 10 s`.
- Antes del primer ciclo de sondeo, las líneas ya existentes del archivo del día están ingestadas.
- Los archivos de **días anteriores** no se ingestan.
- Mientras corre, agregar líneas al archivo del día hace que aparezcan dentro del intervalo
  siguiente, y solo esas.
- `Ctrl+C` cierra ordenadamente con código `0`, y una corrida posterior no duplica nada.

**Verifica**: FR-014, FR-014a, FR-015, FR-017, FR-018, historias P2 y P3, SC-003.

## Escenario 8 — Fallo parcial (FR-024 a FR-026)

Hacer ilegible uno de los archivos y correr el snapshot.

**Esperado**:

- Los demás archivos se ingestan completos.
- Una línea `OMITIDO  <ruta> — <causa>` y `Omitidos: 1` en el resumen.
- **Código de salida `2`**, no `0`.

**Verifica**: FR-023 a FR-025, SC-007. En modo continuo, el mismo archivo debe reintentarse en el
ciclo siguiente sin que el proceso se detenga.

## Escenario 9 — Argumentos inválidos (FR-020)

```bash
IISLogParser --mode turbo
```

```bash
IISLogParser --poll-interval 0
```

**Esperado en ambos**: mensaje que nombra la causa, código de salida `1`, y **ningún archivo de
base de datos creado ni modificado**.

**Verifica**: FR-020, historia P3 escenario 3.

## Escenario 10 — No bloquear a IIS (FR-006)

Mantener un escritor con el archivo del día abierto en modo escritura, y correr la ingesta.

**Esperado**: la ingesta lee sin error y el escritor sigue pudiendo escribir durante toda la
corrida.

**Verifica**: FR-006, decisión D-003.

---

## Comprobación de rendimiento (SC-002)

Generar un archivo de referencia de 500.000 líneas válidas y correr un snapshot sobre él.

**Esperado**: menos de 10 minutos, que es el piso que fija el SC-002 (≥50.000 registros/minuto). El
diseño de lotes transaccionales debería quedar holgadamente por debajo; si no lo hace, el problema
está en el tamaño de lote o en los pragmas de SQLite (D-008), no en el parseo.

## Comprobación del log estructurado (Principio IV)

Redirigir stderr a un archivo durante cualquier escenario:

```bash
IISLogParser --mode snapshot --logs-path ./sandbox/logs 2> diag.jsonl
```

**Esperado**: cada línea de `diag.jsonl` es un objeto JSON válido con `ts`, `level` y `event`.
Aparecen eventos de borde (`startup`, `batch_committed`, `cycle_completed`, `shutdown`) y **no**
aparece un evento por línea de log parseada: instrumentar el flujo interno paso a paso está
prohibido por el Principio IV.
