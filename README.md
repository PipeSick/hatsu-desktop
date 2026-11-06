# Hatsu Desktop

Experiencia de escritorio inspirada en macOS Tahoe para **Windows 11**: barra superior, Dock, Launchpad, Spotlight, centro de notificaciones, una isla informativa (**Hatsu Notch**) y un conjunto de módulos de productividad. Código abierto bajo **Apache-2.0** ([`LICENSE`](LICENSE) · [`NOTICE`](NOTICE) · [`THIRD_PARTY.md`](THIRD_PARTY.md)).

> **Estado: en desarrollo activo (versión 2.0.x).** Lo lógico se prueba en cada commit en el CI; lo gráfico, la entrada y el hardware se validan a mano en un Windows real, y cada documento dice qué está validado y qué no.
>
> **Hatsu Desktop es la evolución de WinMac.** Antes se llamaba «WinMac». Por compatibilidad se conservan los identificadores internos (`WinMac*.exe`, `%APPDATA%\WinMac`, canales entre componentes). Ver [`docs/46`](docs/46-rebranding-hatsu-desktop.md). El repositorio antes era `PipeSick/win11-macos` ([`docs/54`](docs/54-migracion-repositorio-hatsu-desktop.md)).

## Qué incluye

| Área | Qué hace | Documento |
|---|---|---|
| **Barra superior** | Menú de la app activa (Global Menu), reloj, batería, Control Center, panel multifunción con Tiempo, Limpieza, Periféricos y VPN | [12](docs/12-barra-superior.md) · [17](docs/17-barra-control-center.md) · [18](docs/18-panel-multifuncion-y-tiempo.md) · [23](docs/23-global-menu.md) |
| **Dock** | Iconos reales, anclado, vista previa de ventanas (con × para cerrar cada una), un Dock por monitor, ocultamiento automático | [10](docs/10-dock-funcional.md) |
| **Launchpad y Spotlight** | Búsqueda con vista previa, proveedores y acciones | [22](docs/22-launchpad-spotlight.md) · [27](docs/27-spotlight-pro.md) |
| **Centro de notificaciones** | Notificaciones reales de Windows y widgets | [20](docs/20-centro-de-notificaciones.md) |
| **Hatsu Notch (la Isla)** | Multimedia, volumen, brillo, notificaciones, portapapeles, estantería (Shelf+) y centro de actividades. Repositorio aparte: `PipeSick/notch-win` (se fija en [`tools/notch-pin.txt`](tools/notch-pin.txt)) | [21](docs/21-isla-informativa.md) · [25](docs/25-isla-2-centro-de-actividades.md) · [28](docs/28-portapapeles.md) |
| **Capturas y grabación** | Capturas, galería, editor y grabación | [26](docs/26-capturas-editor-grabacion.md) |
| **Espacios de trabajo y extensiones** | Guardar y restaurar la distribución de ventanas; API de extensiones v1 | [29](docs/29-espacios-de-trabajo.md) · [30](docs/30-extensiones.md) |
| **Connect** | Batería, estado y notificaciones del iPhone; investigación de llamadas | [40](docs/40-winmac-connect-fases-1-2.md) · [55–62](docs) |
| **Módulos de productividad** | Mission Control, Snap, Live Text, Spotlight Actions, Gestures y Shelf+; cada uno se enciende y apaga por separado en *Ajustes › Módulos* | [63](docs/63-modulos-de-productividad.md) |
| **Instalación** | Asistente de primera ejecución, MSI y MSIX (Microsoft Store), versión portable | [48](docs/48-fase3-instalador-onboarding.md) · [52](docs/52-msix-microsoft-store.md) · [53](docs/53-primer-envio-microsoft-store.md) |

Todo se configura desde una sola ventana de Ajustes ([`docs/31`](docs/31-configuracion-unificada.md)) y `winmac.json` es la única fuente de verdad.

## Novedades

Resumen en [`CHANGELOG.md`](CHANGELOG.md). Lo último (rama de trabajo, **pendiente de validar en Windows real**):
- Seis módulos de productividad nuevos, con interruptor individual, atajos configurables con captura de teclado y estado de salud en Ajustes.
- Hatsu Notch: banner de descargas que se ajusta al contenido, clic en una notificación abre la app que la envió, Shelf+.
- Dock: botón × en cada ventana de la vista previa; «Reservar espacio» y la vista previa se aplican sin reiniciar.
- VPN en la barra (opcional): ver el estado y conectar o desconectar las VPN de Windows desde un panel.
- Limpieza con panel rediseñado; icono propio de Hatsu en la barra y el panel.
- Gestures permite abrir tus aplicaciones o páginas web favoritas con una esquina, un botón del ratón o un atajo; Spotlight puede acomodar las ventanas abiertas con una distribución de Snap.

## Probar y compilar

- **Paquete del CI:** cada push genera el artifact `hatsu-desktop-paquete-<commit>` (ZIP portable). Descomprímelo en una carpeta aparte y cierra antes cualquier Hatsu instalado (bandeja › Salir). Los binarios del CI **no están firmados**: con *Smart App Control* activado Windows puede bloquearlos ([`docs/50`](docs/50-requisitos-distribucion-smart-app-control.md)).
- **Compilar en local (Windows 11, .NET 10):** `powershell -ExecutionPolicy Bypass -File tools\Build-Local.ps1` → `dist\WinMac`.
- **Pruebas:** `dotnet test src/<Proyecto>.Tests` (la lógica pura corre también en Linux con `-p:EnableWindowsTargeting=true`).
- **Pasos de prueba manual:** [`docs/INSTRUCCIONES-PRUEBAS.md`](docs/INSTRUCCIONES-PRUEBAS.md) · pendiente de hardware físico: [`docs/PENDIENTE-WINDOWS-FISICO.md`](docs/PENDIENTE-WINDOWS-FISICO.md).
- **Desinstalar / restaurar la barra de tareas:** `tools/Uninstall-WinMac.ps1`, `tools/Restore-Taskbar.ps1`.

## Estructura del repositorio

| Carpeta | Contenido |
|---|---|
| `src/` | Código: `Host` (supervisor y Ajustes), `Bar`, `Dock`, `Connect`, `Apps`, `System`, `Ipc`, `Extensions.Sdk` y sus pruebas |
| `docs/` | Documentación; el índice por tema está en [`docs/README.md`](docs/README.md) |
| `installer/` | MSI (WiX), MSIX y firma |
| `tools/` | Scripts de compilación, empaquetado, diagnóstico y CI |
| `extensions/` | Extensiones de demostración |
| `assets/brand/` | Identidad visual ([`README`](assets/brand/README.md)) |
| `spikes/` | Prototipos de la Fase 1 (históricos) |
| `.github/workflows/` | CI (`ci.yml`: pruebas, informes, artifacts temporales; **no publica versiones**) y publicación de la política de privacidad |

## Privacidad

Sin telemetría. La política está en [`docs/privacy/`](docs/privacy/POLITICA-DE-PRIVACIDAD.md) (español) y [en inglés](docs/privacy/PRIVACY-POLICY.en.md).

## Contribuir

Ver [`CONTRIBUTING.md`](CONTRIBUTING.md).
