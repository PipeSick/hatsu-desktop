# 65 · Periféricos Logitech con receptor (HID++) — PROBABLE, sin verificar

**Estado: escrito sin ningún dispositivo Logitech para probarlo.** Sale de la documentación pública del protocolo (Solaar, el controlador `hid-logitech-hidpp` de Linux), no de una medición. Las pruebas automáticas usan tramas armadas desde esa especificación, no de un dispositivo real. Nadie lo ha visto funcionar.

## Qué hace
- Ajustes › Barra superior › Periféricos › «Leer la batería de periféricos Logitech (receptor)»: **apagado por defecto**. Hace solo consultas de **lectura** al receptor.
- Busca la interfaz HID++ del receptor (VID `046D`, página `FF00`, uso `0002`, informes de 20 bytes) y recorre sus 6 ranuras.
- Por ranura: pregunta por la función de nombre (sin respuesta en 0,3 s = ranura vacía o dispositivo dormido), luego por la batería (`0x1004` si la tiene; si no, `0x1000`) y el nombre y tipo del propio dispositivo.
- El **nombre sale del dispositivo**, no de una tabla. Si no responde el nombre: «Dispositivo Logitech (ranura N)».
- Porcentajes fuera de 1–100 se descartan; nunca se inventa un valor.

## Qué no se sabe
- Que funcione con ningún modelo real. Pendiente: probar con un dispositivo (una prueba corta con la opción activada).
- Dispositivos Logitech por Bluetooth directo (sin receptor) ya los cubre el lector Bluetooth estándar.
- Dispositivos con cable, y los que usan HID++ 1.0 antiguos (Unifying viejos), pueden no responder.
- Si G HUB u Options+ están abiertos, comparten el canal; Hatsu filtra las respuestas por su identificador de software, pero puede haber interferencias.
