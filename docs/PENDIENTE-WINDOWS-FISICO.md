# Pendiente de validar en Windows físico (documento vivo)

Estado a 2026-10-31. Todo lo de esta lista **no está validado**: el CI de Windows (runner virtual, sin GPU real) solo comprueba compilación, pruebas unitarias, ejecución sin errores y dibujo fuera de pantalla sobre WARP.

## Limitaciones conocidas que hay que revisar antes de seguir
| # | Limitación | Estado | Cuándo revisarla |
|---|---|---|---|
| L1 | **Prototipo S3 (WinUI 3) se cierra al arrancar en el runner** de CI (`0xC000027B`, excepción almacenada; run 41127458759). Causa **no determinada** (hipótesis: WinUI sin empaquetar necesita sesión interactiva/runtime que el runner no ofrece). **Decisión del usuario: no se investiga ahora.** | Documentada; el humo de prototipos es *informativo* | **Antes de desarrollar cualquier componente que dependa de WinUI 3** (Settings, Finder, Launchpad según la hipótesis de docs/00) |
| L2 | El runner no tiene GPU real (Hyper-V Video): el dibujo corre en WARP | Conocida | Medir en GPU física |

## Pruebas pendientes en Windows físico
| # | Qué | Por qué no basta el CI |
|---|---|---|
| W1 | FPS, frame pacing y latencia reales del Dock (PresentMon) | El bench del CI mide dibujo por software + lectura de fotograma |
| W2 | Consumo de CPU/GPU/memoria en reposo y animando; energía en portátil | Sin GPU ni medición de energía |
| W3 | Ventana real con DirectComposition: transparencia, parpadeo, ordenación sobre la barra de tareas, ventanas maximizadas | El selftest solo ejecuta 30 fotogramas sin error |
| W4 | Fondo de pantalla real: estilos (Rellenar/Ajustar/Estirar/Centrar), presentación/slideshow, color sólido, varios monitores | El CI usa un fondo procedural |
| W5 | Íconos reales del Shell (apps empaquetadas/UWP) y seguimiento de ventanas con apps reales | La escena demo no tiene apps reales (íconos 0/11) |
| W6 | Clic: activar/restaurar/lanzar con `SetForegroundWindow` desde una ventana `NOACTIVATE` | Requiere ventanas reales |
| W7 | DPI 125–200 %, DPI mixtos, varios monitores, hot-plug | Solo se probó escala 1, 0,75 y 2 en PNG |
| W8 | Pantalla completa/juegos: el dock se aparta y vuelve | Sin juegos ni vídeo exclusivo |
| W9 | Autohide y zona caliente en el borde inferior | Requiere puntero real |
| W10 | Detalle por sección del `--selftest` (solo se verificó el código de salida 0) | No se pueden descargar artefactos desde la sesión de desarrollo |
| W11 | Accesibilidad (UIA, Narrador, teclado): **no implementada** | — |
| W12 | S5 (barra de tareas) en una VM con snapshot; S6 con 10+ apps; S7 (escritorios virtuales 24H2/25H2) | Modifican el escritorio o requieren apps reales |
| W13 | Spikes S2/S3/S4 con `Run-Phase1Tests.ps1 -Runs 3` (comparativa WPF/WinUI/DComp) | Solo hay humo informativo |

## Qué NO bloquea el trabajo visual
La calibración contra macOS Tahoe (docs/08) se hace con capturas del Mac y los PNG del CI; no necesita Windows físico. Los valores calibrados se confirmarán después en Windows físico (W3, W4, W7).
