# Propuesta: NotchWin como módulo de WinMac con ajustes centrales

Estado: **IMPLEMENTADO en local (F3a, F4a y F4b), pendiente de publicar y de validar en Windows.** Aprobado por Felipe: N1–N7, proceso aparte + IPC, ajustes centrales y D9 (submódulo git fijado a un commit). Ver §7 (cómo quedó, pruebas, guía manual y límites). Las secciones 1–6 conservan la propuesta original; donde difiere, manda §7 (por ejemplo, los tópicos usan puntos: `config.notch`, no barras).

Decisiones tomadas por Felipe en esta conversación: (1) NotchWin sigue como **proceso aparte** hablando con WinMac por **IPC** (D3); (2) **ajustes centrales desde ya** (adelanta F3 y parte de F4).

## 1. Lo que se verificó en NotchWin (OBSERVADO, commit `99cd5b2`, rama `claude/vorssaint-windows-clone-cioxjq`)
| Hecho | Consecuencia |
|---|---|
| `net10.0-windows10.0.19041.0`, WinUI 3 sin empaquetar, WinAppSDK 1.6.250108002 autocontenido | Mismo TFM que WinMac: la migración .NET 8 → 10 de `docs/13` ya está hecha. Siguen dos runtimes de UI (Win32/D2D y WinUI 3) → proceso aparte |
| No tiene `--hosted`, canal IPC, instancia única ni evento de cierre | Hay que añadirlos (N1–N3) |
| Ajustes en `%LOCALAPPDATA%\NotchWin\settings.json` (`AppSettings` + `SettingsStore`, JSON plano) | Fuente a migrar (N4) |
| Icono de bandeja propio con «Ajustes…» y «Salir» (`TrayIconService`) | Duplicaría el de WinMac (N6) |
| `AppSettings.AnthropicAdminApiKey` guarda una clave **en texto plano** en `settings.json`; solo la UI y una prueba la usan (ningún proveedor la lee) | **Discrepancia** con la regla «nunca guardar credenciales». **No se migra al archivo central**; propongo quitarla de NotchWin (N7) |
| El comentario de `LyricsEnabled` dice `lrclib.net`; el cliente real es `NeteaseLyricsClient` | Doc desactualizada en NotchWin; el README debe decir NetEase |

## 2. Qué es «central» y qué no
| Central (en WinMac, lo edita el usuario en un solo lugar) | Local de NotchWin (no se mueve) |
|---|---|
| `NotchModuleEnabled`, `NotchPosition`, `GlassLevel`, `NotchSections` (orden y visibilidad), `MusicPlayer`, `CustomPlayerPath`, `LyricsEnabled`, `LyricsInPill`, `PollIntervalSeconds` | Rutas de datos (`ClaudeHomeOverride`, `CopilotHomeOverride`, `AdditionalCodexHomes`, `OpenCodeDataOverride`, `DownloadsFolder`, `CalendarIcsPath`), `usage-history.json`, `pricing.json` |
| | **Secretos: ninguno en el archivo central** |

`MusicModuleEnabled` y `AiAgentsModuleEnabled` salen de las pestañas (`NotchSections`); no se duplican.

## 3. Diseño
**WinMac (host)** — sin tocar NotchWin, es la parte que se puede construir y probar primero:
- `winmac.json` gana la sección `notch` con los campos centrales de §2 (valores por defecto = los de NotchWin hoy). Escritura **atómica** (archivo temporal + reemplazo) y validación/saneado de cada valor.
- Tópico IPC retenido `config/notch` (el canal ya retiene el último valor): el host lo publica al conectar el módulo y a cada cambio.
- Mensaje de módulo → host `config/notch/set` con un **parche** (solo los campos cambiados). El host valida, guarda y vuelve a publicar. Ningún módulo escribe `winmac.json` directamente.
- Importación **única** y no destructiva: si `winmac.json` no tiene `notch` y existe `settings.json` de NotchWin, se copian solo los campos centrales (nunca la clave) y se anota en el log; el archivo original no se borra.
- Bandeja: entradas «Abrir Isla» (ya cubierta por la reapertura de módulos) y «Ajustes de la Isla…» (le pide al módulo abrir su ventana de ajustes).
- El módulo `notch` sigue **desactivado por defecto** y ausente = Failed aislado: WinMac funciona igual sin NotchWin (regla ya aprobada).

**NotchWin** — cambios concretos pedidos (N1–N7), todos con interruptor: sin `--hosted` se comporta **exactamente como hoy**:
- **N1** Referenciar `WinMac.Ipc` (ver D9) y, con `--hosted --pipe <n>` + `WINMAC_TOKEN`, abrir el enlace: `ready`, `pong`, `bye`; borrar el token del entorno tras leerlo.
- **N2** Salud real: `HealthCheck` que solo contesta si el bucle de interfaz (`DispatcherQueue`) está vivo, igual que Dock y Barra. `shutdown` → `ExitApp()` por el camino normal; salida voluntaria = `bye` + código 0.
- **N3** Instancia única (mutex propio) para que un doble inicio no cree dos notchs por monitor.
- **N4** Fuente de ajustes: `SettingsStore` detrás de una interfaz; en modo hosted los campos centrales llegan de `config/notch` y los cambios salen como `config/notch/set`. Los locales siguen en `settings.json`. Si el host no está o se cae, usa el último valor recibido y reintenta (como los módulos de WinMac).
- **N5** Aplicación en vivo: el cambio de un campo central pasa por el `NotchViewModel` y sus eventos (`SectionsChanged`, `GlassChanged`…), **nunca** moviendo ventanas por otra vía (reglas de NotchWin: `ApplyLayout`/`RelayoutIfExpanded`, DPI sin mover la ventana). No se toca la escala de Windows ni se añade reescalado automático.
- **N6** En modo hosted, ocultar el icono de bandeja de NotchWin (la bandeja es de WinMac); «Ajustes» se abre por orden del host.
- **N7** Quitar `AnthropicAdminApiKey` (campo, UI y prueba) o, como mínimo, no escribirla a disco; corregir comentario y README de letras (NetEase).

Lo que **no** cambia: privacidad (sin telemetría; NetEase solo con letras activadas), nada de tokens OAuth de Claude, ni la UI/visual del notch.

## 4. Fases propuestas (cada una con su CI de Windows y tu aprobación para subir)
| Fase | Qué | Toca NotchWin |
|---|---|---|
| **F3a** | Host: sección `notch`, tópico `config/notch`, `config/notch/set`, importación única, entradas de bandeja; pruebas (Linux) + Test-Host | No |
| **F4a** | N1–N3 y N6: NotchWin como módulo supervisado (arranca, se vigila, cierra, un solo notch) con sus ajustes **aún locales** | Sí, solo hosted |
| **F4b** | N4–N5 y N7: ajustes centrales de verdad | Sí |
| **F5** | Empaquetado (`dist\WinMac\Notch\`, runtime WinAppSDK duplicado ~100 MB), instalador | — |

Orden elegido a propósito: lo central llega tras tener el módulo estable, para que un fallo de ajustes no esconda uno de ciclo de vida.

## 5. Riesgos
- **Tamaño**: el runtime autocontenido de WinAppSDK se duplica junto al de WinMac. Aceptable en desarrollo; se revisa en F5.
- **Smart App Control** bloquea .exe sin firmar: solo en tu PC de pruebas (ya desactivado por ti). La firma es F5.
- **Compilar NotchWin en el CI de WinMac**: el repo no se copia; el CI hace checkout de un **SHA fijado** (D1). La ruta debe ser corta (límite MAX_PATH del compilador XAML).
- **Fusión de ajustes**: si el usuario edita `winmac.json` a mano mientras corre, gana el último en escribir; el host revalida y solo recarga al reiniciar el módulo (no hay vigilancia de archivo en F3a).

## 6. Decisiones que necesito de ti
- **D9 — cómo comparte NotchWin el código del canal (`WinMac.Ipc`)**: (a) NotchWin copia esos ~6 archivos, o (b) se referencia por submódulo git a un SHA fijo, o (c) esperar al monorepo (D1). Recomiendo (b): una sola fuente de verdad y sin copias que diverjan.
- Aprobar la lista N1–N7 (o tachar lo que no quieras), y el **orden F3a → F4a → F4b**.
- Confirmar que se **quita** la clave de administrador de Anthropic (N7).

## 7. Cómo quedó (implementación, pruebas y validación)

### 7.1 WinMac (rama `claude/vibrant-keller-7tyi6u`)
- `NotchSettings` (Host.Core): ajustes centrales validados; parche camelCase que ignora lo desconocido o inválido campo a campo; importación **no destructiva** (`ImportFromNotchWin`: ni claves ni rutas locales).
- `HostConfig` v2: sección `notch`, **guardado atómico**, la Isla **habilitada por defecto**. Un módulo **opcional** sin ejecutable queda `NotInstalled` («no instalada»): ni se lanza ni cuenta como fallo; la Barra y el Dock siguen siendo obligatorios.
- `ConfigService` es el dueño único de `winmac.json` en ejecución; `HostController` publica `config.notch` (retenido), acepta `config.notch.set` **solo del módulo `notch`**, y activa o desactiva módulos en vivo.
- `Supervisor.SetEnabledAsync`: cierra o lanza UN módulo sin tocar a los demás ni al host (desactivar = cierre normal, `Disabled`, sin reinicio; activar = lanzamiento inmediato con el historial de caídas limpio; nada durante el cierre del host).
- `SettingsWindow` (Win32 puro): **única** ventana de configuración (módulos, inicio con Windows y opciones de la Isla), cambios **en vivo** (sin «Guardar»). Bandeja: «Abrir/Activar …» por módulo, «Ajustes de WinMac…» y «Salir».
- Canal: `IpcServer.Emit` (evento no retenido, para «abrir ajustes locales») y `ModuleHostLink.Subscribe/Event`. Commit del canal que fija NotchWin: `2374531`.
- Migración única al primer arranque: si `winmac.json` no tiene `notch` y existe `%LOCALAPPDATA%\NotchWin\settings.json`, se copian los campos centrales; el archivo original **no se toca**.
- `tools/Build-Local.ps1 -NotchPath C:\nw` publica NotchWin (autocontenido) en `dist\WinMac\NotchWin\`; sin clon, WinMac funciona y la Isla sale «no instalada».

### 7.2 NotchWin (rama local `claude/winmac-integration`, sobre `99cd5b2`)
- `external/winmac`: submódulo de `PipeSick/win11-macos` fijado a `2374531` (**una sola** implementación de `WinMac.Ipc`).
- `NotchWin.Core/Hosting`: `CentralSettings` (qué posee WinMac, `Diff`) y `HostedSession` (solo existe con `--hosted`).
- N1/N2: enlace, ajustes retenidos al conectar, latido que **solo responde si el hilo de interfaz está vivo**, `shutdown` → cierre normal con aviso de salida voluntaria. N3: instancia única. N4/N5: ajustes centrales aplicados en vivo solo donde cambiaron (pestañas, vidrio, reproductor, letras, módulos música/agentes, sondeo, posición); los cambios hechos en el propio notch se piden al host y **no se escriben en disco**. N6: sin bandeja propia; su ventana de ajustes queda solo para rutas y datos locales. N7: `AnthropicAdminApiKey` eliminada (campo, interfaz, prueba) y borrada de los `settings.json` anteriores.
- Sin `--hosted`, NotchWin funciona como antes (ejecución independiente, con su bandeja y su ventana de ajustes completa).

### 7.3 Decisiones de implementación que conviene saber
- La **posición** del notch se aplica recreando sus ventanas (así no se toca `NotchWindowController`, cuyas reglas de DPI y de no mover la ventana se respetan).
- Los ajustes centrales **no se copian de vuelta** al `settings.json` de NotchWin: si se ejecuta suelto después, conserva los valores de antes de la migración.
- Si el host cae, el notch sigue con los últimos ajustes recibidos y reintenta conectar.
- Si ya hay un NotchWin suelto abierto cuando WinMac lanza el suyo, el nuevo avisa «salida voluntaria» y sale: queda «detenido» (no «caído»), y la bandeja ofrece «Abrir Isla» cuando se cierre el otro.

### 7.4 Pruebas
- WinMac (Linux, sin advertencias): Common 12 · Apps 25 · Dock 135 · Bar 23 · **Ipc 29** · **Host.Core 64** = **288**. Nuevas: valores y parches de `NotchSettings` (rangos, tipos, enums, pestañas, importación sin secretos), `ConfigService` (guardado, no-op, fallo de disco, conserva el resto del archivo), `HostController`, activar/desactivar en vivo y módulo no instalado, **procesos reales** (apagar/encender un módulo por la configuración) y **canal real** (ajustes de ida y vuelta, orden del host).
- NotchWin (Linux): Core 74 de 76. Las 2 que fallan (`MusicPlayerLauncherTests.NamesAndSessionTokens` y `Feishin_FindsTheFirstExistingStandardLocation`) usan rutas de Windows y ya fallaban en Linux antes de estos cambios; no tocan lo nuevo. Nuevas: `CentralSettings`, `HostedSession` contra el servidor real, y el borrado de la clave del disco.
- **Compilación de la aplicación WinUI de NotchWin**: el compilador XAML solo corre en Windows. Se verificó el C# (con stubs de las clases generadas por XAML, fuera del repositorio): compila sin errores; el XAML de ajustes se comprobó bien formado. **No se ha compilado ni ejecutado la aplicación completa.**

### 7.5 Guía manual en Windows (tras publicar; ≈ 20 min)
Preparación: `git pull` de WinMac; clonar NotchWin **en una ruta corta** (`C:\nw`) con `git clone --recurse-submodules` (rama `claude/winmac-integration`); luego `powershell -ExecutionPolicy Bypass -File tools\Build-Local.ps1`. Cierra antes cualquier WinMac, NotchWin, Dock o Barra.
| # | Qué hacer | Qué debe pasar |
|---|---|---|
| M13 | `dist\WinMac\WinMac.exe` | Barra, Dock **y la Isla** aparecen; **un solo** icono en la bandeja (el de WinMac) |
| M14 | Clic derecho en la bandeja | Estado de los 3 módulos, «Ajustes de WinMac…» y «Salir» |
| M15 | «Ajustes de WinMac…» | Una ventana con módulos, inicio con Windows y opciones de la Isla |
| M16 | Quita «Isla» | Solo la Isla se cierra; Barra y Dock siguen. Vuelve a marcarla: reaparece enseguida |
| M17 | Cambia el vidrio, la posición, una letra o el orden de pestañas | La Isla lo aplica **sin reiniciar**; el notch no salta ni parpadea |
| M18 | Reordena pestañas desde el propio notch | El cambio se refleja en la ventana de WinMac |
| M19 | Cierra WinMac con «Salir» | Se cierran todos los módulos, incluida la Isla; no queda ningún proceso |
| M20 | Mata `NotchWin.App.exe` en el Administrador de tareas | WinMac lo reinicia solo; la Barra y el Dock no parpadean |
| M21 | Sin `C:\nw` (compila sin la Isla) | WinMac arranca igual; la bandeja dice «Isla: no instalada» |
| M22 | Si tenías `%LOCALAPPDATA%\NotchWin\settings.json`: abre WinMac por primera vez | Tus ajustes de antes (vidrio, pestañas, reproductor…) aparecen; el archivo original sigue ahí **sin** la clave de Anthropic |
| M23 | «Rutas y datos de la Isla…» | Se abre la ventana de NotchWin solo con rutas y datos locales |
| M24 | `NotchWin.App.exe` suelto (sin WinMac) | Funciona como antes, con su bandeja y su ventana de ajustes |

### 7.6 Límites y pendientes
- **Todo lo anterior solo corre en Windows y no se ha ejecutado**: la ventana de ajustes de WinMac (Win32 escrito a mano, sin captura ni prueba visual), el menú de la bandeja, el arranque real de la Isla bajo el host y los cambios en vivo sobre WinUI.
- El CI de WinMac **todavía no compila ni ejecuta NotchWin**: requiere que su rama esté publicada y fijar su SHA (D1); queda para después de autorizar la publicación.
- Se mantienen pendientes de NotchWin: Feishin sin probar, escala distinta de 100 %, monitores reales y Haiku 5.5 sin precio (no son parte de esta integración).
