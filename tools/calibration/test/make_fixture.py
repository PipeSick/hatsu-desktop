"""Genera una captura SINTÉTICA con geometría conocida (para probar la herramienta de medición, no es una referencia de nada).
Escala 2 px/pt. Barra 82 pt de alto, 10 pt de margen lateral, 3 íconos de 60 pt separados 6 pt, radio 26 pt."""
from PIL import Image, ImageDraw
import json, math, sys
S = 2
W, H = 800 * S // 2 * 2, 300  # 800 px x 300 px
img = Image.new("RGB", (W, H), (255, 255, 255))
d = ImageDraw.Draw(img)
barL, barT, barW, barH = 100, 100, (10 + 60 + 6 + 60 + 6 + 60 + 10) * S, 82 * S   # px
d.rounded_rectangle([barL, barT, barL + barW - 1, barT + barH - 1], radius=26 * S, fill=(120, 120, 160))
truth = {"bar_height_pt": 82, "bar_width_pt": barW / S, "icon_pt": 60, "gap_pt": 6, "pad_x_pt": 10, "radius_pt": 26, "ppp": S,
         "barL": barL, "barT": barT, "barW": barW, "barH": barH}
x = barL + 10 * S; icons = []
for i in range(3):
    top = barT + 8 * S
    d.rectangle([x, top, x + 60 * S - 1, top + 60 * S - 1], fill=(250, 200, 60))
    icons.append([x, top, x + 60 * S, top + 60 * S]); x += (60 + 6) * S
truth["icons"] = icons
# línea de degradado para probar perfiles: franja negra→blanca de 40 px a la derecha
for i in range(40):
    v = int(255 * i / 39); d.line([(barL + barW + 20 + i, 100), (barL + barW + 20 + i, 200)], fill=(v, v, v))
truth["ramp"] = [barL + barW + 20, 100, barL + barW + 60, 100]
img.save(sys.argv[1]); json.dump(truth, open(sys.argv[1] + ".json", "w"))
print("fixture", img.size)
