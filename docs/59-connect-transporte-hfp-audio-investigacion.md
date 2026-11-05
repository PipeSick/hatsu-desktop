# 59 — Transporte telefónico, HFP y audio: qué se sabe, qué se midió, qué falta

Etiquetas: **DOC** documentado oficialmente (fuente leída en esta sesión) · **SDK** observado en los metadatos del Windows SDK 10.0.26100 · **EQUIPO** observado en los diagnósticos reales del usuario (Beta 2.0.2.0) · **INFERIDO** razonable sin prueba directa · **NO VERIFICADO**.
Nada de esto es éxito: **no hay todavía una llamada real con audio bidireccional**.

## 1. Lo que los diagnósticos reales dicen (EQUIPO)
| Prueba | iPhone desconectado | iPhone conectado y en llamada (audio en el teléfono) |
|---|---|---|
| Paquete Store/MSIX | sí | sí |
| `PhoneCallManager.IsCallActive` | False | **False** (con una llamada real) |
| Línea predeterminada (`GetDefaultLineAsync`) | `0x8007139F` | `0x8007139F` |
| Transporte telefónico Bluetooth | 1 listado, `RequestAccessAsync` = Allowed | igual |
| Endpoints de audio | 1 salida + 1 entrada | igual; ninguno «Hands-Free» |
| Bluetooth emparejados | 3, 0 conectados | 3, 1 conectado; HFP-AG, PBAP, MAP anunciados; A2DP ausente |

Lectura honesta: Windows **no tenía una sesión HFP gestionada por el sistema** con el iPhone durante esa llamada (sin endpoint Hands-Free, sin A2DP, sin línea, `IsCallActive=False`). El audio siguió en el teléfono porque nadie lo pidió al PC.

## 2. Preguntas del encargo
1. **Identidad del transporte que dio Allowed.** NO VERIFICADO con los datos actuales: el diagnóstico 2.0.2.0 no traía identificadores. **Inferido:** el transporte aparece también con el iPhone desconectado y hay 1 transporte para 3 emparejados (1 teléfono) → probablemente el iPhone emparejado como dispositivo con HFP-AG, no un dispositivo nuevo ni una abstracción por llamada. Esta versión lo comprueba: lista cada transporte con `id`, contenedor, `Transport`, `DeviceId`, `IsRegistered`, `AudioRoutingStatus` y **a qué dispositivo emparejado corresponde** (por `DeviceId`/dirección o `ContainerId`, todo anonimizado y correlacionable).
2. **Qué significa `Allowed`.** DOC: `PhoneLineTransportDevice` «represents the hardware device associated with a PhoneLine… currently only supported for use with bluetooth devices» y `RequestAccessAsync` devuelve un `DeviceAccessStatus`. **Inferido:** `Allowed` = Windows deja a esta app abrir ese dispositivo (acceso al dispositivo), no «la app es cliente de telefonía». **EQUIPO:** `Allowed` se obtuvo SIN declarar `phoneLineTransportManagement` en el manifiesto → esa capacidad no es necesaria para *pedir acceso*. Si lo es para `RegisterApp`/`ConnectAsync` (operaciones de gestión) es NO VERIFICADO: lo responde el experimento 3.
3. **`PhoneCallManager=False`.** DOC: sirve «para determinar el estado de las llamadas»; su página lista las capacidades `phoneCallHistory`/`phoneCallHistorySystem` (restringidas) y no dice qué líneas cubre. **Inferido (el más probable):** solo refleja llamadas de líneas que el sistema gestiona (celular/VoIP/líneas Bluetooth con sesión HFP del sistema); sin sesión HFP del sistema no hay línea → False. La revisión por líneas (`PhoneLine.GetAllActivePhoneCalls`, SDK) de esta versión lo confirma o lo descarta.
4. **`0x8007139F`.** DOC: `ERROR_INVALID_STATE` (5023) «The group or resource is not in the correct state to perform the requested operation». **Inferido:** no hay línea predeterminada en un estado utilizable (coherente con no haber sesión HFP/celular). No es «acceso denegado».
5. **Endpoints de audio.** NO VERIFICADO su identidad (1+1 pueden ser los altavoces/micrófono del PC). Esta versión los identifica por contenedor y los relaciona con los emparejados («pertenece a / NO pertenece a un dispositivo Bluetooth emparejado»); un endpoint no cuenta como audio telefónico.
6. **Estado real HFP.** EQUIPO: sin endpoint Hands-Free ni A2DP con el iPhone conectado → ninguna sesión de audio clásica con Windows. Falta saber si la conexión clásica HFP se establece al pedirla.
7. **Audio bidireccional.** Hay una ruta **pública** nunca probada aquí (ver §3). NO VERIFICADO.

## 3. La API pública que faltaba en el análisis anterior (SDK 26100, metadatos)
- `PhoneLine`: `Transport` (Cellular | VoipApp | **Bluetooth**), `TransportDeviceId`, `GetAllActivePhoneCallsAsync()`.
- `PhoneCall`: `Status` (Incoming, Dialing, **Talking**, Held, Ended, Lost), `AudioDevice` (**LocalDevice** | **RemoteDevice**), `AcceptIncomingAsync`, `RejectIncomingAsync`, `EndAsync`, `MuteAsync`, `ChangeAudioDeviceAsync`, `GetPhoneCallInfoAsync` (nombre, número, dirección, inicio).
- `PhoneLineTransportDevice`: `GetDeviceSelector`, `FromId`, `RequestAccessAsync`, `RegisterApp/UnregisterApp(ForUser)`, `IsRegistered()`, `Connect/ConnectAsync`, `AudioRoutingStatus`, `InBandRingingEnabled`, `DeviceId`, `Transport`.
- DOC: `PhoneCall` requiere la capacidad `phoneCall` (general, **no restringida** según la lista de capacidades). `PhoneLine`/`PhoneCallManager` listan capacidades restringidas de historial (los textos de Microsoft son parcos: NO VERIFICADO qué exige cada método en la práctica).
- Arquitectura que esto sugiere (INFERIDO, la de Phone Link): transporte Bluetooth → Windows mantiene HFP (SLC + SCO) → aparece una línea → llamadas con `AcceptIncoming/Reject/End` → `ChangeAudioDevice(LocalDevice)` lleva el audio a los endpoints del PC. Hatsu no manejaría PCM: lo hace el sistema.
- Compatible con **iPhone y Android** por igual (ambos son HFP-AG); Android no necesitaría app. NO VERIFICADO en hardware.

## 4. Capacidad restringida `phoneLineTransportManagement`
DOC: restringida, «manage phone line connectivity devices»; la información se da al enviar a la Store; se puede declarar sin aprobación solo por instalación lateral. Qué operación la exige: NO VERIFICADO (el experimento `RegisterApp` lo medirá: si Windows deniega sin ella, el informe lo mostrará con el código). Procedimiento y compatibilidad con *package flights*: NO VERIFICADO (la documentación accesible no los detalla); la decisión de pedirla sigue siendo del propietario y **no se declaró nada**.

## 5. Android (DOC parcial)
`InCallService` (developer.android.com) es el servicio de la app **marcador predeterminado** (`ROLE_DIALER`, `BIND_INCALL_SERVICE`); incluye enrutado de audio entre endpoints del propio teléfono (`requestCallEndpointChange`), no un puente al PC. Una app complementaria podría contestar/colgar, pero el audio al PC seguiría dependiendo de HFP: por eso la vía principal es la misma de Windows. No se desarrolló ninguna app. (Se leyó solo la primera parte de una página muy larga.)

## 6. Qué cambia en esta versión (2.0.3.0)
- **Diagnóstico ampliado (solo lectura):** identidad y correlación de transporte/líneas/audio con los emparejados, `AudioRoutingStatus`, vigilante de líneas, llamadas activas por línea (con nombre/número enmascarados), nombres de los HRESULT (`0x8007139F`…).
- **Experimentos explícitos (confirmación; reversibles):** (a) registrar la app en el transporte + `ConnectAsync` + observar + `UnregisterApp`; (b) con una llamada **ya contestada**, pasar su audio al PC unos 10 s y devolverlo; (c) deshacer el registro. Ninguno contesta, cuelga ni marca.
- El proyecto Connect se compila contra el SDK 26100 para poder llamar a esas API; el mínimo del paquete sigue en 10.0.19041 y cada llamada va protegida (si falta, el informe dice «No compatible»).
- **Dos paquetes en el CI:** el normal (igual capacidades que Stable) y un alterno que añade solo la capacidad **general** `phoneCall` (la que la documentación asocia a `PhoneCall`). **La elección es tuya:** el alterno modifica el manifiesto (sin capacidades restringidas); por eso no es el predeterminado.
- Sin botones de contestar/rechazar/colgar en la interfaz.

## 7. Secuencia de pruebas con hardware (versión Store)
1. iPhone desconectado → *Ejecutar diagnóstico* → copiar el informe.
2. iPhone conectado, sin llamada → diagnóstico.
3. iPhone conectado, **llamada sonando** → diagnóstico (¿aparece línea/llamada con estado Incoming?).
4. **Llamada contestada en el teléfono** → diagnóstico (¿Talking?).
5. Experimento *registrar y conectar HFP*: ¿Windows acepta `RegisterApp`/`ConnectAsync`? ¿aparecen línea, endpoint Hands-Free, `AudioRoutingStatus`?
6. Si hay línea con llamada Talking: experimento *pasar el audio al PC*; decir a mano si se oye y si te oyen.
7. Repetir 1-6 con un Android emparejado (sin app).
8. *Deshacer el registro* al terminar.
Todo informe: «Copiar» o «Guardar copia» y pegarlo.
