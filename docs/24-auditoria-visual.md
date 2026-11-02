# 24 · Auditoría visual (revisión a mano de las capturas reales)

Capturas: run 41457877425 (commit 2abe1b3), job «integración con NotchWin», paso «Auditoría visual». Artefacto `auditoria-visual-<sha>`
(29 PNG, `mosaico.png`, `informe-automatico.md` y este informe). Son capturas de la **pantalla real** del runner (Windows Server 2025, 1024×768, 100 %)
con WinMac completo (host + Barra + Dock + Isla de `dist/WinMac`). Revisadas una a una. **No se ha corregido nada todavía.**

## Estado por funcionalidad

| # | Estado pedido | Captura | Revisado a ojo | Resultado |
|---|---|---|---|---|
| 1 | Isla cerrada | 01, 01b | sí | Correcto: píldora negra 180×28, centrada, a ras del borde |
| 1 | Volumen 65 % | 02 | sí | Correcto: altavoz, barra blanca ~65 %, «65 %»; 280×28 |
| 1 | Silencio | 03 | sí | Correcto: icono tachado y barra en gris, «Silencio» |
| 1 | Brillo 80 % | 04 | sí | Correcto: sol, barra ~80 %, «80 %» |
| 1 | Notificación | 05, 05b | sí | Correcto: icono 28 px, título en negrita, texto en gris; 380×52 |
| 1 | Regreso | 06 | sí | Correcto: vuelve a 180×28 tras volumen → notificación reanudada |
| 1 | Expandida | 07 | sí | Se ve bien (pestañas, «Nada en reproducción», «Abrir Spotify»); ver P2 |
| 2 | Launchpad principal | 11, 11b | sí | Fondo desenfocado y cuadrícula 6×5 correctos, nombres legibles con «…»; ver P3 |
| 2 | Búsqueda vacía / «notepad» | 11c, 12 | sí | Campo correcto; resultado único resaltado; ver P3 (icono) |
| 3 | Spotlight inicial | 13, 13b | sí | Correcto: centrado, a ~22 % de alto, vidrio oscuro legible |
| 3 | App «notepad» | 14, 14b | sí | Correcto: icono real, fila azul, sección «Aplicaciones» |
| 3 | «12*12» | 15, 15b | sí | Correcto: «144» / «12*12 =», sección «Calculadora» |
| 3 | Varios resultados («con») | 16, 16b | sí | Bien distribuido (7 filas, secciones); ver P5 |
| 4 | Bloc de notas activo + menús | 08, 08b | sí | Correcto: «Notepad File Edit Format View Help» (Win32, reales) |
| 4 | Menú desplegado / submenú / deshabilitados | 09-* | sí | **No se abre**: ver P1. Sin captura válida de desplegable, submenú ni deshabilitados |

## Problemas detectados (por gravedad)

- **P0 · Isla: la píldora puede quedarse atascada en una notificación (funcional, intermitente).** En el run 41458868288 (commit 840535c) la notificación reanudada tras la
  interrupción del volumen nunca terminó: no llegó «pill none» y falló la comprobación «al terminar vuelve al contenido habitual» (en los runs anteriores pasó).
  Causa probable (leyendo el código): el temporizador de la Isla (`DispatcherQueueTimer`) puede dispararse unos milisegundos antes de la hora; `PillArbiter.Tick()` ve que aún no
  ha vencido y no hace nada, y nadie vuelve a programar el temporizador. Arreglo propuesto (una línea, sin cambiar el comportamiento): en el `Tick` del temporizador de
  `NotchWindow.Pill.cs`, si después de `_pill.Tick()` sigue habiendo un elemento, volver a llamar a `ScheduleTick()`. **No aplicado** (auditoría sin correcciones).

- **P1 · Global Menu: el desplegable no se abre (funcional, grave).** Con el Bloc de notas activo, la barra muestra sus 5 menús Win32 y la etiqueta pulsada queda resaltada,
  pero no aparece ningún desplegable (ni el de WinMac ni el nativo) y no hay error en `bar.log`. Se repite en los 5 menús, también con la Isla ya cerrada (09-menu-5-Help).
  La prueba anterior del CI solo comprobaba que se descubrían los menús, no que se abrieran. Hipótesis a investigar: el panel se abre y se cierra en el acto
  (pérdida de foco: `WM_ACTIVATE` lo oculta) o `ShowAppMenu` sale antes de tiempo. El clic de la flecha de la bandeja (mismo tipo de panel, mismo clic inyectado) sí abre su desplegable.
- **P2 · Isla expandida: no se cerró al mover el ratón fuera (posible).** Tras abrirla con un clic, se movió el puntero con `SetCursorPos` y siguió abierta, tapando parte de la barra
  (los menús View/Help y el centro). Puede ser un efecto de mover el ratón por programa (WinUI no recibe la salida del puntero); hay que comprobarlo en el PC con el ratón real.
  Se cerró al pulsarla otra vez.
- **P3 · Launchpad: la mayoría de iconos tardan o no llegan.** A los 1,5 s de abrir, solo 9 de 30 casillas tenían su icono real; el resto mostraba el mosaico con la inicial
  (Calculator, Command Prompt, Control Panel, Notepad…). En «notepad» el icono seguía sin cargar, mientras que Spotlight, después, sí mostró el icono real del Bloc de notas
  (pide 64 px; Launchpad pide 128). Hipótesis: cola de iconos lenta (2 hilos) o fallo a 128 px en apps empaquetadas y accesos del menú Inicio. Algunos iconos Win32
  (7-Zip, Application Stack Builder) salen pequeños dentro de un cuadro gris claro: es el icono que entrega Windows a ese tamaño, sin escalar.
- **P4 · Global Menu: «System» como menú de Windows Terminal.** UI Automation expone el menú de sistema de la ventana (barra de título) como `MenuBar`; la barra lo muestra como
  si fuera un menú de la app («Terminal  System»). Habría que descartar la barra de menú de sistema.
- **P5 · Spotlight: coincidencias flojas.** Con «con» aparecen «Notificaciones» (por letras sueltas en orden) y «Accesibilidad» (por la palabra clave «contraste»). No es grave,
  pero son resultados poco esperables; la búsqueda por letras en orden debería pedir más o no aplicarse a Configuración.
- **P6 · Contraste Isla / barra clara.** Con el tema claro, la barra es blanca translúcida y la Isla es una píldora negra. Es coherente con el notch de macOS (Golden Gate), pero
  la Isla se ve como un bloque aislado; en tema oscuro adopta el vidrio de la barra. Decisión de diseño, no defecto.
- **P7 · Launchpad tapa la barra superior.** Cubre todo el monitor, también la barra (como en macOS). No hay captura «Launchpad con la barra visible» porque por diseño no lo está.
- **P8 · Dock y barra de tareas de Windows a la vez.** El Dock flota justo encima de la barra de tareas de Windows (WinMac no la oculta). Coherente con la decisión
  de no tocar la barra de tareas, pero visualmente se apilan dos barras.

## Integración visual (convivencia)

- Barra + Isla (01b, 05b): sin solapes con la Isla cerrada; la notificación crece hacia abajo sin tapar iconos de la barra.
- Barra + Global Menu (08): las etiquetas quedan a la izquierda y no llegan a la Isla cerrada (x 132–330 frente a 422–602). Con la Isla **expandida** (520 px) sí quedan tapadas (P2).
- Spotlight con la barra visible (13–16): no tapa la barra ni la Isla; el Dock queda debajo.
- Escalado: solo 100 % (el runner no permite otro). Recortes: ninguno observado en la Isla, Spotlight ni la cuadrícula; los nombres largos se recortan con «…» a propósito.

## Limitaciones del runner (lo que queda para el PC)

- Un monitor de 1024×768 al 100 %: sin varias pantallas, DPI alto, HDR ni fondo del usuario.
- Sin audio, panel interno ni permiso de notificaciones: los estados de la Isla son la secuencia de muestra por el canal real (Barra → host → Isla), no la detección real.
- Sin GPU real: las animaciones se capturan en un instante; no se mide su fluidez.
- Windows Search no responde en el runner (0x80041820).
- La tecla Windows y Alt+Espacio físicos no se pueden pulsar (teclas inyectadas ignoradas a propósito).
- Ratón por programa (`SetCursorPos`/`mouse_event`): puede no reproducir la entrada y salida del puntero de WinUI (P2).
