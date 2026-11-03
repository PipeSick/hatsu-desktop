# WinMac Connect — Fases 1 y 2: batería, estado y notificaciones del iPhone

Fecha: 2026-11-03 · Rama `claude/relaxed-turing-cijzko` · Base: investigación y pruebas reales de `docs/39` · **Nada publicado.**
**CI verde = compilación y pruebas técnicas.** El comportamiento con tu iPhone 17 Pro lo validas tú con la lista del §6.

## 1. Qué se construyó

| Requisito | Implementación |
|---|---|
| Módulo opcional y desactivable | `WinMac.Connect.App.exe`: un proceso más, lanzado y vigilado por el host igual que la Barra y el Dock. **Desactivado por defecto** (Ajustes › General o Ajustes › WinMac Connect). Apagado no se inicia, no usa el Bluetooth y la Barra borra de memoria todo lo del iPhone. Si falla, el host lo reinicia con espera creciente y lo demás sigue igual. |
| Sin Phone Link ni app en el iPhone | API pública de Windows (`Windows.Devices.Bluetooth`, GATT cliente) sobre el emparejamiento normal de Windows. Sin servidores ni cuentas. |
| **Fase 1: batería y estado** | Battery Service (0x180F): lectura y aviso de cambios. Estados: conectado, conectando, sin conexión, ningún iPhone emparejado, Bluetooth apagado. Indicador opcional en la barra con el porcentaje **solo si está conectado y la lectura tiene menos de 15 min**; sin conexión, el icono atenuado y sin número. Panel del iPhone: nombre, estado, batería, «Actualizada hace…», última conexión. La carga no se muestra porque Bluetooth no la da, y el panel lo dice. |
| Desconexiones | Windows avisa del cambio de conexión y de servicios. Al volver, se rehacen la batería y las suscripciones (los UID de ANCS cambian). Si no hay conexión, se reintenta con espera creciente de 2 s a 60 s, más una comprobación barata cada 30 s. La lista del iPhone se mantiene 60 s por si el corte es breve y después se vacía. Se pide a Windows mantener la conexión (`GattSession.MaintainConnection`): sin eso no llegaban avisos en la prueba real. |
| **Fase 2: notificaciones** | ANCS: Notification Source, Control Point y Data Source, con reensamblado de respuestas troceadas y un comando a la vez. Las respuestas `0xA2` (notificación ya borrada) se toleran. |
| Importación silenciosa | Las que ya estaban (bandera PreExisting; en la prueba real, 322) entran en la lista sin un solo aviso. |
| Nuevas en tiempo real | Se avisan una vez, al llegar sus datos. Si el iPhone no los da en 3 s, el aviso sale con lo que haya. Las silenciosas solo se listan. |
| Eliminaciones | Lo que se borra en el iPhone desaparece de WinMac y de la Isla. «Ocultar» en WinMac solo oculta en esta sesión: **no borra nada del iPhone**. |
| Origen claro | En el Centro de Notificaciones, una sección propia **«iPhone»**, y cada tarjeta dice «iPhone · App». En la Isla, el mismo «iPhone · App» en la pestaña Notificaciones y en la píldora, **sin cambiar el aspecto de la Isla** (NotchWin no se modificó). Las del iPhone no tienen «Abrir»: una app del iPhone no se puede abrir desde el PC. |
| Privacidad configurable | «Contenido de las notificaciones»: Completo, Solo título o Solo la app. Lo que no se elige **ni siquiera se pide al iPhone**. Todo solo en memoria; nada en disco ni en los registros, que solo anotan estados. |
| Llamadas | Avisos de llamada **entrante** («Llamada entrante · nombre») y **perdida**. Se pueden desactivar. **Contestar y rechazar no se ofrecen**: el comando de ANCS (`PerformNotificationAction`) está investigado y codificado en el núcleo, pero sin validar con el iPhone, así que no hay ningún botón. No se intenta el audio de llamadas: Windows lo restringe (`DeniedBySystem`, docs/39). |
| Errores sin bloquear | Todo el trabajo Bluetooth va en un bucle propio dentro del módulo, en otro proceso. Cualquier excepción se captura y se reintenta. La Barra solo recibe mensajes del canal y los aplica en su hilo. |
| Recursos | Sin sondeo. La lista se publica como mucho cada 400 ms (la importación trae cientos de cambios). Máximo 400 notificaciones en memoria. Textos limitados (título y subtítulo 128 B, mensaje 512 B). |
| Mensajes | Fuera de esta fase: no se leen, envían ni borran mensajes. |

**AMS («Ahora suena»)**, investigado en paralelo: el iPhone lo publica (docs/39 §6b; servicio `89D3502B-…`). Encaja en el mismo módulo y la misma conexión: suscripción a Entity Update (Player, Track) y control con Remote Command. Queda para una fase 3, sin tocar la Isla salvo para alimentar su «En curso».

## 2. Arquitectura

```
iPhone ──BLE (BAS + ANCS)──▶ WinMac.Connect.App ──canal local del host──▶ Barra ──▶ indicador · panel iPhone · Centro de Notificaciones
                               (proceso aparte)    connect.state (retenido)     └──▶ Isla (notch.feed: lista combinada y píldoras)
                                                   connect.notifications (retenido)
                                                   connect.alert (evento)  ◀── config.connect (Ajustes)
```

- `src/WinMac.Connect.Core`: lógica pura, probada también en Linux.
  - `Ancs.cs`: protocolo y reensamblado.
  - `PhoneNotifications.cs`: el libro de notificaciones (importación silenciosa, avisos, eliminaciones, llamadas, límites).
  - `ConnectModel.cs`: ajustes, estado, frescura, formato del canal y reintentos.
- `src/WinMac.Connect.App`: `PhoneLink.cs` (adaptador WinRT con un único bucle de trabajo) y `Program.cs` (enlace con el host).
- La Barra (`BarHost.Connect.cs` y `PhoneInbox`) fusiona la lista para la Isla con las de Windows. Los ids de la Isla para el iPhone parten de 2⁴⁰ y llevan la sesión: nunca chocan con los de Windows.
- El host:
  - publica `config.connect`, la sección `connect` de winmac.json, que no hace falta migrar porque los campos nuevos toman su valor por defecto;
  - cuando el módulo se apaga o falla, publica un estado «apagado» para que nunca se muestre un estado viejo.

## 3. Ajustes › WinMac Connect

| Ajuste | Valor por defecto |
|---|---|
| Conectar con el iPhone (el módulo) | **Desactivado** |
| Nombre del iPhone | Vacío: el primer iPhone emparejado. El de la prueba se llama «iPhone de PipeSick». |
| Batería y estado de conexión | Activado |
| Indicador del iPhone en la barra superior | Activado (solo aparece con datos reales) |
| Notificaciones del iPhone | Activado |
| Avisos de llamadas | Activado |
| También en la Isla | Activado |
| Aviso emergente para las nuevas | Activado (respeta «No molestar», presentación y pantalla completa, como las de Windows) |
| Contenido de las notificaciones | Completo |

## 4. Pruebas automáticas

- **Núcleo de Connect (13 pruebas):**
  - eventos y comandos byte a byte según la especificación de Apple;
  - reensamblado en trozos de 7 bytes, peticiones rechazadas y basura;
  - fechas;
  - importación silenciosa de 322 notificaciones (los datos de la prueba real);
  - un solo aviso por notificación nueva y aviso tras espera;
  - las silenciosas no se avisan;
  - eliminaciones y nueva sesión;
  - llamada entrante que cuelga antes de mostrarse y llamada perdida;
  - límite de memoria;
  - frescura de la batería, ajustes y formato del canal;
  - reintentos.
- **Barra (6 pruebas nuevas):**
  - indicador solo con módulo y datos recientes (desconectado: sin número);
  - panel del iPhone con la antigüedad real;
  - sección iPhone identificada; ocultar una, una app o todas sin borrar del iPhone; lo borrado en el iPhone desaparece;
  - las tarjetas del iPhone no abren nada;
  - sin módulo no hay sección;
  - lista de la Isla sin choques de id y aviso de llamada.
- **Host:** módulo desactivado por defecto y opcional; página de Ajustes solo con controles reales (ni Phone Link ni contestar o rechazar); ajustes guardados y publicados al módulo; orden de arranque.
- Totales locales: Bar.Core 432 · Host.Core 127 · Connect.Core 13 · Dock.Core 148 · Ipc 31.

## 5. Limitaciones conocidas

- Sin validar con el iPhone real en esta versión del módulo. La herramienta `ConnectProbe` sí lo validó: batería, ANCS, llamadas y MAP.
- Estado de carga del iPhone: no existe por Bluetooth.
- Contestar y rechazar llamadas: no ofrecido hasta validarlo.
- Audio de llamadas: bloqueado por Windows.
- Mensajes: fase posterior.
- Si Phone Link también está vinculado al iPhone, pueden pelear por la conexión. Mejor no usarlos a la vez.
- Al reconectar, el iPhone reenvía todas sus notificaciones con identificadores nuevos: entran otra vez en silencio. Las ocultadas en WinMac antes del corte vuelven a verse.

## 6. Lista de validación con tu iPhone

1. Ajustes › WinMac Connect › activa «Conectar con el iPhone». En unos segundos debe aparecer el icono del iPhone en la barra con su porcentaje.
2. Clic en el icono: nombre, «Conectado», batería y «Actualizada hace…». Enchufa el cargador o espera a que baje un 1 %: el número debe cambiar solo.
3. Centro de Notificaciones (reloj): sección «iPhone» con tus notificaciones, sin ningún aviso emergente al importarlas.
4. Pide que te escriban por WhatsApp: aparece al momento en la sección iPhone y como píldora en la Isla («iPhone · WhatsApp»).
5. Borra esa notificación en el iPhone: desaparece de WinMac y de la Isla.
6. Llámate desde otro teléfono: aviso «Llamada entrante» con el nombre. Cuelga: «Llamada perdida». No hay botones de contestar ni rechazar.
7. Privacidad: cambia a «Solo la app»; las nuevas muestran solo «iPhone · App», sin título ni texto. Vuelve a «Completo».
8. Desconexión: apaga el Bluetooth del iPhone. El indicador pierde el número y el panel dice «Sin conexión» con la última lectura. Enciéndelo: vuelve solo.
9. Desactiva el módulo: el icono y la sección iPhone desaparecen. Reactívalo: vuelven.
10. Regresiones: notificaciones de Windows, la Isla (música y letras), el Dock y el mezclador de sonido siguen igual.

Registro del módulo: `%LOCALAPPDATA%\WinMac\logs\connect.log`. Solo contiene estados, nunca contenido.

## 7. Siguiente

- Pendiente de tu validación: las correcciones de la iteración 4 (docs/38 §7) y el mezclador de sonido siguen en el plan.
- Fases posteriores de Connect, por orden:
  1. AMS («Ahora suena»).
  2. Validar contestar y rechazar llamadas.
  3. Mensajes por MAP.
