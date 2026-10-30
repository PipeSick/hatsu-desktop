# Cómo ejecutar las pruebas de la Fase 1 (Windows 11)

Tiempo: unos 15 minutos la primera vez (descarga y compilación) + ~4 min por repetición.
**No se instala nada en tu sistema y no se cambia ninguna configuración.** No se ejecuta S5 (barra de tareas).

## Antes de empezar
1. Equipo **físico** con Windows 11 (ideal 24H2), conectado a corriente. Cierra apps pesadas.
2. SDK de **.NET 10** instalado (https://dotnet.microsoft.com/download/dotnet/10.0). El script **no lo instala**: si falta, te lo dice.
3. Puesto un fondo de pantalla **neutro** si no quieres que las capturas muestren tu escritorio (las capturas recortan la franja inferior de la pantalla principal; se guardan solo en tu equipo; se pueden desactivar con `-NoScreenshots`).

## Pasos
1. Descarga el repositorio (rama `claude/vibrant-keller-7tyi6u`): `git clone` o ZIP desde GitHub, y abre PowerShell en la carpeta.
2. Primero solo comprueba requisitos (no descarga ni compila nada):
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Run-Phase1Tests.ps1 -CheckOnly
   ```
3. Prueba de humo rápida (fases acortadas, solo verifica que todo arranca; **no** evalúa umbrales):
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Run-Phase1Tests.ps1 -Quick
   ```
   Te pedirá escribir `SI` para permitir la descarga de paquetes NuGet (lista exacta en pantalla).
4. Ejecución completa, 3 repeticiones (la mediana va al resumen):
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Run-Phase1Tests.ps1 -Runs 3
   ```
5. Durante la ejecución aparecerá un dock de prueba en la parte inferior, ~35 s por prototipo. **No muevas el ratón ni uses el equipo** mientras corre (mover el ratón no altera la prueba, pero otras cargas sí alteran los números).

`-ExecutionPolicy Bypass` vale solo para esa ejecución; no cambia la política del sistema.

## Qué se ejecuta
| Qué | Cuándo |
|---|---|
| Sonda de plataforma S7 (solo lectura) | siempre |
| Docks S2 (WPF ×2 variantes), S3 (WinUI ×2), S4 (DirectComposition) | siempre |
| Capturas de reposo / hover / oculto | siempre, salvo `-NoScreenshots` |
| S1 (seguimiento pasivo de ventanas, 30 s) | solo con `-IncludeWindowTracker` |
| S6 (lectura de menús por UI Automation; interactúa con la app en primer plano) | solo con `-IncludeUia` |
| **S5 (modifica la barra de tareas)** | **nunca desde este script**; se probará en una VM con snapshot |

## Qué obtienes
Carpeta `bench-results\<fecha>\` con: `summary.md` (resumen), `summary.json`, un JSON por ejecución, `machine.json`,
`S7-platform.txt`, `screenshots\*.png` y los logs de compilación. Envíame esa carpeta (o al menos `summary.md`, `machine.json`, los JSON y `screenshots`).

Para regenerar solo el resumen a partir de una carpeta existente:
```powershell
powershell -NoProfile -File tools\Run-Phase1Tests.ps1 -SummaryFrom bench-results\<fecha>
```

## Si algo falla
- **Falta el SDK de .NET 10**: instálalo tú y reintenta. El script nunca instala nada.
- **Un prototipo no compila**: el log está en `bench-results\<fecha>\build-<nombre>.log`; el resto sigue. Envíame el log.
- **Un prototipo se queda colgado**: el script lo mata a los 120 s y lo marca como fallido.
- **Algo quedó abierto** (por Ctrl+C): cierra `S2.DockWpf`, `S3.DockWinUI`, `S4.DockDComp` desde el Administrador de tareas. No hay ninguna otra reversión pendiente, porque estas pruebas no modifican el sistema.
- Los binarios quedan en `artifacts\` (ignorada por git). Puedes borrarla cuando quieras.

## Probar el Dock (iteración 1) y ver el CI sin Windows
- Instrucciones del Dock, lista de lo pendiente de validación y cómo descargar los PNG/artefactos del CI desde macOS: [`06-dock-iteracion-1.md`](06-dock-iteracion-1.md) §4.
- Este script (`Run-Phase1Tests.ps1`) cubre los **prototipos** S2/S3/S4; el Dock tiene su propio `--selftest` y `--render-png` (no invasivos).
