# Dock funcional (iteración 2)

Objetivo: convertir el Dock aprobado visualmente en un componente funcional de Windows 11 **sin tocar su aspecto** (geometría, sombras, bordes, material, magnificación, etiquetas).

> Estado de validación: **compilado** en Linux (compilación cruzada) y **pruebas unitarias ejecutadas** (Apps.Core 25, Dock.Core 91). El código de Windows (`Apps.Win`, `Dock.Win`, `Dock.App`) **no se ha ejecutado todavía**: queda pendiente el CI de Windows y la validación manual (ver `PENDIENTE-WINDOWS-FISICO.md`, V1–V12).

## Capas
| Proyecto | Contenido | Plataforma |
|---|---|---|
| `WinMac.Apps.Core` | Identidad de app, iconos (modelo, validación, ajuste de aspecto, fallback), caché y `IconService` no bloqueante | puro, reutilizable (Spotlight/Launchpad futuros) |
| `WinMac.Apps.Win` | Extracción con `IShellItemImageFactory`, resolución ventana→app, nombres | Windows, sin NuGet |
| `WinMac.Dock.Core` | Modelo, configuración v2, `DockPinning`, `DockMenu`, `LaunchGuard`, arrastre en el controlador | puro, probado en Linux |
| `WinMac.Dock.Win` | Pintor, host, seguimiento de ventanas, acciones | Windows |
| `WinMac.Dock.App` | CLI y selftest | Windows |

## Decisiones y alternativas
1. **Identidad estable** (`AppIdentity`): Win32 → `exe:<ruta normalizada>`; empaquetadas (MSIX/UWP) → `aumid:<aumid>`. Alternativa descartada: solo ruta del exe (falla con UWP, que corre bajo `ApplicationFrameHost`). Las ventanas de `ApplicationFrameHost` se resuelven con `System.AppUserModel.ID` vía `SHGetPropertyStoreForWindow`.
2. **Activar apps empaquetadas**: `explorer.exe shell:AppsFolder\<AUMID>`. Alternativa: `IApplicationActivationManager` (más control, más COM); se deja como mejora si falla en pruebas manuales.
3. **Iconos**: API documentada `IShellItemImageFactory` (256→32 px, conserva transparencia y proporciones), un hilo STA por llamada, tiempo máximo 4 s, caché negativa (reintento a los 5 min), caché en memoria (LRU) y disco (`.wmicon`, tolerante a corrupción). Sin placa ni contorno alrededor de iconos reales; los azulejos de letra de la escena demo conservan el aspecto aprobado. Iconos alternativos: `IconOverride` por app anclada.
4. **Anclado**: `dock.json` v2 (`PinnedApp {Id, Path, Aumid, Title, IconOverride}`), compatible con v1 (Path/Title). Operaciones puras (`DockPinning`), guardado atómico con debounce, recarga si el archivo cambia.
5. **Menú contextual**: menú nativo Win32 (`TrackPopupMenuEx`) en lugar de uno dibujado: accesible, tema del sistema, sin tocar el aspecto del Dock. Entradas: Abrir, ventanas disponibles, Anclar/Quitar del Dock, Mover a izquierda/derecha, Cerrar ventana, Salir de la aplicación. Cerrar = `WM_CLOSE` (cooperativo); **nunca** `TerminateProcess`.
6. **Reordenar**: arrastre (el controlador lo gestiona sin magnificar; el elemento arrastrado se dibuja encima) y, como alternativa sin ratón/arrastre, "Mover a la izquierda/derecha".
7. **Lanzamiento**: `LaunchGuard` (4 s) evita duplicados por doble clic; si la app ya corre se activa/restaura.
8. **Pantalla completa**: se mantiene `SHQueryUserNotificationState`.

## Estabilidad
No modifica archivos del sistema, no reemplaza el Explorador, no oculta la barra de tareas, no requiere administrador, se cierra con `--quit` (evento `Local\WinMac.Dock.Quit`) o Alt+F4/Esc según el host, cada acción tiene su `try/catch`, y un icono corrupto o lento solo produce el fallback.

## CLI (`WinMac.Dock.App.exe`)
- `--list-pinned` · `--pin <ruta|AUMID> [--title T]` · `--unpin <ruta|AUMID>` (editan `dock.json`; un Dock en marcha lo recarga en ≤2 s)
- `--icon-test <ruta|AUMID> [--out icono.png]` extrae el icono con el mismo servicio que el Dock
- `--quit` cierra el Dock en marcha · `--demo` solo para pruebas · `--selftest` (secciones nuevas: identidades, iconos + caché en disco, anclar/reordenar/desanclar, modelo real)
- `--no-config` no lee ni escribe configuración

## Limitaciones conocidas
- Nada del lado Windows ejecutado aún; el comportamiento real (SetForegroundWindow desde ventana NOACTIVATE, activación de UWP, arrastre) es hipótesis hasta validar.
- Sin accesibilidad (UIA) ni teclado en el Dock (W11).
- "Salir de la aplicación" solo envía `WM_CLOSE` a las ventanas de la app; si ésta lo ignora, no se fuerza.
- Iconos de apps empaquetadas dependen del Shell; si falla, se usa el fallback determinista.
- Los iconos alternativos se configuran editando `dock.json` (sin interfaz).
- Varios monitores y DPI mixtos sin validar.
