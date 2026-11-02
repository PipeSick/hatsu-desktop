# 30 · Extensiones de WinMac (API v1)

Las extensiones son **programas aparte** que WinMac inicia, vigila y detiene. Opcionales: ninguna viene activada.

## Modelo de seguridad
- **Canal propio**: un segundo *named pipe* solo del usuario actual (`<pipe>-ext`). Las extensiones nunca reciben el token del canal principal.
- **Un token por lanzamiento**, ligado al PID que inició WinMac: ninguna extensión puede hacerse pasar por otra.
- **Sin reenvío**: lo que envía una extensión solo llega a WinMac, nunca a otras extensiones ni a los módulos.
- **Permisos explícitos** en el manifiesto; el intermediario (`ExtensionBroker`) descarta todo lo demás. Se muestran en Ajustes antes de activarla.
- **Límites**: 8 KB por mensaje, 10 mensajes/s, 1 aviso cada 5 s; 20 mensajes no válidos ⇒ se detiene.
- **Aislamiento de fallos**: si se cae, se reinicia como mucho 3 veces (2, 4, 8 s) y luego queda detenida con el motivo. El host, la Barra y la Isla no se ven afectados.
- **Límite honesto**: se ejecuta con los permisos del usuario. Aísla a WinMac de sus fallos y limita lo que puede pedirle a WinMac; **no** es un entorno aislado para código desconocido. Instala solo extensiones en las que confíes. Sin red expuesta: el canal es local.

## Manifiesto (`extension.json`)
```json
{ "id": "pomodoro", "name": "Pomodoro", "version": "1.0.0", "api": 1,
  "entry": "MiExtension.exe", "args": ["pomodoro"],
  "permissions": ["activity", "notify"], "description": "…" }
```
- `id`: minúsculas, números y guiones (3–40); la carpeta debe llamarse igual.
- `version`: x.y.z. `api`: 1 (WinMac rechaza otras con un mensaje claro).
- `entry`: un `.exe` **dentro** de su carpeta. `args`: hasta 8 textos.
- `permissions`: `activity` (actividades en la Isla), `notify` (avisos breves en la Isla). Cualquier otro valor invalida el manifiesto.

## API (SDK `WinMac.Extensions.Sdk`)
```csharp
var ext = WinMacExtension.Connect(args, "pomodoro", "1.0.0");   // null si no la inició WinMac
ext.ShutdownRequested += () => /* salir */;
ext.ShowActivity("focus", "Concentración", "Quedan 24:59", progress: 0.01);
ext.FinishActivity("focus");
ext.Notify("Pomodoro terminado", "Descansa cinco minutos");
```
Mensajes en el canal (por si se usa otro lenguaje): evento `ext.activity` `{op: upsert|finish|remove, id, title, subtitle?, progress? 0–1, state?}`
y evento `ext.notify` `{title, body?}`. Los ids se convierten en `ext:<id>:<tuyo>` (no pueden tocar actividades de la Isla ni de otras extensiones).

## Instalar, activar, desinstalar
Ajustes › **Extensiones**: escribir la carpeta (con `extension.json`) en «Instalar» ⇒ se copia a `%LOCALAPPDATA%\WinMac\extensions\<id>` **desactivada**.
El interruptor la inicia o la detiene al momento. «Desinstalar» la detiene y borra **solo su carpeta**. Estado: `extensions.json` (lista de ids activos).

## Demostraciones (usan la API real; sin red ni credenciales)
`dist/WinMac/extensions/pomodoro` (temporizador con progreso real y aviso al terminar) y `.../uptime` (tiempo que lleva Windows encendido).

## Versionado
`api` sube solo con cambios incompatibles; WinMac v2 admite la 1. Campos nuevos opcionales no cambian la versión.
