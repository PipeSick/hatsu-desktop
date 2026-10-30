# Dock — iteración 1 (provisional)

Estado: **primera implementación. Compila (Linux y, para los prototipos previos, Windows en CI). El Dock NO se ha ejecutado nunca en Windows.**
Lo no ejecutado está marcado **PENDIENTE DE VALIDACIÓN**. Nada aquí declara terminada la Fase 2.

> **Actualización:** el CI de Windows ya termina en éxito y ejecuta `--selftest`, `--render-png` y `--bench-offscreen`. Ver **docs/07-validacion-ci.md** (la tabla de abajo es del primer run, ya superado).

## 0. Resultado del CI en Windows (primera ejecución, superada)
Ejecución [41115820149](https://github.com/PipeSick/win11-macos/actions/runs/41115820149) sobre `32d92dd` (Windows Server 2025, build 26100, SDK 10.0.403):

| Comprobación | Resultado |
|---|---|
| Restaurar dependencias (todos los componentes) | **OK** |
| Compilar Dock.Core, Dock.Win, Dock.App y los prototipos en Windows | **OK**, 0 errores, 0 advertencias |
| Pruebas unitarias de Dock.Core en Windows | **OK** (código de salida 0; el recuento exacto no quedó en el log de consola) |
| Publicar binarios (`dotnet publish -r win-x64`) | **FALLÓ** en todas las apps: error NETSDK1047/1112 — fallo **del script de CI** (`--no-restore` con identificador de runtime), no del código |
| `--selftest` y `--render-png` | **NO SE EJECUTARON**: el job de humo depende de la publicación y quedó omitido |
| Artefactos | `ci-report` generado (logs, binlog, trx); el artefacto de binarios quedó **vacío** por el fallo anterior |

Corrección local (pendiente de autorización para subirla): publicar sin `--no-restore`, mostrar el recuento de pruebas en el log, y ampliar el humo.
**Un CI en verde no valida el aspecto ni el comportamiento visual.**

## 1. Estructura (`src/`)
| Proyecto | Contenido | Dependencias |
|---|---|---|
| `WinMac.Dock.Core` (net10.0) | Métricas, layout/magnificación, resortes, controlador (hover, autohide, clics, salto de lanzamiento), modelo de ítems, configuración JSON, parámetros del vidrio, mapeo de fondo de pantalla, codificador PNG, datos demo | ninguna |
| `WinMac.Dock.Core.Tests` | **43 pruebas unitarias** (ejecutadas en Linux: 43/43 OK) | xUnit |
| `WinMac.Dock.Win` (net10.0-windows) | Seguimiento de ventanas (`SetWinEventHook`), acciones (activar/minimizar/restaurar/lanzar), íconos del Shell, detector de pantalla completa, `D2DDevice` (GPU→WARP), `DockPainter`, `GlassBackdrop`, `WallpaperSource`, `DockHost` (Win32+DirectComposition), render fuera de pantalla | Vortice 3.8.3 |
| `WinMac.Dock.App` | Ejecutable: dock real, `--demo`, `--render-png`, `--selftest` | — |

## 2. Funcionalidades implementadas (en código; ver estado de validación en §3)
| Área | Implementado |
|---|---|
| **Geometría** | Barra con radio propio, íconos squircle, separadores, espaciado; todo en `DockMetrics` (calibrable) |
| **Ampliación** | Magnificación coseno con alcance configurable; **resortes por ítem** (ligero rebote); la barra crece y centra; los íconos nunca se solapan (probado) |
| **Animaciones** | Aparición de ítems nuevos (crecen desde 0), **salto de lanzamiento** con rebote, ocultar/mostrar por resorte (desplazamiento del Visual) |
| **Temas** | Claro y oscuro (`GlassStyle.For`), `Theme: System/Light/Dark` en la configuración (el modo Sistema lee `AppsUseLightTheme`); *implementado, sin validar visualmente* |
| **Transparencia/material** | Vidrio en capas: fondo de pantalla desenfocado+saturado cacheado, tinte, brillo superior, **borde especular doble**, sombra en 14 capas; nivel `Tint` de reserva |
| **Indicadores** | Punto bajo apps abiertas (más intenso si está activa); etiqueta con el nombre sobre el ícono bajo el cursor |
| **Apps** | Ancladas (JSON en `%AppData%\WinMac\dock.json`), recientes (apps en ejecución no ancladas, máx. 3, orden estable), carpeta Descargas, **Papelera** (ícono propio; abre `shell:RecycleBinFolder`) |
| **Ventanas** | Seguimiento por eventos (sin polling de ventanas) y agrupación por ejecutable; clic: lanzar / restaurar minimizadas / activar; opción para minimizar al clicar la app activa (`ClickActiveMinimizes`, por defecto estilo macOS: no) |
| **Comportamiento** | Ocultamiento automático opcional (retardos configurables, zona caliente en el borde inferior); **se aparta ante pantalla completa** (`SHQueryUserNotificationState`) |
| **Entrada** | Solo la barra captura el ratón (`WM_NCHITTEST` → el resto es transparente); no roba el foco (`WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE`) |
| **Rendimiento (diseño)** | Sin render en reposo; bucle a ~20 Hz solo para sondear; fondo del vidrio cacheado; fallback a WARP |
| **Seguridad/Convivencia** | **No** toca la barra de tareas, el registro, ni archivos del sistema; no inyecta en procesos; instancia única; configuración con escritura atómica y carga tolerante a errores |
| **CI** | Render fuera de pantalla → PNG (5 escenas × 2 temas, a 2×), `--bench-offscreen` (coste de dibujo en WARP; **no es FPS real**) y `--selftest` en runner Windows como humo informativo |

## 3. Pendiente de validación (NO ejecutado en Windows)
| # | Funcionalidad | Qué se debe comprobar | Cómo |
|---|---|---|---|
| P1 | **Todo el render** en ventana real (swap chain de composición, DComp, alfa premultiplicado) | Que aparece, sin parpadeo, con transparencia correcta | `WinMac.Dock.App --demo` |
| P2 | Fidelidad visual del vidrio frente a Tahoe | Rúbrica 0–3 × 10 atributos (**no aprobada**; valores provisionales) | Capturas + tus referencias |
| P3 | Rendimiento (frame-time, CPU reposo/animación, memoria, arranque) | Umbrales docs/03 (objetivos) | Falta instrumentar el Dock con `BenchRun`; hoy solo S2/S3/S4 lo están |
| P4 | Seguimiento de ventanas y agrupación | Apps reales; UWP bajo `ApplicationFrameHost`; procesos elevados | `--selftest` + uso real |
| P5 | Íconos reales vía `IShellItemImageFactory` | Calidad/alfa; apps UWP/empaquetadas | `--selftest` (imprime íconos cargados) |
| P6 | Clic: activar/restaurar/lanzar | `SetForegroundWindow` desde un clic en ventana `NOACTIVATE` | Uso real |
| P7 | Fondo de pantalla: estilos (Rellenar/Ajustar/Estirar/Centrar), presentación/slideshow, color sólido, varios monitores | Que el recorte coincide con el fondo real | `--demo` con varios fondos |
| P8 | DPI 125–200 % y monitores mixtos; **solo se usa el monitor principal** | Posición, nitidez, `WM_DPICHANGED` | Pruebas reales |
| P9 | Pantalla completa / juegos | El dock se oculta y vuelve | Un juego y un vídeo |
| P10 | **Accesibilidad (UIA)**: **no implementada** | Narrador, teclado | Pendiente de diseño |
| P11 | Autohide y zona caliente | Revelado al tocar el borde inferior | Uso real |
| P12 | Interacción con la barra de tareas (el dock se coloca sobre el área de trabajo; no reserva espacio) | Ventanas maximizadas bajo el dock | Uso real; decidir AppBar |
| P13 | Reordenar/arrastrar, menús contextuales, miniaturas, múltiples ventanas por app (menú), gestos | **No implementados** | Fuera de esta iteración |
| P14 | Persistencia: guardar anclajes al cambiar | **No implementado** (solo lectura del JSON) | — |

## 4. Cómo probarlo después (en Windows 11)
```powershell
dotnet build src\WinMac.Dock.App -c Release
# 1) Comprobaciones no interactivas con diagnóstico (no abre un dock permanente)
.\src\WinMac.Dock.App\bin\Release\net10.0-windows\WinMac.Dock.App.exe --selftest --out selftest-out
# 2) Dock con datos de demostración (Esc no cierra: termina el proceso desde el Administrador de tareas o Ctrl+C si lo lanzas desde consola)
.\src\WinMac.Dock.App\bin\Release\net10.0-windows\WinMac.Dock.App.exe --demo
# 3) Dock real con tus apps (instancia única; configuración en %AppData%\WinMac\dock.json)
.\src\WinMac.Dock.App\bin\Release\net10.0-windows\WinMac.Dock.App.exe
# Variantes: --tier tint  ·  --no-config
# Renders fuera de pantalla (sin ventana):
.\...\WinMac.Dock.App.exe --render-png dock.png --pose hover --tier wallpaper --scale 1.5
```
Todo es **no invasivo**: no modifica la barra de tareas ni configuraciones. Para salir: Ctrl+C en la consola o terminar el proceso. **Antes de la primera ejecución en tu equipo, léelo** (docs/04-adr-001-provisional.md, riesgos).

### Ver el resultado del CI sin Windows (desde macOS)
1. En GitHub: pestaña **Actions** → ejecución del workflow `ci` → sección **Artifacts**.
2. `smoke-<sha>`: contiene `dock-rest.png`, `dock-hover.png`, `dock-hidden.png` (render D2D sobre WARP con fondo procedural), `smoke.md` y los logs del `--selftest`.
3. `ci-report`: `summary.md` con el estado de restore/build/tests/publish por componente.
4. `winmac-binarios-<sha>`: binarios publicados de los prototipos y del Dock (**artefactos temporales; no son una versión oficial**). Requieren Windows para ejecutarse.
**Los PNG del CI validan que el dibujo se ejecuta y muestran su aspecto aproximado; no validan la composición en ventana, la entrada del ratón ni el rendimiento.**

## 5. Riesgos y próximos pasos
**Riesgos principales:** (1) código de GPU sin ejecutar: el CI es la primera ejecución; (2) UIA/accesibilidad ausente; (3) fidelidad calibrada sin referencias; (4) dependencia de Vortice por verificar; (5) UWP/elevados/multi-monitor sin cubrir.

**Próximos pasos propuestos (requieren tu aprobación):**
1. Autorizar el push de estos commits para ejecutar el CI en Windows; corregir lo que falle y revisar los PNG.
2. Calibración con tus capturas de referencia (parámetros de `DockMetrics`/`GlassStyle`).
3. Instrumentar el Dock con `BenchRun` y medir en un equipo Windows (o en una VM, como dato adicional).
4. Diseño de UIA (decide la continuidad de la elección provisional).
5. Spike de refracción con `DisplacementMap` y, solo después de medir, decidir sobre captura en vivo.
