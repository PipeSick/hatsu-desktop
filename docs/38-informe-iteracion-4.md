# WinMac 2.0 — Cuarta iteración: corrección de la beta, mezclador de sonido, WinMac Connect y barra de tareas (WM-022 … WM-034)

Fecha: 2026-11-03 · Rama `claude/relaxed-turing-cijzko` (WinMac y NotchWin) · Base: beta `57f4854` · **Nada publicado.** Informes anteriores: `docs/36`, `docs/37` (no se modifican).

**CI verde = compilación y pruebas técnicas correctas.** No aprueba la interfaz ni el comportamiento en tu PC: eso lo validas tú con la lista del §7.
Estados: **Corregido** (causa encontrada y arreglada, con prueba automática de la lógica) · **Parcial** (lo posible hecho; el resto lo impide Windows o falta prueba real) · **Bloqueado** (no hay forma soportada).

## 1. Informe técnico por incidencia (entregable A)

| ID | Causa | Archivos | Solución | Pruebas | Estado |
|---|---|---|---|---|---|
| **WM-031** Play en NotchWin cierra la Isla | Los botones de la cabecera «En curso» llamaban al reproductor desde `async void` sin capturar errores: sin sesión multimedia, la excepción cerraba NotchWin y el host lo reiniciaba | NotchWin `Views/NotchWindow.Activities.cs`, `NotchWindow.xaml(.cs)`, `Media/IMediaControls.cs`, `GsmtcMediaProvider.cs` | Los controles pasan por `RunControl` (errores capturados y mostrados). Tres estados: **reproduciendo**, **en pausa** y **sin sesión** («Nada en reproducción», Play abre el reproductor preferido de Ajustes › Isla; anterior/siguiente se ocultan). Nunca se finge una reproducción. Letras, carátulas y aspecto sin cambios | Compila en CI; arranque real de la Isla bajo el host (integración) | Corregido · pendiente de prueba real |
| **WM-033** Autoocultación del Dock al revés | 1) Al cambiar «Ocultar automáticamente» en Ajustes, el Dock recargaba la configuración pero no se la pasaba al controlador si el diseño no cambiaba. 2) Al colocar la ventana se leía el área de trabajo ANTES de quitar el AppBar: el Dock quedaba una franja más arriba. 3) La franja de activación se medía contra el borde de la pantalla y no contra la ventana | `Dock.Win/Rendering/DockHost.cs`, `Dock.Core/DockSettings.cs` (`EdgeHot`) | Configuración siempre al controlador; AppBar quitado antes de medir; franja de 4 px en el borde inferior real del Dock | 2 pruebas nuevas (activar en vivo, franja de activación) | Corregido · pendiente de prueba real |
| **WM-026** Error 0x80070490 al borrar | `ClearNotifications()` falla con «elemento no encontrado» | `Bar.Core/Notifications/NotificationStore.cs` | Iteración 3: borrado una a una y relectura. Ahora además: prueba con la colección cambiando DURANTE el borrado | `NotificationConcurrencyWm026Tests` | Corregido · pendiente de prueba real |
| **WM-025** Notificaciones en la Isla | (iteración 3) la pestaña solo mostraba el historial propio | NotchWin (sin cambios en esta iteración) | Se mantiene: instantánea del centro de Windows, sin duplicados, expandible, persiste al cambiar de pestaña | Pruebas de mezcla y sin duplicados en NotchWin | Corregido · pendiente de prueba real |
| **WM-022** Iconos de energía | El arco pedía siempre el arco corto (iteración 3) y las formas dibujadas a mano no eran las estándar | `Bar.Win/Rendering/PanelPainter.cs` | Glifos del sistema (Segoe Fluent Icons / MDL2: Suspender luna `E708`, Reiniciar `E777`, Apagar `E7E8`); si la fuente falta, formas vectoriales cerradas. El menú no se rediseñó | Compila; pruebas del modelo del menú W | Corregido · pendiente de prueba real |
| **WM-030** Texto instructivo | Nota «nc-mode» en el centro de notificaciones | `PanelBuilder.Notifications.cs` | Eliminada sin reemplazo | Prueba: no existe | Corregido |
| **WM-029** Foto de la cuenta | Solo iniciales | `Bar.Win/Shell/AccountPicture.cs`, `PanelPainter.cs`, `BarHost.Panels.cs`, `PanelModel.cs` | `Windows.System.User.GetPictureAsync` (API pública, sin administrador), recorte cuadrado centrado, círculo; si falla, la ruta que Windows publica en `HKLM\…\AccountPicture\Users\<SID>` (solo lectura); si no hay, iniciales. Se relee al abrir el menú | Modelo: clave de imagen e iniciales | Corregido · pendiente de prueba real |
| **WM-028** Galería de capturas | Ventana blanca, cuadrícula fija, acciones apretadas | NotchWin `Views/GalleryWindow.cs` | Barra de título oscura, cuadrícula adaptable con miniaturas que conservan proporción, vista previa limitada, acciones en dos columnas con textos claros. La selección múltiple, Quick Look (doble clic/Espacio), info y estados vacío/error que ya existían se conservan | Compila en CI | Parcial · pendiente de prueba real (aspecto y DPI) |
| **WM-027** Batería | — | sin cambios | Se mantiene la verificación de la iteración 3: solo valores que Windows y el informe de batería dan | Pruebas de batería (sin ciclos inventados, tiempo solo con batería) | Corregido |
| **WM-023** Bandeja incompleta | No hay API pública para enumerar los iconos de otras apps; leer la memoria de Explorer o inyectar está prohibido | sin cambios | Se mantiene «Ver la bandeja de Windows» (abre la bandeja nativa). **No es paridad** con la bandeja de Windows y no se presenta como tal | Pruebas de la fila | Bloqueado (limitación de Windows) |
| **WM-024** Diálogo blanco | `MessageBox` | `Dock.Win/Rendering/DarkDialog.cs` | Diálogo oscuro (iteración 3) | Prueba del texto del botón | Corregido |
| **3.1 No molestar** | Windows 11 no tiene API pública para activarlo ni para leer su estado de forma fiable (el estado vive en WNF, privado) | `PanelBuilder.cs` | El mosaico abre su ajuste de Windows y dice «Abrir en Windows»; no hay interruptor falso. Sin escrituras de registro ni automatizaciones | Prueba del modelo | Bloqueado (limitación de Windows) |
| **3.2 Modo oscuro** | Solo estaba en el Centro de Control; mostraba un sí/no aunque apps y Windows tuvieran temas distintos | `PanelBuilder.cs`, `System.Core/Models.cs`, `PowerAppearanceDisplay.cs`, `PanelPainter.cs` | Mosaico real también en el menú W. Cambia el tema de apps (`AppsUseLightTheme`) y de Windows (`SystemUsesLightTheme`) como «Elige tu modo» y avisa con `WM_SETTINGCHANGE`. Si los dos temas difieren muestra «Mixto: apps claras/oscuras». El tema de WinMac es otro ajuste (Barra › Tema; «Sistema» sigue al de apps cada 5 s). Los cambios hechos fuera se ven al releer | `Dark_mode_tells_apps_and_windows_theme_apart_and_never_pretends` | Corregido · pendiente de prueba real |
| **WM-034** Ocultar barra de tareas | Pedido nuevo | `Host/TaskbarHider.cs`, `Host.Core/TaskbarLease.cs`, `Host/HostApp.cs`, `Program.cs`, `Tray.cs`, Ajustes, `tools/Restore-Taskbar.ps1`, desinstalador | Ver §4 | `TaskbarTests` (4 pruebas: ajuste y 4 combinaciones con el Dock, política, freno, registro de recuperación) | Parcial · desactivado por defecto · pendiente de la lista de validación del §4 |

## 2. Sonido (entregable B)

**El menú existente** del icono de volumen (no uno duplicado) es ahora el mezclador. Orden: 1) volumen general con % y silencio, 2) salida predeterminada, 3) entrada predeterminada con volumen y silencio del micrófono, 4) «Por aplicación»: icono, nombre, deslizador, %, silencio y actividad de cada sesión real (hasta 12; dinámico, varias sesiones de una app se agrupan por proceso), 5) salida por aplicación → mezclador de Windows, 6) configuración de sonido de Windows. La cabecera y los botones de Windows quedan fijos; el centro se desplaza si no cabe.

| API | Uso | Tipo |
|---|---|---|
| Core Audio `IMMDeviceEnumerator` / `IAudioEndpointVolume` | volumen y silencio general y del micrófono, listas de dispositivos | pública |
| `IAudioSessionManager2` / `ISimpleAudioVolume` / `IAudioSessionControl2` | sesiones por aplicación: volumen, silencio, estado activo | pública |
| `IPolicyConfig` (no documentada) | cambiar salida y entrada predeterminadas | no documentada: se comprueba antes (`CanSwitchOutput`), se verifica después del cambio y se vuelve al anterior si Windows no lo aplicó; si no es compatible, las filas quedan atenuadas con el motivo |
| salida **por aplicación** | — | **no hay API pública** (Windows usa una interna de AudioPolicyConfig). No se implementa ni se aparenta: botón «Salida por aplicación (mezclador de Windows)» → `ms-settings:apps-volume` |

Ajuste: **Barra › Sonido › «Mezclador en el menú Sonido»** (activado; desactivado vuelve el menú anterior y no se leen micrófono ni sesiones). Micrófono y sesiones solo se leen con el menú abierto (cada 2,5 s). Perfiles de sonido: no incluidos (opcionales).
Pruebas: orden, % real, filas solo de sesiones reales, sin selector falso de salida por app, estados vacíos, máximo de apps, desplazamiento con pantalla baja, entrada no conmutable sin `IPolicyConfig`, ajuste desactivado.

## 3. WinMac Connect — matriz de viabilidad (entregable C)

Solo lo verificado (fuentes: documentación de Enlace Móvil para iPhone, especificación ANCS de Apple). «Sin app» = sin app nuestra en el iPhone.

| Función | Sin app | Software existente (Enlace Móvil + «Enlace a Windows») | App complementaria propia | Esta beta |
|---|---|---|---|---|
| Notificaciones | Posible con **ANCS** (Bluetooth LE, especificación pública de Apple, requiere emparejar) | Sí | No necesaria | Abre Enlace Móvil |
| Llamadas | No (sin APIs privadas) | Sí (contestar, marcar) | No permitida por iOS | Abre Enlace Móvil |
| SMS / iMessage | No | Sí, con límites (sin grupos ni adjuntos) | No permitida por iOS | Abre Enlace Móvil |
| Transferencia de archivos | Posible por red local (servidor en el PC + Safari) con emparejamiento cifrado | Sí en versiones recientes (iOS 16+, Bluetooth LE) | Posible | No implementada |
| Batería / conectividad | No (el iPhone no las publica a un PC) | Parcial | Posible | No se muestran (no hay dato real) |
| Portapapeles universal | No (iCloud/Continuity privados) | No | Limitada por iOS (solo en primer plano) | No viable |
| Compartir enlaces | Posible con código QR en pantalla | Sí | Posible | No implementada |

Excluidos por el pedido: duplicar pantalla, control remoto, Handoff. Implementado: **Ajustes › WinMac Connect** (estado honesto, matriz, «Abrir Enlace Móvil», «Configuración de Bluetooth»). Sin datos simulados, sin credenciales de Apple, sin servidores, sin indicador en la barra (no habría datos reales que mostrar). Siguiente paso recomendado: QR de enlaces (sin red) y, después, ANCS como módulo opcional.

## 4. WM-034 — Ocultar la barra de tareas de Windows

**Cómo funciona.** Ajustes › Escritorio y Dock › «Ocultar barra de tareas de Windows» (**desactivado por defecto**, independiente de la autoocultación del Dock: las 4 combinaciones son válidas). El host, no un módulo, hace lo siguiente:
1. Escribe `%LOCALAPPDATA%\WinMac\taskbar-restore.json` con tu valor de «Ocultar automáticamente» **antes** de tocar nada (escritura atómica).
2. Activa «Ocultar automáticamente» de Explorer con la API documentada `SHAppBarMessage(ABM_SETSTATE)`. Sin eso Windows seguiría reservando la franja de la barra y las ventanas maximizadas dejarían un hueco.
3. Oculta con `ShowWindow` las ventanas `Shell_TrayWnd` y `Shell_SecondaryTrayWnd` **de Explorer**. Se comprueba el proceso porque la Barra de WinMac tiene una ventana propia con la misma clase.

No cierra, sustituye, inyecta ni parchea Explorer. No usa ganchos ni escribe en el registro por su cuenta: el único cambio que persiste es el de «Ocultar automáticamente», y se devuelve.

**Recuperación (no depende solo del proceso principal):**

| Situación | Quién la devuelve |
|---|---|
| Desactivar el ajuste / salir de WinMac | el host, al instante (lo primero del cierre) |
| Cierre forzado o caída del host | el **guardián** `WinMac.exe --taskbar-guard <pid>` (proceso aparte, fuera del Job Object): espera al host y restaura si el registro sigue ahí |
| El host deja de responder ~20 s | el guardián restaura y borra el registro; el host, al volver, ve que ya no es dueño y no la oculta otra vez en esa sesión (no se pelean) |
| Apagado, o el guardián también murió | el siguiente arranque de WinMac restaura antes de nada |
| WinMac no arranca | `WinMac.exe --restore-taskbar`, o `Restore-Taskbar.ps1` (incluido en el ZIP, no necesita WinMac.exe), o reiniciar el Explorador / cerrar sesión (las ventanas vuelven; «Ocultar automáticamente» se corrige en Configuración › Personalización › Barra de tareas) |
| Desinstalar | `Uninstall-WinMac.ps1` restaura si hay registro |

**Sin parpadeo ni sondeo agresivo.** Con el ajuste desactivado no se mira nada. Activado, hay una comprobación barata cada 2 s. Ante «TaskbarCreated» (Explorer reiniciado) vuelve a ocultar a los 1,5 s. Las pantallas nuevas y los cambios de DPI o resolución se recogen en la comprobación. Si Windows la vuelve a mostrar más de 6 veces en 60 s, WinMac se rinde en esa sesión, la deja visible y Ajustes dice por qué.

**Limitaciones de Windows 11.** Con la barra oculta, Inicio, Win+X y la bandeja nativa se abren igual (Inicio aparece donde estaría la barra). Algunas acciones de Windows (Win+T, ciertas notificaciones) pueden mostrarla un instante hasta la siguiente comprobación. Tu «Ocultar automáticamente» está activado mientras dura la ocultación. Si lo cambias a mano en ese tiempo, al restaurar vuelve el valor que tenías al activar la opción.

**Lista de validación (en tu PC; no la sustituye el CI):** activar/desactivar · cerrar WinMac normal · cerrar `WinMac.exe` desde el Administrador de tareas (debe volver en ~2 s) · reiniciar el Explorador con la opción activa (se vuelve a ocultar) · cambiar resolución y DPI · conectar y desconectar un monitor · maximizar una ventana (sin hueco abajo, con y sin Dock autoocultable) · reiniciar Windows con la opción activa (con y sin inicio automático) · `Restore-Taskbar.ps1` con WinMac cerrado.

## 5. Beta privada (entregable E)

- Commits de esta iteración: `a196fb0` … `20ca1c9` (WinMac) y `698969f`, `bd588fb` (NotchWin; fijado en `tools/notch-pin.txt`).
- CI y artefacto: §6.
- Actualizar: cierra WinMac (icono de la bandeja › Salir, o `WinMac.exe --quit`), descomprime el ZIP nuevo **sobre la misma carpeta** (o en otra y apunta el inicio automático con `WinMac.exe --install-autostart`) y abre `WinMac.exe`. Tus ajustes (`%APPDATA%\WinMac\winmac.json`, `dock.json`) se conservan. Los campos nuevos (`hideTaskbar`, `bar.soundMixer`) toman su valor por defecto sin migración destructiva.
- Avisos conocidos: el cambio de salida/entrada usa `IPolicyConfig` (no documentada); la ocultación de la barra de tareas está sin validar en hardware real; la bandeja no tiene paridad (WM-023); «No molestar» solo abre su ajuste.

## 6. Ejecución del CI

Commit `20ca1c9` (último con código; después solo documentación) · [Actions 41702458325](https://github.com/PipeSick/win11-macos/actions/runs/41702458325): los cuatro trabajos en verde (compilación y pruebas Windows, pruebas del host, integración con NotchWin + paquete reproducible + desinstalación + `Verify-Package`, compilación cruzada Linux).
**Artefacto de la beta: `winmac-paquete-20ca1c9eb0c735412539a9f844c99c4917f45ab3`** (ZIP autocontenido, con `Restore-Taskbar.ps1`). Pruebas locales: Bar.Core 426, Host.Core 126, Dock.Core 148 en verde.

## 7. Lista de prueba manual

1. **Isla**: con Spotify cerrado, «En curso» → Play abre el reproductor y la Isla NO se cierra; con música: pausa/siguiente/anterior; las letras siguen sincronizadas.
2. **Dock**: Ajustes › «Ocultar automáticamente» activado en vivo → el Dock baja y desaparece; al llevar el ratón al borde inferior vuelve. Desactivado → queda fijo y las ventanas maximizadas terminan encima.
3. **Menú W**: foto de tu cuenta (o iniciales), iconos Suspender/Reiniciar/Apagar correctos, mosaico «Modo oscuro» que cambia el tema de verdad.
4. **Sonido**: el icono de volumen abre el mezclador; mover el volumen de una app; silenciarla; cambiar salida y micrófono; «Salida por aplicación» abre el mezclador de Windows.
5. **Notificaciones**: «Borrar todo» sin error 0x80070490; sin el texto instructivo.
6. **Galería** de capturas: barra oscura, miniaturas proporcionadas, acciones legibles a 100–200 %.
7. **Barra de tareas** (WM-034): la lista del §4.
8. **WinMac Connect**: la página se abre y «Abrir Enlace Móvil» abre la app de Microsoft.
9. Regresiones: Spotlight, Launchpad, Quick Look, capturas, notificaciones en la Isla.
