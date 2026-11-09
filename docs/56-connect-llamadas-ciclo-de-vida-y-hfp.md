# 56 — Llamadas del iPhone: ciclo de vida ANCS corregido e investigación HFP

Etiquetas: **OBSERVADO** (prueba real del usuario con la app de la Store), **FUENTE** (documentación pública leída), **INFERIDO**, **PENDIENTE-HW**.

## 1. Causa de «Llamada finalizada» (OBSERVADO + código)
Secuencia real: llega `Added/IncomingCall` (suena, nombre correcto) → el usuario contesta en el iPhone → iOS **retira** la notificación (`Removed`). `CallTracker` interpretaba «Removed sin perdida en 4 s» como `Ended` y la Isla mostraba «Llamada finalizada» mientras la llamada seguía. ANCS **no informa** del estado telefónico: la retirada ocurre igual al contestar, rechazar o colgar quien llama. El texto lo pinta NotchWin a partir de `state:"ended"`.
No se pudo confirmar en el iPhone qué atributos trae un `Modified`; el código no los usa para cambiar de fase (INFERIDO: iOS no actualiza el estado de la llamada por ahí).

## 2. Corrección (rama `feat/hatsu-launchpad-dock-connect`)
- Retirada sin perdida, o 90 s sin noticias → **cierre neutro** (`state:"none"`, nada que decir). `CallPhase.Ended` ya no sale de ANCS.
- «Perdida» se conserva (ANCS la publica explícitamente) y también si llega hasta 10 s después del cierre neutro (misma sesión).
- `Added` duplicado de la misma llamada ya no borra a quien llama. `Removed`/`OnCaller` atrasados o de otra sesión no tocan una llamada posterior. `Modified` no cambia la fase. Reconexión: `Reset` limpia todo (incluida la ventana de perdida tardía).
- Nueva fase `Active` y `Ended` **solo** desde una fuente de telefonía fiable (`CallTracker.OnTelephony`), con correlación (este iPhone acaba de sonar) y duración (`ActiveDuration`/`since`).
- Cable hacia la Isla: `{kind:"call", state, caller, device:"iPhone", since?, controls:{answer,reject,hangup:false}}`. Cambio aditivo. **La Isla (repositorio NotchWin, fijado en `tools/notch-pin.txt`) debe: no dibujar botones salvo `controls.*`, mostrar `active` con duración desde `since`, y dar prioridad a `active` sobre la música pausada.** Eso no está en este repositorio: PENDIENTE (cambio en NotchWin + subir el pin).
- Notificaciones normales (WhatsApp…) no se tocaron.

## 3. HFP en Windows (FUENTE + docs/39 + docs/41)
- Windows negocia HFP (rol Hands-Free) con su propia pila; el iPhone es Audio Gateway. La pila del sistema ya ocupa el perfil; un HFP propio por RFCOMM compite con ella y el audio SCO necesita controlador (descartado).
- Interfaz pública de telefonía: `Windows.ApplicationModel.Calls` (`PhoneLine`, `PhoneLineTransportDevice` —«currently only supported for use with bluetooth devices»—, `PhoneCallManager`, `PhoneCallStore`).
- `Windows.Devices.Bluetooth(.Rfcomm)` / `Enumeration`: públicos con la capacidad `bluetooth`; sirven para descubrir/emparejar/leer servicios, **no** exponen control de llamadas HFP.

## 4. APIs y estado
| API | Acceso | Estado en Hatsu |
|---|---|---|
| ANCS por WinRT GATT | Público (`bluetooth`) | **Validado** (aviso y nombre) |
| `PhoneCallManager.IsCallActive/CallStateChanged` | Público; la doc lista `phoneCall` (general, no restringida) | **Experimental**, solo lectura (`SystemCallObserver`); sin validar que refleje llamadas del iPhone |
| ANCS Perform Notification Action (contestar/rechazar) | Público (Apple); el iPhone anuncia las acciones | Codificado (`Ancs.PerformAction`), **no expuesto**: PENDIENTE-HW |
| `PhoneLineTransportDevice` (registro, conectar, audio) | Capacidad **restringida** `phoneLineTransportManagement` | Sin acceso: `DeniedBySystem` (OBSERVADO) |
| HFP propio (RFCOMM) + SCO | Sin API; SCO requiere controlador | Descartado |

## 5. `phoneLineTransportManagement` (FUENTE: app-capability-declarations, repo público de MicrosoftDocs)
- Es **restringida**: «must provide info during the app submission process»; apps con carga lateral pueden declararla sin aprobación. Se pide en Partner Center al enviar (campo de capacidades restringidas, justificación al equipo de certificación). La doc no indica un trámite distinto para desarrolladores independientes ni garantiza concesión: se evalúa caso a caso (foros: anecdótico).
- Efecto en certificación: declararla sin aprobación bloquea/retrasa el envío; podría afectar el envío estable. **No se declaró nada** y el manifiesto no cambió.
- La identidad MSIX actual permite probar **por carga lateral** con un paquete de pruebas aparte (otra identidad, no el de la Store). No hay evidencia de que la identidad sola desbloquee la API (informes de acceso denegado, docs/41).
- Es necesaria para: registrar la app como cliente de la línea, conectar HFP y audio. **No** es necesaria para observar el estado (`PhoneCallManager`) ni para ANCS.

## 6. Viabilidad
| Función | Veredicto |
|---|---|
| Detectar entrante, nombre, dispositivo | **Funciona** (ANCS) |
| Estado activo + duración | **Experimental** (observador del sistema, opt-in `HATSU_CONNECT_SYSTEM_CALLS=1`); sin validar |
| Contestar / rechazar | Ruta pública plausible (ANCS acciones): **no expuesta** hasta probarla con el iPhone |
| Colgar | Sin ruta pública verificada (ANCS lo retira al terminar; las acciones valen solo mientras suena) |
| Marcar | Solo vía `PhoneLine` → capacidad restringida: **bloqueado** |
| Micrófono/altavoz del PC | Solo vía HFP/SCO de Windows: **bloqueado** (restringida o controlador) |

## 7. Enlace Móvil (Phone Link)
Confirmado en docs/41: Phone Link es quien en la práctica usa la telefonía HFP. Conflicto con Hatsu: **hipótesis** (ambos con el mismo iPhone; ANCS es una suscripción GATT por dispositivo y iOS solo concede ANCS a un cliente emparejado a la vez — a verificar). Sin acceso a datos internos de Phone Link.

## 8. Alternativas legítimas (sin ingeniería insegura)
1. Dejar el audio y el control de la llamada a Windows/Phone Link y que Hatsu **solo muestre** el estado.
2. Solicitar la capacidad restringida con justificación (decisión del propietario) y probar con paquete lateral separado.
3. Contestar/rechazar por ANCS tras prueba real, con botones gobernados por `CallControlCapabilities`.

## 9. Pruebas manuales con el iPhone (Windows, app instalada)
1. Llamada entrante → píldora con nombre; **contestar en el iPhone → NO debe aparecer «Llamada finalizada»**: la píldora se cierra sin texto.
2. Rechazar en el iPhone → cierre neutro. No contestar → «Llamada perdida» con nombre.
3. Colgar el otro lado antes de contestar → cierre neutro o perdida.
4. Apagar Bluetooth del PC con llamada sonando → la píldora desaparece.
5. Evidencia del observador: contestar una llamada y leer en el registro de Connect las líneas `telefonía del sistema: activa=…`. Si aparece `activa=True` y `llamada (fuente: sistema): Active` (con la variable de entorno), la ruta es válida; si no, confirma que Windows no refleja llamadas del iPhone ahí.
6. Con la variable activada, comprobar que una llamada de Teams en reposo NO muestra llamada activa del iPhone.
7. Música pausada + llamada contestada (requiere el cambio de NotchWin).
