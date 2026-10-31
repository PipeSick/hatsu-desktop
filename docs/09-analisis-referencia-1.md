# Análisis de la referencia real 1 (macOS Tahoe, Dock en reposo) frente a WinMac

Estado: **análisis; el renderizador y `DockMetrics`/`GlassStyle` NO se han modificado.** Esta tabla alimentará una única iteración de calibración cuando existan las tres capturas.
Esta captura es una **referencia visual con medidas de proporción**, no una especificación completa de medidas físicas.

## 0. Advertencia sobre la imagen y la escala
- La imagen recibida mide **2000 × 202 px** (la segunda, 2000 × 293 px). El usuario indicó 2048 × 175 px para la primera: **no coincide**. Puede que la copia que llega a esta sesión esté reescalada o sea otra versión. Por eso **no se asume ninguna escala (píxeles por punto) ni resolución de pantalla**.
- Todas las medidas se dan en **píxeles de la captura** y, sobre todo, como **razón respecto al ancho del ícono**, que no depende de la escala. Un control cruzado con la segunda imagen (otra escala, ícono ≈ 90,5 px frente a 87 px) da razones iguales dentro del 1–3 %: consistencia ref2(otra escala): ancho 90.5, hueco 32.0 → hueco/ícono 0.354 (ref1 0.353); barra/ícono 1.680 (ref1 1.673); margen sup/ícono 0.326 (ref1 0.335).
- La razón **solo es válida si la imagen no está deformada** (misma escala en x e y). Los íconos salen casi cuadrados (alto/ancho ≈ 1,01–1,03), así que no hay deformación apreciable.
- El tamaño absoluto del Dock (el control «Tamaño» de macOS) es una **preferencia del usuario**, no una proporción: se tratará como parámetro configurable. Tampoco se indicó si el Dock de la captura estaba en apariencia clara u oscura (**pendiente de confirmar**).

## 1. Método y reproducibilidad
1. Detección **asistida** de bordes por gradiente (propone puntos); los puntos finales se aplican con `tools/calibration/test/aplicar_puntos.mjs`, que los pasa por `calibracion.html` y exporta el JSON con el mismo código que la herramienta manual. Mismos puntos + misma imagen = mismo resultado.
2. Convención de borde: frontera de píxel = índice del gradiente máximo + 1. Incertidumbre mínima ±1 px.
3. Los valores del código se **leen de los fuentes** (`tools/calibration/valores_codigo.py`), no se escriben a mano.
4. La imagen, los puntos y los JSON de medidas están en `calibration/private/` (ignorado por git). Aquí solo van valores numéricos derivados.
5. En la herramienta el valor aparece con la etiqueta «pt», pero al usar 1 px/pt **son píxeles de captura**.

Clases: **medida directa** (observable en los píxeles) · **estimación** (ajuste de un modelo o criterio visual; con su error) · **pendiente de confirmar**.

## 2. Geometría — tabla comparativa (medidas directas salvo el radio)
| Parámetro | Cat. | macOS (px de captura) | Variación observada | macOS ÷ ícono | WinMac (código, DIP) | Código ÷ ícono | macOS equivalente con BaseSize=60 | Nota |
|---|---|---|---|---|---|---|---|---|
| Ancho visible del ícono | observable | 87 | 86–87 (n=6) | 1.000 (÷ ícono) | 60 | 1.000 | 60.0 |  |
| Alto visible del ícono | observable | 88 | 88–90 (n=6) | 1.011 (÷ ícono) | 60 | 1.000 | 60.7 | ≈ +2 % vs ancho (sombra/antialias); se toma el ancho como tamaño |
| Separación entre íconos | observable | 30 | 30–31 (n=5) | 0.345 (÷ ícono) | 6 | 0.100 | 20.7 |  |
| Altura de la barra | observable | 145 | 144–145 (convención de borde) | 1.667 (÷ ícono) | 82 | 1.367 | 100.0 |  |
| Margen interno izquierdo | observable | 29.5 | ±1,5 (borde del ícono Finder) | 0.339 (÷ ícono) | 10 | 0.167 | 20.3 |  |
| Margen interno superior | observable | 29 | ±1 | 0.333 (÷ ícono) | 8 | 0.133 | 20.0 |  |
| Margen interno inferior | observable | 28 | ±1 | 0.322 (÷ ícono) | 14 | 0.233 | 19.3 |  |
| Radio de esquina (ajuste de círculo) | estimable | 43.1653 | RMS del ajuste 0.22 px | 0.496 (÷ ícono) | 26 | 0.433 | 29.8 | la razón respecto a la barra es la relevante: Mac ≈ 0.298, código ≈ 0.317 |
| Indicador: diámetro | observable | 7 | 6–8 (umbral sobre antialias) | 0.080 (÷ ícono) | 4 | 0.067 | 4.8 |  |
| Indicador: centro bajo el borde inferior del ícono | observable | 18.9 | 18.1–19.9 (n=5) | 0.217 (÷ ícono) | 6.5 | 0.108 | 13.0 |  |
| Indicador: centro sobre el borde inferior de la barra | observable | 9.1 | ±1 | 0.105 (÷ ícono) | 7.5 | 0.125 | 6.3 |  |
| Separador: espacio último ícono → línea | observable | 43 | 40–44 (2 separadores) | 0.494 (÷ ícono) | 16 | 0.267 | 29.7 |  |
| Separador: espacio línea → siguiente ícono | observable | 40 | ±1 | 0.460 (÷ ícono) | 11 | 0.183 | 27.6 |  |
| Separador: altura de la línea | observable | 108 | ≈ 74 % de la barra | 0.745 (÷ barra) | 49.2 | 0.600 | 61.1 (con barra=82) |  |
| Separador: grosor | observable | 2 | 2 columnas (perfil medio) | 0.023 (÷ ícono) | 1 | 0.017 | 1.4 |  |

Lectura (razones respecto al ícono; el ícono de macOS se toma como 1,00):
- Todas las **holguras** de WinMac son menores que las de macOS: separación entre íconos **×0,29**, márgenes **×0,4–0,7**, espacio del separador **×0,4–0,55**, indicador a **×0,5** de distancia del ícono. A la vez, el ícono de WinMac ocupa el **73 %** de la altura de la barra frente al **60 %** de macOS: relativamente, **los íconos de WinMac son demasiado grandes**, no demasiado pequeños.
- El **radio de esquina** es la única magnitud casi acertada (razón respecto a la barra 0,298 en macOS frente a 0,317 en el código).
- El **indicador** es algo mayor en macOS (×1,2) y está a casi el doble de distancia del ícono.

## 3. Material, bordes y sombras — **estimaciones** (no medidas directas)
| Aspecto | macOS (captura) | WinMac (código) | Clase / confianza |
|---|---|---|---|
| Ganancia de luminancia interior÷exterior (k) | **0,62** (regresión sobre 29 ventanas, R²=0,82); `ajustar.py material`: 0,59 (RMS 15,9 → el modelo no basta) | 0,586 (cálculo analítico a y≈10 % de la barra) | estimación, media. Casi igual |
| Término aditivo del vidrio (c) | **≈ 31** (luminancia; RGB 42/30/36 por ajuste) → tinte **gris medio-oscuro** | **105,7** (tinte blanco, α·255) | estimación, media. El código es ≈ **3,4× más claro/lechoso** |
| Saturación del material | **inconcluso**: razón HSV interior/exterior ≈ 0,95 (n=5 columnas saturadas), pero el ajuste de modelo cae al límite inferior (s=0,5) con RMS 15,9 | `Saturation` = 1,7 | pendiente de confirmar. Con este fondo no se separa la saturación del desenfoque; se necesitan los fondos sólidos de calibración |
| Borde superior | filete de **≈ 2 px** brillante: L=121 sobre un interior de ≈ 73–86 y exterior 64–66 (**α blanco ≈ 0,26–0,36**), seguido de un resplandor interior que decae en ≈ 6–8 px (91→73) | trazo exterior α **0,80** arriba (→0,28 en medio, 0,12 abajo) + filo interior | estimación, media. El código es ≈ **2–3×** más intenso arriba |
| Bordes laterales | contorno **oscuro** de ≈ 1–2 px: izquierdo L=142 entre exterior 238 e interior 167 (−25 respecto al interior); derecho 32–34 entre 44 y 50 (−13) | trazo **claro** con gradiente vertical (0,28–0,12 en el medio) | estimación (signo opuesto), media |
| Borde inferior | filete de ≈ 1–2 px: L=103 sobre un interior de ≈ 50 y exterior de 56 (α blanco ≈ 0,26) | trazo exterior α 0,12 + filo interior α 0,30 | estimación, media |
| Sombra lateral | oscurecimiento **≤ 2 %** a la izquierda (238→243 en 40 px) y ≤ 6 % a la derecha (44→46); puede ser solo degradado del fondo, así que es una **cota superior** | oscurecimiento acumulado **≈ 51 %** justo fuera del borde lateral y alcance de 25,6 px (cálculo analítico: las 14 capas multiplican su opacidad) | estimación, **alta** confianza en que el código es mucho más intenso |
| Sombra superior | ninguna (exterior constante 192 hasta el filete) | desfase vertical 10 | medida directa |
| Sombra inferior | **no medible**: el recorte deja ≈ 9 px bajo la barra y el fondo oscurece hacia abajo | — | pendiente de confirmar |

## 4. Prioridades pedidas y qué dice la medida
| Prioridad | Qué se vio | Confianza para corregir |
|---|---|---|
| Saturación excesiva del material | La captura no prueba un realce de saturación; no es concluyente (ver arriba). Es probable que el exceso de WinMac venga de `Saturation`=1,7 **más** el tinte lechoso | baja–media; se confirma con fondos sólidos |
| Grosor e intensidad de bordes | Filete de ≈ 2 px (en la escala de la captura) con α ≈ 0,3; WinMac usa α 0,8 arriba y un filo interior | media |
| Intensidad de sombras | macOS casi no tiene sombra lateral; **el código genera ≈ 51 %** por acumulación de capas (defecto de diseño del apilado, no solo de parámetros) | **alta** |
| Márgenes internos | macOS ≈ 0,33 × ícono por los cuatro lados; WinMac 0,13–0,23 | alta (observable) |
| Tamaño relativo de los íconos | macOS: ícono = 60 % de la altura de la barra; WinMac 73 % | alta (observable) |
| Separadores | línea de 2 px, altura 74 % de la barra, ≈ 0,48 × ícono de espacio a cada lado, α ≈ 0,35–0,48; WinMac: 1 px, 60 %, espacio 0,27/0,18 | alta (geometría) / media (alfa) |
| Indicadores | diámetro 0,08 × ícono; centro a 0,22 × ícono bajo el ícono | alta |
| Geometría general | altura de barra 1,67 × ícono (WinMac 1,37) | alta |

## 5. Lo que NO se ha tocado ni medido todavía
- **Magnificación** (curva, MaxScale, vecinos): sin cambios hasta la segunda captura.
- **Etiquetas**: sin cambios hasta la tercera. La segunda imagen recibida parece mostrar la etiqueta («WhatsApp») pero **el ícono bajo ella no parece ampliado** (ancho ≈ igual al de sus vecinos, 90 px): si debía ser la escena de magnificación, la ampliación estaba desactivada o es mínima; **hay que confirmarlo**. No se usó para calibrar.
- **Íconos de las apps**: los de WinMac son de demostración; el arte no se calibra con capturas y no se copia nada de Apple.
- Tiempos de animación, refracción, iluminación dinámica y distancia del Dock al borde de la pantalla (el recorte la oculta).

## 6. Iteración de calibración aplicada (solo lo cubierto por la referencia 1)
Alcance fijado por el usuario: **geometría, separadores, indicadores, bordes, sombras y material**. **No** se tocó la curva de magnificación (`MaxScale`, `Radius`, forma coseno) ni la etiqueta (esperan las capturas 2 y 3), ni se introdujo captura continua ni refracción, ni se copiaron recursos de Apple.

| Parámetro (÷ ícono salvo indicación) | macOS (medido) | WinMac antes | WinMac después | Desvío vs macOS |
|---|---|---|---|---|
| Separación entre íconos | 0.345 | 0.100 | 0.345 | 71 % → 0 % |
| Margen lateral | 0.339 | 0.167 | 0.339 | 51 % → 0 % |
| Margen superior | 0.333 | 0.133 | 0.333 | 60 % → 0 % |
| Margen inferior | 0.322 | 0.233 | 0.322 | 27 % → 0 % |
| Altura de la barra | 1.667 | 1.367 | 1.655 | 18 % → 1 % |
| Radio de esquina (÷ barra) | 0.298 | 0.317 | 0.298 | 7 % → 0 % |
| Indicador: diámetro | 0.080 | 0.067 | 0.080 | 17 % → 1 % |
| Indicador: centro bajo el ícono | 0.217 | 0.108 | 0.217 | 50 % → 0 % |
| Separador: espacio a la izquierda | 0.494 | 0.267 | 0.494 | 46 % → 0 % |
| Separador: espacio a la derecha | 0.460 | 0.183 | 0.460 | 60 % → 0 % |
| Separador: altura (÷ barra) | 0.745 | 0.600 | 0.745 | 19 % → 0 % |
| Separador: grosor | 0.023 | 0.017 | 0.023 | 28 % → 0 % |
| Ícono ÷ altura de la barra | 0.600 | 0.732 | 0.604 | 22 % → 1 % |

| Material / bordes / sombra | macOS (estimado) | Antes | Después claro | Después oscuro |
|---|---|---|---|---|
| Ganancia k (interior÷exterior) | 0,59–0,62 | 0.59 | 0.71 | 0.60 |
| Término aditivo c (0–255) | ≈ 31 | 106 | 73 | 31 |
| Realce de saturación | ≈ 1,0 (inconcluso) | 1.7 | 1 | 1 |
| Filete superior (α blanco) | 0,26–0,36 | 0.8 | 0.32 | 0.32 |
| Contorno oscuro en los costados (α negro) | ≈ 0,1–0,25 (equiv.) | 0 | 0.2 | 0.2 |
| Sombra: opacidad junto al borde lateral | ≤ 0,02–0,06 (cota) | 0.51 | 0.060 | 0.080 |

**Cómo leer la primera tabla:** los parámetros se *fijaron* a las razones medidas, así que el «0 %» **es una consecuencia de haberlos igualado, no una validación**. La comprobación real es medir los PNG/capturas de WinMac con la misma herramienta (`aplicar_puntos.mjs` + `calibracion.html`); pendiente.
**Cómo leer la segunda:** la «Después oscuro» reproduce k≈0,60 y c≈31 medidos **suponiendo que la captura era apariencia oscura (PENDIENTE de confirmar)**. El preset claro conserva su material anterior (solo cambian saturación, borde y sombra) porque no hay referencia clara; sigue siendo más lechoso que la referencia.

### Cambios (código)
| Área | Cambio | Clase de evidencia |
|---|---|---|
| `DockMetrics` | La geometría pasa a **proporciones del ícono** (`GapRatio`, `PadXRatio`, `PadTopRatio`, `PadBottomRatio`, `BarRadiusOfBarHeight`, `SeparatorLeft/Right/LineRatio`, `SeparatorHeightOfBarHeight`, `DotRatio`, `DotCenterBelowIconRatio`); los valores absolutos son derivados. `BaseSize` (60) sigue siendo preferencia del usuario | medida directa (±1 px) |
| `DockLayout` | El separador se coloca con sus espacios izquierdo/derecho medidos y grosor propio; `ItemFrame.X` del separador = borde izquierdo de la línea | medida directa |
| `DockPainter` | Separador como rectángulo de 2 px al 74 % de la barra; indicador a 0,22 × ícono bajo el ícono; **ícono de reemplazo = mosaico a sangre completa** (se elimina la placa blanca gruesa); contorno del ícono α 0,30; corregido el gradiente del mosaico (estaba en coordenadas equivocadas) | medida directa / criterio de usuario |
| Sombra (`ShadowMath`) | Capas con **opacidad acumulada controlada** (ya no 51 %): claro 0,06, oscuro 0,08; alcance 19 px, desfase 4 px (**el desfase vertical no es medible**: el recorte deja 9 px bajo la barra) | estimación (cota superior); defecto del apilado: alta confianza |
| Bordes | Se elimina el filo interior; filete claro de 1,25 DIP solo arriba/abajo (α 0,32 / 0,26) y contorno oscuro fino (α 0,20) en todo el perímetro; resplandor interior limitado al 10 % superior | estimación, media |
| Material | Saturación 1,7 → **1,0** (ambos temas); preset oscuro: tinte gris 0,31 con α 0,40 | saturación inconclusa (baja confianza); k y c: estimación media |

### Pruebas
62 pruebas unitarias de `Dock.Core` (16 nuevas en `CalibrationTests`): fijan las razones medidas con ±3 %, el escalado con el tamaño del ícono, el espaciado del separador, la no-acumulación de la sombra y las condiciones del material. Si alguien cambia estas proporciones, las pruebas obligan a justificarlo con nuevas medidas. **No hay prueba visual automática**: el aspecto real se ve en la ventana o en los PNG del CI.

### Pendiente tras esta iteración
1. Medir los PNG nuevos de WinMac con la misma herramienta (verificación real, no tautológica).
2. Confirmar apariencia clara/oscura de la referencia y fondos sólidos para fijar saturación y tinte.
3. Segunda y tercera captura: magnificación y etiqueta. Nota: `Radius` (150 DIP) es absoluto, y con el paso mayor entre íconos (≈ 81 DIP) la ampliación alcanza a menos vecinos que antes; es un efecto de la nueva geometría, pendiente de calibrar con la captura 2.
4. Íconos propios de apps (los de demostración siguen siendo genéricos).
5. Desfase vertical y alcance reales de la sombra inferior (requiere captura con más margen bajo el Dock).
