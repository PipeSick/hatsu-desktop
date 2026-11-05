# 60 — Llamadas completas (iPhone y Android): decisión de arquitectura e implementación

Etiquetas: **DOC** documentado (fuente leída) · **SDK** metadatos del Windows SDK 26100 · **EQUIPO** diagnósticos reales del usuario · **CONOC.** conocimiento técnico previo, no re-verificado en esta sesión · **INFERIDO** · **PEND.** requiere hardware o autorización.
Objetivo: recibir la llamada, ver al contacto, contestar y rechazar desde Hatsu Notch, hablar y escuchar por el micrófono y los audífonos del PC, colgar desde Hatsu; sin tocar el teléfono; iPhone y Android.

## 1. Alternativas comparadas
| # | Alternativa | Control | Audio en el PC | iPhone | Android | Store / MSIX / SAC | Veredicto |
|---|---|---|---|---|---|---|---|
| A | **Windows como cliente HFP; Hatsu usa `PhoneLineTransportDevice` + `PhoneLine` + `PhoneCall`** | sí: `AcceptIncoming/RejectIncoming/End/Mute` (SDK) | sí, lo hace Windows: `ChangeAudioDeviceAsync(LocalDevice)` lleva la llamada a los endpoints del PC (SDK) | sí (HFP-AG anunciado, EQUIPO) | sí (HFP-AG estándar, CONOC.) | API pública; sin drivers ni EXE extra; solo capacidades del manifiesto | **ELEGIDA** |
| B | HFP-HF propio por RFCOMM (comandos AT) | sí en teoría | **no**: el audio es SCO/eSCO y exige un controlador de perfil | — | — | choca con la pila HFP del sistema que ya ocupa el perfil | descartada |
| C | Controlador propio (perfil HFP/SCO + audio virtual) | sí | sí | sí | sí | WDK + firma de Microsoft (atestación/HLK); un MSIX de la Store no instala controladores; no encaja con SAC ni con Store | descartada (no segura/compatible) |
| D | App Android complementaria (`InCallService`/Telecom) | contestar/colgar con el rol de marcador (DOC) | **no**: Android no entrega a una app el audio de la llamada ni deja inyectarlo (CONOC.); el enrutado de `InCallService` es entre endpoints del propio teléfono (DOC) | — | control sí, audio no | requiere app y rol de marcador predeterminado | descartada como solución: resolvería lo que A ya hace sin audio |
| E | Solo ANCS (iPhone) | rechazar; contestar dejaría la llamada en el teléfono | no | parcial | no | pública | usada **solo** como complemento: nombre del contacto y rechazo |
| F | Enlace Móvil (Phone Link) | sí | sí | sí | sí (app «Link to Windows») | solo Microsoft | no integrable; es la referencia de que A es el camino |

**Decisión:** A para iPhone y Android con un único coordinador; ANCS aporta el nombre y el rechazo del iPhone. Hatsu no maneja audio (no hay PCM en el código): Windows enruta a los dispositivos de audio que el usuario ya eligió.

## 2. Arquitectura implementada
```
Teléfono (iPhone / Android)  ── Bluetooth HFP ──  Windows (pila HFP + SCO del sistema)
                                                      │  PhoneLineTransportDevice / PhoneLine (Transport=Bluetooth) / PhoneCall
                                                      ▼
Hatsu Connect (módulo)   WindowsTelephonyProvider ──► CallCoordinator ──► CallTracker (+ ANCS: contacto, rechazo)
        ▲ connect.callcmd {answer|reject|hangup|mute}          │ CallState (+ controles, audio en el PC, aviso, dispositivo)
        │                                                      ▼
Hatsu Notch (NotchWin) ◄───────── connect.call → Barra → notch.feed ───────── píldora con botones, duración y audio
```
- **`WindowsTelephonyProvider`** (Connect.App): `RequestAccessAsync → RegisterApp → ConnectAsync`; `PhoneLineWatcher`; solo líneas con transporte **Bluetooth** (nunca Teams/SIM); lecturas cada 1,5 s (0,4 s con llamada); dos lecturas vacías seguidas para dar una llamada por terminada; `UnregisterApp` al apagar la función o cerrar el módulo.
- **`CallCoordinator`** (Connect.Core, puro): una orden a la vez, tiempo límite de 8 s, comprobación al ejecutar, contestar = `AcceptIncoming` + `ChangeAudioDevice(LocalDevice)` con reintento cuando Windows da la llamada por activa; si el audio no pasa al PC lo **dice** («sigue en el teléfono»). Nunca declara «audio en el PC» por suposición: lo lee de `PhoneCall.AudioDevice`.
- **`CallControlPlanner`**: botones solo si el proveedor está **Listo** y la llamada existe en el sistema (contestar, colgar, silenciar). Rechazar también por ANCS (iPhone) si Windows no concede la telefonía. Con la función apagada: ningún botón.
- **`CallTracker.OnProviderCall`**: Android solo con el sistema (sin ANCS); iPhone une la llamada del sistema a la que ANCS ya anunció (conserva el nombre); una llamada que desaparece sonando se cierra **en neutro**; «Llamada terminada» solo desde el sistema.
- **Hatsu Notch** (repo NotchWin, rama `feat/hatsu-calls`, `175091a`): botones contestar/rechazar/colgar/silenciar visibles solo si `controls.*` es verdadero; duración y «Audio en el PC / en el teléfono»; una notificación o el volumen toman la píldora unos segundos **sin destruir** la llamada activa; al terminar vuelve su comportamiento habitual. Letras y controles musicales sin cambios.
- **Ajustes › Hatsu Connect › Llamadas completas desde el PC:** interruptor **apagado por defecto** (registra a Hatsu como cliente de telefonía mientras esté activo).
- Compartido iPhone/Android: mismo coordinador, mismo cable, misma píldora. Las diferencias quedan en el origen (ANCS solo iPhone).

## 3. Lo que depende de Microsoft (bloqueo externo)
`RequestAccessAsync` devolvió **Allowed** en la Beta de la Store sin capacidades restringidas (EQUIPO). Lo que **no se sabe** hasta ejecutar el proveedor con un teléfono real: si `RegisterApp`/`ConnectAsync` y las operaciones de `PhoneCall` los concede Windows sin `phoneLineTransportManagement`. Por eso hay **dos paquetes** (ambos con la identidad oficial, sin firmar, salen del CI; no se han enviado):
- **A `…-beta-phonecall-unsigned.msix`:** Stable + capacidad **general** `phoneCall` (DOC: la usa `PhoneCall`; no es restringida). Se puede presentar sin aprobación especial.
- **B `…-beta-callcontrol-unsigned.msix`:** A + `phoneLineTransportManagement` (**restringida**, DOC: «manage phone line connectivity devices»). **No se puede publicar sin la aprobación de Microsoft.**

**Qué hacer (propietario):**
1. Enviar primero **A** al vuelo Beta. Si con A el módulo registra y el estado es «Listo», la función ya funciona y no hace falta nada más.
2. Si el registro de Hatsu en el registro del módulo (`connect.log`) dice «hace falta la capacidad phoneLineTransportManagement», presentar **B**: en Partner Center, al enviar el paquete (vuelo o publicación), la sección de capacidades restringidas pide una justificación. Texto sugerido: *«Hatsu Desktop es una app de escritorio gratuita y de código abierto. La función opcional "Llamadas completas desde el PC" actúa como cliente de manos libres (HFP) de un teléfono emparejado por Bluetooth: registra la app con PhoneLineTransportDevice, contesta/rechaza/cuelga con PhoneCall y pide a Windows que enrute el audio a los dispositivos del PC. No lee historial de llamadas ni mensajes, no envía datos fuera del equipo y se desactiva por defecto; al apagarla se deshace el registro.»*
3. Qué parte queda condicionada: **toda** la función de contestar/rechazar/colgar/audio por Windows (el resto — avisos, contacto, rechazo ANCS del iPhone, Notch — no). Sin aprobación el proveedor queda «No disponible» con la causa y no se muestra ningún botón de contestar.
4. Compatibilidad con *package flights* y plazos de aprobación: NO VERIFICADO (la documentación accesible no los detalla).

## 4. Qué se verificó aquí y qué no
- **Automatizado** (sin hardware): Connect.Core 51 pruebas (Android solo-sistema; iPhone ANCS+sistema; gating de botones; contestar con audio; fallo de contestar; audio que no pasa; reintento; llamada en espera; doble clic; Windows lento; desaparición; reinicio), Bar.Core 626 (cable a la Isla y visibilidad), Host.Core 344 (ajuste opt-in), NotchWin.Core 215 (píldora, controles, duración, prioridad frente a notificaciones). Compilan Connect.App, el host y el CI genera ambos MSIX con verificación de identidad y de capacidades exactas.
- **No verificable aquí:** que Windows conceda el registro, que la llamada aparezca en la línea Bluetooth, que el audio fluya en los dos sentidos, la interfaz WinUI con clics reales (se compila en el CI de Windows; no se ejecuta). **El audio bidireccional NO está demostrado.**
- Riesgos conocidos: conflicto con Enlace Móvil (ambos quieren la misma llamada); en iPhone, HFP exige tener activado en Ajustes › Bluetooth del iPhone el uso como dispositivo de audio (CONOC.); varias líneas/teléfonos a la vez (se toma la llamada Talking, luego Incoming).
