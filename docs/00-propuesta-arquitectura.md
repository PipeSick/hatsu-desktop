# WinMac — Propuesta de arquitectura y viabilidad (Fase 0)

Estado: **BORRADOR PARA APROBACIÓN**. No hay código. Fecha: 2026-10-30.

Convención de evidencia:
- **[V]** verificado en fuente consultada hoy (enlaces al final).
- **[C]** conocimiento técnico previo, estable, pero **no re-verificado hoy**.
- **[H]** hipótesis: hay que probarla con un spike antes de comprometerse.

> Mi búsqueda web hoy fue acotada (9 consultas). Todo lo marcado [C]/[H] debe entrar
> en la Fase 1 como spike con criterio de aceptación, no darse por cierto.

---

## 1. Evaluación de viabilidad (resumen honesto)

La visión completa es **alcanzable en un ~70-80 % funcional**, no al 100 % idéntico a macOS.
Lo que impone el techo es Windows, no el esfuerzo:

1. **Ventanas de terceros**: se pueden mover, redimensionar, minimizar, ocultar, listar, y
   cambiarles esquinas/sombra/backdrop vía DWM [V parcial]. **No** se puede reemplazar su barra de
   título ni inyectar botones semáforo sin inyección de código (invasivo, rompe anti-cheat/EDR). → Se
   descarta por principio (#7, #16, #17).
2. **Menús de apps activas**: no existe un "global menu" en Windows. UI Automation permite leer el
   árbol de menús de apps Win32/WPF/UWP razonables [C], pero es lento, parcial y nulo en apps
   Electron/Qt/juegos sin accesibilidad. → Parcial, con degradación explícita.
3. **Escritorios virtuales**: API pública solo `IVirtualDesktopManager` (¿en qué escritorio está una
   ventana? / mover ventanas **propias**) [V]. Crear/cambiar/mover ventanas ajenas exige interfaces COM
   **no documentadas** que cambian entre builds [V]. → Aislar tras una capa reemplazable con
   degradación.
4. **Reemplazar Explorer.exe como shell**: Microsoft ha bloqueado herramientas de personalización en 24H2
   (reportes de 2024, antiguos) [V, dated]. → **No reemplazamos el shell.** Convivimos con él.
5. **SmartScreen**: ningún certificado da reputación instantánea; ni EV [V].
6. **Apple**: iMessage/AirDrop/Handoff/iCloud no tienen protocolo abierto utilizable → no viable
   (§5). Alternativas propias o abiertas sí.

**Recomendación estratégica:** *convivencia, no sustitución*. WinMac corre como shell de usuario
superpuesto (AppBar + ventanas propias), esconde/ajusta la barra de tareas de forma reversible, y nunca
toca archivos del sistema.

---

## 2. Comparación de tecnologías

| Tecnología | Rol propuesto | Veredicto |
|---|---|---|
| **C# / .NET (LTS actual)** | Lógica, servicios, UI | Sí. Verificar versión: .NET 10 es el objetivo del usuario; confirmar compatibilidad con la versión estable de Windows App SDK elegida [H]. |
| **Windows App SDK** | Ciclo de vida, `AppWindow`, backdrops, instalador MSIX opcional | Sí. Hoy existen líneas **1.8.x y 2.x** estables [V, fuente secundaria]; fijar versión en Fase 1. |
| **WinUI 3** | UI de Settings, Finder, Launchpad, Spotlight | Sí para ventanas "normales". Riesgo: ventanas sin borde/transparentes, AppBar y arranque en frío pesan más que Win32 puro [C]. |
| **DirectComposition / Windows.UI.Composition (Visual Layer)** | Dock, Mission Control, animaciones | **Sí, para superficies críticas** (dock, barra). Animaciones en compositor = 60 FPS+ sin pasar por el hilo UI [C]. |
| **Direct2D/Direct3D** | Renderizado propio (dock magnificado, blur personalizado) | Solo si Composition no alcanza. Más control, más código [H]. |
| **Backdrops DWM / Mica / Acrylic** | "Liquid Glass" aproximado | Mica y Acrylic soportados; Acrylic solo para superficies transitorias según guía MS [V]. `DWMWA_WINDOW_CORNER_PREFERENCE` desde build 22000 [V]. **Liquid Glass real (refracción, lensing) no existe nativo**; se aproxima con shaders propios [H]. |
| **Win32 / Shell APIs** (`SHAppBarMessage`, `IShellItem`, `IFileOperation`, `IContextMenu`, `SHChangeNotify`) | Dock/menú como AppBar, operaciones de archivos, menús contextuales nativos | Sí, núcleo [C]. P/Invoke/CsWin32 (generador de bindings MIT [C]). |
| **UI Automation** | Menús/ventanas de apps externas, nombre activo | Parcial [C]. |
| **Windows Search (índice)** | Spotlight: archivos/apps | Sí vía OLE DB/`ISearchQueryHelper` [C]; alternativa: Everything SDK (licencia a verificar) [H]. |
| **Virtual Desktop COM no documentado** | Mission Control / Spaces | Solo tras abstracción; usar proyectos que rastrean builds (MScholtes, Slions) [V]. |
| **Rust + Tauri (como Seelen UI)** | Alternativa total | Seelen usa Rust+TS+Tauri sobre WebView2 [V]. Es "web disfrazada" → **contradice la restricción 6**. Descartado como base, útil como referencia. |
| **Electron** | — | Descartado (principio 13). |
| **WPF / Avalonia / Qt** | Alternativas UI | WPF: maduro para ventanas sin borde y AppBar, sin Composition moderno ni Fluent nativo; **candidato legítimo para dock/barra** (ver ADR-001). Avalonia/Qt: ajenos al stack MS, sin ganancia clara. |

**Decisión propuesta (a ratificar con spike):** *UI híbrida*. Superficies de baja latencia (Dock, Menu
Bar, overlay de Mission Control) en **Win32 + Visual Layer/DirectComposition** vía C#; apps ricas
(Settings, Finder, Launchpad) en **WinUI 3**. Un solo proceso por rol, un único modelo de dominio.

---

## 3. Arquitectura recomendada

### 3.1 Procesos

```
winmac.exe (Host/Supervisor, sin UI)  ── servicios núcleo, bus, config, watchdog
 ├─ winmac-shell.exe   Dock + MenuBar + Desktop + Mission Control + Quick Look overlay
 ├─ winmac-finder.exe  Finder (multi-instancia)
 ├─ winmac-launcher.exe Spotlight + Launchpad (arranque en caliente, residente)
 └─ winmac-settings.exe Settings + Control Center (bajo demanda)
```
- **Aislamiento**: un crash de Finder no tumba el Dock. El supervisor reinicia con backoff y, si falla N
  veces, **restaura la barra de tareas original** (garantía de seguridad, principio 9/18).
- Todo corre **con el usuario, sin privilegios elevados**. Sin servicios de Windows ni drivers.

### 3.2 Capas (solución .NET)

```
WinMac.Abstractions   interfaces + contratos (IWindowService, IDesktopService, ...)
WinMac.Core           modelo de dominio, config, eventos, logging
WinMac.Platform.*     adaptadores Win32/Shell/UIA/VirtualDesktop (únicos con P/Invoke)
WinMac.Rendering      animación, resortes, backdrop, helpers Composition
WinMac.Modules.*      Dock, MenuBar, Finder, Spotlight, MissionControl, Launchpad, ...
WinMac.App.*          ejecutables
```
Regla: **los módulos solo dependen de `Abstractions`**. Los adaptadores `Platform.*` se pueden sustituir
(p. ej. VirtualDesktop por build de Windows) y mockear en pruebas.

### 3.3 Gestión de ventanas
Un `WindowTracker` único: `SetWinEventHook` (create/destroy/foreground/minimize/move) [C] en vez de
polling; filtra ventanas "reales" (estilo, cloaked, owner, tool-window). Los módulos consumen un
`IObservable<WindowEvent>`. Acciones (minimizar, restaurar, mover) pasan por un único `IWindowActions`
con *rate limiting* y manejo de elevación (UIPI impide actuar sobre ventanas de procesos admin) [C].

### 3.4 Comunicación entre módulos
- Intra-proceso: bus de eventos tipado (`System.Threading.Channels`/Rx).
- Inter-proceso: **Named Pipes con ACL del usuario actual** + mensajes versionados (MessagePack o
  System.Text.Json) [H]. Sin sockets TCP.
- Prohibido: memoria compartida sin validar, COM registrado globalmente.

### 3.5 Render y animaciones
Animaciones declarativas ejecutadas en el compositor (Spring/ExpressionAnimation) [C]. Presupuesto:
≤ 2 % CPU en reposo, 0 repintados cuando está oculto, reduce-motion respetado
(`UISettings.AnimationsEnabled`) [C]. Medición continua con un banco de rendimiento en CI (§9).

### 3.6 Configuración
JSON versionado en `%AppData%\WinMac\` con esquema y migraciones; escritura atómica (tmp+rename); sin
registro salvo la clave `Run` de inicio automático (reversible). Un "perfil de restauración" guarda el
estado original de la barra de tareas (autohide, posición) al instalar.

### 3.7 Seguridad
- Mínimo privilegio, sin inyección en procesos ajenos, sin hooks globales de teclado de bajo nivel salvo
  atajos con `RegisterHotKey` [C].
- "Ejecutar comandos" de Spotlight: **lista blanca explícita** y confirmación; nunca shell libre.
- Plugins (futuro): proceso separado, permisos declarados. No en v1.
- Telemetría: **ninguna** por defecto.
- Dependencias: SBOM + `dotnet list package --vulnerable` en CI [C].

### 3.8 Distribución y actualizaciones
- Formatos: **MSIX no obligatorio**. Instalador por usuario (WiX o Inno Setup — decidir) + **portable
  zip** + winget manifest [C]. Actualizador: Velopack o equivalente a evaluar [H].
- **Firma**: Azure Artifact Signing (~10 USD/mes) solo para organizaciones en USA/CAN/UE/UK e
  individuos en USA/CAN [V]; fuera de eso, OV de una CA (~300-500 USD/año) [V]. **Ninguno evita el
  aviso SmartScreen inicial** y cada build nuevo parte con reputación cero [V]. Hay reportes de abril/junio
  2026 de avisos en binarios firmados tras rotación de CA [V]. Para Chile hay que verificar elegibilidad
  [H] — decisión de negocio pendiente.
- CI/CD: GitHub Actions (windows-latest), builds deterministas
  (`ContinuousIntegrationBuild`, `Deterministic`), checksums y *attestations* [C].
- Licencia propuesta: **MIT o Apache-2.0** (decidir; Apache da patentes). Ver §4.

### 3.9 Accesibilidad y DPI
Todo WinUI con `AutomationProperties`; dock navegable por teclado; contraste alto respeta tema del
sistema; PerMonitorV2 en manifiesto; pruebas con 100/125/150/200 % y monitores mixtos [C].

---

## 4. Proyectos reutilizables / referencia

| Proyecto | Qué aporta | Licencia / estado |
|---|---|---|
| **Seelen UI** (Rust/TS/Tauri) | Dock estilo macOS, barra, widgets, tiling | Licencia **no confirmada** en mi búsqueda (revisar LICENSE) [V parcial]. Usa WebView2 → solo referencia de producto. |
| **Files** (files-community) | Explorador alternativo WinUI 3, pestañas, columnas | Relicenciado **MIT** según su blog [V]; confirmar versión actual de stack. Candidato real a estudiar/contribuir; ojo: es una app grande y su código de shell sirve como guía de `IShellItem`. |
| **YASB** (Python/PyQt6) | Barra de estado configurable | Referencia de widgets; stack ajeno [V]. |
| **GlazeWM / Komorebi** | Tiling | Referencia de gestión de ventanas; licencias a verificar [H]. |
| **Flow Launcher / PowerToys Run / Everything SDK** | Spotlight | Licencias **no verificadas hoy** [H]. |
| **MScholtes/VirtualDesktop, Slions.VirtualDesktop** | Escritorios virtuales por build | Mantenidos por comunidad; fragilidad entre builds [V]. |
| **Open-Shell, ExplorerPatcher, StartAllBack** | Ejemplos de lo que MS bloquea | Evidencia de riesgo, no para reutilizar [V]. |

Regla de reutilización: copiar código solo si la licencia es compatible y se anota en `THIRD_PARTY.md`.
Nada de recursos de Apple (iconos, fuentes SF, sonidos). Fuentes: Inter/Noto/Segoe; iconos propios
(Fluent/Lucide, licencias a verificar [H]).

---

## 5. Limitaciones importantes

1. Sin barra de título/botones semáforo en apps ajenas (sin inyección). Aproximación: **overlay de
   ventana propia no es fiable** (z-order, DPI, capturas). Se ofrece solo para ventanas **propias de
   WinMac** y, en ajenas, atajos y "snap/zoom" equivalentes.
2. El botón verde macOS (pantalla completa a escritorio nuevo) requiere mover la ventana de otro
   proceso a otro escritorio virtual → solo vía COM no documentado.
3. Barra de tareas: ocultarla es posible (autohide vía `SHAppBarMessage`) [C]; **no** es posible
   eliminarla sin parches.
4. Menús de apps activas: ver §1.2.
5. Brillo: control directo solo en pantallas internas (WMI/`SetMonitorBrightness`) [C]; externas vía DDC/CI
   si el monitor lo soporta [C].
6. Wi-Fi/Bluetooth: lectura y toggle de radios con `Windows.Devices.Radios` [C]; elegir red y emparejar
   requieren abrir Configuración (o API Native Wifi, parcialmente) [H].
7. Notificaciones de otras apps: leer vía `UserNotificationListener` exige permiso y paquete con
   identidad [C/H]. "Centro de notificaciones" propio puede ser limitado; alternativa: abrir el de Windows.
8. Quick Look con barra espaciadora: requiere detectar selección en Explorer ajeno (UIA/Shell) [H]. En el
   Finder propio es total.
9. Pantalla completa/juegos: ocultar overlays al detectar ventana en *exclusive fullscreen* (`SHQueryUserNotificationState`) [C]. Sin hooks en procesos de juego.
10. Anti-cheat/EDR: cualquier inyección está descartada por diseño.

---

## 6. Riesgos técnicos

| # | Riesgo | Prob. | Impacto | Mitigación |
|---|---|---|---|---|
| R1 | Cambio en 25H2/26H1 rompe COM de escritorios virtuales | Alta | Medio | Capa aislada + degradación + tests por build |
| R2 | Microsoft endurece la barra de tareas/shell | Media | Alto | No parchear nada; solo APIs públicas; kill-switch |
| R3 | Dock a 60 FPS con alto DPI multimonitor | Media | Alto | Spike de rendimiento antes de comprometer WinUI vs Win32 |
| R4 | Conflictos con otras herramientas de personalización | Media | Medio | Detección y aviso |
| R5 | SmartScreen frena adopción | Alta | Medio | Firma + reputación + winget + portable |
| R6 | Alcance gigante, abandono | Alta | Alto | Fases verificables, estabilidad > cantidad |
| R7 | Marca/legal (iconos, nombres "Finder", "Spotlight", etc.) | Media | Medio | Nombres propios en UI pública; revisión legal ligera |
| R8 | Falla del dock deja al usuario sin lanzador | Baja | Alto | Watchdog que restaura la barra original |
| R9 | Accesibilidad deficiente por render personalizado | Media | Medio | UIA desde el día 1 |
| R10 | Versión de Windows App SDK / .NET inestable | Media | Medio | Fijar versiones; spike |

---

## 7. Matriz de viabilidad

Clave: **C** = implementable completamente · **P** = parcialmente · **L** = dependiente de limitaciones de Windows · **N** = no viable hoy.

| Módulo / función | Clase | Razón |
|---|---|---|
| **Dock**: posición, tamaño, autohide, separadores, magnificación, anclados, recientes | C | Ventana propia + AppBar/Composition |
| Dock: indicadores de apps abiertas, minimizar/restaurar | C | WinEventHook + `ShowWindow` (no sobre procesos elevados) |
| Dock: menús contextuales de app / jump lists | P | Jump lists accesibles vía Shell, calidad variable |
| Dock: Papelera | C | `IShellItem` de Recycle Bin, `SHEmptyRecycleBin` |
| Dock: miniaturas de ventanas | P | `DwmRegisterThumbnail` [C]; no en ventanas cloaked/minimizadas con detalle |
| Dock: soporte multimonitor | C | Una instancia por monitor |
| **Menu Bar**: reloj, batería, volumen, red, BT (estado) | C | APIs documentadas |
| Menu Bar: nombre de app activa | C | `GetForegroundWindow` + proceso |
| Menu Bar: menús de la app activa | L | UIA parcial; no universal (§1.2) |
| Menu Bar: iconos de bandeja de terceros | L | Bandeja de Windows no ofrece API pública para reproducirla; solo vía hooks/notify area internals [H] |
| Menu Bar: notificaciones | L | `UserNotificationListener` con identidad/permiso (§5.7) |
| **Finder**: vistas icono/lista/columnas/galería, pestañas, favoritos, etiquetas, vista previa, operaciones | C | Shell APIs, `IFileOperation`, preview handlers |
| Finder: etiquetas de color | P | Sin concepto nativo; guardarlas en **ADS** o BD propia (ADS se pierde fuera de NTFS) [H] |
| Finder: rutas de red/UNC, menús contextuales del shell | P | `IContextMenu` funciona pero con extensiones de terceros inestables |
| **Spotlight**: apps, archivos, calculadora, comandos, ajustes, atajo | C | Windows Search/propia indexación; "ajustes" vía URIs `ms-settings:` |
| **Mission Control**: vista de ventanas, cambio, multimonitor | P | Miniaturas por DWM; rendimiento y ventanas protegidas |
| Mission Control: escritorios virtuales | L | COM no documentado (§1.3) |
| **Launchpad**: cuadrícula, carpetas, búsqueda, DnD | C | Detección vía Start Menu, UWP via `PackageManager` |
| **Control Center**: volumen, salida audio, modo oscuro, medios | C | Core Audio, SMTC, registro de tema |
| CC: brillo | P | Depende del monitor (§5.5) |
| CC: Wi-Fi/Bluetooth toggle | P | Radios sí; selección de red → abrir Configuración |
| **Ventanas**: esquinas, sombras, backdrop en ajenas | P | `DwmSetWindowAttribute` [V], varía por app y build |
| Ventanas: botones semáforo en ajenas | N | Requiere inyección/subclasificar (descartado) |
| Ventanas: botones semáforo en propias | C | Control total |
| Ventanas: maximización estilo macOS, tiling básico/snap | P | Mover/redimensionar sí; "fullscreen a nuevo Space" depende de COM |
| **Quick Look**: imágenes/PDF/texto/audio/video/info | C (en Finder) / P (en Explorer ajeno) | Detectar selección ajena (§5.8) |
| **Desktop**: fondos, widgets propios, esquinas activas | C | Ventana al fondo (WorkerW) [C], hot corners por hilo de ratón |
| Desktop: gestión de iconos del escritorio de Windows | L | Controlar `SysListView32` de Explorer es hackeo frágil [H] |
| **Settings** (módulos, atajos, restauración, desinstalar) | C | App propia |
| **Avanzado**: Stage Manager | P | Reglas de agrupación y snapping propias; sin fidelidad total |
| Gestos multitáctiles (trackpad de precisión) | P | Raw Input/`PointerDevice` limitado; gestos de 3-4 dedos reservados por Windows [H] |
| Portapapeles/Archivos entre dispositivos propios (WinMac↔Android/Linux) | C | Protocolo propio / abierto (p. ej. KDE Connect/LocalSend como referencia, licencia a verificar) |
| AirDrop real, Handoff, Continuity, iMessage, iCloud | **N** | Protocolos propietarios cerrados; no prometer |
| Integración iPhone | P | Solo apps/protocolos permitidos (compartir archivos por web/LocalSend, "Phone Link" de MS no integrable) [H] |
| Modo claro/oscuro, fondos dinámicos, sonidos libres | C | |
| Automatizaciones | P | Motor propio de reglas (v2+) |
| Desinstalar / restaurar Windows original | C | Diseño de reversibilidad desde el día 1 |

---

## 8. Propuesta de módulos (paquetes)

`Core`, `Platform.Windowing`, `Platform.Shell`, `Platform.Audio/Power/Network`, `Platform.VirtualDesktop`,
`Rendering`, `Dock`, `MenuBar`, `Finder`, `Launcher` (Spotlight+Launchpad), `MissionControl`,
`ControlCenter`, `QuickLook`, `Desktop`, `Settings`, `Updater`, `Installer`, `Tests.*`.

---

## 9. Plan por fases (cada una cerrada y aprobada antes de la siguiente)

| Fase | Objetivo | Entregable probable | Aceptación |
|---|---|---|---|
| **0** | Esta propuesta | Este documento | Tu aprobación + decisiones §11 |
| **1** | Fundaciones + spikes | Solución .NET, CI, supervisor, config, logging, ciclo instalar/restaurar barra de tareas; **spikes**: Composition vs WPF para dock, SetWinEventHook, virtual desktops, UIA menús | CI verde; ADRs escritos con mediciones; restaurar barra funciona en 3 resoluciones/2 monitores |
| **2** | **Dock mínimo real** | Dock con anclados + abiertos + magnificación, 1 monitor | 60 FPS medidos; ≤2 % CPU reposo; minimiza/restaura apps reales; crash → barra restaurada |
| 3 | Dock completo | Multi-monitor, autohide, DnD, miniaturas, Papelera, config | Matriz Dock de §7 en C/P verificada |
| 4 | Menu Bar | Barra superior con reloj/batería/vol/red + app activa + (opcional) menús UIA | Probado con 10 apps muestra; documentar cuáles fallan |
| 5 | Settings + Control Center | Gestión de módulos, atajos, apariencia | Desactivar todo = Windows original |
| 6 | Spotlight | Índice + calculadora + ajustes | Latencia < 100 ms en consultas locales |
| 7 | Launchpad + Desktop | | |
| 8 | Finder (núcleo) | Vistas + pestañas + operaciones con `IFileOperation` | No pierde datos en suite de pruebas de operaciones |
| 9 | Quick Look + Finder avanzado | | |
| 10 | Mission Control / Window Experience | Con degradación por build | |
| 11 | Avanzado (Stage Manager, sync propio) | | |
| 12 | Distribución v1.0 | Firma, winget, auto-update, docs | |

---

## 10. Estrategia de pruebas

- **Unitarias** sobre `Core` y `Abstractions` (xUnit), adaptadores *mockeados*.
- **Integración** en runner `windows-latest` + VMs locales con 11 22H2/23H2/24H2/25H2 [H: disponibilidad de imágenes].
- **UI**: UIA-based automation (FlaUI [C]) y capturas de comparación con tolerancia.
- **Rendimiento**: banco automatizado (frame time, CPU, memoria, tiempo de arranque), umbrales que rompen CI.
- **Multi-monitor/DPI**: matriz manual guiada + VM con 2 pantallas virtuales.
- **Seguridad**: SAST (CodeQL), dependencias, revisión de IPC.
- **Reversibilidad**: prueba de "instalar → usar → desinstalar" que compara estado de barra, registro, procesos y archivos.
- **Juegos/fullscreen**: checklist manual con 3 títulos (DX11, DX12, Vulkan) y un anti-cheat popular.

---

## 11. Primer componente recomendado y decisiones que necesito de ti

**Primer componente funcional: WinMac Dock (Fase 2), precedido por la Fase 1 mínima.**
Razones: es el componente más visible, ejercita las tres piezas más arriesgadas (ventanas de terceros,
renderizado a 60 FPS, reversibilidad de la barra de tareas) y valida o refuta la arquitectura rápidamente.

Decisiones pendientes:
1. **¿Aceptas "convivencia con Explorer, sin reemplazar el shell"?** (recomendado: sí)
2. **UI del dock**: dejar que el spike decida entre WinUI 3 + Composition vs Win32/WPF + Composition, o fijar ya uno.
3. **Licencia**: MIT vs Apache-2.0.
4. **Nombre público** (WinMac contiene "Mac"; riesgo de marca, bajo).
5. **Firma de código**: ¿puedes asumir ~10-30 USD/mes o un certificado OV? ¿País de residencia para elegibilidad?
6. **Versión mínima de Windows 11** soportada (propuesta: 23H2+).
7. **Hardware de pruebas**: ¿tienes Windows 11 físico/VM, multimonitor, trackpad de precisión?

---

## Fuentes consultadas hoy
- Windows App SDK (resultado secundario, versiones 1.8.x / 2.x): https://www.nuget.org/packages/Microsoft.WindowsAppSDK
- System backdrops (Mica/Acrylic): https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
- DWM window attributes: https://learn.microsoft.com/en-us/windows/desktop/api/dwmapi/ne-dwmapi-dwmwindowattribute
- Virtual desktops (libs): https://github.com/sameera/VirtualDesktop · https://www.nuget.org/packages/Slions.VirtualDesktop · https://github.com/highphilosopher/MaximizeToVirtualDesktop
- Seelen UI: https://github.com/eythaann/seelen-ui · YASB: https://github.com/amnweb/yasb · GlazeWM: https://github.com/glzr-io/glazewm
- Files (MIT): https://www.files.community/blog/posts/introducing-files-v2-3
- Bloqueo de personalización en 24H2: https://www.neowin.net/news/microsoft-blocks-even-more-customization-apps-in-windows-11-version-24h2/default.htm
- Firma y SmartScreen: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options · https://learn.microsoft.com/et-ee/windows/apps/package-and-deploy/smartscreen-reputation
