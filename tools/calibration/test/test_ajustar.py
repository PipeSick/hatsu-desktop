"""Pruebas de ajustar.py con datos SINTÉTICOS de parámetros conocidos (no son mediciones de macOS)."""
import json, math, os, subprocess, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, os.path.dirname(HERE))
import ajustar as A

def w(d, name, obj):
    p = os.path.join(d, name); json.dump(obj, open(p, "w")); return p

def test_all():
    d = tempfile.mkdtemp(); fails = 0
    def ok(name, got, want, tol):
        nonlocal fails; good = abs(got - want) <= tol; fails += not good; print("OK  " if good else "FAIL", name, f"obtenido {got:.3f} esperado {want} ±{tol}")
    # magnificación: tamaño 60, hueco 6, MaxScale 1.7, R = 150
    size, gap, smax, R = 60, 6, 1.7, 150; pitch = size + gap
    hov = {"hover_icon_size_center": {"value": size * smax}}
    for k in (1, 2, 3):
        for t in "pm": hov[f"hover_neighbor_{t}{k}"] = {"value": size * A.bump(k * pitch, smax, R)}
    r = A.fit_magnification({"measurements": {"icon_size_rest": {"value": size}, "icon_gap_rest": {"value": gap}}}, {"measurements": hov})
    ok("MaxScale", r["MaxScale (OBSERVABLE = hover/reposo)"], 1.7, 0.001); ok("Radius", r["Radius (ESTIMADO, ajuste coseno)"], 150, 1.0)
    # desplazamiento: reposo equiespaciado, hover con vecinos empujados simétricamente
    rest = [100 + 66 * i for i in range(5)]; hover = [rest[0] - 20, rest[1] - 12, rest[2], rest[3] + 12, rest[4] + 20]
    dsp = A.displacement({"measurements": {"icon_centers": {"value": rest}}}, {"measurements": {"icon_centers": {"value": hover}}})
    vals = list(dsp.values())[0]; ok("desplazamiento extremo", vals[0], -20, 0.01); ok("desplazamiento centro", vals[2], 0, 0.01)
    # material: out = k·(s·I+(1−s)·L)·bg + b con k=0.8, s=1.5, tinte (240,240,250)·0.2
    k, s, L = 0.8, 1.5, [0.2126, 0.7152, 0.0722]; tint = [240, 240, 250]
    def glass(bg): return [min(255, max(0, k * sum((s * (i == j) + (1 - s) * L[j]) * bg[j] for j in range(3)) + 0.2 * tint[i])) for i in range(3)]
    bgs = [[255, 255, 255], [0, 0, 0], [128, 128, 128], [255, 0, 0], [0, 255, 0], [0, 0, 255]]
    files = [w(d, f"m{i}.json", {"measurements": {"glass_sample": {"value": [bg, [round(c) for c in glass(bg)]]}}}) for i, bg in enumerate(bgs)]
    m = A.fit_material(files); ok("saturación s", m["saturación s (ESTIMADA)"], 1.5, 0.15); ok("alfa", m["opacidad del tinte α = 1−k (ESTIMADA)"], 0.2, 0.04); ok("RMS material", m["error RMS (0-255)"], 0.0, 3.0)
    # sombra: A=0.3, oy=6, σ=8 sobre fondo blanco, 60 pt a 2 muestras/pt
    n = 120; prof = [255 * (1 - 0.3 * (1 - A.phi(((i + 0.5) * 0.5 - 6) / 8))) for i in range(n)]
    sh = A.fit_shadow({"measurements": {"shadow_profile_below": {"value": prof, "length_pt": 60}}})
    ok("sombra A", sh["opacidad máxima A (ESTIMADA)"], 0.3, 0.04); ok("sombra offset", sh["desfase vertical (pt, ESTIMADO)"], 6, 2); ok("sombra sigma", sh["desenfoque σ (pt, ESTIMADO)"], 8, 2.5)
    # borde: línea de 1 pt (2 px) más clara en el centro de un perfil de 12 pt (24 muestras), fondo 100
    prof = [100.0] * 24; prof[12] = prof[13] = 220.0
    b = A.fit_border({"measurements": {"border_profile_top": {"value": prof, "length_pt": 12}}}, "border_profile_top")
    ok("borde ancho a media altura (pt)", list(b.values())[2], 1.0, 0.01)
    # CLI
    out = subprocess.run([sys.executable, os.path.join(os.path.dirname(HERE), "ajustar.py"), "--help"], capture_output=True, text=True); fails += out.returncode != 0
    print("OK  CLI --help" if out.returncode == 0 else "FAIL CLI")
    print("\nTODO OK" if not fails else f"\n{fails} FALLOS"); return fails

if __name__ == "__main__": sys.exit(1 if test_all() else 0)
