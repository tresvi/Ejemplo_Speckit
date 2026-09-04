# Contrato: Interfaz de línea de comandos

**Feature**: [../spec.md](../spec.md) | **Plan**: [../plan.md](../plan.md)

Superficie externa de `IISLogParser`. Este contrato es lo que verifican los tests de
`tests/IISLogParser.Tests/Contract/CliContractTests.cs`. Un cambio acá es un cambio de contrato.

---

## Invocación

```text
IISLogParser [--mode <snapshot|continuous>] [--poll-interval <segundos>]
             [--logs-path <ruta>] [--database <ruta>] [--help]
```

## Opciones

| Opción | Valor | Por defecto | Regla |
|---|---|---|---|
| `--mode` | `snapshot` \| `continuous` | `continuous` | Sin distinguir mayúsculas. Cualquier otro valor es un error de argumentos. |
| `--poll-interval` | Entero de segundos | `10` | Debe ser un entero > 0. Solo aplica al modo `continuous`; si se pasa junto a `--mode snapshot` se acepta y se ignora, informándolo en el reporte de configuración. |
| `--logs-path` | Ruta de directorio | `C:\inetpub\logs\LogFiles` | Debe existir y ser legible al arrancar. |
| `--database` | Ruta de archivo | `.\iislogs.db` en el directorio de trabajo | Se crea si no existe, junto con su esquema. |
| `--help` | — | — | Imprime el uso en stdout y termina con código 0, sin tocar el almacén. |

**FR cubiertos**: FR-002, FR-018, FR-019, FR-020, FR-021.

## Códigos de salida

| Código | Significado | Cuándo |
|---|---|---|
| `0` | Éxito | Snapshot completo sin omisiones; o modo continuo cerrado ordenadamente por el operador; o `--help`. |
| `1` | Configuración inválida | Argumento desconocido, modo inválido, intervalo no entero o ≤ 0, `--logs-path` inexistente o ilegible. **No se escribe nada en el almacén** (FR-020). |
| `2` | Completado con omisiones | Snapshot terminó su recorrido pero omitió al menos un archivo o carpeta de sitio (FR-023, FR-024). Los datos ingestados son válidos; la corrida es incompleta. |
| `3` | Fallo de almacén | El almacén no pudo crearse, abrirse o escribirse. Interrumpe cualquiera de los dos modos. |

**Regla**: el código `2` no puede enmascararse como `0`. Un planificador que reciba `0` debe poder
asumir que la corrida fue completa.

## Salida estándar (stdout) — reporte al operador

Texto legible. Dos bloques.

### Al arrancar (FR-021)

```text
IISLogParser
  Modo             : continuous
  Carpeta de logs  : C:\inetpub\logs\LogFiles
  Base de datos    : C:\ops\iislogs.db
  Poll interval    : 10 s
```

En modo `snapshot`, la línea de *Poll interval* se omite salvo que el operador la haya pasado
explícitamente, en cuyo caso se muestra como `10 s (ignorado en modo snapshot)`.

### Al cerrar un ciclo o la ejecución (FR-022)

```text
[2026-09-03T14:22:31Z] Archivos: 12 | Nuevos: 4831 | Rechazadas: 2 | Omitidos: 0
```

En modo `snapshot` se emite una sola vez al final; en modo `continuous`, una vez por ciclo. Un
ciclo sin novedades emite igual su línea con ceros: el silencio no debe ser ambiguo respecto de un
proceso colgado.

Cuando hay omisiones, cada una se detalla antes de la línea de resumen:

```text
  OMITIDO  W3SVC3/u_ex260903.log — acceso denegado
```

## Salida de error (stderr) — log estructurado

JSON por línea, un objeto por evento. Se instrumentan **solo** límites, decisiones y fallos; nunca
el flujo línea a línea (Principio IV de la constitución).

```json
{"ts":"2026-09-03T14:22:31.412Z","level":"Information","event":"batch_committed","site":"W3SVC1","file":"W3SVC1/u_ex260903.log","offset":184320,"rows":5000}
```

Campos obligatorios en todo evento: `ts`, `level`, `event`. Los demás dependen del evento.

**Eventos del contrato**: `startup`, `file_opened`, `file_replaced`, `field_map_changed`,
`batch_committed`, `path_skipped`, `cycle_completed`, `shutdown`, `fatal_error`.

**Prohibido en stderr**: secretos de cualquier tipo. Los campos de los logs de IIS no se consideran
datos personales en este proyecto (decisión registrada en `## Clarifications` de la spec), de modo
que el contenido de una línea rechazada se reporta íntegro, sin enmascarar (FR-005).

## Comportamiento por modo

### `snapshot`

1. Valida configuración. Error ⇒ código 1, sin tocar el almacén.
2. Abre o crea el almacén y su esquema.
3. Recorre todos los sitios y **todos** los archivos de cada uno.
4. Emite el reporte final y termina: código 0, o 2 si hubo omisiones.

### `continuous`

1. Valida configuración. Error ⇒ código 1.
2. Abre o crea el almacén y su esquema.
3. Ingesta el archivo del día de cada sitio desde su marca de progreso, o desde el principio si no
   la tiene (FR-014a).
4. Repite indefinidamente: procesa el conjunto vigilado, emite la línea de ciclo, espera el
   *Poll Interval*. Los ciclos no se solapan.
5. Ante `Ctrl+C`: confirma el lote en curso, emite `shutdown` y termina con código 0 (FR-017).

**Conjunto vigilado en cada ciclo** = archivo del día de cada sitio ∪ todo archivo con bytes
pendientes respecto de su marca de progreso. La segunda parte es lo que impide perder la cola del
archivo del día anterior cuando cambia la fecha (FR-016).
