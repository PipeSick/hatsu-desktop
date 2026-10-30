# Fidelidad visual — referencia macOS Tahoe

La fidelidad es criterio principal. Este documento define **qué se evalúa**, **cómo se calibra contra una referencia real** y
**qué rúbrica se usa**. No contiene medidas oficiales de Apple ni usa recursos de Apple.

## 1. Estado de la evidencia
| Clase | Contenido |
|---|---|
| **Hipótesis** | Todas las cifras de `DockScene` (tamaño 60, magnificación ×1,7, alcance 150, radio de barra 26, espaciado 6, indicador 4, etc.) son una **aproximación mía**. No están medidas contra Tahoe. |
| **Limitación** | No tengo acceso a un Mac ni a capturas de Tahoe en esta sesión; Tahoe además incorpora Liquid Glass (refracción/lensing) que **no existe como API nativa de Windows**. |
| **Procedimiento** | La calibración se hace con **capturas de referencia que tomes tú en tu Mac** (§3). Hasta entonces, los prototipos son *«inspirados en»*, no *«calibrados con»*. |

## 2. Atributos evaluados
| # | Atributo | Qué se compara | Dónde se define hoy |
|---|---|---|---|
| 1 | Geometría | Altura/anchura de la barra, tamaño de ícono, radio de barra, curva del ícono (squircle) | `DockScene`, `Squircle` |
| 2 | Transparencias | Opacidad de la barra, cómo se ve el fondo a través | variantes de cada prototipo |
| 3 | Materiales | Blur, saturación, luminosidad (vidrio) | acrílico DWM / degradado simulado / (S4b: captura + shader) |
| 4 | Bordes | Borde fino con brillo especular superior | degradado de borde |
| 5 | Sombras | Sombra de la barra y de los íconos | `DropShadowEffect` / capas D2D |
| 6 | Animaciones | Curva de magnificación, resorte, ocultar/mostrar | `DockScene.Layout`, `BenchRun` (smoothstep) |
| 7 | Ampliación de íconos | Factor máximo, alcance, solapes, desplazamiento vertical | `MaxScale`, `Radius` |
| 8 | Espaciado | Hueco entre íconos, margen lateral, separador | `Gap`, `PadX`, `SeparatorGap` |
| 9 | Indicadores | Punto de app abierta: tamaño, posición, opacidad | `DotSize` |
| 10 | Ocultamiento | Duración, curva, retención, zona de revelado | `BenchRun` (0,35 s + 0,8 s) |

## 3. Procedimiento de calibración (lo haces tú, una vez)
1. En tu Mac con Tahoe, con el **fondo de pantalla neutro por defecto** y un Dock con ~12 apps, haz capturas de pantalla completa (⇧⌘3) en: reposo, cursor sobre el 6.º ícono, y una captura de **vídeo corto** (⇧⌘5) del hover y del ocultar/mostrar.
2. Anota: resolución, escala, y el ajuste «Tamaño» y «Ampliación» del Dock.
3. Guárdalas **en tu equipo** (no las subas al repositorio: son material de Apple; se usan solo como referencia privada de comparación).
4. Yo derivo de ellas, midiendo píxeles, los parámetros de `DockScene` y los anoto en este documento como **medidos**.

## 4. Rúbrica de evaluación visual (para decidir tecnología)
Puntuación por atributo, a juzgar por ti comparando capturas del prototipo con tu referencia, a distancia normal de uso:

| Puntos | Significado |
|---|---|
| 0 | No implementado o claramente distinto |
| 1 | Reconocible pero genérico («parece un dock cualquiera») |
| 2 | Muy cercano; las diferencias se ven al comparar lado a lado |
| 3 | Difícil de distinguir de la referencia a simple vista |

Se suman los 10 atributos (máx. 30) por variante. Ver pesos y puertas en `03-criterios-aceptacion.md`.

## 5. Escalera de fidelidad (técnica)
| Nivel | Técnica | Estado |
|---|---|---|
| F1 | Ventana propia + acrílico DWM + esquinas del sistema | S2 `dwm`, S3 `acrylic` |
| F2 | F1 + región de ventana con radio propio | sin prototipo; hipótesis |
| F3 | Vidrio simulado (degradados, borde especular, sombra) | S2 `layered`, S4 `glass` |
| F4 | Captura del fondo + shader D3D11 (blur, saturación, borde especular, lensing) + DirectComposition | **S4b, aún no construido**; se decide con los datos |

## 6. Intención geométrica compartida
`docs/img/geometria-reposo.svg`, `geometria-hover.svg`, `geometria-oculto.svg`: generadas por `tools/PreviewGeometry.cs` a partir de
**la misma `DockScene`** que usan los tres prototipos. **No son capturas de ningún prototipo**: sirven para saber qué debería verse.

## 6.1 Recursos
Íconos 100 % originales (degradado + letra). Prohibido incluir iconos, fuentes (SF), fondos, sonidos o capturas de Apple en el repositorio.
