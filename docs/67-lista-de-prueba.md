# Qué probar en un Windows real (2.0.13)

Todo esto pasa las pruebas automáticas, pero **nadie lo ha visto funcionar en un Windows real**. Marca lo que funcione y anota lo que no (con la hora, para buscarlo en el registro).

## Ajustes y barra
- [ ] Ajustes: buscador, 12 secciones en 5 grupos, ventana de 700 de alto.
- [ ] Barra superior › iconos: arrastrar el asa ⋮⋮ cambia el orden; «Agregar» y «Personalizar iconos…» funcionan.
- [ ] Rendimiento, Red y Discos son iconos sueltos, cada uno abre su panel.
- [ ] Gestos: «Aplicación…» (lista con «Instalada») y «Página web…» (Cancelar / Guardar).
- [ ] Menú de Hatsu › «Salir de Hatsu Desktop».
- [ ] Abrir Hatsu otra vez desde el menú Inicio muestra Ajustes.

## Pantalla completa y Dock
- [ ] Un juego a pantalla completa en el monitor 1 **no** esconde el Dock ni la Barra del monitor 2.
- [ ] Minimizar Steam **no** esconde la Barra ni el Dock.
- [ ] Modo juego (Ajustes › Dock): con un juego en ventana el Dock se oculta solo y libera el espacio; al salir vuelve.

## Mezclador y notch
- [ ] Sonido: salen las apps de **todas** las salidas y dice en cuál suena cada una. Cambiar el volumen de una app que suena en otra salida funciona. Pulsar la salida abre el menú para moverla.
- [ ] Notch: click en la barra de reproducción salta a ese punto (pestaña de música y encabezado).
- [ ] Notch: clic en una notificación de WhatsApp abre la app (registro en `%LOCALAPPDATA%\NotchWin\notificaciones.log`).

## Asistente
- [ ] Instalación nueva y «Abrir el asistente» desde Ajustes: pasos «Funciones extra» (todas apagadas) e «Iconos de la barra» (13 interruptores).
- [ ] «Omitir» deja apagadas Spotlight Actions y Shelf+.

## Periféricos (Ajustes › Barra superior › Periféricos)
- [ ] ASUS ROG: con el Omni, ¿sale el **nombre del modelo** sin escribirlo? Strix Scope II 96 (60 %) y Keris II Origin (97 %) coinciden con Armoury Crate.
- [ ] Auriculares ROG Delta II **encendidos**: ¿sale el porcentaje? ¿cuadra con Armoury Crate?
- [ ] «Leer la batería de más marcas»: anota qué aparatos salen y si el número es correcto. Los que no salen, también.
- [ ] Mandos: DualSense/DualShock por USB y Bluetooth, Xbox, Switch Pro.

## Si algo no sale
- Un aparato que no aparece: anota marca, modelo, cómo se conecta (receptor, Bluetooth, cable) y si estaba encendido.
- Un número equivocado: anota el que da el programa del fabricante y el que da Hatsu.
