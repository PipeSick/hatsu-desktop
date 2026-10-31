#!/usr/bin/env python3
"""Tabla macOS ↔ WinMac ANTES (revisión git indicada, por defecto la anterior a la calibración) ↔ DESPUÉS (código actual, leído con valores_codigo.cs).
Uso: tabla_antes_despues.py medidas.json [rev_antes=c05aa0f]"""
import json, subprocess, sys, os, importlib
here = os.path.dirname(os.path.abspath(__file__)); root = os.path.abspath(os.path.join(here, '..', '..'))
rev = sys.argv[2] if len(sys.argv) > 2 else 'c05aa0f'
sys.argv = [sys.argv[0], sys.argv[1], '--rev', rev]       # valores_codigo.py lee --rev de sys.argv
sys.path.insert(0, here); vc = importlib.import_module('valores_codigo'); A, GA = vc.M, vc.G
raw = subprocess.run(['dotnet', 'run', os.path.join(here, 'valores_codigo.cs')], cwd=root, capture_output=True, text=True, check=True).stdout.strip().splitlines()[-1]
D = json.loads(raw); DM, GL, GD = D['metrics'], D['glass_light'], D['glass_dark']
mac = json.load(open(sys.argv[1]))['measurements']; v = lambda k: mac[k]['value']; icon = v('icon_size_rest'); bar = v('bar_height')
def row(name, macpx, before, after, bb, ba, base='icon'):
    mr = macpx / (icon if base == 'icon' else bar); br = before / bb; ar = after / ba
    return f"| {name} | {mr:.3f} | {br:.3f} | {ar:.3f} | {abs(br - mr) / mr * 100:.0f} % → {abs(ar - mr) / mr * 100:.0f} % |"
print('| Parámetro (÷ ícono salvo indicación) | macOS (medido) | WinMac antes | WinMac después | Desvío vs macOS |'); print('|---|---|---|---|---|')
b0, a0 = A['BaseSize'], DM['BaseSize']
print(row('Separación entre íconos', v('icon_gap_rest'), A['Gap'], DM['Gap'], b0, a0))
print(row('Margen lateral', v('bar_pad_left'), A['PadX'], DM['PadX'], b0, a0))
print(row('Margen superior', v('bar_pad_top'), A['PadTop'], DM['PadTop'], b0, a0))
print(row('Margen inferior', v('bar_pad_bottom'), A['PadBottom'], DM['PadBottom'], b0, a0))
print(row('Altura de la barra', bar, A['BarHeight'], DM['BarHeight'], b0, a0))
print(row('Radio de esquina (÷ barra)', v('bar_corner_radius'), A['BarRadius'], DM['BarRadius'], A['BarHeight'], DM['BarHeight'], 'bar'))
print(row('Indicador: diámetro', v('indicator_diameter'), A['DotSize'], DM['DotSize'], b0, a0))
print(row('Indicador: centro bajo el ícono', v('indicator_below_icon'), A['dot_center_below_icon'], DM['DotCenterBelowIcon'], b0, a0))
print(row('Separador: espacio a la izquierda', v('separator_gap_left'), A['sep_gap_left_visible'], DM['SeparatorLeft'], b0, a0))
print(row('Separador: espacio a la derecha', v('separator_gap_right'), A['sep_gap_right_visible'], DM['SeparatorRight'], b0, a0))
print(row('Separador: altura (÷ barra)', v('separator_height'), A['sep_height'], DM['SeparatorHeight'], A['BarHeight'], DM['BarHeight'], 'bar'))
print(row('Separador: grosor', 2.0, 1.0, DM['SeparatorLine'], b0, a0))
print(f"| Ícono ÷ altura de la barra | {icon / bar:.3f} | {A['BaseSize'] / A['BarHeight']:.3f} | {DM['BaseSize'] / DM['BarHeight']:.3f} | {abs(A['BaseSize'] / A['BarHeight'] - icon / bar) / (icon / bar) * 100:.0f} % → {abs(DM['BaseSize'] / DM['BarHeight'] - icon / bar) / (icon / bar) * 100:.0f} % |")
print()
def kc(g, y=0.10):
    top = g['TintAlpha'] + g['Brightness']; bot = g['TintAlpha'] * g.get('TintBottomFactor', 0.8); a_t = top * (1 - y) + bot * y
    f = g.get('SheenFraction', 0.55); a_s = g['TopSheenAlpha'] * max(0.0, 1 - y / f); a = 1 - (1 - a_t) * (1 - a_s); lum = 255 * (0.2126 * g['TintR'] + 0.7152 * g['TintG'] + 0.0722 * g['TintB'])
    return 1 - a, a * lum
kb, cb = A_k = (GA['k_efectivo_y10'], GA['c_aditivo_y10 (blanco 255·α)'])
print('| Material / bordes / sombra | macOS (estimado) | Antes | Después claro | Después oscuro |'); print('|---|---|---|---|---|')
kl, cl = kc(GL); kd, cd = kc(GD)
print(f"| Ganancia k (interior÷exterior) | 0,59–0,62 | {kb:.2f} | {kl:.2f} | {kd:.2f} |")
print(f"| Término aditivo c (0–255) | ≈ 31 | {cb:.0f} | {cl:.0f} | {cd:.0f} |")
print(f"| Realce de saturación | ≈ 1,0 (inconcluso) | {GA['Saturation']:g} | {GL['Saturation']:g} | {GD['Saturation']:g} |")
print(f"| Filete superior (α blanco) | 0,26–0,36 | {GA['RimTopAlpha']:g} | {GL['RimTopAlpha']:g} | {GD['RimTopAlpha']:g} |")
print(f"| Contorno oscuro en los costados (α negro) | ≈ 0,1–0,25 (equiv.) | 0 | {GL['SideContourAlpha']:g} | {GD['SideContourAlpha']:g} |")
print(f"| Sombra: opacidad junto al borde lateral | ≤ 0,02–0,06 (cota) | {GA['sombra_oscurecimiento_borde_lateral']:.2f} | {D['shadow_edge_light']:.3f} | {D['shadow_edge_dark']:.3f} |")
