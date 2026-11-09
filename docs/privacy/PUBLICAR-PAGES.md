# Publicar la política de privacidad en GitHub Pages — pasos exactos

**Estado a 2026-11-05:** el flujo `pages-politica-de-privacidad` ya aparece en Actions y se probó **sin desplegar** (run 41959929895: construir y verificar = OK; desplegar y comprobar URLs = omitidos). **Nada está publicado.** Las URLs seguirán dando 404 hasta que ejecutes el paso 3.

## Por qué no aparecía
GitHub solo lista en Actions los flujos con `workflow_dispatch` si el archivo existe en la **rama predeterminada** (`claude/vibrant-keller-7tyi6u`). El flujo estaba solo en `feat/hatsu-phase3-installer-onboarding`. Solución aplicada: **un único archivo** (`.github/workflows/pages.yml`) añadido a la rama predeterminada (commit `eb5389b`), sin fusionar nada. El contenido de las políticas NO sale de esa rama: el flujo hace `checkout` del commit aprobado `de51f4be1ee22086f983fcd87c09537f3ce48d84` (campo `ref`, por defecto).

## Pasos
1. **Comprobar Pages** (una sola vez): *Settings → Pages → Build and deployment → Source: GitHub Actions* (ya lo tienes). Anota si aparece «Enforce HTTPS».
2. **Ensayo sin publicar (opcional, recomendado):** *Actions → pages-politica-de-privacidad → Run workflow* · Branch: `claude/vibrant-keller-7tyi6u` · `ref`: dejar el valor por defecto · `desplegar`: **desmarcado** → *Run workflow*. Al terminar, abre el artefacto `sitio-previsualizacion` para ver el HTML exacto.
3. **Publicar:** el mismo *Run workflow*, esta vez con `desplegar` **marcado**. Si GitHub pide aprobar el entorno `github-pages`, aprueba.
4. **Comprobar:** el job «Comprobar las URLs publicadas» reintenta hasta 3 minutos y falla si alguna URL no responde 200 con el texto esperado. Después abre tú mismo, sin sesión iniciada:
   - https://pipesick.github.io/hatsu-desktop/privacy/
   - https://pipesick.github.io/hatsu-desktop/privacy/en/
5. Pega la primera URL en Partner Center → Propiedades → Política de privacidad.

## Qué verifica el flujo antes de desplegar (falla y NO despliega)
- `ref` es un SHA de 40 caracteres.
- Las dos políticas fuente coinciden con `docs/privacy/APPROVED.sha256` (si editas una política, hay que volver a aprobarla y regenerar el manifiesto).
- Existen `privacy/index.html`, `privacy/en/index.html`, `index.html` y `.nojekyll`; ninguna vacía.
- Cada sección del Markdown llegó al HTML; están los destinos de red, «Hatsu Notch», DPAPI, el contacto de Issues, el borrado definitivo de Cleanup y «agentes desactivados por defecto» en ambos idiomas; las tablas se generaron; no hay marcas de borrador; `lang` correcto; sin enlaces absolutos a `/` (romperían bajo `/win11-macos/`); los enlaces es↔en resuelven.

## Restricciones del entorno `github-pages`
La regla de ramas del entorno suele permitir solo la rama predeterminada. El flujo se ejecuta **desde** la rama predeterminada (el contenido lo toma del commit pinneado), así que no debería bloquearse. Si aun así el despliegue se rechaza por la regla de ramas: *Settings → Environments → github-pages → Deployment branches and tags* y permite `claude/vibrant-keller-7tyi6u`.

## Si cambias la política más adelante
Edita en la rama de desarrollo → vuelve a revisar → regenera `docs/privacy/APPROVED.sha256` → copia el SHA del nuevo commit en el campo `ref` al ejecutar (o cámbialo como valor por defecto en `pages.yml`, en AMBAS ramas).
