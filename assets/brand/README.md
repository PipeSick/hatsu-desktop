# Recursos de marca de Hatsu Desktop

**Estado: icono Flux (definitivo de Fase 2).** Fuente única: el kit «Hatsu Desktop Flux Brand Kit». Todo lo de esta carpeta se genera con un solo script, `source/make_brand.py`; no se edita a mano.

| Archivo | Uso |
|---|---|
| `HatsuDesktop.ico` | `ApplicationIcon` de HatsuDesktop.exe, .Bar, .Dock y .Connect; bandeja; paquete portable. Frames PNG 16–256 px. |
| `hatsu-flux-{16…1024}.png` | Vistas, README, Acerca de. |
| `embedded/hatsu-flux-{32,128}.bgra` | Píxeles premultiplicados incrustados en el Host/Barra/Dock (`BrandIcon`) para dibujar con Direct2D sin archivos externos. |
| `previews/sizes-on-{light,dark}.png` | Comprobación visual de tamaños sobre fondo claro y oscuro. |
| `manifest.json` | sha256 de cada recurso (reproducibilidad). |
| `source/` | Master original del ZIP, referencia aprobada y el generador. |

## Correcciones aplicadas al master (sin rediseñar H, cinta ni cromática)
- Eliminado el halo oscuro alrededor del tile (halo): se recortó por el contorno visible (rayos desde el centro + máscara anti-alias, −2,2 px de franja).
- **Márgenes:** 24 px sobre 1024 uniformes; el tile ya no toca el borde.
- **`tray-32` mal etiquetado** en el kit: se regenera desde el master a su tamaño real.
- **Variante de bandeja** (frames ≤ 24 px): contraste y trazo reforzados para que la «H» siga legible; misma forma y colores.
- Remuestreo en alfa premultiplicado (sin bordes sucios).
- **No es vectorial:** el master es un PNG de 1024 px; por debajo de 1024 se reduce, nunca se amplía.

## Paleta de referencia
`#7C3AED`, `#A78BFA`, `#C4B5FD`, `#EDE9FE`, `#0B0618`. Es referencia de marca: no sustituye los temas de la interfaz.

## Regenerar
```bash
pip install pillow numpy scipy
python3 assets/brand/source/make_brand.py
```
Cambiar el manifest implica actualizar los recursos incrustados; las pruebas `RebrandingTests` lo verifican.
