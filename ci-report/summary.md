# WinMac — Informe de CI

Commit: `` · SO: Unix 6.18.44.185 · .NET SDK: 10.0.114 · Configuración: Release

| Componente | Tipo | Restore | Build | Tests | Publish | Warnings | Errores | s |
|---|---|---|---|---|---|---|---|---|
| Common | lib | OK | OK | - | - | 0 | 0 | 2.3 |
| Common.Tests | test | OK | OK | OK | - | 0 | 0 | 18.2 |
| S2.DockWpf | app | OK | OK | - | - | 0 | 0 | 3.4 |
| S3.DockWinUI | app | OK | OK | - | - | 0 | 0 | 6.9 |
| S4.DockDComp | app | OK | OK | - | - | 0 | 0 | 3.8 |
| S1.WindowTracker | app | OK | OK | - | - | 0 | 0 | 3 |
| S5.TaskbarGuard | app | OK | OK | - | - | 0 | 0 | 2.4 |
| S6.UiaMenus | app | OK | OK | - | - | 0 | 0 | 2.6 |
| S7.PlatformProbe | app | OK | OK | - | - | 0 | 0 | 2.3 |
| Apps.Core | lib | OK | OK | - | - | 0 | 0 | 2.4 |
| Apps.Core.Tests | test | OK | OK | OK | - | 0 | 0 | 5.2 |
| Apps.Win | lib | OK | OK | - | - | 0 | 0 | 2.7 |
| Dock.Core | lib | OK | OK | - | - | 0 | 0 | 3 |
| Dock.Core.Tests | test | OK | OK | OK | - | 0 | 0 | 6.9 |
| Dock.Win | lib | OK | OK | - | - | 0 | 0 | 4.6 |
| Dock.App | app | OK | OK | - | - | 0 | 0 | 5.2 |
| Bar.Core | lib | OK | OK | - | - | 0 | 0 | 2.4 |
| Bar.Core.Tests | test | OK | OK | OK | - | 0 | 0 | 5 |
| Bar.Win | lib | OK | OK | - | - | 0 | 0 | 4.1 |
| Bar.App | app | OK | OK | - | - | 0 | 0 | 4.5 |
| Ipc | lib | OK | OK | - | - | 0 | 0 | 2.2 |
| Ipc.Tests | test | OK | OK | OK | - | 0 | 0 | 7.4 |
| Host.Core | lib | OK | OK | - | - | 0 | 0 | 2.7 |
| Host.Core.Tests | test | OK | OK | OK | - | 0 | 0 | 40.3 |
| FakeModule | app | OK | OK | - | - | 0 | 0 | 2.4 |
| Host | app | OK | OK | - | - | 0 | 0 | 2.8 |

**Todos los componentes restauran, compilan, pasan sus pruebas y se publican como artefacto.**

## Qué valida y qué NO valida este informe
- Valida: restauración de dependencias, compilación específica de Windows (WinUI/PRI/manifiesto en el runner Windows), pruebas unitarias de lógica pura, y que los binarios se pueden publicar.
- **NO valida**: comportamiento visual, animaciones, rendimiento real, interacción con ventanas de otras aplicaciones ni compatibilidad GPU. «Compila» no es «funciona».
- No se publica ninguna versión oficial: los binarios son artefactos temporales del workflow.

