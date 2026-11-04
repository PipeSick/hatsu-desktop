# Investigación independiente — contestar llamadas del iPhone desde Windows (WinMac Connect)

Fecha: 2026-11-03 · Rama `research/iphone-calls` (worktree aparte, sobre `28f8aee`) · **No se modificó ningún archivo de WinMac ni de NotchWin.**

## Resumen ejecutivo final (iteración de viabilidad, 2026-11-04)

**Conclusión: C — NO DEMOSTRADO bajo las restricciones actuales. Recomendación: detener el desarrollo de esta función en WinMac.** Detalle, fuentes y matriz en §15.

**Fase 4 (arquitecturas HFP alternativas, §16):** ninguna arquitectura estudiada llega a A (viable y demostrada) ni a B (plausible, pendiente de prueba). Respuesta a «¿hay una arquitectura defendible con el Bluetooth existente y sin sustituir su controlador?»: **NO DEMOSTRADA**. Falta un transporte SCO de modo usuario (Windows solo expone RFCOMM; SCO es de kernel) o un registro efectivo del rol HF. BluetoothHFP exige poseer el controlador (fundamental, sin licencia, BTstack no comercial); iphonebridge delega en oFono+BlueZ+PipeWire y sockets SCO de usuario que Windows no tiene. Único experimento propuesto: E1 (§16.9), solo observación.

- **Hecho en hardware (P1–P3, build 26300):** `RegisterApp()` retorna sin error pero `IsRegistered()` nunca pasa a `True`, con paquete suelto (`Allowed`) y con MSIX firmado (`DeniedBySystem`). El mismo síntoma está reportado por un tercero en la misma build (Microsoft Q&A 6041632).
- **Documentación oficial (leída íntegra):** `RegisterApp` solo exige la capacidad restringida; la carga lateral no necesita aprobación; la Store sí. No dice nada de Limited Access Features para esta clase, aunque el ID `phonelinetransportdevice_v1` devuelve `Unavailable` (= sigue limitado y no tenemos acceso). **No hay evidencia pública de un registro efectivo en Windows 11 actual sin saltarse un control.**
- **Audio:** el SCO solo es accesible por drivers de modo kernel (documentación de Microsoft). Con APIs públicas de usuario solo se llega al audio a través de `PhoneCall`, que depende del registro. RFCOMM propio, ANCS y una app en iOS: **no viables** para el objetivo completo (D).
- **Única ruta con audio bidireccional demostrado en Windows con un iPhone** (un tercero, un equipo): pila Bluetooth propia en modo usuario sobre el radio reasignado al WinUSB *inbox* (BluetoothHFP `native-poc`). **No cumple el espíritu de las restricciones** (toma el radio, un hardware, licencia de BTstack) y no la hemos reproducido. En Linux, un iPhone con iOS 26.5 sí contesta/cuelga/habla (iphonebridge): el límite está en Windows, no en el iPhone.
- **¿Store?** Solo hipótesis. **Causa exacta del bloqueo: desconocida.**
- Confianza: alta (≈85 %) en «hoy no hay ruta reproducible»; baja (≈30 %) en que LAF o Store lo desbloqueen.

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
| Zadig/WinUSB + pila propia (BTstack) | **EXCLUIDA por restricciones (pero demostrada por un tercero, ver §15.5)** | Quita el radio a Windows (rompe mandos y demás dispositivos; Klangbruecke §5). BluetoothHFP `native-poc` (`dbd737b`) muestra audio bidireccional con un iPhone sobre WinUSB *inbox* + BTstack en un equipo; no es reproducible aquí ni cumple las restricciones. |
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

## 12. Iteración 2 — El bloqueo real de `RegisterApp()` (resultados en hardware + auditoría)

Etiquetas: **[HW]** demostrado en el PC del usuario · **[SDK]** verificado en metadatos del SDK · **[DOC]** documentación oficial (solo la he podido leer por resúmenes de búsqueda: `learn.microsoft.com` está fuera de la red de esta sesión; **hay que permitir ese dominio** para contrastar el texto literal) · **[3P]** evidencia de terceros · **[HIP]** hipótesis · **[INF]** inferencia mía.

### 12.1 Resultados reales (commit probado `2a62c15`, build 10.0.26300.9711, iPhone conectado por Classic, Phone Link cerrado, paquete firma `None`)

| Ejecución | `RequestAccessAsync` | `RegisterApp()` | `IsRegistered()` a los 10 s | `AudioRoutingStatus` |
|---|---|---|---|---|
| Etapa 3, intento 1 | Allowed | sin excepción, 79 ms | **False** | CannotRouteToLocalDevice |
| Etapa 3, intento 2 (iPhone desbloqueado) | Allowed | sin excepción, 56 ms | **False** | CannotRouteToLocalDevice |
| `stage3 status` posterior | Allowed | — | False | — |

Diario: `clean`; sin llamadas; sin Phone Link. [HW]

**Demostrado [HW]:** la llamada retorna sin error y el registro efectivo no se materializa, con el iPhone bloqueado o desbloqueado. **No demostrado:** la causa. **Nota:** 56–79 ms es muy poco para un cambio de estado persistente en el servicio de telefonía; es compatible con un retorno anticipado sin efecto [INF], pero un tiempo corto no prueba nada por sí solo.

### 12.2 Auditoría de las tres llamadas

| Llamada | Qué es [SDK]/[DOC] | Qué NO garantiza |
|---|---|---|
| `RequestAccessAsync()` → `DeviceAccessStatus` | Consulta de permiso de acceso al dispositivo para el contexto actual | Que el registro vaya a funcionar. `Allowed` ≠ registrado |
| `RegisterApp()` → `void` | Pide registrar la app (por usuario; [DOC] el registro persiste hasta `UnregisterApp` o hasta que se quite el dispositivo, y es necesario para ver la `PhoneLine` del transporte) | Cualquier señal de fracaso salvo excepción: al ser `void`, un no-op silencioso es indistinguible de un éxito sin `IsRegistered()` |
| `IsRegistered()` → `bool` | Estado del registro de esta app para el usuario activo | — es el único criterio válido de éxito |

Existen además `RegisterAppForUser`/`UnregisterAppForUser` [SDK, `evidence/sdk-10.0.26100.57-…`]. No se usan: requieren un `User` y serían otra escritura; no hay evidencia de que cambien el resultado [HIP].

### 12.3 Evidencia de terceros por versión de Windows [3P]

| Build | Reporte | Fuente |
|---|---|---|
| Win10 19045, MSIX firmado autofirmado | `RegisterApp` lanza `UnauthorizedAccess` sin `RequestAccessAsync` previo; con él, `IsRegistered=True`; LAF `Unavailable` y aun así funciona. **Probado con Android**, no con iPhone | Klangbruecke FINDINGS §12 |
| Win10 21H2, LTSC 2021 | Funciona | MyPhone #26 |
| Win11 22H2 (22621) | `DeniedBySystem` o `RegisterApp` no funcional / `UnauthorizedAccess` | MyPhone #26 |
| Win11 23H2 | Sigue fallando (comentarios nov-2023 y ene-2024) | MyPhone #26 |
| Win11 26200 (prueba de un tercero) | `DeniedBySystem` para apps instaladas por carga lateral | Klangbruecke §15.1 |
| Win11 **26300**, app empaquetada | `Allowed`, `RegisterApp` sin error, `IsRegistered` False, `ConnectAsync` False. **Idéntico a nuestro caso.** Sin respuesta aceptada | Microsoft Q&A 6041632 (el resumen del buscador la describe a veces como «empaquetada» y a veces como «identidad dispersa»: no verificado) |
| Win11 (oct-2024) | Con la clave `HKLM\…\Hfp\HandsFree\BypassRegistration=1` + desbloqueo LAF, `RequestAccess/RegisterApp/Connect` pasan; sin funcionalidad de llamadas aún | MyPhone #26 — **vía descartada** (cambia seguridad / falsifica token) |
| Win11 (abr-2026) | Phone Link omite la comprobación LAF desde la build 22000 (`ShouldSkipLAFCheck`); «si otra app ya registró el dispositivo, desempareja antes o recibirás `DeniedBySystem`» | MyPhone #26 |
| Cualquier | Thy Phone y CallCenter (Store) funcionan en Win11 según usuarios; ningún comentario compara Store vs carga lateral | MyPhone #26 |

Conclusiones prudentes: (1) el síntoma exacto de este PC está reportado por un tercero en la misma build, sin solución conocida [3P]; (2) **nadie ha demostrado** registro funcional de una app propia en Win11 sin saltarse un control; (3) las pruebas con éxito en Win10 usaron Android; con iPhone solo hay afirmaciones de compatibilidad (MyPhone README) sin evidencia de llamadas controladas; (4) la aseveración «Store o firma comercial lo arreglan» **no tiene evidencia** a favor ni en contra.

### 12.4 Auditoría de manifiesto, capacidades y registro

| Aspecto | CallProbe | MyPhone | Sefirah | Klangbruecke |
|---|---|---|---|---|
| `phoneLineTransportManagement` (rescap) | sí | sí | sí | sí |
| `runFullTrust` | sí | sí | sí | sí |
| `phoneCall` (uap) | sí | no | sí | no |
| `bluetooth` (DeviceCapability) | sí | no | sí | sí |
| Extensión `phoneCallActivation` | no | no | sí | no |
| `TargetDeviceFamily` mínimo | 19041 | 18362 | 19041 | 19041 |
| **Firma / distribución** | **ninguna (registro suelto, modo desarrollador)** | firmada | firmada | firmada autofirmada |
| Lanzamiento | **`Invoke-CommandInDesktopPackage`** | activación normal | activación normal | activación normal |

Lectura: el conjunto de capacidades no es el factor diferenciador (cubre el de todas las referencias que funcionan en Win10). Las diferencias reales son tres [INF]:
- **H1 — firma:** somos el único paquete sin firma. Las pruebas exitosas conocidas son de paquetes firmados. Efecto sobre capacidades restringidas en registro suelto: sin documentación localizada.
- **H3 — activación:** nuestro proceso nace de `Invoke-CommandInDesktopPackage`, no del arranque normal del paquete. Tiene identidad (lo prueba el `Allowed`), pero podría no tener el contexto de activación que el servicio asocia al registro. Sin evidencia a favor ni en contra.
- **H2 — build/Windows 11:** el síntoma coincide con el tercero en 26300 aunque su configuración sea distinta.
`phoneCallActivation` es para la interfaz de llamadas de la app; no hay evidencia de que sea precondición del registro (una sugerencia en MyPhone #26 sin resultado reportado) [3P].

### 12.5 Hipótesis ordenadas por evidencia (no por intuición)

| # | Hipótesis | Evidencia a favor | Evidencia en contra | Estado | Prueba |
|---|---|---|---|---|---|
| A | En Win11 26300 Windows ignora/deniega el registro de apps de terceros por una restricción de plataforma (independiente de nuestro empaquetado) | Mismo síntoma de un tercero en 26300 [3P]; fallos en 22H2/23H2/26200 [3P]; Phone Link tiene vía propia [3P] | Ninguna directa | **La más respaldada**; no probada | Control con Thy Phone/Store (¿registra ahí?) — solo informativo |
| B | El registro exige firma real/Store | Sólo lógica general | Klangbruecke y MyPhone funcionan **autofirmados** en Win10 [3P] | Sin evidencia en Win11 | H1: paquete firmado autofirmado |
| C | El registro suelto (firma `None`) no honra la capacidad restringida | Somos el único sin firma | Sí obtenemos `Allowed` (la capacidad parece leída) [HW] | Plausible, barata de descartar | H1 |
| D | Hace falta lanzar por activación normal | Diferencia de proceso real | `Allowed` ya demuestra identidad [HW] | Débil | H3 (`-Activate`) |
| E | Otra app tiene registrado el transporte (Phone Link/Windows) | Aviso «desempareja antes» [3P] | Allowed (no `DeniedBySystem`) [HW]; Phone Link cerrado [HW] | Poco probable | `observe --lines` + Phone Link cerrado/abierto |
| F | Se necesita `ConnectAsync` antes o a la vez | El tercero en 26300 también obtuvo `False` en `ConnectAsync` | No sabemos si lo probamos con `--connect` | Sin probar | `register --connect` (efecto adicional) |
| G | Restricción específica de iPhone (Apple) | Un hilo (Q&A 5956795, 25H2) dice que los endpoints HFP del iPhone no se exponen [3P, sin respuesta]; Microsoft indica que Apple limita la integración [3P] | El síntoma ocurre antes de cualquier audio; y en 26300 el tercero no usaba necesariamente iPhone | No determinable | Repetir con un teléfono Android (control) — informativo |
| H | `LimitedAccessFeatures` bloquea | `Unavailable` [HW] | Win10 funciona con `Unavailable` [3P]; descartado forjar token | No usable legítimamente | — |

### 12.6 `AudioRoutingStatus`: qué sabemos

- [SDK] El enum tiene dos valores, `CanRouteToLocalDevice` y `CannotRouteToLocalDevice`; existe el evento `AudioRoutingStatusChanged`. Según el orden de metadatos, `CanRouteToLocalDevice` sería el valor 0 (por defecto) [INF].
- [DOC/INF] Describe si Windows permitiría enrutar el audio de una llamada al dispositivo local; es una propiedad del transporte, no una prueba de audio funcional.
- [HW] Lecturas hasta ahora: etapa 2 (antes de registrar; estado del enlace no registrado en esa ejecución): `CanRoute…`; etapa 3 (iPhone conectado por Classic, 10 s tras `RegisterApp`): `CannotRoute…`.
- **Las lecturas NO son comparables:** momento distinto, estado de enlace no registrado junto a la lectura, y en etapa 3 una sola lectura. El cambio podría deberse al enlace HFP/Classic, a que el transporte pasó de «sin sesión» a «con sesión», o a otra condición. Que `RegisterApp` no tuvo efecto [HW] hace **improbable** que el cambio dependa del registro, pero no está demostrado.
- No demostrado: qué valor toma durante una llamada real, si `CannotRoute` en reposo es normal, y si el valor cambia al conectar/desconectar HFP.
- Mejora: línea de tiempo con marcas de tiempo (antes / justo después / cada 500 ms) con `IsRegistered`, `AudioRoutingStatus`, enlace Classic, llamadas, más suscripción a `AudioRoutingStatusChanged`, y un analizador que clasifica el cambio (constante / con la conexión / tras RegisterApp / sin explicación) y advierte que sin registro no demuestra nada. No cambia la ruta de audio ni fuerza conexiones.

### 12.7 Mejoras de diagnóstico (solo lectura, con pruebas)

| Mejora | Por qué |
|---|---|
| `stage3 observe --seconds N [--interval MS] [--lines]` | Línea de tiempo comparable; separa enlace, registro y audio |
| Eventos `AudioRoutingStatusChanged`/`InBandRingingEnabledChanged` con marca de tiempo | Instante exacto de cada cambio |
| Presencia de API (`ApiInformation`): contratos `CallsPhoneContract` v1–v8, `PhoneCall`, `AudioRoutingStatus`, `RegisterAppForUser` | Descarta que la build no implemente algo |
| Propiedades del transporte (`PhoneLineTransportDevice.Connected` —no documentada—, `Present`, `InterfaceEnabled`, `Connected`) | Estado del transporte según Windows; sin valores sensibles |
| Líneas visibles sin registrar (`--lines`) | La documentación dice que el registro es necesario para verlas: se comprueba |
| Errores con HRESULT por paso, `n/d` ≠ False | Sin falsos negativos |
| `-Activate` (H3) con archivo de órdenes que solo admite etapas 1–3 | Probar activación normal sin instalar nada |
| Scripts H1 (`Install/Uninstall-SignedDevPackage.ps1`) | Preparados, **no ejecutados**: tocan el almacén de confianza |

Pruebas automatizadas: **60/60** correctas (`dotnet test CallProbe.Logic.Tests`); compilación de `CallProbe` sin errores. Nuevas: análisis de línea de tiempo (7), política del archivo de órdenes (8).

### 12.8 Alternativas si la API de Windows resulta inutilizable

Requisitos: sin app en iOS, sin Phone Link, sin drivers propios, libre, control de llamadas desde WinMac, audio bidireccional por micrófono/auriculares del PC.

| Alternativa | Señalización HFP | Audio SCO bidireccional | Veredicto |
|---|---|---|---|
| RFCOMM propio (`Windows.Devices.Bluetooth.Rfcomm`) | **Viable en teoría [INF]**: abrir canal HFP del iPhone y hablar AT (ATA, AT+CHUP, +CLIP). Conflicto: Windows ya mantiene el HFP del PC; no hay evidencia de que la API deje conectar a un servicio ya atendido por el controlador | **No**: SCO/eSCO lo gestiona el controlador del perfil en el kernel (`BthHFEnum`, modo kernel [3P]); no existe API pública de modo usuario para abrir SCO [sin documentación localizada] | No viable por audio |
| Windows `PhoneLine` (esta investigación) | Bloqueado hoy | Sí (lo enruta Windows) | Bloqueado por `RegisterApp` |
| Controlador propio / WinUSB+BTstack | — | — | Descartado por el requisito |
| ANCS (notificaciones del iPhone por BLE) | Avisos y acciones de llamada **no demostrados** en este equipo | No | Solo señalización; no cumple audio |
| Phone Link | Funciona (Microsoft) | Funciona | Excluido por requisito |
| Raspberry/ESP32 como puente HFP | Viable técnicamente [3P] | Sí | Requiere hardware extra y rompe «sin dependencias» |

**Ninguna alternativa cumple a la vez señalización y audio bidireccional sin driver dentro de la pila de Windows: no se presenta ninguna como viable.** (Corrección de §15: existe un tercero que lo consigue sustituyendo la pila de Windows por una en modo usuario sobre el WinUSB *inbox*; ver §15.5, ruta E.) Ruta demostrable pendiente: que `PhoneLineTransportDevice` registre.

### 12.9 Próxima prueba mínima recomendada

1. **Prueba P1 (solo lectura, sin riesgo, no requiere autorización especial):** observación de 90 s con el iPhone conectado y luego, durante la observación, el usuario apaga y enciende Bluetooth del iPhone (acción propia del usuario) para correlacionar `AudioRoutingStatus` con el enlace. Responde 12.6. Comando:
   ```powershell
   .\package\Run-Probe.ps1 stage3 observe -ExtraArgs '--seconds 90 --lines'
   ```
   Resultado esperado: línea de tiempo y veredicto del analizador; `Líneas Bluetooth visibles` = 0 confirmaría que sin registro no hay línea.
2. **Prueba P2 (H3, activación normal; bajo riesgo; ejecuta el mismo `RegisterApp` ya conocido y se revierte solo, así que requiere tu autorización explícita):**
   ```powershell
   .\package\Run-Probe.ps1 stage3 register -Activate
   ```
   Se escribe `REGISTRAR` en la consola que se abre. Esperado: `IsRegistered` True → H3 confirmada; False → H3 descartada.
3. **Prueba P3 (H1, firmada; modifica el almacén de confianza; solo si P2 falla; requiere autorización explícita y administrador):** quitar el paquete suelto, `Install-SignedDevPackage.ps1 -BuildDir <publish>`, repetir `stage3 register`, y revertir con `Uninstall-SignedDevPackage.ps1`. Riesgo: el certificado autofirmado queda de confianza hasta desinstalar.

**Criterios:**
- **Continuar** si en P2 o P3 `IsRegistered()==True` **y** aparece una línea telefónica accesible (`PhoneLine` del transporte). Siguiente paso: prueba con llamada real autorizada (sección 11.6).
- **Detener la vía nativa** si P2 y P3 dan `IsRegistered()==False` con `Allowed`, iPhone conectado y revertido limpio: el bloqueo es de plataforma (hipótesis A). Entonces solo quedaría un control informativo (Thy Phone/otro teléfono) y documentar que, con los requisitos actuales, la función no es viable en esta compilación. No se pasa a bypass, token ni driver.
- Si P1 muestra que `AudioRoutingStatus` cambia con el enlace, se documenta como diagnóstico (no desbloquea nada).

## 13. P3 — MSIX firmado: preparación segura (NO ejecutada por la investigación)

Etiquetas como en §12. **Nada de esto se ha ejecutado en tu PC ni en mi entorno** (aquí solo se corrieron las pruebas simuladas de 13.8). **Corrección posterior (§14):** esas pruebas fallaron en Windows real por un defecto de ámbito en la propia batería; el recuento «165» de 13.8 queda sustituido por el de §14.4. Un certificado autofirmado **no equivale** a una firma de Microsoft Store: P3 solo responde si *un paquete firmado y de confianza local* cambia el resultado; un fallo de P3 no descarta la Store y un éxito no la implica.

### 13.1 Estado de partida (informado por el usuario; los registros de P1/P2 no los he visto) [HW]
Build 10.0.26300.9711 · iPhone por Classic · Phone Link cerrado · paquete suelto (`None`) · `RequestAccessAsync=Allowed` · `RegisterApp()` retorna, `IsRegistered()=False`. P1: detecta desconexión/reconexión sin cambios de registro. P2 (activación por `shell:AppsFolder`): `RegisterApp()` en 24 ms y `IsRegistered()` False 10 s → **H3 (activación) queda descartada [HW]**. Sin llamadas reales ni audio. SDK `10.0.28000.0` instalado (el script elige el SDK más nuevo con `makeappx` y `signtool` en `x64`).

### 13.2 Auditoría: qué estaba mal y cómo quedó

| # | Requisito | Problema en la versión anterior | Corrección |
|---|---|---|---|
| 1 | Auditar | Un solo script monolítico sin pruebas; sin estado | `P3.Common.ps1` (lógica) + dos scripts finos + `Test-P3.ps1` |
| 2 | Huella antes de lo que puede fallar | La huella solo existía en memoria; si fallaba la importación o la instalación se perdía | `state.json` (escritura atómica) en `%LOCALAPPDATA%\CallProbeP3\<id>\` se crea **antes** de crear nada; la huella se guarda justo al crear el certificado, y la *intención* de importar/instalar se guarda **antes** de ejecutarlo. `state.json` nunca se borra |
| 3 | Borrar solo por huella exacta | El desinstalador borraba por `Subject` | Solo `Cert:\<almacén>\<huella-de-40-hex>` de **esa** ejecución; antes de borrar se exige huella válida y sujeto coincidente. Una huella vacía, corta, con comodín o `CN=…` se rechaza y no se borra nada |
| 4 | Recuperación ante fallos parciales | Ninguna | Cualquier excepción tras crear el certificado dispara `Invoke-P3Rollback` automático; si no termina, estado `cleanup-incomplete` y mensaje con huella y comando |
| 5 | No borrar paquetes ajenos / parar ante identidad inesperada | `Remove-AppxPackage` por nombre | Precondición: si ya existe **cualquier** `WinMac.Research.CallProbe` (el suelto, uno de Store…) se detiene sin crear nada. Al limpiar solo se quita un paquete cuyo `PackageFullName`, versión y publisher son los anotados por esa ejecución; si no, **se detiene todo** y lo explica |
| 6 | No tocar otras ejecuciones | Directorio fijo en `%TEMP%` | Carpeta por ejecución; solo se vacía esa (y solo si tiene la forma `CallProbeP3\<id>`); una ejecución pendiente bloquea una nueva |
| 7 | Publisher = sujeto | No se comprobaba | Se compara manifiesto de origen, certificado, manifiesto del MSIX **empaquetado** y manifiesto **instalado** |
| 8 | Identidad y capacidades | No se comprobaba | Conjunto exacto `bluetooth, phoneCall, phoneLineTransportManagement, runFullTrust`, nombre `WinMac.Research.CallProbe`, ejecutable `CallProbe.exe`; se lee del MSIX y del paquete instalado. Además `signtool verify /pa /v` debe mostrar **la huella de esta ejecución** y Windows no puede registrar el paquete con firma `None` |
| 9 | Sin tocar protecciones | — | No se toca Defender, Smart App Control ni directivas. Si Windows bloquea el paquete, la instalación falla y se revierte |
| 10 | Sin tocar Bluetooth/audio/apps | — | Los scripts no llaman a nada de Bluetooth, audio ni WinMac |

Extras: certificado de **30 días** y **clave no exportable**; confirmación escrita `INSTALAR-P3` con la lista de cambios; el nombre del informe de `Run-Probe.ps1` ahora incluye `SignatureKind` (`con-paquete-None` vs `con-paquete-<firmado>`) para no mezclar resultados.

### 13.3 Cambios persistentes que hace P3 (todos reversibles)

| Dónde | Qué | Quién lo quita |
|---|---|---|
| `%LOCALAPPDATA%\CallProbeP3\<id>\` | copia de la app (~cientos de MB si es self-contained), `.cer`, `.msix`, `state.json` | el desinstalador deja solo `state.json` (registro) |
| `Cert:\CurrentUser\My` | certificado `CN=WinMacResearch` (30 días, clave no exportable) | el desinstalador, por huella |
| `Cert:\LocalMachine\TrustedPeople` | parte pública del mismo certificado | el desinstalador, por huella |
| Paquete MSIX `WinMac.Research.CallProbe` (usuario actual) | app firmada con sus datos en `%LOCALAPPDATA%\Packages\…` | el desinstalador (los **informes** están en `Documentos\CallProbe` y se conservan) |
| Registro de eventos de Windows | entradas normales de instalación | no aplica |

No cambia: Bluetooth, emparejamiento, audio, Defender, Smart App Control, directivas, Modo desarrollador. Durante la prueba, `stage3 register` mantiene su propio diario (`Documentos\CallProbe\stage3-journal.json`) y revierte solo.

### 13.4 Riesgos residuales (los que no se pueden eliminar)
1. **Confianza local:** mientras el certificado esté en `TrustedPeople`, cualquier MSIX firmado con *esa* clave se instala sin aviso. La clave es no exportable y vive en tu perfil, caduca en 30 días y la prueba dura minutos; aun así existe esa ventana. Se mitiga limpiando inmediatamente.
2. **Control de aplicaciones:** si Smart App Control u otra directiva bloquea un paquete autofirmado, la instalación fallará (se revierte). No se desactiva nada; en ese caso P3 no es ejecutable en este PC sin cambiar protecciones y **se detiene**. [HIP: no sé si está activo en tu equipo.]
3. **Mismo usuario:** hay que elevar con la **misma cuenta** que usa la app (en un PC con una sola cuenta administradora es lo normal). Si elevas con otra cuenta, el paquete se instalaría para ella y no para ti; en ese caso no continúes.
4. **Identidad compartida:** el paquete firmado tiene la misma identidad que el suelto, por lo que **no pueden coexistir**; hay que quitar el suelto primero (paso 2 de 13.6). Tras P3 puedes volver a `Register-DevPackage.ps1`.
5. Si el equipo se apaga a mitad: ver 13.5.
Ninguno de estos riesgos exige detener la preparación; el 1 y el 2 los decide el usuario.

### 13.5 Si P3 falla a mitad

| Punto del fallo | Qué queda | Qué pasa |
|---|---|---|
| Precondición (paquete existente, SDK ausente, ejecución pendiente, manifiesto incorrecto, no confirmas) | nada | Se detiene sin crear nada |
| Tras crear el certificado, antes de exportar / empaquetar / firmar | cert en `My` | Rollback automático lo quita por huella |
| Durante o tras importar a `TrustedPeople` | cert en `My` y quizá en `TrustedPeople` | Rollback intenta ambos almacenes (la intención quedó anotada antes de importar) |
| Durante `Add-AppxPackage` (incluida instalación a medias) | posible paquete | Se quita **solo** si es exactamente el esperado; si es otro, se detiene y avisa sin tocar el cert |
| Corte de luz / cierre de la consola | `state.json` en estado `in-progress` | Bloquea nuevas ejecuciones; `Uninstall-SignedDevPackage.ps1` lo completa leyendo la huella del estado |
| El rollback tampoco puede terminar | estado `cleanup-incomplete` | Mensaje `P3-FALLO-INCOMPLETO` con huella y `-RunId`; los comandos manuales por huella exacta están en 13.6 |

### 13.6 Comandos exactos (nadie los ha ejecutado aún)

**0. Ver el estado actual (Windows PowerShell normal):**
```powershell
cd <repo>\research\iphone-calls
git pull origin research/iphone-calls
.\package\Run-Probe.ps1 stage3 status
```
Debe decir `IsRegistered() False` y diario `clean`. Si no, para y revisa.

**1. Publicar la versión actual:**
```powershell
dotnet publish CallProbe -c Release -r win-x64 --self-contained -o publish
```

**2. Quitar el paquete suelto (misma identidad; usuario normal):**
```powershell
.\package\Unregister-DevPackage.ps1
```

**3. Probar los scripts sin tocar nada (opcional, en pwsh o PowerShell 5.1):**
```powershell
.\package\Test-P3.ps1
```

**4. Instalar (consola de Windows PowerShell 5.1 «Ejecutar como administrador», misma cuenta):**
```powershell
cd <repo>\research\iphone-calls
.\package\Install-SignedDevPackage.ps1 -BuildDir .\publish
```
Lista los cambios y pide escribir `INSTALAR-P3`. **Apunta la huella** que imprime.

**5. Comprobar la instalación (usuario normal):**
```powershell
Get-AppxPackage WinMac.Research.CallProbe | Format-List Name, Publisher, Version, SignatureKind, PackageFullName
```
Esperado: `Publisher: CN=WinMacResearch`, `SignatureKind` distinto de `None` (supongo `Developer` [HIP]).

**6. Ejecutar la prueba (usuario normal, iPhone conectado y desbloqueado, Phone Link cerrado, sin llamadas):**
```powershell
.\package\Run-Probe.ps1 stage2
.\package\Run-Probe.ps1 stage3 status
.\package\Run-Probe.ps1 stage3 register
```
Escribe `REGISTRAR` cuando lo pida; revierte solo al terminar.

**7. Limpiar siempre al terminar, sea cual sea el resultado (administrador, PowerShell 5.1):**
```powershell
.\package\Uninstall-SignedDevPackage.ps1
```
Si hay varias ejecuciones pendientes pide `-RunId <id>`.

**8. Verificar la limpieza (sustituye `<HUELLA>` por la que apuntaste):**
```powershell
Get-AppxPackage WinMac.Research.CallProbe
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq '<HUELLA>'
Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq '<HUELLA>'
```
Las tres no deben devolver nada. Último recurso manual, **solo por huella exacta** (administrador): `Remove-Item Cert:\LocalMachine\TrustedPeople\<HUELLA>` y `Remove-Item Cert:\CurrentUser\My\<HUELLA> -DeleteKey`. No borres certificados por nombre.

**9. Volver al estado anterior (opcional):** `.\package\Register-DevPackage.ps1 -BuildDir .\publish`.

### 13.7 Cómo interpretar el resultado de P3
| Resultado de `stage3 register` | Lectura | Siguiente |
|---|---|---|
| `IsRegistered=True` **y** línea telefónica visible | H1/C confirmada para un paquete firmado de desarrollo (no implica Store) | Informar; después, y solo con tu autorización, plan de llamada real |
| `IsRegistered=True`, sin línea | Registro aceptado; línea no expuesta | Informar; no avanzar a llamadas |
| `IsRegistered=False` con `Allowed` | La firma local no basta en esta build: se refuerza la hipótesis A (restricción de plataforma). **No** prueba nada sobre Store | Limpiar (paso 7). Decidir el cierre del proyecto con los criterios de §12.9 |
| Error de instalación (SAC/directiva/0x800B…) | P3 no ejecutable sin cambiar protecciones | Se detiene; no se desactiva nada |

### 13.8 Pruebas ejecutadas (en mi entorno, simuladas)
- `pwsh 7.6.9`: `Test-P3.ps1` → **165 comprobaciones correctas, 0 fallidas**. Cubren: sintaxis de todos los `.ps1`, rutas/carga de `P3.Common.ps1`, validador de huella (vacía, corta, comodín, con espacios, `CN=…`), coincidencia publisher/sujeto, identidad y capacidades exactas (falta una, sobra una, nombre distinto), instalación completa, desinstalación exacta, fallo simulado en cada paso (`export`, `pack`, `sign`, `import`, `import` a medias, `verify`, `add`, `add` a medias), manifiesto empaquetado distinto, huella de `.cer` distinta, firma `None`, paquete previo (Store/None/Developer) sin crear nada, cancelación, ejecución pendiente que bloquea, varias pendientes con y sin `-RunId`, estado ilegible, huella inválida (no se borra por `Subject`), mismo hash con otro sujeto, carpeta con forma inesperada, tabla de decisión de paquetes, y que los certificados *señuelo* con el mismo `Subject` y los archivos de otras ejecuciones quedan intactos.
- Comprobación de que las pruebas detectan errores: quitando la verificación del sujeto o la guarda «no intentó instalar», fallan.
- `dotnet test CallProbe.Logic.Tests` → ver commit; `dotnet build CallProbe` sin errores.
- **No probado (requiere Windows real):** `New-SelfSignedCertificate`, `makeappx`, `signtool`, `Add-AppxPackage`, almacenes `Cert:\`, el valor real de `SignatureKind`, y el comportamiento de Smart App Control. Esas operaciones solo se ejercitan en tu ejecución; por eso cada una tiene su comprobación y reversión.

## 14. Corrección de P3: el fallo `Hex40` al ejecutar `Test-P3.ps1` en Windows real

**Resultado en el PC del usuario (commit `1722484`):** `P3: pruebas` → `P3-FALLO (revertido por completo): El término "Hex40" no se reconoce…` en `P3.Common.ps1:226`. P3 **no** había pasado sus propias pruebas; mi entrega anterior lo presentó como «165 correctas» porque solo lo ejecuté con `pwsh -File`, el único modo en que el defecto no aparece. Ese informe era incompleto en ese punto.

### 14.1 Causa raíz [reproducida]
- `Hex40` (y `New-Cert`, `New-Pkg`) son **funciones auxiliares definidas en `Test-P3.ps1`**, es decir, en el *ámbito del script*. Las operaciones simuladas (`NewCert`, `ImportTrust`, `Verify`, `AddPackage`) son scriptblocks creados con `.GetNewClosure()`, que se ejecutan en un **módulo dinámico**: allí los *comandos* se resuelven desde el ámbito **global**, no desde el del script, y de las *variables* solo se capturan las **locales** de la función que lo creó.
- Con `pwsh -File Test-P3.ps1` el script corre en el ámbito global, así que las funciones sí se ven (por eso lo di por bueno). Con `.\package\Test-P3.ps1`, `& script` o `pwsh -Command ./Test-P3.ps1` el script tiene su propio ámbito y `Hex40` desaparece. **Reproducido en pwsh 7.6.9 (Linux)** con `-Command ./Test-P3.ps1`, sin necesitar Windows.
- La misma causa afectaba a las variables `$TP` y `$MY` (rutas de almacenes) usadas dentro de los closures: habrían dado `Index operation failed; the array index evaluated to null` en cuanto se corrigiera `Hex40`. Se detectó al reproducir.
- **Dónde estaba el fallo:** solo en la **prueba simulada** (`Test-P3.ps1`). **El instalador real no se ve afectado:** `New-P3RealOps` usa únicamente cmdlets de Windows, ninguna función auxiliar del script de pruebas (comprobado por inspección y por el análisis AST de 14.3).
- Diferencia 5.1 / 7: el mecanismo es el mismo en ambas; lo que cambia es *cómo se invoca* el script, no la versión.

### 14.2 Defectos adicionales encontrados al auditar
1. **Mensaje engañoso:** `P3-FALLO (revertido por completo)` se emitía en cuanto el rollback devolvía «completo», sin volver a consultar el sistema; en este caso ni siquiera se había creado nada que revertir. **Corregido:** tras la limpieza se hace una **verificación independiente** (`Get-P3Residuals`: paquete, huella en ambos almacenes, contenido de la carpeta). Solo entonces el mensaje dice `limpieza verificada` y cita la huella y los almacenes comprobados; si no se creó nada, dice `no se había creado nada que revertir`; si queda cualquier residuo o la limpieza lanza una excepción, `P3-FALLO-INCOMPLETO` con los residuos, el estado `cleanup-incomplete` (que bloquea nuevas ejecuciones) y el comando para continuar. Matiz honesto: «verificada» significa *según lo que el script puede consultar*, no una auditoría del sistema entero.
2. **Codificación:** los `.ps1` estaban en UTF-8 **sin BOM** con caracteres no ASCII (`→`, `—`, `«`). Windows PowerShell 5.1 los lee como ANSI y varios de esos bytes (p. ej. `0x92`, `0x94`) se convierten en comillas tipográficas que PowerShell trata como comillas reales: riesgo de errores de análisis. **Corregido:** todos los `.ps1` del paquete se guardan con BOM UTF-8.
3. La batería terminaba en la primera excepción inesperada y ocultaba el resto. **Corregido:** cada bloque corre dentro de `Section`, que registra la excepción como fallo y continúa.

### 14.3 Pruebas de regresión añadidas (`Test-P3.ps1`)
| Prueba | Qué detecta |
|---|---|
| Análisis AST de los closures simulados | cualquier llamada a una función definida en el propio script de pruebas, o uso de una variable del ámbito del script que el closure no captura |
| `NewCert` simulado invocado directamente | el camino exacto que falló |
| La batería completa se relanza como `& script` en un proceso nuevo | el modo de invocación que usa un usuario (y que `-File` ocultaba) |
| BOM UTF-8 y lectura simulada como Windows-1252 | caracteres no ASCII sin BOM / comillas tipográficas |
| Mensajes de recuperación | `RemoveCert` ineficaz → `INCOMPLETO` con residuos y sin decir «verificada»; limpieza que lanza → `INCOMPLETO` y pendiente; fallo previo a crear nada → lo dice; fallo normal → cita huella y almacenes |
Comprobación de que detectan el error: reintroduciendo la llamada a `Hex40` en un closure, quitando la copia local de `$TP/$MY`, o quitando el BOM de `P3.Common.ps1`, la batería falla.

### 14.4 Resultados (mi entorno, sin Windows)
| Ejecución | Resultado |
|---|---|
| `pwsh 7.6.9 -File Test-P3.ps1` | 182 correctas, 0 fallidas |
| `pwsh -Command ./Test-P3.ps1` (modo que reproducía el fallo) | 182 correctas, 0 fallidas |
| `pwsh -Command "& ./Test-P3.ps1"` | 182 correctas, 0 fallidas |
| `dotnet test CallProbe.Logic.Tests` | 60/60 |
**Windows PowerShell 5.1: NO verificado aquí** (no hay 5.1 disponible en este entorno). Lo cubren las pruebas de ámbito y de codificación, pero la confirmación real es la ejecución del usuario (14.5). No se hizo ninguna instalación ni se tocó ningún certificado.

### 14.5 Cómo repetir la validación en tu PC (no instala nada, no toca certificados)
```powershell
cd <repo>\research\iphone-calls
git pull origin research/iphone-calls
$PSVersionTable.PSVersion
.\package\Test-P3.ps1
```
Esperado, última línea: `P3: 182 comprobaciones correctas, 0 fallidas` (sin líneas `FALLA`). Repite también en la otra versión de PowerShell que tengas:
```powershell
pwsh -NoProfile -File .\package\Test-P3.ps1
powershell.exe -NoProfile -File .\package\Test-P3.ps1
```
Si alguna línea dice `FALLA`, envíame esa línea completa y `$PSVersionTable.PSVersion`; **no instales P3**. Solo cuando la batería pase en tu Windows PowerShell 5.1 sigue valiendo el procedimiento de §13.6.

## 15. Investigación final de viabilidad (2026-11-04)

**Alcance:** solo lectura documental y de código público. No se instaló nada, no se crearon certificados, no se ejecutó nada en el PC del usuario, no se implementó ningún prototipo. Evidencia de hardware propia: P1, P2, P3 (informada por el usuario; los registros de P3 no los he visto).

**Escala de evidencia:** **DEMOSTRADO** = documentación oficial inequívoca o implementación reproducible con evidencia · **INDICIO** = parcial o indirecta · **HIPÓTESIS** = plausible, sin verificar · **NO DISPONIBLE** = falta evidencia. «Autor» = lo afirma el autor del proyecto con sus propios registros, sin reproducción independiente.

**Fuentes primarias leídas en esta iteración** (texto íntegro, vía los repositorios oficiales `MicrosoftDocs` en GitHub, porque `learn.microsoft.com` sigue bloqueado en la sesión):
- `winrt-api` @ `4c1e9164` (2026-10-20): [`PhoneLineTransportDevice`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel.calls/phonelinetransportdevice.md), [`RegisterApp`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel.calls/phonelinetransportdevice_registerapp_1450770636.md), [`RequestAccessAsync`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel.calls/phonelinetransportdevice_requestaccessasync_380675631.md), [`AudioRoutingStatus`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel.calls/phonelinetransportdevice_audioroutingstatus.md), [`LimitedAccessFeatures`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel/limitedaccessfeatures.md), [`TryUnlockFeature`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel/limitedaccessfeatures_tryunlockfeature_180193416.md), [`LimitedAccessFeatureStatus`](https://github.com/MicrosoftDocs/winrt-api/blob/4c1e9164521785f82dc63ab57a5ee4644f9d1888/windows.applicationmodel/limitedaccessfeaturestatus.md).
- `windows-uwp` @ `a3812316` (2026-10-14): [App capability declarations](https://github.com/MicrosoftDocs/windows-uwp/blob/a3812316d9d6678be4e526ffb4b1c4f3958d956d/uwp/packaging/app-capability-declarations.md).
- `windows-driver-docs` @ `b1f6aa5` (2026-10-29): [Introduction to Bluetooth profile drivers](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-profile-drivers-overview.md), [Bluetooth Classic audio](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-classic-audio.md), [HFP bypass audio streaming](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/audio/bluetooth-hfp-bypass-audio-streaming.md).

### 15.1 `PhoneLineTransportDevice`: qué dice la documentación y qué no

| Afirmación | Clase | Fuente / nota |
|---|---|---|
| `RegisterApp()` «registra la app con Windows para el transporte»; el registro es necesario para ver la `PhoneLine`; es por usuario; persiste hasta `UnregisterApp` o hasta que se quite el dispositivo | **DEMOSTRADO** | `RegisterApp` (arriba) |
| `RegisterApp`, `RequestAccessAsync` y `ConnectAsync` requieren la capacidad `phoneLineTransportManagement` (`AudioRoutingStatus` y `PhoneCall`, la capacidad `phoneCall`) | **DEMOSTRADO** | páginas de cada método; lista de capacidades, l. 258 |
| `IsRegistered()` devuelve el estado de registro de la app para el usuario activo | **DEMOSTRADO** | `IsRegistered` |
| `RegisterApp` es `void`: no hay valor de retorno ni código que indique que el registro no se materializó | **DEMOSTRADO** | firma en el SDK (`evidence/sdk-10.0.26100.57-…`) |
| **Ninguna** de las páginas de la clase menciona Limited Access Features, ni otra condición adicional para que el registro surta efecto | **DEMOSTRADO (ausencia)** | las seis páginas de la clase leídas (clase, `RegisterApp`, `IsRegistered`, `RequestAccessAsync`, `ConnectAsync`, `AudioRoutingStatus`) |
| `phoneLineTransportManagement` figura en la lista de capacidades **restringidas**, **sin** la advertencia «casi nunca se aprueba» que sí llevan otras | **DEMOSTRADO** | l. 258 vs. l. 180–196 |
| Las apps con capacidades restringidas **pueden instalarse por carga lateral sin aprobación**; la aprobación solo se exige al enviar a la Store (formulario en Partner Center, revisión caso a caso) | **DEMOSTRADO** | l. 126–129, 151–159 |
| Los Limited Access Features son funciones de Windows que «requieren aprobación específica»; para usarlas «se necesita permiso explícito de Microsoft»: ID de función y token mediante el [formulario de solicitud LAF](https://go.microsoft.com/fwlink/?linkid=2271232&clcid=0x409) | **DEMOSTRADO** | `LimitedAccessFeatures` |
| `TryUnlockFeature` → `Unavailable` = «el usuario no tiene acceso»; `AvailableWithoutToken` = «la función ya no está limitada»; `Unknown` = ID no encontrado | **DEMOSTRADO** | `LimitedAccessFeatureStatus` |
| En este PC (26300) el ID `…phonelinetransportdevice_v1` devuelve `Unavailable` (con token vacío) | **DEMOSTRADO** (P2, P3) | por el punto anterior significa que sigue limitado y que no tenemos acceso, no que el ID sea inválido |
| Que `RegisterApp` dependa de ese LAF | **NO DISPONIBLE** | la documentación no lo dice; en Win10 funciona con `Unavailable` (Klangbruecke FINDINGS §12); en 26300 no |
| El ID `phonelinetransportdevice_v1` existe y es un LAF real | **INDICIO** | proviene de código de terceros (Sefirah `CallingFeatureUtils.cs`) y del registro de una máquina (Klangbruecke §2); la documentación oficial no lo nombra |
| Una app **pública de terceros** puede usar legítimamente la API hoy | **NO DISPONIBLE** | la API es pública y documentada; los permisos que la hacen efectiva en Windows 11 actual no están documentados |
| Existe evidencia de funcionamiento en versiones actuales de Windows 11 | **NO DISPONIBLE** | ver 15.3: todos los reportes de Win11 son negativos o usan vías rechazadas |

**No se confunde presencia con permiso:** `PhoneCall` (v6) y `AudioRoutingStatus` existen en esta build [SDK] y `RequestAccessAsync` dio `Allowed` en P2 [HW], y aun así `IsRegistered()` nunca pasó a `True` [HW].

### 15.2 Paquete suelto, MSIX de desarrollo y Store

| Modalidad | Resultado en este PC [HW] | Lo que dice la documentación | Lo que NO sabemos |
|---|---|---|---|
| Sin paquete | `DeniedBySystem` | (sin identidad no hay capacidad) | — |
| Paquete suelto, firma `None` (P2) | `Allowed`; `RegisterApp` sin error (24–79 ms); `IsRegistered=False` | sideload permitido sin aprobación | por qué Allowed y por qué el registro no se materializa |
| MSIX firmado, `Developer` (P3) | **`DeniedBySystem`**; `IsRegistered=False` | idem | por qué pasa de Allowed a Denied |
| Store | **NO PROBADO** | requiere aprobación de la capacidad restringida en Partner Center | **todo**: si se aprobaría, y si la aprobación haría efectivo el registro |

Observación honesta: lo que el usuario esperaba (la firma «ayuda») ocurrió al revés en `RequestAccessAsync`. P3 no demuestra que la Store sea rechazada ni que la firma de desarrollo sea la causa del `DeniedBySystem` (puede deberse a otra variable que cambió entre P2 y P3; ver 15.6).

### 15.3 Evidencia pública sobre funcionamiento en Windows 11

| Fuente | Fecha / build | Qué demuestra | Qué NO demuestra |
|---|---|---|---|
| [MyPhone #26](https://github.com/BestOwl/MyPhone/issues/26) (hilo; leído por resumen de la herramienta de lectura web) | 22H2 (22621) → comentarios 2023–abr-2026 | `DeniedBySystem` / `RegisterApp` sin efecto o `UnauthorizedAccess` en Win11; funciona en Win10 21H2 y LTSC 2021; Microsoft contestó de forma contradictoria («se arreglará», «resuelto», «se quitó de la lista LAF, no necesita token»); un comentario de oct-2024 consiguió pasar con `BypassRegistration=1` (HKLM) **y** desbloqueo LAF falsificado (vías rechazadas); abr-2026: Phone Link omite el LAF desde la 22000 y hay que desemparejar si otra app ya registró | funcionamiento sin saltarse un control; funciones de llamada |
| [Microsoft Q&A 6041632](https://learn.microsoft.com/en-us/answers/questions/6041632/phonelinetransportdevice-registerapp-has-no-effect) (vista por buscador; sin respuesta aceptada) | build 26300 | **El mismo síntoma que P2**: `Allowed`, `RegisterApp` sin error, `IsRegistered=False`, `ConnectAsync=False` | causa ni solución |
| [Klangbruecke](https://github.com/MYSTRAVIL/klangbruecke) @ `2cb8e00` (2026-09-29), `docs/FINDINGS.md` §12, §15.1 | Win10 19045, **Android** | sí funciona el registro firmado y autofirmado con `RequestAccessAsync` previo; LAF `Unavailable` y aun así registra; en Win11 26200 (prueba de un tercero) `DeniedBySystem` para apps instaladas por carga lateral | iPhone; Windows 11 |
| [BluetoothHFP](https://github.com/saymonsh/BluetoothHFP/tree/dbd737b) @ `dbd737b` (2026-09-30), mitad Flutter | Win11 (build no indicada), **iPhone** | su README dice textualmente que si Windows deniega el acceso al transporte «incluso con el paquete instalado y los permisos activados», es «una limitación real del SO/API» y la app lo informa; remite a Phone Link | que el registro funcione en ningún equipo |
| [Sefirah](https://github.com/shrimqy/Sefirah) @ `6db524a` (2026-10-17) | Android | en Win11 **omite** el desbloqueo LAF (`ShouldSkipLAFCheck() => Major >= 22000`) y llama a `RequestAccessAsync`→`RegisterApp`; para Win10 deriva un token con una clave de comunidad (**vía rechazada**) | que en Win11 actual registre (no revisé sus issues) |
| Thy Phone, CallCenter (Store, cerrados) | sin fecha/versión verificable | **INDICIO** de que apps de Store funcionan (comentarios en MyPhone #26) | build, teléfono (¿iPhone?), audio bidireccional, ni mecanismo |

Conclusión de 15.3: **no existe evidencia verificable de un registro efectivo de una app propia de terceros en Windows 11 actual sin saltarse un control.** Hay un reporte independiente del mismo síntoma en la misma build.

### 15.4 Bluetooth HFP y audio bidireccional con APIs públicas de Windows

Distinciones (aplicadas a lo que sabemos con evidencia):

| Nivel | Qué es | ¿Accesible para una app convencional? | Clase |
|---|---|---|---|
| Bluetooth Classic conectado (ACL) | enlace con el teléfono | solo lectura de estado (`BluetoothDevice.ConnectionStatus`) | **DEMOSTRADO** (P1: detecta desconexión/reconexión) |
| Perfil HFP conectado (SLC por RFCOMM, comandos AT) | señalización | sí en teoría con `Windows.Devices.Bluetooth.Rfcomm` (MyPhone.Demo hace el SLC completo); Windows además mantiene su propio HFP | **INDICIO**; coexistencia con el HFP de Windows: **NO DISPONIBLE** |
| Control de llamada con AT (`ATA`, `AT+CHUP`, `+CLIP`) | señalización | misma vía RFCOMM; no demostrado contra iPhone en Windows | **NO DISPONIBLE** |
| Audio SCO/eSCO | voz | **No desde modo usuario**: Microsoft documenta los DDI de SCO como interfaces de modo kernel para *profile drivers*; Windows trae el driver HFP que gestiona la conexión SCO | **DEMOSTRADO** (docs `bluetooth-profile-drivers-overview`, `bluetooth-hfp-bypass-audio-streaming`) |
| Micrófono y parlantes del PC | endpoints WASAPI | sí (API pública) | **DEMOSTRADO** |
| Que Windows enrute el audio de la llamada a los endpoints de comunicaciones | lo hace Windows tras el registro | `PhoneCall.ChangeAudioDeviceAsync(LocalDevice)` es la API pública; requiere registro | **INDICIO** (Klangbruecke, Android/Win10); **bloqueado** aquí |

Roles: Windows como **Hands-Free** del iPhone (teléfono = Audio Gateway) es el rol que `PhoneLineTransportDevice` administra; la documentación de audio clásico de Windows solo describe el caso inverso o accesorios (headsets) y no menciona este rol [ausencia]. **Conclusión:** con APIs públicas de usuario solo se llega al audio mediante `PhoneLineTransportDevice`/`PhoneCall`; no hay camino alternativo de usuario a SCO.

Restricción específica del iPhone: **no se encontró ninguna** que impida el rol Hands-Free. Evidencia en contra de esa hipótesis: [iphonebridge](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/spike/RESULTS.md) (`078e9a4`, 2026-10-21, **iPhone 16 Pro Max / iOS 26.5**, Linux con BlueZ+oFono+PipeWire): identificación de llamada con `LineIdentification`, `Answer()` con audio SCO al portátil (mSBC), `Hangup()`, y 3/3 llamadas salientes — **autor**, registros en `spike/RESULTS.md`. Es la prueba más clara de que el iPhone sí acepta un PC como manos libres; el cuello de botella está en Windows, no en iOS (**INDICIO fuerte**, no reproducido por nosotros, no es Windows).

### 15.5 Alternativas, proyectos y matriz

**Proyectos revisados (commit, fecha, qué está demostrado):**

| Proyecto | Commit / fecha | Código | Demostrado | Anunciado/no verificado | Audio bidireccional real | Limitaciones |
|---|---|---|---|---|---|---|
| MyPhone (MIT) | `dbd0ede` · 2022-12-23 | sí | SLC HFP; registro y llamadas en Win10 | iOS en el README | **No** (autor: «falló SCO») | abandonado; roto en Win11 (#26, #27) |
| Klangbruecke (sin licencia) | `2cb8e00` · 2026-09-29 | sí | Win10: registro, A2DP, enrutado de llamada (Android) | — | Win10/Android, autor | Win11 no registra; sin iPhone |
| Sefirah (GPL-3.0) | `6db524a` · 2026-10-17 | sí | llamadas por `PhoneLine` con Android | — | no acreditado | Win10 usa token no autorizado; Android |
| BluetoothHFP (mitad Flutter) | `dbd737b` · 2026-09-30 | sí | transporte denegado ⇒ lo informa | — | no por la vía Windows | remite a Phone Link |
| **BluetoothHFP `native-poc`** | `dbd737b` | sí | **iPhone 12 Pro Max**: pila BTstack en modo usuario sobre el radio Intel AX201 reasignado al WinUSB *inbox*; SLC, eSCO, CVSD/mSBC, A2DP; «el usuario confirmó audio en ambos sentidos» (`evidence/ACCEPTANCE.md`, `hfp-sco-success.txt`) | control desde PC (`hfp_hf_answer_incoming_call`, `hfp_hf_terminate_call` en `q7/q7_bridge.c`) existe en el código, no está en los registros | **Sí, por el autor, 1 equipo** | **toma el radio completo** (Windows deja de usar Bluetooth mientras funciona); hardware único (`VID_8087&PID_0026`); operaciones de controlador con permisos de administrador; copia de seguridad y reversión propias; BTstack: «uso no comercial personal; el comercial exige licencia» (su `THIRD-PARTY.md`); falta carga en frío de firmware |
| iphonebridge → BlueFerry (GPL-2.0) | `078e9a4` · 2026-10-21 | sí | iPhone/iOS 26.5, llamadas completas en **Linux** | — | Sí, autor, Linux | no es Windows |
| bthaven | página no disponible (404) | — | — | según un resultado de búsqueda, marca el transporte de llamadas y el micrófono como «gated/denied» | — | **NO DISPONIBLE**: no pude verificar |
| Phone Link / Intel Unison | cerrados | no | Microsoft documenta llamadas por Bluetooth con iPhone; Unison exige app en iOS y ciertos equipos Intel | — | no verificable | excluidos por el proyecto |

**Matriz comparativa** (clase de evidencia por función; ✘ = demostrado imposible por esa vía; vacío = no aplicable):

| Alternativa | Identificar | Contestar | Rechazar | Finalizar | Audio → PC | Mic PC → iPhone | Cumple restricciones | Veredicto |
|---|---|---|---|---|---|---|---|---|
| A. `PhoneLineTransportDevice`, carga lateral/dev (P2, P3) | API existe [SDK]; sin línea | NO DISP. | NO DISP. | NO DISP. | NO DISP. | NO DISP. | sí | **bloqueado en nuestro hardware (3 intentos)** |
| B. Igual, con autorización LAF de Microsoft | NO DISP. | NO DISP. | NO DISP. | NO DISP. | NO DISP. | NO DISP. | sí (proceso oficial) | **C: no probado** |
| C. Igual, publicada en la Store (capacidad aprobada) | NO DISP. | NO DISP. | NO DISP. | NO DISP. | NO DISP. | NO DISP. | sí, con cuenta Partner Center | **C: hipótesis** |
| D. RFCOMM/HFP propio en modo usuario | INDICIO | INDICIO | INDICIO | INDICIO | **✘** (SCO solo kernel) | **✘** | sí | **D: no viable** (falta audio) |
| E. WinUSB *inbox* + BTstack (`native-poc`) | autor | INDICIO | INDICIO | INDICIO | **autor** | **autor** | **parcial/NO**: toma el radio, sólo ese hardware, licencia BTstack | demostrado por terceros; **exige cambiar restricciones** |
| F. ANCS desde Windows (GATT cliente) | INDICIO (Apple spec + iphonebridge en Linux) | INDICIO (spec: acción positiva «puede contestar») | INDICIO (acción negativa «puede rechazar») | NO DISP. | **✘** | **✘** | sí | **D como solución**: solo control, sin audio |
| G. App complementaria iOS (APIs públicas de Apple) | parcial (`CXCallObserver`, sin controlar) | **✘** | **✘** | **✘** (solo llamadas propias) | **✘** | **✘** | contradice «sin app» | **D**: iOS no entrega llamadas celulares a apps |
| H. Phone Link / Unison | sí | sí | sí | sí | sí | sí | **✘ (excluidos)** | fuera de alcance |
| I. Linux (oFono) | sí (autor) | sí | sí | sí | sí | sí | no es Windows | informativo |

Notas de la matriz: (F) ANCS requiere además un enlace LE enlazado y autorizado en el iPhone; que Windows lo pueda hacer como cliente GATT contra el iPhone: **NO DISPONIBLE** (no encontré proyecto en Windows). El texto de Apple dice que la acción positiva sobre una llamada entrante «puede contestarla» y la negativa «puede rechazarla», y que lo decide el proveedor, no el consumidor ([especificación ANCS](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Specification/Specification.html)). (G) Las fuentes de Apple que pude leer son foros y la página de `CXCallObserver` (solo título); el reparto «observar sí, controlar no, audio no» es **INDICIO** (foros de desarrolladores de Apple y conocimiento general), no un texto oficial inequívoco.

### 15.6 P2 frente a P3: qué hipótesis son compatibles

Datos [HW]: sin paquete → `DeniedBySystem`; suelto sin firma → `Allowed` + registro sin efecto; firmado `Developer` → `DeniedBySystem` + registro sin efecto; en las tres, LAF `Unavailable`. Variables que cambiaron entre P2 y P3: tipo de firma, instalación vía `Add-AppxPackage -Path` (en lugar de `-Register`), certificado de confianza en `TrustedPeople`, ubicación de instalación, **y el estado previo** (la misma identidad de paquete, `…_qb22tsgw2hqnw`, ya había llamado a `RegisterApp` varias veces antes de P3).

| # | Hipótesis | ¿Compatible con P2 y P3? | Clase |
|---|---|---|---|
| H-a | Windows 11 26300 ya no concede el registro a apps de terceros (política de plataforma), y `Allowed` en P2 es solo el resultado de una comprobación de acceso al dispositivo que no implica registro | **Sí** a ambos; explica el mismo síntoma de Q&A 6041632 | **HIPÓTESIS** (la más simple) |
| H-b | La comprobación de acceso trata distinto el paquete en modo desarrollador (suelto = «de prueba») y el paquete firmado/instalado (sujeto a política de capacidades restringidas); en ambos, el registro real exige una autorización que no tenemos (LAF) | **Sí**; explica `Allowed`→`Denied` | **HIPÓTESIS** |
| H-c | Estado persistente por identidad (PFN): las llamadas previas a `RegisterApp` dejaron un registro/decisión asociada al mismo PFN que provocó `DeniedBySystem` en P3 | Compatible; no explica por sí sola que P2 no registre | **HIPÓTESIS** (se apoya en el aviso de MyPhone #26 sobre registros previos) |
| H-d | La firma de desarrollo o el almacén de confianza hacen que Windows no considere el editor «de confianza» para esta capacidad | Compatible con P3; no con P2 (P2 no tiene firma y fue `Allowed`) | **HIPÓTESIS** débil |
| H-e | El rol Hands-Free del PC no se ofrece al iPhone (restricción de Apple) | Contradicha en lo general por iphonebridge/BluetoothHFP (iPhone acepta PC como HF); no explica `DeniedBySystem` | **INDICIO en contra** |
| H-f | Activación del proceso (`Invoke-CommandInDesktopPackage`) | **Descartada** por P2 con `shell:AppsFolder` | **DEMOSTRADO** (P2) |
| — | **Causa exacta** | — | **DESCONOCIDA** |

Observaciones **no invasivas** que podrían distinguir (sin proponer pruebas nuevas ni cambios): HRESULT/eventos de los registros de Windows en el instante de `RequestAccessAsync` y `RegisterApp` (Bluetooth, AppModel y acceso a dispositivos; los nombres exactos de los canales no están verificados aquí); el estado del conmutador por app en *Privacidad y seguridad › Llamadas telefónicas* para cada paquete; si el mismo síntoma ocurre con una app de Store conocida (observación pasiva de su comportamiento, no de nuestro código). Ninguna de las tres decide por sí sola entre H-a y H-b.

### 15.7 Veredicto, preguntas y confianza

**Clasificación** (se separa C de D: faltar evidencia no es imposibilidad):

| Ruta | Categoría |
|---|---|
| `PhoneLineTransportDevice` con carga lateral / desarrollo | bloqueada en nuestro hardware; sin evidencia pública de éxito en Windows 11 actual → **C** (no se puede declarar D: no se probó con autorización LAF ni en la Store) |
| `PhoneLineTransportDevice` con LAF autorizado o Store | **C — NO DEMOSTRADO** (posible, sin ninguna evidencia) |
| HFP propio por RFCOMM (APIs públicas de usuario) | **D** (audio SCO exige modo kernel, demostrado por la documentación) |
| ANCS (control sin audio) y app complementaria iOS | **D** como solución completa |
| WinUSB *inbox* + BTstack | demostrado por un tercero en un equipo; **incompatible con el espíritu de las restricciones** (toma el radio, un hardware, licencia) → solo viable **cambiando restricciones** |
| **Objetivo completo bajo las restricciones actuales** | **C — NO DEMOSTRADO** (sin ruta reproducible; ninguna ruta de usuario pública cumple por sí sola) |

**Respuestas directas**
1. **¿Podemos implementarlo hoy en WinMac sin Phone Link?** **No.** No hay una ruta reproducible y compatible con las restricciones; la única API pública completa (`PhoneLineTransportDevice`/`PhoneCall`) no se registra en este PC en tres configuraciones.
2. **¿Audio bidireccional real con APIs públicas?** **No demostrado.** En principio solo vía `PhoneCall.ChangeAudioDeviceAsync`, que depende de un registro que no se obtiene. Con RFCOMM/Winsock es imposible (SCO es de kernel).
3. **¿La Store lo resolvería?** **Es solo una hipótesis.** La documentación solo dice que la Store exige aprobar la capacidad restringida; no dice que eso haga efectivo `RegisterApp`. Aplicaciones de Store que lo logran existen según terceros, sin build ni teléfono verificables.
4. **¿Qué restricciones habría que cambiar?** Una de estas (cada una con coste real): (a) aceptar un proceso de autorización de Microsoft (solicitud LAF y/o Store con cuenta de Partner Center), de resultado incierto; (b) aceptar que la función tome el radio Bluetooth (WinUSB *inbox* + BTstack), solo en hardware validado, con permisos de administrador y una licencia de BTstack que hay que revisar — es la única ruta con audio demostrado, por un tercero; (c) aceptar Phone Link/Unison (excluido). Una app en iOS no resuelve nada.
5. **¿Seguir?** **Detener el desarrollo de esta función en WinMac.** No hay ningún paso técnico sensato que dar en el código con la información actual. Como única acción no técnica y acotada: si el responsable lo considera valioso, decidir explícitamente si invierte en (a); de no hacerlo, cerrar la investigación. No se recomienda continuar solo porque algo sea imaginable.

**Confianza:** *alta* (≈85 %) en que hoy no hay ruta reproducible bajo las restricciones y en D para RFCOMM/ANCS/iOS; *media* (≈60 %) en que la causa sea una política de plataforma (H-a/H-b) en lugar de algo corregible en nuestro empaquetado; *baja* (≈30 %) sobre si LAF o Store desbloquearían el registro; la causa exacta es **desconocida**.

**Límites de esta lectura:** `learn.microsoft.com` bloqueado (leí el mismo texto desde `MicrosoftDocs`); hilos de MyPhone y Microsoft Q&A leídos a través de herramientas de resumen; no reproduje ninguna de las demostraciones de terceros; no verifiqué si las condiciones de BTstack se aplican a un proyecto de código abierto.

## 16. Fase 4 — Arquitecturas alternativas para HFP en Windows (2026-11-04)

**Alcance:** lectura de documentación oficial y de código público. No se instaló nada, no se crearon certificados, no se tocó Bluetooth ni el PC del usuario, no se escribió ningún prototipo. Los fragmentos de código de terceros se **estudian**, no se copian.

**Escala de evidencia** (la pedida en esta fase): **DEMOSTRADO** = prueba reproducible o código/registro verificable · **DOCUMENTADO** = lo dice documentación oficial · **INFERIDO** = deducido de la arquitectura · **HIPÓTESIS** = pendiente de prueba · **NO DEMOSTRADO** · **INCOMPATIBLE** = choca con una restricción. «Autor» = lo afirma el autor sin reproducción independiente.

**Clasificación de arquitecturas:** A viable y demostrada · B plausible, pendiente de prueba · C no demostrada · D inviable bajo nuestras restricciones.

**Fuentes que no pude leer:** especificación HFP del Bluetooth SIG (`www.bluetooth.com` no resuelve en esta sesión), `learn.microsoft.com` (leí el mismo texto desde `MicrosoftDocs`), y el código de oFono (`git.kernel.org` bloqueado; lo que digo de oFono sale del código de iphonebridge y de PipeWire que lo consume). Para permitirlo hay que añadir esos dominios a la red de la sesión.

### 16.1 Resumen ejecutivo y decisión

**¿Existe una arquitectura técnicamente defendible para llamadas del iPhone en Windows 11, con audio bidireccional, usando el Bluetooth existente y sin reemplazar su controlador?** → **NO DEMOSTRADA.** Ninguna de las arquitecturas estudiadas llega a A o B. Ninguna ruta de usuario pública puede transportar SCO; la única interfaz documentada para SCO es de **modo kernel**; y la ruta nativa (`PhoneLineTransportDevice`) sigue bloqueada en el registro por una causa **desconocida**.

Lo que se aprendió en esta fase:
1. **Windows ya implementa el rol Hands-Free completo** (señalización HFP, SCO y puntos finales de audio) en componentes propios (stack Bluetooth + driver HFP de modo kernel). Una app solo puede **registrarse** para ese rol vía `PhoneLineTransportDevice`; no puede «añadir» audio por su cuenta. (DOCUMENTADO + INDICIO de Klangbruecke en Win10.)
2. **BluetoothHFP necesita WinUSB por construcción, no por comodidad:** BTstack es una pila *host* completa que habla HCI; solo puede haber un host por controlador, y en Windows el controlador lo posee `BTHUSB`. La dependencia es **fundamental** para esa arquitectura (INFERIDO con evidencia de código y del propio autor). Además **no tiene licencia** (el repositorio no trae `LICENSE`) y BTstack prohíbe uso comercial (`LICENSE`, cláusula 4) → **INCOMPATIBLE** con un proyecto de código abierto.
3. **iphonebridge no implementa HFP:** delega todo en **oFono + BlueZ + PipeWire**, y se apoya en tres cosas que Windows no ofrece a una app: `RegisterProfile` de BlueZ (el HFP lo implementa una app), **sockets SCO de modo usuario** (`AF_BLUETOOTH/BTPROTO_SCO`) y una pila de audio que consume el descriptor SCO. No hay equivalente público en Windows.
4. **Un driver propio firmado** es *plausible en el papel* (existen BRB de SCO documentados para *profile drivers*) pero **no hay evidencia de coexistencia** con el driver HFP de Windows y el camino de firma para público general es costoso y, en su variante barata, «solo pruebas».
5. **Experimento mínimo propuesto (E1, solo observación, sin instalar nada):** comprobar, durante una llamada real, si el iPhone ofrece el PC como destino de audio y si Windows expone/activa los puntos finales «Hands-Free» del iPhone **sin ninguna app registrada**. Es la única observación nueva que distingue entre «el rol HF de Windows está vivo y solo falla el registro» y «el rol HF no existe para este iPhone en la 26300». Aunque diera positivo, **solo resolvería el audio**, no el control desde Windows.

### 16.2 Arquitectura HFP de Windows (qué es accesible y desde dónde)

| Afirmación | Clase | Fuente |
|---|---|---|
| El stack Bluetooth de Windows tiene componentes de modo usuario (APIs publicadas vía `BthProps`), un servicio (`BthServ`) y componentes de kernel (`BthPort`, `RFCOMM`, `BthEnum`, `BthLEEnum`, `BthUsb`). Los *profile drivers* (incluido el de audio HFP/HSP «incluido desde Windows 8») acceden a SCO a través de `BthPort` | **DOCUMENTADO** | [Bluetooth driver stack](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-driver-stack.md) |
| Un app de usuario llega a RFCOMM **por Winsock**; `BthEnum` crea un PDO por cada **servicio** del dispositivo remoto (salvo los listados en `UnsupportedServices` de `Bth.inf`) → el driver HFP de Windows se engancha por sí solo al servicio HFP del teléfono | **DOCUMENTADO** | misma página |
| Winsock Bluetooth: `AF_BTH`, `SOCK_STREAM`, y **«`BTHPROTO_RFCOMM` es el protocolo soportado»**; no hay protocolo SCO ni L2CAP en la API de sockets (la página es de 2018; no encontré documentación posterior que añada otros) | **DOCUMENTADO** | [Bluetooth and socket](https://github.com/MicrosoftDocs/win32/blob/ca8274d/desktop-src/Bluetooth/bluetooth-and-socket.md) |
| Los **DDI de SCO** (abrir, aceptar, transferir) son de **modo kernel**, para *profile drivers*: `BRB_SCO_OPEN_CHANNEL`, `BRB_SCO_REGISTER_SERVER`, `BRB_SCO_TRANSFER`, enviados con `IOCTL_INTERNAL_BTH_SUBMIT_BRB` | **DOCUMENTADO** | [Introducción a profile drivers](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-profile-drivers-overview.md), [Aceptar SCO](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/accepting-sco-connections-in-a-bluetooth-profile-driver.md), [Cliente SCO](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/creating-a-sco-client-connection-to-a-remote-device.md) (los BRB enlazan a `previous-versions`: documentación heredada) |
| El driver HFP de Windows gestiona la conexión SCO del dispositivo HFP; el driver de audio le envía `IOCTL_BTHHFP_STREAM_OPEN/CLOSE`; ante desconexión remota reintenta por su cuenta. La documentación describe el caso «PC como AG» (auriculares) y no menciona el rol «PC como HF de un teléfono» | **DOCUMENTADO** + ausencia | [HFP device connection](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/audio/hfp-device-connection.md), [HFP DDI IOCTLs](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/audio/bluetooth-hfp-ddi-ioctls.md) |
| Audio clásico: HFP da captura y reproducción **mono**, 8 kHz (CVSD/SBC) o 16 kHz (mSBC); al abrir el micrófono HFP, el estéreo A2DP se descarta | **DOCUMENTADO** | [Bluetooth Classic audio](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-classic-audio.md) |
| Existe una API de usuario documentada que **habilita o deshabilita servicios de un dispositivo remoto** (`BluetoothSetServiceState`, `BLUETOOTH_SERVICE_ENABLE/DISABLE`, por GUID de servicio) | **DOCUMENTADO** | [BluetoothSetServiceState](https://github.com/MicrosoftDocs/sdk-api/blob/656e965/sdk-api-src/content/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate.md) |
| En Windows 10 19045 el PC actúa como **HF** y el teléfono como **AG**: Windows publica un servicio Hands-Free (`Bluetooth\Audio\Hfp\HandsFree`, `RfcommServerChannel=2`, HFP 1.7), aplica `AT+VGM` al micrófono de comunicaciones y **no crea un punto final intermedio**: toma el micrófono/altavoz de comunicaciones predeterminados y los codifica en SCO | **INDICIO** (Win10, Android, un equipo) | Klangbruecke [`FINDINGS.md`](https://github.com/MYSTRAVIL/klangbruecke/blob/2cb8e00/docs/FINDINGS.md) §7 (l. 184-202) y §11 (l. 366-431) |
| Para que el teléfono ofrezca el PC como destino de audio de llamada, **una app debe registrar el rol**; el registro no es opcional | **INDICIO** (Win10/Android; «Android no ofrecía el PC en absoluto antes de registrar») | Klangbruecke §12 (l. 480-530), diagnóstico 3 |
| En Windows 11 25H2 el HFP del iPhone aparece en el Administrador de dispositivos pero los puntos finales de audio **no se activan** | **INDICIO** (un hilo, sin respuesta) | [Microsoft Q&A 5956795](https://learn.microsoft.com/en-ca/answers/questions/5956795/phone-link-initiates-iphone-calls-but-transfer-to) |
| Canal RFCOMM propio contra el AG del teléfono desde modo usuario: SLC completa (`AT+BRSF`, `+BAC`, `+CIND`, `+CMER`…) | **DEMOSTRADO** (código MIT; «llamadas por el SLC, falló SCO») | [MyPhone.Demo `HFP.cs`](https://github.com/BestOwl/MyPhone/blob/dbd0ede/MyPhone.Demo/MyPhone.Demo.HFP/HFP.cs) y su README |
| Coexistencia entre un SLC propio y el HFP de Windows para el mismo teléfono | **NO DEMOSTRADO**; **INFERIDO** que no es posible a la vez, porque el driver HFP de Windows ya se conecta al servicio y el AG suele aceptar un solo HF por dirección (HFP normal; no verificado contra iOS) | — |
| El rol HF de Windows frente a un teléfono es opaco: no figura en la documentación de driver ni de audio, y su registro por apps está controlado por la capacidad restringida | **DOCUMENTADO** (ausencia) + [P1–P3] | §15 |

**Distinción pedida — abrir RFCOMM ≠ transportar SCO/eSCO:**

| Nivel | Usuario sin privilegios | Cómo |
|---|---|---|
| ACL conectado | lectura de estado | `BluetoothDevice.ConnectionStatus` (P1 lo detectó) |
| Canal RFCOMM / SLC HFP | **sí** (si el HFP de Windows no lo ocupa) | Winsock `AF_BTH`/`BTHPROTO_RFCOMM`, DOCUMENTADO |
| Comandos de llamada (`ATA`, `AT+CHUP`, `+CLIP`) | **sí en teoría**, sobre ese RFCOMM | INFERIDO (MyPhone.Demo hace el SLC; las órdenes de llamada no se probaron contra iPhone en Windows) |
| **SCO/eSCO** | **no**: DDI de kernel; sin protocolo en Winsock | DOCUMENTADO |
| Micrófono y altavoz del PC | sí | WASAPI |
| Puente SCO ↔ endpoints | lo hace Windows (driver HFP + audio), solo tras el registro | INDICIO |

### 16.3 Análisis técnico de iphonebridge (código real, `078e9a4`, 2026-10-21, GPL-2.0-or-later)

Repositorio archivado; continúa como BlueFerry (no revisado). Descripción por capas:

| Pregunta | Respuesta (con fuente) | Clase |
|---|---|---|
| ¿Quién implementa HFP? | **oFono**, no el proyecto. El módulo `hfp/ofono_client.py` solo observa `org.ofono` en el bus de sistema: `Manager.ModemAdded`, `VoiceCallManager.CallAdded/Dial/HangupAll`, `VoiceCall.Answer/Hangup/LineIdentification` ([`ofono_client.py` l. 1-21, 156, 231-335](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/src/iphonebridge/hfp/ofono_client.py)) | **DEMOSTRADO** (código) |
| Roles | Linux = **Hands-Free (HF)**; iPhone = **Audio Gateway (AG)** (el módem oFono «hfp» es el iPhone; `bluez5.roles` incluye `hfp_hf`) | DEMOSTRADO |
| Descubrir/conectar | emparejamiento BR/EDR normal con BlueZ; ajusta la **Class of Device a «A/V Hands-Free»** con `btmgmt class 4 8` ([`bluez_setup.py` l. 59-73](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/src/iphonebridge/bluez_setup.py)) porque iOS solo muestra los conmutadores por perfil con esa clase | DEMOSTRADO (autor) |
| Eventos de llamada | señales D-Bus de oFono (`CallAdded`, `PropertyChanged`); número en `LineIdentification` (`+CLIP`), nombre vía PBAP/contactos | DEMOSTRADO (código); en vivo: autor, iPhone 16 Pro Max / iOS 26.5 ([`spike/RESULTS.md` l. 22-57](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/spike/RESULTS.md)) |
| Contestar / colgar / marcar | `Answer()`, `Hangup()`, `Dial()`; oFono las traduce a `ATA`, `AT+CHUP`, `ATD…;` | DEMOSTRADO (código del cliente); traducción AT: INFERIDO (no pude leer oFono) |
| Audio | **SCO** lo abre oFono vía BlueZ y lo entrega a **PipeWire** (backend `ofono`, MIT): el agente `org.ofono.HandsfreeAudioAgent.NewConnection` recibe el **descriptor de un socket SCO** y lo lee/escribe ([`backend-ofono.c` l. 64-66, 484-590](https://github.com/PipeWire/pipewire/blob/acc0f78c148d0eb4e18475368687b87b40cdc51a/spa/plugins/bluez5/backend-ofono.c)). Códec negociado: mSBC | DEMOSTRADO (código) |
| Componentes BlueZ/kernel | `org.bluez.ProfileManager1.RegisterProfile` (UUID HFP; BlueZ solo entrega el descriptor RFCOMM a la app, [doc](https://github.com/bluez/bluez/blob/7a713dc0c1b7550bb16b67a7b0f8298e04a807b8/doc/org.bluez.ProfileManager.rst)); **socket SCO del kernel**, `socket(PF_BLUETOOTH, SOCK_SEQPACKET, BTPROTO_SCO)` ([`sco.c` l. 680-697](https://github.com/torvalds/linux/blob/05441d697f7f9a927264f4058cbbc6a03dfcac9d/net/bluetooth/sco.c#L680); `sco_sock_create` no exige capacidades especiales) | DEMOSTRADO (código) |
| Alternativa sin oFono | PipeWire trae un backend **nativo** (MIT) que implementa el HF en modo usuario sobre BlueZ: `ATA` (l. 1538), `AT+CHUP` (l. 1572), `ATD` (l. 1632), `+CLIP`, `+CIEV` (l. 2112), con `BRSF`/códecs ([`backend-native.c`](https://github.com/PipeWire/pipewire/blob/acc0f78c148d0eb4e18475368687b87b40cdc51a/spa/plugins/bluez5/backend-native.c)) | DEMOSTRADO (código); en vivo no lo ejecuté |
| ANCS | el proyecto lo hace por BLE; **no convive** con el emparejamiento BR/EDR por la misma MAC: necesita `LastUsedBearer=le` escrito en el registro de BlueZ (`systemd/set-le-bearer.sh`), y el autor documenta el «mutex BR/EDR-vs-BLE» ([`RESULTS.md` §5, l. 86-106](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/spike/RESULTS.md)) | DEMOSTRADO (autor) |

**Equivalentes en Windows:**

| Mecanismo Linux | Equivalente público en Windows | Veredicto |
|---|---|---|
| `RegisterProfile` de HFP (la app es el perfil) | **Ninguno público.** `PhoneLineTransportDevice.RegisterApp` es el equivalente funcional, pero bloqueado en P2/P3 | no disponible / bloqueado |
| Socket RFCOMM entregado por el stack | Winsock `AF_BTH/RFCOMM` (sí), pero Windows ya tiene su servicio HFP | parcial |
| **Socket SCO de modo usuario** | **Ninguno.** Kernel-only (DDI BRB) | **sin equivalente conocido** |
| oFono (HFP + máquina de llamadas) | Rol HF interno de Windows + `PhoneLine`/`PhoneCall` | gated por registro |
| PipeWire consume el SCO | Windows lo hace internamente (driver HFP + audio) | solo a través del registro |
| `btmgmt class` (Class of Device) | **Ninguno público documentado** | sin equivalente |
| `LastUsedBearer=le` / ANCS | Cliente GATT (`Windows.Devices.Bluetooth.GenericAttributeProfile`) existe, pero sin evidencia de ANCS contra iPhone desde Windows ni forma de elegir portador | NO DEMOSTRADO |

**Conclusión:** iphonebridge prueba que **el iPhone sí acepta un PC como Hands-Free con control y audio** (INDICIO fuerte, un autor, Linux) y que el cuello de botella es la **ausencia de sockets SCO de usuario y de un registro de perfil** en Windows. **No se puede portar el código** (oFono/BlueZ/PipeWire/D-Bus no existen en Windows; GPL-2.0+ además).

### 16.4 Análisis técnico de BluetoothHFP (`dbd737b`, 2026-09-30)

El repositorio tiene **un solo commit**, ningún archivo `LICENSE` en el árbol y dos partes:

**Parte Flutter (pila de Windows):** `AudioPlaybackConnection` (A2DP) + `PhoneLineTransportDevice`. Su README admite que si Windows deniega el acceso «incluso con el paquete instalado y los permisos activados» es «una limitación real del SO/API» y remite a Phone Link ([README](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/README.md), pasos 4-5). Corrobora P2/P3. Un experimento de registro `HFP-EXPERIMENT.md` (cambio en el registro) se cita pero no está en el árbol.

**`native-poc/` (pila propia):**

| Pregunta | Respuesta |
|---|---|
| Acceso al adaptador | Reasigna el **Intel AX201 (`USB\VID_8087&PID_0026`, instancia exacta)** del driver Intel al **WinUSB incluido en Windows** y abre el dispositivo con `WinUsb_*` ([`ax201_transport.cpp`](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/bluetooth/ax201_transport.cpp), [`Set-BluetoothDriver.ps1`](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/tools/Set-BluetoothDriver.ps1)) |
| HFP | **BTstack** (BlueKitchen) como HF: `hci_init(hci_transport_usb_instance())`, `hfp_hf_init`, `hfp_hf_establish_service_level_connection`, `hfp_hf_answer_incoming_call`, `hfp_hf_terminate_call` ([`q7_bridge.c` l. 738-778, 502-507](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/q7/q7_bridge.c)) |
| Audio | `ENABLE_SCO_OVER_HCI` ([`btstack_config.h` l. 14](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/q7/btstack_config.h)): paquetes SCO por el endpoint **isócrono USB** del controlador; CVSD/mSBC en BTstack; WASAPI para micrófono/altavoz |
| Hardware | Intel AX201 concreto; carga de firmware en frío **no implementada**; probado en **un** equipo ([`ACCEPTANCE.md`](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/evidence/ACCEPTANCE.md): iPhone 12 Pro Max, «el usuario confirmó audio en ambos sentidos», mSBC) |
| Efecto en el sistema | «Los periféricos Bluetooth de Windows no están disponibles mientras WinUSB posee el controlador» (README); la guía del puente Q7 (hebreo) dice que **el Bluetooth de Windows queda apagado mientras funciona** y que se desactivan los dispositivos de audio Bluetooth de Windows; **copia las claves de enlace de Windows** a su carpeta ([`Set-BluetoothDriver.ps1` cabecera](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/tools/Set-BluetoothDriver.ps1)) |
| Límites propios | el controlador de ese equipo **se negó a abrir un segundo canal de audio** (teléfono + auriculares a la vez) en el puente Q7 (`GUIDE-HE.md`) |

**¿WinUSB es fundamental o una decisión de implementación?** **Fundamental para esa arquitectura (INFERIDO, con evidencia):** BTstack es un *host*; el host habla HCI; solo puede existir uno por controlador. `COMPATIBILITY.md` registra que `WinUsb_Initialize` falla con error 50 mientras `BTHUSB` es el driver. No hay en Windows un canal HCI de usuario que permita compartir el controlador (conocimiento general, no verificado aquí: en Linux el canal HCI «de usuario» también es exclusivo). No es una decisión de comodidad; **cualquier pila host propia exige poseer el controlador**, lo que choca con tus restricciones.

**¿Qué se puede reutilizar sin sustituir el driver?** Solo **ideas**, no código:
- La secuencia SLC/AT de HFP — ya está en MIT en `MyPhone.Demo` y en PipeWire (`backend-native.c`).
- La gestión de códec (CVSD/mSBC) y el deriva de reloj — válidas **solo si** se tuviera SCO, que es lo que falta.
- **Licencia:** BTstack (`LICENSE`, cláusula 4) exige que «cualquier redistribución, uso o modificación se haga únicamente para beneficio personal y no con fines comerciales ni de lucro» ([texto](https://github.com/bluekitchen/btstack/blob/9edb5d82ea6e/LICENSE)); es una restricción de campo de uso, **incompatible con distribución abierta general**. Sus dependencias (FDK AAC) tienen aviso propio. El repo de BluetoothHFP **no concede licencia** alguna → su código no se puede reutilizar.
- Hay además un riesgo de seguridad de diseño: leer las claves de enlace de Windows y apagar el Bluetooth del sistema. **INCOMPATIBLE** con «no modificar el sistema de manera insegura o difícil de revertir».

### 16.5 Comparación de arquitecturas

| Arq. | Descripción | Clase | Por qué |
|---|---|---|---|
| **A** | `PhoneLineTransportDevice`/`PhoneCall` + stack Windows (ya probado) | **C** | P1-P3 sin registro; causa desconocida; sin explicación verificable de cómo superar el bloqueo (§15). |
| **B** | HFP propio en modo usuario sobre Winsock RFCOMM, audio por el stack | **D** | Control posible en teoría (SLC + AT); **audio imposible** en modo usuario (solo `BTHPROTO_RFCOMM`; SCO = kernel). La variante «yo llevo la señalización y el driver HFP de Windows lleva el SCO» exigiría dos dueños del mismo HFP: INFERIDO incompatible. |
| **C** | Componentes nativos de Windows sin Phone Link ni registro (rol HF del sistema; el usuario elige el PC como audio en el iPhone) | **C** | No hay API de control; solo audio y solo si el sistema publica y activa el rol HF sin app (INDICIO en contra: Klangbruecke §12, Q&A 5956795). **Insuficiente por sí sola** (sin control desde Windows). El experimento E1 mide justo esto. |
| **D** | Componente/driver propio **firmado** (perfil de kernel con `BRB_SCO_*`; RFCOMM en modo usuario o en el driver) | **C** | Existe el DDI documentado, pero **no hay evidencia de coexistencia** con el driver HFP de Windows ni de que BthEnum/BthPort admitan dos consumidores SCO para el mismo dispositivo; firma: ver 16.6. Todo el diseño sería de cero, en kernel. |
| **E1** | Pila host propia (BTstack/WinUSB) — BluetoothHFP | **D** | **Demostrada por un tercero**, pero toma el controlador, hardware único, sin licencia, BTstack no comercial, claves de enlace copiadas. Incumple varias restricciones. |
| **E2** | Control por ANCS (BLE) + audio por el rol HF nativo (C) | **C** | ANCS permite «contestar/rechazar» según Apple (acciones positiva/negativa) pero no hay prueba en Windows y el mutex BR/EDR-vs-BLE de iphonebridge exige elegir portador (BlueZ); depende de C para el audio. |
| **E3** | LE Audio / Telephone Bearer Service | **C** (NO DISPONIBLE) | Windows documenta BAP/VCP/MICP/TMAP en LE Audio, no TBS/CCP; no encontré evidencia de que iOS exponga TBS a terceros. |
| **E4** | Sistema Linux (VM/WSL/segundo equipo) o hardware puente | **D** | Exige un controlador exclusivo (passthrough) o hardware adicional. |
| **E5** | Audio por cable USB-C | **D** | Hardware/cable adicional; BluetoothHFP indica que un cable de carga/datos no expone los endpoints necesarios. |
| **E6** | App complementaria iOS | **D** | iOS no entrega llamadas celulares a apps (§15). |

**Ninguna arquitectura recibe A ni B.**

### 16.6 Obstáculos técnicos y de distribución

| Obstáculo | Detalle | Clase |
|---|---|---|
| Sin SCO en modo usuario | Winsock solo RFCOMM; DDI SCO de kernel | DOCUMENTADO |
| Un host por controlador | Una pila propia exige poseer el radio | INFERIDO (+ evidencia de BluetoothHFP) |
| Rol HF de Windows opaco y con registro restringido | Registro bloqueado en 26300; causa desconocida | DEMOSTRADO (P1-P3) + desconocido |
| Driver HFP de Windows ya se engancha al servicio HFP del teléfono | Competencia por el mismo SLC | INFERIDO (`BthEnum` crea el PDO; `BluetoothSetServiceState` es la palanca de usuario documentada para apagarlo por dispositivo) |
| Firma de un driver propio | Se necesita Hardware Developer Program y **certificado EV**. La firma por *attestation* es, según Microsoft, «**solo para pruebas**» y **no se puede publicar en Windows Update para el público**; el camino para el público es **HLK/WHCP**. | DOCUMENTADO ([offerings](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/dashboard/driver-signing-offerings.md), [attestation](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/dashboard/code-signing-attestation.md)) |
| Instalación de un driver | requiere administrador y un instalador de driver (INF); un MSIX no instala controladores (conocimiento general, **no verificado aquí**) | INFERIDO |
| Mantenimiento de un driver de kernel | Riesgo de bloqueos del sistema, compatibilidad por cada build, y la documentación BRB está en «previous-versions» | INFERIDO |
| Licencias | BTstack no comercial; BluetoothHFP sin licencia; iphonebridge GPL-2.0+; oFono/BlueZ GPL; PipeWire MIT; MyPhone MIT | DEMOSTRADO |
| Hardware | BluetoothHFP: un único chipset | DEMOSTRADO |
| Class of Device | iOS usa la clase «A/V Hands-Free» para ofrecer perfiles; Windows no expone API pública para fijarla | NO DEMOSTRADO para HFP; DEMOSTRADO para los conmutadores MAP/PBAP en Linux |

### 16.7 Matriz de capacidades

(Clase de evidencia por capacidad; ✘ = imposible por esa vía según documentación o diseño.)

| Arq. | Detección | Identificación | Control | Audio entrante | Audio saliente | Coexistencia | Distribución |
|---|---|---|---|---|---|---|---|
| A. PhoneLine | NO DEMOSTRADO (bloqueado) | NO DEMOSTRADO | NO DEMOSTRADO | NO DEMOSTRADO | NO DEMOSTRADO | sí (stack propio) | buena (MSIX) si funcionara |
| B. HFP usuario | INFERIDO | INFERIDO | INFERIDO | **✘** | **✘** | parcial (apagar HFP del teléfono) | buena |
| C. Nativo | ✘ (sin API) | ✘ | ✘ (solo en el iPhone) | NO DEMOSTRADO | NO DEMOSTRADO | sí | buena |
| D. Driver firmado | NO DEMOSTRADO | NO DEMOSTRADO | NO DEMOSTRADO | NO DEMOSTRADO (DDI existe) | NO DEMOSTRADO | NO DEMOSTRADO | **mala** (EV, WHCP, instalador) |
| E1. BTstack/WinUSB | autor | autor | INFERIDO (código) | autor | autor | **✘** (apaga Windows BT) | **✘** (hardware, licencia) |
| E2. ANCS + C | INFERIDO | INFERIDO | INFERIDO (Apple) | = C | = C | sí | media |
| E3. LE Audio/TBS | NO DISP. | NO DISP. | NO DISP. | NO DISP. | NO DISP. | — | — |
| Ref.: Linux (oFono) | autor | autor | autor | autor | autor | sí | no es Windows |

**Ninguna arquitectura cumple a la vez las siete columnas con evidencia.** No se considera viable una que solo controle.

### 16.8 Evidencia verificable (índice)

| Tema | Enlace |
|---|---|
| Arquitectura del stack | [bluetooth-driver-stack.md](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-driver-stack.md) |
| SCO kernel | [bluetooth-profile-drivers-overview.md](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-profile-drivers-overview.md), [accepting-sco…](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/accepting-sco-connections-in-a-bluetooth-profile-driver.md), [creating-a-sco-client…](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/creating-a-sco-client-connection-to-a-remote-device.md) |
| Driver HFP/IOCTL | [hfp-device-connection.md](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/audio/hfp-device-connection.md), [bluetooth-hfp-ddi-ioctls.md](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/audio/bluetooth-hfp-ddi-ioctls.md), [bypass](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/audio/bluetooth-hfp-bypass-audio-streaming.md) |
| Winsock solo RFCOMM | [bluetooth-and-socket.md](https://github.com/MicrosoftDocs/win32/blob/ca8274d/desktop-src/Bluetooth/bluetooth-and-socket.md) |
| Servicios por dispositivo | [BluetoothSetServiceState](https://github.com/MicrosoftDocs/sdk-api/blob/656e965/sdk-api-src/content/bluetoothapis/nf-bluetoothapis-bluetoothsetservicestate.md) |
| Audio clásico, LE Audio | [classic-audio](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-classic-audio.md), [le-audio](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/bluetooth/bluetooth-low-energy-audio.md) |
| Firma de drivers | [offerings](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/dashboard/driver-signing-offerings.md), [attestation](https://github.com/MicrosoftDocs/windows-driver-docs/blob/b1f6aa5/windows-driver-docs-pr/dashboard/code-signing-attestation.md) |
| Linux SCO / perfil | [sco.c](https://github.com/torvalds/linux/blob/05441d697f7f9a927264f4058cbbc6a03dfcac9d/net/bluetooth/sco.c#L680), [ProfileManager](https://github.com/bluez/bluez/blob/7a713dc0c1b7550bb16b67a7b0f8298e04a807b8/doc/org.bluez.ProfileManager.rst) |
| PipeWire HFP (MIT) | [backend-ofono.c](https://github.com/PipeWire/pipewire/blob/acc0f78c148d0eb4e18475368687b87b40cdc51a/spa/plugins/bluez5/backend-ofono.c), [backend-native.c](https://github.com/PipeWire/pipewire/blob/acc0f78c148d0eb4e18475368687b87b40cdc51a/spa/plugins/bluez5/backend-native.c) |
| iphonebridge | [ofono_client.py](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/src/iphonebridge/hfp/ofono_client.py), [RESULTS.md](https://github.com/gabrielmeir53/iphonebridge/blob/078e9a4/spike/RESULTS.md) |
| BluetoothHFP | [README](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/README.md), [native-poc/README](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/README.md), [COMPATIBILITY](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/COMPATIBILITY.md), [q7_bridge.c](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/q7/q7_bridge.c), [THIRD-PARTY](https://github.com/saymonsh/BluetoothHFP/blob/dbd737b/native-poc/THIRD-PARTY.md) |
| BTstack licencia | [LICENSE](https://github.com/bluekitchen/btstack/blob/9edb5d82ea6e/LICENSE) |
| Windows HF en Win10 | [Klangbruecke FINDINGS](https://github.com/MYSTRAVIL/klangbruecke/blob/2cb8e00/docs/FINDINGS.md) |
| SLC en modo usuario | [MyPhone.Demo HFP.cs](https://github.com/BestOwl/MyPhone/blob/dbd0ede/MyPhone.Demo/MyPhone.Demo.HFP/HFP.cs) |
| ANCS (Apple) | [especificación](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Specification/Specification.html) |
| iPhone/HFP en Win11 25H2 | [Q&A 5956795](https://learn.microsoft.com/en-ca/answers/questions/5956795/phone-link-initiates-iphone-calls-but-transfer-to) |

### 16.9 Hipótesis nuevas y experimento mínimo

| # | Hipótesis | Evidencia | ¿Experimento? |
|---|---|---|---|
| H1 | **El rol HF de Windows, sin ninguna app registrada, ya está o no está disponible para este iPhone:** el iPhone ofrece (o no) el PC como destino de audio de llamada y Windows crea/activa (o no) los puntos finales «Hands-Free» del iPhone | Klangbruecke §12 (Win10: sin registro el teléfono no ofrecía el PC); Q&A 5956795 (Win11 25H2: endpoints inactivos); P1 `AudioRoutingStatus=CannotRoute…` (sin llamada, poco informativo) | **Sí → E1** |
| H2 | Se puede liberar el SLC del iPhone con `BluetoothSetServiceState` para que una app lo use por RFCOMM | API documentada | **No**: aporta solo control (sin SCO), modifica el estado Bluetooth del usuario; no cambia el veredicto |
| H3 | Un *profile driver* firmado puede coexistir con el driver HFP de Windows | BRB documentados | **No**: exigiría escribir un driver; fuera de esta fase y sin evidencia previa |
| H4 | El iPhone expone TBS/CCP (LE Audio) a un cliente Windows | ninguna | **No**: NO DISPONIBLE |

**E1 — pre-registro del experimento (no ejecutado; requiere tu autorización explícita):**
- **Qué queremos demostrar:** si, con el iPhone conectado por Bluetooth Classic y **sin ninguna app de investigación instalada ni registrada** (estado actual del PC: paquete de pruebas desinstalado y limpio; basta confirmar con `Get-AppxPackage WinMac.Research.CallProbe` que no devuelve nada), el iPhone ofrece el PC en su menú de audio de llamada y Windows activa un punto final «Hands-Free» del iPhone que transporta voz.
- **Evidencia que lo respalda:** las del cuadro anterior (H1).
- **Procedimiento mínimo:** (1) antes de la llamada, listar en PowerShell los dispositivos de audio con «Hands-Free» y su estado (`Get-PnpDevice -Class AudioEndpoint`; solo lectura); (2) recibir una **llamada real** en el iPhone; (3) en el iPhone abrir el menú de audio de la llamada y anotar si el PC aparece; si aparece, seleccionarlo (acción del usuario en su propio teléfono); (4) repetir el listado del punto 1 y anotar si algún endpoint pasa a *Active*, y si se oye al interlocutor / lo oye el interlocutor mientras hay micrófono y auriculares del PC como dispositivos de comunicaciones predeterminados; (5) volver a seleccionar el iPhone en el menú de audio y colgar.
- **Resultado que confirmaría:** el PC aparece y el audio fluye en ambos sentidos → el rol HF nativo está vivo sin app; el hueco se reduce a *control desde Windows* (E2).
- **Resultado que refutaría:** el PC **no aparece** → el rol HF solo se publica tras el registro de una app autorizada (la puerta es la misma que ya está bloqueada) → las vías nativas pasan a **D**. Aparece pero **no hay endpoints activos / sin voz** → coincide con Q&A 5956795: el rol HF de la 26300 no es usable para este iPhone.
- **Riesgos:** ninguno para el sistema (no se instala, registra ni modifica nada). Una llamada real y un posible cambio de ruta de audio del teléfono; si el menú de audio deja la llamada en el PC sin voz, basta volver a seleccionar el iPhone.
- **Reversión:** volver a seleccionar el iPhone como ruta de audio; no hay estado en Windows que revertir.
- **Qué aporta que P1, P2 y P3 no dieron:** esas pruebas midieron **registro y permisos de la API**, nunca la **ruta de voz nativa en una llamada real**. E1 separa «Windows no sabe hablar HFP con este iPhone» de «Windows sí, pero no deja a mi app registrarse».
- **Límite:** incluso con resultado positivo, solo habría audio; el control (contestar/rechazar/colgar) seguiría en el iPhone. No cumpliría por sí solo el objetivo.

### 16.10 Recomendación final

**Decisión:** **NO DEMOSTRADA.** No existe hoy una arquitectura defendible con A o B para llamadas del iPhone en Windows 11 con audio bidireccional, usando el Bluetooth existente y sin sustituir su controlador.

**Qué acceso, interfaz o capacidad falta (exactamente):**
1. Un **medio legítimo de transportar SCO/eSCO desde modo usuario** — no existe ni en la API documentada (Winsock = solo RFCOMM) ni como socket del kernel (cf. `BTPROTO_SCO` en Linux).
2. O bien un **registro efectivo del rol HF de Windows para una app de terceros** (`RegisterApp` con `IsRegistered()==True` y una `PhoneLine` visible) en la 26300 — hoy denegado o inefectivo por una causa **desconocida**.
3. O bien una **vía de kernel documentada y soportada que permita coexistir** con el driver HFP de Windows (un *profile driver* propio), con un canal de firma viable para el público (hoy «attestation = solo pruebas»).

**Evidencia que haría reconsiderarlo:**
- Una app de terceros, reproducible, que muestre `IsRegistered()==True` y una `PhoneLine` Bluetooth con un iPhone en Windows 11 ≥ 26100, con su manifiesto, firma y forma de distribución (un hilo de Q&A, un issue o un binario con código fuente).
- O una declaración/documentación de Microsoft que describa las condiciones reales del registro (p. ej. LAF + Store) y la respuesta a una solicitud LAF para este caso.
- O el resultado de E1 con aparición del PC y voz bidireccional (reduce el problema al control).
- O documentación/pruebas de coexistencia entre un *profile driver* propio y el driver HFP de Windows.

**Experimento único recomendado:** E1 (16.9). Si el usuario no lo autoriza, la recomendación es **cerrar la investigación** sin más trabajo técnico en WinMac. No se recomienda ninguna implementación (ni driver, ni pila propia, ni port de iphonebridge): no hay camino que cumpla las restricciones.

**Confianza:** *alta (≈85 %)* en que no hay hoy una ruta reproducible bajo estas restricciones (SCO de usuario inexistente y pila propia incompatible); *alta (≈80 %)* en que BluetoothHFP depende fundamentalmente de poseer el controlador y no es redistribuible; *media (≈55 %)* en que un driver firmado sea realmente inviable para público general (faltan pruebas de coexistencia, no de imposibilidad); *baja (≈30 %)* en el valor de E1 para cambiar el veredicto global (puede cerrar la puerta nativa pero no abre el control).

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
