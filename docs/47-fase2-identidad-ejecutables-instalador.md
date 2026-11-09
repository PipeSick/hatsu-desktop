# Hatsu Desktop — Fase 2: identidad, ejecutables y migración

Base: `573c645` (Fase 1). Rama `feat/hatsu-rebrand-phase2`. NotchWin sin cambios (pin `tools/notch-pin.txt` = `80bd29a`). Sin PR, merge ni release.

## 1. Icono Flux
Fuente única: `assets/brand/source/make_brand.py` (ver `assets/brand/README.md`). Se corrigió el master (halo oscuro, márgenes, `tray-32` mal etiquetado, variante de bandeja ≤24 px) sin rediseñar H, cinta ni cromática. Integrado en: ejecutables (`ApplicationIcon`), bandeja (`ExtractIconExW` del propio exe), panel principal y barra (bitmaps BGRA incrustados vía `BrandIcon`), Dock y Connect (icono del exe, que usa la barra de tareas), paquete portable (`HatsuDesktop.ico`). Acerca de y capturas visuales: **pendiente de verificar en Windows real**; CI solo comprueba incrustación y metadatos.

## 2. Nombres públicos
| Antes | Ahora |
|---|---|
| `WinMac.exe` | `HatsuDesktop.exe` |
| `WinMac.Bar.App.exe` | `HatsuDesktop.Bar.exe` |
| `WinMac.Dock.App.exe` | `HatsuDesktop.Dock.exe` |
| `WinMac.Connect.App.exe` | `HatsuDesktop.Connect.exe` |

Auditoría de dependencias del nombre (todas actualizadas o con soporte de ambos): `HostConfig.Known` (supervisor), `ForceQuitGuard` y `WindowArranger.OwnProcesses` y `SecurityDiagnostics` (aceptan ambos), `AppAudioRoutingClient`, scripts (`Package-WinMac`, `Build-Local`, `Uninstall-WinMac`, `Verify-Package`, `Test-Host`, `Test-Notch`), `ci.yml`. NotchWin no depende del nombre del host (IPC por pipe/tópicos).

**Conservados** (compatibilidad): carpetas `%APPDATA%\WinMac` y `%LOCALAPPDATA%\WinMac`, valor Run `WinMac`, mutex/eventos `Local\WinMac.*`, pipe `WinMac.<SID>.<pid>`, clases de ventana, ensamblados/namespaces `WinMac.*`, carpetas `src/WinMac.*`, tópicos IPC, nombre del repo.

## 3. Estrategia de actualización desde WinMac
- Sin lanzador de compatibilidad ni segundo exe residente: la instancia vieja y la nueva comparten mutex y evento de cierre.
- `HatsuDesktop.exe --replace-running`: pide cerrar a la instancia anterior (evento Quit), espera hasta 20 s al mutex; si no se libera, la nueva sale sin cambios (no hay duplicados).
- `tools/Migrate-FromWinMac.ps1`: simulación por defecto; `-Apply` hace la migración; `-RemoveLegacyFiles` solo borra los exe antiguos de esa carpeta. Nunca borra carpetas de datos ni carpetas antiguas.
- Reversión: volver a ejecutar el `WinMac.exe` antiguo; la configuración y el valor Run se conservan en el mismo formato.
- El paquete nuevo no incluye los exe antiguos.

## 4. Autostart (clave `Run\WinMac`)
`AutostartMigration.Assess` clasifica: NotConfigured, Current, Orphan (ruta que ya no existe), UpgradeInPlace (`WinMac.exe` de la misma carpeta), OtherInstall (otra instalación viva), Unreadable.
- Automático solo en Orphan y UpgradeInPlace; conserva argumentos y rutas con espacios; idempotente.
- OtherInstall: nunca por una ejecución de prueba; solo con adopción explícita (Ajustes → confirmación, o `--adopt-autostart`).
- Desactivado sigue desactivado: nunca se crea ni habilita el valor sin consentimiento.
Pruebas (`AutostartTransitionTests`): desactivado, ruta antigua existente/eliminada, ruta nueva activa, dos paquetes en ubicaciones distintas, exe renombrado, argumentos, espacios, repetida, reversión, acción de adopción.

## 5. Instalador: comparación y recomendación (solo diseño; no se instaló ni publicó nada)
| Criterio | WiX/MSI | MSIX | Alternativas (Inno Setup, ZIP+script) |
|---|---|---|---|
| Por usuario sin admin | Sí (`perUser`) | Sí | Sí |
| Actualización | Major upgrade con UpgradeCode estable | Automática (App Installer) | Reinstalar encima |
| Desinstalación limpia | Sí, estándar | Sí, aislada | Depende del script |
| Preservar `%APPDATA%\WinMac` | Sí (no se toca) | **Virtualiza** escrituras de AppData: riesgo para la compatibilidad con datos existentes | Sí |
| Autostart opcional y con consentimiento | Casilla propia | `startupTask` (el usuario lo habilita en Windows) | Casilla propia |
| Accesos directos | Sí | Sí | Sí |
| Firma | Opcional (aviso SmartScreen) | Obligatoria (certificado) | Opcional |
| Ocultar barra de tareas / ventanas shell | Sin restricciones | **Sandbox/capabilities** problemáticas para un shell-extender | Sin restricciones |
| Compatibilidad con ZIP portable | Convive | Convive | Es el mismo modelo |

**Recomendación:** mantener el ZIP portable como canal principal y, para un instalador, **WiX v4 (MSI per-user)** con UpgradeCode estable, sin autostart por defecto (casilla opcional desactivada), sin tocar datos y reutilizando `Migrate-FromWinMac.ps1`. MSIX queda descartado por la virtualización de AppData y las restricciones de un programa que manipula la barra de tareas. Inno Setup es la alternativa si se quiere menos complejidad. **Estado: sin prototipo; nada validado en Windows real.**

## 6. NotchWin
Sin cambios al submódulo/pin, al IPC, a letras ni a HFP. La detección de procesos no depende del nombre del host. CI de integración debe seguir verde (ver resultado de CI).

## 7. Checklist de validación manual (pendiente, requiere Windows real)
1. Icono Flux nítido en barra de tareas, bandeja (100–200 % DPI), Dock, barra, panel y Acerca de, en claro y oscuro.
2. Ejecutar `Migrate-FromWinMac.ps1` (simulación) desde una instalación WinMac real y revisar el plan; luego `-Apply`.
3. Con autostart activado en WinMac: tras actualizar, el valor apunta a `HatsuDesktop.exe`. Con autostart desactivado: sigue sin existir.
4. `--replace-running` con WinMac abierto: sin duplicados, sin cierre forzado.
5. Dos paquetes en carpetas distintas: no se cambia el inicio automático sin confirmar.
6. Reversión al `WinMac.exe` antiguo; datos intactos.
7. NotchWin/Dynamic Island, letras y llamadas siguen funcionando.

## 8. Riesgos y limitaciones
- Nada de esto está validado en hardware ni Windows real: CI/simulación ≠ validación física.
- Accesos directos, antivirus o scripts de usuarios que apunten a `WinMac.exe` dejarán de funcionar al borrar los exe antiguos (por eso no se borran por defecto).
- El master es raster 1024 px (no vectorial).
- Los identificadores internos siguen diciendo «WinMac»: es deliberado y está documentado.
