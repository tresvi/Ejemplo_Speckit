---
name: conventional-commit
description: Genera mensajes de commit siguiendo el estándar Conventional Commits. Úsese al crear un commit o cuando el usuario pida un mensaje de commit.
---

# Conventional Commit

Genera mensajes de commit que siguen el estándar [Conventional Commits](https://www.conventionalcommits.org/).

## Formato

```
tipo(scope): descripción
```

- **tipo**: obligatorio. Uno de los tipos de abajo.
- **scope**: opcional. Área del código afectada, entre paréntesis (ej. `front`, `back`, `ecg`, `filtros`).
- **descripción**: obligatoria. En **imperativo**, en **minúscula**, **sin punto final**, y la línea completa (`tipo(scope): descripción`) de **máximo 72 caracteres**.

## Tipos permitidos

- `feat`: una nueva funcionalidad
- `fix`: corrección de un bug
- `docs`: cambios solo en documentación
- `style`: formato, espacios, punto y coma (sin cambios de lógica)
- `refactor`: cambio de código que no corrige un bug ni agrega una feature
- `perf`: mejora de rendimiento
- `test`: agregar o corregir tests
- `build`: cambios en el sistema de build o dependencias
- `ci`: cambios en configuración de CI
- `chore`: tareas de mantenimiento que no afectan el código de producción
- `revert`: revierte un commit anterior

## Reglas

1. La descripción va en **imperativo presente**: "agrega", no "agregado" ni "agregando".
2. Todo en **minúscula** (salvo nombres propios o siglas como ECG, CSV, API).
3. **Sin punto final** en la descripción.
4. La línea `tipo(scope): descripción` **no supera los 72 caracteres**.
5. Un commit = un cambio lógico. Si hay varios cambios sin relación, sugerí separarlos.
6. Para un breaking change, agregá `!` antes de los dos puntos (`feat(api)!: ...`) y/o un footer `BREAKING CHANGE: <explicación>`.

## Cuerpo y footer (opcionales)

Si el cambio necesita más contexto, dejá una línea en blanco después del asunto y agregá un cuerpo explicando el **qué** y el **por qué** (no el cómo). Los footers (`BREAKING CHANGE:`, `Refs: #123`) van al final, separados por otra línea en blanco.

```
feat(filtros): agrega filtro notch a 50 Hz

Permite eliminar la interferencia de línea sin salir de la pantalla
principal. La señal original se conserva para poder revertir.

Refs: #42
```

## Cómo generar el mensaje

1. Revisá los cambios reales (`git diff --staged`, o `git diff` si nada está en stage).
2. Determiná el **tipo** según la naturaleza del cambio.
3. Determiná el **scope** según el área afectada (dejalo vacío si el cambio es transversal).
4. Escribí la descripción en imperativo, minúscula, sin punto, respetando los 72 caracteres.
5. Si hace falta, sumá cuerpo y/o footer.

## Ejemplos

- `feat(ecg): agrega carga de señales desde CSV de un canal`
- `fix(filtros): corrige inversión de fase en el pasa banda`
- `docs: actualiza el PRD con la sección de riesgos`
- `refactor(back): extrae el cálculo de BPM a un servicio`
- `test(front): agrega E2E de marcado de eventos con playwright`
- `perf(ecg): reduce el tiempo de render de la ventana visible`
