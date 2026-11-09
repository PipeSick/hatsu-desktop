# 51 · Firma con SignPath Foundation: requisitos, cumplimiento, publicación y diseño

Estado: **análisis y diseño. No se ha activado ningún servicio externo, no se ha enviado ninguna solicitud, no se ha publicado nada nuevo y no se ha generado ningún MSI nuevo para instalar.** Smart App Control y las demás protecciones de Windows siguen activadas.

Etiquetas: **[MEDIDO]** observado en este repo/CI · **[SEGUNDA MANO]** leído en resúmenes de búsqueda (signpath.org no era accesible desde el entorno) · **[SIN VERIFICAR]**. Todo lo de SignPath debe confirmarse en su sitio antes de decidir.

## 1. Requisitos de SignPath Foundation [SEGUNDA MANO]
- Licencia aprobada por OSI y **sin componentes propietarios**.
- Equipo de firma = equipo que desarrolla / dueño del repo; **MFA** para todos los miembros (SignPath y GitHub).
- **Política de firma de código** publicada en la página del proyecto, con la frase de atribución.
- El publicador que Windows muestra es **SignPath Foundation**, no Hatsu.
- Binarios **compilados verificablemente desde el código fuente** (GitHub-hosted runners en todos los jobs previos a firmar; políticas restringidas a ramas main/release).
- Solo se firman binarios **propios** (no de terceros).
- Criterios comunitarios: repositorio público, ya publicado en la forma que se firmará, mantenido activamente; hay casos de rechazo por falta de visibilidad externa.
- Sin verificar: elegibilidad exacta, si una misma organización puede tener dos proyectos (Hatsu y NotchWin), versión vigente de la acción de GitHub (la documentación menciona v1/v3/v4).

## 2. ¿Cumple Hatsu hoy?
| Requisito | Estado |
|---|---|
| Licencia OSI | Sí: Apache-2.0 (Hatsu), MIT (NotchWin) [MEDIDO] |
| Repo público | Sí, ya lo es (creado 2026-10-30) [MEDIDO] |
| Sin componentes propietarios | **Duda:** el runtime de Windows App SDK es de Microsoft (licencia propia); preguntar a SignPath si cuenta |
| Solo binarios propios | **Parcial:** 8 DLL MIT de terceros sin firma (docs/50 §5.1) no se pueden firmar |
| Build verificable en runners hospedados | Sí en CI; **falta** un flujo de release (solo existe el borrador inerte) |
| Rama main / de release | **No:** no existe `main`; la rama por defecto es `claude/…` |
| Ya publicado en la forma a firmar | **No:** sin tags ni releases |
| Política de firma publicada | **No** (borrador sin publicar) |
| MFA / equipo | **Sin verificar** (lo confirma el propietario) |
| Visibilidad / mantenimiento | **Riesgo:** 0 estrellas, repo de 6 días, 290 commits todos de `Claude <noreply@anthropic.com>` |
| Comportamiento sensible declarado | Gancho de teclado, atajos globales, vigilancia del portapapeles (NotchWin), ocultar barra de tareas, red hacia speed.cloudflare.com / open-meteo / music.163.com; sin telemetría. Debe constar en la política |

Conclusión: **cumple lo técnico (licencia, repo público, CI hospedado) y falla lo de proceso y visibilidad** (sin main, sin release, sin política, sin historial externo). Es subsanable sin gasto; la aprobación depende de SignPath y no se puede garantizar.

## 3. Limitaciones que hay que aceptar
- El publicador mostrado será **SignPath Foundation**.
- Cada release requiere **aprobación manual** en SignPath.
- **Firmar no garantiza que SAC acepte**: SAC decide primero por reputación en la nube y solo después por firma; hay reportes de binarios firmados bloqueados. Se medirá, no se supondrá.
- El certificado de prueba de SignPath no es de confianza para usuarios.
- NotchWin es otro proyecto: pipeline y solicitud aparte (o consulta si caben en una sola).
- Las 8 DLL de terceros siguen sin firma (opciones en docs/50 §5.1).
- Alternativas si se rechaza: Azure Artifact Signing (vista previa restringida; para personas solo EE. UU./Canadá, probablemente no disponible desde Chile) [SEGUNDA MANO]; certificado comercial OV/EV con HSM o firma en la nube (**cuesta dinero: requiere autorización**); MSIX/Store (evita SAC pero virtualiza AppData; descartado en Fase 2).

## 4. Lista de comprobación para publicar con seguridad
Estado actual [MEDIDO]: el repositorio **ya es público**; esta lista es de higiene sobre lo ya expuesto.
- [x] Escaneo de secretos en todo el historial: 0 hallazgos.
- [ ] **Fuga:** el nombre de usuario real de Windows apareció en docs/50 (commit `250ca48`); corregido en la punta, **sigue en el historial**. Decidir: reescribir historial (destructivo, requiere autorización) o aceptarlo.
- [ ] NotchWin: el único commit lleva el correo personal del autor, ya público. Decidir si importa.
- [ ] `ci-report/` versionado con registros: revisar y sacar del control de versiones.
- [ ] README, THIRD_PARTY y NOTICE desactualizados (nombres WinMac/Hatsu, lista de dependencias).
- [ ] Faltan SECURITY.md, CONTRIBUTING.md, CODEOWNERS, plantillas de issue/PR, dependabot.
- [ ] Crear `main` canónica y cambiar la rama por defecto; protección de rama.
- [ ] Activar secret scanning y push protection; 2FA/MFA en la cuenta; Actions restringidas y acciones fijadas por SHA; entornos `signing` y `release` con revisores obligatorios.
- [ ] Licencias: NuGet todas MIT/Apache [MEDIDO]; Windows App SDK bajo licencia de Microsoft; icono Flux (procedencia); afirmación de «clean-room» de NotchWin frente a Vorssaint (GPLv3) a revisar con cuidado; términos de Open-Meteo y NetEase.
- [ ] Declarar la autoría asistida por IA y el comportamiento sensible en README/política.

## 5. Diseño de integración (inerte)
Archivos ya preparados, **sin subir ni activar**: `docs/signing/release-signed.yml.draft`, `installer/signpath/*`, `tools/signing/New-ArtifactConfiguration.ps1`, `tools/signing/Merge-SignedBinaries.ps1`, `tools/ci/Verify-Signatures.ps1 -Require`.
Flujo: build (runner hospedado) → subir binarios propios → SignPath firma PE → se superponen sobre el paquete (se verifica firma válida y sello de tiempo) → MSI → SignPath firma MSI → `Verify-Signatures -Require` + `Test-Msi` → release **en borrador**, con aprobación humana. El token de SignPath solo lo crea el propietario como secreto del entorno; yo nunca lo manejo. NotchWin ya tiene un andamiaje parecido que se reutiliza.

## 6. Recomendación
1. **Seguir con SignPath Foundation** como primera opción: es gratis, compatible con open source y coherente con tu decisión. No comprar nada.
2. **Antes de postular**, subsanar lo que SignPath mira: `main` canónica, política de firma publicada, higiene de la sección 4, y un pre-release en borrador compilado en CI (sin pedir a nadie instalarlo).
3. **Medir antes de prometer:** la aceptación por SAC no está garantizada; se probará en modo evaluación (`citool -lp` / eventos 3076) o en un Windows con SAC activo solo cuando exista un binario firmado.
4. Plan B documentado: certificado comercial (decisión de gasto tuya) o MSIX.

## 7. Pasos que requieren tu autorización (en orden)
1. Confirmar que el repositorio público es intencional y autorizar crear `main` y cambiar la rama por defecto.
2. Confirmar MFA en GitHub (y luego en SignPath).
3. Autorizar el lote de higiene (sección 4) y decidir sobre reescritura de historial (`250ca48`) y el correo en NotchWin.
4. Autorizar publicar la política de firma (`docs/signing/CODE_SIGNING.md`, por redactar) y enlazarla desde el README.
5. Autorizar activar el flujo de release (renombrar el borrador a `.yml`) para producir un pre-release en borrador.
6. Tú solicitas SignPath Foundation (yo redacto el texto), consultando de paso lo de Windows App SDK, dependencias y dos proyectos.
7. Tú creas el token como secreto de entorno; yo no lo veo.
8. Tras firmar: una sola ronda de prueba física en tu Windows, solo si lo autorizas.

Hasta entonces: **no se instala ningún MSI nuevo** y no se publican releases sin firma.
