# 23 · Global Menu (opción A)

Los menús de la app activa aparecen en la barra superior junto a su nombre; **los originales siguen en la ventana de la app** (no se mueven, ocultan ni modifican).
Se activa en Ajustes → Barra superior → Comportamiento → «Menús de la app activa en la barra» (apagado por defecto, como antes).

| Tipo de app | Cómo se descubren | Qué hace un clic | Estado |
|---|---|---|---|
| Win32 clásica con barra de menús (`HMENU`: Bloc de notas clásico, Paint clásico, regedit, mmc, muchas apps de escritorio) | `GetMenu` + árbol completo con `GetMenuItemInfo`; antes de leer cada submenú se envía `WM_INITMENUPOPUP` (como Windows al abrirlo, 250 ms de tope) para que la app actualice sus entradas | Desplegable con el estilo de WinMac: atajo a la derecha, ✓ marcadas, atenuadas deshabilitadas, separadores, submenús con «›» (y «‹» para volver). La orden se ejecuta con `WM_COMMAND`, como si se eligiera en su menú | implementado |
| Win32 que dibuja sus menús (owner-draw) | se detecta y no se lee el texto | se abre el menú nativo de la propia app bajo la barra (`TrackPopupMenu` con su `HMENU`) | implementado |
| WinUI / WPF / Electron (VS Code) y otras con barra de menús accesible | UI Automation (COM, sin dependencias): `MenuBar` → `MenuItem` de nivel superior, en segundo plano con 2 s de tope | se pide a la app que abra SU menú (`ExpandCollapsePattern.Expand`); el desplegable es el suyo, en su ventana | implementado (submenús con el estilo de WinMac: no) |
| Sin barra de menús (Office con cinta, navegadores, Explorador, apps sin menús) | — | solo el nombre de la app; no se inventan «Archivo» ni «Edición» | fallback |

Sin inyección de código, sin ganchos en otros procesos, sin cambiar interfaces de terceros. La lectura nunca bloquea la barra: la de UI Automation va en segundo plano y los resultados se guardan por ventana 8 s. Una app colgada no congela WinMac (`SMTO_ABORTIFHUNG`, topes de tiempo).

Validación: `AppMenuTests` (Bar.Core: texto y atajos tal como vienen del menú, desplegable, submenús, deshabilitadas no se ejecutan, sin menús no se inventa nada). CI (`Test-Launchpad.ps1`): barra real con el ajuste activado + Bloc de notas real → la barra registra sus menús. Pendiente en el PC: clic en cada tipo de app (Bloc de notas, Paint, VS Code, una app WPF), estado deshabilitado en vivo, dos monitores.
