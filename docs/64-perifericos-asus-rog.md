# 64 · Periféricos ASUS ROG por el receptor ROG OMNI

Estado: **implementado, probado en lógica pura; NO validado en Windows real con Hatsu**. La parte medida (el protocolo) sale de capturas hechas en el PC del propietario; el lector de Hatsu que las usa todavía no se ha ejecutado.

## 1. Qué se midió (OBSERVADO)
| Paso | Resultado |
|---|---|
| Diagnóstico de solo lectura (`Diagnose-Peripherals.ps1`) | El receptor ROG OMNI (USB `0B05:1ACE`) expone tres canales del fabricante de 64 bytes (páginas de uso `FF00`, `FF01`, `FF02`) y otros tres de solo entrada de 21 bytes (`FFC0`–`FFC2`). Ningún dispositivo informa batería por Bluetooth. Armoury Crate no ofrece una API pública. |
| Escucha pasiva de esos canales | Solo contestan cuando algo los provoca; en reposo no envían batería. |
| Captura USB pasiva de Armoury Crate (USBPcap) | Armoury Crate manda una consulta de 64 bytes por el endpoint interrupt OUT `0x03` y el receptor contesta por el IN `0x83`. |

## 2. El protocolo (OBSERVADO en 3 repeticiones cada uno)
| Dispositivo | Consulta (64 bytes, el resto ceros) | Respuesta | Dónde está la batería |
|---|---|---|---|
| Teclado (Strix Scope II 96) | `02 12 01 00 …` | `02 12 01 00 00 00 3C 02 01 00 14 3C xx 0F …` | byte 6 = `0x3C` = 60 % (Armoury Crate mostraba 60 %) |
| Ratón (Keris II Origin) | `03 12 07 00 …` | `03 12 07 00 00 61 02 14 xx 10 00 00 01 …` | byte 5 = `0x61` = 97 % (Armoury Crate mostraba 97 %) |

**INFERIDO, sin confirmar:** los bytes que cambian entre lecturas (teclado 12–13, ratón 8–9, little-endian) valen ≈ 4,0–4,1 V si son milivoltios: probablemente la tensión de la batería. Hatsu **no** los usa.

## 3. Qué hace Hatsu
- Ajustes › Barra superior › Periféricos › «Leer la batería de periféricos ASUS ROG»: **apagado por defecto**. Es el único lector que **envía** algo al dispositivo (la misma consulta de Armoury Crate); los demás solo leen.
- Abre los canales `FF00`/`FF01` del receptor `0B05:1ACE` (nunca el teclado ni el ratón como tales), manda la consulta, lee con plazo de 0,8 s y descarta cualquier respuesta que no sea la esperada o cuyo valor no esté entre 1 y 100 %.
- Sin respuesta → el dispositivo no se muestra (está apagado o dormido). Nunca se inventa un porcentaje.

## 4. Límites
- **Solo estos dos modelos tienen datos medidos.** Otros equipos ROG con el mismo receptor pueden usar otros números de consulta o otras posiciones del byte.
- **Cargando / no cargando:** no se ha identificado el indicador; se informa «desconocido».
- **Nombre del modelo:** el receptor no lo dice; se muestra «Teclado ROG» / «Ratón ROG».
- **Auriculares ROG DELTA II (2,4 GHz):** no se ha medido su batería (estaban apagados durante las capturas). Su canal propio solo mostró un mensaje constante (`CC 51 30 00 00 FF AA`).
- **Otras marcas:** ver la lista de compatibilidad prevista en `docs/44`; ninguna se ha medido.

## 5. Nombres y receptores no compatibles
- **Nombres:** el receptor no dice el modelo. Ajustes › Barra superior › Periféricos tiene dos campos, «Nombre del teclado ROG» y «Nombre del ratón ROG»: lo que escribas es el nombre que se muestra (vacío = «Teclado ROG (receptor 2,4 GHz)»).
- **Otros receptores ASUS** (por ejemplo, el de los auriculares ROG DELTA II): se detectan por el nombre que el propio receptor da por USB y se listan, sin leer nada de ellos, como «Receptor detectado, batería aún no compatible». La presencia del receptor **no** prueba que el dispositivo esté encendido.
- **Cómo crece:** cada modelo nuevo necesita su mensaje medido; ver la propuesta de lista de compatibilidad y de «reportar este dispositivo» en `docs/44`.


## Auriculares ROG Pelta y ROG Delta II (sin verificar)

El receptor del audífono (VID `0B05`, PID `1B84` Pelta / `1AFA` Delta II) es otro dispositivo, distinto del Omni. Su canal propio es el `FF00:0001`, informe `CC` de 64 bytes (en la captura del 8 de noviembre aparece con `PID=1AFA`, 64 bytes de entrada; el audífono estaba apagado y el receptor solo repetía `CC 51 30 … FF AA`).

La lectura es una consulta de solo lectura, igual que con el teclado y el ratón:
- `CC 12 07` → la respuesta trae la batería en el byte 6 (`FE` = todavía no lo sabe).
- `CC 12 08` → el byte 5 vale 1 si está cargando.
- `FF AA` en los bytes 1–2 o 5–6 = apagado o dormido.

**Origen y licencia.** Estos datos de protocolo vienen de lo que describe el proyecto [Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor) (GPL-3.0) y de los proyectos en que se basa. En Hatsu no se copia su código: el lector está escrito aparte (`AsusRogProtocol`, `AsusRogProvider`), solo con estos datos. **Pendiente:** probarlo con el audífono encendido; hasta entonces el dato se marca como lectura sin verificar y, sin respuesta, el dispositivo queda «no disponible».

## Modelos ROG (tabla completa, sin verificar salvo los dos medidos)

Hatsu ya no se limita a «un teclado» y «un ratón» por el receptor Omni. Ahora (`AsusRogModels`):
1. Le pregunta al receptor Omni qué aparatos tiene emparejados (`01 A0 00` por la colección col01 de la interfaz 2: ranuras de 4 bytes con el id de producto desde el byte 5) y lee cada uno por su canal (teclados: col02, informe 2; ratones: col03, informe 3). Sale con el **nombre del modelo**. Si el Omni no da la lista, queda la consulta genérica de antes.
2. Lee los teclados y ratones con su **propio receptor o por cable** (informe sin número, interfaz 1 para teclados y 0 para ratones).
3. Cada modelo tiene su posición de la batería (teclados: byte 6, o el 11 en el Falchion; ratones: byte 5, o el 7 en el Strix Carry) y su escala (×25 en los medidores antiguos de 0 a 4). La carga sale del byte 9 (teclado) y del 10 (ratón).

Modelos en la tabla: Azoth, Azoth Extreme y Extreme SE, Azoth X, Falchion, Falchion RX y RX Low Profile, Strix Scope II 96 y 96 RX, Strix Scope RX TKL; Chakram y Chakram X, Gladius II y III (Aimpoint y EVA-02), Harpe Ace (Aim Lab, Extreme, Mini) y Harpe II Ace, Keris Wireless (EVA, Aimpoint), Keris II Ace, Origin y Origin KJP, Pugio II, Spatha X, Strix Carry y Strix Impact II y III.

Fuente: el proyecto Wireless-Device-Battery-Monitor (GPL-3.0) y los que cita (peripheral-battery-tray, G-Helper, BatteryTool). Solo se usaron los datos de protocolo; el código es propio.
