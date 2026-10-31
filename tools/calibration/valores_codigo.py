#!/usr/bin/env python3
"""Lee los VALORES ACTUALES del código (DockMetrics.cs, Glass.cs) y calcula magnitudes derivadas, para que la tabla comparativa NO se escriba a mano.
No modifica nada. Uso: python3 valores_codigo.py [--json]"""
import re, json, os, sys, math
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def props(path, record):
    txt = open(os.path.join(ROOT, path), encoding='utf-8').read()
    body = txt[txt.index(record):]
    out = {}
    for name, val in re.findall(r'public double (\w+) \{ get; init; \} = ([0-9.]+);', body):
        out[name] = float(val)
    return out
M = props('src/WinMac.Dock.Core/DockMetrics.cs', 'record DockMetrics')
G = props('src/WinMac.Dock.Core/Glass.cs', 'record GlassStyle')
# derivados (iguales a DockMetrics.cs)
M['BarHeight'] = M['BaseSize'] + M['PadTop'] + M['PadBottom']; M['SeparatorWidth'] = M['SeparatorGap'] * 2 + 1
# disposición real del separador en DockLayout.Compute: línea a SeparatorGap tras (último ícono + Gap); siguiente ícono a SeparatorWidth tras ese punto
M['sep_gap_left_visible'] = M['Gap'] + M['SeparatorGap']                       # borde del último ícono → línea
M['sep_gap_right_visible'] = M['SeparatorWidth'] - M['SeparatorGap']           # línea → siguiente ícono (incluye el ancho de 1 de la línea)
M['sep_across'] = M['Gap'] + M['SeparatorWidth']                               # ícono → ícono cruzando el separador
M['sep_height'] = M['BarHeight'] * 0.6                                         # DockPainter.DrawSeparators: 20 %..80 %
M['dot_center_below_icon'] = 6.5                                               # DockPainter.DrawItem: barBottom − PadBottom + 6.5
M['dot_center_above_bar_bottom'] = M['PadBottom'] - 6.5
# material en el pixel del interior superior (y≈0.10 de la barra): composición de tinte, brillo superior; el realce de saturación (Saturation) se aplica ANTES al fondo
y = 0.10; a_t = (G['TintAlpha'] + G['Brightness']) * (1 - y) + (G['TintAlpha'] * 0.8) * y
a_s = G['TopSheenAlpha'] * max(0.0, 1 - y / 0.55)
a_eff = 1 - (1 - a_t) * (1 - a_s)
G['k_efectivo_y10'] = 1 - a_eff; G['c_aditivo_y10 (blanco 255·α)'] = 255 * a_eff
# sombra: oscurecimiento acumulado JUSTO fuera del borde lateral (todas las capas lo cubren; desplazamiento solo vertical)
n = 14; prod = 1.0
for i in range(1, n + 1):
    t = i / n; a = G['ShadowAlpha'] * (1 - t) ** 2 * 0.5 + 0.004; prod *= (1 - a)
G['sombra_oscurecimiento_borde_lateral'] = 1 - prod; G['sombra_alcance_px'] = G['ShadowBlur'] * 1.6
if __name__ == '__main__':
    if '--json' in sys.argv: print(json.dumps({'DockMetrics': M, 'GlassStyle(claro)': G}, indent=1, ensure_ascii=False))
    else:
        for k, v in M.items(): print(f'DockMetrics.{k:28s} {v:g}')
        for k, v in G.items(): print(f'GlassStyle.{k:34s} {v:.4g}')
