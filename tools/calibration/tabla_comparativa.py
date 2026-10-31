#!/usr/bin/env python3
"""Tabla comparativa macOS ↔ WinMac a partir de (1) JSON de medidas exportado por calibracion.html, (2) repeticiones, (3) valores ACTUALES del código (valores_codigo.py).
Las medidas se expresan en píxeles de la captura (escala desconocida) y como razón respecto al ancho del ícono, que NO depende de la escala.
Uso: tabla_comparativa.py medidas.json repeticiones.json"""
import json, sys, os, statistics as st
sys.path.insert(0, os.path.dirname(__file__)); import valores_codigo as vc
M, G = vc.M, vc.G
j = json.load(open(sys.argv[1])); rep = json.load(open(sys.argv[2]))
v = lambda k: j['measurements'][k]['value']
icon = v('icon_size_rest'); bar = v('bar_height'); b60 = M['BaseSize']
def mm(a): return f"{min(a):g}–{max(a):g} (n={len(a)})" if len(set(a)) > 1 else f"{a[0]:g} (n={len(a)})"
r1, r2 = rep['ref1'], rep['ref2']
rows = []
def add(name, mac, macnote, code, cat, ratio_base='icon', note=''):
    """mac y code en las mismas unidades de longitud (px de captura / DIP)"""
    base_mac = icon if ratio_base == 'icon' else bar; base_code = b60 if ratio_base == 'icon' else M['BarHeight']
    rm = mac / base_mac if mac is not None else None; rc = code / base_code if code is not None else None
    eq = rm * (b60 if ratio_base == 'icon' else M['BarHeight']) if rm is not None else None
    rows.append((name, cat, mac, macnote, rm, code, rc, eq, ratio_base, note))
add('Ancho visible del ícono', icon, mm(r1['w']), M['BaseSize'], 'obs')
add('Alto visible del ícono', v('icon_size_rest_h'), mm(r1['h']), M['BaseSize'], 'obs', note='≈ +2 % vs ancho (sombra/antialias); se toma el ancho como tamaño')
add('Separación entre íconos', v('icon_gap_rest'), mm(r1['gaps']), M['Gap'], 'obs')
add('Altura de la barra', bar, '144–145 (convención de borde)', M['BarHeight'], 'obs')
add('Margen interno izquierdo', v('bar_pad_left'), '±1,5 (borde del ícono Finder)', M['PadX'], 'obs')
add('Margen interno superior', v('bar_pad_top'), '±1', M['PadTop'], 'obs')
add('Margen interno inferior', v('bar_pad_bottom'), '±1', M['PadBottom'], 'obs')
add('Radio de esquina (ajuste de círculo)', v('bar_corner_radius'), f"RMS del ajuste {j['measurements']['bar_corner_radius']['rms']:.2f} px", M['BarRadius'], 'est', note='la razón respecto a la barra es la relevante: Mac ≈ %.3f, código ≈ %.3f' % (v('bar_corner_radius') / bar, M['BarRadius'] / M['BarHeight']))
add('Indicador: diámetro', v('indicator_diameter'), '6–8 (umbral sobre antialias)', M['DotSize'], 'obs')
add('Indicador: centro bajo el borde inferior del ícono', v('indicator_below_icon'), mm([round(c - 165, 1) for c in r1['dot_cy']]), M['dot_center_below_icon'], 'obs')
add('Indicador: centro sobre el borde inferior de la barra', v('indicator_above_bar_bottom'), '±1', M['dot_center_above_bar_bottom'], 'obs')
add('Separador: espacio último ícono → línea', v('separator_gap_left'), '40–44 (2 separadores)', M['sep_gap_left_visible'], 'obs')
add('Separador: espacio línea → siguiente ícono', v('separator_gap_right'), '±1', M['sep_gap_right_visible'], 'obs')
add('Separador: altura de la línea', v('separator_height'), '≈ %.0f %% de la barra' % (100 * v('separator_height') / bar), M['sep_height'], 'obs', ratio_base='bar')
add('Separador: grosor', 2.0, '2 columnas (perfil medio)', 1.0, 'obs')
out = ['| Parámetro | Cat. | macOS (px de captura) | Variación observada | macOS ÷ ícono | WinMac (código, DIP) | Código ÷ ícono | macOS equivalente con BaseSize=60 | Nota |', '|---|---|---|---|---|---|---|---|---|']
cat = {'obs': 'observable', 'est': 'estimable'}
for name, c, mac, macnote, rm, code, rc, eq, base, note in rows:
    unit = '÷ barra' if base == 'bar' else '÷ ícono'
    out.append(f"| {name} | {cat[c]} | {mac:g} | {macnote} | {rm:.3f} ({unit}) | {code:g} | {rc:.3f} | {eq:.1f}{' (con barra=%.0f)' % M['BarHeight'] if base=='bar' else ''} | {note} |")
print('\n'.join(out))
print()
print(f"consistencia ref2 (otra escala): ancho {st.mean(r2['w']):.1f}, hueco {st.mean(r2['gaps']):.1f} → hueco/ícono {st.mean(r2['gaps'])/st.mean(r2['w']):.3f} (ref1 {st.mean(r1['gaps'])/st.mean(r1['w']):.3f}); barra/ícono {(r2['barBottom']-r2['barTop'])/st.mean(r2['w']):.3f} (ref1 {bar/st.mean(r1['w']):.3f}); margen sup/ícono {(r2['iconTop']-r2['barTop'])/st.mean(r2['w']):.3f} (ref1 {v('bar_pad_top')/st.mean(r1['w']):.3f})")
