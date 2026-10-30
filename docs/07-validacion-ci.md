# Validación del Dock en CI (runner Windows) — registro de correcciones

Estado: **el workflow de Windows termina en éxito** (run 4). Esto valida compilación, pruebas unitarias, ejecución de `--selftest`, `--render-png`, `--bench-offscreen` y generación de artefactos.
**No valida** que el Dock se vea o se comporte bien en una pantalla real: ver §5.

## 1. Run exitoso
[Run 41128436257](https://github.com/PipeSick/win11-macos/actions/runs/41128436257) sobre `cace429` — Windows Server 2025 (build 26100), .NET SDK 10.0.403, GPU del runner: *Microsoft Hyper-V Video* (virtual; el dibujo usa **WARP**, render por software).

| Job | Resultado |
|---|---|
| Linux · compilación cruzada y pruebas | éxito |
| Windows · restaurar, compilar, probar, publicar | éxito (todos los componentes: restore/build/test/publish OK, 0 errores, 0 advertencias) |
| Windows · humo del Dock (selftest, PNG, bench) | éxito |

## 2. Historia de ejecuciones y correcciones
| Run | Commit | Resultado | Causa | Corrección (commit) |
|---|---|---|---|---|
| 41111226149 (workflow antiguo `spikes`) | `b061b22` | éxito | — | Solo compilaba prototipos y 12 pruebas; sin artefactos |
| 41115820149 | `32d92dd` | **fallo** | `dotnet publish -r win-x64 --no-restore` → NETSDK1047/1112 en todas las apps (bug del **script de CI**, no del código). El humo quedó omitido | Publicar sin `--no-restore` (`35357cc`) |
| 41126531148 | `35357cc` | éxito **engañoso** | `Invoke-Smoke.ps1`: `Join-Path $OutDir (…) + '.txt'` pasaba `+` y `.txt` como argumentos → todos los pasos del Dock fallaban al arrancar y el script **ocultaba** el error (artefacto de 1 KB) | Rutas absolutas, `Start-Process -ErrorAction Stop`, y salida con error si falla un paso (`21d87a0`, `c76192c`) |
| 41127458759 | `c76192c` | fallo | El humo **ya ejecutó el Dock** (selftest OK, 11 PNG, bench). Falló solo el prototipo S3 (WinUI): se cierra al arrancar en el runner con `0xC000027B` | S3/S2/S4 `--quick` pasan a *informativos* (`cace429`) |
| 41128436257 | `cace429` | **éxito** | Al revisar los PNG: el fondo procedural de CI cubría solo ~75–84 % de la ventana (se dibujaba en píxeles con DPI≠96) | `SyntheticBackdrop` dibuja en DIP (`cace429`) |

Además: salida de consola en UTF-8 (`cace429`); Job Summary, hoja de contacto y artefactos separados (`21d87a0`).
La corrección del fondo sintético **solo afecta a CI/offscreen**; el host en pantalla usa el fondo real y no estaba afectado.

## 3. Resultados
**Pruebas unitarias (Windows):** Common.Tests **12/12** · WinMac.Dock.Core.Tests **46/46**.
**`--selftest`:** código de salida 0 = todas las secciones OK (configuración, seguimiento de ventanas con `SetWinEventHook`, íconos del Shell, detector de pantalla completa, render fuera de pantalla de 10 PNG, ventana + DirectComposition con 30 fotogramas). El detalle por sección está en el Job Summary y en el artefacto `smoke-*`; esta sesión no puede descargar artefactos, así que **no revisé esas líneas una por una**.
**`--render-png`:** 11 PNG generados (5 escenas × 2 temas a 2× + variante sin fondo), más 2 vistas previas.
**`--bench-offscreen` (WARP, NO es FPS real):** dibujar un fotograma p50 ≈ 5,7 ms, p95 ≈ 7,9 ms; con lectura del fotograma p50 ≈ 10,5 ms; construir el fondo ≈ 27 ms. Son tiempos de **render por software en una GPU virtual**, sin presentación ni DirectComposition; solo sirven para comparar versiones y detectar regresiones gruesas.
**Prototipo S3 (WinUI) en el runner:** se cierra al arrancar (`0xC000027B`, excepción almacenada). Causa **no determinada** (hipótesis: WinUI sin empaquetar necesita una sesión interactiva/runtime que el runner no ofrece). No afecta al Dock.

## 4. Artefactos (run 41128436257; caducan a los 14 días)
Se ven en la pestaña **Actions → run → Artifacts** (<https://github.com/PipeSick/win11-macos/actions/runs/41128436257>) y el **Job Summary** del job de humo.
| Artefacto | Contenido |
|---|---|
| `dock-hoja-de-contacto-<sha>` (≈2 MB) | `contact-sheet.png` (todas las escenas, claro/oscuro) + 2 vistas previas |
| `smoke-<sha>` (≈7 MB) | 11 PNG a 2×, bench JSON, logs del selftest, `smoke.md` |
| `ci-report` (≈5,8 MB) | `summary.md/json`, logs por componente, binlog, trx |
| `winmac-binarios-<sha>` (≈99 MB) | Binarios publicados del Dock y los prototipos (**temporales; no es una versión oficial**) |

## 5. Limitaciones
- **Compilar y ejecutar en CI ≠ funcionar visualmente.** Los PNG salen del renderizador real (DockController + DockPainter + GlassBackdrop) pero **fuera de pantalla**, sobre WARP y con un **fondo procedural**. No validan: ventana con DirectComposition (solo se comprobó que 30 fotogramas se ejecutan sin error), entrada del ratón, fondo real del escritorio, GPU física, DPI mixtos, varios monitores, apps reales (íconos 0/11 en la escena demo), rendimiento.
- Las métricas de WARP no representan una GPU física. FPS, latencia y consumo definitivos requieren Windows físico.
- No pude descargar los PNG completos (el proxy de esta sesión bloquea el host de artefactos); revisé solo dos vistas previas (0,75×) transmitidas por el log. Los 11 PNG a 2× están disponibles para ti en el artefacto.
- La rúbrica de fidelidad **no está aprobada**; los valores de `DockMetrics`/`GlassStyle` son provisionales.
- Accesibilidad (UIA), reordenar/arrastrar, menús contextuales, miniaturas, multimonitor: **no implementados** (ver docs/06).

## 6. Observaciones preliminares sobre las dos vistas previas (no es una aprobación)
- El vidrio muestra el fondo desenfocado/saturado, borde claro y sombra; en oscuro el material baja de brillo como se espera.
- Los íconos de demostración (placa blanca + letra de color) se ven genéricos frente a macOS; la sombra de la barra parece pesada en tema claro; la etiqueta es pequeña. Son puntos a calibrar con tus referencias.

## 7. Decisiones que necesito de ti
1. Revisar los PNG del artefacto `dock-hoja-de-contacto-…` y `smoke-…` y decirme qué atributos (proporciones, espaciado, magnificación, bordes, transparencias, sombras, indicadores) se alejan de Tahoe.
2. ¿Capturas de referencia de tu Mac para calibrar `DockMetrics`/`GlassStyle` (privadas, no se suben)?
3. ¿Investigo el fallo de S3 en el runner o lo dejo como limitación del prototipo?
4. ¿Seguimos con el Dock (calibración visual) o pausas hasta tener un Windows físico para medir?
