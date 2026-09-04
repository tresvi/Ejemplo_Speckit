# ECGViewer
Aplicación web para visualizar, filtrar y analizar señales de electrocardiograma (ECG) desde archivos CSV.
Orientada a entornos educativos y de investigación en ingeniería biomédica.

## Stack
- Front: React 19.2 + TypeScript 5.x. Ubicado en `src/front`.
- Back: .NET 10. Ubicado en `src/back`.
- Base de datos: SQLite (para usuarios/contraseñas y sesiones).

### Dependencias NuGet (back)
Versiones exactas viven en el `.csproj`; acá solo el "qué y por qué".
- `FftSharp` (Scott Harden): cálculos de filtros DSP (pasa bajo/alto/banda/notch) — RF-04.
- `ClosedXML` y `DocumentFormat.OpenXml`: manipulación y creación de archivos Excel `.xlsx` (import/export) — RF-05.

## Cómo correr
Estructura: en la raíz hay una carpeta `src` con `src/front` (frontend) y `src/back` (backend).

Backend (`src/back`):
- Instalar dependencias: `dotnet restore`
- Compilar: `dotnet build -c Debug`
- Ejecutar: `dotnet run`
- Tests (unitarios, xUnit): `dotnet test`

Frontend (`src/front`):
- Instalar dependencias: `npm install`
- Desarrollo: `npm run dev`
- Build de producción: `npm run build`
- Tests E2E (Playwright): `npx playwright test`

## Qué NO hacer
- NO persistir cambios automáticamente: marcadores, filtros y recortes solo se guardan cuando el usuario presiona explícitamente "Guardar". Si hay cambios pendientes al cerrar o recargar, alertar y pedir confirmación.
- NO modificar destructivamente la señal original: los filtros y recortes deben poder revertirse a la señal cargada.
- NO calcular las métricas (BPM, SDNN, RMSSD, pNN50) sobre todo el archivo: siempre sobre la ventana de tiempo visible.
- NO usar librerías gráficas que no cumplan el rendimiento exigido: render <0.1 s para 1 minuto de señal y sin parpadeos.
- NO asumir señales multicanal: la app soporta un solo canal; ante un CSV/XLSX multicanal, informar y no procesar.
- NO guardar contraseñas ni datos privados en texto plano: usar hash seguro (bcrypt/argon2).
- NO hardcodear la API key de Claude: va en `.env` como ANTHROPIC_API_KEY.
- NO llamar a la API de Claude desde los tests: usar mocks/fakes.
- NO agregar features fuera del alcance definido: captura en tiempo real por hardware, multi-usuario/roles/nube, HL7/DICOM, export a firmware, multi-tenant.
- NO presentar ECGViewer como herramienta de diagnóstico clínico certificado.
