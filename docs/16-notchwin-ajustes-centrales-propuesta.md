# Propuesta: NotchWin como módulo de WinMac con ajustes centrales

Estado: **PROPUESTA, sin implementar. Nada de NotchWin se ha modificado.** Necesita tu aprobación de los cambios concretos (N1–N7) antes de tocar NotchWin (regla del proyecto: F4, solo con cambios aprobados).

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
