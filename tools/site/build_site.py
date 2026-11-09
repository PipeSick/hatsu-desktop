#!/usr/bin/env python3
"""Genera el sitio estático de GitHub Pages (solo la política de privacidad) en la carpeta indicada. Sin red. Uso: build_site.py <salida>."""
import html, pathlib, sys
import markdown

ROOT = pathlib.Path(__file__).resolve().parents[2]
PAGES = [("privacy/index.html", "docs/privacy/POLITICA-DE-PRIVACIDAD.md", "es", "Política de privacidad · Hatsu Desktop"),
         ("privacy/en/index.html", "docs/privacy/PRIVACY-POLICY.en.md", "en", "Privacy Policy · Hatsu Desktop")]
CSS = ("body{font:16px/1.6 system-ui,Segoe UI,sans-serif;max-width:46rem;margin:2rem auto;padding:0 1rem;color:#1b1b1f;background:#fff}"
       "table{border-collapse:collapse;width:100%;font-size:.92rem}td,th{border:1px solid #c9c9d1;padding:.4rem .5rem;vertical-align:top;text-align:left}"
       "code{background:#f0f0f4;padding:0 .2rem;border-radius:3px}a{color:#0b57d0}"
       "@media(prefers-color-scheme:dark){body{background:#17171a;color:#e6e6ea}td,th{border-color:#44444c}code{background:#2a2a30}a{color:#8ab4f8}}")

def page(lang, title, body, alt):
    return (f'<!doctype html><html lang="{lang}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
            f'<title>{html.escape(title)}</title><style>{CSS}</style></head><body>{alt}{body}</body></html>')

def main(out):
    out = pathlib.Path(out)
    for rel, src, lang, title in PAGES:
        body = markdown.markdown((ROOT / src).read_text(encoding="utf-8"), extensions=["tables"])
        alt = ('<p><a href="en/">English</a></p>' if lang == "es" else '<p><a href="../">Español</a></p>')
        target = out / rel; target.parent.mkdir(parents=True, exist_ok=True); target.write_text(page(lang, title, body, alt), encoding="utf-8")
    (out / "index.html").write_text('<!doctype html><meta charset="utf-8"><meta http-equiv="refresh" content="0; url=privacy/"><title>Hatsu Desktop</title><a href="privacy/">Política de privacidad / Privacy policy</a>', encoding="utf-8")
    (out / ".nojekyll").write_text("", encoding="utf-8")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "_site")
