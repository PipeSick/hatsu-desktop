# Estado de la sesión (traspaso para la siguiente conversación)

Actualizado 2026-11-02. Léelo primero si retomas el trabajo tras un `/clear`.

## Repos y ramas
- **WinMac:** `PipeSick/win11-macos`, rama `claude/relaxed-turing-cijzko`.
- **NotchWin (la Isla):** `PipeSick/notch-win`, rama `claude/relaxed-turing-cijzko` (basada en `claude/winmac-integration`). WinMac fija el commit de NotchWin en `tools/notch-pin.txt` (hoy `1101d06`).
- En el PC del usuario: WinMac en `C:\Users\felip\Desktop\win11-macos\win11-macos`; NotchWin en `C:\nw`. Se compila con `powershell -ExecutionPolicy Bypass -File tools\Build-Local.ps1` y se abre con `.\dist\WinMac\WinMac.exe` (`--quit` para cerrar).
- El usuario está ahora en un **Mac** con el PC Windows apagado: **solo se puede validar en el CI de Windows**.

## Cómo trabajar en este entorno
- El SDK de .NET 10 se instala con `apt-get install -y dotnet-sdk-10.0`. La lógica pura y sus pruebas corren en Linux; los proyectos de Windows compilan aquí (`EnableWindowsTargeting`). La app WinUI de NotchWin **no** compila aquí (su compilador XAML solo funciona en Windows): la valida su CI.
- El CI corre en cada push a `claude/**`. Los artefactos no se pueden descargar desde aquí (el proxy bloquea el almacenamiento de GitHub): las capturas se leen del **registro del job** (líneas `PREVIEW_B64` / `PVB64|`, decodificar con Python).
- Cuidado: un comentario `// …` al final de una línea editada con Python se ha tragado código más de una vez. Comprobar siempre compilando.
- El usuario usa Windows por **Sunshine/Moonlight**: nunca cambiar resolución, monitor principal, red ni audio, ni reiniciar.

## Hecho en esta sesión (todo subido y con CI en verde salvo lo indicado)
- **Discos:** diagnóstico SMART/NVMe; aviso de errores registrados por Windows (7 días). El disco D: (Kingston, firmware CBG04418) tuvo 1190 errores de E/S el 29/10 y 3 reinicios de la controladora; se recuperó tras apagar del todo. Al usuario se le recomendó hacer copia de D:.
- **Centro de Notificaciones** (`docs/20-centro-de-notificaciones.md`): se abre con el reloj. Lee las notificaciones reales con `UserNotificationListener` (medido en el PC: 20 notificaciones de 5 apps, iconos 19 de 20; el evento de cambios **no** llega, así que relee cada 4 s con el panel abierto). Widgets: calendario, tiempo y batería.
- **Isla (NotchWin):** ya no se queda entre pantallas (rectángulo vivo del monitor y vigilancia cada 3 s, confirmado por el usuario); dentro de WinMac cerrada tiene el alto de la barra (28 DIP), el vidrio de la barra, sin «hombros» y con sombra suave; abierta, vidrio estilo Liquid Glass.
- **Dock:** los clics en su zona transparente llegan a las apps de debajo (región de ventana; confirmado por el usuario); ya no muestra la ventana oculta de dwm.exe; **un Dock por pantalla**; registro de por qué una app se queda sin icono (caso de Edge sin resolver: no hay datos todavía).
- **Menú de la manzana** con aspecto de macOS (panel propio).
- **Apps en segundo plano (bandeja):** WinMac recibe los iconos de la bandeja con su propia ventana `Shell_TrayWnd` y lo **reenvía todo a Explorer** (`TrayHost`). En la barra hay **un botón con flecha abajo** (arriba con el desplegable abierto) que abre una **cuadrícula de iconos como la de Windows**: clic = abrir la app, clic derecho = su menú. Un clic fuera, Esc o volver a pulsar lo cierran.

## Pendiente ahora mismo
- Último push `d309ed9`: arregla el recorte de la vista previa del job `windows-tray-capture`. En el run anterior (`9e633f6`) la prueba **ya pasó lo importante**: estado interno con los 3 iconos de prueba, flecha pulsada con el ratón (x=750, y=14) y **3 de 3 iconos visibles en el desplegable** de la captura real. Falló solo al recortar la vista previa. Hay que comprobar el run de `d309ed9`, extraer `PREVIEW_B64 tray-real` del registro y mirar la imagen.
- Validar en el PC del usuario (cuando vuelva a Windows): clics en los iconos del desplegable con apps reales, Dock en dos pantallas, menú de la manzana, Isla y Edge sin icono (`dock.log` → «sin icono»).
