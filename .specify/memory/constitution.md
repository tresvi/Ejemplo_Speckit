<!--
SYNC IMPACT REPORT
==================
Cambio de versión: plantilla sin ratificar → 1.0.0
Tipo de bump: MAJOR (primera ratificación con principios concretos)

Principios definidos (todos nuevos; reemplazan placeholders):
  - [PRINCIPLE_1_NAME] → I. Cero Alucinación (NO NEGOCIABLE)
  - [PRINCIPLE_2_NAME] → II. Aislamiento de la Capa de IA
  - [PRINCIPLE_3_NAME] → III. Test-First (NO NEGOCIABLE)
  - [PRINCIPLE_4_NAME] → IV. Trazabilidad
  - [PRINCIPLE_5_NAME] → V. Flujo de Spec Kit

Secciones agregadas:
  - [SECTION_2_NAME] → Restricciones Adicionales
  - [SECTION_3_NAME] → Flujo de Desarrollo y Puertas de Calidad
  - Governance → reglas de enmienda, versionado, ámbito de aplicación y cumplimiento

Secciones removidas: ninguna

Templates dependientes:
  ACTUALIZADO .specify/templates/plan-template.md      — "Constitution Check" poblado con las 5 puertas
  ACTUALIZADO .specify/templates/tasks-template.md     — tests dejan de ser opcionales (Principio III)
  ACTUALIZADO .specify/templates/spec-template.md      — fuente de verdad obligatoria en features con modelo
  SIN CAMBIOS .specify/templates/checklist-template.md — agnóstico de principios
  PENDIENTE   README.md — no referencia la constitución ni el flujo SDD

TODOs diferidos: ninguno
-->

# Constitución de Ejemplo_Speckit

## Core Principles

### I. Cero Alucinación (NO NEGOCIABLE)

El sistema NUNCA emite datos que no estén en su fuente de verdad.

- Toda salida derivada de un modelo DEBE citar el registro de origen que la fundamenta.
- Ninguna salida entregada al usuario puede contener un dato que no sea rastreable hasta un
  registro concreto de la fuente de verdad declarada para esa feature.
- Cuando el modelo no puede fundamentar una salida en un registro concreto, el sistema DEBE
  marcarla para revisión humana y NO DEBE entregarla como resultado.
- Cada feature que invoque un modelo DEBE declarar explícitamente su fuente de verdad y su
  camino de revisión humana antes de que exista código.

**Rationale**: El modo de fallo por defecto es la revisión, nunca la invención. Un resultado
ausente y señalado es recuperable; un resultado inventado y presentado como cierto contamina
toda decisión que se apoye en él, y no deja rastro de que había que dudar.

### II. Aislamiento de la Capa de IA

Toda interacción con modelos vive detrás de una frontera explícita.

- Toda invocación a un modelo DEBE ocurrir dentro de un módulo dedicado, expuesto al resto del
  sistema únicamente a través de una interfaz.
- La lógica de negocio NO DEBE importar el SDK del proveedor.
- La lógica de negocio NO DEBE construir prompts.
- Los prompts DEBEN existir como archivos versionados en el repositorio, con una versión
  identificable y estable a la que los logs puedan referirse.

**Rationale**: Un proveedor de modelos es un detalle de infraestructura con vida propia: cambia
de API, de precios y de comportamiento. Si el SDK y los prompts se filtran al dominio, cada uno
de esos cambios se vuelve una refactorización transversal, y ningún test puede sustituir el
modelo sin tocar la lógica.

### III. Test-First (NO NEGOCIABLE)

El test se escribe antes que la implementación.

- Ciclo rojo-verde-refactor obligatorio: se escribe el test, se verifica que falla, recién
  entonces se implementa.
- Los tests del ciclo normal NO DEBEN invocar un modelo real: usan fakes o respuestas grabadas.
- La suite por defecto DEBE ser determinista y ejecutable sin credenciales de proveedor y sin
  acceso a red.
- Toda tarea de implementación DEBE tener su test asociado escrito y fallando antes de empezar.

**Rationale**: Un sistema que consulta modelos es no determinista por naturaleza; si además su
suite lo es, no queda ninguna superficie estable contra la cual afirmar que algo funciona. El
determinismo se recupera en el borde, grabando lo que el modelo respondió.

### IV. Trazabilidad

Toda llamada a un modelo deja registro suficiente para reconstruirla.

- Toda llamada a un modelo DEBE registrar, como mínimo: identificador y versión del modelo,
  versión del prompt utilizado, y la fuente consultada.
- Los logs DEBEN ser estructurados y consultables, no prosa libre.
- Los logs NO DEBEN contener secretos ni PII, en ningún nivel de severidad.
- La instrumentación DEBE limitarse a límites del sistema, decisiones y fallos. El flujo interno
  paso a paso NO DEBE instrumentarse.

**Rationale**: Cuando una salida resulta ser incorrecta, la pregunta útil es qué modelo, con qué
prompt y sobre qué registro la produjo. Sin esos tres datos la corrección es adivinanza.
Instrumentar además el flujo interno solo entierra esa señal bajo ruido.

### V. Flujo de Spec Kit

El desarrollo avanza por fases, y cada fase deja huella.

- Cada fase de Spec Kit (`constitution`, `specify`, `clarify`, `plan`, `tasks`, `implement`)
  DEBE cerrar en su propio commit sobre la rama de la feature.
- El merge a `main` DEBE exigir dos condiciones simultáneas: suite de tests en verde, y
  `/speckit-converge` sin deriva reportada entre spec, plan, tasks y código.

**Rationale**: El valor del desarrollo dirigido por especificación está en poder ver, en el
historial, en qué momento se decidió cada cosa. Un commit único que mezcla spec, plan y código
borra exactamente esa información.

## Restricciones Adicionales

- **Fuente de verdad**: toda feature que invoque un modelo DEBE nombrar su fuente de verdad en
  el `spec.md`, antes de pasar a `plan.md`. Una feature sin fuente de verdad declarada no pasa
  a plan.
- **Versionado de prompts**: un cambio de prompt es un cambio versionado del sistema. Editar un
  prompt sin cambiar su versión identificable está prohibido.
- **Secretos**: las credenciales se proveen por configuración o entorno. NO DEBEN estar en el
  repositorio, en los prompts, ni en los logs.
- **PII**: los datos personales no se registran. Cuando un registro de origen contiene PII, el
  log cita el identificador del registro, no su contenido.
- **Revisión humana**: la marca para revisión humana DEBE ser una salida de primera clase del
  sistema, no una excepción ni una línea de log. El usuario tiene que poder verla.

## Flujo de Desarrollo y Puertas de Calidad

- **Ramas**: una rama por feature, con numeración secuencial (`###-nombre-feature`).
- **Puerta de plan**: el `Constitution Check` del `plan-template.md` DEBE pasar antes de la fase
  de investigación y volver a verificarse después del diseño. Toda violación se justifica en
  `Complexity Tracking` o se corrige.
- **Puerta de implementación**: no se escribe código de producción sin un test fallando.
- **Puerta de merge**: tests en verde y `/speckit-converge` sin deriva.
- **Revisión**: toda PR verifica explícitamente el cumplimiento de estos principios.

## Governance

Esta constitución supersede cualquier otra práctica del proyecto. Ante conflicto entre este
documento y una convención, un template o una preferencia de estilo, manda este documento.

**Ámbito de aplicación**: los Principios I y II, y la parte del Principio IV relativa a llamadas
a modelos, aplican a toda feature que invoque un modelo. Una feature que no invoca modelos los
cumple de forma trivial y no debe inventar estructura para satisfacerlos. Los Principios III y
V, y las reglas de logs del Principio IV, aplican siempre y sin excepción.

**Enmiendas**: toda enmienda requiere (a) propuesta documentada con su motivo, (b) incremento de
versión según la política de abajo, y (c) propagación a los templates dependientes en el mismo
commit.

**Versionado**: versionado semántico sobre la constitución misma.

- **MAJOR**: remoción o redefinición incompatible de un principio o de la gobernanza.
- **MINOR**: nuevo principio o sección, o expansión material de una guía existente.
- **PATCH**: aclaraciones, redacción, correcciones sin cambio semántico.

**Cumplimiento**: se revisa en cada PR. La complejidad que viole un principio DEBE justificarse
explícitamente en la tabla `Complexity Tracking` del plan, o rechazarse. Para guía de desarrollo
en tiempo de ejecución, ver los templates en `.specify/templates/`.

**Version**: 1.0.0 | **Ratified**: 2026-09-03 | **Last Amended**: 2026-09-03
