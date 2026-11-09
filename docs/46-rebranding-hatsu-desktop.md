# Rebranding: WinMac → Hatsu Desktop (Fase 1)

Rama `feat/hatsu-rebrand-phase1` (base: iteración 8, `dbad8c9`; NotchWin fijado en `80bd29a`). Alcance: **identidad y compatibilidad**. No se tocan funciones, arquitectura, IPC ni NotchWin. Sin PR, merge ni release. El repositorio no se renombra.

## 1. Cómo se almacenan hoy los datos (por qué no se migra nada)
| Dato | Dónde | Decisión |
|---|---|---|
| Configuración central, Dock, Barra, Isla (local), Peripherals y Cleanup (dentro de `bar.json`), flujos de trabajo, scripts de Spotlight | `%APPDATA%\WinMac\{winmac,dock,bar,workspaces,spotlight-scripts}.json` | **Se conserva la carpeta `WinMac`** (B). Renombrarla exigiría mover datos de un usuario; el riesgo supera el beneficio |
| Registros, cachés, lanzador, edición de capturas, extensiones, restauración de la barra de tareas | `%LOCALAPPDATA%\WinMac\…` | Se conserva (B) |
| Inicio automático | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `WinMac` | Se conserva el nombre (B). Se **repara** si queda huérfano (ver §4) |
| Instancia única y cierre | `Local\WinMac.Host[.Quit/.Reopen]`, `WinMac.Bar/Dock/Connect.SingleInstance` | Contrato entre procesos (D): no se cambia |
| Canal IPC (host ↔ módulos ↔ NotchWin) | tubería `WinMac.<SID>.<pid>` | Contrato con NotchWin (D): no se cambia |
| Clases de ventana (`WinMacBar`, `WinMacDock`, `WinMacHost`…) | internas; el Dock y la Barra se buscan entre sí por clase | (D) no se cambia |
| NotchWin | su propia configuración y el submódulo `external/winmac` | Sin cambios (D) |

No hay instalador (MSI/MSIX/EXE): el producto se distribuye como un `.zip` portable. No existe entrada en «Aplicaciones instaladas», ni claves `Uninstall`, ni accesos directos creados por WinMac; por eso no hay identificador de producto del actualizador que migrar (no hay actualizador).

## 2. Auditoría de referencias «WinMac» (≈3 100 apariciones, 440 archivos)
| Categoría | Qué es | Acción |
|---|---|---|
| **A. Identidad pública** | Textos visibles en cadenas de C# (ajustes, paneles, menús, bandeja, mensajes de error, diálogos, Acerca de, nombre de notificaciones y de ventanas) — 105 líneas en 46 archivos, más metadatos de ensamblado y scripts de usuario (desinstalación, empaquetado) | **Cambiado** a «Hatsu Desktop» (y «Hatsu Connect», «Hatsu Cleanup» para los subproductos) |
| **B. Identificador interno persistente** | Carpetas de datos, valor de inicio automático, nombres de archivo de configuración, nombres de ejecutables, nombres de proceso | **Conservado** (compatibilidad) |
| **C. Referencia histórica** | `docs/` anteriores (nombre en su época), comentarios XML, historial de commits, `NOTICE`/copyright del titular original | **Conservado** (el README explica la equivalencia; `NOTICE` añade «Hatsu Desktop (antes WinMac)» y mantiene el copyright) |
| **D. Contrato entre componentes** | Espacios de nombres y ensamblados `WinMac.*`, tubería IPC, mutex y eventos, clases de ventana, referencias del CI y de NotchWin | **Conservado**: solo se cambiaría con una migración explícita y probada |
La sustitución se hizo con un analizador de literales de cadena (no con reemplazo global): solo cambia el texto **dentro de cadenas con prosa**, y se protegen `WinMac.Algo`, `WinMac\ruta`, `WinMac/…`, `WinMacHost`, etc. Auditoría completa y reproducible: `tools/manual/Audit-Rebrand.ps1` (solo lectura).

## 3. Cambios públicos
- **Metadatos de los ejecutables** (lo que ve el Administrador de tareas y «Propiedades»): Producto «Hatsu Desktop»; descripción del archivo «Hatsu Desktop», «— Barra superior», «— Dock», «— Connect». Los archivos siguen llamándose `WinMac*.exe` (ver §6).
- **Icono**: icono provisional «H» (`assets/brand/`), incrustado en Host/Barra/Dock/Connect y usado por la bandeja del sistema (antes el genérico de Windows). El logotipo de la barra superior pasa de «W» a «H».
- **Ventanas y mensajes**: títulos («Ajustes de Hatsu Desktop»), menú de la bandeja («Salir de Hatsu Desktop»), panel de la bandeja, Acerca de, diálogos, avisos, notificaciones de la Isla y de la barra.
- **Paquete**: `HatsuDesktop-<versión>-<commit>-win-x64.zip` con carpeta interna `HatsuDesktop/`, `Uninstall-HatsuDesktop.ps1` y `VERSION.txt` «Hatsu Desktop … (antes WinMac)». El artefacto de CI pasa a `hatsu-desktop-paquete-<sha>`.
- **README y NOTICE**: ver arriba. No se tocó `LICENSE` ni `THIRD_PARTY.md`.
- **NotchWin**: sin cambios (solo contiene textos propios de la Isla; no muestra el nombre «WinMac» al usuario).

## 4. Estrategia de actualización desde una instalación WinMac
1. Cierra WinMac (`WinMac.exe --quit`), extrae el paquete nuevo (carpeta `HatsuDesktop`) donde quieras y ejecútalo. **Los ajustes se leen de las mismas carpetas** (`%APPDATA%\WinMac`, `%LOCALAPPDATA%\WinMac`): nada se copia ni se pierde, y las claves nuevas toman sus valores por defecto.
2. **Inicio automático**: es la misma clave (`Run\WinMac`). Si apuntaba a una carpeta que ya no existe (por haber borrado la antigua), al arrancar la nueva se **reescribe** apuntando a la actual (idempotente). Si apunta a otra instalación que sigue existiendo, **no se toca** (no se secuestra otra copia): desactívalo y vuelve a activarlo en Ajustes › General para cambiarlo.
3. No hay datos duplicados ni segundo producto: mismo identificador de datos, mismo valor de inicio, misma instancia única (`Local\WinMac.Host`): dos copias (antigua y nueva) **no** pueden ejecutarse a la vez.
4. Reversión: volver a ejecutar el paquete antiguo funciona (los datos no cambiaron de formato).
**No validada en un Windows real**: ver el checklist.

## 5. Pruebas
Automáticas (CI y Linux): compilación de todos los componentes; integración con NotchWin; suites existentes; `RebrandingTests` (metadatos del producto; icono válido con todos los tamaños; los identificadores B/D siguen EXACTAMENTE como estaban —contrato—; ningún texto público de ajustes/bandeja/paneles conserva «WinMac» fuera de rutas y archivos permitidos; migración de inicio automático con casos: sin valor, mismo ejecutable, huérfano, otra instalación viva, formato raro, idempotencia); carga de configuraciones antiguas sin pérdida de preferencias; el paquete, su reproducibilidad y su verificación (`Verify-Package.ps1`); desinstalación en el runner.
En entorno simulado: capturas de CI con datos de muestra. **En Windows real: nada todavía** (checklist de §7).

## 6. Decisiones que necesitan aprobación
1. **Renombrar los ejecutables** (`WinMac.exe` → `HatsuDesktop.exe`, etc.): afecta al inicio automático guardado, a scripts, a la desinstalación, a NotchWin (que busca procesos y rutas) y a los procesos de guardián. Propuesta para la Fase 2: renombrar con un lanzador/atajo de compatibilidad y migración del valor `Run`.
2. **Renombrar la carpeta de datos** (`WinMac` → `HatsuDesktop`): requeriría migrar con copia verificada y reversible; hoy no aporta valor visible.
3. Sub-marcas: «Hatsu Connect» / «Hatsu Cleanup» / «Hatsu Peripherals» frente a «Connect»/«Cleanup»/«Peripherals» a secas.
4. Logotipo **definitivo** y paleta (el actual es provisional).
5. Renombrar repositorio y URLs (propuesta: cambiar el nombre en GitHub —que redirige—, actualizar enlaces del README/CI/submódulo de NotchWin y la URL del submódulo `external/winmac`, y hacerlo cuando no haya PRs abiertas).
6. Si se quiere un **instalador real** (MSIX/WiX) con identidad `Hatsu Desktop`, definir ahora el identificador de producto/actualización para no crear un segundo producto después.

## 7. Checklist de validación manual en Windows
1. Instalar WinMac anterior, configurar Dock (anclas y orden), Barra, Isla, Peripherals y Cleanup; activar el inicio automático.
2. Cerrar WinMac; extraer el paquete `HatsuDesktop`; ejecutar. Comprobar que **todo** sigue igual y que no hay dos copias.
3. Reiniciar sesión: arranca una sola copia (la correcta). Borrar la carpeta antigua, reiniciar sesión: sigue arrancando una sola (autoreparación del inicio automático).
4. Administrador de tareas: «Hatsu Desktop» (y subprocesos «— Barra superior», «— Dock»); icono propio. Propiedades del `.exe`: Producto/Descripción.
5. Bandeja: icono «H», tooltip, menú («Ajustes de Hatsu Desktop…», «Salir de Hatsu Desktop»), panel.
6. Acerca de este PC, ajustes, diálogos de apagar/reiniciar, notificaciones: sin «WinMac» visible.
7. Isla: música, letras sincronizadas, controles; arranque y cierre coordinados; llamadas/HFP intactos.
8. Desinstalar con `Uninstall-HatsuDesktop.ps1` (sin y con `-RemoveData`).
