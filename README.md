# Hatsu Desktop

> Repositorio: `PipeSick/hatsu-desktop` (antes `PipeSick/win11-macos`; GitHub redirige los enlaces antiguos). Módulo integrado: **Hatsu Notch**. Migración: [`docs/54`](docs/54-migracion-repositorio-hatsu-desktop.md).

> **Hatsu Desktop es la evolución de WinMac.** El proyecto se llamó «WinMac» (nombre provisional) hasta la iteración 8; el nombre público ahora es **Hatsu Desktop**. Por compatibilidad con instalaciones, configuraciones y datos existentes, en esta fase se conservan los **identificadores internos** antiguos (archivos `WinMac*.exe`, carpeta de datos `%APPDATA%\WinMac` y `%LOCALAPPDATA%\WinMac`, valor de inicio automático «WinMac», nombres de proceso y canales entre componentes). Detalle de la auditoría y de la estrategia de migración: [`docs/46-rebranding-hatsu-desktop.md`](docs/46-rebranding-hatsu-desktop.md). El repositorio y sus URLs **no** se han renombrado todavía.
>
> Identidad visual: [`assets/brand/`](assets/brand/README.md) (el icono actual es **provisional**, no un diseño aprobado).

_(Los documentos históricos de `docs/` conservan el nombre «WinMac» tal como se escribieron.)_


Experiencia de escritorio inspirada en macOS Tahoe para Windows 11. Proyecto open source bajo licencia **Apache-2.0** (ver [`LICENSE`](LICENSE), [`NOTICE`](NOTICE) y [`THIRD_PARTY.md`](THIRD_PARTY.md)).

Estado: **Fase 1** (validación de tecnologías). No hay producto utilizable todavía.

- Propuesta de arquitectura: [`docs/00-propuesta-arquitectura.md`](docs/00-propuesta-arquitectura.md)
- Informe de Fase 1: [`docs/01-fase1-resultados.md`](docs/01-fase1-resultados.md)
- **Probar los prototipos en Windows:** [`docs/INSTRUCCIONES-PRUEBAS.md`](docs/INSTRUCCIONES-PRUEBAS.md)
- Fidelidad visual: [`docs/02-fidelidad.md`](docs/02-fidelidad.md) · Criterios de aceptación: [`docs/03-criterios-aceptacion.md`](docs/03-criterios-aceptacion.md)
- Dock (iteración 1, provisional): [`docs/06-dock-iteracion-1.md`](docs/06-dock-iteracion-1.md) · ADR provisional: [`docs/04-adr-001-provisional.md`](docs/04-adr-001-provisional.md) · Liquid Glass: [`docs/05-liquid-glass.md`](docs/05-liquid-glass.md)
- CI: `.github/workflows/ci.yml` (informe, pruebas y artefactos temporales; **no publica versiones**)
- Dock funcional (iconos reales, anclado, ventanas, menús): [`docs/10-dock-funcional.md`](docs/10-dock-funcional.md)
- Host de WinMac (F1: supervisor, IPC local, guía de validación): [`docs/15-f1-resultados.md`](docs/15-f1-resultados.md) · alcance: [`docs/14-f1-alcance-minimo.md`](docs/14-f1-alcance-minimo.md)
- Barra superior (diseño, alternativas y limitaciones): [`docs/12-barra-superior.md`](docs/12-barra-superior.md)
- Centro de Notificaciones (panel lateral con notificaciones reales de Windows y widgets; APIs, límites y cómo probarlo): [`docs/20-centro-de-notificaciones.md`](docs/20-centro-de-notificaciones.md)
- Calibración visual contra macOS Tahoe: [`docs/08-calibracion-visual.md`](docs/08-calibracion-visual.md) · herramienta: `tools/calibration/calibracion.html` · pendiente de Windows físico: [`docs/PENDIENTE-WINDOWS-FISICO.md`](docs/PENDIENTE-WINDOWS-FISICO.md)
