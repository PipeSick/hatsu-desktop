# WinMac — Correcciones de interfaz de la iteración 6 (WM-043, WM-037, WM-035)

Rama `fix/winmac-iteration-6-ui` (WinMac) y `fix/winmac-iteration-6-ui` (NotchWin) · Base: `28f8aee` de `claude/relaxed-turing-cijzko`, que contiene el commit probado `6c6c252` · No contiene nada de `research/iphone-calls` · **Nada publicado, sin PR, sin merge.**

**CI verde = compila y pasan las pruebas de lógica.** No valida cómo se ve ni cómo se siente en tu PC, ni que el cambio de salida por aplicación se aplique de verdad. Estados: *Corregido en código · pendiente de prueba real*.

## WM-043 — El Dock lejos del borde físico

| | |
|---|---|
| **Causa** | El Dock se apoyaba en el borde inferior del **área de trabajo** (`rcWork`). Con la barra de Windows oculta por WinMac, Explorer puede seguir **reservando la franja** de la barra aunque no se vea (la captura mostraba ~64 px de hueco bajo el Dock y la ventana de Edge acababa ahí). Además, aun apoyado en el borde, la separación de la barra al borde era de 24 DIP. La reserva propia del Dock (`ABM_QUERYPOS`) también lo subía por encima de esa franja. |
| **Corrección** | `DockSettings.Baseline`: con una barra de Explorer **realmente visible** en ese borde (`VisibleTaskbarTop`: ventana visible y con más de 6 px dentro del monitor; una barra autoocultada solo asoma 1–2 px; las barras de otro monitor o vertical no cuentan) el Dock se apoya sobre ella, como siempre; **sin barra visible se apoya en el borde físico del monitor** y la barra del Dock queda a 10 DIP de él (`FreeEdgeGapDip`, escala con el DPI). La reserva propia del Dock va al ras (`flushWithEdge`) en ese caso. La comprobación de cada 2 s también recoloca el Dock cuando la barra aparece o desaparece. La zona de activación sigue siendo el último píxel de la pantalla (WM-043 previo). |
| **Sin tocar** | `TaskbarHider` y la reocultación inmediata (WM-044/WM-045) no cambian. Con la barra de Windows visible, la posición es exactamente la de antes. |
| **Límite conocido** | Sin barra visible, el Dock se superpone a cualquier otra AppBar inferior de terceros (poco común). La ventana del Dock sobresale unos DIP bajo el monitor (solo sombra transparente); con monitores apilados, eso cae sobre el borde superior del monitor de abajo. |
| **Pruebas** | `DockBaselineTests` (Dock.Core): franja fantasma de 0/48/64/96 px, DPI 100–250 %, barra visible, autoocultada, otro monitor, monitores a la izquierda y apilados, desplazamiento de altura, zona de activación. Dock.Core: 191 pruebas. |

## WM-037 — Panel «Personalizar» ilegible con cristal al mínimo

| | |
|---|---|
| **Causa** | No era el tema de Windows: era **lo que hay detrás**. Con «Cristal» al mínimo el panel es casi transparente y, con una página web blanca detrás (incluso con Windows en tema **oscuro**, como en tu captura), el fondo quedaba gris claro con texto blanco. WM-037 solo protegía el tema claro. Además los rótulos («CRISTAL», «SECCIONES») eran #636366 (2,8:1 sobre grafito) y las secciones desactivadas también. |
| **Corrección** | El panel lleva un fondo propio casi opaco (grafito al 92 %, `PanelLegibility.Scrim`) que se lee con cualquier nivel de cristal y cualquier cosa detrás, en ambos temas; el cristal sigue visible en el resto de la Isla y el control «Cristal» sigue funcionando. Rótulos #A1A1A6 y secciones desactivadas #8E8E93. La curva del cristal de la Isla (`GlassTint`) no cambia. |
| **Pruebas** | `PanelLegibilityTests` (NotchWin.Core): texto ≥ 7:1, rótulos ≥ 4,5:1, desactivadas ≥ 3:1 sobre cualquier fondo de 0 a 255; el color antiguo está por debajo del mínimo; la curva de la Isla no cambia. NotchWin.Core: 186 pruebas. |
| **Commit de NotchWin** | `f866eff` en `PipeSick/notch-win`, rama `fix/winmac-iteration-6-ui`; el CI de WinMac lo compila por `tools/notch-pin.txt`. |

## WM-035 — Mezclador espacioso

| | |
|---|---|
| **Causa** | Fila de una sola línea de 34 DIP con columnas fijas (nombre 112, % 40, silencio 26, salida 96) en un panel de 400: todo comprimido, nombres recortados. El selector de salida usaba el **menú nativo de Windows** (blanco, en la posición del puntero, nombres recortados al ancho del control). |
| **Corrección** | Panel de **520 DIP** (alto máximo 640). Cada sesión es una tarjeta de tres líneas: (1) icono · nombre (todo el espacio libre) · porcentaje · silencio; (2) deslizador con todo el ancho; (3) selector de salida con el nombre completo del dispositivo, **solo con la función experimental activada**. Alto: 78 DIP (114 con selector). Las sesiones de una misma app siguen agrupadas («Sesión 2», cada una con su volumen). Los selectores de **Salida** y **Entrada** usan una columna de rótulo fija y un botón ancho. El desplegable ahora es propio (`DarkChoiceMenu`): tema del panel (oscuro/claro), colocado **contra el control que lo abrió**, tan ancho como el nombre más largo (hasta el borde de la pantalla), dentro del área de trabajo de su monitor, hacia arriba si no cabe abajo y con desplazamiento si hay muchas opciones; teclado (↑ ↓ Inicio Fin Intro Esc) y rueda. En pantallas pequeñas el panel se ajusta (`FitWidth`). |
| **Conservado** | Agrupación de sesiones, volumen por sesión, selección general de dispositivos, «Volver a la salida anterior», aislamiento del proceso experimental (`--audio-route`, 5 s máximo, lectura de vuelta). Sin la función experimental no aparece ningún selector de salida por aplicación. |
| **No validado** | **Que el cambio de salida por aplicación se aplique de verdad sigue sin estar probado en hardware.** Esta corrección no toca esa lógica: solo la interfaz. Ver la lista de pruebas manuales. |
| **Pruebas** | `MixerLayoutTests` (Bar.Core) y `ChoiceMenuLayoutTests`/`ChoiceMenuPaletteTests` (Dock.Core): sin solapes a 520, 440, 360 y 300 DIP, columnas alineadas, clics por línea, ancho adaptable, desplazamiento con muchos apps, colocación del desplegable a 100–300 % de DPI, monitores con coordenadas negativas y apilados, contraste del menú oscuro y claro. Bar.Core: 451 pruebas. |
| **Capturas de CI** | El CI renderiza `mixer` y `mixer-routing` (tema oscuro y claro, 100 % y 150 %) con el pintor real y datos de muestra → artefacto `capturas-mezclador-<sha>`. Son muestras de CI, **no tu PC**. |

## Actualizar sin perder ajustes
Cierra WinMac (bandeja › Salir), descomprime el ZIP nuevo sobre la misma carpeta y abre `WinMac.exe`. Los ajustes (`%APPDATA%\WinMac`, `%LOCALAPPDATA%\WinMac`) se conservan; **no uses `-RemoveData`**. Si usas NotchWin por separado, esta versión trae la Isla integrada.

## Pruebas manuales (en tu PC)
1. **Dock, barra de Windows oculta por WinMac:** el Dock queda a ~10 DIP del borde físico, sin hueco de ~64 px. Maximiza una ventana: no debe quedar una franja de escritorio bajo ella (WM-044). Toca el borde: la barra de Windows no reaparece más de un instante (WM-045).
2. **Dock, barra de Windows visible, y con «Ocultar automáticamente» de Windows:** el Dock sigue sobre la barra como antes; con autoocultado la barra aparece al tocar el borde.
3. **Dock en 100/125/150 %, y en un segundo monitor:** misma separación relativa, cada Dock en su monitor.
4. **NotchWin → Personalizar:** con «Cristal» al mínimo y una página web blanca detrás, tanto con Windows en tema oscuro como claro, todo el texto del panel se lee. Mueve el control: el cristal de la Isla cambia, el panel sigue legible.
5. **Mezclador:** abre el menú Sonido; el panel es más ancho; cada app en tres líneas (dos sin la función experimental); nombres de apps y dispositivos legibles; «Sesión 2» bajo su app.
6. **Desplegables:** pulsa Salida, Entrada y (con la función experimental) la salida de una app: oscuros, junto a su botón, con el nombre completo, dentro de la pantalla (prueba también con el panel cerca del borde y con la pantalla a 150 %). Esc y clic fuera lo cierran.
7. **Salida por aplicación (experimental, aún sin validar):** elige otra salida para una app y comprueba **por el oído** que solo esa app cambia de dispositivo; vuelve a «Predeterminada». Anota si algo falla en `bar.log`.
8. **Ajustes y datos:** tras actualizar, tus ajustes de Dock, Barra e Isla siguen igual.
