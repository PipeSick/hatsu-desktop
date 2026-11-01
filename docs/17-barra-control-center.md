# Barra superior y Control Center: funciones reales (F5)

Estado: **implementado en local, sin publicar y SIN ejecutar en Windows real.** Lo que aquí se llama «compilado» está comprobado solo por el compilador (Linux, `EnableWindowsTargeting`); lo «probado» tiene pruebas automáticas que corren en Linux. Nada de esto se ha visto funcionando en tu PC todavía.

## Arquitectura
| Capa | Proyecto | Contenido | Probado |
|---|---|---|---|
| Núcleo puro | `WinMac.System.Core` | `ServiceResult` (Ok/Unavailable/Denied/Failed), modelos, contratos, `ForceQuitGuard`, lector `.ics` y rejilla de calendario | Sí (20 tests) |
| Adaptadores Windows | `WinMac.System.Win` | Audio, Wi-Fi, Bluetooth, brillo, energía, apariencia, pantallas, sesión, menús de app | Solo compilado |
| Modelo de la Barra | `WinMac.Bar.Core` | `BarConfig` (parches), elementos, paneles (`PanelBuilder`/`PanelController`) | Sí (57 tests) |
| Interfaz | `WinMac.Bar.Win` | Ventana-popover D2D, `SystemHub` (lecturas sin bloquear la interfaz), menú Apple, menús de app | Solo compilado |
| Configuración central | `WinMac.Host.Core` / `WinMac.Host` | `config.bar` retenido, `config.bar.set` solo desde el módulo `bar`, migración única de `bar.json`, grupo «Barra superior» en Ajustes | Sí (Host.Core, 67 tests); ventana solo compilada |

Regla: ningún control finge. Si el equipo no lo soporta, el control aparece **deshabilitado con su motivo** (o se oculta desde Ajustes).

## Funciones
| Función | Cómo | Límite real |
|---|---|---|
| Menú Apple | Bloquear, Suspender, Reiniciar, Apagar, Cerrar sesión, Forzar salida, Ajustes; confirmación en lo destructivo (configurable; «Forzar salida» siempre pregunta) | Se confirma con un diálogo de Windows |
| Wi-Fi | Estado, SSID, señal, lista de redes, activar/desactivar radio, conectar **solo a perfiles ya guardados** | No se piden contraseñas ni se crean perfiles; offsets de `wlanapi` fijos (sin ejecutar) |
| Bluetooth | Estado y radio (WinRT `Radios`), dispositivos emparejados | Windows no permite conectar/desconectar un dispositivo concreto sin APIs no oficiales: solo se listan |
| Sonido | Volumen, silencio, lista de salidas, cambio de salida | El cambio usa `IPolicyConfig` (**no oficial**), aislado, con sondeo previo, verificación y reversión; si no está disponible, abre Configuración de sonido |
| Brillo | DDC/CI por monitor (dxva2) y WMI para pantalla interna | Muchos monitores no exponen DDC/CI; WMI interno nunca probado en portátil |
| Energía | Batería, estado, modo (overlay de `powrprof`) | El modo depende de que el equipo lo soporte |
| Apariencia | Claro/oscuro por `HKCU\...\Personalize` + `WM_SETTINGCHANGE`, sin admin | Solo cambia las claves de tema; no toca fondo ni acento |
| Pantallas | Duplicar/extender (`SetDisplayConfig`) | Sin reorganizar monitores |
| Reloj y calendario | Desplegable con rejilla y eventos del `.ics` configurado | Solo lectura; el lector es el MISMO de la Isla (`WinMac.Calendar`) |
| Notificaciones | Abre el centro de notificaciones nativo (Win+N) | No se lee su contenido; Windows no lo permite |
| Menús de app (experimental) | Lee el `HMENU` de la app activa y ejecuta la orden con `WM_COMMAND` | Solo Win32 con menú estándar; sin dibujo propio (owner-draw) ni apps modernas; apagado por defecto |
| Ajustes | Todo en la ventana de WinMac, con efecto en vivo | `Barra superior` en la columna derecha |

## No viable en Windows (con alternativa)
- Menú global de macOS para cualquier app: solo menús Win32 estándar (experimental).
- Foco/No molestar, Night Shift/True Tone, contenido de notificaciones: se abre el panel nativo de Windows.
- Conectar un dispositivo Bluetooth concreto: abrir Configuración de Bluetooth.
- Cambio de salida de audio oficial: no existe; se usa la vía no oficial con reversión o se abre Configuración.

## Medición automática
`WinMac.Bar.App --selftest-system` informa qué permite **este** equipo (sin cambiar nada). En CI corre como informativo: el runner no tiene Wi-Fi, Bluetooth ni brillo.

## Guía de validación en tu Windows (manual)
1. Ejecuta `WinMac.Bar.App.exe --selftest-system` y revisa el listado (qué controles dice soportar).
2. Abre WinMac y la Barra: aparecen Wi-Fi, volumen, batería, Bluetooth y Control Center.
3. Volumen/silencio: mueve el deslizador y confirma que Windows lo sigue (y al revés). Cambia de salida y vuelve.
4. Wi-Fi: abre la lista; conecta a una red **ya guardada**. Apaga/enciende la radio.
5. Bluetooth: enciende/apaga la radio; revisa la lista de emparejados.
6. Brillo: con 2 monitores, comprueba cuál responde (DDC/CI). En portátil, el interno.
7. Apariencia: alterna claro/oscuro; no debe pedir admin.
8. Pantallas: duplicar/extender y volver.
9. Menú Apple: **con cuidado** Suspender/Reiniciar/Apagar (la confirmación debe aparecer).
10. Calendario: configura un `.ics` en Ajustes y revisa los eventos del día.
11. Menús de app: actívalos en Ajustes (experimental) y prueba con el Bloc de notas.
12. Ajustes → «Barra superior»: cada opción debe verse al instante en la Barra.
