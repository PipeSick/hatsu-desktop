# Aproximar «Liquid Glass» con APIs documentadas de Windows

Estado: investigación + primera implementación del **nivel 1**. Los parámetros visuales son **provisionales** hasta tener referencias reales (docs/02-fidelidad.md).
Clasificación: **[V]** fuente consultada · **[C]** conocimiento previo · **[H]** hipótesis/estimación sin medir · **[B]** compilado, no ejecutado.

## 1. Qué pide el efecto y de qué se compone
Liquid Glass (Tahoe) = material translúcido que **desenfoca y satura** lo que hay detrás, **refracta** el contenido en los bordes (efecto lente), tiene **borde iluminado** con especular, **brillo interior**, y **sombra** que lo separa del fondo. No existe API nativa de Windows equivalente [C].

## 2. Ingredientes y cómo se logran hoy
| Ingrediente | Técnica documentada | Estado en el Dock | Coste |
|---|---|---|---|
| Blur + saturación del fondo | Efectos de Direct2D `GaussianBlur` y `Saturation` (existen como efectos integrados [V, vía documentación de envoltorios .NET; verificar en learn.microsoft.com]) | **Implementado** [B]: `GlassBackdrop` (nivel *WallpaperBlur*) | Se calcula **una vez** y se cachea; por frame solo se recorta con la forma de la barra |
| Forma arbitraria de la barra | Relleno con `BitmapBrush` sobre `RoundedRectangle` | **Implementado** [B] | ~1 llamada de dibujo |
| Tinte lechoso + brillo superior | Gradientes `LinearGradientBrush` | **Implementado** [B] | ~3 llamadas |
| Borde especular doble (exterior + filo interior) | Trazos con gradiente vertical | **Implementado** [B] | 2 llamadas |
| Sombra suave | 14 capas concéntricas (gaussiana aproximada) | **Implementado** [B] | 14 rellenos pequeños |
| **Refracción aproximada** | Efecto D2D `DisplacementMap` con un mapa de desplazamiento procedural (normales del borde de la barra) aplicado al fondo desenfocado | **No implementado.** Diseño en §4 | Una pasada extra por reconstrucción del fondo (no por frame si el fondo es estático) [H] |
| Fondo **vivo** (ventanas moviéndose detrás) | `Windows.Graphics.Capture` | **No implementado a propósito** (§3) | Alto y continuo |
| Vidrio del propio sistema | Acrílico DWM (`DWMWA_SYSTEMBACKDROP_TYPE`) | Solo en prototipos S2/S3 | Gratis, pero cubre **toda la ventana** y sin forma propia [V] |

## 3. Alternativas evaluadas para el fondo (la decisión de coste)
| Opción | ¿Documentada? | Fidelidad | Coste/riesgo | Veredicto provisional |
|---|---|---|---|---|
| **A. Sin fondo** (nivel `Tint`) | Sí | Baja-media | Mínimo; cualquier GPU | Plan de reserva siempre disponible |
| **B. Fondo de pantalla desenfocado** (nivel `WallpaperBlur`) | Sí: `SPI_GETDESKWALLPAPER` + estilo en `Control Panel\Desktop` + WIC | Media-alta cuando el dock está sobre el escritorio | Una sola vez por cambio de fondo/monitor/DPI; memoria ≈ tamaño de ventana | **Elegido para la iteración 1** |
| **C. Acrílico DWM de ventana** | Sí [V] | Media | Gratis, sin control de forma | Solo para prototipos |
| **D. Host backdrop brush** (`DWMWA_USE_HOSTBACKDROPBRUSH`, Win11+) | Sí, con limitaciones (airspace, DPI, Win2D no soportado en escritorio) y reportes de fallos con efectos propios [V parcial] | Media | Riesgo de funcionamiento | **Spike futuro**, no base |
| **E. Captura en vivo** (`Windows.Graphics.Capture`) | Sí | **Alta** (fondo real con ventanas) | Ver análisis abajo | **No implementar aún**; decidir con mediciones |
| F. Parámetros no documentados (`SetWindowCompositionAttribute`/accent) | No | Media-alta | Puede romperse con cualquier actualización | **Descartado** |

### Análisis de coste de la captura en vivo (E) — por qué no se implementa todavía
- **Qué entrega la API:** superficies GPU del monitor/ventana completa [C]. Aunque solo interese una franja, la captura es de pantalla entera: coste de ancho de banda de memoria proporcional al monitor (p. ej. un cuadro 4K BGRA ≈ 33 MB; a 60 fps serían ≈ 2 GB/s solo para recibirlo) [H: cifra aritmética, sin medir].
- **Mitigaciones posibles** [H]: capturar solo cuando algo cambia bajo el dock (rectángulos de ventanas vía el `WindowTracker`), limitar a 15–30 fps, copiar solo la región (`CopySubresourceRegion`), y volver al nivel B cuando no haya ventanas debajo.
- **Borde amarillo de captura:** existe en Windows 11; `GraphicsCaptureSession.IsBorderRequired` existe desde Windows 10 2104 (build 20348) [V], pero **no pude confirmar** que una app **sin empaquetar** pueda desactivarlo sin permisos/capacidades especiales → **riesgo de fidelidad y de experiencia** [V parcial].
- **Autocaptura:** el dock se vería a sí mismo; `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` (Win10 2004+, requiere DWM, solo ventana propia, no es DRM) permitiría excluirlo [V].
- **Contenido protegido (DRM) y juegos:** pueden salir en negro [C]; el dock ya se aparta con pantalla completa.
- **Energía y GPU integradas:** coste continuo; debe ser opcional [H].
- **Implicaciones de privacidad/seguridad:** capturar la pantalla es sensible; la API muestra indicadores y puede requerir consentimiento [V parcial]. Cualquier captura debe ser **opt-in, local y sin almacenamiento**.
> Conclusión: **no se construye el sistema de captura continua** hasta (1) medir el nivel B en hardware real, (2) un spike que confirme el borde de captura en apps sin empaquetar, y (3) tu aprobación.

## 4. Diseño propuesto para la refracción aproximada (no implementado)
1. Generar un **mapa de desplazamiento** `RG` del tamaño de la barra: en una banda de ~14 DIP junto al borde, el vector apunta hacia el interior de forma proporcional a la distancia al borde (campo de normales de un rectángulo redondeado); cero en el centro.
2. Aplicar `DisplacementMap(escala≈10–18 px)` al fondo **ya desenfocado** y antes del tinte; el borde parece «curvar» el fondo.
3. Reconstruir solo cuando cambia el ancho de la barra por **más de N px** o el fondo; con magnificación, la barra cambia de ancho cada frame → usar el mapa de la barra en reposo estirado en 3 trozos (extremos fijos + centro estirable) [H].
4. Coste esperado: una pasada de efecto por reconstrucción [H]. Si no cabe en el presupuesto en GPUs integradas, queda como opción «Alta calidad».
5. Criterio de aceptación: mejora visible en la rúbrica (atributos 2, 3, 4) sin superar los umbrales de CPU/GPU.

## 5. Compatibilidad con GPU
| Caso | Comportamiento |
|---|---|
| GPU con Direct3D 11 | Ruta normal [B] |
| Sin GPU compatible / runner de CI | **WARP** (renderizado por software de D3D) automáticamente [B]; más lento, válido para humo y capturas |
| Efectos de D2D no disponibles o error | `BuildBackdrop` captura la excepción y degrada a vidrio sin fondo; el nivel `Tint` no usa efectos [B] |
| GPUs integradas antiguas | **Sin evidencia**; pendiente de pruebas reales |

## 6. Rendimiento por frame (diseño, sin medir)
Con el fondo cacheado, cada frame hace: 1 relleno con brush de bitmap, ~3 gradientes, 2 trazos, 14 rellenos de sombra, y por ícono: 1 elipse de sombra, 1 geometría con gradiente, 1 bitmap, 1 trazo (+ punto). Para 14 ítems ≈ 150 llamadas de dibujo 2D [H: recuento del código]. Objetivo: cumplir los umbrales de docs/03 en GPU integrada. **No hay medición todavía.**

## Fuentes
- SetWindowDisplayAffinity / WDA_EXCLUDEFROMCAPTURE: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- GraphicsCaptureSession.IsBorderRequired: https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.graphicscapturesession.isborderrequired
- Efectos integrados de Direct2D (listado vía documentación de un envoltorio .NET; verificar en Microsoft Learn): https://developer.mescius.com/componentone/docs/wpf/online-bitmap/C1.WPF.DX.4.6.2~C1.Util.DX.Direct2D.Effects_namespace.html
- Host backdrop brush y Visual Layer con Win32: https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/README.md · https://learn.microsoft.com/en-us/answers/a/152907
- Backdrops del sistema: https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
