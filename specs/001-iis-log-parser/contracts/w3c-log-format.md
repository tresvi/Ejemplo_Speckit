# Contrato: Formato de entrada aceptado

**Feature**: [../spec.md](../spec.md) | **Investigación**: [../research.md](../research.md) (D-005)

Define exactamente qué entrada acepta el parser y qué hace con lo que no encaja. Verificado por
`tests/IISLogParser.Tests/Unit/W3CLineParserTests.cs` y `FieldMapTests.cs`.

---

## Estructura de un archivo

Formato **W3C extendido**, el predeterminado de IIS. Codificación UTF-8; se tolera y descarta la
marca BOM inicial.

```text
#Software: Microsoft Internet Information Services 10.0
#Version: 1.0
#Date: 2026-09-03 00:00:02
#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip cs(User-Agent) sc-status sc-substatus sc-win32-status time-taken
2026-09-03 00:00:02 10.0.0.4 GET /index.html - 80 - 10.0.0.99 Mozilla/5.0 200 0 0 15
2026-09-03 00:00:03 10.0.0.4 POST /api/items id=42 80 jdoe 10.0.0.99 Mozilla/5.0 201 0 0 87
```

### Líneas de directiva y comentario

Toda línea que empieza con `#` es directiva o comentario y **nunca** se ingesta (FR-004). Solo
`#Fields:` tiene efecto sobre el parseo; el resto se ignora.

### Directiva `#Fields:`

Declara los nombres de columna, separados por espacios, en el orden en que aparecen los valores.
Puede aparecer **más de una vez** en el mismo archivo: cada aparición reemplaza el mapa vigente
para las líneas siguientes. IIS reescribe esta directiva cuando cambia la configuración de logging
del sitio sin reiniciarlo, así que el caso es real.

### Líneas de datos

Valores separados por **un espacio simple**. El valor `-` significa campo vacío y se persiste como
nulo (V-04). Los valores no llevan comillas ni escapes: el formato W3C de IIS reemplaza por `+` los
espacios dentro de un valor, de modo que el conteo de valores es fiable.

---

## Correspondencia de campos

| Campo W3C | Columna |
|---|---|
| `date` + `time` | `timestamp_utc` (combinados) |
| `s-ip` | `s_ip` |
| `cs-method` | `cs_method` |
| `cs-uri-stem` | `cs_uri_stem` |
| `cs-uri-query` | `cs_uri_query` |
| `s-port` | `s_port` |
| `cs-username` | `cs_username` |
| `c-ip` | `c_ip` |
| `cs-version` | `cs_version` |
| `cs(User-Agent)` | `cs_user_agent` |
| `cs(Referer)` | `cs_referer` |
| `cs(Cookie)` | `cs_cookie` |
| `cs-host` | `cs_host` |
| `sc-status` | `sc_status` |
| `sc-substatus` | `sc_substatus` |
| `sc-win32-status` | `sc_win32_status` |
| `sc-bytes` | `sc_bytes` |
| `cs-bytes` | `cs_bytes` |
| `time-taken` | `time_taken` |
| `s-sitename` | `s_sitename` |
| `s-computername` | `s_computername` |
| *cualquier otro* | `extra_fields` (JSON) |

Normalización: `-` y los paréntesis de `cs(X)` pasan a `_`; comparación sin distinguir mayúsculas.

---

## Reglas de rechazo

Una línea **rechazada** se cuenta, se reporta con sitio, archivo, offset y contenido íntegro
(FR-005), y no interrumpe el procesamiento del resto del archivo.

| Regla | Caso | Motivo |
|---|---|---|
| V-01 | Línea de datos antes de cualquier `#Fields:` | No hay mapa de columnas; adivinarlo sería inventar. |
| V-02 | Menos valores que columnas declaradas | No se puede saber cuáles faltan. |
| V-03 | Más valores que columnas declaradas | El formato no admite valores sin campo. |

**No es rechazo** un valor que no convierte a su tipo numérico (V-05): la columna queda nula y el
par campo/valor original se preserva en `extra_fields`. Se prefiere una fila con una anomalía
preservada antes que perder la petición entera.

---

## Nombres de archivo y fecha

| Patrón | Cuándo lo usa IIS | Fecha |
|---|---|---|
| `u_exYYMMDD.log` | Rotación diaria con nombres en UTC (predeterminado) | De `YYMMDD` |
| `exYYMMDD.log` | Rotación diaria con nombres en hora local | De `YYMMDD` |

Un archivo cuyo nombre no encaja en ningún patrón conocido **se ingesta igual en modo snapshot**,
pero no puede considerarse "de hoy" en modo continuo, porque no hay forma confiable de fecharlo por
nombre. Entra al conjunto vigilado solo si tiene bytes pendientes.

Formatos fuera de alcance en esta versión, según los supuestos de la spec: rotación horaria
(`u_exYYMMDDHH.log`), NCSA, IIS nativo y el log binario centralizado.

---

## Marcas temporales

Los campos `date` y `time` se combinan en `timestamp_utc` como ISO-8601
(`YYYY-MM-DDTHH:MM:SS`). **No hay conversión de zona horaria**: se conserva lo que IIS escribió,
que por configuración predeterminada es UTC. Si el sitio está configurado con hora local, los
valores serán hora local, y es responsabilidad de quien consulte saberlo — la herramienta no
adivina ni convierte.
