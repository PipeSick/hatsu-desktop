# Barra superior y Control Center: funciones reales (F5)

Estado: **implementado; parte probada en un PC real de Windows 11, parte solo compilada y con pruebas automáticas.** Cada afirmación lleva su etiqueta:

- **PROBADO EN PC** = Felipe lo ejecutó en su equipo y lo vio funcionar.
- **PRUEBA AUTOMÁTICA** = cubierto por pruebas que corren en CI (lógica pura, sin hardware).
- **SOLO COMPILA** = compila (y el CI de Windows arranca el programa), pero nadie ha visto ese comportamiento en un equipo real.

## Arquitectura
| Capa | Proyecto | Contenido | Verificación |
|---|---|---|---|
| Núcleo puro | `WinMac.System.Core` | `ServiceResult` (Ok/Unavailable/Denied/Failed), modelos, contratos, `ForceQuitGuard`, lector `.ics` compartido con la Isla, rejilla de calendario | PRUEBA AUTOMÁTICA (20) |
| Adaptadores Windows | `WinMac.System.Win` | Audio, Wi-Fi, Bluetooth, brillo, energía, apariencia, pantallas, sesión, menús de app | Ver tabla de funciones |
| Lógica de la Barra | `WinMac.Bar.Core` | `BarConfig` (parches), elementos, paneles, **`SystemHub`**, **`PanelOperations`** (acciones de los paneles), **`MonitorPlan`/`TopologyWatcher`/`PopoverPlacement`** | PRUEBA AUTOMÁTICA (113) |
| Interfaz | `WinMac.Bar.Win` | `BarHost` (orquestador), **`BarWindow` (una por monitor)**, popover D2D, menú Apple, menús de app, enumerador de monitores | SOLO COMPILA + CI de Windows (arranque, `--selftest*`) |
| Configuración central | `WinMac.Host.Core` / `WinMac.Host` | `config.bar` retenido, `config.bar.set` solo desde el módulo `bar`, migración única de `bar.json`, grupo «Barra superior» en Ajustes | PRUEBA AUTOMÁTICA (75) + CI de Windows (migración real, paso `MIG`) |

Regla: ningún control finge. Si el equipo no lo soporta, el control aparece **deshabilitado con su motivo** (o se oculta desde Ajustes).

## Una Barra por monitor
- **Una ventana por monitor activo** (`BarWindow`), cada una con su propia superficie, su AppBar y su recorte, a la resolución, posición y escalado DPI de **ese** monitor. Comparten configuración, estado del sistema, panel desplegable y menús (`BarHost`).
- **Identificador estable:** la ruta del dispositivo del monitor (EDID/PnP), no el número de pantalla; si dos coinciden se desambiguan (`#2`). Así cada barra sigue a SU monitor aunque Windows renumere las salidas. PRUEBA AUTOMÁTICA.
- **Modo espejo (duplicar):** monitores con los mismos límites cuentan como uno → **una sola barra**, nunca repetidas. PRUEBA AUTOMÁTICA.
- **Cambios de topología sin reiniciar:** `WM_DISPLAYCHANGE` + revisión periódica (por si un aviso se pierde). Se espera 500 ms a que Windows termine de acomodar las pantallas antes de actuar; luego se **crean** las barras de monitores nuevos, se **recolocan** las que cambiaron de resolución/posición/DPI/principal y se **liberan** (AppBar, superficie, ventana) las de monitores desconectados. El área de trabajo no entra en la comparación (la propia barra la cambia al reservar su espacio). PRUEBA AUTOMÁTICA (lógica); el efecto real, SOLO COMPILA.
- **Independencia:** si la barra de un monitor falla al dibujar, se aísla y se recrea esa sola; las demás siguen (tras 5 fallos seguidos se deja caer el proceso para que el host lo reinicie limpio).
- **Control Center y menús en el monitor pulsado:** el panel nace bajo la barra donde se hizo clic, escalado al DPI de ese monitor, y se mantiene entero dentro de él (también con monitores a la izquierda del principal, coordenadas negativas). Un clic en el mismo icono de OTRA barra lo abre allí. El menú Apple y los menús de app nacen bajo la barra pulsada. PRUEBA AUTOMÁTICA (colocación); SOLO COMPILA (ventanas).
- **Pantalla completa por monitor:** una app a pantalla completa en un monitor oculta solo la barra de ese monitor.
- **Isla:** cada barra recorta SU ventana donde un hueco de la Isla la toca; un hueco de otro monitor no recorta nada (se interseca con la ventana de cada barra). Al cerrarse o caer la Isla el host publica «sin huecos» (por corte del canal **y** por estado del supervisor) y todas las barras recuperan su ventana completa. PRUEBA AUTOMÁTICA.

## Funciones
| Función | Cómo | Estado real |
|---|---|---|
| Menú Apple | Bloquear, Suspender, Reiniciar, Apagar, Cerrar sesión, Forzar salida, Ajustes; confirmación en lo destructivo (configurable; «Forzar salida» siempre pregunta) | SOLO COMPILA (las acciones destructivas nunca se han probado) |
| Wi-Fi: lectura | Estado, SSID, señal, lista de redes, potencia (dBm) y banda | **PROBADO EN PC**: coincide con `netsh` (PipeSick, 5 GHz, ≈-55 dBm, 28 redes) |
| Wi-Fi: conectar / radio | Solo a **perfiles ya guardados**; radio con WinRT | SOLO COMPILA (no probado: cortaría la sesión remota) |
| Wi-Fi: desplegable | Con la opción activa, tocar una red muestra señal, dBm, banda y seguridad, con la acción dentro | **PROBADO EN PC** (se ve; ajustado tras ver texto cortado) + PRUEBA AUTOMÁTICA |
| Bluetooth | Estado, radio y dispositivos emparejados | **PROBADO EN PC** (activar desde la Barra y listar dispositivos) |
| Sonido: volumen y silencio | Deslizador y botón | **PROBADO EN PC** |
| Sonido: cambio de salida | `IPolicyConfig` (**no oficial**), aislado, con sondeo, verificación y reversión | Se activa y la Barra responde (**PROBADO EN PC**); **no se pudo oír** si suena donde corresponde (Moonlight) |
| Brillo | DDC/CI por monitor (dxva2) y WMI para pantalla interna | **No disponible** en el PC probado: DDC/CI apagado en los monitores (activarlo en su menú OSD). Sin probar con DDC/CI. |
| Modo de energía | Overlay de `powrprof` | **PROBADO EN PC** (cambia al instante); el reflejo en la Barra era lento y se corrigió: **falta reconfirmar** |
| Apariencia | Claro/oscuro por `HKCU\...\Personalize` + `WM_SETTINGCHANGE`, sin admin | **PROBADO EN PC** |
| Pantallas | Duplicar/extender (`SetDisplayConfig`, igual que Win+P) | El panel abre; al cambiar de modo la **Isla quedó entre las dos pantallas** (arreglada en NotchWin, **falta reconfirmar**). Barra por monitor: SOLO COMPILA |
| Reloj y calendario | Desplegable con rejilla y eventos del `.ics` configurado (solo lectura; mismo lector que la Isla) | Rejilla/lector: PRUEBA AUTOMÁTICA. En PC: sin probar |
| Notificaciones | Abre el centro nativo de Windows (Win+N) | SOLO COMPILA |
| Menús de app (experimental) | Lee el `HMENU` de la app activa y ejecuta con `WM_COMMAND`; apagado por defecto | SOLO COMPILA; solo apps Win32 con menú estándar |
| Ajustes | Todo en la ventana de WinMac con efecto en vivo; la migración de `bar.json` es una sola vez y no toca el original | PRUEBA AUTOMÁTICA + CI de Windows (migración); la ventana, SOLO COMPILA |

## Descubrimientos de las pruebas en el PC
- **Wi-Fi exige el permiso de ubicación** en Windows 11: sin él, toda la API Wi-Fi (y `netsh wlan`) responde «acceso denegado». Winhance había puesto la política `HKLM\SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors\DisableLocation = 1`. La Barra ahora lo **dice** y ofrece abrir la página de privacidad, en vez de mostrar una lista vacía.
- Un error real que encontraron las pruebas automáticas: al juntarse movimientos del deslizador, se perdía el aviso del *último* (el que relee el valor real al soltar). Corregido en `SystemHub`.
- Un parche de configuración sin cambios efectivos ya no crea la sección `bar` ni escribe el archivo.

## No viable en Windows (con alternativa)
- Menú global de macOS para cualquier app: solo menús Win32 estándar (experimental).
- Foco/No molestar, Night Shift/True Tone, contenido de notificaciones: se abre el panel nativo de Windows.
- Conectar/desconectar un dispositivo Bluetooth concreto: abrir Configuración de Bluetooth.
- Cambio de salida de audio oficial: no existe; se usa la vía no oficial con reversión, o se abre Configuración.
- Fondo de vidrio distinto por monitor: la lectura del fondo de pantalla da UNA imagen; todas las barras la usan para el vidrio (el tinte puede diferir si cada monitor tiene un fondo distinto).

## Medición y CI
- `WinMac.Bar.App --selftest-system`: qué permite **este** equipo (sin cambiar nada). `--selftest-monitors`: monitores que ve Windows y cuáles llevarán barra (espejo = una sola). En CI son informativos (el runner no tiene Wi-Fi, Bluetooth, brillo ni segundo monitor).
- El CI de Windows ejecuta además `Test-Host.ps1` (paso `MIG`: arranque real con un `bar.json`, comprobación de `winmac.json`, original intacto y segundo arranque sin pisar) y el arranque integrado con Barra, Dock e Isla reales.

## Guía de validación en tu Windows de dos monitores
Prerrequisito (una vez): Ubicación activada (Configuración → Privacidad → Ubicación) para ver las redes Wi-Fi; DDC/CI activado en el OSD de los monitores si quieres brillo.

1. Compila: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`, luego `.\tools\Build-Local.ps1` (con WinMac cerrado: `.\dist\WinMac\WinMac.exe --quit`).
2. `.\dist\WinMac\WinMac.Bar.App.exe --selftest-monitors | Out-File monitores.txt -Encoding utf8` y `Get-Content monitores.txt`: deben aparecer tus 2 monitores, uno `[BARRA]` cada uno y el principal marcado.
3. Abre `.\dist\WinMac\WinMac.exe`. Debe haber **una Barra en cada monitor**, a todo el ancho de cada uno, con la misma apariencia.
4. Abre el Control Center en el monitor 2: debe abrirse **en el monitor 2**, pegado a su borde derecho y entero. Repite en el monitor 1, y haz clic en el mismo icono de la otra barra con el panel abierto (debe saltar a esa barra).
5. Con los menús: clic en la manzana de cada barra (el menú nace bajo esa barra).
6. **Duplicar** (Win+P o panel de pantallas): debe quedar **una sola Barra** y la Isla bien colocada. Vuelve a **Extender**: la segunda Barra reaparece y la Isla vuelve a su pantalla, **sin reiniciar WinMac**.
7. Isla: ponla en el monitor 2; solo la Barra del monitor 2 debe tener el hueco, la del monitor 1 queda completa. Cierra la Isla (Ajustes → módulo Isla): ambas Barras completas.
8. Desconecta (o apaga) el monitor 2 y vuelve a conectarlo: la Barra desaparece y reaparece sin tocar la del monitor 1.
9. Cambia el escalado de un monitor (Configuración → Pantalla): su Barra se reajusta; la otra no.
10. Ajustes de WinMac → «Barra superior»: cada opción se ve al instante en las dos Barras.
11. Si algo falla, pega: `Get-Content "$env:LOCALAPPDATA\WinMac\logs\bar.log" -Tail 60` (las líneas `[bar] monitores:` dicen qué barras se crearon, recolocaron o liberaron).

Sin hacer frente al PC (dejaría la sesión remota sin imagen o sin red): conectar a otra red Wi-Fi, apagar la radio, duplicar/extender, y la salida de audio (que suene donde corresponde).
