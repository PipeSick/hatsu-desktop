# WinMac Connect — Investigación de integración nativa con iPhone (batería, notificaciones, llamadas, mensajes)

Fecha: 2026-11-03 · Solo investigación y arquitectura: **no se implementa nada en WinMac ni en NotchWin**. Hardware objetivo: PC con Windows 11 y Bluetooth integrado + iPhone 17 Pro, sin app en el iPhone y sin Phone Link.
Las pruebas de concepto van en una herramienta aparte (`ConnectProbe`, fuera de los repositorios, §6). **Nada de este informe está verificado en tu hardware:** los estados «Viable técnicamente» se apoyan en documentación y en proyectos de terceros que funcionan.

## 1. Resumen ejecutivo

| Función | Qué se puede construir sin app en el iPhone | Estado |
|---|---|---|
| **Batería** | Porcentaje real (1 %), con aviso de cambios, por el servicio estándar Battery Service (0x180F) que iOS publica en la conexión LE vinculada. Estado de carga: **no** (BAS solo da el nivel). | Viable técnicamente |
| **Notificaciones** (verificado en §6c) | Todas las notificaciones del iPhone (app, título, subtítulo, texto, fecha, categoría) con **ANCS**, la especificación pública de Apple. Nuevas, modificadas y eliminadas. Acciones positiva y negativa (p. ej. contestar o rechazar una llamada, borrar). **No** se pueden responder. | Viable técnicamente |
| **Llamadas** | *Aviso* de llamada entrante con nombre o número y **contestar / rechazar** desde el PC: sí, por ANCS (la llamada sigue en el iPhone). *Marcar y hablar con el micrófono y altavoces del PC* (HFP): la API pública de Windows existe, pero exige una capacidad **restringida** y paquete MSIX, y el único proyecto que lo intentó no logró el audio. | Aviso y contestar: viable · Audio en el PC: experimental, probablemente bloqueado |
| **SMS / iMessage** | Recibir y **responder** conversaciones 1:1 (SMS e iMessage) con **MAP** sobre RFCOMM, que Windows permite usar con API pública. Dos proyectos Linux lo hacen con iOS 26/27. En Windows nadie lo ha publicado funcionando. Sin grupos, sin historial completo, sin adjuntos. | Experimental (en Windows) |

**Recomendación:** empezar por **batería + notificaciones (ANCS)**, que comparten la misma conexión LE y ya funcionan en Windows en un proyecto abierto ([iphone-companion](https://github.com/312arjun/iphone-companion), MIT). Así salen también el aviso de llamadas y el «Ahora suena» (AMS). Después, **mensajes por MAP**, tras pasar la prueba 8b. Las llamadas con audio en el PC quedan aparcadas hasta que Windows deje usar su API de telefonía a una app como WinMac.

## 2. Documentación oficial consultada

| Fuente | Qué aporta |
|---|---|
| [Apple — ANCS Specification](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Introduction/Introduction.html) ([Specification](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Specification/Specification.html), [Appendix](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleNotificationCenterServiceSpecification/Appendix/Appendix.html)) | Servicio `7905F431-B5CE-4E99-A40F-4B1E122D00D0`. Notification Source `9FBF120D-…` (notificable, obligatoria), Control Point `69D1D8F3-…` (escritura con respuesta), Data Source `22EAC6E9-…` (notificable). «All these characteristics require authorization for access.» El iPhone es *Notification Provider* y el accesorio (WinMac) es *Notification Consumer*, cliente GATT. ANCS «is not guaranteed to always be present»: hay que vigilar *Service Changed*. Eventos: añadida 0, modificada 1, eliminada 2. Banderas: Silent, Important, PreExisting, PositiveAction, NegativeAction. 12 categorías (IncomingCall = 1, MissedCall = 2…). Atributos: AppIdentifier, Title, Subtitle, Message (con longitud máxima), MessageSize, Date, etiquetas de acción. Comandos: GetNotificationAttributes, GetAppAttributes (DisplayName), PerformNotificationAction. Los identificadores solo valen dentro de una sesión. |
| [Apple — AMS Specification](https://developer.apple.com/library/archive/documentation/CoreBluetooth/Reference/AppleMediaService_Reference/Specification/Specification.html) | Servicio `89D3502B-0F36-433A-8EF4-C502AD55F8DC`. Remote Command, Entity Update y Entity Attribute. Entidades Player, Queue y Track. Sirve para mostrar y controlar lo que suena en el iPhone (no es una de las cuatro funciones pedidas, pero viene gratis en la misma conexión). |
| Apple — Accessory Design Guidelines (PDF) | **No se pudo leer** (supera el límite de descarga). Las versiones de perfiles Classic de iOS se toman de fuentes secundarias (abajo). |
| Microsoft — [PhoneLineTransportDevice](https://github.com/MicrosoftDocs/winrt-api/blob/docs/windows.applicationmodel.calls/phonelinetransportdevice.md) y [declaraciones de capacidades](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/uwp/packaging/app-capability-declarations.md) | Leídas en el repositorio público de la documentación, porque learn.microsoft.com no era accesible desde el entorno de investigación. «Currently only supported for use with bluetooth devices.» `phoneLineTransportManagement` «allows apps to manage system devices responsible for phone line connectivity» y figura entre las capacidades restringidas («require approval for Microsoft Store submission and/or are generally only available to Microsoft and certain partners»; una app de carga lateral puede declararlas sin aprobación). La capacidad `bluetooth` permite «communicate with already paired bluetooth devices over both Generic Attribute (GATT) or Classic Basic Rate (RFCOMM)». |
| Microsoft — preguntas y respuestas ([RegisterApp sin efecto en Windows 11](https://learn.microsoft.com/en-us/answers/questions/6041632/phonelinetransportdevice-registerapp-has-no-effect), [RequestAccessAsync denegado](https://learn.microsoft.com/en-gb/answers/questions/1258935/in-phonelinetransportdevice-windows-applicationmod)) y [FAQ de Bluetooth](https://learn.microsoft.com/windows/uwp/devices-sensors/bluetooth-dev-faq) | Vistas solo en los resultados de búsqueda. Un desarrollador obtiene `DeniedBySystem`. Otro, en la compilación 26300, logra acceso pero `IsRegistered` sigue en false y `Connect` falla. El rol periférico depende del hardware (`IsPeripheralRoleSupported`). |
| [Boletín técnico de Audi (NHTSA)](https://static.nhtsa.gov/odi/tsbs/2019/MC-10165969-9999.pdf) | Desde iOS 13, MAP en el iPhone permite **leer y enviar** mensajes; antes solo leer. Hay que activar «Mostrar notificaciones» en Ajustes › Bluetooth › (i). |
| Especificación HFP (vía la [implementación de Chromium OS](https://android.googlesource.com/platform/external/adhd/+/d4592bd501f5898d7f00e709711d896e04a88c40/cras/src/server/cras_hfp_slc.c)) | El teléfono (Audio Gateway) puede enviar los indicadores `battchg` (0–5), `signal` (0–5) y `service` por `+CIEV`. La batería por HFP solo tiene 6 niveles y exige tener el canal HFP, que en Windows controla su propia pila. |

## 3. Proyectos open source relevantes

| Proyecto | Licencia | Mantenimiento | SO | Funciones reales con iPhone | ¿App en iPhone? | Reutilizable |
|---|---|---|---|---|---|---|
| [312arjun/iphone-companion](https://github.com/312arjun/iphone-companion) | MIT | Reciente (6 commits, sin fechas visibles) | **Windows 10/11** | Notificaciones (ANCS) de todas las apps, llamada entrante con contestar/rechazar (acciones ANCS), multimedia (AMS), **batería 0x180F con avisos de cambio**. Sin SMS ni audio de llamadas. Python + bleak (que usa la API WinRT de GATT) | **No** | Sí (MIT). Sobre todo como referencia de protocolo y de emparejamiento en Windows: emparejar con «Agregar dispositivo» de Windows, confirmar el código y permitir compartir notificaciones en el iPhone; vincular una vez; Phone Link no debe tener el iPhone; Windows guarda en caché la lista de servicios |
| [dennis6481/unlock-windows-with-iphone](https://github.com/dennis6481/unlock-windows-with-iphone) | MIT | Activo (versión 1.0 fusionada el 11-10-2026) | **Windows 10 2004+ / 11** | Presencia del iPhone por BLE y ANCS (para desbloquear) | **No** desde la 1.0 («without installing or running any additional software on the phone») | Sí (MIT). Confirma emparejamiento del sistema + cliente GATT nativo en Windows, sin rol periférico |
| [erikwb/blueferry](https://github.com/erikwb/blueferry) | GPL-2.0-or-later (según resumen) | Muy activo (1645 commits) | Linux (BlueZ ≥ 5.86) | SMS/iMessage/RCS enviar y recibir (MAP), contactos (PBAP), notificaciones (ANCS), batería, AMS; llamadas HFP en vista previa. Probado con iOS 26.5 (iPhone 16 Pro Max) e iOS 27 beta (iPhone 17 Pro Max) | No | **No se puede copiar código** a WinMac (GPL). Su [PROTOCOL.md](https://github.com/erikwb/blueferry/blob/main/PROTOCOL.md) sirve como referencia de comportamiento: el PC inicia la conexión, iOS pide la autenticación, y el anuncio con solicitud de ANCS solo se usa en Linux tras el vínculo Classic |
| [zackb/tether](https://github.com/zackb/tether) | MIT | Muy activo (731 commits; [issue 66](https://github.com/zackb/tether/issues/66) del 28-10-2026) | Linux | Leer y responder SMS e iMessage (MAP, estable). Notificaciones (estable; «does not work on iOS 18 and earlier»). Llamadas: marcar, contestar y colgar, con identificador (beta, PipeWire). Requiere en el iPhone «Mostrar notificaciones de mensajes» y «Sincronizar contactos» | No para mensajes y notificaciones | Sí (MIT), como referencia. Documenta límites reales de MAP con iOS: un remitente, sin id de conversación ni participantes, tipo siempre `SMS_GSM`, carpeta Enviados vacía, SMS e iMessage indistinguibles → **sin grupos**. Adaptadores problemáticos: Realtek RTL8761BU, clones CSR, Barrot |
| [BestOwl/MyPhone](https://github.com/BestOwl/MyPhone) | MIT | Sin fechas visibles; SMS «en v1.0» todavía pendiente | Windows 10 1903+ (MSIX) | Llamadas por `PhoneLineTransportDevice`: establece la conexión HFP y marca, pero el autor «failed to establish SCO audio connection and transfer any audio». Tiene un proyecto OBEX propio | No | Sí (MIT), como evidencia de que la API de llamadas de Windows solo controla la llamada y no el audio |
| [saymonsh/BluetoothHFP](https://github.com/saymonsh/BluetoothHFP) | No indicada | — | Windows 11 (MSIX, modo desarrollador) | Probado con iPhone 12 Pro Max / iOS 17: A2DP sí; acceso al transporte de llamadas «Windows returned denied call access»; sin marcar, contestar ni colgar automáticos | No | **No** (sin licencia). Evidencia del bloqueo de la telefonía |
| [JPG-Consulting/IPhoneNotifications](https://github.com/JPG-Consulting/IPhoneNotifications) | No indicada | Antiguo | Windows 10 | ANCS a notificaciones de Windows | **Sí** (LightBlue o Aha NP para vincular) | No. Muestra el método antiguo, que la 1.0 de unlock-windows-with-iphone ya no necesita |

No hay que confundir los ejemplos de protocolo (Nordic, Silicon Labs, ESP32-ANCS) con integraciones de iPhone en un PC. Sirven para entender ANCS, no prueban nada en Windows.

## 4. Matriz de compatibilidad

| Función | Tecnología | Sin app iOS | API pública Windows | Compatibilidad iPhone | Riesgos | Estado |
|---|---|---|---|---|---|---|
| Batería | Battery Service 0x180F (Battery Level 0x2A19, lectura y aviso) sobre la conexión LE vinculada. Alternativa: HFP `battchg` 0–5 (no usable: canal en manos de Windows) | Sí | Sí: `Windows.Devices.Bluetooth.GenericAttributeProfile` (aplicación de escritorio sin paquete, como hace bleak) | Publicado por iOS según iphone-companion (Windows) y BlueFerry (iOS 27). **Sin estado de carga** (BAS no lo tiene). La notificación «Batería» de ANCS no existe | El servicio puede tardar en publicarse tras reiniciar el iPhone. La dirección LE rota (hay que identificar por vínculo, no por dirección) | **Viable técnicamente** |
| Notificaciones | ANCS (+ Service Changed) | Sí | Sí, GATT cliente | iOS 7+. Requiere vínculo cifrado y «Compartir notificaciones del sistema» activado para el PC. Las notificaciones «entregadas sin avisar» o desactivadas no se envían | Reconexión tras reiniciar el iPhone (repetir descubrimiento), caché de servicios de Windows, conflicto si Phone Link usa el mismo iPhone, adaptadores sin LE Secure Connections | **Viable técnicamente** |
| Llamadas: aviso, contestar, rechazar | ANCS categoría IncomingCall + PerformNotificationAction | Sí | Sí, GATT cliente | Funciona en iphone-companion (Windows). El audio sigue en el iPhone | La duración y el estado («en curso») se deducen, no se reciben | **Viable técnicamente** |
| Llamadas: marcar, colgar, audio en el PC | HFP (rol Hands-Free) | Sí | **Parcial**: `PhoneLineTransportDevice` + `PhoneLine`, con la capacidad **restringida** `phoneLineTransportManagement` y paquete MSIX. La pila HFP de Windows ya ocupa el perfil; uno propio por RFCOMM chocaría con ella y el audio SCO requiere controlador | iOS es Audio Gateway HFP | Acceso denegado por el sistema (dos informes), sin audio (MyPhone), dependencia de un permiso que solo Microsoft concede, dos pilas peleando por el mismo perfil | **Experimental** (control) · **Bloqueado** (audio sin controlador propio) |
| SMS / iMessage | MAP 1.x sobre RFCOMM/OBEX (MAS 0x1132; avisos con un servidor MNS 0x1133 en el PC). Además ANCS para el aviso | Sí | Sí: `Windows.Devices.Bluetooth.Rfcomm` (cliente RFCOMM y `RfcommServiceProvider` para MNS) + OBEX propio | Leer y enviar desde iOS 13. Requiere «Mostrar notificaciones» (y «Sincronizar contactos» para nombres). Sin grupos, sin adjuntos, sin reacciones, historial limitado (lo que el iPhone ofrece por MAP), SMS e iMessage indistinguibles | Que Windows no deje abrir el canal MAP del iPhone desde una app (sin comprobar: lo responde la prueba 8b) o que Windows ya registre su propio servidor MNS. OBEX y MAP hay que implementarlos desde cero | **Experimental** |

## 5. Restricciones de Apple y de Windows

**Apple (iOS):**
- Nada de esto necesita APIs privadas: ANCS y AMS son especificaciones públicas de Apple, y BAS, HFP y MAP son estándares del Bluetooth SIG.
- Lo que **no** es accesible sin APIs privadas, por ningún medio: portapapeles universal, Handoff, AirDrop, historial completo de iMessage, chats de grupo, adjuntos, reacciones y responder a notificaciones de otras apps (ANCS es solo lectura más dos acciones).
- Permisos que da el usuario en el iPhone, en Ajustes › Bluetooth › (i) junto al PC: «Compartir notificaciones del sistema» (ANCS), «Mostrar notificaciones» (MAP), «Sincronizar contactos» (PBAP, para ver nombres). Los nombres exactos pueden variar con la versión de iOS.
- Una **app complementaria no arregla** llamadas, SMS, iMessage ni las notificaciones de otras apps: iOS no permite a ninguna app leerlos ni enviarlos. Solo aportaría estado de carga, transferencia de archivos por la red local y portapapeles manual, y vive suspendida en segundo plano. **No está justificada** para estas cuatro funciones.

**Windows 11:**
- Una aplicación de escritorio sin paquete, como WinMac, puede usar el cliente GATT y RFCOMM de WinRT con dispositivos ya emparejados. El ejemplo es iphone-companion con bleak.
- El emparejamiento se hace con el de Windows (Configuración › Bluetooth › Agregar dispositivo, comparación numérica). No hace falta que WinMac empareje por su cuenta, ni el rol periférico, ni anunciarse.
- **Telefonía**: `phoneLineTransportManagement` es restringida. En la práctica la usa Phone Link. WinMac necesitaría identidad de paquete (MSIX o paquete disperso) y, aun así, hay informes de acceso denegado y de que no da audio.
- No se proponen controladores propios: un perfil HFP de usuario con audio exigiría un controlador de perfil, que está fuera de lo permitido.
- Adaptador: hace falta LE + rol central + Classic. Recomendable: LE Secure Connections. La prueba 1 lo comprueba.

## 6. Pruebas de concepto (para tu PC, en orden)

Herramienta: **`ConnectProbe.exe`**. Es una consola autocontenida para Windows x64 que no necesita instalar nada, y su código va junto en el ZIP. Está **fuera de los repositorios** de WinMac y NotchWin.
- **Solo lectura**: no empareja ni desempareja, no cambia ajustes, no guarda nada (salvo `--out`), no usa la red.
- Por defecto no muestra el contenido de notificaciones ni de mensajes. Las direcciones salen recortadas.
- **No está probada con hardware**: se compiló, pero nadie la ha ejecutado contra un iPhone. Lo que diga en tu PC es la evidencia.

Abre «Terminal» o «Símbolo del sistema» en la carpeta donde la descomprimas.

| # | Qué haces | Comando | Resultado esperado si es viable | Qué significa si no |
|---|---|---|---|---|
| 1 | Nada (solo leer el adaptador) | `ConnectProbe adapter` | `Classic: True`, `Low Energy: True`, `Rol central: True`; idealmente `LE Secure Connections: True` | Sin LE o sin rol central: ANCS y batería imposibles con este adaptador (haría falta otro, USB) |
| 2 | Cierra Phone Link si lo tienes. En el iPhone abre Ajustes › Bluetooth (queda visible). En el PC: Configuración › Bluetooth y dispositivos › Agregar dispositivo › Bluetooth › tu iPhone. **Compara el código en las dos pantallas** y acepta en ambas. Si el iPhone pregunta por compartir notificaciones o contactos, **permítelo** | — | El iPhone aparece como emparejado en Windows | — |
| 3 | Comprobar el vínculo | `ConnectProbe devices` | El iPhone aparece en **Classic y en Low Energy**, con protección `Encryption…` | Solo en Classic: ANCS y batería no accesibles con ese emparejamiento (repetir; anotar el adaptador) |
| 4–5 | iPhone desbloqueado y cerca | `ConnectProbe gatt --name "iPhone"` (usa el nombre de tu iPhone) | Lista con `Battery Service`, `ANCS`, `AMS`. `Batería: NN % (lectura real)`. Al enchufar o desenchufar en los 20 s: `batería notificada` | Sin `Battery Service`: anotar la versión de iOS. `AccessDenied`: olvidar en los dos lados y emparejar otra vez |
| 6–7 | Ten a mano otro teléfono o una app que te mande una notificación | `ConnectProbe ancs --name "iPhone" --seconds 60` | `Suscripción a Notification Source: Success`, un resumen de las ya existentes y, al llegar una nueva, la línea con categoría y app (`com.apple.MobileSMS`…). Con `--titles` también el título | Error de autorización: en el iPhone, Ajustes › Bluetooth › (i) › «Compartir notificaciones del sistema». Si no existe el interruptor, el vínculo no está cifrado |
| 6b | Llámate desde otro teléfono durante la prueba 6 | (la misma) | Línea `nueva · Llamada entrante … · acción positiva · acción negativa` → contestar/rechazar desde el PC es posible | Sin acciones: solo se podría avisar |
| 8 | — | `ConnectProbe classic --name "iPhone"` | Servicios `HFP Audio Gateway`, `MAP Message Access Server`, `PBAP`. Telefonía: probablemente `DeniedBySystem` | Sin MAP: activar «Mostrar notificaciones» para el PC en el iPhone. `Allowed` en telefonía sería una sorpresa positiva (anótalo) |
| 8b | Con «Mostrar notificaciones» activado en el iPhone | `ConnectProbe map --name "iPhone"` | `Respuesta OBEX a CONNECT: 0xA0 (éxito…)` y `DISCONNECT: 0xA0`. No se lee ni lista ningún mensaje | `0xC3`: falta permiso en el iPhone. Si no se abre el canal RFCOMM: Windows no deja usar MAP desde una app (los mensajes quedarían bloqueados) |
| — | Todo seguido, con informe | `ConnectProbe all --name "iPhone" --out informe.txt` | Un `informe.txt` para compartir (sin contenido de notificaciones si no usas `--titles`) | — |

Para deshacerlo todo: Configuración › Bluetooth › tu iPhone › Quitar dispositivo, y en el iPhone, Ajustes › Bluetooth › (i) › Omitir dispositivo. No queda nada más instalado.

## 6b. Resultados reales — primera ronda (2026-11-03, PC del usuario, Windows 11 compilación 26300, iPhone 17 Pro)

| Prueba | Resultado | Conclusión |
|---|---|---|
| 1 Adaptador | Classic, LE, rol central y periférico, LE y Classic Secure Connections, publicidad extendida: **todo True** | El Bluetooth integrado sirve para todo lo investigado |
| 2–3 Emparejamiento | El iPhone aparece en **Classic** (conectado) y en **LE** (dirección aleatoria, protección `EncryptionAndAuthentication`) | El emparejamiento normal de Windows crea también el vínculo LE cifrado y autenticado |
| 4–5 GATT y batería | Servicios publicados: Battery Service, ANCS, AMS, Current Time, Device Information (más Continuity y Nearby, privados, que no se usan, y servicios de apps de terceros, `0xFE15` y otros). **Batería: 75 % (lectura real)**; suscripción a cambios: Success | **Batería: verificada** (lectura y suscripción; falta ver un cambio de valor) |
| 6–7 ANCS | Servicio presente, Notification Source, Control Point y Data Source presentes; **suscripciones aceptadas (Success)**, sin error de autorización. En 30 s llegaron **0** eventos, ni siquiera de las notificaciones ya existentes | Acceso autorizado. Falta ver eventos: segunda ronda de 2 min con la conexión LE mantenida (`GattSession.MaintainConnection`) y comprobación del permiso en el iPhone |
| 8 Classic | El iPhone publica **MAP** (0x1132), **PBAP** (0x112F) y **HFP AG** (0x111F), con cifrado y autenticación (más dos servicios de Apple, iAP, que no se usan). Telefonía de Windows: **`DeniedBySystem`**, no registrado | **Llamadas con audio y control HFP desde WinMac: bloqueado** (confirmado en tu PC). Contestar y rechazar sigue siendo posible por ANCS |
| 8b MAP | **El canal RFCOMM de MAP se abre** desde una aplicación de escritorio normal. Respuesta OBEX a CONNECT: `0xC3` (prohibido) | Windows no bloquea MAP. El iPhone lo rechaza porque falta su permiso para el PC («Mostrar notificaciones» en Ajustes › Bluetooth › (i)). Se repite en la segunda ronda |

Estados actualizados: **Batería → Verificado** · **Notificaciones → Viable técnicamente** (autorizado, eventos pendientes) · **Llamadas (aviso y contestar por ANCS) → Viable técnicamente** · **Llamadas (HFP, audio en el PC) → Bloqueado** · **SMS/iMessage → Viable técnicamente** (el canal se abre; falta el permiso del iPhone).

## 6c. Resultados reales — segunda ronda (mismo día, conexión LE mantenida, permisos del iPhone activados)

| Prueba | Resultado | Conclusión |
|---|---|---|
| ANCS | Sesión GATT mantenida y conexión LE estable durante 2 min. Al suscribirse llegaron las **322 notificaciones ya existentes** (Otra 112, Social 182, Noticias 15, Finanzas 8, Entretenimiento 7, Ubicación 2…). Después, en directo: WhatsApp (3), Telegram, Cartera (`com.apple.Passbook`), y **eliminadas** cuando se borraban en el iPhone. Cada nueva traía su app (Get Notification Attributes por Control Point y Data Source) | **Notificaciones: verificado** (nuevas, eliminadas, ya existentes, identificación de la app). El contenido (título y texto) no se pidió por privacidad: los atributos llegan por el mismo canal ya probado |
| Llamada | **Llamada entrante** detectada al instante (`com.apple.mobilephone`) con **acción positiva y negativa** disponibles; al colgar desde el otro teléfono: eliminada → **llamada perdida**. Un `0xA2` (parámetro inválido) al pedir los datos de una notificación que el iPhone ya había borrado: carrera esperable, hay que tolerarla | **Aviso de llamada: verificado.** Contestar o rechazar (Perform Notification Action) queda viable técnicamente: las acciones están anunciadas, pero no se ejecutaron |
| MAP | OBEX CONNECT `0xA0` (éxito) y DISCONNECT `0xA0` tras activar el permiso en el iPhone. No se leyó ni listó ningún mensaje | **Sesión MAP: verificada** desde una aplicación de escritorio con API pública. Leer el listado y enviar siguen sin probar |

**Matriz final:** Batería → **Verificado** · Notificaciones → **Verificado** · Llamadas (aviso) → **Verificado**; contestar/rechazar → Viable técnicamente; audio y marcar en el PC (HFP) → **Bloqueado** (`DeniedBySystem`) · SMS/iMessage → **Viable técnicamente** (sesión MAP verificada; listado y envío pendientes).

Avisos para la implementación, aprendidos de las pruebas:
- `GattSession.MaintainConnection = true` es necesario: sin él no llegaron eventos en la primera ronda.
- Al suscribirse llegan todas las notificaciones guardadas en el iPhone (aquí 322). La primera carga debe ir en silencio: las marcadas PreExisting se meten en el centro sin avisos ni píldoras.
- Hay que tolerar `0xA2` cuando una notificación desaparece antes de pedir sus datos.

## 7. Arquitecturas candidatas

| Estrategia | Descripción | Valoración |
|---|---|---|
| **A — Nativa dentro de un módulo existente** | La Barra (o la Isla) habla Bluetooth directamente | Rechazada. Mezcla Bluetooth con interfaz, y un fallo de la pila o una espera larga afectaría a la barra. Rompe el diseño de módulos |
| **B — Módulo local `connect` supervisado por el host** (recomendada) | Un proceso más (`WinMac.Connect.App.exe`), como Bar, Dock e Isla: lanzado, vigilado y reiniciado por el host, **desactivado por defecto**. Gestiona emparejamiento ya hecho por Windows, conexión LE (ANCS, AMS, BAS) y, más adelante, RFCOMM (MAP). Publica por el canal IPC actual: `connect/state` (dispositivo, conexión, batería, última actualización), `connect/notifications` (instantánea con ids) y, en fases posteriores, `connect/messages` | Es la «integración nativa» que se pide, sin app en el iPhone y sin servicios de terceros, y encaja con el host único. En .NET con `net10.0-windows10.0.19041.0`, igual que el resto de WinMac |
| **C — App complementaria iOS** | Solo para lo que A y B no pueden hacer | **No justificada** para batería, notificaciones, llamadas ni mensajes (§5). Se reconsidera solo para estado de carga o archivos, si algún día se piden |

## 8. Propuesta de implementación (para decidir juntos)

1. **Primero:** batería + notificaciones ANCS (incluido el aviso de llamada con contestar/rechazar). AMS es opcional y viene en la misma conexión.
2. **Arquitectura:** estrategia B.
3. **APIs:** `Windows.Devices.Bluetooth` (adaptador, `BluetoothLEDevice`, `ConnectionStatusChanged`), `GenericAttributeProfile` (descubrimiento sin caché, Service Changed, CCCD, `WriteValueWithResultAsync`), `Windows.Devices.Enumeration` (emparejados, `DeviceWatcher`) y, en la fase de mensajes, `Rfcomm` + `StreamSocket`. Sin bibliotecas de terceros obligatorias. Los proyectos MIT se usan como referencia; BlueFerry (GPL) no se copia.
4. **Componentes nuevos:**
   - `WinMac.Connect.Core`, puro y con pruebas: analizador ANCS (eventos, atributos troceados, reintentos), máquina de estados de conexión, modelo de dispositivo y caché en memoria.
   - `WinMac.Connect.Win`: adaptador WinRT.
   - `WinMac.Connect.App`: el proceso.
   - Un tópico IPC nuevo.
   - Página de Ajustes «WinMac Connect» real: elegir el iPhone de los emparejados en Windows, interruptores por función, «Olvidar y borrar datos».
5. **Barra superior:** un indicador opcional con el icono del iPhone y el porcentaje, que se ve **solo** si hay lectura real y reciente. Desconectado: «iPhone · sin conexión desde hh:mm» en el desplegable, nunca un porcentaje viejo como actual. Integrado en el sistema de iconos actual, sin duplicar el de la batería del PC.
6. **Notificaciones y NotchWin:** las del iPhone entran en el `NotificationStore` actual como **otra fuente** («iPhone · app»), con ids `ancs:<sesión>:<uid>`. La Isla las recibe por la instantánea `NotchFeed.Snapshot` que ya existe, sin tocar su aspecto. Las eliminadas en el iPhone (evento 2) desaparecen. Las «ya existentes» (PreExisting) no hacen sonar avisos. Si Windows ya muestra la misma notificación (p. ej. Phone Link), se deduplica por app + título + hora.
7. **Emparejamiento:** el de Windows. WinMac lo explica y abre Configuración › Bluetooth, comprueba que el vínculo existe en LE y Classic, y guía para activar los interruptores del iPhone. WinMac no crea vínculos propios.
8. **Desconexiones:** `ConnectionStatusChanged` y reintentos con espera creciente. Al reconectar: descubrimiento sin caché y nueva suscripción (los uid de ANCS no sobreviven a la sesión). Sin sondeo cuando el iPhone no está. El indicador pasa a «sin conexión» con la hora de la última lectura válida.
9. **Datos:** solo en memoria por defecto. Nada a disco ni a la red. Sin credenciales de Apple. Botón «Olvidar» que borra todo lo de WinMac. Las notificaciones del iPhone siguen las mismas reglas que las de Windows en WinMac (sin registrar su contenido).
10. **Pendiente para después:** mensajes MAP (si la prueba 8b da 0xA0), contactos PBAP para poner nombres. **No se hará**: audio de llamadas en el PC, grupos, adjuntos, portapapeles universal, Handoff.
11. **Pruebas reales que necesitas hacer:** las del §6, y anotar la versión de iOS y el modelo del adaptador (Administrador de dispositivos › Bluetooth).
12. **Complejidad y riesgos:**

| Fase | Contenido | Complejidad | Riesgo principal |
|---|---|---|---|
| 0 | Pruebas del §6 en tu PC | — | Que el adaptador integrado no cumpla (se resuelve con uno USB) |
| 1 | Módulo, batería, estado de conexión, indicador | Media | Reconexión fiable y caché de servicios de Windows |
| 2 | ANCS en el centro de notificaciones y en la Isla; aviso de llamada con contestar/rechazar | Media-alta | Sesiones ANCS, deduplicación, privacidad |
| 3 | AMS en «Ahora suena» (opcional) | Media | No duplicar con la sesión multimedia de Windows |
| 4 | Mensajes por MAP | Alta (OBEX + MAP + MNS desde cero) | Que Windows no deje usar MAP o MNS; límites de iOS |
| — | Llamadas con audio en el PC | — | Bloqueado por la capacidad restringida |

## 9. Riesgos técnicos

- Variación entre adaptadores y controladores: Realtek y CSR dan problemas en Linux, y en Windows está sin datos. Mitigación: la prueba 1 y un adaptador USB.
- iOS puede cambiar la publicación de los servicios o los nombres de los interruptores. Mitigación: Service Changed, descubrimiento sin caché y textos genéricos.
- Si Phone Link está vinculado al mismo iPhone, pueden pelear por las conexiones. Mitigación: avisar y no usarlos a la vez.
- Batería del iPhone: una conexión LE mantenida tiene coste bajo pero real. Mitigación: avisos (sin sondeo) y solo con la función activada.
- Privacidad: las notificaciones contienen datos sensibles. Mitigación: todo en memoria, nada en los registros y desactivado por defecto.
