# 25 · WinMac 2.0 — Dynamic Island y Centro de actividades (fase 1)

Referencia visual: mockup aprobado por el usuario (Isla compacta adaptable, Isla expandida con encabezado multimedia, pestañas
«En curso · Capturas · Portapapeles · Descargas · Historial», lista de actividades con progreso y acciones, galería de capturas).

## Etapa A — Decisiones (tras revisar el repositorio)

**Qué existe y se reutiliza (NotchWin, módulo del host):**
- Música real por GSMTC (`GsmtcMediaProvider`, `IMediaControls`: reproducir/pausar, siguiente, anterior) y su pestaña.
- Descargas reales por carpeta (`DownloadTracker`: archivos `.crdownload/.part` en Descargas; sin tamaño total → progreso indeterminado).
- Portapapeles (`ClipboardHistory`, solo en memoria y solo con su pestaña activada).
- Pestañas configurables (`NotchSections`, también en los ajustes del host `NotchSettings.SectionIds`).
- Árbitro de la píldora (`PillArbiter`) para lo temporal (volumen, brillo, notificaciones, eventos propios).
- Canal IPC del host (tubería con token; eventos entre módulos) — ya lo usa `notch.feed`.

**Qué se añade (sin procesos nuevos, sin frameworks nuevos):**
- `NotchWin.Core/Activities`: modelo común `Activity` (id, tipo, origen, título, descripción, miniatura, estado, progreso, acciones, prioridad,
  creación/actualización, qué hacer al terminar, marca de **datos de prueba**) y `ActivityStore` (registrar, actualizar, terminar, historial,
  límites, caducidad de actividades sin noticias para que nada quede «en curso» para siempre, aislamiento de errores). Puro y probado en Linux.
- Fuentes reales: música (GSMTC), descargas (carpeta), capturas (carpeta de capturas de Windows `Imágenes\Capturas de pantalla`,
  solo metadatos; miniaturas reducidas al dibujar), portapapeles (el existente).
- API **interna** `notch.activity` por el IPC del host: solo los módulos de WinMac (tubería con token de sesión, `CurrentUserOnly`) pueden
  registrar/actualizar/terminar actividades. Nada público. Preparada para extensiones futuras (tipo `extension`), sin sistema de plugins.
- Pestañas nuevas `activity` («En curso»), `captures` («Capturas»), `history` («Historial»); se reutilizan `clipboard` y `downloads`.
  Orden por defecto como el mockup; a quien ya tenía su orden se le añaden al final (sin perder nada).
- Encabezado multimedia sobre las pestañas en la Isla expandida (portada, título, artista, progreso si GSMTC lo da, controles SOLO si son reales).
- Isla compacta: además de la pista y el temporizador, una descarga en curso (nombre + progreso) y la confirmación de captura (miniatura)
  mediante el árbitro (tipo nuevo `Capture`), sin romper volumen/brillo/notificaciones.

**Acciones:** cada acción solo se muestra habilitada si la Isla tiene una implementación real para ese tipo:
abrir archivo / mostrar ubicación / copiar imagen / eliminar (a la Papelera) para capturas; abrir / mostrar ubicación para descargas
terminadas; reproducir-pausar / siguiente / anterior para música real; eliminar del historial. Pausar/reanudar/cancelar descargas de un
navegador NO tienen API universal en Windows: no se muestran.

**No en esta fase:** motor de capturas, editor, grabación de pantalla (el tipo existe en el modelo, sin fuente), gestor avanzado de portapapeles,
extensiones, Spotlight Pro.

**Datos de prueba en el CI:** se marcan como tales en la interfaz («Datos de prueba»). Siempre que es posible se usan caminos reales:
archivos PNG reales en la carpeta de capturas, un archivo `.crdownload` real en Descargas, texto real en el portapapeles; solo la música
(sin reproductor en el runner) llega como actividad de prueba por la API interna.


## Resultado de la fase 1 (run 41480265034, commit 7d14f5b · NotchWin 02c2613)

| Elemento | Estado | Evidencia |
|---|---|---|
| Modelo de actividades, almacén, API interna `notch.activity` | **Implementado y probado** (NotchWin.Core: 124 pruebas, 15 nuevas de actividades/IPC/letras) | CI Linux y Windows |
| Pestañas En curso · Capturas · Portapapeles · Descargas · Historial | **Implementado**, navegación real con el ratón en el CI | capturas 21, 22, 25–29 |
| Encabezado multimedia (portada, título, artista, progreso, línea de la letra, controles solo reales) | **Implementado**; en el CI solo con música **simulada** (sin reproductor: «Datos de prueba», sin controles) | 21, 25 |
| Galería de capturas (carpeta de capturas de Windows, miniaturas pequeñas, abrir/copiar/mostrar/eliminar a la Papelera) | **Implementado y probado con archivos reales** | 26 |
| Descargas como actividades (archivo .crdownload real) y en el historial al terminar | **Implementado y probado** | 23, 25, 28, 29 |
| Captura guardada en la Isla compacta (miniatura) | **Implementado y probado** (archivo real nuevo en la carpeta) | 24 |
| Portapapeles | Existente, reutilizado (texto real) | 27 |
| Letras sincronizadas | **Sin cambios**; regla de la Isla compacta probada (pausa, reanudación, cambio de canción, interrupciones); línea actual en el encabezado (solo lectura) | pruebas `LyricsRegressionTests`; no verificable en el CI sin reproductor |
| Grabación, motor de capturas, pausar/cancelar descargas | **Pendiente** (sin fuente o sin API universal) | — |

Comparación con el mockup: misma organización (encabezado multimedia, barra de pestañas con la seleccionada como píldora con nombre, lista de
actividades con progreso y acciones, galería 3×2 con «Hoy/Ayer hh:mm», historial). Diferencias:
- la píldora de la pestaña seleccionada sale gris y no azul (el estilo del botón de WinUI se impone; pendiente);
- con 12 pestañas activadas, las no seleccionadas son solo iconos (el mockup tiene 5 con nombre);
- el encabezado no tiene botones anterior/pausa/siguiente con datos de prueba (a propósito);
- la Isla compacta con música real muestra la pista o la letra como siempre (no se cambió para no tocar las letras).

Problemas corregidos durante la auditoría: conflicto de nombres con System.Diagnostics (compilación), pestañas sin nombre, icono repetido,
«…» en la descarga compacta, auditoría que capturaba la pestaña equivocada.


## Teclado (fase de estabilización)
La Isla nunca toma el teclado por sí sola (al pasar el ratón, con notificaciones o con la píldora). Solo cuando el usuario la abre **con un clic** quita
`WS_EX_NOACTIVATE` y se pone en primer plano; al cerrarse lo restaura. No intercepta teclas globales.
Esc cierra · ←/→ (desde las pestañas) o Ctrl+←/→ cambian de sección · Ctrl+1…9 sección N · Tab/Mayús+Tab recorren pestañas, tarjetas y acciones (sin salir de la
Isla) · ↓/↑ siguiente/anterior elemento · Intro/Espacio activan · foco con el rectángulo del sistema. Validado en el CI con pulsaciones reales (captura 48).

WinMac cierra la Isla abierta antes de capturar, grabar o editar (orden `collapse`), porque está siempre encima y se llevaría los clics.
