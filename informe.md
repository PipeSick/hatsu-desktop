# Capturas Store · informe automático

Commit: de8d91fcd00f7818eeea63a44884913f9db74bd3 · 2026-11-05 05:13:38 UTC-local del runner

## Pantalla
Modos admitidos por el adaptador: 1024x768, 1152x864, 1280x1024, 1280x720, 1280x800, 1366x768, 1440x900, 1600x1200, 1600x900, 1680x1050, 1920x1080, 640x480, 800x600
- probar 2560x1440 (CDS_TEST) → -2
- probar 1920x1080 (CDS_TEST) → 0
  aplicar → 0
Pantalla tras el ajuste: **1920x1080** (aplicado: 1920x1080)
Fondo de pantalla de Windows: C:\Windows\Web\4K\Wallpaper\Windows\img0_1920x1200.jpg → True

Configuración aislada: C:\Users\runneradmin\AppData\Local\Temp\hatsu-store-7e0bed\winmac.json (módulos: Barra, Dock y Hatsu Notch activados; barra de tareas de Windows oculta —función real de Hatsu—; Connect desactivado: no hay iPhone)

## Medios
Chrome (pid 6108) reproduciendo el tono: ventana principal «»
Vista del escritorio: 65736

## Procesos reales
Host, Barra, Dock y Hatsu Notch en estado Running: **True**
- HatsuDesktop : pid 6500, 38 MB, ruta D:\a\hatsu-desktop\hatsu-desktop\dist\WinMac\HatsuDesktop.exe
- HatsuDesktop.Bar : pid 2296, 163 MB, ruta D:\a\hatsu-desktop\hatsu-desktop\dist\WinMac\HatsuDesktop.Bar.exe
- HatsuDesktop.Dock : pid 6848, 120 MB, ruta D:\a\hatsu-desktop\hatsu-desktop\dist\WinMac\HatsuDesktop.Dock.exe
- NotchWin.App : pid 2968, 128 MB, ruta D:\a\hatsu-desktop\hatsu-desktop\dist\WinMac\NotchWin\NotchWin.App.exe
- Hatsu Notch, ventanas: WinUIDesktopWin32WindowClass 810,0 300x28; 
- Dock, ventanas: WinMacDock 294,649 1332x449; 
- Barra, ventanas: WinMacBar 0,0 1920x28; 

## Ventanas de las aplicaciones: Bloc de notas=66060 · Calculadora=262604 · Explorador=66132
- Explorador: 852,70 1070x930
- Bloc de notas: 40,70 800x450
- Calculadora: 70,545 340x430

## Capturas (pantalla real completa)
- **00-diagnostico-estado-inicial.png** (1920x1080, 490 KB): estado tras arrancar (diagnóstico: no es una captura para la Store)
- **01-escritorio.png** (1920x1080, 490 KB): Escritorio con Barra, Dock y Hatsu Notch (reposo) y aplicaciones reales abiertas
- Hatsu Notch antes de expandir: WinUIDesktopWin32WindowClass 810,0 300x28; 
- Hatsu Notch tras el clic: WinUIDesktopWin32WindowClass 700,0 520x358; 
- **02-hatsu-notch-expandido.png** (1920x1080, 496 KB): Hatsu Notch expandido con la reproducción multimedia real (tono generado, sesión de Chrome)
- **02b-hatsu-notch-contraido.png** (1920x1080, 490 KB): Hatsu Notch contraído (reproduciendo)
- **03-dock.png** (1920x1080, 465 KB): Dock con el ratón sobre el icono del Explorador (ampliación y vista previa de ventana)
- **03b-dock-reposo.png** (1920x1080, 490 KB): Dock en reposo
- **05-diag-menu-hatsu.png** (1920x1080, 508 KB): diagnóstico: menú de Hatsu de la Barra abierto con un clic real en el logotipo
- Ventana de Ajustes visible: False
  (la visibilidad se comprobó con IsWindowVisible; ventana por clase: 131866)
  posición: 522,230 876x619
- **04-ajustes.png** (1920x1080, 445 KB): Ajustes de Hatsu Desktop (ventana real)

## Registro del host (últimas líneas)
    2026-11-05 05:14:05.667 [host] iniciado (pid 7644); configuración: C:\Users\runneradmin\AppData\Local\Temp\hatsu-store-7e0bed\winmac.json; canal: WinMac.S-1-5-21-3289802278-1561746829-1498425070-500.2
    2026-11-05 05:14:05.789 [host] icono de bandeja creado
    2026-11-05 05:14:05.830 [barra de tareas] guardián iniciado (pid 6500): devuelve la barra si Hatsu Desktop se cierra de golpe o deja de responder
    2026-11-05 05:14:05.925 [bar] Starting
    2026-11-05 05:14:06.011 [dock] Starting
    2026-11-05 05:14:06.132 [notch] Starting
    2026-11-05 05:14:06.226 [host] módulos lanzados: bar=Starting, dock=Starting, notch=Starting, connect=Disabled
    2026-11-05 05:14:06.314 [host] no se pudo crear el icono de la bandeja (el host sigue funcionando)
    2026-11-05 05:14:06.316 [barra de tareas] Explorer recreó la barra de tareas: se volverá a ocultar
    2026-11-05 05:14:06.517 [dock] Running
    2026-11-05 05:14:06.628 [bar] Running
    2026-11-05 05:14:07.259 [notch] Running
    2026-11-05 05:14:09.125 [barra de tareas] oculta, pero el área de trabajo aún termina 86 px antes del borde inferior (otra AppBar visible, como el Dock con «Reservar espacio», o Explorer sin liberar su franja)
    2026-11-05 05:14:09.125 [barra de tareas] oculta en 1 pantalla(s)
    2026-11-05 05:14:16.319 [host] no se pudo crear el icono de la bandeja (el host sigue funcionando)
    2026-11-05 05:14:16.320 [barra de tareas] Explorer recreó la barra de tareas: se volverá a ocultar
    2026-11-05 05:14:45.246 [host] ajustes: primer clic recibido en (112,548) con escala 1.00

## Versión de los binarios
- HatsuDesktop.dll · 2.0.1.0 · SHA-256 0CF345782AFA73DB…
- HatsuDesktop.Bar.exe · 2.0.1.0 · SHA-256 D2996120B270D798…
- HatsuDesktop.Dock.exe · 2.0.1.0 · SHA-256 9E2B12A6ED2CB52F…
- NotchWin\NotchWin.App.exe · 0.1.0.0 · SHA-256 051BFC2E077F32CC…
