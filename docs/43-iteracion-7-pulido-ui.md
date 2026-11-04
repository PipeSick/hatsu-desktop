# Iteración 7 — pulido de interfaz y experiencia de usuario

Ramas: WinMac `feat/winmac-iteration-7-polish` (base `59e5d6f`), NotchWin `feat/winmac-iteration-7-polish` (base `f866eff`, pin en `tools/notch-pin.txt`). Investigación de llamadas (`research/iphone-calls`, `f4f1ecd`): sin cambios; ningún código HFP. Sin PR ni merge.

> Las pruebas automáticas y las capturas de CI **no son validación en hardware real**. Todo lo marcado «pendiente en hardware» está en la lista manual del final.

## Diagnóstico y causas
| # | Hallazgo | Estado |
|---|---|---|
| 1 | La barra ya seguía el tema de Windows (sondeo de 5 s) pero tenía un selector manual; diálogos, ventana principal y NotchWin llevaban colores fijos. | Demostrado en código |
| 3 | La tarjeta multimedia inferior («En curso») dibujaba **siempre el glifo de Reproducir** (``) para el botón Play/Pausa, mientras el reproductor superior cambiaba Play/Pausa. Además las lecturas asíncronas de GSMTC podían terminar desordenadas (una lectura vieja «reproduciendo» pisaba a una nueva «pausa») y tras un comando no se releía el estado. | Primera causa demostrada en código; la carrera es probable (corregida con tickets), sin reproducción en hardware |
| 6 | Un SAI/UPS (clase Batería por HID/USB) hace que `GetSystemPowerStatus` informe batería con un % que no es del PC. | Inferido (corregido clasificando dispositivos) |
| 9 | Al hacer clic derecho en un icono de «Apps en segundo plano» el panel se cerraba antes de reenviar el clic, devolviendo el foco a otra ventana justo cuando la app abría su menú → menú sin foco, que no se cerraba al pulsar fuera. | Inferido por el flujo; no reproducido |
| 10 | Los iconos de Volumen y «Quitar hardware con seguridad» son casi monocromos y blancos (hechos para barra oscura) y el panel claro los pintaba tal cual. | Inferido por píxeles; corregido por análisis de píxeles |

## Cambios por componente
**WinMac** (commits separados): paleta común `UiPalette` y diálogo de confirmación reutilizable (tarjeta muy redondeada, icono por acción, separador, dos botones iguales, destructivo en rojo, Esc cancela, foco visible, por defecto Cancelar); ventana principal clara/oscura; «Acerca de este PC» propio (datos locales, «No disponible» si Windows no los da, GPU principal sin adaptadores virtuales, sin telemetría); barra: sin selector de tema, batería del iPhone (% opcional, «—» sin lectura válida, tachado sin conexión), batería del PC solo con batería real (SAI excluido, sin 100 % ficticio, icono de enchufe opcional, enchufe pequeño si hay corriente sin cargar), botón del Administrador de tareas (opcional, `taskmgr.exe` por el shell; cancelar UAC no es error), reordenar arrastrando (umbral de 5 DIP, indicador de inserción, un arrastre nunca es clic, orden persistido en `bar.json › barOrder`, «Restaurar orden predeterminado»), sugerencias nativas; panel del iPhone con tarjetas; bandeja: el panel permanece mientras exista el menú contextual (`TrayMenuWatch`) e iconos legibles (`IconAdapt`).
**NotchWin**: tema claro completo (`IslandPalette` en Core, recursos por tema en el XAML, `Pal` en código, cambio en caliente, cristal claro con mínimos de opacidad), Play/Pausa real y sincronizado.

## Qué se puede y qué no mover en la barra
Movibles: apps en segundo plano, panel WinMac, Tiempo, Bluetooth, red, volumen, iPhone, batería/enchufe, Administrador de tareas. **Fijos**: menú WinMac, nombre de app/menús (izquierda), Centro de control y reloj (derecha, últimos). Los iconos de la bandeja de Windows son de otros procesos: se agrupan en un solo botón y no se reordenan individualmente. El orden se aplica a todas las barras (multi-monitor) y se conserva la posición de módulos hoy ocultos.

## Limitaciones
- Portátil con SAI: Windows mezcla ambas baterías; se usa su cifra.
- Por Bluetooth el iPhone no indica si carga: no se muestra «cargando».
- Las sugerencias son el control nativo de Windows (su tema lo decide Windows).
- `MessageBoxW` informativos de `TaskbarHider`/`SecurityDiagnostics` y el menú del Dock siguen oscuros/nativos. No se tocó Dock, mezclador ni enrutamiento.
- En tema claro el cristal de NotchWin tiene un mínimo de opacidad (0,86–0,88) para que el texto se lea sobre cualquier fondo; el control «Cristal» actúa por encima de ese mínimo.
- «Notificaciones recibidas» del iPhone = las que hay en lista ahora, no un histórico.

## Pruebas
Dock.Core 210, Bar.Core 532, Host.Core 137, Connect.Core 16, Apps 25, Ipc 31, System 20; NotchWin.Core 206 — verdes. NotchWin compila en Windows (CI). Cubren: paleta/contraste claro-oscuro, diálogos, Acerca de, batería iPhone/PC, orden y arrastre, Administrador de tareas, menú contextual de la bandeja, adaptación de iconos, Play/Pausa (orden de publicación, glifos), coincidencia XAML↔paleta.

## Actualizar sin perder ajustes
Cerrar WinMac (`WinMac.exe --quit`), reemplazar la carpeta del paquete `winmac-paquete-<sha>`, abrir. La configuración (`bar.json` y demás) vive fuera de esa carpeta; los campos nuevos tienen valores por defecto. `theme` de `bar.json` se conserva pero se ignora.

## Lista de pruebas manuales (hardware real)
1. Cambiar el modo de apps de Windows con WinMac abierto: barra, diálogos, ventana principal, Acerca de, panel iPhone e Isla cambian sin reiniciar.
2. Isla en claro: legible con fondo blanco y negro detrás; barrer el control «Cristal».
3. Reproducir/pausar desde la Isla, desde el reproductor y desde fuera (tecla multimedia, la app): ambas tarjetas coinciden; cerrar el reproductor; cambiar de pista.
4. iPhone: conectar, desconectar, apagar Bluetooth; % con/sin porcentaje.
5. Sobremesa con y sin SAI: sin módulo de batería; con la opción, enchufe. Portátil: % y enchufe.
6. Arrastrar módulos, reiniciar WinMac, «Restaurar orden»; clic en cada módulo; 2 monitores y DPI distinto.
7. Administrador de tareas: abrir, cancelar UAC.
8. Clic derecho en iconos de «Apps en segundo plano» (incluidos Volumen y Quitar hardware): el panel sigue, el menú se cierra con Esc/clic fuera y el panel se cierra después.
9. Iconos de Volumen y USB legibles en claro y oscuro.
10. Apagar/Reiniciar/Cerrar sesión/Eliminar notificaciones: Esc cancela, Enter ejecuta solo con el foco en la acción.
