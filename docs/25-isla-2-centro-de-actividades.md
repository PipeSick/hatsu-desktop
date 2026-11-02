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
