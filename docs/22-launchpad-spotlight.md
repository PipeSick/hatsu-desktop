# 22 · Launchpad, tecla Windows y Spotlight

## Launchpad (implementado)

**Tecla Windows.** Gancho de teclado de bajo nivel (`WH_KEYBOARD_LL`, API documentada; no inyecta código en ningún proceso) en un hilo propio del proceso de la barra.
Deja pasar **todas** las teclas. Solo cuando se pulsa y se suelta Windows **sola** (sin otra tecla ni clic en medio y en menos de 0,7 s) envía antes una tecla sin asignar
(VK 0xE8, la técnica de la «tecla máscara»): Windows lo interpreta como una combinación y no abre el menú Inicio; entonces se abre Launchpad.
- Atajos intactos: Win+E, Win+R, Win+L, Win+D, Win+Tab, Win+Shift+S, Win+números… pasan sin tocarse (`WinKeyTapTests`).
- Ctrl+Esc sigue abriendo el menú Inicio. Una pulsación larga de Windows deja el comportamiento de Windows.
- Las teclas inyectadas por programas no cuentan (accesibilidad, macros, escritorio remoto que reenvía teclas: no abren Launchpad por error).
- Recuperación: el gancho vive en el proceso de la barra; si la barra se cierra o falla, Windows vuelve a abrir Inicio sin hacer nada. Si Launchpad no puede abrirse, se abre el menú Inicio (Ctrl+Esc). Se desactiva en vivo en Ajustes → Barra superior → Launchpad.
- Limitación: Windows puede no entregar la tecla al gancho en pantallas seguras (UAC, Ctrl+Alt+Supr) y con ventanas de administrador si WinMac no lo es; ahí la tecla funciona como siempre.

**Índice de apps compartido** (`WinMac.Apps.Core/Catalog`, `WinMac.Apps.Win/AppsFolderReader.cs`): la carpeta virtual `shell:AppsFolder` («Todas las aplicaciones», la misma del menú Inicio): accesos directos Win32 del usuario y de todos, y apps MSIX/UWP. Identificador estable = nombre de análisis (AUMID en las empaquetadas). Se quitan desinstaladores, léames y enlaces web. Se relee al arrancar, al cambiar las carpetas del menú Inicio (vigiladas) y al abrir si pasó un minuto (las apps empaquetadas no tocan esas carpetas).
- Lanzamiento: `explorer.exe shell:AppsFolder\<id>` (como el menú Inicio; la app no hereda nada de WinMac).
- Iconos: `ShellIconProvider` (el del Dock) con caché en disco `%LOCALAPPDATA%\WinMac\icon-cache-launcher`.
- Preferencias (`%LOCALAPPDATA%\WinMac\launcher.json`): orden, ocultas, veces abierta (ordena la búsqueda).

**Interfaz** (`WinMac.Bar.Win/Launcher`): pantalla completa en el monitor del ratón, fondo de pantalla desenfocado, campo «Buscar», cuadrícula 7×5 (menos en pantallas pequeñas), puntos de página, aparición con fundido (respeta «Mostrar animaciones»).
Teclado: escribir busca; flechas, Re Pág/Av Pág, Inicio/Fin; Intro abre; Esc borra la búsqueda o cierra. Ratón: clic abre; clic en el fondo cierra; rueda cambia de página; arrastrar reordena; clic derecho: Abrir, Mover al principio, Ocultar de Launchpad; en el fondo: Mostrar apps ocultas, Restablecer el orden. Volver a pulsar Windows lo cierra.
`WinMac.Bar.App --launchpad` lo abre o cierra desde fuera (atajos propios, pruebas).

**Validación.** Automática: `LauncherTests.cs` (Bar.Core, 19). CI de Windows (`Test-Launchpad.ps1`): barra real, índice real, abrir, escribir «notepad» + Intro abre el Bloc de notas, Esc cierra, captura `launchpad-real.png`.
Pendiente en el PC: la tecla Windows física (el CI no puede: las teclas inyectadas se ignoran a propósito), aspecto real, varias pantallas, apps de la Tienda.
