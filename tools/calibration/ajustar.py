#!/usr/bin/env python3
"""Ajustes numéricos sobre los JSON exportados por calibracion.html. Solo biblioteca estándar (sin instalar nada).

  ajustar.py magnificacion reposo.json hover.json       MaxScale y alcance (Radius) de la curva
  ajustar.py desplazamiento reposo.json hover.json      desplazamiento de los íconos vecinos (centros X)
  ajustar.py material f1.json f2.json ...               tinte/opacidad/saturación del vidrio (≥4 fondos distintos; ideal 6)
  ajustar.py sombra perfil.json                         sombra bajo la barra (desfase, desenfoque, opacidad)
  ajustar.py borde perfil.json                          grosor e intensidad de un borde (perfil corto)

Cada resultado indica si es OBSERVABLE (medido) o ESTIMADO (ajuste de un modelo con supuestos): no son la verdad, son una estimación con su error.
No modifica el renderizador ni ningún archivo del repositorio.
"""
import json, math, sys

def load(p):
    with open(p, encoding="utf-8") as f: return json.load(f)

def val(j, key):
    m = j["measurements"].get(key)
    return None if m is None else m["value"]

# ───────────────────────────── magnificación
def bump(d, smax, radius):
    d = abs(d)
    return 1.0 if d >= radius else 1 + (smax - 1) * 0.5 * (1 + math.cos(math.pi * d / radius))

def fit_magnification(rest, hover):
    size = val(rest, "icon_size_rest"); gap = val(rest, "icon_gap_rest"); center = val(hover, "hover_icon_size_center")
    if None in (size, gap, center): raise SystemExit("Faltan icon_size_rest/icon_gap_rest (reposo) o hover_icon_size_center (hover).")
    smax = center / size; pitch = size + gap
    obs = []
    for k in (1, 2, 3):
        for sgn, tag in ((1, "p"), (-1, "m")):
            v = val(hover, f"hover_neighbor_{tag}{k}")
            if v is not None: obs.append((k, sgn, v / size))
    best = None
    for r10 in range(int(pitch * 10), int(pitch * 12 * 10)):   # radio de 1 a 12 pasos de íconos, paso 0,1 pt
        R = r10 / 10
        err = sum((bump(k * pitch, smax, R) - s) ** 2 for k, _, s in obs)
        if best is None or err < best[0]: best = (err, R)
    out = {"MaxScale (OBSERVABLE = hover/reposo)": round(smax, 3), "tamaño reposo (pt)": size, "paso entre íconos (pt)": round(pitch, 2)}
    if obs and best:
        out["Radius (ESTIMADO, ajuste coseno)"] = best[1]
        out["error RMS de escala"] = round(math.sqrt(best[0] / len(obs)), 4)
        out["escalas medidas (k, lado, escala)"] = [(k, s, round(v, 3)) for k, s, v in obs]
        out["aviso"] = "El modelo coseno (1+(Smax-1)·½(1+cos(πd/R))) puede no ser la curva real; si el error RMS es alto, hay que cambiar la forma de la curva, no solo R."
    return out

def displacement(rest, hover):
    a, b = val(rest, "icon_centers"), val(hover, "icon_centers")
    if not a or not b or len(a) != len(b): raise SystemExit("icon_centers debe existir en reposo y hover con el mismo número de íconos.")
    ca, cb = (a[0] + a[-1]) / 2, (b[0] + b[-1]) / 2   # el dock está centrado: se compara respecto al centro de la barra
    return {"desplazamiento de cada ícono respecto a reposo, centrado (pt, OBSERVABLE)": [round((y - cb) - (x - ca), 2) for x, y in zip(a, b)],
            "separación entre centros en reposo (pt)": [round(a[i + 1] - a[i], 2) for i in range(len(a) - 1)],
            "separación entre centros en hover (pt)": [round(b[i + 1] - b[i], 2) for i in range(len(b) - 1)]}

# ───────────────────────────── material (regresión lineal)
def lstsq(X, y):
    n = len(X[0]); A = [[sum(r[i] * r[j] for r in X) for j in range(n)] for i in range(n)]; b = [sum(r[i] * yy for r, yy in zip(X, y)) for i in range(n)]
    for i in range(n):
        p = max(range(i, n), key=lambda r: abs(A[r][i]))
        if abs(A[p][i]) < 1e-9: raise SystemExit("Los fondos son linealmente dependientes: usa blanco, negro, gris y al menos dos colores saturados.")
        A[i], A[p] = A[p], A[i]; b[i], b[p] = b[p], b[i]
        for r in range(n):
            if r != i:
                f = A[r][i] / A[i][i]
                for c in range(i, n): A[r][c] -= f * A[i][c]
                b[r] -= f * b[i]
    return [b[i] / A[i][i] for i in range(n)]

def fit_material(files):
    """Ajuste directo del modelo  out = clamp( k·(s·I + (1−s)·L)·bg + c )  (k = 1−α, c = α·tinte).
    El recorte a 0..255 se incluye en el modelo: con colores saturados la saturación genera valores fuera de rango y una regresión lineal pura se sesga."""
    pairs = []
    for f in files:
        c = val(load(f), "glass_sample")
        if not c or len(c) < 2: raise SystemExit(f"{f}: falta glass_sample con 2 muestras (fondo, vidrio).")
        pairs.append((c[0], c[1]))
    if len(pairs) < 4: raise SystemExit("Se necesitan ≥4 escenas con fondos distintos (ideal: blanco, negro, gris50, rojo, verde, azul).")
    L = [0.2126, 0.7152, 0.0722]; clamp = lambda v: 0.0 if v < 0 else 255.0 if v > 255 else v
    best = None
    for s100 in range(50, 301, 5):
        s = s100 / 100; T = [[s * (i == j) + (1 - s) * L[j] for j in range(3)] for i in range(3)]
        base = [[sum(T[i][j] * bg[j] for j in range(3)) for i in range(3)] for bg, _ in pairs]
        for k100 in range(30, 101):
            k = k100 / 100; total = 0.0; cs = []
            for i in range(3):
                bc = None
                for c in range(0, 256, 3):
                    e = sum((clamp(k * base[n][i] + c) - pairs[n][1][i]) ** 2 for n in range(len(pairs)))
                    if bc is None or e < bc[0]: bc = (e, c)
                total += bc[0]; cs.append(bc[1])
            if best is None or total < best[0]: best = (total, s, k, cs)
    total, s, k, cs = best; alpha = 1 - k; rms = math.sqrt(total / (3 * len(pairs)))
    return {"opacidad del tinte α = 1−k (ESTIMADA)": round(alpha, 3), "color del tinte RGB = c/α (ESTIMADO)": [round(c / alpha, 1) if alpha > 1e-3 else None for c in cs],
            "saturación s (ESTIMADA)": round(s, 2), "término aditivo c RGB (0-255)": cs, "error RMS (0-255)": round(rms, 2),
            "aviso": "Supone un modelo lineal con recorte (tinte + saturación). Ignora el brillo especular, degradados internos y el desenfoque; si el error RMS es >6, el modelo no basta. Con α<0,05 el color del tinte no es fiable."}

# ───────────────────────────── sombra y borde (perfiles)
def phi(x): return 0.5 * (1 + math.erf(x / math.sqrt(2)))

def profile_of(j, key):
    m = j["measurements"].get(key)
    if not m: raise SystemExit(f"No hay {key} en el archivo.")
    return m["value"], m["length_pt"] if "length_pt" in m else None

def fit_shadow(j):
    lum, length = profile_of(j, "shadow_profile_below"); n = len(lum); length = length or n
    ds = [(i + 0.5) * length / n for i in range(n)]; base = max(lum[-3:]) if n > 3 else 255   # fondo blanco lejos de la barra
    dark = [max(0.0, 1 - v / max(base, 1)) for v in lum]
    best = None
    for A in [a / 100 for a in range(2, 65, 2)]:
        for oy in [o / 2 for o in range(0, 49)]:
            for sg in [s / 2 for s in range(2, 61)]:
                err = sum((A * (1 - phi((d - oy) / sg)) - y) ** 2 for d, y in zip(ds, dark))
                if best is None or err < best[0]: best = (err, A, oy, sg)
    err, A, oy, sg = best
    return {"opacidad máxima A (ESTIMADA)": A, "desfase vertical (pt, ESTIMADO)": oy, "desenfoque σ (pt, ESTIMADO)": sg,
            "oscurecimiento medido (OBSERVABLE) en el 1.er punto": round(dark[0], 3), "error RMS": round(math.sqrt(err / n), 4),
            "aviso": "Supone sombra negra, perfil que empieza justo en el borde de la barra y fondo blanco. Sombra de un borde plano (no esquina)."}

def fit_border(j, key):
    lum, length = profile_of(j, key); n = len(lum); length = length or n
    if n < 6: raise SystemExit("Perfil demasiado corto.")
    q = max(1, n // 6); base_out = sum(lum[:q]) / q; base_in = sum(lum[-q:]) / q
    ref = (base_out + base_in) / 2; dev = [v - ref for v in lum]
    i = max(range(n), key=lambda t: abs(dev[t])); peak = dev[i]
    half = [t for t in range(n) if abs(dev[t]) >= abs(peak) / 2]
    return {"posición del pico (pt desde el inicio, OBSERVABLE)": round((i + 0.5) * length / n, 2), "desviación del pico respecto al fondo (0-255, OBSERVABLE)": round(peak, 1),
            "ancho a media altura (pt, OBSERVABLE; límite = 1 píxel)": round((half[-1] - half[0] + 1) * length / n, 2), "luminancia fuera/dentro (0-255)": [round(base_out, 1), round(base_in, 1)],
            "aviso": "Con captura Retina (2 px/pt) el límite de resolución es 0,5 pt. El brillo del borde (alfa) se estima comparando sobre fondo negro y blanco."}

def main(argv):
    if len(argv) < 3 or argv[1] in ("-h", "--help"): print(__doc__); return 0
    cmd, files = argv[1], argv[2:]
    if cmd == "magnificacion": out = fit_magnification(load(files[0]), load(files[1]))
    elif cmd == "desplazamiento": out = displacement(load(files[0]), load(files[1]))
    elif cmd == "material": out = fit_material(files)
    elif cmd == "sombra": out = fit_shadow(load(files[0]))
    elif cmd == "borde": out = fit_border(load(files[0]), "border_profile_top" if len(files) < 2 else files[1])
    else: print(__doc__); return 2
    print(json.dumps(out, indent=2, ensure_ascii=False)); return 0

if __name__ == "__main__": sys.exit(main(sys.argv))
