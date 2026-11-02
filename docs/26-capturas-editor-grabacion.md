# 26 · WinMac Screenshot: capturas, galería, editor y grabación

Todo forma parte de la Barra (`WinMac.Bar.App`) y de la Isla (NotchWin); no hay aplicaciones aparte.

## Capturas
- Modos: **región** (arrastrar, con su tamaño en píxeles), **ventana** (la de debajo del cursor, resaltada con su título), **pantalla** (el monitor del cursor) y **todas las pantallas**.
- La pantalla se **congela** al abrir (copia GDI documentada, píxeles físicos; la Barra es Per-Monitor V2), se atenúa y lo elegido queda a brillo completo. Esc o clic derecho cancela; teclas 1–4 cambian de modo.
- Multimonitor con coordenadas negativas y DPI mixtos: geometría pura probada con distribuciones **simuladas** (`CaptureTests`); en el CI solo hay un monitor real.
- Atajos (Ajustes › Barra › Capturas): Ctrl+Mayús+3 pantalla, +4 región, +5 barra de captura, +6 grabar/detener. Si otro programa ya los registró, Windows los rechaza y queda en `bar.log`.
- Se guardan como «Captura AAAA-MM-DD a las HH.MM.SS.png» (o JPG) en la carpeta elegida (por defecto `Imágenes\Capturas de pantalla`); opcionalmente también al portapapeles.
- Lo que una app protege (`SetWindowDisplayAffinity`) sale negro, como quiere Windows.

## Galería
Pestaña «Capturas» de la Isla (6 recientes, acciones reales) y botón **Ver todas** ⇒ ventana con búsqueda (sin acentos), orden (recientes, antiguas, nombre, tamaño),
multiselección, vista ampliada, abrir, **editar**, copiar imagen, copiar ruta, mostrar en la carpeta, renombrar (validado, nunca sobrescribe) y eliminar **a la Papelera**
(pregunta antes). Solo metadatos en memoria; miniaturas decodificadas al tamaño dibujado (lectura por flujo con reintento para archivos recién creados).
La carpeta es la de Ajustes (la Isla la recibe de la Barra). Lee el disco cada vez: sobrevive a los reinicios.

## Editor
Recortar, lápiz, flecha, rectángulo, texto, resaltar; colores; deshacer/rehacer; copiar; **Guardar** crea «… (editada).png» junto al original (nunca lo sobrescribe);
**Guardar como…** pregunta dónde. Direct2D a resolución real (lo que se ve es lo que se guarda). Cerrar con cambios sin guardar avisa en la propia ventana.
Abrir: «Editar» en la Isla o en la galería, o `WinMac.Bar.App --edit <ruta>`.

## Grabación
- Solo por acción explícita (atajo, Spotlight «Grabar la pantalla», `--record region|window|screen|stop`); se elige el área en la misma superposición.
- **Solo vídeo**: nunca audio ni micrófono. MP4 (H.264) con los códecs de Windows (MediaStreamSource + MediaTranscoder), 15 fps. Destino: `Vídeos\Screen Recordings`.
- **Indicador siempre visible** («● Grabando mm:ss · Detener», clic = detener), excluido del propio vídeo con `WDA_EXCLUDEFROMCAPTURE`; por eso tampoco sale en las capturas del CI
  (se verifica que el clic real sobre él detiene la grabación). Además, actividad «Grabando la pantalla» con el tiempo en la Isla, también en la Isla compacta.
- Al terminar: entrada en el historial con un fotograma de muestra (Windows.Media.Editing) y acciones abrir / mostrar.
