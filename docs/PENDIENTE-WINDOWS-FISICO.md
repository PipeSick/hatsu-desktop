# Pendiente de validar en Windows físico (documento vivo)

Estado a 2026-10-31. Todo lo de esta lista **no está validado**: el CI de Windows (runner virtual, sin GPU real) solo comprueba compilación, pruebas unitarias, ejecución sin errores y dibujo fuera de pantalla sobre WARP.

## Validado en Windows físico (2026-10-31, equipo del usuario)
Windows 11 build 26200, GPU NVIDIA GeForce RTX 5070 Ti, escala 100 %, SDK .NET 10.0.403, compilado desde la rama.
| Qué | Resultado |
|---|---|
| `--selftest` | **todo OK**: configuración, seguimiento de ventanas (5 ganchos, 9 apps con ventana en 8 ms), íconos del Shell (128×128), detector de pantalla completa, render fuera de pantalla (10 PNG, WARP), ventana + DirectComposition con **GPU real** (30 fotogramas, fondo de pantalla cargado) |
| `--demo` en pantalla | **correcto según el usuario**: aparece sobre la barra de tareas, vidrio con el fondo real, íconos, indicadores, separadores, carpeta y papelera visibles |
| Aspecto | confirma lo medido en docs/09: sombra inferior pesada, placas de ícono gruesas, bordes intensos (a corregir en la iteración de calibración) |
Nota: W3 (ventana real con DirectComposition) y parte de W4 (fondo real) quedan **validadas en este equipo**, sin cubrir otras GPU, DPI ≠ 100 % ni varios monitores.

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

## Validación manual del Dock funcional (docs/10)
Código compilado y probado en lógica pura; **sin ejecutar en Windows**. Orden sugerido:
| # | Qué probar |
|---|---|
| V1 | `--selftest` (secciones nuevas) y `--icon-test` con explorer/notepad/calc y un AUMID (p. ej. Calculadora/Configuración) |
| V2 | Iconos reales: nitidez, transparencia, proporciones; apps MSIX/UWP; icono corrupto no cuelga |
| V3 | Anclar/desanclar desde el menú y desde CLI; persistencia tras reiniciar el Dock |
| V4 | Reordenar arrastrando y con "Mover a…"; el orden persiste |
| V5 | Apps abiertas no ancladas aparecen y desaparecen al cerrarlas; indicadores correctos |
| V6 | Clic: lanzar si no corre, activar, restaurar minimizada, sin duplicados con doble clic |
| V7 | Varias ventanas de una app: lista en el menú y activación de cada una |
| V8 | Cerrar ventana / Salir de la aplicación (cooperativo; app con diálogo de guardar) |
| V9 | Menú nativo: aparece, se cierra con clic fuera/Esc, no roba el foco de forma anómala |
| V10 | Pantalla completa/juego: el Dock se aparta; `--quit` lo cierra |
| V11 | Editar `dock.json` a mano: recarga en ≤2 s; JSON inválido no rompe el Dock |
| V12 | Sin permisos de administrador; Explorador y barra de tareas intactos |
