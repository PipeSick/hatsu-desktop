# 57 — Llamadas completas (iPhone y Android): HFP, audio del PC y viabilidad

Etiquetas: **OBSERVADO** (prueba real o compilación contra el SDK 10.0.26100), **FUENTE** (documentación pública leída en esta sesión), **CONOCIMIENTO** (conocimiento técnico previo, **no re-verificado** aquí: learn.microsoft.com no es accesible desde la sesión), **INFERIDO**, **PENDIENTE-HW**.
Regla de aceptación del producto: contestar/rechazar/colgar **solo** se ofrecen si hay control **y** audio bidireccional del PC verificado en una llamada real (`CallRoutePlanner`, con pruebas).

## 1. Resultado de la investigación HFP
1. **Qué implementa Windows (CONOCIMIENTO + FUENTE parcial).** Windows tiene pila HFP propia con driver (SCO/eSCO). Como Audio Gateway sirve a auriculares (FUENTE: guías de accesorios de Microsoft); como Hands-Free con un teléfono (rol que usa Phone Link para llamadas con micrófono y altavoces del PC) lo hace la pila del sistema y expone el audio como endpoints «Hands-Free» del sistema. Hatsu no mueve audio: lo mueve Windows.
2. **Qué es accesible a una app (OBSERVADO en el SDK 26100).** `PhoneLineTransportDevice` expone `GetDeviceSelector`, `FromId`, `RequestAccessAsync`, `RegisterApp/UnregisterApp(ForUser)`, `IsRegistered()`, `ConnectAsync`, `AudioRoutingStatus` (+ evento), `InBandRingingEnabled` (+ evento). Es decir: **registrar la app, conectar y ver el estado de enrutado**. No hay API que entregue ni reciba flujo PCM de la llamada. `PhoneAudioRoutingEndpoint`/`PhoneCallAudioDevice` describen destinos de la propia línea (auricular, altavoz, Bluetooth), no un puente al micrófono del PC. **Conclusión (INFERIDO):** incluso con acceso, el audio iría por los endpoints del sistema que Windows conecta; la app solo orquesta.
3. **Capacidad restringida.** `phoneLineTransportManagement`: restringida, «Manage phone line connectivity devices» (FUENTE: app-capability-declarations de MicrosoftDocs). Se justifica al enviar a la Store; con carga lateral se declara sin aprobación. Hay **informes** de acceso denegado y de `RegisterApp` sin efecto en compilaciones recientes (docs/41). Tu PC: `DeniedBySystem` sin paquete (OBSERVADO). Con la identidad MSIX no hay evidencia de que cambie.
4. **No confundir:** autorización para declarar ≠ soporte real. Aprobada o no, hay que probarlo en hardware.

## 2. Alternativas evaluadas (control + audio juntos)
| Ruta | iPhone | Android | Control | Audio PC | Controlador | Store/MSIX | Estado |
|---|---|---|---|---|---|---|---|
| ANCS Perform Action | Sí (anuncia acciones) | No existe | contestar/rechazar plausible | **No** | no | sí | Pendiente-HW; **sin audio no se expone** |
| `PhoneCallManager` (observar) | por probar | por probar | solo estado | no | no | sí (`phoneCall` general) | Experimental (solo registro) |
| `PhoneLineTransportDevice` | HFP AG (CONOCIMIENTO) | HFP AG (CONOCIMIENTO) | registro/conexión/enrutado | vía endpoints de Windows | no | **requiere aprobación de Microsoft** | **Bloqueado por permisos** |
| HFP-HF propio por RFCOMM (AT: ATA, AT+CHUP, +CLCC) | control posible en teoría | idem | sí | **No**: SCO exige driver | **sí** | choca con la pila HFP del sistema que ya ocupa el perfil | **No viable** |
| Driver propio SCO | — | — | — | sí | **sí (inseguro/ fuera de Store/SAC)** | no | Descartado |
| App Android (InCallService/TelecomManager) | — | contestar/rechazar con rol/permiso | sí | **No**: Android no deja a una app capturar/inyectar el audio de la llamada (CONOCIMIENTO) | no | n/a | Resuelve control, **no audio** |
| Phone Link (Microsoft) | sí | sí (app «Link to Windows») | sí | sí | — | solo Microsoft | Existe; no integrable |

## 3. Viabilidad por plataforma
- **iPhone, llamadas completas:** la única ruta que une control y audio es HFP del sistema vía `PhoneLineTransportDevice`: **bloqueada por permisos** y sin validación en hardware. Lo que sí funciona: detectar y mostrar contacto (ANCS).
- **Android, llamadas completas:** misma conclusión. Android como teléfono (AG) habla HFP con cualquier Bluetooth; no hay ANCS, así que la detección sin app depende de la misma ruta HFP/PhoneLine (bloqueada) y no hay aviso previo. Una app complementaria resolvería **control y datos del contacto**, **no el audio**, así que **no se justifica** como solución del objetivo principal (el requisito dice no construir una app que solo resuelva notificaciones/control). No se desarrolló.
- Diferencias entre fabricantes (perfiles SDP, versión de HFP, mSBC/CVSD): **no evaluadas**, requieren dispositivos.

## 4. Alternativa más cercana y recomendada
1. Mantener lo que funciona: aviso con contacto (ANCS) y cierre neutro; estado activo/duración solo si `PhoneCallManager` resulta fiable (prueba de §8).
2. Decisión del propietario: **solicitar a Microsoft** la capacidad `phoneLineTransportManagement` con un paquete experimental **separado** (otra identidad, no el de la Store), para medir con `--probe-calls` si hay acceso y si aparece audio. Sin esa aprobación el objetivo «contestar y hablar desde el PC» no es alcanzable con APIs públicas.
3. Entretanto, para llamadas completas: Phone Link de Microsoft.

## 5. Código implementado (esta fase)
- `CallRoutePlanner` + `CallProbeReport` + `ControlSupport.Verified` (Core, 4 pruebas): codifica el criterio de aceptación; hoy ningún botón.
- `HatsuDesktop.Connect.exe --probe-calls` (`CallProbe`, App): sondeo **de solo lectura**. Escribe `…\WinMac\logs\connect-probe-calls.txt` (en MSIX se virtualiza bajo `%LOCALAPPDATA%\Packages\<id>\LocalCache\Local\WinMac\logs\`) con: identidad de paquete, `PhoneCallManager`, `PhoneCallStore`/líneas, transportes Bluetooth y `RequestAccessAsync` (no registra ni conecta), endpoints de audio «Hands-Free», servicios SDP de los teléfonos emparejados (`PhoneCapabilities`). Sin nombres ni números.
- Compila contra el SDK 26100 (confirma que las firmas existen); **no se ha ejecutado en hardware**.

## 6. Hatsu Notch
Cable ya preparado (docs/56): `state` ringing/active/missed, `since`, `device`, `controls.{answer,reject,hangup}` en `false`. NotchWin (repo aparte) debe: pintar botones solo con `controls.*`, mostrar duración desde `since`, priorizar `active` sobre música pausada, y estado de micrófono/salida cuando exista. **No se modificó NotchWin ni `tools/notch-pin.txt`.** Android reutilizaría el mismo cable con `device:"Android"`.

## 7. Estados
| Elemento | Estado |
|---|---|
| Detección + contacto iPhone (ANCS), cierre neutro | Implementado; corrección pendiente de validar en el PC real |
| `CallRoutePlanner`, `PhoneCapabilities`, `CallTracker.Active` | Implementado y probado (unitario) |
| `--probe-calls`, `SystemCallObserver` | Implementado, pendiente de hardware |
| Contestar/rechazar/colgar con audio del PC (iPhone y Android) | **Bloqueado por permisos** (capacidad restringida) |
| Audio bidireccional | **No verificado / sin ruta pública disponible** |
| HFP propio + SCO | **No viable** (driver) |
| App Android para audio | **No viable** (restricción de audio de Android, CONOCIMIENTO) |

## 8. Pruebas manuales con hardware
1. `HatsuDesktop.Connect.exe --probe-calls` (PC con el iPhone y con un Android emparejados por Bluetooth clásico) y leer el informe: ¿líneas Bluetooth?, ¿`RequestAccessAsync`?, ¿endpoints Hands-Free?, ¿HFP-AG anunciado?
2. Con una llamada real contestada en el teléfono, mirar `connect.log` (`telefonía del sistema: activa=…`) y probar `HATSU_CONNECT_SYSTEM_CALLS=1`.
3. Si se consigue el permiso: paquete experimental lateral que solo llame a `RequestAccessAsync`, `RegisterApp`, `ConnectAsync` y lea `AudioRoutingStatus`; después llamada real: audio teléfono→PC y micrófono PC→teléfono (ambos sentidos); sin tocar el teléfono; colgar desde Hatsu y comprobar que se liberan los endpoints. Repetir con iPhone y con Android.
4. Windows ya conectado como manos libres al mismo teléfono (Phone Link): comprobar el conflicto.
