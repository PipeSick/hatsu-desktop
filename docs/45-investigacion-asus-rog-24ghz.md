# Investigación: batería de ASUS ROG por 2,4 GHz para WinMac Peripherals

Estado: **solo investigación**. No se modificó código funcional de WinMac ni NotchWin ni el módulo Peripherals. Archivos añadidos (aislados, rama `feat/research-asus-rog-24ghz` sobre `dbad8c9`):
- `docs/45-investigacion-asus-rog-24ghz.md` (este informe)
- `tools/manual/Get-RogReceiverInfo.ps1` (diagnóstico local de **solo lectura**; ver §6)

> Honestidad sobre las fuentes: la lectura de código de terceros se hizo con un lector automático de páginas que resume, no con un clon local. Las cifras de bytes/offsets de §3 son **hipótesis a verificar leyendo el código fuente original** (enlaces abajo) antes de implementar nada. No se pudo acceder a `www.asus.com` ni a `g-helper.com` desde este entorno (red restringida), así que no hay evidencia de primera mano de ASUS. Nada de esto se ha probado con tus dispositivos.

## 1. Resumen ejecutivo
- **No hay API pública de ASUS para la batería** de periféricos. El SDK público que se encontró (Aura SDK) es de iluminación. Armoury Crate no documenta ninguna interfaz local de lectura (no se encontró; lo no encontrado no prueba que no exista).
- **Sí hay evidencia comunitaria concreta** de que estos tres modelos exactos están implementados en **G-Helper** (Windows, GPLv3) con lectura de batería por HID, incluso detrás del **ROG Omni Receiver** (VID `0x0B05`, PID `0x1ACE`, compartido por varios dispositivos). Es evidencia de **código de terceros**, no documentación del fabricante ni verificación en tu hardware.
- **libratbag** (MIT) soporta el receptor Omni y varios ratones ASUS, pero **no lee batería** (solo perfiles, DPI, LED…); sirve de referencia de transporte, no de batería.
- El mayor riesgo: la lectura es **una petición enviada al dispositivo** (no un informe pasivo) en un canal propietario compartido con Armoury Crate. Hay que probarla con autorización explícita y un procedimiento controlado (§9).
- Licencias: WinMac es Apache-2.0; G-Helper es GPLv3 → **no se puede copiar su código** a WinMac. Habría que reimplementar de forma independiente (y con revisión legal) o usar un proveedor externo opcional.

## 2. Qué se sabe del hardware (con fuente)
| Dato | Fuente | Tipo |
|---|---|---|
| Los tres son multimodo (cable / Bluetooth / 2,4 GHz) y traen receptor | Fichas y minoristas: [Keris II Origin](https://rog.asus.com/mice-mouse-pads/mice/wireless/rog-keris-ii-origin/spec), [Scope II 96 Wireless](https://rog.asus.com/au/keyboards/keyboards/compact/rog-strix-scope-ii-96-wireless/spec/), [Delta II (minorista)](https://www.microcenter.com/quickView/683470/asus-rog-delta-ii-wireless-gaming-headset) | Ficha del fabricante / tienda |
| Receptor **ROG Omni**: VID `0B05` PID `1ACE`; interfaz HID `MI_01` visible en listados de controladores | [DriverMax: ROG OMNI RECEIVER](https://drivermax.com/ROG-OMNI-RECEIVER-ASUSTeK-Computer-Inc_HID-VID-0B05-PID-1ACE-MI-01-1_00_50_0-2023-07-14-4682794-driver.htm); [libratbag `asus-rog-omni-receiver.device`](https://github.com/libratbag/libratbag/blob/master/data/devices/asus-rog-omni-receiver.device) (`DeviceMatch=usb:0b05:1ace`) | Agregador / código MIT |
| El receptor Omni es **compartido** por varios ratones (mismo USB ID); el modelo real se identifica por una consulta | Comentarios de [`driver-asus.c`](https://github.com/libratbag/libratbag/blob/master/src/driver-asus.c) y [`asus.h`](https://github.com/libratbag/libratbag/blob/master/src/asus.h) (`ASUS_QUIRK_OMNI_RECEIVER`, "report ID 0x03"); [debate de G-Helper #4321](https://github.com/seerge/g-helper/discussions/4321) | Código MIT / comunidad |
| Delta II es tri-modo con **dongle USB-C 2,4 GHz propio** ("SpeedNova") | Minoristas ([Galaxus](https://www.galaxus.be/en/s1/product/asus-rog-delta-ii-cable-wireless-gaming-headset-47185717), [Micro Center](https://www.microcenter.com/quickView/683470/asus-rog-delta-ii-wireless-gaming-headset)) | Tienda |
| Strix Scope II 96 Wireless: ID de interfaz HID `0B05:1AAE` en listados de controladores | [DriverMax](https://www.drivermax.com/ROG-STRIX-SCOPE-II-96-WIRELESS-ASUSTeK-Computer-Inc_HID-VID-0B05-PID-1AAE-MI-02-Col04-1_00_52_0-2023-08-24-4852820-driver.htm) (no dice si es teclado o receptor) | Agregador |

**No asumir** que el PID por cable coincide con el del receptor: en el código comunitario el Keris II Origin por cable es PID `1C0C` y detrás del Omni es `1ACE`.

## 3. Lo que muestran los proyectos comunitarios (hipótesis a verificar en el código original)
Repositorio: [seerge/g-helper](https://github.com/seerge/g-helper) (carpeta `app/Peripherals/`), licencia **GPLv3** (verificada en su `LICENSE`).

| Modelo | Clase en G-Helper | VID:PID | Interfaz / informe | Lectura de batería (según resumen del código) |
|---|---|---|---|---|
| Keris II Origin (2,4 GHz Omni) | [`KerisIIOriginOmni`](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Mouse/Models/KerisIIOrigin.cs) | `0B05:1ACE` | `mi_02&col03`, report ID `0x03` | Petición `{reportId, 0x12, 0x07}`; respuesta válida si bytes 1–2 repiten `12 07`; % = byte 5; carga = byte 10 > 0. Con el ratón dormido/apagado el dongle no informa batería. ([`AsusMouse.cs`](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Mouse/AsusMouse.cs)) |
| Strix Scope II 96 Wireless (Omni) | [`StrixScopeII96WirelessOmni`](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Keyboard/Models/StrixScopeII.cs) | `0B05:1ACE` (también `1AAE` y RX `1B78`) | `mi_02&col02`, report ID `0x02`, paquete 64 B | `HasBattery = true`. Petición `{reportId, 0x12, 0x01}`; % = byte 6; carga = byte 9 == 1. ([`AsusKeyboard.cs`](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Keyboard/AsusKeyboard.cs)) |
| Delta II | [`DeltaII`](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Headset/Models/DeltaII.cs) | `0B05:1AFA` (KJP: `1D41`) | Página de uso `0xFF00`, report ID `0xCC`, salida ≥ 64 B | Petición `0x12 0x07`; % = byte 6 (válido si ≤ 100); carga: petición `0x12 0x08`, byte 5 == 1. ([`AsusHeadset.cs`](https://github.com/seerge/g-helper/blob/main/app/Peripherals/Headset/AsusHeadset.cs)) |

Observaciones: (1) **los offsets difieren entre ratón, teclado y auriculares** (5/6/6; carga en 10/9/5): una implementación común sería un error; (2) la identificación del modelo detrás del Omni es un problema abierto incluso para G-Helper (confunde modelos con firmas parecidas, [debate #4321](https://github.com/seerge/g-helper/discussions/4321)); (3) el código de conexión solo hace consultas (`0x12`…) y reserva `0x50/0x51` para escrituras: **esa separación es lo que haría seguro el uso, pero hay que respetarla literalmente**; (4) un tercero lo confirma para otro ratón (Pugio II) con la misma petición `00 12 07` ([herramienta de batería de Pugio II](https://google.18789066745.workers.dev/dkajan19/Asus-ROG-Pugio-II-Battery-Status), espejo no oficial); (5) G-Helper indica que solo una app puede hablar con la interfaz HID a la vez (según la web de G-Helper, vista solo en un resumen de búsqueda): **posible conflicto con Armoury Crate**, sin verificar.
**Delta II**: no está claro si `1AFA` es el PID del dongle 2,4 GHz, del auricular por cable o ambos; los resúmenes no lo dicen.

Otros proyectos: [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) (iluminación; sin evidencia de batería ASUS en lo encontrado), [libratbag](https://github.com/libratbag/libratbag) (MIT; ver arriba). **OpenRazer, Solaar y OpenLinkHub no se revisaron en esta pasada** (otros fabricantes; solo servirían de referencia arquitectónica).

## 4. Armoury Crate
| Pregunta | Resultado | Nivel |
|---|---|---|
| ¿API local documentada para batería? | No encontrada | Sin evidencia |
| ¿SDK oficial? | Existe Aura SDK ([guía v3.1](https://www.asus.com/microsite/aurareadydevportal/index.html)): COM/DLL para **iluminación**; nada de batería en lo visto | Documentado (iluminación) |
| ¿Servicios/IPC de lectura? | Desconocido; el script de §6 lista nombres de servicios y procesos del fabricante en TU equipo | Pendiente de diagnóstico |
| ¿Cómo obtiene la batería? | No documentado. La hipótesis razonable (y coherente con G-Helper) es la misma consulta HID propietaria | Hipótesis |
No se debe modificar, interceptar ni reemplazar sus servicios; no se asume que usar sus interfaces internas sea lícito.

## 5. Opciones
| | Viabilidad | Riesgos | Mantenimiento | Compatibilidad | Software extra |
|---|---|---|---|---|---|
| **A. HID directo** (mismo canal que Armoury Crate) | Media: existe evidencia por modelo, no verificada | Una petición mal formada podría interpretarse como orden; posible conflicto de acceso con Armoury Crate; modelo equivocado detrás del Omni | Alto: protocolo propietario, cambia con firmware | Por modelo/PID; offsets distintos por tipo | Ninguno |
| **B. Integración de solo lectura con Armoury Crate** | Baja: no hay API documentada | Depender de interfaz interna no documentada; licencia/ToS | Muy alto | Solo con AC instalado | Armoury Crate |
| **C. Adaptador con protocolo comunitario** | Media (equivale a A, con la referencia de G-Helper) | **GPLv3 vs Apache-2.0**: no copiar; reimplementación independiente y revisión legal | Alto | Como A | Ninguno |
| **D. Proveedor externo opcional** | Baja ahora: no se encontró una interfaz estable de G-Helper u otra herramienta para consumir | Dependencia opcional frágil | Medio | Depende | La herramienta externa |

## 6. Diagnóstico local (sin modificar nada)
`tools/manual/Get-RogReceiverInfo.ps1` (PowerShell 5.1+; **sin administrador**):
```
powershell -ExecutionPolicy Bypass -File .\tools\manual\Get-RogReceiverInfo.ps1 -IncludeHidCaps -OutDir .\diag
```
Recopila solo: dispositivos PnP presentes con VID `0B05` (nombre, clase, estado, servicio, proveedor y versión del controlador, IDs de hardware; el tramo final del identificador de instancia se sustituye por `<id>`); con `-IncludeHidCaps`, por interfaz HID de ASUS, Usage Page/Usage y longitudes de informe **ya en caché de Windows** (se abre con acceso 0, sin lectura ni escritura, sin pedir serie ni cadena de producto, sin enviar informes); nombres/estado de servicios y nombres de proceso que contengan ASUS/Armoury/ROG/Aura. **No** recopila series, nombre de equipo/usuario, rutas, claves, tokens ni contenido de informes. Produce `rog-diagnostico.txt` y `.json`; revísalos antes de compartirlos. No se ha podido ejecutar aquí (sin Windows): el CI solo comprueba que el script **analiza** sin errores de sintaxis.

## 7. Matriz de compatibilidad (2,4 GHz)
Estados: Confirmado con hardware real · Documentado para el modelo exacto, sin prueba local · Experimental · Hipótesis · No disponible · Bloqueado por falta de información.

| Dispositivo | Receptor identificado | Batería 2,4 GHz | Método posible | Evidencia | Estado |
|---|---|---|---|---|---|
| Keris II Origin | Omni `0B05:1ACE` (según código comunitario; **por verificar** en tu equipo) | % y carga (según G-Helper) | A/C: HID, `mi_02&col03`, report `0x03`, petición `12 07` | Código comunitario GPLv3 para el modelo exacto; 3 fuentes independientes del mismo formato de petición en ratones ASUS; sin documentación ASUS | **Documentado (comunitario) para el modelo exacto, sin prueba local** |
| Strix Scope II 96 | Omni `0B05:1ACE` compartido (según G-Helper); `1AAE`/`1B78` en otros modos | % y carga (según G-Helper) | A/C: HID, `mi_02&col02`, report `0x02`, petición `12 01` | Código comunitario GPLv3 para el modelo exacto; una sola fuente | **Documentado (comunitario) para el modelo exacto, sin prueba local** |
| Delta II | Dongle propio; PID no confirmado (¿`1AFA`?) | % y carga (según G-Helper, página `0xFF00`, report `0xCC`) | A/C: HID propietario, `12 07`/`12 08` | Código comunitario; el PID del dongle y el modo 2,4 GHz no están confirmados | **Hipótesis / bloqueado por falta de información** (PID del dongle) |

Ninguna fila está *Confirmada con hardware real*.

## 8. Arquitectura propuesta (cuando se autorice implementar)
- Proveedor opcional `AsusOmniProvider : IPeripheralProvider` en un ensamblado separado, **desactivado por defecto**, activado con aviso explícito («experimental; usa el canal propietario de ASUS»).
- Tabla cerrada de modelos (VID/PID/colección/report ID/offsets), sin sondeo de dispositivos desconocidos.
- **Lista blanca de una sola petición de lectura** por tipo; prohibido enviar cualquier byte inicial `0x50`/`0x51`; la respuesta solo es válida si repite la petición y el % ≤ 100; si no, la lectura es «desconocida» (nunca se infiere).
- Dormido/apagado/sin respuesta ⇒ `Connected=false` o batería desconocida (el almacén ya lo trata; el dongle sin dispositivo ≠ 0 %).
- Frecuencia ≥ 5 min y bajo demanda (panel abierto); sin hilos propios permanentes; plazo de 3 s; si hay conflicto de acceso (Armoury Crate abierto), se abandona la lectura y se informa «ocupado».
- Identificación del dispositivo detrás del Omni por la propia consulta del modelo, validada contra la tabla; si no coincide, no se muestra nada.
- Sin copia de código GPL: reimplementación a partir de la especificación observada y de nuestras propias capturas, con revisión legal previa.

## 9. Dificultad y orden
- **Keris II Origin — media**: mejor documentado (clase propia, colección y report ID); el protocolo de ratón se repite en otros modelos. **Abordar primero.**
- **Strix Scope II 96 — media-alta**: comparte receptor con el ratón; offsets y report ID distintos; una sola fuente.
- **Delta II — alta / bloqueada**: dongle propio, PID sin confirmar, protocolo de auriculares con página `0xFF00`; además interactúa con el dispositivo de audio del dongle.

## 10. Información y pruebas que faltan
1. Salida de `Get-RogReceiverInfo.ps1 -IncludeHidCaps` con los tres dispositivos conectados por 2,4 GHz (¿un solo Omni o receptores separados? PID real del dongle del Delta II; colecciones y longitudes de informe).
2. Si Armoury Crate está instalado y ejecutándose, y los nombres de sus servicios (el script los lista).
3. Versiones de firmware (de la app del fabricante) para anotar la ventana de compatibilidad.
4. **Autorización explícita** para una prueba controlada de *una* petición de lectura (`12 07` / `12 01`) con un arnés aparte, primero con Armoury Crate cerrado y luego abierto, comparando con el % que muestra Armoury Crate. Riesgo: bajo si solo se envía la petición de lectura y se aborta ante cualquier respuesta no esperada; no nulo (protocolo propietario y no documentado). Procedimiento a acordar antes.
5. Revisión del código original de G-Helper (offsets y manejo de dormido) leyendo el fuente directamente, y revisión legal de licencias.

## 11. No hecho
Ningún comando enviado a dispositivos reales; sin ingeniería inversa activa; sin captura USB; sin modificar el módulo Peripherals ni código funcional; sin PR, merge ni release.
