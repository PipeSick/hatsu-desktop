#!/usr/bin/env python3
"""
Hatsu Desktop — generador de los recursos de marca Flux (UNA fuente: este script + source/flux-master-1024.original.png).
Salida: assets/brand/*.png, HatsuDesktop.ico, embedded/*.bgra, previews/*.png y manifest.json.

Qué corrige respecto al kit entregado (el diseño aprobado NO se toca: ni la H, ni la cinta, ni los colores):
  1. El master traía un halo oscuro semitransparente FUERA del contorno del icono (restos del fondo negro de la referencia) y el icono tocaba el borde del lienzo
     (margen de 7 px de 1024). Sobre fondos claros se veía como una sombra sucia. Se recorta con una máscara geométrica ajustada al contorno visible
     (contorno visible medido por rayos desde el centro, suavizado), con borde suavizado, y se deja un margen del 2,3 %.
  2. «hatsu-flux-tray-32.png» del kit era una copia de 1024 px del master (no un recurso de bandeja): no se usa.
  3. Los tamaños pequeños se regeneran desde el master con remuestreo en alfa premultiplicado (sin halos) y, para 16-24 px, un refuerzo de contraste/nitidez
     (variante «bandeja»: mismo diseño, más legible; no se redibuja nada).
Requiere: Pillow, numpy, scipy.  Uso: python3 assets/brand/source/make_brand.py
"""
import hashlib, io, json, os, struct, sys
import numpy as np
from PIL import Image, ImageFilter, ImageEnhance

HERE = os.path.dirname(os.path.abspath(__file__)); OUT = os.path.dirname(HERE)
SRC = os.path.join(HERE, 'flux-master-1024.original.png')
MARGIN = 24  # px sobre 1024
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
PNG_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024]

def premul(im):
    a = np.asarray(im.convert('RGBA'), dtype=np.float32) / 255.0
    a[..., :3] *= a[..., 3:4]; return a
def unpremul(a):
    out = a.copy(); al = out[..., 3:4]; out[..., :3] = np.where(al > 1e-4, out[..., :3] / np.maximum(al, 1e-4), 0)
    return Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA')
def resize_premul(im, size):
    a = premul(im); chans = [Image.fromarray(a[..., i]).resize((size, size), Image.LANCZOS) for i in range(4)]
    return unpremul(np.stack([np.asarray(c, dtype=np.float32) for c in chans], -1))

def outline_polygon(arr, cx0=512.0, cy0=512.0, steps=2880, thresh=55):
    """Contorno VISIBLE del icono: por rayos desde el centro, el último punto (de fuera hacia dentro) cuya luminosidad supera el umbral del borde luminoso;
    el halo exterior es casi negro y queda fuera. Suavizado angular para quitar el ruido de la fuente."""
    from scipy.ndimage import map_coordinates, gaussian_filter, median_filter
    lum = arr[..., :3].astype(np.float32).mean(-1); lum = gaussian_filter(lum, 1.2)
    th = np.linspace(0, 2 * np.pi, steps, endpoint=False); rs = np.arange(760, 250, -0.5)
    X = cx0 + np.cos(th)[:, None] * rs[None, :]; Y = cy0 + np.sin(th)[:, None] * rs[None, :]
    prof = map_coordinates(lum, [Y.ravel(), X.ravel()], order=1, mode='nearest').reshape(steps, -1)
    inside = (X >= 0) & (X < 1023) & (Y >= 0) & (Y < 1023)
    hit = (prof > thresh) & inside; first = np.argmax(hit, axis=1)   # primer acierto yendo hacia dentro = último desde el exterior
    r = rs[first].astype(np.float32)
    r = median_filter(r, size=15, mode="wrap"); r = gaussian_filter(r, 4, mode="wrap") - 2.2   # 2,2 px hacia dentro: quita el filete oscuro de transición
    return cx0 + np.cos(th) * r, cy0 + np.sin(th) * r

def aa_polygon(xs, ys, ss=4, size=1024):
    from PIL import ImageDraw
    big = Image.new('L', (size * ss, size * ss), 0); ImageDraw.Draw(big).polygon([(float(x) * ss, float(y) * ss) for x, y in zip(xs, ys)], fill=255)
    return np.asarray(big.resize((size, size), Image.LANCZOS), dtype=np.float32) / 255.0

def build_master():
    im = Image.open(SRC).convert('RGBA'); arr = np.asarray(im)
    xs, ys = outline_polygon(arr); mask = aa_polygon(xs, ys)
    rgb = arr[..., :3].astype(np.float32) / 255.0
    clean = Image.fromarray((np.dstack([rgb, mask]) * 255 + 0.5).astype(np.uint8), 'RGBA')
    x0, y0, x1, y1 = float(xs.min()), float(ys.min()), float(xs.max()), float(ys.max()); bb = (int(np.floor(x0)), int(np.floor(y0)), int(np.ceil(x1)), int(np.ceil(y1)))
    tile = clean.crop(bb); w, h = tile.size; side = 1024 - 2 * MARGIN
    # el contorno es (casi) cuadrado; se escala conservando la proporción y se centra
    k = side / max(w, h); tile = resize_premul_rect(tile, (round(w * k), round(h * k)))
    canvas = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0)); canvas.alpha_composite(tile, ((1024 - tile.width) // 2, (1024 - tile.height) // 2))
    return canvas, dict(bbox=bb, width=w, height=h, aspect=round(w / h, 4), points=len(xs))

def resize_premul_rect(im, size):
    a = premul(im); chans = [Image.fromarray(a[..., i]).resize(size, Image.LANCZOS) for i in range(4)]
    return unpremul(np.stack([np.asarray(c, dtype=np.float32) for c in chans], -1))

def small_variant(master, size):
    im = resize_premul(master, size)
    if size <= 24:   # variante «bandeja»: más contraste y nitidez; mismo dibujo
        rgb = im.convert('RGB'); a = im.getchannel('A')
        rgb = ImageEnhance.Contrast(rgb).enhance(1.18); rgb = ImageEnhance.Color(rgb).enhance(1.08)
        rgb = rgb.filter(ImageFilter.UnsharpMask(radius=0.7, percent=90, threshold=0)); im = Image.merge('RGBA', (*rgb.split(), a))
    elif size <= 48:
        rgb = im.convert('RGB'); a = im.getchannel('A'); rgb = rgb.filter(ImageFilter.UnsharpMask(radius=0.6, percent=45, threshold=1)); im = Image.merge('RGBA', (*rgb.split(), a))
    return im

def write_ico(path, frames):
    blobs = []
    for s in sorted(frames):
        buf = io.BytesIO(); frames[s].save(buf, 'PNG', optimize=True); blobs.append((s, buf.getvalue()))
    head = struct.pack('<HHH', 0, 1, len(blobs)); off = 6 + 16 * len(blobs); entries = b''; data = b''
    for s, b in blobs:
        entries += struct.pack('<BBBBHHII', s if s < 256 else 0, s if s < 256 else 0, 0, 0, 1, 32, len(b), off + len(data)); data += b
    open(path, 'wb').write(head + entries + data)

def sha(p): return hashlib.sha256(open(p, 'rb').read()).hexdigest()

def main():
    master, fit = build_master(); os.makedirs(os.path.join(OUT, 'embedded'), exist_ok=True); os.makedirs(os.path.join(OUT, 'previews'), exist_ok=True)
    frames = {}; files = {}
    for s in PNG_SIZES:
        im = master if s == 1024 else (small_variant(master, s) if s <= 256 else resize_premul(master, s)); p = os.path.join(OUT, f'hatsu-flux-{s}.png'); im.save(p, optimize=True); files[os.path.basename(p)] = p
        if s in SIZES: frames[s] = im
    ico = os.path.join(OUT, 'HatsuDesktop.ico'); write_ico(ico, frames); files['HatsuDesktop.ico'] = ico
    for s in (32, 128):   # recursos incrustados en la barra y en «Acerca de» (BGRA premultiplicado, filas de arriba abajo)
        a = premul(frames[s]); bgra = (np.clip(a[..., [2, 1, 0, 3]], 0, 1) * 255 + 0.5).astype(np.uint8).tobytes(); p = os.path.join(OUT, 'embedded', f'hatsu-flux-{s}.bgra'); open(p, 'wb').write(bgra); files[f'embedded/hatsu-flux-{s}.bgra'] = p
    # vistas previas (no se usan en el producto)
    for name, bg in (('light', (243, 243, 247, 255)), ('dark', (22, 22, 26, 255))):
        sheet = Image.new('RGBA', (len(SIZES) * 150 + 10, 190), bg)
        x = 10
        for s in SIZES:
            up = frames[s].resize((min(128, s * (4 if s <= 32 else 1)),) * 2, Image.NEAREST if s <= 32 else Image.LANCZOS); sheet.alpha_composite(up, (x + (140 - up.width) // 2, 10 + (128 - up.height) // 2)); x += 150
        p = os.path.join(OUT, 'previews', f'sizes-on-{name}.png'); sheet.convert('RGB').save(p); files[f'previews/sizes-on-{name}.png'] = p
    manifest = dict(source=dict(file='source/flux-master-1024.original.png', sha256=sha(SRC)), fit=fit, ico_frames=sorted(frames), files={k: dict(sha256=sha(v), bytes=os.path.getsize(v)) for k, v in sorted(files.items())})
    json.dump(manifest, open(os.path.join(OUT, 'manifest.json'), 'w'), indent=2, sort_keys=True)
    print('ajuste', fit); print('archivos', len(files))

if __name__ == '__main__': main()
