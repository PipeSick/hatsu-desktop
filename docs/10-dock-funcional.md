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

## Iteración 3: apps temporales movibles y vista previa de ventanas
**Temporales movibles.** Las apps abiertas no ancladas se pueden arrastrar, pero solo entre ellas (nunca mezcladas con las ancladas, y viceversa). El orden vive en memoria (`DockModelBuilder.SetRecentOrder`): se olvida al cerrarse la app o el Dock; no se guarda en `dock.json`.

**Vista previa de ventanas** (estructura de la referencia aportada: panel de vidrio con pico sobre el icono, cabecera con icono y nombre, una tarjeta por ventana con su título en una píldora y su miniatura; las minimizadas llevan la píldora «Minimizada»).
- Aparece a los 0,35 s de pasar el ratón sobre una app con ventanas (`PreviewDelaySeconds`), cambia al instante entre apps, y se oculta 0,25 s después de salir del icono, del pasillo o del panel. Clic en una tarjeta = activar (y restaurar si está minimizada) esa ventana. No aparece al arrastrar, con el Dock oculto ni a pantalla completa. Se desactiva con `"PreviewsEnabled": false`.
- El panel se ancla a la posición **en reposo** del icono (con holgura para la magnificación máxima) para que no se mueva mientras el puntero sube hacia él; mientras está abierto reemplaza a la etiqueta de nombre.
- **Decisión de arquitectura:** se dibuja DENTRO de la ventana del Dock, que crece hacia arriba y a los lados (zona transparente, el ratón la atraviesa). Alternativa descartada: una segunda ventana (otro dispositivo, otro swap chain, enrutado de ratón entre ventanas). La barra no cambia de tamaño ni de posición; con `PreviewsEnabled: false` la ventana mide lo mismo que antes. Hasta 5 tarjetas (el resto se anuncia como «+N más»); en pantallas estrechas las miniaturas se reducen.
- **Miniaturas:** `PrintWindow(PW_RENDERFULLCONTENT)` en un hilo aparte (el Dock nunca espera), sin los bordes invisibles de redimensión, recortadas a 480×300, refrescadas cada ~1,5 s mientras se pasa el ratón. Se omiten ventanas que no responden y capturas totalmente negras; las minimizadas conservan su última captura (o muestran el icono de la app). Alternativas: miniaturas en vivo de DWM (`DwmRegisterThumbnail`, necesita una ventana destino propia) o `Windows.Graphics.Capture` (más coste); se pueden revisar si esta resulta insuficiente.
- **Limitación conocida:** es una instantánea, no vídeo; apps con contenido protegido o aceleración especial pueden salir negras (se muestra el icono). Medidas del panel sin calibrar.
- Pruebas: geometría (centrado, bordes, reducción, proporciones raras, holgura en la ventana), comportamiento (retardo, cambio, salida, clic, bloqueo tras clic, config, arrastre), arrastre de temporales y orden en memoria. CI: escena `--pose preview` con ventanas sintéticas (claro/oscuro), `--thumb-test` y una sección del selftest.
- CLI nueva: `--thumb-test [--out carpeta]` captura las ventanas abiertas con el mismo servicio y las guarda como PNG.
