# WinMac 2.0 — Iteración 6: escritorio, sonido avanzado y continuidad con iPhone

Fecha: 2026-11-03 · Rama `claude/relaxed-turing-cijzko` (WinMac y NotchWin) · Base: `698cc58` (WinMac Connect fases 1 y 2) · **Nada publicado.**

**CI verde = compilación y pruebas técnicas.** Nada de lo que sigue está validado en tu Windows hasta que lo pruebes. Estados:
- **Corregido en código:** causa identificada y corregida, con pruebas de la lógica.
- **Pendiente de prueba real:** solo tu PC lo confirma.
- **Bloqueado:** Windows o iOS no lo permiten por vías autorizadas.

## Etapa A — Correcciones del escritorio

| ID | Causa identificada | Cambios | Pruebas | Estado |
|---|---|---|---|---|
| **WM-043** Dock oculto que no reaparece con la barra de tareas oculta | El Dock se coloca contra el borde inferior del **área de trabajo**, pero no escuchaba cuando esa área cambiaba (`SPI_SETWORKAREA`). Al ocultar la barra de tareas, el área crece unos 48 px y el Dock se quedaba donde estaba. Su franja de activación (WM-033: el borde de su propia ventana) quedaba unos 48 px por encima del borde real, y el cursor en el borde de la pantalla no la tocaba nunca. | `DockHost`: se recoloca cuando cambia el área de trabajo, tanto por el aviso de Windows como por una comprobación ligera cada 2 s (una consulta). `DockSettings.EdgeHot`: si **no** hay una barra de tareas de Explorer visible en ese monitor, el último píxel de la pantalla también despierta al Dock, dentro de su ancho. Con la barra visible se mantiene WM-033: su borde no despierta al Dock. | Las 4 combinaciones de barra oculta × Dock autoocultable, 150 %, fuera del ancho; recolocación al ocultar la barra (+48 px). Dock.Core: 150 pruebas. | Corregido en código · pendiente de prueba real |
| **WM-044** El Dock oculto parece seguir reservando espacio | Dos orígenes posibles: 1) el propio Dock, que **no** reserva con «Ocultar automáticamente» (comprobado: solo reserva con «Reservar espacio» y sin autoocultar); 2) **Explorer**: WinMac activaba su «Ocultar automáticamente» y escondía su ventana en el mismo instante. Explorer aplica ese cambio con una animación y podía quedarse con la franja reservada, dejando el hueco abajo. | `TaskbarHider`: activa primero «Ocultar automáticamente» y esconde la ventana 1,2 s después, cuando Explorer ya liberó el espacio. Tras ocultar, comprueba el área de trabajo y anota en `host.log` si sigue habiendo una franja reservada y de cuántos píxeles. El Dock no cambia de reserva al aparecer y desaparecer: depende del ajuste, no de la animación. | Política y registro de recuperación (pruebas existentes) y recolocación del Dock. | Corregido en código · pendiente de prueba real (el registro dirá si Explorer aún reserva) |
| **WM-045** La barra de Windows reaparece al llevar el cursor abajo | Con «Ocultar automáticamente» de Windows activo (el tuyo o el que pone WinMac), Explorer **vuelve a mostrar su barra cada vez que el cursor toca el borde inferior**, que es justo donde está el Dock. WinMac solo la volvía a ocultar en su comprobación cada 2 s: la barra quedaba visible hasta 2 s. Además, el freno antiparpadeo contaba esas reapariciones normales (6 en 60 s), se **rendía** y dejaba la barra visible toda la sesión. Desactivar y reactivar el ajuste borraba la rendición: por eso «se arreglaba». | **Reocultación inmediata:** aviso de accesibilidad pública fuera de proceso (`SetWinEventHook` / `EVENT_OBJECT_SHOW`, solo para las ventanas de la barra de Explorer). No se inyecta nada en Explorer ni se cambia nada permanente. **Freno nuevo:** solo una pelea real cuenta (más de 20 reapariciones en 10 s, más rápido que una persona). **Rendición con caducidad:** si se rinde, vuelve a intentarlo sola a los 2 min. Nunca hace falta desactivar y reactivar el ajuste. **Reconciliación** con el estado real: al iniciar, tras reiniciar Explorer (`TaskbarCreated`, con el aviso reinstalado para el nuevo Explorer) y en caliente. La restauración al desactivar, al salir, por el guardián y con `--restore-taskbar` no cambia. | El cursor tocando el borde cada 1,5 s durante 5 min nunca dispara el freno; 20 por segundo sí; la rendición caduca; reconciliación idempotente (inicio, ya oculta, Explorer la recreó, desactivar en caliente). Host.Core: 131 pruebas. | Corregido en código · pendiente de prueba real |
| **WM-037** La Isla transparente con el tema claro | El cristal es acrílico oscuro **poco opaco** (≈38 % con el nivel por defecto) y el texto, blanco. Con el tema oscuro lo que hay detrás suele ser oscuro y se lee. Con el tema claro lo que hay detrás es blanco y el cristal quedaba casi transparente. No es una transición: es permanente. Además, los controles sin color fijo (iconos y botones) tomaban el tema **claro** del sistema y salían oscuros sobre el cristal oscuro. | NotchWin: todo el contenido usa siempre el **tema oscuro** (`RequestedTheme="Dark"`). Con Windows en tema claro, el tinte del mismo cristal tiene un **mínimo de opacidad** (`GlassTint`, puro y con pruebas). Con el tema oscuro la curva es exactamente la de antes. Sigue los cambios de tema en vivo (`UISettings.ColorValuesChanged`). Ni diseño, ni controles, ni letras cambian. | El mínimo con el tema claro en todos los niveles; aspecto oscuro idéntico; el máximo del usuario se respeta. NotchWin.Core: 170 pruebas. | Corregido en código · pendiente de prueba real (pasos 1–8 del encargo) |
| **WM-036** El Dock no abrió y apareció información de Smart App Control | Sin acceso a tu PC no se puede confirmar qué archivo se bloqueó. Lo que sí se encontró en el código: si Windows impedía lanzar un módulo, por cualquier motivo, el host lo marcaba **«Fallado» sin reintentar** hasta reiniciar WinMac, y el registro no decía el código real. Eso encaja con «no abrió y luego volvió a funcionar». | `LaunchDiagnostics`: el motivo con su código de Windows (Control de aplicaciones o Smart App Control 4551, Integridad de código 577, directiva 1260, antivirus 225/226, acceso denegado 5) y los códigos de salida de una biblioteca bloqueada (`0xC0000428`) o ausente. Un bloqueo de seguridad o un fallo pasajero **se reintenta** con espera creciente; un ejecutable inexistente no. Nuevo `WinMac.exe --diagnose-security` (**solo lectura**): estado de Smart App Control, eventos de Integridad de código y del antivirus de los últimos 14 días que nombran archivos de WinMac, y firma de cada ejecutable. Informe en `%LOCALAPPDATA%\WinMac\logs\security-report.txt`. No se desactiva nada. | Explicación y reintento por código; códigos de salida. | Diagnóstico mejorado · causa por confirmar con tu informe |

### WM-036 — distribuir ejecutables sin firma

- La beta **no está firmada**. Smart App Control bloquea los ejecutables sin firma y sin reputación en la nube, y cada beta tiene archivos nuevos que todavía no la tienen. Por eso el bloqueo puede ir y venir.
- Alternativas confiables y gratuitas o baratas para un proyecto open source:
  - **SignPath Foundation**: firma gratuita para proyectos OSS. NotchWin ya tiene una carpeta `signpath/`.
  - **Azure Trusted Signing**: de pago, pocos dólares al mes.
  - **Microsoft Store / MSIX**: lo firma Microsoft.
  - **winget**: no firma por sí solo.
- Ninguna exige que el usuario desactive su protección. Hasta entonces: no desactives Smart App Control; si algo no abre, ejecuta `WinMac.exe --diagnose-security` y comparte el informe.

### Matriz del Dock (para tu validación)

| Barra de Windows oculta | Dock autoocultable | Esperado |
|---|---|---|
| No | No | Dock visible; las maximizadas terminan sobre él si «Reservar espacio» está activo |
| No | Sí | El Dock se oculta y reaparece en su franja, justo sobre la barra de Windows |
| Sí | No | Dock visible pegado al borde; barra de Windows oculta, también al tocar el borde |
| Sí | Sí | El Dock se oculta y reaparece al tocar el borde de la pantalla; la barra de Windows sigue oculta; las maximizadas llegan al borde |

Para cada combinación: cursor al borde, maximizar y restaurar, 150 %, cambiar el ajuste en caliente, reiniciar y cerrar WinMac, reiniciar el Explorador, y desactivar la ocultación (la barra vuelve con tu ajuste original).

## Etapa B — Mezclador de sonido (WM-035)

| Problema | Cambio |
|---|---|
| Panel alto y estrecho | 400 DIP de ancho, alto máximo de 560 DIP con desplazamiento interno. Título y volumen general quedan fijos arriba; los accesos a Windows, fijos abajo. Se adapta a pantallas pequeñas: el centro se desplaza. |
| Controles separados y desalineados | Fila de aplicación compacta de 34 DIP: icono · nombre (columna fija) · deslizador (misma columna en todas las filas) · % (columna fija) · silencio · [salida]. |
| Dispositivos en listas largas | Selectores desplegables de **Salida** y **Entrada** con el dispositivo activo a la vista, y deslizador del micrófono con % y silencio en su icono. |
| Aplicaciones duplicadas | Las sesiones de una misma app se **agrupan**: la primera lleva icono y nombre; las demás van debajo como «Sesión 2…», unidas por una guía. **Cada una conserva su volumen**: no se suman ni se igualan. |
| Texto explicativo innecesario | Fuera. Solo quedan los motivos reales (sin aplicaciones, sin micrófono, salida por app no disponible). |
| Accesos de Windows | «Mezclador de Windows» y «Sonido de Windows», en una fila. |

Con «Mezclador en el menú Sonido» desactivado sigue el menú sencillo de antes, igual que estaba.

### Salida de audio por aplicación (experimental, desactivada por defecto)

- **Interfaz:** Windows no tiene API pública. Se usa la **interfaz interna `Windows.Media.Internal.AudioPolicyConfig`** (`IAudioPolicyConfigFactory`), la misma de «Volumen de la aplicación y preferencias de dispositivo» de Configuración, que también usan proyectos abiertos como EarTrumpet (MIT).
  - Hay dos variantes conocidas: Windows 10 1803–21H1 y Windows 11/21H2+. Se prueban las dos.
  - Con `Get` y `Set` sobre `SetPersistedDefaultAudioEndpoint`, para los roles multimedia y consola, como Configuración.
- **Aislamiento:** todo ocurre en un **proceso auxiliar** (`WinMac.Bar.App --audio-route probe|get|set`), con un tiempo máximo de 5 s. Si la interfaz no encaja con tu Windows, el que falla es ese proceso, no la Barra, y queda anotado en `bar.log`.
- **Verificación:** cada cambio se **lee de vuelta**. Si Windows no lo guardó, se informa del fallo y nunca se da por aplicado. Un cambio solo afecta a la app elegida (su proceso).
- **Persistencia:** la preferencia la guarda Windows por aplicación, como al elegirla en Configuración, así que sobrevive a que la app reinicie su sesión de audio. «Predeterminada del sistema» la borra.
- **Uso:** Ajustes › Barra › Sonido › «Salida por aplicación (experimental)». Con ella activa y aceptada por Windows, cada fila muestra una píldora con su salida («Predeterminada», «Audífonos USB»…) y al tocarla se elige. Si Windows no la acepta, no hay píldoras y una línea dice por qué. El mezclador de Windows sigue a un toque.
- **No validado en hardware:** el CI no tiene dispositivos de audio. Pruébalo así: Spotify → altavoces, Discord → audífonos USB, Chrome → altavoces. Después reinicia cada app y comprueba que la preferencia se mantiene. Vuelve a «Predeterminada».

Archivos: `PanelBuilder.SoundPanel`/`MixerRows`/`OutputLabel`, `PanelModel` (fila compacta, `MaxHeight`), `PanelController` (selector de salida), `PanelOperations` (`appout:`/`approute:`), `SystemHub.RefreshRouting`, `IAppAudioRouting` (System.Core), `AudioPolicyRouting` (System.Win), `AppAudioRoutingClient` (Bar.Win), `WinMac.Bar.App --audio-route`, `PanelPainter.Hub` (fila compacta), ajuste `appOutputRouting`.
Pruebas: orden y medidas del menú, % y silencio, dispositivo activo visible, agrupado sin mezclar volúmenes, límite de filas, alto máximo con desplazamiento, salida solo con la función activa y aceptada, motivo cuando no, columnas alineadas y clics en silencio, salida y deslizador. Bar.Core: 433 pruebas.
