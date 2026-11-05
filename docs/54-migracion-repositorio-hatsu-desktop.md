# 54 · Migración del repositorio a `PipeSick/hatsu-desktop` — informe previo

**Estado:** preparado en la rama `feat/hatsu-repo-rename-prep` (sin fusionar). **El repositorio NO se ha renombrado.** Este informe es previo a tu autorización. Nada se envió a la Store, no hay releases, Smart App Control intacto, identidad MSIX sin cambios (`PipeSick.HatsuDesktop`, PFN `…_83tr6qzbbarj8`).

## 1. Referencias antiguas encontradas
| Qué | Dónde | Cantidad |
|---|---|---|
| `win11-macos` (nombre del repo) | `ci-report/` (38 registros históricos), 9 docs históricos (`docs/06, 07, 16, 38, 40, 41, CLAUDE_HANDOFF, ESTADO-SESION`…) | ~640 menciones |
| `win11-macos` **vivas** | `.github/workflows/pages.yml` (URLs de comprobación), `installer/Package.wxs` (`ARPURLINFOABOUT`), `tools/manual/Visual-Audit.ps1` (texto de prueba), `tools/site/verify_site.py`, `docs/53`, `docs/privacy/PUBLICAR-PAGES.md` | 7 sitios |
| `win11-macos` en las **políticas aprobadas** | `POLITICA-DE-PRIVACIDAD.md` y `PRIVACY-POLICY.en.md`: 2 menciones cada una (contacto de Issues y enlace al código) | 4 |
| **Repositorio externo** | `PipeSick/notch-win` → `.gitmodules`: submódulo `external/winmac` apunta a `https://github.com/PipeSick/win11-macos` (fijado a `f947602`) | 1 |
| `WinMac` | 1318 menciones en 336 `.cs` (espacios de nombres, proyectos, ensamblados), 254 en `.ps1`, 61 en `.csproj`, docs históricos | — |
| Textos visibles con «WinMac» | Ninguno en la interfaz (solo README: nota histórica; NOTICE/THIRD_PARTY: atribución histórica) | 7 |

Partner Center: aún **no hay envío**; las únicas referencias que usarás son la URL de la política, el sitio web y el soporte (ver §6). No hay nada guardado allí que apunte al nombre antiguo.

## 2. Qué debe actualizarse
- `pages.yml` (ambas ramas): URLs derivadas del repositorio, no escritas a mano. **Hecho en la rama de preparación.**
- `ARPURLINFOABOUT` del MSI (enlace «Acerca de» en Aplicaciones instaladas): `https://github.com/PipeSick/hatsu-desktop`. Propiedad informativa; no afecta a UpgradeCode ni a rutas. **Hecho.**
- `verify_site.py`: acepta el contacto con ambos nombres (GitHub redirige) y construye las URLs de salida desde `GITHUB_REPOSITORY`. **Hecho.**
- `docs/53`, `PUBLICAR-PAGES.md`, `README`: nueva identidad pública. **Hecho.**
- **Políticas (requiere tu autorización):** las 2 menciones por idioma. Parche ya generado y NO aplicado: `docs/privacy/PROPUESTA-urls-hatsu-desktop.patch` (cambia solo `win11-macos`→`hatsu-desktop` y regenera `APPROVED.sha256`). Mientras no lo autorices, las políticas siguen aprobadas tal cual; tras el renombrado, el enlace antiguo de Issues **redirige**, así que no se rompe.
- `PipeSick/notch-win` `.gitmodules` → `…/hatsu-desktop` y nuevo pin en `tools/notch-pin.txt`. **Solo después del renombrado** (antes apuntaría a un repositorio inexistente).

## 3. Qué se conserva por compatibilidad (no se toca)
Carpetas de datos `%APPDATA%\WinMac` y `%LOCALAPPDATA%\WinMac`/`\NotchWin`; clave de inicio automático `WinMac`; mutex y tuberías (`Local\WinMac.*`, `winmac.json`); espacios de nombres y nombres de ensamblado `WinMac.*`; protocolo y tópicos IPC; `NotchWin.App.exe`; `external/winmac` (nombre del submódulo, ruta local); identidad del paquete MSIX y UpgradeCode del MSI; migradores (`Migrate-FromWinMac.ps1`, importación de ajustes de NotchWin); documentos históricos (describen el nombre de su época). Una prueba nueva impide que reaparezcan referencias vivas al nombre antiguo fuera de la lista permitida.

## 4. Qué se prepara automáticamente (hecho en `feat/hatsu-repo-rename-prep`)
Los cambios de §2 marcados «Hecho», la prueba de referencias vivas, el parche de políticas (sin aplicar) y este informe.

## 5. Qué debes hacer tú en GitHub (solo cuando autorices)
Orden recomendado — **renombrar ANTES de publicar la política y antes de enviar nada a Partner Center**, para no tener que cambiar la URL de la política después:
1. *Settings → General → Repository name* → `hatsu-desktop` → *Rename*. GitHub redirige web, `git` y API del nombre antiguo; **Pages NO redirige**: la ruta pasa a `https://pipesick.github.io/hatsu-desktop/`.
2. *Settings → Pages*: comprobar que sigue «Source: GitHub Actions» y HTTPS activado (el renombrado conserva la configuración, pero verifícalo).
3. *Settings → Environments → github-pages*: revisar la regla de ramas (debe seguir permitiendo la rama predeterminada).
4. En tu clon local (si lo hay): `git remote set-url origin https://github.com/PipeSick/hatsu-desktop.git`.
5. Avísame: entonces (a) fijo la URL del submódulo en `notch-win` y actualizo el pin, (b) ejecuto el CI completo, (c) lanzo el ensayo de Pages (`desplegar=false`) y, con tu OK, la publicación.
6. No crees un repositorio nuevo con el nombre `win11-macos`: rompería las redirecciones.

## 6. Cómo verificaremos después
- **CI:** ejecución verde completa (MSI, MSIX, pruebas de Hatsu Notch) con la identidad MSIX sin cambios (comprobaciones de PFN ya en `Test-Msix.ps1`).
- **Redirecciones:** `git ls-remote` y la URL web del nombre antiguo responden y llevan al nuevo (lo compruebo con curl).
- **Submódulo:** clon de `notch-win` en el pin nuevo con `git submodule update --init` contra la URL nueva.
- **Pages:** ensayo `desplegar=false` → publicación con tu aprobación → el job «Comprobar las URLs» exige HTTP 200 y el texto esperado en `https://pipesick.github.io/hatsu-desktop/privacy/` y `…/privacy/en/`; tú las abres sin sesión.
- **Partner Center:** al crear el envío, pega la URL nueva de la política (§ docs/53 §3–4), sitio web = `https://github.com/PipeSick/hatsu-desktop`, soporte = `…/issues`. Comprueba que la ficha muestre el nombre «Hatsu Desktop».
- **Usuarios existentes:** las rutas de datos, la clave de inicio automático y los mecanismos de migración no cambian (pruebas de migración y de configuración existentes siguen en verde).

## 7. Riesgos
| Riesgo | Impacto | Mitigación |
|---|---|---|
| Pages no redirige tras renombrar | URL de política antigua da 404 | Aún no se publicó nada ni se usó en Partner Center; renombrar primero |
| `.gitmodules` de `notch-win` apunta al nombre antiguo | Funciona por redirección de GitHub, frágil si el nombre antiguo se reutiliza | Actualizar tras renombrar; no reutilizar el nombre |
| `pages.yml` de la rama predeterminada con URL fija | Comprobación de URLs falla tras renombrar | Ya derivado del repositorio; hay que subir ese único archivo a la rama predeterminada (ver informe) |
| Workflows con permisos/entornos | El renombrado puede requerir re-aprobar el entorno `github-pages` | Paso 3 de §5 |
| Enlaces externos (otros repos, notas) al nombre antiguo | Redirigen | Ninguno conocido salvo `notch-win` |
| Políticas con el nombre antiguo | Texto desfasado (no roto) | Parche listo; requiere tu autorización |
