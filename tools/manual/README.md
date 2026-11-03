# Herramientas manuales (fuera del CI obligatorio)

Desde la tercera iteración de WinMac 2.0 el CI solo valida lo técnico (compilar, pruebas, integración, paquete). Estos scripts siguen
disponibles para investigar un defecto concreto en un Windows con escritorio; NO se ejecutan en cada push:

- `Visual-Audit.ps1` — recorre WinMac completo y hace capturas de la pantalla real (necesita las variables `WINMAC_MENU_DIAG=1` y `WINMAC_PILL_SELFTEST`).
- `Invoke-Smoke.ps1` — renders fuera de pantalla del Dock, la Barra y Ajustes (PNG) y selftests; llama a `../ci/Test-Host.ps1`.
- `Test-TrayCapture.ps1` (+ `TrayIconProcess.ps1`) — iconos de bandeja de prueba y captura del desplegable.
- `Test-Launchpad.ps1` — Launchpad con el índice real de apps.
- `Lyrics-Diag.ps1` — consulta de solo lectura a NetEase/LRCLIB para un título y artista.
- `Write-Summary.ps1` — resumen visual para GitHub.

La aprobación visual y de interacción la hace el usuario en su Windows real.
