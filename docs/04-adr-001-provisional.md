# ADR-001 (PROVISIONAL) — Tecnología del host del Dock

Estado: **PROVISIONAL y REVERSIBLE. No es una decisión definitiva.** Se confirmará o revertirá con las mediciones reales (docs/03-criterios-aceptacion.md).
Fecha: 2026-10-30.

## Decisión provisional
Host del Dock = **Win32 + DirectComposition + Direct3D11/Direct2D (Vortice 3.8.3)**, con el comportamiento en una librería sin dependencias de render (`WinMac.Dock.Core`) y el dibujo aislado en `DockPainter`.
WPF y WinUI 3 se **mantienen** como alternativas (los prototipos S2/S3 siguen en el repositorio y en el CI).

## Evidencia disponible (y su calidad)
| Evidencia | Clase | Qué dice |
|---|---|---|
| Los 3 prototipos (WPF, WinUI 3, DirectComposition) **compilan en un runner Windows** (run `41111226149`, Windows Server 2025 build 26100, SDK 10.0.403): 0 errores, 0 advertencias; incluye PRI/manifiesto reales de WinUI 3 | **Medición (compilación)** | Los tres son *construibles*. No dice nada del comportamiento. |
| Acrílico/Mica del sistema se aplican a **toda la ventana**; Acrílico se recomienda solo para superficies transitorias | **Documentada** | Limita la forma del material en WPF-`dwm` y WinUI-`acrylic`. |
| WinUI 3 no ofrece ventana transparente nativa | Conocimiento previo [C]; **no verificado hoy** | Por eso existe la variante `plain` (para comprobarlo en Windows). |
| `DWMWA_USE_HOSTBACKDROPBRUSH` permite a apps Win32 usar el *host backdrop brush* (Win11+) con limitaciones (airspace, DPI, Win2D no soportado) y reportes de fallos con grafos de efectos | Documentación + reportes de la comunidad | El blur «detrás de la ventana» con efectos propios **no está garantizado** por vía documentada. |
| `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`: Win10 2004+, requiere DWM, solo ventanas del propio proceso, no es DRM | **Documentada** | Habilita (en el futuro) captura del fondo excluyendo nuestra ventana. |
| **Mediciones de rendimiento, consumo y latencia: ninguna** | — | No se puede afirmar que ninguna tecnología cumpla los umbrales. |

## Razones de la elección (basadas en capacidades, no en rendimiento medido)
1. **Techo de fidelidad.** Es la única vía con forma de barra arbitraria, alfa por píxel, bordes iluminados y sombras propias **con APIs documentadas**; es también la que permite, más adelante, vidrio con refracción (docs/05-liquid-glass.md).
2. **Coste en reposo.** Sin framework de UI: en reposo no se renderiza y el bucle espera mensajes (sondeo a ~20 Hz). *Hipótesis* a medir (umbral: ≤ 0,5 % CPU).
3. **Control del ritmo de frames** (Present con vsync) y de la entrada (`WM_NCHITTEST` → el ratón atraviesa lo transparente).
4. **Aislamiento.** Toda la lógica (43 pruebas hoy) vive en `Dock.Core` sin dependencias de Windows; el painter solo recibe un `DockView`.

## Riesgos de esta elección
| Riesgo | Gravedad | Mitigación |
|---|---|---|
| Código de GPU **escrito sin poder ejecutarlo** (sesión en Linux); pueden aparecer fallos en tiempo de ejecución que la compilación no detecta | **Alta** | `--selftest` y renders fuera de pantalla en CI (humo informativo); pruebas reales en Windows pendientes |
| **Accesibilidad (UIA) hay que implementarla a mano** (no existe aún) | **Alta** (criterio del proyecto) | Pendiente; si su coste es excesivo, es motivo de reversión a WPF/WinUI |
| Más código propio y conocimiento DirectX → barrera para contribuidores | Media | Painter pequeño y comentado; lógica separada |
| Dependencia de **Vortice** (bindings DirectX; licencia MIT [C], sin verificar hoy) | Media | Verificar licencia/mantenimiento antes de publicar; interfaz `IIconSource`/`DockPainter` aíslan el uso |
| Variabilidad de GPU/controladores | Media | Fallback automático a WARP; nivel de vidrio `Tint` sin efectos |
| Multi-monitor, DPI mixto, 125–200 %: **solo monitor principal** hoy | Media | Pendiente; el diseño ya separa DIP/píxel |
| Overlay no reserva área de trabajo (no es AppBar): las ventanas maximizadas pueden quedar bajo el dock | Media | Decisión consciente (no tocar componentes críticos); evaluar AppBar después |

## Reversibilidad (por qué no es irreversible)
- `Dock.Core` no referencia Windows ni DirectX. Un host WPF o WinUI solo debe: obtener un `DockView` por frame, dibujarlo, y reenviar puntero/clics.
- Coste estimado de revertir a WPF: reescribir `DockPainter` (272 líneas) y `DockHost` (248 líneas); S2 ya contiene un dibujo equivalente más simple.
- Disparadores de reversión: (a) no cumplir G1 en mediciones reales; (b) UIA inviable en plazo razonable; (c) problemas de licencia/mantenimiento de Vortice; (d) inestabilidad en GPUs reales que WARP/`Tint` no resuelvan.

## Qué NO se ha decidido
Tecnología para Finder, Settings, Launchpad ni Spotlight (hipótesis: WinUI 3). Tecnología definitiva del Dock.
