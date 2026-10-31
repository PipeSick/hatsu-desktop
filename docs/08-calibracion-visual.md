# Calibración visual del Dock contra macOS Tahoe — procedimiento

Estado: **procedimiento y herramientas listos; sin capturas de referencia todavía.** Nada del renderizador ni de `DockMetrics`/`GlassStyle` se ha modificado.
Decisión del usuario: **no se ajusta nada de forma arbitraria**; primero se miden referencias reales de un Mac con macOS Tahoe y se prepara **una única iteración de calibración**.

## 1. Qué se mide y con qué grado de certeza
Dos categorías, que se mantienen separadas en todos los informes:

| Categoría | Significado | Ejemplos | Precisión esperada |
|---|---|---|---|
| **Observable** | Se mide directamente sobre los píxeles de la captura | tamaño y separación de íconos, altura de la barra, márgenes, posición del indicador, tamaño de íconos vecinos al ampliar, desplazamiento de vecinos, tamaño y posición de la etiqueta, distancia al borde de pantalla | ±1 px (≈ ±0,5 pt en Retina 2×); error dominado por el antialiasing del borde |
| **Estimable** | Solo se infiere ajustando un modelo o por criterio visual | radio de esquina (ajuste de círculo; la esquina «continua» no es un círculo), opacidad/tinte/saturación del vidrio, desenfoque σ, grosor e intensidad del borde (límite = 1 px), sombra (desfase, desenfoque, opacidad), refracción («lente»), curva de magnificación (forma y alcance) | Depende del modelo; cada ajuste devuelve su error. **No son verdades**, son estimaciones con supuestos declarados |

**No se pueden medir con capturas estáticas** (quedan fuera de esta iteración y se documentan): iluminación dinámica del vidrio al mover el puntero, respuesta del material al contenido en movimiento, y los tiempos de animación (requieren vídeo; ver §6, opcional).

### Lista de medidas (incluye todo lo que pediste)
| Pedido | Medidas (ids en la herramienta) | Cat. | Parámetro actual del código que se calibrará |
|---|---|---|---|
| Tamaño de los íconos | `icon_size_rest`, `icon_size_rest_h` | obs | `DockMetrics.BaseSize` = 60 |
| Separación entre íconos | `icon_gap_rest` | obs | `Gap` = 6 |
| Altura del Dock | `bar_height`, `bar_width_total` | obs | `BarHeight` = BaseSize+PadTop+PadBottom = 82 |
| Márgenes internos | `bar_pad_left/right/top/bottom` | obs | `PadX` = 10, `PadTop` = 8, `PadBottom` = 14 |
| Radio de esquinas | `bar_corner_radius` | est (ajuste) | `BarRadius` = 26 |
| Posición de indicadores | `indicator_diameter`, `indicator_below_icon`, `indicator_above_bar_bottom` | obs | `DotSize` = 4; centro a barBottom−PadBottom+6,5 |
| Separador | `separator_gap_left/right`, `separator_height` | obs | `SeparatorGap` = 10; alto = 60 % de la barra |
| Magnificación | `hover_icon_size_center` → MaxScale; `hover_neighbor_p1..p3/m1..m3` → forma/alcance | MaxScale obs; alcance est | `MaxScale` = 1,7; `Radius` = 150 (coseno) |
| Desplazamiento de vecinos | `icon_centers` en reposo y en hover → `ajustar.py desplazamiento` | obs | modelo: la barra crece centrada, sin solapes |
| Elevación del ícono ampliado | `hover_icon_rise`, `hover_icon_bottom_gap`, `hover_bar_height` | obs | (ventana: `BaseSize·(MaxScale−1)`) |
| Etiquetas | `label_height`, `label_width`, `label_text_width`, `label_gap_to_icon`, `label_corner_radius`, `label_align`, `label_cap_height`, `label_colors` | mayoría obs; tamaño de fuente y alfa est | `LabelHeight` = 28, `LabelGap` = 10, fuente 13 |
| Distancia al borde de pantalla | `bar_bottom_margin` | obs | (colocación del host) |
| Bordes | `border_profile_top`, `border_profile_bottom` → `ajustar.py borde` | est (límite 1 px) | `RimTopAlpha`, `RimBottomAlpha`, `InnerRimAlpha` |
| Sombras | `shadow_profile_below`, `shadow_profile_side` → `ajustar.py sombra` | est | `ShadowOffsetY` = 10, `ShadowBlur` = 16, `ShadowAlpha` = 0,30 |
| Material (vidrio saturado) | `glass_sample` en 6 fondos → `ajustar.py material` | est | `TintAlpha` = 0,20, `Saturation` = 1,7, `BlurSigma` = 22 |
| Desenfoque / refracción | `blur_edge_profile`, `refraction_grid_points` | est (baja fiabilidad / cualitativa) | `BlurSigma`; refracción no implementada |

## 2. Preparar el Mac (una sola vez)
Anota estos datos en `calibration/private/ficha.txt` (ver plantilla en §7):
1. **Versión exacta** de macOS (Menú Apple → Acerca de este Mac) y modelo/pantalla.
2. **Resolución**: Ajustes del Sistema → Pantallas. Usa la resolución *predeterminada*; anota los «puntos» (p. ej. 1512×982) y los píxeles nativos (p. ej. 3024×1964). **Píxeles por punto = ancho en px / ancho en puntos** (normalmente 2).
3. **Escritorio y Dock**: posición **abajo**; «Tamaño» y «Ampliación» (anota la posición de cada control; no los cambies entre capturas); «Mostrar indicadores de aplicaciones abiertas» **activado**; «Ocultar y mostrar automáticamente» **desactivado** (para vídeos de autohide se activa aparte).
4. **Accesibilidad → Pantalla**: «Reducir transparencia» **desactivado**, «Aumentar contraste» **desactivado**, «Reducir movimiento» **desactivado**. Estilo de íconos del sistema en **Predeterminado** (anótalo).
5. **Contenido del Dock**: ≥ 9 apps, 3–4 abiertas (con indicador), con el separador y la zona derecha (carpeta + Papelera). Anota cuántos íconos hay y cuáles están abiertos.
6. **Fondos de calibración**: abre `tools/calibration/calibracion.html` → pestaña 1 → introduce el tamaño nativo → descarga los PNG → ponlos como fondo (modo «Rellenar»; desactiva fondos dinámicos/rotación; nombra el que uses en cada captura).

## 3. Capturas a tomar (Mac)
Atajo: **⇧⌘3** (pantalla completa, PNG, tamaño nativo). Mueve el puntero fuera del Dock salvo en las escenas de hover. Guarda en `calibration/private/mac/` con el nombre indicado (**no subir al repositorio**).

| Archivo | Tema | Fondo | Puntero | Para medir |
|---|---|---|---|---|
| `mac-reposo-claro.png` | claro | gris50 | fuera del Dock | geometría, márgenes, indicadores, separador, radio, `bar_bottom_margin`, `icon_centers` |
| `mac-reposo-oscuro.png` | oscuro | gris50 | fuera | ídem (comparar con claro) |
| `mac-hover-N-claro.png` | claro | gris50 | **centro** del ícono N (elige uno de en medio) | ampliación, vecinos ±1..3, elevación, etiqueta, `icon_centers` |
| `mac-hover-primero-claro.png` | claro | gris50 | centro del primer ícono | comportamiento en el extremo |
| `mac-hover-ultimo-claro.png` | claro | gris50 | centro del último ícono | ídem |
| `mac-hover-entre-claro.png` | claro | gris50 | **entre** dos íconos | simetría de la curva |
| `mac-etiqueta-sinmag-claro.png` | claro | gris50 | sobre un ícono con la **ampliación desactivada** (restáurala después) | etiqueta aislada |
| `mac-fondo-blanco-claro.png` … `-negro`, `-gris50`, `-rojo`, `-verde`, `-azul` | claro | cada color | fuera | `glass_sample` (material), bordes sobre blanco/negro, **sombra sobre blanco** |
| ídem en `oscuro` | oscuro | los 6 colores | fuera | ídem |
| `mac-mitad-vertical-claro.png`, `mac-mitad-horizontal-claro.png` | claro | mitad blanca/negra | fuera | transición de desenfoque (baja fiabilidad) |
| `mac-rejilla-claro.png` | claro | rejilla | fuera | refracción (cualitativa) |

Consejos: la captura debe contener el Dock **completo y sin ocultar**; si el sistema añade una sombra de ventana o el puntero, no importa mientras no tape los bordes que se miden. Nombra el archivo exactamente: el nombre de escena se usa al exportar.

## 4. Medir (en tu equipo, sin subir nada)
1. Abre `tools/calibration/calibracion.html` con el navegador (doble clic).
2. Pestaña **2 · Medir captura**: carga la captura; **Origen = Mac**; **Escena** = nombre (p. ej. `reposo-claro`); **Píxeles por punto** = el de tu ficha. Elige cada medida de la lista de la izquierda (indica si es *observable* o *estimable* y qué parámetro del código afecta) y haz clic en los puntos indicados (zoom con la rueda; flechas = 1 px). Exporta el JSON: `calibration/private/medidas/medidas-mac-<escena>.json`.
3. Repite **con los PNG de WinMac del CI** (`dock-light-rest.png`, `dock-light-hover.png`, etc., del artefacto `smoke-<sha>`; están a **2×**, así que píxeles por punto = **2**; en la escena `hover` el puntero está sobre el 4.º ícono y en `open` sobre el 5.º): **Origen = WinMac**. Así ambas fuentes se miden con el mismo método y el mismo criterio de borde.
4. Pestaña **3 · Comparar**: carga el JSON de Mac y el de WinMac de la misma escena → tabla con Mac / WinMac / valor del código / diferencias.
5. Ajustes numéricos (solo Python 3 estándar, sin instalar nada):
```
python3 tools/calibration/ajustar.py magnificacion  mac-reposo-claro.json mac-hover-N-claro.json
python3 tools/calibration/ajustar.py desplazamiento mac-reposo-claro.json mac-hover-N-claro.json
python3 tools/calibration/ajustar.py material mac-fondo-blanco-claro.json mac-fondo-negro-claro.json mac-fondo-gris50-claro.json mac-fondo-rojo-claro.json mac-fondo-verde-claro.json mac-fondo-azul-claro.json
python3 tools/calibration/ajustar.py sombra mac-fondo-blanco-claro.json
python3 tools/calibration/ajustar.py borde  mac-fondo-negro-claro.json
```
Cada resultado etiqueta lo **observable** y lo **estimado** y devuelve su error de ajuste.
6. Envíame los JSON de medidas (contienen coordenadas y muestras de color, **no la imagen**). Las capturas de Apple **no se suben al repositorio**; solo se commitearán, si lo apruebas, los valores agregados (`calibration/medidas-resumen-mac.json`).

## 5. Qué haré con las medidas (una única iteración)
1. Tabla Mac vs WinMac vs código (observables con tolerancia ≤ 1 pt o ≤ 3 %).
2. Cambios **solo** en `DockMetrics`/`GlassStyle` y en el dibujo de borde/sombra donde los perfiles lo exijan; el modelo de magnificación se cambia solo si el error RMS del ajuste coseno es alto.
3. Pruebas unitarias actualizadas con los valores medidos (p. ej. alto de barra, MaxScale), CI de Windows y nuevo juego de PNG.
4. Segunda medición sobre los PNG nuevos con la misma herramienta y comparación; criterios en §6.

### Criterios de aceptación de la calibración (propuestos; sujetos a tu aprobación)
| Medida | Criterio |
|---|---|
| Observables de geometría (tamaño, separación, alturas, márgenes, posición de indicador/etiqueta) | |Δ| ≤ 1 pt o ≤ 3 % (lo que sea mayor) |
| MaxScale | |Δ| ≤ 0,03 |
| Curva de magnificación (escala de vecinos ±1..±3) | error RMS ≤ 0,03 |
| Radio de esquina (estimado) | |Δ| ≤ 2 pt |
| Borde (ancho a media altura) | |Δ| ≤ 0,5 pt y intensidad dentro del 25 % |
| Sombra (oscurecimiento máximo, desfase, σ) | máximo ±0,03, desfase ±3 pt, σ ±25 % |
| Material (α, saturación) | α ±0,05, s ±0,2; y, sobre todo, comparación visual lado a lado sobre los 6 fondos |
| Rúbrica visual 0–3 × 10 atributos (docs/02) | **sigue sin estar aprobada**; se revisará con las capturas |

## 6. Opcional / posterior: tiempos de animación
Con vídeo (⇧⌘5 → «Grabar pantalla completa»): barrido lento y rápido del puntero sobre el Dock, y autohide (aparición/desaparición). Se pueden extraer fotogramas con QuickTime (paso a paso) o `ffmpeg` si lo tienes. Da duraciones y retardos observables (±1 fotograma ≈ 17 ms) y permite estimar los parámetros del resorte. **No se hace en esta iteración.**

## 7. Plantilla de ficha (`calibration/private/ficha.txt`)
```
macOS: ____ (build ____)   Modelo/pantalla: ____
Resolución: ____×____ puntos  =  ____×____ px   → px/pt = ____
Dock: posición abajo | Tamaño (control) = ____ | Ampliación (control) = ____ | autohide: no | indicadores: sí | recientes: ____
Accesibilidad: reducir transparencia no | aumentar contraste no | reducir movimiento no
Estilo de íconos: Predeterminado
Íconos en el Dock: ____ (abiertos: ____) | separadores: ____
```

## 8. Límites y reglas
- **Sin recursos de Apple en el repositorio**: ni capturas, ni íconos, ni fuentes. Los íconos de WinMac serán propios; la forma (squircle) se calibra, el arte no se copia.
- Las capturas de Tahoe pueden incluir iluminación dinámica y otros efectos del material que una captura estática no separa: lo que no se pueda medir se marca como **no calibrable** y se evalúa solo visualmente.
- El renderizador **no se toca** hasta tener las medidas.
- Pruebas pendientes de Windows físico: `docs/PENDIENTE-WINDOWS-FISICO.md`.
- La herramienta se autoprueba con una captura sintética de geometría conocida (`tools/calibration/test/`): mide bien alturas, anchos, separaciones, radio de esquina y perfiles; `ajustar.py` recupera parámetros conocidos de datos sintéticos. **Eso valida la herramienta, no ninguna referencia de macOS.**
