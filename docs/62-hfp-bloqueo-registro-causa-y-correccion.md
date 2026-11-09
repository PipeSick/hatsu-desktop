# 62 — El experimento HFP que no terminaba en 60 s: causa, correcciones y qué falta

Etiquetas: **OBSERVADO** (pruebas reales del usuario, Beta 2.0.3.0, Windows 11 compilación 26300.0, iPhone emparejado) · **CÓDIGO** (revisión del código de 2.0.3.0) · **HIPÓTESIS** · **DOC/REPORTES** (documentación o informes de terceros ya citados en docs/61).

## 1. Lo observado
1. «Experimento: registrar y conectar HFP» → «no terminó en 60 s (se canceló)». El host cerró el proceso a los 60 s.
2. «Deshacer el registro» → «Transporte #544da4: esta app no estaba registrada».
Eso es todo lo que se sabe. **No se sabe en qué etapa se detuvo** (2.0.3.0 solo escribía el informe al final).

## 2. Qué hacía el código de 2.0.3.0 (CÓDIGO)
Secuencia del experimento y del proveedor: `RequestAccessAsync` (async) → `IsRegistered()` (**síncrona**) → `RegisterApp()` (**síncrona**) → `await ConnectAsync()` (async, **sin tope**) → líneas.
- Ninguna etapa tenía tope propio. Un `RegisterApp()` síncrono que no vuelve no se puede interrumpir con un `CancellationToken`; un `ConnectAsync()` que no termina dejaba el `await` abierto para siempre. El único límite era el de 60 s **del host**, que mata el proceso: por eso el mensaje era genérico y no decía dónde.
- El `finally` con `UnregisterApp` del experimento no se ejecuta cuando el host mata el proceso: un registro conseguido a medias podía quedar (aquí la limpieza posterior dijo «no registrada»).
- El **proveedor del módulo** (el camino real de producto) tenía el mismo defecto: `await t.ConnectAsync()` sin límite dentro del bucle de arranque. Con un `ConnectAsync()` colgado, el proveedor se quedaba para siempre en «Iniciando…» (sin causa visible) y `DisposeAsync` esperaba al bucle: el módulo no se cerraba bien.
- Otros `await` de lectura sin límite: `FindAllAsync`, `RequestStoreAsync`, `PhoneLine.FromIdAsync`, `GetAllActivePhoneCallsAsync`, `GetPhoneCallInfoAsync`.
- Dos procesos podían tocar el transporte a la vez (el módulo con «Llamadas completas» activo y el proceso del experimento): nada lo impedía.
- El reintento de conexión (cada ≥45 s) y la reconciliación (30 s) llamaban a `ConnectAsync`/`IsRegistered` sin tope y desde el mismo bucle que el arranque.

## 3. Causa del timeout: lo que es evidencia y lo que es hipótesis
- **Evidencia:** el proceso no terminó en 60 s; ninguna etapa tenía tope; el código tiene al menos dos llamadas capaces de no volver (`RegisterApp`, `ConnectAsync`).
- **Hipótesis (no demostrable con lo que hay):** (a) `ConnectAsync()` no vuelve (Klangbruecke documenta que su resultado es inestable); (b) `RegisterApp()` se queda dentro o no tiene efecto (MyPhone issue 26 y un Q&A de Microsoft describen justo eso en Windows 11, incluida la compilación 26300, con la capacidad declarada y `RequestAccessAsync = Allowed`); (c) bloqueo por el otro proceso (módulo) — descartable solo si «Llamadas completas» estaba apagado. **La etapa exacta no puede determinarse** con los datos de 2.0.3.0; la nueva versión la registra.
- **«No estaba registrada»** es compatible con: `RegisterApp()` nunca completó, completó sin efecto, o Windows descarta el registro al morir el proceso. No prueba ninguna.

## 4. Correcciones (2.0.4.0)
| Problema | Corrección |
|---|---|
| Etapas sin tope | `TransportSequence`: acceso 10 s, `IsRegistered` 5 s, `RegisterApp` 15 s, `ConnectAsync` 15 s, presupuesto total 40 s (< 60 s del host); ninguna etapa excede lo que queda del presupuesto |
| Llamada síncrona imposible de cancelar | `StageWorker`: **un hilo dedicado** ejecuta, de una en una, las operaciones del transporte. Si una no vuelve, el trabajador queda **aislado** (no acepta más trabajo): nada más toca el transporte desde ese proceso. Una finalización tardía no ejecuta nada nuevo |
| Dejar un hilo colgado | Un hilo bloqueado en una llamada de WinRT no se puede matar: el único modo legítimo de liberarlo es **reiniciar el proceso del módulo**. Se anota el bloqueo (`hfp-wedge.json`: etapa, hora, contador), se intenta `UnregisterApp` con un trabajador nuevo (4 s) y el módulo se reinicia. El módulo nuevo **no toca el transporte** durante un enfriamiento creciente (5 min, 15 min, 1 h, 6 h) y lo dice: sin bucle de reinicios |
| Timeout genérico | Cada etapa informa resultado, milisegundos y **HRESULT** de Windows; el informe del experimento se reescribe tras **cada etapa**, y si el host tiene que cerrar el proceso muestra el informe **parcial** (última etapa alcanzada) |
| «Listo» sin confirmar | «Listo» exige `IsRegistered()` verdadero **después** de registrar y tras conectar; `RegisterApp()` sin efecto → «No disponible: RegisterApp no tuvo efecto»; acceso denegado y `UnauthorizedAccess` nombran la causa |
| Fallo de `ConnectAsync` | no invalida un registro verificado (las líneas pueden aparecer cuando el teléfono conecte); su bool no se evalúa |
| Duplicados | una secuencia a la vez (la segunda se rechaza); cerrojo de archivo entre el módulo y los experimentos (un Mutex tiene afinidad de hilo y aquí se cruzan `await`s) |
| Lecturas sin tope | `FindAllAsync`, `RequestStoreAsync`, `FromIdAsync`, llamadas activas e info de llamada, con tope de 3–10 s; la lectura periódica de `IsRegistered` y la reconexión también |
| Cierre | `DisposeAsync` espera como máximo 3 s al bucle y deshace el registro con tope; si el trabajador está aislado, usa uno nuevo |
| Estado invisible | Ajustes › Hatsu Connect muestra «Estado del control de llamadas: LISTO / NO DISPONIBLE — causa / apagado» (evento `connect.callstatus` del módulo al host, repetido cada 30 s) |

## 5. Comportamiento esperado ahora
1. Con «Llamadas completas desde el PC» activo, el módulo ejecuta la secuencia por etapas (≤ 40 s) y el log de Connect trae una línea por etapa con ms y HRESULT.
2. Si Windows niega o ignora el registro: **No disponible** con la causa exacta; sin botones; el módulo sigue (ANCS intacto) y reintenta cada 30 s.
3. Si Windows deja una llamada colgada: el módulo se reinicia una vez, queda en enfriamiento y lo dice en Ajustes.
4. Si el registro es válido: «Listo»; aparecen líneas Bluetooth cuando el teléfono conecta; acciones reales (contestar, rechazar, colgar, silenciar, pasar el audio al PC) solo con una llamada del sistema en esa línea.

## 6. Estado de las seis cosas pedidas
| Estado | Cómo se establece | Verificado |
|---|---|---|
| Acceso al transporte permitido | `RequestAccessAsync = Allowed` | OBSERVADO en 2.0.2.0 |
| Aplicación registrada | `IsRegistered() == true` tras `RegisterApp()` | **NO observado nunca** |
| Transporte conectado | `ConnectAsync` terminó (bool ignorado) | NO observado |
| Línea telefónica disponible | `PhoneLineWatcher` + `TransportDeviceId` del transporte registrado | NO observado |
| Llamada controlable | `PhoneCall` en esa línea | NO observado |
| Audio bidireccional | `ChangeAudioDevice(LocalDevice)` + la persona lo confirma | NO observado |

## 7. Límite de Windows 11 compilación 26300
Un Q&A de Microsoft sobre la 26300 y el issue 26 de MyPhone describen `RegisterApp()` sin efecto (`IsRegistered` queda en falso) con la capacidad declarada. Si eso es lo que ocurre en este equipo, **no existe corrección legítima desde Hatsu** (se descartan el `BypassRegistration` de HKLM, las claves comunitarias de Limited Access Feature y cualquier API privada). En ese caso el módulo queda estable: informa «RegisterApp no tuvo efecto» y no ofrece botones. La versión 2.0.4.0 es la que dirá si es ese el caso.

## 8. Qué falta para una llamada real
1. Que `IsRegistered()` sea verdadero tras `RegisterApp()` en el equipo del usuario (Ajustes lo muestra).
2. Que aparezca una línea Bluetooth con el iPhone conectado por HFP.
3. Una llamada entrante: contestar desde Notch, oír y hablar por el PC, colgar.

## 9. Paquete candidato 2.0.4.0 y CI (OBSERVADO: run 42143938006, commit `4192378`, pin NotchWin `e98863c`; los 4 jobs en success; log del paso MSIX leído, sin FALLO)
| Paquete | Archivo | SHA-256 | Artefacto |
|---|---|---|---|
| **Candidato con `phoneLineTransportManagement`** | `HatsuDesktop-2.0.4.0-beta-callcontrol-unsigned.msix` (172 MB) | `400f665ad4cde592e5f0cb61bdf086bbee7501033d3c1a862b68e8013fbbca74` | `msix-llamadas-sin-firmar-41923787f388ad0cd684afedb8075d0ad799aad7` |
| Equivalente a Stable (sin las capacidades de telefonía) | `HatsuDesktop-2.0.4.0-store-unsigned.msix` | `a5a43a227ef6af6d0a738f3dd0c16c9b5788c54296d870a6b269d17db6c834e3` | `msix-store-sin-firmar-41923787f388ad0cd684afedb8075d0ad799aad7` |
Capacidades del candidato (leídas del manifiesto del `.msix`): `runFullTrust`, `phoneLineTransportManagement`, `phoneCall`, `userNotificationListener`, `bluetooth`, `location`, `webcam`. Identidad `PipeSick.HatsuDesktop` · `CN=ACC0FCB8-4D92-4E20-9731-7254D5E43289` · v2.0.4.0 · x64. El paquete instalado en el runner conserva la PFN `PipeSick.HatsuDesktop_83tr6qzbbarj8`. Los artefactos caducan a los 14 días.

## 10. Resultado real de 2.0.4.0 (OBSERVADO, 2026-11-06 10:44, Windows 11 26300.0, iPhone emparejado) y siguiente paso
```
acceso (RequestAccessAsync)            Ok en 14 ms
¿registrado antes? (IsRegistered)      Ok en 0 ms   (falso)
registro (RegisterApp)                 Ok en 24 ms  (sin excepción)
verificación (IsRegistered)            Ok en 0 ms   → FALSO
Resultado: RegisterApp no tuvo efecto
```
- **No hubo bloqueo.** Las etapas funcionan y terminan en milisegundos. Esto **cambia** la hipótesis de §3: lo que colgaba en 2.0.3.0 era, con alta probabilidad, `ConnectAsync()` llamado sobre un transporte **no registrado** (2.0.3.0 lo llamaba aunque el registro no existiera); 2.0.4.0 ya no lo llama sin registro verificado. (Sigue siendo inferencia: no se observó el cuelgue.)
- **El hecho real es otro:** Windows acepta `RegisterApp()` y no registra. Es el síntoma que describen el issue 26 de MyPhone y el Q&A de Microsoft para la compilación 26300 («RegisterApp sin efecto, IsRegistered queda en falso, con la capacidad declarada y `RequestAccessAsync = Allowed`»). El paquete lleva `phoneLineTransportManagement` aprobado por la Store: la capacidad **no** es lo que falta.
- **Qué se agregó (2.0.5.0):** si `RegisterApp` vuelve sin efecto, se prueba **una vez** el camino público alternativo `RegisterAppForUser` (la documentación dice que el registro es por usuario) y se vuelve a verificar con `IsRegistered()`; el módulo no insiste cada 30 s (reintenta cada 10 min); el mensaje de estado explica las causas posibles y qué hacer.
- **Qué NO se hace (ilegítimo):** `BypassRegistration` en HKLM, claves comunitarias de Limited Access Feature, APIs privadas.

### Causas posibles de «sin efecto» y qué las distingue
| Causa | Evidencia | Qué lo descarta/confirma |
|---|---|---|
| Otra app tiene el rol HFP de ese iPhone (Enlace Móvil/Phone Link, Intel Unison…) | my-phone README e issue 26: el rol es de una app por teléfono; el síntoma típico es `DeniedBySystem`, a veces sin efecto | cerrar/desinstalar esa app y quitar el iPhone de ella; si el registro funciona después, era eso |
| Windows 11 26300 ignora el registro de apps de terceros | Q&A de Microsoft y issue 26 | no hay prueba desde Hatsu; solo Microsoft puede confirmarlo |
| La API exige un acceso limitado (Limited Access Feature) en esta compilación | hilos contradictorios (un comentarista dice que Microsoft la sacó de la lista; Klangbruecke no la necesita en Windows 10) | **solo Microsoft** puede conceder una LAF; no se usa ninguna clave comunitaria |

### Camino legítimo con Microsoft (propietario)
Abrir un caso de soporte del desarrollador en Partner Center (o publicar en Microsoft Q&A con la etiqueta de Windows App SDK/Bluetooth) con estos datos: app `PipeSick.HatsuDesktop` 2.0.5.0 distribuida por la Store con `phoneLineTransportManagement` aprobada; Windows 11 compilación 26300.0; `PhoneLineTransportDevice.RequestAccessAsync` = Allowed; `IsRegistered()` falso antes; `RegisterApp()` vuelve sin excepción en ≈24 ms; `IsRegistered()` sigue falso; `RegisterAppForUser` igual (según 2.0.5.0). Preguntar: (1) ¿requiere esta API una Limited Access Feature en esta compilación y cómo se solicita? (2) ¿hay un cambio de comportamiento documentado de `RegisterApp` para apps de terceros? (3) ¿el rol HFP del iPhone lo retiene Enlace Móvil?

## 11. Pista nueva: Enlace Móvil bloqueado por el «administrador del sistema» (OBSERVADO en una captura del usuario, 2026-11-06)
Enlace Móvil muestra «El administrador del sistema ha bloqueado esta característica». Según fuentes públicas (documentación de Microsoft sobre la directiva y artículos como [winhelponline](https://www.winhelponline.com/blog/phone-link-blocked-administrator/)) ese mensaje aparece cuando la directiva **«Vinculación teléfono-PC en este dispositivo»** está en «No vincular»: valor `EnableMmx = 0` en `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`. El usuario cree haberlo deshabilitado con un programa de optimización de Windows.
- **Lo que no se sabe:** si esa directiva (o servicios deshabilitados por el mismo programa) es la razón por la que `RegisterApp()` no tiene efecto. Hay un reporte público de una herramienta de privacidad que rompió las llamadas de Phone Link ([privacy.sexy #350](https://www.github.com/undergroundwires/privacy.sexy/issues/350)), lo que hace plausible la hipótesis, pero **no está demostrado** para la API de transporte telefónico.
- **Qué hace Hatsu (2.0.5.0):** cuando el registro no tiene efecto, el estado de Ajustes añade, si los encuentra, estos cambios del sistema (lectura del registro, sin escribir nada): `EnableMmx = 0` y servicios deshabilitados (`PhoneSvc`, `CDPSvc`, `BTAGService`, `bthserv`). Hatsu **nunca** modifica esos valores.
- **Comprobación de solo lectura (PowerShell):** `reg query "HKLM\SOFTWARE\Policies\Microsoft\Windows\System" /v EnableMmx` y `Get-Service PhoneSvc,CDPSvc,BTAGService,bthserv | Select Name,Status,StartType`.
- **Si salen bloqueados** y el usuario decide deshacerlo (es su sistema y su decisión): eliminar el valor `EnableMmx` (o ponerlo en 1) con un PowerShell de administrador, poner los servicios en Manual/Automático, reiniciar y comprobar que Enlace Móvil ya no muestra el bloqueo; después, que «Estado del control de llamadas» de Hatsu cambie.

## 12. Lecturas del usuario y repetición del experimento (OBSERVADO, 2026-11-06)
`EnableMmx = 1`; servicios `PhoneSvc`, `CDPSvc`, `BTAGService`, `bthserv` en ejecución; `System`: `EnableActivityFeed=0`, `PublishUserActivities=0`, `UploadUserActivities=0`, `AllowClipboardHistory=0`, `AllowCrossDeviceClipboard=0`; `CloudContent`: `DisableWindowsConsumerFeatures = 1` (según [winhelponline](https://www.winhelponline.com/blog/phone-link-blocked-administrator/), causa del bloqueo de Enlace Móvil); `Messaging`: `AllowMessageSync = 0`. El experimento 2.0.4.0 repetido a las 11:03: acceso Ok (12 ms), `RegisterApp` Ok (17 ms), `IsRegistered()` **falso**. Mismo resultado que a las 10:44. Prueba reversible (solo si se quiere): `DisableWindowsConsumerFeatures` a 0 + reinicio (`reg export` antes).

## 13. Hipótesis más fuerte hasta ahora: Limited Access Feature `phonelinetransportdevice_v1`
- Un proyecto que hoy sí registra el rol en Windows 11 ([surya-sk/my-phone](https://github.com/surya-sk/my-phone), sin licencia, compilación de 2026-10-12) **desbloquea la función de acceso limitado `phonelinetransportdevice_v1`** con un token «comunitario» derivado del nombre de familia del paquete; Klangbruecke documenta que en Windows 11 el registro se deniega y en Windows 10 no hace falta. Un comentario del issue 26 de MyPhone dice que Microsoft la sacó de la lista; el proyecto de 2026 sigue necesitándola: **contradicción sin resolver**.
- Si la función está bloqueada, `RegisterApp` sin efecto y `IsRegistered()` en falso es exactamente lo observado (con capacidad declarada y `RequestAccessAsync = Allowed`). **Es hipótesis, no prueba.**
- **Camino legítimo:** el token de una LAF lo concede Microsoft. Hatsu **no** usará la clave comunitaria (es un token derivado de forma no oficial: eludiría la restricción). Solicitar a Microsoft el acceso a `phonelinetransportdevice_v1` para `PipeSick.HatsuDesktop` (caso de soporte del desarrollador en Partner Center / Q&A) con los datos de la sección 10 de este documento.
- **Qué agrega 2.0.5.0:** cuando el registro no tiene efecto, el estado de Ajustes incluye el resultado de **consultar** esa función con token vacío (no desbloquea nada): dato exacto para el caso de soporte.
- **Si Microsoft concede un token**, Hatsu llamará a `LimitedAccessFeatures.TryUnlockFeature(id, token, atestación)` antes de `RegisterApp`; el token se guardará como un secreto de compilación, no en el repositorio.
