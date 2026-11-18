# Periféricos de más marcas (lote 1)

Hatsu lee la batería de aparatos de más marcas por el protocolo propio de cada una. **Todo este lote está SIN VERIFICAR con aparatos reales**: se escribió con la documentación pública y sin tener los aparatos. Se activa en Ajustes › Barra superior › Periféricos › «Leer la batería de más marcas» (apagado por defecto).

## Qué marcas cubre este lote
| Marca | Aparatos | Protocolo |
|---|---|---|
| Razer | teclados y ratones inalámbricos (tablas de modelos de OpenRazer) | informe de característica de 90 bytes, clase 07 (batería 80, carga 84) |
| SteelSeries | Arctis 1, 7, 7+, 9, Pro, Nova 3P/5/7/Pro, GameBuds; Aerox 3/5/9, Prime, Rival 3, 650, Pro | informes de salida/entrada propios de cada familia |
| Corsair | Void/HS (C9 64), Bragi (Virtuoso, HS80 MAX, Slipstream), Compx (Sabre V2 Pro) | tres protocolos |
| HyperX | Cloud II, II Core, III, III S, Alpha, Alpha 2, Flight, Stinger Core; Pulsefire Haste 2 | un protocolo por familia |
| Glorious y afines | Model O/D (2), Hitscan, G-Wolves, WLMouse YING | informe de característica de 65 bytes |
| ASUS ROG | teclado, ratón (Omni) y auriculares Pelta/Delta II | ver `docs/64` |
| Logitech | receptores Unifying, Bolt, Lightspeed (HID++) | ver `docs/65` |

## Cómo se hizo
- Los datos de protocolo (qué bytes se mandan y dónde viene la batería) salen de lo que describen el proyecto [Wireless-Device-Battery-Monitor](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor) (GPL-3.0) y los proyectos abiertos en que se basa: OpenRazer, rivalcfg, HeadsetControl, HyperHeadset, HyperXWM, HyperBatteryMonitor, ngenuity-linux-cli, OpenRGB, HaloBattery, glorious-indicator y los configuradores web de las marcas.
- **No se copia su código.** Cada marca tiene una parte pura (`Peripherals/*Protocol.cs`: armar la consulta y leer la respuesta, con pruebas) y un lector de Windows (`Shell/Peripherals/*Provider.cs`) escritos de nuevo. Hatsu sigue siendo Apache-2.0.
- Solo se mandan consultas de lectura. Si el aparato no responde en el plazo, no se muestra; nunca se inventa un porcentaje.

## Límites
- Sin verificar: puede fallar un modelo concreto, o dar un valor corrido. Si te pasa, el diagnóstico de `--dump` de Hatsu (no incluido) o una captura USB sirven para corregirlo.
- Los aparatos que Synapse, iCUE, G HUB o NGENUITY tengan abiertos pueden contestar tarde o dejar la respuesta a medias; el lector reintenta.
- Pendiente (lotes 2 y 3): Cooler Master, Keychron, Pulsar, Kysona, ATK, Lamzu, Endgame Gear, Arbiter, Cherry, NZXT, Poly, Audeze, Jabra, JBL, Sony INZONE, Samsung Galaxy Buds, AirPods, mandos (PlayStation, Xbox, Switch, 8BitDo…) y los genéricos de HID y Bluetooth.
