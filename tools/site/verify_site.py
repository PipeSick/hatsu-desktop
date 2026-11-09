#!/usr/bin/env python3
"""Verifica ANTES de desplegar que el sitio generado contiene exactamente la política aprobada. Sin red. Uso: verify_site.py <sitio> [--repo <raíz>]. Sale con 1 si algo falla."""
import hashlib, os, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
fails = []

def check(ok, what, detail=""):
    print(("OK    " if ok else "FALLO ") + what + (f"  {detail}" if detail else ""))
    if not ok: fails.append(what)

def main(site, repo):
    site = pathlib.Path(site); repo = pathlib.Path(repo)
    # 1) los archivos fuente son EXACTAMENTE los aprobados (manifiesto de hashes versionado)
    manifest = repo / "docs/privacy/APPROVED.sha256"
    check(manifest.is_file(), "existe el manifiesto de archivos aprobados", str(manifest))
    if manifest.is_file():
        for line in manifest.read_text(encoding="utf-8").splitlines():
            if not line.strip() or line.startswith("#"): continue
            digest, rel = line.split(None, 1); rel = rel.strip()
            f = repo / rel
            actual = hashlib.sha256(f.read_bytes()).hexdigest() if f.is_file() else "(falta)"
            check(actual == digest, f"hash aprobado: {rel}", actual[:16] if actual == digest else f"esperado {digest[:16]}… obtenido {actual[:16]}…")
    # 2) rutas publicadas
    pages = {"es": site / "privacy/index.html", "en": site / "privacy/en/index.html"}
    for k, p in pages.items(): check(p.is_file() and p.stat().st_size > 4000, f"existe y no está vacía: {p.relative_to(site)}", f"{p.stat().st_size if p.is_file() else 0} B")
    check((site / "index.html").is_file(), "existe index.html raíz"); check((site / ".nojekyll").is_file(), "existe .nojekyll")
    if not all(p.is_file() for p in pages.values()):
        print("Faltan páginas: no se sigue."); sys.exit(1)
    html = {k: p.read_text(encoding="utf-8") for k, p in pages.items()}
    # 3) contenido completo: cada título de sección del Markdown fuente aparece, y las frases/destinos obligatorios
    src = {"es": (repo / "docs/privacy/POLITICA-DE-PRIVACIDAD.md").read_text(encoding="utf-8"), "en": (repo / "docs/privacy/PRIVACY-POLICY.en.md").read_text(encoding="utf-8")}
    for k in ("es", "en"):
        heads = re.findall(r"^## (.+)$", src[k], re.M)
        check(len(heads) >= 7, f"[{k}] el Markdown tiene ≥7 secciones", str(len(heads)))
        missing = [h for h in heads if re.sub(r"[*`]", "", h) not in re.sub(r"<[^>]+>", "", html[k])]
        check(not missing, f"[{k}] todas las secciones llegaron al HTML", ", ".join(missing))
        for must in ("api.open-meteo.com", "geocoding-api.open-meteo.com", "speed.cloudflare.com", "music.163.com", "lrclib.net", "Hatsu Notch", "DPAPI"):
            check(must in html[k], f"[{k}] contiene «{must}»")
        # contacto: GitHub Issues del proyecto. Acepta el nombre anterior (win11-macos) y el definitivo (hatsu-desktop): GitHub redirige tras renombrar
        check(re.search(r"github\.com/PipeSick/(?:win11-macos|hatsu-desktop)/issues", html[k]) is not None, f"[{k}] contiene el contacto de GitHub Issues")
        check("<table>" in html[k] and html[k].count("<tr>") >= 10, f"[{k}] las tablas se generaron", f"{html[k].count('<tr>')} filas")
        check(not re.search(r"VERIFICAR|VERIFY|TODO|FIXME|\*\(", html[k]), f"[{k}] sin marcas de borrador")
        check(f'<html lang="{k}">' in html[k], f"[{k}] atributo lang correcto")
        check(not re.search(r'(?:href|src)="/', html[k]), f"[{k}] sin enlaces absolutos a la raíz (romperían bajo /<repositorio>/)")
    check("desactivado por defecto" in html["es"] and "off by default" in html["en"], "agentes de IA: desactivado por defecto en ambas")
    check("definitiv" in html["es"] and "permanent" in html["en"].lower(), "Cleanup: borrado definitivo declarado en ambas")
    # 4) enlaces entre idiomas y de la raíz resuelven a archivos existentes dentro del sitio
    def resolves(base, href):
        t = (base.parent / href); return (t / "index.html").is_file() if href.endswith("/") else t.is_file()
    check(resolves(pages["es"], "en/"), "enlace es → en/ resuelve"); check(resolves(pages["en"], "../"), "enlace en → ../ resuelve")
    check('href="privacy/"' in (site / "index.html").read_text(encoding="utf-8"), "index raíz apunta a privacy/")
    repo_name = os.environ.get("GITHUB_REPOSITORY", "PipeSick/hatsu-desktop").split("/")[1]; owner = os.environ.get("GITHUB_REPOSITORY_OWNER", "PipeSick").lower()
    print(f"\nURLs que resultarán (tras desplegar): https://{owner}.github.io/{repo_name}/privacy/  ·  https://{owner}.github.io/{repo_name}/privacy/en/")
    if fails: print(f"\n{len(fails)} comprobaciones fallaron"); sys.exit(1)
    print("\nTodo correcto.")

if __name__ == "__main__":
    a = sys.argv[1:]; repo = ROOT
    if "--repo" in a: i = a.index("--repo"); repo = pathlib.Path(a[i + 1]); del a[i:i + 2]
    main(a[0], repo)
