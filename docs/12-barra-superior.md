# Barra superior de WinMac (Fase 4 · «Menu Bar») — diseño y decisiones

Estado: **diseño + primera iteración de código**, sin ejecutar en Windows. Las decisiones marcadas «provisional» se revisan con las pruebas manuales y con capturas de referencia del Mac (todavía no hay: el aspecto actual es una propuesta, no una calibración).

## Alcance de la primera iteración (de docs/00 §7 y §9)
| Incluido | Fuente (API documentada) |
|---|---|
| Menú «WinMac» (botón a la izquierda) | menú nativo Win32 (Acerca de, Salir de la barra) |
| Nombre de la app activa | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + identidad/nombre ya existentes (`WindowIdentityResolver`, `DisplayNames`) |
| Reloj y fecha | temporizador alineado al minuto (segundos opcionales) |
| Batería (nivel, cargando, sin batería) | `GetSystemPowerStatus` |
| Volumen (nivel, silencio) | Core Audio (`IMMDeviceEnumerator`/`IAudioEndpointVolume`) |
| Red (Wi-Fi, Ethernet, sin conexión) | `NetworkChange` + `NetworkInterface` |
| Clic en un indicador | abre su página de Configuración (`ms-settings:`) |
| Reserva de espacio arriba | AppBar real (`SHAppBarMessage`) |

**Fuera** (con motivo): menús de la app activa (UIA parcial, spike S6; se evalúa después), iconos de bandeja de terceros y notificaciones (sin API pública, docs/00 §7), Bluetooth (requiere WinRT `Windows.Devices.Radios` → otro TFM; pendiente), intensidad de señal Wi-Fi (API WLAN; pendiente), varios monitores (solo el principal), Centro de control.

## Decisiones y alternativas
**D1 · Reserva de espacio.**
A) **AppBar real** (`ABM_NEW/ABM_SETPOS`, borde superior): las ventanas maximizadas quedan debajo de la barra; Windows restaura el área de trabajo si el proceso muere. **Elegida** (con opción `ReserveSpace=false`).
B) Ventana superpuesta sin reservar: nada se mueve, pero las ventanas maximizadas quedan tapadas por la barra. Disponible como modo `ReserveSpace=false`.
C) Cambiar el área de trabajo con `SystemParametersInfo(SPI_SETWORKAREA)`: persistente e invasivo si el proceso cae. **Descartada** (viola «no cambios destructivos / cerrar con normalidad»).
Seguridad: `ABM_REMOVE` al salir, evento `Local\WinMac.Bar.Quit` para `--quit`, la barra de tareas de Windows no se toca.

**D2 · Tecnología de render.** Win32 + DirectComposition + Direct2D, la misma que el Dock (ADR-001 provisional): una sola base, sin dependencias nuevas. Alternativas WPF/WinUI 3: sin ventaja medida para una barra de una línea; WinUI 3 sigue con la limitación L1 del CI.

**D3 · Reutilización.** `Bar.Win` referencia `Dock.Win` para `D2DDevice`, `GlassBackdrop`, `WallpaperSource`, `FullscreenWatcher` y `SystemTheme`. Alternativa limpia: extraer un `WinMac.Gfx.Win` común; implica mover archivos del Dock que el usuario está validando, así que se pospone (**deuda técnica anotada**).

**D4 · Capas** (igual que el Dock): `Bar.Core` (puro y probado en Linux: estado, formato del reloj, distribución, controlador, configuración, acciones), `Bar.Win` (proveedores del sistema, AppBar, ventana y pintor), `Bar.App` (ejecutable y CLI).

**D5 · Aspecto — ELEGIDO por el usuario: variante B (vidrio a todo el ancho).** Las variantes A (transparente) y C (dos islas) quedan descartadas por ahora; A sigue disponible con `Background=Transparent`. Detalles provisionales: Barra transparente sobre el fondo (como en Tahoe) o con vidrio desenfocado (`Background=Glass`); texto blanco/negro según el tema de Windows (la adaptación al brillo del fondo queda pendiente); iconos de estado **vectoriales dibujados con Direct2D** (sin fuentes ni recursos de terceros). Altura 28 DIP. Todo está en `BarMetrics`/`BarStyle` para calibrar con capturas del Mac.

**D6 · Convivencia con la otra app del usuario (tipo «notch»).** La barra deja libre el centro: sin elementos en el tercio central por defecto (`CenterGap`), de modo que una superposición en el centro superior no tape información. Si esa app va a reservar espacio arriba también, hay que coordinar quién es dueño del AppBar (pendiente de definir con el usuario).

## Seguridad y estabilidad
Sin administrador, sin tocar Explorer ni la barra de tareas, cierre normal (`--quit`, menú «Salir de la barra superior»), proveedores aislados (un fallo de volumen/red/batería muestra el indicador como no disponible, nunca tumba la barra), se oculta con apps a pantalla completa.

## CLI (`WinMac.Bar.App.exe`)
`(sin args)` arranca la barra · `--quit` · `--no-reserve` (sin AppBar) · `--render-png f.png [--theme light|dark] [--scale 2]` (offscreen, para CI) · `--selftest` (no registra AppBar) · `--selftest-appbar` (solo CI: registra y quita un AppBar) · `--status` (imprime los valores leídos del sistema).

## Pruebas
Unitarias (Linux): formato del reloj (es-CL, 12/24 h, segundos, fecha), distribución y hit-test, hover/clic/acciones, configuración tolerante, mapeo de estado → icono. CI Windows: PNG de la barra (claro/oscuro), `--status`, selftest. Manual (Windows físico): ver `PENDIENTE-WINDOWS-FISICO.md` §Barra superior.

## Integración futura con la «isla» central (app propia del usuario)
Decisión: **sí se contempla**, pero no se implementa hasta que esa app exista y se acuerde el contrato. La barra ya deja libre el tercio central (`CenterGap`, 30 %) y no coloca nada ahí.
Opciones (a decidir con el usuario cuando su app esté lista):
1. **Procesos separados que conviven** (la más simple y robusta): la isla es una ventana superpuesta propia, centrada sobre la barra; la barra no sabe nada de ella. Hay que acordar quién reserva el espacio superior (AppBar): lo natural es que lo haga solo la barra y la isla se dibuje encima sin reservar.
2. **Ranura central en la barra**: la barra expone un punto de extensión (p. ej. tubería con nombre) donde la isla le pide ancho/alto y la barra le cede el centro; sirve si la isla debe crecer y empujar los indicadores.
3. **Un solo proceso/librería**: la isla como módulo dentro de WinMac; mayor integración, pero acopla ambos proyectos.
Datos que faltan: tecnología de esa app (lenguaje, UI), si reserva espacio de pantalla, si debe recibir eventos de la barra (reloj, app activa, volumen) o solo dibujarse encima.

### Hallazgos tras revisar `PipeSick/notch-win` (MIT, WinUI 3; versión temprana, sin probar en pantalla real)
- Su ventana es **siempre visible (`IsAlwaysOnTop`)**, `WS_EX_NOACTIVATE`/`TOOLWINDOW`, del tamaño exacto de la píldora, pegada al borde físico superior (`OuterBounds`, **no** `WorkArea`) y **sin AppBar**: su propio código comenta que debe abrazar el borde «aunque otra app-bar reserve espacio allí».
- Eso encaja con la **opción 1 (procesos separados)**: la barra reserva el espacio (AppBar) y la isla se dibuja encima del centro, sin pelear por el área de trabajo. **Recomendada.**
- Puntos a vigilar cuando se prueben juntas en Windows: (a) **orden Z**: ambas son «topmost»; la isla debe quedar por encima de la barra (la barra solo se reordena con `ABN_FULLSCREENAPP`; si hace falta, la isla reafirma su posición al expandirse); (b) la isla **colapsada** debe caber en el hueco central (30 % ≈ 576 px en 1920); al expandirse cubre la barra en el centro, como la isla de macOS; (c) la posición `TopLeft/TopRight` de la isla taparía elementos de la barra: se recomienda dejarla en el centro; (d) un notch por monitor frente a la barra solo en el principal (limitación conocida de la barra).
- Licencias compatibles (MIT/Apache-2.0). Distinta toolchain (la isla exige MSBuild/Visual Studio; WinMac usa `dotnet`): no se mezclan en una misma solución por ahora.
- Pendiente si se quiere más integración (opción 2): un contrato mínimo (p. ej. tubería con nombre) para que la barra le diga a la isla su ancho útil central y la app activa; no hace falta para la v1.
