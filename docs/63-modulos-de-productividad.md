# 63 · Módulos de productividad: Mission Control, Snap, Live Text, Spotlight Actions, Gestures y Shelf+

Estado: **implementado y probado automáticamente (lógica pura y ajustes); PENDIENTE DE VALIDACIÓN EN WINDOWS REAL para todo lo gráfico, de entrada y de hardware.** Nada de esto se ha ejecutado en un Windows con pantalla: aquí solo hay compilación (Linux con `EnableWindowsTargeting`) y pruebas de lógica. La interfaz WinUI 3 de la estantería (NotchWin) **solo compila en el CI de Windows**.

Etiquetas: **IMPLEMENTADO** (código) · **COMPILA** · **PRUEBAS** (automáticas, de lógica) · **PENDIENTE EN WINDOWS** (nadie lo ha visto funcionar).

## 1. Arquitectura común
| Pieza | Dónde | Qué hace |
|---|---|---|
| `FeatureManager`, `IHatsuFeature`, `FeatureScope` | `Bar.Core/Features/FeatureManager.cs` | Enciende y apaga módulos en caliente. Todo lo que un módulo abre (atajos, ganchos, ventanas, hilos, suscripciones) se apunta en su `FeatureScope` y se libera en orden inverso al apagarlo, o si el arranque falla a medias. Un módulo defectuoso queda en *Failed* con su causa y no afecta a los demás; un fallo en marcha reinicia hasta 3 veces y luego se detiene; «Reintentar» desde Ajustes. |
| `HotkeySpec`, `HotkeyRegistry` | `Bar.Core/Features/` | Registro central de atajos con dueño: conflictos entre módulos, combinaciones reservadas de Windows (Alt+Tab, Alt+Espacio, Ctrl+Mayús+Esc…), reasignar, liberar. Exige Ctrl o Alt (nunca una tecla suelta); nunca usa Win. |
| `HotkeyBinder` | `Bar.Win/Features/` | Consulta el registro y registra con `RegisterHotKey` (sin ganchos). Si Windows lo rechaza, queda «no aceptado» y Ajustes lo muestra. Los atajos que ya tenía la Barra (Spotlight, capturas) se anotan en el registro para que los módulos nuevos vean el conflicto. |
| `WindowCatalog` | `Bar.Win/Features/WindowCatalog.cs` | **Reutiliza** `WindowTracker`, `WindowThumbnails` y `WindowActions` del Dock (no hay otra enumeración de ventanas). Se crea cuando el primer módulo lo adquiere y se libera con el último. Solo ve el escritorio virtual actual. |
| Configuración | `BarConfig` (campos `mc*`, `snap*`, `lt*`, `spotActionsEnabled`, `gest*`) + `NotchSettings.ShelfMaxItems` | Persiste en `winmac.json` por el mismo camino que el resto (parche, saneado, publicación `config.bar` / `config.notch`). Valores por defecto: **apagados** salvo Spotlight Actions (que no instala nada). |
| Estado de salud | tópico `bar.features` → `FeatureStatusStore` | La Barra informa del estado real de cada módulo; Ajustes lo muestra con la causa del fallo. |
| Ajustes › Módulos | `SettingsModules.cs` | Una tarjeta por módulo: interruptor, descripción, configuración, estado, «Reintentar». Desactivar un atajo (vaciarlo) **no** desactiva el módulo. |

Contrato futuro para escritorios virtuales y para compartir ventanas por IPC: el catálogo expone `Snapshot()` (lista de `McWindow`) y `Activate/Close`; si el Dock debe compartir su lista por IPC más adelante, bastará publicar esa misma lista en un tópico (no se implementó porque no hace falta todavía).

## 2. Mission Control
- **Qué hace:** vista general de las ventanas del escritorio virtual actual. Una superposición por monitor (fondo desenfocado, búsqueda, tarjeta por ventana con miniatura, aplicación y título). Clic o Intro activan (y restauran si estaba minimizada), Supr o la «×» cierran la ventana (cierre cooperativo `WM_CLOSE`), Esc borra la búsqueda y luego sale, flechas/Tab navegan, escribir filtra. Atajo por defecto **Ctrl+Alt+↑** (configurable/vaciable).
- **Reutiliza:** `WindowTracker.Snapshot`, `WindowThumbnails` (PrintWindow), `WindowActions` (activar/cerrar), `FullscreenWatcher` (no se abre sobre un juego o presentación), el patrón de ventana de Launchpad.
- **Rendimiento:** con la vista cerrada no hay captura, temporizador ni gancho (solo el atajo). Abierta: refresco a 10 Hz, miniaturas por la cola del Dock (hilo de baja prioridad, nunca bloquea la interfaz).
- **Limitaciones (reales):** (1) **solo ve el escritorio virtual actual**: Windows marca como «cloaked» las ventanas de otros escritorios y no se usan APIs privadas; **no es el Mission Control completo de macOS**. (2) Las miniaturas son instantáneas (`PrintWindow`), no vídeo; las ventanas protegidas o aceleradas por hardware pueden salir negras (se muestra la inicial de la app); las minimizadas usan la última captura o la inicial. (3) Las ventanas de apps elevadas pueden no capturarse. (4) La asignación a monitores se hace por el centro de la ventana. (5) Cambiar de escritorio desde aquí no existe: lo hace Gestures con Win+Ctrl+←/→.
- **Estado:** IMPLEMENTADO · COMPILA · PRUEBAS (`MissionControlTests`, 11: cuadrícula sin solapes ni salidas de pantalla, búsqueda con acentos, teclado, clic, multimonitor, cerrar) · **PENDIENTE EN WINDOWS** (dibujo, DPI mixto, 2+ monitores, UWP/Electron/protegidas).

## 3. Snap
- **Qué hace:** manteniendo **Mayús** (configurable: Ctrl o Alt) mientras arrastras una ventana aparecen las zonas del monitor bajo el puntero; al soltar dentro de una, la ventana se coloca en ella. Sin la tecla no se hace nada (el Snap de Windows no se toca). Distribuciones: Mitades, Tercios, Cuadrícula, Principal y lateral + propias (editor de zonas: arrastrar crea, clic derecho borra, Retroceso quita la última, Intro guarda; `snap-layouts.json`). Separación entre ventanas configurable. Atajo opcional «ventana activa a la zona siguiente».
- **Reutiliza:** `WindowArranger.Apply` (colocación con `SetWindowPlacement`, conversión de área de trabajo, mensajes de error para ventanas elevadas) y los monitores del capturador (`ScreenGrabber.Monitors`: DPI por monitor). Detecta el arrastre con el evento de sistema `EVENT_SYSTEM_MOVESIZESTART/END` (`SetWinEventHook`, sin inyección). Compensa el borde invisible de redimensión de Windows para que el borde visible llene la zona.
- **Limitaciones:** no mueve ventanas sin marco redimensionable, a pantalla completa, ni de programas con más permisos que Hatsu (UIPI; se registra el motivo). Windows aplica los tamaños mínimos de cada app. Si el usuario suelta en un borde con la tecla pulsada, el Snap nativo y el de Hatsu podrían competir: Hatsu coloca la ventana ~90 ms después de que Windows termine (**comportamiento a validar**). Juegos y pantalla completa: se ignoran.
- **Estado:** IMPLEMENTADO · COMPILA · PRUEBAS (`SnapTests`, 8: zonas, separación, monitores con coordenadas negativas, solapes, bordes invisibles, archivo de distribuciones, límites, rejilla del editor) · **PENDIENTE EN WINDOWS** (arrastre real, DPI mixto, Electron/WinUI, editor).

## 4. Live Text
- **Qué hace:** atajo (**Ctrl+Alt+Espacio**, configurable) → el capturador de regiones de siempre (pantalla congelada, varios monitores, Esc cancela) → **OCR local** de Windows (`Windows.Media.Ocr`) → panel con el texto, «Copiar» y «A la estantería». Opciones: copiar al reconocer, conservar saltos de línea, idioma. Nada sale del equipo; no necesita Internet.
- **Reutiliza:** `CaptureOverlay`/`ScreenGrabber`/`CaptureGeometry.Crop` (no hay otro capturador: `BeginRegionPick` entrega la región a Live Text en vez de guardarla). El texto copiado entra en el portapapeles normal, así que el historial de la Isla lo recoge: **no hay historial duplicado**. Disponible desde Spotlight («Live Text: reconocer texto de la pantalla») y Gestures.
- **Idiomas:** son los paquetes de reconocimiento que Windows tenga instalados (el estado en Ajustes los lista). Elección: idioma de Ajustes → mismo idioma base → perfil del usuario → primero disponible. Sin ninguno, el panel explica cómo añadirlo (Configuración › Hora e idioma › Idioma y región). **No verificado en este entorno** qué paquetes trae cada edición de Windows 11.
- **Limitaciones:** regiones mayores que `OcrEngine.MaxImageDimension` se rechazan con aviso; la calidad depende del idioma y del tamaño del texto; sin texto reconocido se dice.
- **Estado:** IMPLEMENTADO · COMPILA · PRUEBAS (`LiveTextTests`, 5: saltos de línea, palabras partidas, elección de idioma, vista previa) · **PENDIENTE EN WINDOWS** (el OCR real, paquetes de idioma, varios monitores).

## 5. Spotlight Actions
- **Qué hace:** Spotlight busca y ejecuta a través de un **registro central de acciones** (`ActionRegistry`): id, título, descripción, módulo propietario, riesgo, parámetros, disponibilidad y resultado/error. Contiene las 13 acciones de siempre (migradas sin cambiar su comportamiento: capturas, grabar/detener según corresponda, Launchpad, Ajustes, espacios de trabajo), acciones del sistema (bloquear; **suspender, cerrar sesión, reiniciar y apagar piden un segundo Intro** además de la confirmación de la Barra) y las de los módulos **activos** (Mission Control, Snap: siguiente zona/crear/usar distribución, Live Text, Shelf+: abrir). Las acciones de un módulo apagado desaparecen y no se ejecutan; con el módulo Spotlight Actions apagado, Spotlight vuelve a su lista clásica.
- **Seguridad:** un resultado de búsqueda nunca aporta parámetros ejecutables; los scripts de usuario y las herramientas siguen igual (doble Intro, sin elevar).
- **Estado:** IMPLEMENTADO · COMPILA · PRUEBAS (`ActionRegistryTests`, 7) · **PENDIENTE EN WINDOWS** (que cada acción real se dispare desde la ventana de Spotlight).

## 6. Gestures
- **Qué hace:** esquinas activas (el puntero se queda 450 ms —configurable—), botones laterales del ratón y dos atajos de teclado, cada uno con una acción: Mission Control, Spotlight, Launchpad, Live Text, siguiente zona de Snap, escritorio anterior/siguiente (envía Win+Ctrl+←/→), mostrar/ocultar el Dock automáticamente (el host cambia `dock.json`) y capturar región. Las acciones de módulos apagados no se ofrecen ni actúan.
- **Reutiliza:** el **único** gancho de bajo nivel de la Barra (`WinKeyHook`, que ahora comparten la tecla Windows y Gestures; solo existe mientras alguien lo usa). Las esquinas no consumen nada; los botones laterales solo se consumen si tienen una acción asignada y disponible (si no, siguen siendo «atrás/adelante»).
- **Reglas de seguridad:** las esquinas no se activan con un botón del ratón pulsado (arrastrar una ventana a una esquina no dispara nada), ni sobre juegos/pantalla completa/presentación, ni donde otro monitor continúa la esquina.
- **NO hace (y no lo promete):** gestos del **panel táctil** de 3 y 4 dedos: son del sistema y Windows no ofrece una vía compatible para interceptarlos; no se instalan controladores. Alternativas ofrecidas: esquinas, botones laterales y atajos.
- **Estado:** IMPLEMENTADO · COMPILA · PRUEBAS (`GesturesTests` 8, `GestureSettingsTests` 4) · **PENDIENTE EN WINDOWS** (gancho de ratón real, esquinas con 2 monitores, botones laterales, cambio de escritorio).

## 7. Shelf+ (NotchWin)
- **Auditoría previa:** la estantería de NotchWin ya existía (solo rutas de archivos/carpetas, en memoria, sin límite, sin texto ni imágenes, sin vaciar; un original movido cancelaba el arrastre pero la fila quedaba). **No se creó otra**: se amplió.
- **Qué hace ahora:** archivos y carpetas **por referencia** (nunca se copian, mueven ni borran), texto, imágenes (si no tienen archivo, se guarda una copia PNG propia en `%LOCALAPPDATA%\NotchWin\shelf`, **la única ruta que la estantería puede borrar**), arrastrar y soltar dentro y fuera, «Mostrar en el Explorador», «Copiar» (portapapeles normal, sin historial aparte), quitar uno, **Vaciar**, contador «n de máximo», límite configurable 1–100 (el más antiguo sale), originales ausentes marcados «(no encontrado)» y conservados hasta que el usuario los quita. Al reiniciar, se vacía (y se borran las copias propias que quedaron).
- **Integraciones:** Explorador (mostrar en carpeta), portapapeles (copiar), Live Text («A la estantería»), Spotlight («Estantería: abrir», solo con la pestaña activa). **Dock: no aplica** (no hay vía de integración razonable; no se simula).
- **Interruptor:** es la pestaña «Estantería» de la Isla (Ajustes › Módulos › Shelf+).
- **Estado:** IMPLEMENTADO · núcleo COMPILA y con PRUEBAS (`ShelfStoreTests`, 9, en NotchWin.Core.Tests) · **la interfaz WinUI NO se ha compilado localmente** (solo el CI de Windows la compila) · **PENDIENTE EN WINDOWS**.

## 8. Cómo se garantiza que apagar de verdad apaga
Mission Control: `Close()` destruye ventanas y detiene el bucle; el catálogo se libera con el último módulo. Snap: se retira el aviso de movimiento, el bucle y la superposición. Live Text: se descarta el panel y el trabajo en curso se ignora (token). Gestures: se quita el `MouseSink` y, si nadie más lo usa, se destruye el gancho. Spotlight Actions: se retiran las acciones y se vuelve a la lista clásica. Shelf+: es una pestaña de la Isla. Todo lo anterior está en `FeatureScope` y cubierto por `FeatureTests` (liberación en orden inverso, errores al liberar, arranque a medias, reinicios con límite). Lo que **no** está comprobado es que Windows realmente no deje procesos ni ganchos tras apagar (PENDIENTE EN WINDOWS).

## 9. Validaciones pendientes en Windows (agrupadas)
1. **Ventanas (10 min):** atajo de Mission Control con 7+ ventanas, UWP (Calculadora), Electron (VS Code/Chrome), una minimizada; con 2 monitores y escalados distintos; Esc/clic/Intro/Supr; apagarlo y comprobar que el atajo ya no responde.
2. **Snap (10 min):** arrastrar con y sin Mayús; zonas en cada monitor; soltar en el borde con Mayús (¿compite Windows?); editor de zonas; una app elevada.
3. **Live Text (5 min):** reconocer texto en español e inglés; sin paquete de idioma; región enorme; Esc.
4. **Gestures (5 min):** esquina con y sin botón pulsado; con otro monitor pegado; botón lateral con y sin acción; cambio de escritorio.
5. **Spotlight (3 min):** escribir «mission», «snap», «live text», «estantería», «bloquear»; la que pide segundo Intro.
6. **Shelf+ (5 min):** soltar archivo, carpeta, texto, imagen del navegador; arrastrarlos fuera; mover un original; Vaciar; límite; reiniciar.
7. **Ajustes:** cada interruptor enciende/apaga al instante; un atajo en conflicto (p. ej. repetir Ctrl+Alt+Espacio) muestra el aviso.

## 10. Evidencia de que no se duplicó nada
| Capacidad | Ya existía | Cómo se reutilizó |
|---|---|---|
| Enumerar ventanas y su identidad | `WindowTracker`, `WindowIdentityResolver` (Dock) | `WindowCatalog` los envuelve; no hay enumeración nueva |
| Miniaturas | `WindowThumbnails` (Dock) | misma clase y cola |
| Activar/cerrar ventana | `WindowActions` | `Execute(DockAction)` |
| Colocar ventanas | `WindowArranger` (Workspaces) | Snap llama a `Apply` |
| Capturar una región | `CaptureOverlay`, `ScreenGrabber` | `BeginRegionPick` (un destino alternativo) |
| Gancho de bajo nivel | `WinKeyHook` | un solo gancho con contador de uso |
| Atajos | `GlobalHotkey`, `HotkeyAudit` | `HotkeyBinder` + `HotkeyRegistry` |
| Acciones de Spotlight | `SpotPro.Actions` | se registran en `ActionRegistry` (misma lista) |
| Estantería | `NotchWindow.Extras` | ampliada, no sustituida |
| Historial del portapapeles | Isla | Live Text y Shelf+ copian al portapapeles normal |

## 11. Resultados de la primera prueba en Windows real (paquete del CI #218) y pendientes
Probado por el propietario en un solo monitor, versión portable. **Funcionaba:** Mission Control (activar, miniaturas, «(minimizada)»), Snap (zonas, colocación), Live Text desde Spotlight, Spotlight Actions, Gestures (esquinas), Shelf+ (todo lo probado). Hallazgos:
| # | Hallazgo | Estado |
|---|---|---|
| 1 | Mission Control con **dos monitores**: no se pudo probar | PENDIENTE DE VALIDACIÓN |
| 2 | Snap: al soltar en un borde con Mayús aparece además la previsualización del Snap de Windows, pero la ventana queda en la zona de Hatsu | Conocido (cosmético); no se suprime el Snap nativo |
| 3 | Snap: una app ejecutada como administrador no se deja mover (UIPI) | Límite de Windows, documentado; no se eleva Hatsu |
| 4 | Live Text: el **atajo** no respondió (por Spotlight sí). Causa más probable: Windows rechazó Ctrl+Alt+Espacio porque otro programa lo tiene; Ajustes › Módulos lo muestra en la línea de estado | A confirmar con el texto de estado; ahora se puede elegir otra combinación |
| 5 | **Cambiar atajos no funcionaba** (había que escribir el texto a mano) | CORREGIDO: captura de teclado (clic en el campo y pulsar la combinación; Retroceso la quita; Esc cancela) — pendiente de validar |
| 6 | Gestures: la asignación se cambiaba con clics sucesivos | CORREGIDO: lista desplegable con la acción actual marcada y solo las acciones de módulos encendidos — pendiente de validar |
| 7 | Gestures: «Mostrar/ocultar el Dock» solo activa o desactiva «Ocultar automáticamente» | **INCOMPLETO** (no muestra/oculta al instante) |
| 8 | Spotlight: «usar la distribución» solo cambia la distribución activa de Snap, no reordena las ventanas abiertas | **INCOMPLETO** |
