# 31 · Configuración unificada

Una sola ventana de Ajustes (host, `winmac.json` como única fuente de verdad) con todas las áreas:

| Página | Qué incluye |
|---|---|
| General | módulos, inicio automático |
| Barra superior | apariencia, comportamiento, Global Menu, Launchpad, **Capturas** (atajos, formato, portapapeles, carpeta), **Spotlight Pro y espacios de trabajo** |
| Panel y Tiempo | panel de control, tiempo |
| Notificaciones | centro de notificaciones |
| Isla | posición, vidrio, **multimedia y letras**, píldora, **Portapapeles** (límite, no guardar secretos, conservar cifrado), agentes, pestañas |
| Extensiones | instaladas con permisos y estado, activar/desactivar, desinstalar, instalar desde carpeta |

Archivos propios que no son ajustes (se editan a mano o desde Spotlight): `%APPDATA%\WinMac\workspaces.json`, `%APPDATA%\WinMac\spotlight-scripts.json`.

**Migración**: cada opción nueva tiene un valor por defecto seguro y un `winmac.json` de una versión anterior carga sin perder nada (prueba
`A_config_from_before_WinMac_2_loads_with_safe_defaults_for_every_new_option`): las letras y la posición se conservan, las pestañas nuevas se añaden al final, el
portapapeles empieza sin persistencia y sin guardar secretos, las capturas en la carpeta de Windows. Las importaciones únicas anteriores (bar.json, ajustes de NotchWin)
siguen igual y nunca modifican los archivos originales.
