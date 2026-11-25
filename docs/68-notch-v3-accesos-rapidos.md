# Accesos rápidos de la Isla (Notch V3) — canal Isla → Barra

Rama: `feat/notch-v3-hatsu` (parte de `feat/hatsu-modulos-mission-snap`). Sin subir ni fusionar.

## Qué hace
La pestaña **Inicio** de Hatsu Notch muestra una fila de accesos rápidos. Los de Hatsu los ejecuta la Barra a petición de la Isla.

| Acceso | Acción real de Hatsu | Cuándo se ofrece |
|---|---|---|
| Captura | `winmac:capture:region` (la misma que Spotlight) | acción registrada y disponible |
| Snap | `snap:edit` (editor de zonas) | módulo Snap encendido |
| Mission Control | `mission:open` | módulo encendido |
| Live Text | `livetext:run` | módulo encendido |
| Gestures | abre *Ajustes › Atajos y gestos* (`host.cmd settings gestures`) | el host puede abrir Ajustes |
| Portapapeles | **propio de la Isla** (abre su pestaña); no pasa por este canal | pestaña encendida |

## Canal
- **Disponibilidad (Barra → Isla):** por el tópico de avisos de la Isla (`notch.feed`), `{ "kind": "quick", "available": ["capture", …] }`. Se envía al conectar la Isla y cuando cambia la lista (un módulo se enciende o apaga). Se calcula contra el **registro central de acciones**: una acción de un módulo apagado no está registrada, así que no se anuncia.
- **Petición (Isla → Barra):** tópico `notch.quick`, `{ "id": "capture" }`.

## Seguridad
- **Lista cerrada** (`WinMac.Bar.Notch.NotchQuick.Closed`): `capture`, `snap`, `mission`, `livetext`, `gestures`. Cualquier otro valor (incluidos ids de acciones del sistema como apagar o reiniciar) se ignora y se anota «acceso rápido ignorado».
- Lo que llega por el canal **nunca** se ejecuta como comando, ruta ni texto: se traduce a un id del registro (`NotchQuick.Actions`), y el registro vuelve a comprobar disponibilidad y aísla errores.
- Sin credenciales, sin red, sin permisos nuevos.

## Pruebas
`NotchQuickTests` (Bar.Core.Tests): lista cerrada, entradas hostiles, mapa sin acciones del sistema, disponibilidad según el registro. La ejecución real (que Captura abra la captura, etc.) **solo puede comprobarse en un Windows real**.
