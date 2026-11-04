# Investigación independiente — contestar llamadas del iPhone desde Windows (WinMac Connect)

Fecha: 2026-11-03 · Rama `research/iphone-calls` (worktree aparte, sobre `28f8aee`) · **No se modificó ningún archivo de WinMac ni de NotchWin.**

## 0. Alcance y límites de esta investigación (leer primero)

- **No tuve acceso a tu hardware.** Nada de este informe lo verifiqué yo en hardware. Lo «verificado en hardware» viene de terceros o de **tus dos registros originales de `ConnectProbe`** (Apéndice C), que sí son evidencia de hardware real de tu PC.
- **Lo que sí verifiqué yo, con código y comandos reproducibles:** (a) los metadatos oficiales del SDK de Windows (paquete NuGet `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 y 10.0.26100.57), (b) el código fuente de MyPhone, Klangbruecke y Sefirah clonados con `git`, (c) los documentos 39, 40 y 41 y el código de WinMac Connect de la rama `claude/relaxed-turing-cijzko`, (d) que la prueba de concepto compila y que su lógica pasa 19 pruebas.
- **Dominios bloqueados en esta sesión:** `learn.microsoft.com`, `inthehand.com`, `microsoft.github.io`, `kiwix.lema.org`, `api.github.com`. Las páginas de Microsoft Q&A y de documentación de Microsoft las conozco **solo por los resultados de búsqueda** (resumen, no texto íntegro). Si quieres que las lea enteras, hay que permitir esos dominios en la red de la sesión.
- La especificación HFP del Bluetooth SIG no se pudo consultar; lo que digo de comandos AT y SCO sale de documentación pública de Microsoft, del código de MyPhone y de Klangbruecke, y de conocimiento general (marcado como tal).

## 1. Resumen ejecutivo

**Veredicto: PROMETEDOR, con una condición que puede hundirlo.** La conclusión anterior («bloqueado por Windows») **no está demostrada para tu PC** y se generalizó más de lo que la evidencia permite; pero **sí hay evidencia de terceros** de que Windows 11 niega el registro de telefonía a apps de **carga lateral** (§3.1). Lo que ninguna fuente aclara es si una app **firmada y distribuida de otra forma** (Microsoft Store) sí lo consigue en Windows 11. Eso decide el resultado, y solo se puede aclarar con pruebas en tu hardware (§7, §8).

| Capacidad | Estado | Por qué |
|---|---|---|
| **Control de llamadas** (contestar, rechazar, colgar) | **PROMETEDOR** | El SDK de Windows 11 (10.0.26100) trae la clase pública `PhoneCall` con `AcceptIncomingAsync`, `RejectIncomingAsync`, `EndAsync`, `HoldAsync`, `MuteAsync` y `PhoneCallInfo` (contacto, número, dirección). **No existe en el SDK 19041.** Sefirah (GPL, activo en oct. 2026) los usa. Condición: que Windows registre a la app como proveedor de llamadas. |
| **Audio entrante** (iPhone → audífonos del PC) | **PROMETEDOR** | No hace falta controlador propio: Windows es dueño de la ruta SCO y expone `PhoneCall.ChangeAudioDeviceAsync(LocalDevice)` y `PhoneLineTransportDevice.AudioRoutingStatus`. Klangbruecke documenta una llamada real con audio en los dos sentidos (Windows 10 + Android). Con iPhone: sin evidencia de terceros para apps abiertas. |
| **Audio saliente** (micrófono del PC → iPhone) | **PROMETEDOR** | Windows codifica el micrófono predeterminado de *comunicaciones* sobre SCO (Klangbruecke FINDINGS §11). Mismo límite: no hay evidencia con iPhone. |
| **Integración completa** | **PROMETEDOR, no VIABLE** | Ninguna fuente demuestra *iPhone + Windows 11 + app de terceros abierta* de punta a punta. Los informes sobre Windows 11 son contradictorios: apps de carga lateral reciben `DeniedBySystem` (MyPhone #26, Klangbruecke §15.1, Microsoft Q&A), mientras que Sefirah (distribuida por la Store) y Phone Link sí tienen llamadas en Windows 11. **Factor decisivo sin demostrar: carga lateral frente a distribución por la Store.** |

**Si la prueba de §8 muestra que Windows 11 solo concede el registro a paquetes de la Store**, el resultado pasa a depender de un proceso externo (aprobación de la capacidad restringida por Microsoft) y habría que decidir si vale la pena. Sin ese permiso, lo que queda es **PARCIAL**: detección y, si se valida, rechazo por ANCS en el iPhone (sin audio en el PC).

Lo que **sí está bloqueado** por tus restricciones: forjar tokens de Acceso Limitado, escribir `BypassRegistration` en HKLM, controladores propios, WinUSB/BTstack sobre tu radio, y automatizar Phone Link (§5.2).

## 2. Auditoría de `DeniedBySystem`

**Conclusión de la auditoría (actualizada con los registros originales que enviaste, Apéndice C): el `DeniedBySystem` es real y está registrado en hardware, pero se obtuvo SIN identidad de paquete y SIN la capacidad, y la propia herramienta lo declara «lo esperable». Es un resultado verdadero y no concluyente: no prueba nada sobre una app empaquetada, ni sobre contestar, ni sobre audio.**

| Pregunta | Respuesta | Evidencia |
|---|---|---|
| 1. ¿Existe el código de la prueba? | **No en ningún repositorio.** `docs/39` dice que `ConnectProbe` quedó «fuera de los repositorios» y que su código iba «junto en el ZIP». Ese ZIP no está en el repo ni en este entorno. | `docs/39:4`, `:70`; búsqueda en el árbol y en `git log --all -S ConnectProbe`: solo aparece en documentos. |
| 2. ¿Existe un registro original? | **Sí, ahora.** Me enviaste `informe.txt` (comando `all`, 2026-11-03 17:14:11) e `informe2.txt` (`round2`, 17:20:20). En el repositorio y en esta máquina no estaban: solo había la fila resumen de `docs/39:99`. | Apéndice C. |
| 3. ¿En qué equipo? | Tu PC, **Windows 10.0.26300.0** (cabecera de ambos registros), iPhone «de PipeSick» con HFP AG, MAP y PBAP. | Cabecera de `informe.txt` e `informe2.txt`. **Mi sospecha anterior de que el «26300» se hubiera copiado de un reporte ajeno queda descartada:** es la compilación real de tu PC. (Es posterior a 26100/26200; probablemente una compilación de Insider, no verificado.) |
| 4. ¿Local, runner o terceros? | Local, en tu PC (un runner de GitHub no puede emparejar un iPhone). Los *dos informes de terceros* (Q&A) son aparte. | `docs/39:25` (terceros) y `:91` (local). |
| 5. ¿Versión de Windows? | «26300» (sin build menor ni versión). | ídem |
| 6. ¿Identidad de paquete? | **No. El propio registro lo dice:** «Sin identidad de paquete y sin la capacidad «phoneLineTransportManagement» lo esperable es DeniedBySystem». | `informe.txt` línea 84; `docs/39:70` (consola autocontenida). |
| 7. ¿Capacidades declaradas? | **Ninguna posible sin paquete.** Una capacidad restringida solo se declara en un manifiesto de paquete. | Klangbruecke FINDINGS §2 y §12. |
| 8. ¿Qué método devolvió `DeniedBySystem`? | El registro no nombra el método; imprime «acceso DeniedBySystem · registrado: False». Corresponde a `RequestAccessAsync()` (estado de acceso) e `IsRegistered()`. Inferencia, sin código que la confirme. | `informe.txt` línea 83. |
| 9. ¿Se puede reproducir? | Con `CallProbe stage2` (esta carpeta), **sin** paquete y **con** paquete. | §7 |

Hallazgos adicionales de la auditoría:

1. **El resultado se esperaba antes de medirse y la herramienta lo etiqueta como esperable.** La tabla de pruebas decía «Telefonía: probablemente `DeniedBySystem`; `Allowed` sería una sorpresa positiva» (`docs/39:85`) y el registro repite la expectativa (`informe.txt` línea 84). El resultado no está falseado (el registro lo respalda), pero se diseñó como una comprobación de línea base, no como una prueba de la vía empaquetada.
2. **Un `DeniedBySystem` sin paquete es el resultado *esperado* y no prueba un bloqueo.** MyPhone issue #7 (mantenedor, 18-oct-2021): `PhoneLineTransportDevice` «solo se puede llamar desde una app empaquetada en MSIX»; una consola Win32 falla con ese mismo error. Hay un reporte de Microsoft Q&A de WPF con el mismo síntoma (`1341397`, solo por resultado de búsqueda).
3. **Detectar ≠ contestar ≠ audio.** `docs/41` (WM-039/040) pasó de «`DeniedBySystem` en una consola» a «contestar está bloqueado». Eso mezcla tres cosas distintas, como pediste evitar.
4. **Errores factuales en `docs/39` y `docs/41`** (corregidos en el Apéndice A): atribuyen a MyPhone «no logró el audio» cuando eso describe su *propia pila HFP* experimental, no la vía de `PhoneLineTransportDevice`; y afirman «WinMac controla las llamadas con API pública: **No**», cuando `PhoneCall` existe en el SDK 26100.
5. **Hay evidencia de terceros de denegaciones reales en Windows 11, también con paquete** (§3.1): MyPhone #26 (UWP, 22H2+), Microsoft Q&A 1803696/1663702, y Klangbruecke FINDINGS §15.1 («en Win11 la app no puede registrar el transporte: `DeniedBySystem` para apps de carga lateral», citando a MyPhone #26 y a la prueba de un amigo en la compilación 26200, sin registro en su repositorio). Por eso la conclusión previa es **plausible, pero no demostrada para tu PC**, y su alcance («todas las vías bloqueadas») es excesivo: no distingue carga lateral de Store, ni detección de contestar, ni control de audio.

## 3. Análisis técnico por vía

### 3.1 A. `PhoneLineTransportDevice` + `PhoneCall` (vía nativa de Windows) — PROMETEDOR

**Qué ofrece la API pública** (VERIFICADO EN CÓDIGO: `evidence/sdk-10.0.26100.57-Windows.ApplicationModel.Calls.txt`, generado con `tools/ListWinRtApi`):

- `PhoneLineTransportDevice`: `GetDeviceSelector(PhoneLineTransport.Bluetooth)`, `FromId`, `RequestAccessAsync`, `RegisterApp`, `IsRegistered`, `ConnectAsync`, `UnregisterApp`, `AudioRoutingStatus` (`CanRouteToLocalDevice` / `CannotRouteToLocalDevice`), `InBandRingingEnabled`.
- `PhoneLine`: `DialWithResultAsync`, `GetAllActivePhoneCallsAsync`.
- `PhoneCall` (**solo en 26100, no en 19041**): `GetFromId`, `AcceptIncomingAsync`, `RejectIncomingAsync`, `EndAsync`, `HoldAsync`, `ResumeFromHoldAsync`, `MuteAsync`, `UnmuteAsync`, `SendDtmfKeyAsync`, `ChangeAudioDeviceAsync(Unknown | LocalDevice | RemoteDevice)`, eventos `StatusChanged`, `AudioDeviceChanged`, `IsMutedChanged`.
- `PhoneCallInfo`: `CallDirection`, `DisplayName`, `PhoneNumber`, `StartTime`, `IsHoldSupported`.

**Quién lo usa realmente:**

- **Sefirah** (GPL-3.0, commit `6db524a`, 2026-10-17): `Platforms/Windows/Utilities/CallingFeatureUtils.cs:19-22` («In-call control… requires CallsPhoneContract v6 (build 20348+ / Windows 11)»), `Calling/PhoneCall.cs:94-104` (aceptar, rechazar, terminar, cambiar audio), `Calling/PhoneLineService.cs:218` (`ChangeAudioDeviceAsync(LocalDevice)` tras marcar) y `:283-302` (flujo `RequestAccessAsync` → `RegisterApp` → `IsRegistered` → `ConnectAsync`). Su manifiesto declara `phoneCall` (l.137), `phoneLineTransportManagement` (l.143), `bluetooth` y `microphone`, más `uap13:windows.phoneCallActivation` (l.82). Su README enlaza a la Microsoft Store (`apps.microsoft.com/detail/9PJV6D1JPG0H`). Es el único proyecto abierto revisado que usa `PhoneCall` en Windows 11.
- **MyPhone** (MIT, commit `dbd0ede`): `Device/DeviceCallServiceProvider.cs:35` (`RequestAccessAsync`), `:48` (`RegisterApp`), `:52` (`ConnectAsync`), `:81` (`Dial`). **No implementa contestar ni colgar** (búsqueda de `Answer|Reject|EndCall|HangUp` sin resultados). Su README lo presenta como el mismo flujo de Phone Link.
- **Klangbruecke** (sin licencia, commit `2cb8e00`, 2026-09-29): README líneas 13-23 y 106-120. **Importante, y distinto de lo que resumí al principio:** su `FINDINGS.md` §15.1 (l.706-751) y §16 (l.753-775) dicen que (a) en **Windows 10** las llamadas funcionan pero `PhoneCall` **no existe** (midieron con `ApiInformation`: `CallsPhoneContract` solo hasta v5; coincide con mi lectura de los metadatos del SDK 19041), (b) el control de la llamada en Win10 lo pone **la propia interfaz de Windows** (ventana de aceptar/rechazar y ventana de llamada), no la app, y (c) en **Windows 11** la app «no puede registrar el transporte en absoluto: `DeniedBySystem` para apps de carga lateral» (l.741-744), con lo que concluyen: «Win10 tiene llamadas pero no API de enrutado; Win11 tiene la API pero no llamadas». Su reapertura posible: «un paquete firmado por la Store que supere la denegación de Win11 (sin verificar)» (l.748-750).

**Evidencia en contra o de riesgo (REPORTADA POR TERCEROS, solo por resumen de búsqueda salvo MyPhone #26):**

- MyPhone issue #26 (abierto desde 2023-03): en Windows 11 22H2 `RequestAccessAsync` devuelve siempre `DeniedBySystem` a apps UWP; otros usuarios lo confirman en sep. 2024; el autor encontró `TryUnlockPhoneLineTransportDeviceAPIs` en `YourPhone.Calling.Managed.dll` de Phone Link y concluyó «Microsoft bloquea esta API a propósito». En oct. 2024 un comentarista relayó que Microsoft dijo que la API «fue retirada de la lista de acceso limitado y no necesita token» (**no es una cita directa de Microsoft**; el propio comentarista dice luego que seguía bloqueada). En abr. 2026 otro usuario reporta éxito parcial imitando a Phone Link y avisa: si otra app ya registró el dispositivo, `RequestAccess` devuelve `DeniedBySystem`.
- Microsoft Q&A 6041632: app de escritorio **empaquetada** en Windows 11 **26300**: `RequestAccessAsync` = `Allowed`, `RegisterApp` no falla pero `IsRegistered` queda en `false`; `RegisterAppForUser` da `E_ACCESSDENIED`; quitar Phone Link y reemparejar no cambia nada. La única respuesta es de IA, sin solución.
- Microsoft Q&A 1803696 y 1663702: funciona en Windows 10 22H2 y falla en Windows 11 22H2/23H2 con `DeniedBySystem`.
- Si el reporte 6041632 es el mismo caso que el tuyo, **el obstáculo real podría ser la compilación 26300 (una compilación de Insider)**, no una política contra apps de terceros. Es una hipótesis que la prueba 2 y la 3a pueden separar.

**Respuesta a «¿existe alguna configuración legítima?»:** sí, la que usan Sefirah (Store) y Klangbruecke/MyPhone (MSIX en carga lateral): paquete con identidad, `phoneCall` + `phoneLineTransportManagement` + `runFullTrust` y `bluetooth`, y llamar a `RequestAccessAsync` **antes** de `RegisterApp` (Klangbruecke FINDINGS §12: sin eso `RegisterApp` lanza `UnauthorizedAccessException`). Declarar la capacidad no basta por sí solo: Windows decide en tiempo de ejecución.

**Hipótesis nueva que ordena la evidencia (NO demostrada):** lo que Windows 11 evalúa en `RequestAccessAsync` podría depender de **cómo está firmado y distribuido el paquete** (Store frente a carga lateral/desarrollo) y/o de la compilación. Encaja con: Sefirah, Phone Link y Thy Phone (Store) con llamadas en Windows 11, frente a MyPhone, Klangbruecke y los reportes de Q&A (carga lateral) denegados. También encaja con el reporte de Q&A 6041632 (app empaquetada, 26300: acceso `Allowed` pero registro sin efecto). Una consecuencia práctica: **la prueba local por carga lateral solo puede demostrar «denegado en carga lateral»; no puede demostrar «imposible» para una app de la Store.**

**Distribución:** la capacidad es *restringida*. Para la Microsoft Store requiere aprobación; en carga lateral se puede declarar sin ella (documentación de capacidades de Microsoft, citada en `docs/39:24`). Un proyecto abierto distribuible necesitaría un paquete firmado (SignPath Foundation, que `docs/41` ya menciona, o la Store). Un MSIX autofirmado exige confiar un certificado en el equipo, que es justo lo que quieres evitar en el usuario final.

### 3.2 B. HFP propio por RFCOMM — PARCIAL (control sí, audio no)

- **MyPhone.Demo.HFP** (MIT, `MyPhone.Demo/MyPhone.Demo.HFP/HFP.cs`, 438 líneas) implementa el *Service Level Connection* completo de HFP 1.7 como Hands-Free desde una consola Win32 con `Windows.Devices.Bluetooth.Rfcomm`: `AT+BRSF`, `AT+BAC`, `AT+CIND`, `AT+CMER`, `AT+CHLD=?`, `AT+BIND`. Su README (l.47-49): «hice llamadas por el SLC, pero **fallé al establecer SCO y transferir audio**». **Esta es la fuente de «no logró el audio» que `docs/39` atribuyó por error a toda la vía de telefonía.** (REPORTADO POR TERCEROS, con código; no ejecutado por mí.)
- **SCO no es accesible desde modo usuario.** La documentación de Microsoft del *Bluetooth driver stack* dice que el audio SCO pasa por un *profile driver* en modo kernel incluido con Windows (8+), y que desde modo usuario se publican, según el resumen de esa página (visto por búsqueda), Winsock/RFCOMM y las APIs de `BthProps`. MyPhone issue #1 (mantenedor): «con el controlador de Microsoft por defecto no se puede; necesitas controladores de terceros o un controlador de perfil en modo usuario». El issue #27 repite el síntoma («solo hace el SLC… para el audio hace falta SCO… no hay documentación de Microsoft»). (DOCUMENTADO + REPORTADO.)
- Un HFP propio **además** puede chocar con el servicio de Windows, que tiene su propia configuración de Hands-Free (clave `HKLM\…\Bluetooth\Audio\Hfp\HandsFree`, `Enabled=1`, `RfcommServerChannel=2`; Klangbruecke FINDINGS §7). Que choque en la práctica es una inferencia mía, no una medición.

**Respuestas a tus 8 preguntas sobre HFP:**

1. *¿Contestar sin `PhoneLineTransportDevice`?* Control por AT (`ATA`, `AT+CHUP`) **sí** es posible desde modo usuario (MyPhone.Demo) **si Windows no ocupa el canal**; el audio **no**. → BLOQUEADO para integración completa por vía propia.
2. *¿Windows permite abrir RFCOMM HFP directamente?* Sí, con `RfcommDeviceService`; MyPhone.Demo lo hace. (REPORTADO POR TERCEROS.)
3. *¿Y si el servicio HFP de Windows ya tiene la conexión?* **No hay evidencia.** HFP normalmente admite una sola conexión HF por teléfono; es una de las preguntas abiertas de §8.
4. *¿Reutilizar la conexión existente?* No hay API pública para eso; por eso `PhoneLineTransportDevice` es la forma soportada.
5. *¿Controlar sin interferir con el servicio del sistema?* Solo por la API de Windows (vía A).
6. *¿API pública o accesible desde usuario?* **Sí: la vía A** (`PhoneCall`).
7. *¿Límites de SCO/eSCO?* Kernel; Windows negocia códec y enruta (Klangbruecke: pasa por `BthHFEnum`/`BthPort`, sin endpoint intermedio visible).
8. *¿Usar los dispositivos de audio Bluetooth que Windows ya expone?* En una llamada no aparece un endpoint «Hands-Free» intermedio: Windows toma el micrófono y el altavoz **de comunicaciones** predeterminados y los pasa por SCO (Klangbruecke FINDINGS §11, l.424-431).

### 3.3 C. Audio bidireccional — PROMETEDOR (sin controlador propio)

- **Quién es dueño del audio:** Windows. Klangbruecke FINDINGS l.424-431: el PC es Hands-Free, el teléfono es Audio Gateway; durante la llamada los puntos «Hands-Free HF Audio» no aparecen en `MMDeviceEnumerator`, y Windows codifica el micrófono de comunicaciones predeterminado directamente sobre SCO. «La app no puede afectar esto: Windows es dueño de toda la ruta de audio».
- **Límite documentado por Klangbruecke (§15, l.679-699, Android Pixel 9, Win10):** la llamada llega al PC solo cuando **se toma en el PC** (ventana de llamada de Windows o seleccionando el PC como dispositivo); si se contesta en el teléfono, **el audio se queda en el teléfono**. Para tu flujo («Contestar» desde WinMac) eso es coherente: contestar desde el PC debería llevar el audio al PC. Con iPhone no hay evidencia.
- **Cómo se pide el audio local:** `PhoneCall.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice)` (SDK 26100; Sefirah `PhoneLineService.cs:218`).
- **Cómo saber si se puede:** `PhoneLineTransportDevice.AudioRoutingStatus` = `CanRouteToLocalDevice`.
- **Lección práctica de Klangbruecke (§11):** el interlocutor oía mal porque el micrófono *de comunicaciones* estaba al 7,8 % (−38 dB); no era el códec. Para WinMac: validar el nivel del micrófono de comunicaciones antes de dar la prueba por mala.
- **Detalle de hardware:** que el SCO sea de banda ancha (mSBC) o estrecha (CVSD) depende del controlador del radio (hipótesis descartada en ese caso, pero real en general).
- **Auriculares USB/Realtek/Bluetooth:** Windows usa los dispositivos de *comunicaciones* que elijas. Sin pruebas propias.
- **Lo que no está comprobado:** que esto funcione con un **iPhone** como AG. Phone Link para iOS y Thy Phone (de pago, cerrado) anuncian llamadas con audio en el PC con iPhone (REPORTADO POR TERCEROS, páginas de prensa/fabricante vistas solo por búsqueda). Para una app abierta: sin evidencia.

### 3.4 D. Implementaciones existentes

| Proyecto | Licencia | Última actividad | Qué funciona de verdad | Incompleto / límites | Drivers | ¿MSIX? | APIs restringidas | Evidencia reproducible con audio |
|---|---|---|---|---|---|---|---|---|
| **Klangbruecke** (MYSTRAVIL) | **Sin licencia** (no copiable) | 2026-09-29 | Llamadas con audio en ambos sentidos + música A2DP, reconexión (según su README y `FINDINGS.md`) | Windows 10 + **Android** (Pixel 9). **En Windows 11: denegado (carga lateral).** DTMF no funciona (§16); sin ringback saliente; audio solo si la llamada se toma en el PC (§15) | No | **Sí** (carga lateral) | `phoneLineTransportManagement` | Registros del propio autor (FINDINGS §10, §12). **No verificada por mí.** El archivo tiene secciones superpuestas y se corrige a sí mismo (ver nota) |
| **Sefirah** (shrimqy) | **GPL-3.0** (solo referencia) | 2026-10-17 | «Ver y contestar llamadas de tu Android en Windows»; control completo vía `PhoneCall` (según su código y README) | Requiere su app Android para el emparejamiento/otras funciones. Windows 11 (contrato v6) | No | **Sí** (README enlaza a la Store) | `phoneLineTransportManagement`, `phoneCall`; en Windows <22000 genera tokens de Acceso Limitado | Código. Sin vídeo/registro de audio |
| **MyPhone** (BestOwl) | **MIT** | Código 2022-12-23; issues hasta 2026-04 | `RegisterApp`+`ConnectAsync`+`Dial`; SLC HFP propio (demo) | **No contesta ni cuelga**; Win11 22H2+ roto (#26); audio solo vía Windows | No (lo intentó y lo descartó) | Sí | `phoneLineTransportManagement` | No hay |
| **Thy Phone** (InTheHand) | Propietario, Store (~3,69 $) | n/d | Llamadas Bluetooth, incl. iPhone, con audio al PC | Cerrado; Windows 11 22H2 quitó APIs de su diálogo (según su blog, visto por búsqueda) | No | Sí (Store) | n/d | Reportes de usuarios |
| **Phone Link** | Propietario | Activo | Llamadas iPhone/Android con audio en el PC | **Excluido por tu restricción** | — | Sistema | Interna | — |
| **Intel Unison** | Propietario | n/d | Mencionada como funcionando en 22H2 por usuarios de MyPhone #26 | Cerrado | — | — | — | — |
| **saymonsh/BluetoothHFP** | No indicada | n/d | A2DP sí; llamadas denegadas (iPhone 12 Pro Max / iOS 17) | Sin control de llamadas | No | MSIX modo desarrollador | — | Reporte propio (según `docs/39`; no reverificado) |
| **BlueFerry / tether** (Linux) | GPL-2+ / MIT | 2026 | SMS, notificaciones, llamadas HFP (vista previa) con iPhone | Linux (BlueZ), otro modelo de audio | No | n/a | n/a | Según `docs/39`; no reverificado |
| **32feet.NET** | n/d | n/d | RFCOMM/SDP | **Sin audio SCO** (Klangbruecke §5) | — | — | — | — |

*Nota sobre `FINDINGS.md` de Klangbruecke:* es un diario con apéndices. §2 afirma que la API ya no está bajo Acceso Limitado; §12 (con encabezado «RESUELTO», pero cuyo cuerpo conserva diagnósticos previos) muestra que el estado de esa función sigue siendo `Unavailable` y que el registro **funciona igualmente** cuando se llama a `RequestAccessAsync` antes de `RegisterApp`. El estado final que afirman su README y §10 es «llamada real con audio en ambos sentidos, validada en hardware con el build empaquetado».

## 4. Rutas descartadas y por qué

| Vía | Decisión | Motivo |
|---|---|---|
| Generar el token de Acceso Limitado (Sefirah `FeatureTokenGenerator.cs`, algoritmo de withinrafael.com, clave de un gist) | **RECHAZADA** | La atestación incluye la frase «ha registrado su uso con Microsoft y acepta los términos»: sería falsificar una autorización. Sefirah **no la usa en Windows ≥ 22000** (`ShouldSkipLAFCheck`, `CallingFeatureUtils.cs:27`). |
| `HKLM\SYSTEM\CurrentControlSet\Control\Bluetooth\Audio\Hfp\HandsFree\BypassRegistration = 1` (sugerido en MyPhone #26, oct. 2024) | **RECHAZADA** | Modifica la configuración del sistema (administrador), un solo reporte (n=1) cuyo autor dice que no consiguió funciones. Klangbruecke lo confirma en `BTAGService.dll` pero lo declara innecesario. |
| Controlador propio / de perfil | **BLOQUEADA** por restricción del proyecto | Sin controladores de kernel; MSIX tampoco instala controladores. |
| Zadig/WinUSB + pila propia (BTstack) | **BLOQUEADA** | Quita el radio a Windows (rompe mandos y demás dispositivos; Klangbruecke §5). |
| Automatizar la interfaz de Phone Link | **BLOQUEADA** | Phone Link está excluido y la automatización de UI es frágil. |
| App complementaria en el iPhone | **FUERA DE ALCANCE** | Restricción del proyecto (y iOS no deja a ninguna app contestar llamadas). |

## 5. Matriz de alternativas

| Vía | Factibilidad | Restricciones | Evidencia | Riesgos | Compatibilidad | Dificultad | Siguiente prueba |
|---|---|---|---|---|---|---|---|
| **A. `PhoneLineTransportDevice` + `PhoneCall` en paquete** | **PROMETEDORA** | Identidad de paquete; capacidad restringida; Windows 11 con SDK 26100 para `PhoneCall` | Sefirah/Klangbruecke (Android); sin iPhone en abierto | Denegaciones en Win11 22H2+ y en 26300; dos apps no pueden registrarse a la vez (Phone Link) | Windows 11 (22000+; `PhoneCall` = contrato v6) | Media | Etapas 2, 3a, 3b |
| **B. HFP propio (RFCOMM) + audio de Windows** | **PARCIAL** | Choca con el SLC del sistema; SCO inaccesible | MyPhone.Demo (control sin audio) | Interferir con el servicio Bluetooth | Win10/11 | Alta | Solo si A falla: ¿acepta el iPhone una segunda conexión HF? |
| **C. ANCS (acciones positiva/negativa) para contestar/rechazar en el iPhone** | **PARCIAL** | La llamada se queda en el iPhone; audio no pasa al PC por sí solo | ANCS verificado en tu hardware para *avisar* (`docs/39`); las acciones **no se han ejecutado** | iOS decide el audio | iPhone con ANCS | Baja | Ejecutar la acción negativa en una llamada real, con permiso |
| **D. C + enrutado de audio en iOS** | **HIPÓTESIS** | Existe en iOS un ajuste de accesibilidad «Enrutamiento de audio de llamadas»; no consultado en esta sesión | Sin evidencia | Requiere que Windows tenga SLC HFP activo con el iPhone | iPhone | Baja | Comprobar si el PC aparece como salida de audio del iPhone sin registrar ninguna app |
| **E. Vía A distribuida por la Microsoft Store (o firmada por una CA de confianza)** | **HIPÓTESIS / condicionada** | Aprobación de la capacidad restringida por Microsoft; no se sabe si una firma de CA (SignPath) basta o si hace falta identidad de Store | Sefirah (Store) con llamadas en Win11; Klangbruecke/MyPhone (carga lateral) denegados | Plazos y criterio de aprobación ajenos | Win11 | Media-alta | **Prueba de control** (fila siguiente) y, si procede, solicitud a Microsoft |
| **F. Prueba de control sin código: una app de la Store de terceros (Thy Phone, de pago) en TU PC con TU iPhone** | **PRUEBA** | Costo (~3,69 $) y que soporte tu versión de iOS | Terceros dicen que soporta iPhone | Ninguno para el sistema | Win10/11 | Baja | Si **ella** consigue llamadas en tu compilación, el fallo es de carga lateral; si **tampoco**, es la compilación (26300) o el iPhone |
| Forjar token LAF / `BypassRegistration` / driver / BTstack / Phone Link | **RECHAZADAS** | §4 | — | — | — | — | — |

## 6. Arquitectura propuesta para WinMac Connect y NotchWin (sin alterar lo actual)

**Principio:** nada de lo que funciona hoy cambia. Las llamadas siguen detectándose por ANCS (`CallTracker`, verificado en tu hardware según `docs/39`); la nueva capa solo *añade* control cuando está disponible.

1. **Identidad de paquete solo donde hace falta.** El módulo `WinMac.Connect.App.exe` (proceso aparte, ya supervisado por el host) recibe identidad con un **paquete disperso** (*package with external location*), de modo que el resto de WinMac sigue sin paquete. *Hipótesis a probar:* que una capacidad restringida funcione en un paquete disperso (no hay evidencia en las fuentes revisadas; Sefirah y Klangbruecke usan MSIX completo). Alternativa segura: MSIX completo solo para Connect. **La distribución es parte del diseño, no un detalle:** si la prueba de §8 muestra que solo los paquetes de la Store consiguen el registro, la función de llamadas completas solo existiría en una variante distribuida por la Store; el resto de WinMac seguiría siendo la descarga actual sin cambios.
2. **Escalera de degradación**, de menos a más:
   - L0 detección ANCS (existe, verificada).
   - L1 rechazar/contestar por ANCS en el iPhone (sin audio en el PC) — por validar.
   - L2 `PhoneCall` + audio local — por validar (este informe).
3. **Estado y comandos por el canal IPC actual:** `connect.call` (retenido) amplía su carga con `capabilities` (`canControl`, `canRouteAudio`), `source` (`ancs`/`plt`) y los datos de `PhoneCallInfo` cuando existen. Los comandos (`answer`, `reject`, `end`, `route-local`) van por un tópico de comandos del host **solo desde el módulo `bar`**, igual que `host.cmd`, y nunca salen de la máquina.
4. **Fusión sin duplicados:** una llamada de `PhoneCall` y la de ANCS se unen por estado y ventana de tiempo; con `PhoneCallInfo` el nombre es más fiable que el título de la notificación.
5. **NotchWin:** solo muestra los botones **Contestar / Rechazar / Colgar** si `canControl` es verdadero; si no, queda exactamente como hoy. No se toca el cristal ni las letras sincronizadas.
6. **Seguridad y privacidad:** nombres y números solo en memoria, nunca en registros; ninguna acción sin gesto explícito del usuario; nunca contesta automáticamente; el módulo sigue desactivado por defecto.
7. **Qué NO se hace:** no se marcan llamadas, no se tocan ajustes de Bluetooth del usuario, no se desempareja.

## 7. Pruebas de concepto

Carpeta `research/iphone-calls/` (rama `research/iphone-calls`, nada de producción modificado):

| Pieza | Qué es |
|---|---|
| `CallProbe/` | Consola de Windows (.NET 10, SDK 26100) con las etapas 1–5. **Compila** (verificado). **No se ha ejecutado contra tu iPhone.** |
| `CallProbe.Logic/` y `CallProbe.Logic.Tests/` | Lógica pura y 19 pruebas (**pasan** en Linux): cómo interpretar cada resultado (un `DeniedBySystem` sin paquete es «no concluyente», no «bloqueado»), el marcador de la llamada real (solo se puede decir «verificado» si TODAS las casillas están observadas) y la puerta de confirmación (palabra exacta). |
| `package/` | `AppxManifest.xml` (identidad + capacidades) y scripts para registrar el paquete **en modo desarrollador, sin certificados** y ejecutar con o sin identidad. |
| `tools/ListWinRtApi/` | Lista tipos y miembros WinRT leyendo los metadatos del SDK (sirve en Linux). |
| `evidence/` | Listados de la API de `Windows.ApplicationModel.Calls` en los SDK 19041 y 26100. |

**Seguridad de la prueba:** las etapas 1, 2, 3b y 4 son de solo lectura. Registrar exige escribir `REGISTRAR`; contestar, rechazar y colgar exigen `CONTESTAR`, `RECHAZAR` o `COLGAR`. **Nunca marca ni inicia llamadas.** Nombres y números salen enmascarados.

**Compilar y ejecutar (en tu Windows 11, con el SDK de .NET 10):**

```powershell
cd research\iphone-calls
dotnet test CallProbe.Logic.Tests
dotnet publish CallProbe -c Release -r win-x64 --self-contained -o publish
.\package\Run-Probe.ps1 stage1 -Unpackaged      # etapa 1: descubrimiento (solo lectura)
.\package\Run-Probe.ps1 stage2 -Unpackaged      # etapa 2: permisos SIN paquete (línea base)
```

Para la comparación **con** paquete (modo desarrollador activo; es un ajuste tuyo y reversible):

```powershell
.\package\Register-DevPackage.ps1 -BuildDir .\publish
.\package\Run-Probe.ps1 stage2                  # etapa 2 CON paquete
.\package\Unregister-DevPackage.ps1             # al terminar
```

Los informes quedan en `Documentos\CallProbe\`.

## 8. Plan de validación en hardware

Antes de nada: cierra Phone Link y desvincula el iPhone de él; el iPhone, emparejado por Windows (Classic **y** LE), desbloqueado y cerca. Anota la versión de iOS y el modelo del adaptador.

| Etapa | Qué haces | Qué observas | Cómo se clasifica |
|---|---|---|---|
| **1 Descubrimiento** | `stage1 -Unpackaged` | Compilación real, adaptador, servicios SDP del iPhone (HFP AG `0x111F`), transportes de telefonía, `CallsPhoneContract` v6, consentimiento «Llamadas telefónicas», Phone Link en ejecución | Descarta causas locales. Confirma la compilación (26300 o no). |
| **2 Permisos** | `stage2 -Unpackaged`, luego `stage2` con paquete | `RequestAccessAsync` en cada caso | Sin paquete `DeniedBySystem` = no concluyente. Con paquete `DeniedBySystem` = **BLOQUEADO bajo estas condiciones** (tras descartar Phone Link y la privacidad). `Allowed` = pasa a 3a. |
| **3a Registro** | `stage3 register` (escribes `REGISTRAR`) | `IsRegistered()` y `ConnectAsync()` | `true` = registro funciona. `false` = patrón Q&A 6041632: probar en una compilación estable de Windows 11 y/o otro PC antes de concluir. |
| **3b Observación** | `stage3 monitor`, llámate desde otro teléfono **sin contestar** | `CallStateChanged`, `PhoneCall` con estado, dirección, contacto enmascarado | Control: detección por `PhoneCall` (sin tocar nada). |
| **4 Audio** | `stage4 audio` antes y durante una llamada | Dispositivos de comunicaciones predeterminados; `AudioRoutingStatus` | Dice si Windows cree que puede enrutar al PC. |
| **5 Llamada real** | **Solo con tu autorización explícita.** `stage5 guided` | Detectada → contestar (`CONTESTAR`) → audio local → preguntas: ¿oyes?, ¿te oyen? → colgar (`COLGAR`) → recuperación | El marcador resume: **VERIFICADO EN HARDWARE** solo si las 6 casillas se observaron; si no, PARCIAL o SIN VERIFICAR, diciendo qué falta. |

**Prueba de control (decisión tuya, cuesta dinero):** instala Thy Phone desde la Store en el PC (sin Phone Link) y comprueba si consigue registrarse y pasar el audio de una llamada del iPhone. Separa «Windows 11 niega a las apps de carga lateral» de «tu compilación o tu iPhone no lo permiten».

**Pruebas baratas adicionales (sin código):** con una llamada entrante que contestas *tú en el iPhone*, mira en «Audio» de iOS si el PC aparece como destino (hipótesis D). Comprueba también *Configuración › Privacidad y seguridad › Llamadas telefónicas*.

**Árbol de decisión:**
- Etapa 2 con paquete = `Allowed` y 3a `true` → **VIABLE a probar de punta a punta**; seguir con 3b, 4 y 5.
- 3a `false` o `DeniedBySystem` con paquete (**de carga lateral**) → resultado «**BLOQUEADO para carga lateral**», que no equivale a «imposible»: hacer la prueba de control con una app de la Store; si esa funciona, el camino es la distribución por Store; si no, repetir en otra compilación estable/otro PC. Si persiste, quedarse en la escalera L0/L1 (ANCS).
- Etapa 5 con control pero sin audio bidireccional → **PARCIAL**.

## 9. Preguntas abiertas y riesgos

0. **¿Concede Windows 11 el registro a un paquete firmado por la Store pero no a uno de carga lateral?** Es la pregunta central; ninguna fuente la responde.
1. ¿La compilación del PC es realmente 26300 (Insider) o es otra? (`stage1` lo dice).
2. ¿Funciona la vía A con **iPhone** como Audio Gateway para una app de terceros? Solo Phone Link, Thy Phone y Unison lo anuncian.
3. ¿Se puede conceder `phoneLineTransportManagement` a un paquete **disperso**?
4. ¿Hay conflicto con Phone Link si ambos conocen el iPhone? (MyPhone #26: «desvincula primero»).
5. Distribución: ¿aprobará Microsoft la capacidad para la Store, o se firma con SignPath?
6. Estabilidad: la API depende de comportamiento de Windows 11 que ha cambiado entre 22H2, 23H2 y 24H2/26100; no es un contrato estable.
7. El códec SCO (CVSD o mSBC) depende del controlador del radio.
8. **El Asistente de concentración de Windows puede cortar el enlace manos libres** al activarse o desactivarse sus reglas automáticas (p. ej. al abrir un juego o una app a pantalla completa) (Klangbruecke README l.91-96 y FINDINGS §26, observación del autor en Windows 10). Afectaría a llamadas en curso; hay que probarlo en Windows 11.
9. **Windows 11 tiene su propia interfaz de llamada** que, según Klangbruecke §16 (observado en Windows 10), aparece al registrar un transporte (aceptar/rechazar, silenciar, colgar). WinMac tendría que convivir con ella o suprimirla, y no hay evidencia de cómo se comporta con `PhoneCall` en Windows 11.

## 10. Conclusión

**PROMETEDOR, condicionado.** Existe una vía concreta, soportada por la propia Windows, sin controladores, sin Phone Link, sin tocar el iPhone y sin vulnerar protecciones: `PhoneLineTransportDevice` + `PhoneCall` desde un paquete con la capacidad declarada. La conclusión previa («BLOQUEADO») se apoyaba en un `DeniedBySystem` sin código ni registro, probablemente obtenido sin identidad de paquete, que además no prueba nada sobre contestar ni sobre audio. Pero **la evidencia de terceros sí respalda que Windows 11 deniega el registro a apps de carga lateral**, y nadie ha demostrado el caso *iPhone + Windows 11 + app abierta*. Por eso el resultado puede acabar en **PARCIAL** (solo ANCS) o depender de una distribución por la Store. Falta lo esencial: **demostrarlo con tu iPhone**. El plan de §8 lo hace en etapas, empezando por lecturas sin ningún riesgo.

## 11. Resultados reales de CallProbe (etapas 1 y 2) y análisis de la etapa 3

Datos anonimizados (sin direcciones, nombres de dispositivo ni rutas). Windows 11 build 10.0.26300.9711. Los registros originales NO están en el repositorio.

### 11.1 Comparación sin paquete vs. con paquete (VERIFICADO EN HARDWARE)

| Consulta (solo lectura) | Sin paquete | Con paquete (registro de desarrollo, `SignatureKind=None`) |
|---|---|---|
| Identidad de paquete | No | Sí (`WinMac.Research.CallProbe`) |
| `RequestAccessAsync()` | **DeniedBySystem** | **Allowed** |
| `IsRegistered()` antes de registrar | False | False |
| `AudioRoutingStatus` | CanRouteToLocalDevice | CanRouteToLocalDevice |
| `InBandRingingEnabled` | — | False |
| `LimitedAccessFeatures` (token vacío) | Unavailable | Unavailable |

Etapa 1 (sin paquete): Phone Link no estaba en ejecución; el adaptador soporta Classic, LE, central, peripheral y Secure Connections; el iPhone figuraba emparejado y *desconectado* en ese instante, pero su SDP lista HFP-AG, PBAP, MAP (+2 servicios Apple); existe 1 transporte Bluetooth de telefonía.

Conclusiones (VERIFICADO): (a) `DeniedBySystem` sin paquete no es un veredicto sobre la app empaquetada; con identidad de paquete y la capacidad restringida declarada, el permiso pasa a Allowed incluso en esta build. (b) Allowed **no** prueba que el registro funcione: sigue sin existir evidencia de que `RegisterApp()` tenga efecto aquí. (c) El hilo MS Q&A 6041632 (REPORTADO POR TERCEROS, solo vía resumen de búsqueda) describe justo ese patrón en la build 26300: Allowed, `RegisterApp` sin error, `IsRegistered` sigue False.

### 11.2 Bloqueo de la etapa 1 tras `PhoneCallManager.RequestStoreAsync: OK`

- OBSERVADO: el último paso registrado fue `RequestStoreAsync` OK; después el proceso no avanzó.
- Lo que sigue en el código original era el arranque de `PhoneLineWatcher` (`Start()`, esperar `EnumerationCompleted`, `Stop()`) y `RequestLineWatcher`. Esas llamadas son síncronas en WinRT y pueden no retornar si el servicio de telefonía no responde. HIPÓTESIS (no determinable con los logs): bloqueo en `Start/Stop` del watcher, o `Stop()` antes de `EnumerationCompleted`.
- Corrección (en código, con pruebas lógicas): cada llamada de Windows corre con `StepGuard` (tiempo máximo, la llamada colgada se abandona), registra «→ paso» antes de ejecutarse (la última línea dice qué se colgó), el watcher solo se usa con `--lines` y `Stop` no se espera; el proceso termina con `Environment.Exit`. No se necesita cancelación «segura» de la llamada: WinRT no permite cancelarla, solo abandonarla y salir. Etapa 2 (`Permissions`) quedó con las mismas guardas.

### 11.3 Etapa 3 — qué hace exactamente

Solo `stage3 register` modifica algo. Orden: (1) localizar el transporte (aborta si hay más de uno); (2) comprobar el diario de recuperación (si quedó un registro sin revertir, bloquea); (3) instantánea de estado (registrado, llamada activa, Phone Link, Bluetooth clásico conectado, hash de dispositivos de comunicaciones, acceso); (4) precondiciones (bloquea o avisa; si bloquea, no cambia nada); (5) texto explicativo y confirmación escrita **REGISTRAR** (sin stdin interactivo cancela); (6) escribe el diario *antes* de actuar; (7) `RegisterApp()` con tiempo máximo 15 s; (8) sondea `IsRegistered()` 10 s; (9) si quedó registrado: lee líneas y `AudioRoutingStatus`; `ConnectAsync` solo con `--connect` y segunda confirmación **CONECTAR**; (10) espera 10 s (`--hold`); si apareciera una llamada real **no la toca**; (11) interpreta; (12) **revierte siempre** con `UnregisterApp()` salvo `--keep`; (13) segunda instantánea y diferencias. No contesta, rechaza ni marca llamadas; el código de contestar/colgar (etapa 5) existe pero no se ejecuta aquí.

### 11.4 Las 8 preguntas

1. **Qué hace la etapa 3**: ver 11.3. Una única operación de escritura: `RegisterApp()` (+ su `UnregisterApp()`).
2. **Efectos de `RegisterApp()`** — DOCUMENTADO: registra la app como la que gestiona el transporte telefónico (Bluetooth) de ese dispositivo para el usuario, de modo que reciba las llamadas/estado de esa línea. HIPÓTESIS: el registro puede ser exclusivo por transporte (otra app registrada, p. ej. Phone Link, compite). NO verificado en este PC.
3. **Interferencias posibles** — el riesgo es bajo pero no nulo y es HIPÓTESIS: (a) llamadas normales: si el registro tiene efecto, esta app (sin UI) podría pasar a ser la receptora de la línea mientras dura la prueba; por eso se exige no esperar llamadas y el tiempo es de ~30 s; (b) dispositivos de audio: no se cambian dispositivos ni volúmenes; se compara el hash de los dispositivos de comunicaciones antes/después; (c) emparejamiento: no se emparejan ni olvidan dispositivos; `ConnectAsync` (opcional) abre el enlace de telefonía, no el emparejamiento; (d) Phone Link: se bloquea la prueba si está en ejecución (sobrescribible con `--allow-phone-link`, no recomendado).
4. **Verificar éxito**: `IsRegistered()` True dentro de 10 s tras `RegisterApp()` (el programa lo imprime); la segunda instantánea debe mostrar `IsRegistered` False tras revertir.
5. **Revertir / fallo a mitad**: `UnregisterApp()` automático; si el proceso muere, el diario (`Documentos\CallProbe\stage3-journal.json`, escritura atómica) queda como «pending-revert» y **la siguiente ejecución rechaza registrar** hasta ejecutar `stage3 unregister` (que no pide confirmación si hay diario pendiente). `status` es solo lectura. Si `RegisterApp` fuese a persistir tras un cierre brusco, `unregister` lo deshace; el registro está ligado al paquete, y `Unregister-DevPackage.ps1` quita el paquete entero (HIPÓTESIS sobre la limpieza del estado interno de Windows; no verificada).
6. **¿iPhone conectado?** Recomendado: sí, conectado por Bluetooth y desbloqueado, para que el transporte exista y se pueda leer línea/enrutado (el SDP ya muestra HFP-AG). Un iPhone no conectado por Bluetooth Classic **bloquea** la prueba (se omite solo con `--allow-disconnected`), porque un resultado False sin iPhone conectado no significaría nada. Además bloquean: sin identidad de paquete, acceso distinto de Allowed, llamada activa/entrante, `IsRegistered` ya True o ilegible, `--hold` fuera de 0–60. HIPÓTESIS: el registro podría requerirlo.
7. **`RegisterApp()` sin error pero `IsRegistered()` False**: el llamado no lanzó excepción pero Windows no aceptó/persistió el registro. Posibles causas (todas HIPÓTESIS): restricción por tipo de firma/distribución (sideload vs Store) o por build 26300; capacidad restringida declarada pero no honrada para un paquete sin firma; registro retrasado (por eso el sondeo de 10 s). Resultado: se clasifica como «sin efecto», la vía nativa queda bloqueada en este PC con este empaquetado, y se pasa a la prueba de control (Thy Phone de Store) y a evaluar firma real, no a eludir la restricción.
8. **LAF «Unavailable» con Allowed**: `RequestAccessAsync` (acceso al dispositivo) y `LimitedAccessFeatures` (desbloqueo de función con token del fabricante) son controles distintos. Allowed + Unavailable es compatible con: la función no requiere token para este flujo, o no está disponible a apps de terceros. No sabemos cuál. NO se infiere que Allowed garantice llamadas funcionales; la prueba es `IsRegistered()` y, después, una llamada real autorizada. No se forja ningún token (descartado, sección 4).

### 11.5 Evaluación de riesgo de la etapa 3

| Riesgo | Prob. | Impacto | Mitigación |
|---|---|---|---|
| Registro persiste tras fallo | Baja | Bajo | Diario + `unregister`; revertir siempre |
| Interferir con una llamada entrante real | Baja | Medio | Prueba corta, sin llamadas esperadas, no se toca ninguna llamada |
| Conflicto con Phone Link | Media si está abierto | Bajo | Bloqueo si está en ejecución |
| Cambio de dispositivo de audio | Muy baja | Bajo | Comparación de hash antes/después |
| Colgado de WinRT | Media (visto en etapa 1) | Bajo | `StepGuard` + salida forzada |
| Daño al emparejamiento | Muy baja | Medio | No se emparejan/olvidan dispositivos |

### 11.6 Resultados posibles de `stage3 register` e interpretación

| Observación | Interpretación | ¿Avanzar? |
|---|---|---|
| `IsRegistered` True y línea disponible que puede marcar/gestionar | Vía nativa viable en este PC | Sí, con revertir verificado, instantáneas iguales y autorización explícita para una llamada real |
| `IsRegistered` True, sin línea | Registro aceptado, línea aún no expuesta (¿iPhone no conectado como HF?) | Repetir con `--connect` (confirmación aparte) |
| Sin error, `IsRegistered` False | Sin efecto (pregunta 7) | No; prueba de control Thy Phone y estudio de firma/Store |
| Excepción (p. ej. acceso denegado) | Restricción dura | No |
| Timeout de `RegisterApp` | Servicio de telefonía bloqueado | No; revertir, reiniciar sesión y revisar |
| Instantáneas distintas tras revertir | Efecto secundario no deseado | No hasta entenderlo |

Condiciones para pasar a prueba con llamada real (todas): registro con efecto verificado; reversión verificada; instantánea final igual a la inicial; autorización explícita y por escrito del usuario para esa llamada; Phone Link cerrado; llamada iniciada por el usuario desde otro teléfono.

### 11.7 Procedimiento seguro propuesto (NO ejecutado)

```powershell
# 0) Una vez: registrar el paquete de desarrollo (sin certificados) — ya hecho en tu PC
.\package\Register-DevPackage.ps1
# 1) Solo lectura: estado actual
.\package\Run-Probe.ps1 stage3 status
# 2) Con el iPhone conectado y desbloqueado, Phone Link cerrado, sin llamadas esperadas:
.\package\Run-Probe.ps1 stage3 register     # escribe REGISTRAR para continuar; revierte solo
# 3) Solo si quedó algo pendiente:
.\package\Run-Probe.ps1 stage3 unregister
```
Opciones: `--connect` (añade ConnectAsync, pide CONECTAR), `--keep` (no revertir; no recomendado), `--hold N`, `--allow-disconnected`, `--allow-phone-link`. Las etapas 4 y 5 no se ejecutan.

### 11.8 Defectos de privacidad de CallProbe corregidos

Dirección del transporte sin enmascarar, ruta de usuario en la línea de argumentos y etiqueta engañosa de protección de emparejamiento: corregidos en `Report.cs`/`Discovery.cs`. Aun así, revisa los informes antes de compartirlos.

## Apéndice A — Correcciones a `docs/39` y `docs/41`

| Documento | Afirmación | Corrección |
|---|---|---|
| `docs/39:12`, `:50` | «el único proyecto que lo intentó no logró el audio» (MyPhone) | Eso describe la pila HFP **propia** y experimental de MyPhone (README l.47-49). La vía por `PhoneLineTransportDevice` (audio gestionado por Windows) está reportada funcionando por Klangbruecke (Android, Windows 10). |
| `docs/39:99` | «Llamadas con audio y control HFP desde WinMac: bloqueado (confirmado en tu PC)» | No consta con qué código, identidad ni método. No distingue «sin paquete» de «con paquete». |
| `docs/41` WM-039 | «WinMac controla las llamadas con API pública: **No**» | `PhoneCall` (contestar, rechazar, colgar, enrutar audio) es pública desde el SDK 26100 (contrato v6). |
| `docs/39:99` y `docs/41` | «Bloqueado» como conclusión general | Hay evidencia de terceros de denegación en **carga lateral** en Windows 11, pero no de que esté bloqueado para toda distribución (Store) ni para iPhone concretamente. Debe decir «bloqueado para carga lateral, sin verificar en tu PC». |
| `docs/41` WM-039 | «sin audio bidireccional fiable no hay botón Contestar» | El audio lo gestiona Windows; la condición correcta es «sin registro del proveedor de llamadas no hay control», aún por probar. |

## Apéndice B — Referencias

- Documentos y código de WinMac: rama `claude/relaxed-turing-cijzko`, commit `28f8aee` (`docs/39`, `docs/40`, `docs/41`, `src/WinMac.Connect.*`).
- **SDK de Windows:** NuGet `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 y 10.0.26100.57 → `evidence/*.txt` (reproducible con `tools/ListWinRtApi`).
- **Sefirah:** `github.com/shrimqy/Sefirah`, commit `6db524a6ac3b…` (2026-10-17), GPL-3.0 → `src/Sefirah/Platforms/Windows/Utilities/CallingFeatureUtils.cs`, `Calling/PhoneCall.cs`, `Calling/PhoneLineService.cs`, `FeatureTokenGenerator.cs`, `src/Sefirah/Package.appxmanifest`.
- **MyPhone:** `github.com/BestOwl/MyPhone`, commit `dbd0edecd81a…` (2022-12-23), MIT → `MyPhone/Device/DeviceCallServiceProvider.cs`, `MyPhone/Utilities/PhoneLineTransportHelper.cs`, `MyPhone.Demo/MyPhone.Demo.HFP/HFP.cs`, `README.md`; issues [#1](https://github.com/BestOwl/MyPhone/issues/1), [#7](https://github.com/BestOwl/MyPhone/issues/7), [#26](https://github.com/BestOwl/MyPhone/issues/26), [#27](https://github.com/BestOwl/MyPhone/issues/27) (leídos el 2026-11-03).
- **Klangbruecke:** `github.com/MYSTRAVIL/klangbruecke`, commit `2cb8e003459e…` (2026-09-29), sin licencia → `README.md`, `docs/FINDINGS.md` §2, §7, §10, §11, §12.
- **Microsoft Q&A** (solo por resultados de búsqueda; el dominio está bloqueado en esta sesión): [6041632](https://learn.microsoft.com/en-us/answers/questions/6041632/phonelinetransportdevice-registerapp-has-no-effect), [1803696](https://learn.microsoft.com/en-in/answers/questions/1803696/issues-with-access-permissions-in-phonelinetranspo), [1663702](https://learn.microsoft.com/en-us/answers/questions/1663702/deniedbysystem-returned-when-i-was-used-phonelinet), [1341397](https://learn.microsoft.com/en-us/answers/questions/1341397/in-wpf-phonelinetransportdevice-requestaccessasync).
- **Microsoft, controladores de Bluetooth:** `windows-driver-docs` → `bluetooth-driver-stack.md` (leído desde GitHub): el audio SCO pasa por un controlador de perfil en modo kernel.
- **Thy Phone / Phone Link / Intel Unison:** solo por resultados de búsqueda (blog del autor, prensa); sin verificar.

## Apéndice C — Registros originales de `ConnectProbe` (los que enviaste)

Archivos: `informe.txt` (comando `all`, 2026-11-03 17:14:11, Windows 10.0.26300.0) e `informe2.txt` (comando `round2`, 17:20:20). No los añadí al repositorio porque contienen nombres de tus dispositivos y de las apps de tus notificaciones; aquí van solo las líneas relevantes, con el nombre del dispositivo sustituido por `<iPhone>`.

**Las líneas que sostienen la auditoría** (`informe.txt`):

```
Telefonía de Windows (PhoneLineTransportDevice, API pública con capacidad restringida):
  dispositivos de transporte de llamadas: 1
  · <iPhone>: acceso DeniedBySystem · registrado: False
  (Sin identidad de paquete y sin la capacidad «phoneLineTransportManagement» lo esperable es DeniedBySystem:
   así se comprueba si WinMac podría controlar llamadas.)
```

**Qué demuestra y qué no:**

| Afirmación | Estado |
|---|---|
| Windows expone tu iPhone como **transporte de telefonía Bluetooth** (1 dispositivo) | **VERIFICADO EN HARDWARE** (registro original). Esto es un dato nuevo y favorable: el transporte existe. |
| `RequestAccessAsync` devolvió `DeniedBySystem` y `IsRegistered` = `False` **sin identidad de paquete ni capacidad** | **VERIFICADO EN HARDWARE** (registro original), **no concluyente** para una app empaquetada. |
| El iPhone publica HFP Audio Gateway (`0000111f`), MAP (`00001132`) y PBAP (`0000112f`), con cifrado y autenticación | **VERIFICADO EN HARDWARE** (registro original). |
| «Llamadas con audio desde WinMac: bloqueado» (`docs/39:99`, `docs/41`) | **No se desprende de este registro.** El registro no probó contestar, ni audio, ni una app empaquetada. |

**Comprobación cruzada contra `docs/39`** (todo coincide con los registros):

- Adaptador: Classic, LE, rol central y periférico, LE y Classic Secure Connections, publicidad extendida: todo `True` ✓.
- iPhone en Classic (`Connected`) y en LE (`Random`, `EncryptionAndAuthentication`) ✓.
- GATT: Battery Service, ANCS, AMS, Current Time, Device Information (más Continuity y Nearby, privados, y servicios `0xFE15`); batería 75 % y suscripción `Success` ✓.
- ANCS, primera ronda (30 s): **0** eventos; segunda ronda (120 s, sesión GATT mantenida): **322 ya existentes**, 9 nuevas, 4 eliminadas ✓.
- Llamada: `Llamada entrante` (`com.apple.mobilephone`) con **acción positiva y negativa**, luego eliminada y `Llamada perdida` ✓; un `ProtocolError (0xA2)` al pedir datos de una notificación ya borrada ✓.
- MAP: `0xC3` (prohibido) en la primera ronda y `0xA0` (éxito) en la segunda, tras activar el permiso en el iPhone ✓.

**Lo que añaden para la decisión:**

1. La compilación es **26300**. Un reporte ajeno de Microsoft Q&A (6041632) describe **justo esa compilación** con una app **empaquetada**: acceso `Allowed` pero registro sin efecto. Si tu PC se comporta igual con paquete, el obstáculo sería la compilación y no la carga lateral. La etapa 2 con paquete lo separa.
2. Como el transporte **ya existe** y Windows lo enumera, el primer obstáculo real es solo el permiso de registro, que es justo lo que la comparación sin paquete frente a con paquete mide.
3. Las acciones positiva y negativa de ANCS están anunciadas en la llamada entrante, pero **no se ejecutaron**: la vía C (contestar/rechazar por ANCS en el iPhone) sigue sin validar.
