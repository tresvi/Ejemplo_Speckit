# Ejemplo Paso a Paso — Spec Kit

Flujo completo de trabajo con [Spec Kit](https://github.com/github/spec-kit), desde el refinamiento de la idea hasta la implementación.


---

## 1. Preparación

### 1.1. Descargar Node en Windows

Descargar desde [nodejs.org](https://nodejs.org).

### 1.2. Verificar la instalación

Abrir una consola nueva de PowerShell o Terminal y verificar que `node` y `npm` estén instalados:

```powershell
node -v
npm -v
```

### 1.3. Instalar `uv` en Windows

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
```

### 1.4. Instalar Spec Kit

```powershell
uv tool install specify-cli --from git+https://github.com/github/spec-kit.git
```

---

## 2. Ejercicio

### Paso 1 — Preparar el repositorio

Pegar el `AGENTS.md` inicial y el `CLAUDE.md` (o `COPILOT.md`) en el repositorio (nuevo o ya existente).

### Paso 2 — Agregar las skills

Pegar las skills `create-prd` y `conventional-commit`.

### Paso 3 — Inicializar Spec Kit

Ejecutar en el directorio vacío:

```powershell
specify init . --integration claude
```

### Paso 4 — Ejecutar `/speckit.constitution`  (-> Esta constitution se podria reutilizar en cualquier proyecto de cualquier tecnología asi como esta)

```text
/speckit.constitution Este proyecto sigue estos principios no negociables:

CERO ALUCINACIÓN. El sistema nunca emite datos que no estén en su fuente de
verdad. Toda salida derivada de un modelo cita su registro de origen. Si el
modelo no puede fundamentarla, se marca para revisión humana en vez de
entregarse. Fallar hacia la revisión, nunca hacia la invención.

AISLAMIENTO DE LA CAPA DE IA. Toda invocación a modelos vive en un módulo
dedicado detrás de una interfaz. La lógica de negocio nunca importa el SDK
del proveedor ni construye prompts; los prompts son archivos versionados.

TEST-FIRST. Tests antes que implementación, rojo-verde-refactor. Ningún test
del ciclo normal llama a un modelo real: fakes o respuestas grabadas.

TRAZABILIDAD. Toda llamada a un modelo registra modelo, versión de prompt y
fuente consultada. Los logs son estructurados y nunca contienen secretos ni
PII. No se instrumenta el flujo interno, solo límites, decisiones y fallos.

FLUJO. Cada fase de Spec Kit cierra en su propio commit sobre la rama de la
feature. El merge a main exige tests en verde y /speckit.converge sin deriva.
```

### Paso 5 — Ejecutar `/speckit-specify`   

Puede ejecutarse sobre un prompt, un PRD existente, un ticket de Jira o una tarjeta de GitHub Projects.

**Opción A — a partir de un PRD existente:**

```text
/speckit-specify Generá el spec a partir del PRD en docs/PRD.md
```

> Ajustar la ruta según cómo se llame el archivo.

**Opción B — dándole el prompt directamente:**

```text
/speckit-specify Quiero desarrollar un aplicativo de consola que al invocarlo vaya a la
carpeta de los logs de IIS y parsee todos los archivos de logs encontrados. Al resultado
del parseo, lo coloque en una tabla, en una base de datos SQLite.

En principio, todos los registros de todas las carpetas que corresponden a cada sitio,
deberían ir en una misma tabla. Por esto, la tabla deberá tener un campo que identifique
el sitio (W3SVC1, W3SVC2, etc).

Pero no solo eso, también debe:

- El programa tendrá dos modos:
    - El modo snapshot, que cuando lo ejecute levantará todos los logs que encuentre y
      luego finalizará.
    - Y el modo continuous. Este modo lo dejará en ejecución continua y quedará vigilando
      los logs que encuentre el día de hoy. La revisión de si un log cambió (es decir, si
      tiene líneas nuevas) cada un intervalo de segundos llamado "Poll Interval". En caso
      de haber cambiado, debe actualizar la tabla de logs con los logs nuevos.
- El programa se llamará IISLogParser.
- Si lo ejecuto sin ningún parámetro, el programa se ejecutará en modo continuous, con un
  Poll Interval por default de 10 segundos.
```

### Paso 6 — Ejecutar `/speckit-clarify`   (obligatorio si aparecen [NEED CLARIFICATION], si no, es opcional pero recomendado)

```text
/speckit-clarify
```

> Recordar que todas las preguntas que se hagan se van documentando automáticamente.

### Paso 7 — Ejecutar `/speckit-plan`

```text
/speckit-plan
```

### Paso 8 — Ejecutar `/speckit-tasks`

```text
/speckit-tasks
```

### Paso 9 — Ejecutar `/speckit-analyze`	(opcional, pero recomendado)

```text
/speckit-analyze
```

### Paso 10 — Ejecutar `/speckit-implement`

```text
/speckit-implement
```

### Paso 11 — Ejecutar `/speckit-converge`	(opcional en general, pero **super recomendado en este proyecto** antes de mergear a `main`)  
Compara lo que piden `spec.md`, `plan.md` y `tasks.md` contra lo que el código realmente hace hoy.

```text
/speckit-converge
```


### Paso 12 — Proponer una nueva feature reiniciando el ciclo desde Specify hasta implement.
/speckit-specify Quiero que corra sobre una carpeta con la estructura de los logs de IIS, y no necesariamente con el IISLocal. Esto se podria hacer pasandole un paramtero especial. 

---

## Referencias

Ejemplo terminado y documentos de ejemplo (`AGENTS.md`, `CLAUDE.md`, `COPILOT.md`, `PRD`) en:
[github.com/tresvi/Ejemplo_Speckit](https://github.com/tresvi/Ejemplo_Speckit)
