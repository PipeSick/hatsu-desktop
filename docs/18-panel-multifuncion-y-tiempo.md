# Panel multifunción de WinMac y panel del Tiempo (F6)

Estado: **implementado y probado en su lógica; el DIBUJO y las lecturas de hardware nunca se han visto funcionando en un equipo real.** Etiquetas: **PRUEBA AUTOMÁTICA** (corre en CI, lógica pura), **COMPILA** (compila y el CI de Windows arranca el programa, pero nadie ha visto el comportamiento), **POR VALIDAR EN TU PC** (hay que probarlo con el hardware real).

## Qué hay en la Barra
Dos iconos independientes, en este orden, antes de Bluetooth/Wi-Fi/volumen/batería:
- **Icono de WinMac** (uno solo, el planeta con anillo, provisional): abre un panel flotante con **cuatro pestañas** (Sonido, Sistema, Red, Discos), encabezado con el logotipo, selector horizontal de pestañas con iconos, contenido dentro de una tarjeta redondeada y, siempre, **Ajustes** y **Salir** abajo. No hay iconos separados para sonido, sistema, red o discos.
- **Icono del Tiempo** (icono + temperatura, «12°C»): abre su propio panel.
- La **identidad** (nombre y logotipo) vive en un solo registro, `AppIdentity` (Bar.Core), y el dibujo del logotipo se elige por clave: cambiarlos no toca la lógica del panel. El nombre «WinMac» no se cambió.

## Arquitectura (sin procesos nuevos)
| Capa | Dónde | Qué | Verificación |
|---|---|---|---|
| Datos puros | `WinMac.Bar.Core/Hub` | `RateMeter`, `History`, `Units`, `MemoryInfo.Pressure`, `HubMonitor`, `TrafficTracker`, `DiskCatalog`, `SpeedTestEngine/Runner`, `WeatherService/Cache/OpenMeteoParser/WeatherStrip` | PRUEBA AUTOMÁTICA |
| Modelo de panel | `Bar.Core/Panels` | widgets nuevos (pestañas, gráfica, medidor, estadística, selector, fila de app, Tiempo), tarjeta, geometría pulsable, `PanelBuilder.Hub`, `PanelOperations` | PRUEBA AUTOMÁTICA |
| Servicios de audio | `System.Core/Win` | el MISMO `IAudioService` del Control Center, ampliado (micrófono y sesiones por aplicación): no hay una segunda implementación | contrato y doble: PRUEBA AUTOMÁTICA; COM real: COMPILA |
| Adaptadores | `Bar.Win/Shell/Hub` | sensores, red, discos, iconos, ubicación | COMPILA |
| Dibujo | `Bar.Win/Rendering/PanelPainter*.cs` | D2D vectorial propio | COMPILA + CI renderiza cada panel a PNG (datos de muestra) |
| Host | `Host.Core` | orden `host.cmd` (solo desde la Barra, solo «quit»/«settings»), resolvedor de ciudades | PRUEBA AUTOMÁTICA |

- **Una sola ventana, una sola instancia** (el popover de la Barra ya lo era), posicionada en el monitor pulsado y a su DPI (F5).
- **Ajustes** abre la ventana central de WinMac en la sección de la pestaña activa (foco en su grupo); **Salir** manda `quit` al host, que usa su **cierre ordenado** (cierra Barra, Dock e Isla): no se limita a cerrar el panel.
- La pestaña elegida se **recuerda** (`hubTab` en la configuración central).

## Rendimiento y recursos
- Con el panel **cerrado no se lee ningún sensor, red ni disco**. Con el panel abierto se lee **solo la pestaña activa**, cada `HubRefreshMs` (por defecto 1 s; 0,5–10 s en Ajustes).
- Cada lectura va a un hilo de trabajo y es de un solo vuelo; un proveedor que falla deja su estado «no disponible» y no afecta al resto.
- El Tiempo solo hace peticiones si hay ciudad elegida (o ubicación automática activada): caché en disco, 15 min de frescura, reintentos 1-2-4… hasta 30 min, nunca bloquea la Barra.
- Sin administrador en ninguna parte.

## Comparación con las capturas de referencia
**Aviso honesto:** no he visto los paneles renderizados; la comparación es por composición y medidas (los anchos 348 y 304 DIP salen de las capturas a @2×). El CI genera `panel-hub-*.png` y `panel-weather.png` con datos de muestra: es la forma de comparar a ojo.

| Referencia | Se reproduce | Difiere / limitación |
|---|---|---|
| **FOTO 1 · Tiempo** | ciudad (con flecha si es ubicación automática), temperatura grande, icono + descripción + Máx./Mín., tira de 5 horas con la salida del sol en su sitio y probabilidad de lluvia (≥10 %), ciudades adicionales, «Abrir la app Tiempo» | iconos meteorológicos propios (no los de macOS); tipografía Segoe UI en vez de SF; el icono de la barra es más pequeño |
| **FOTO 2 · Sonido** | selector y volumen de salida, selector y volumen de micrófono, lista de aplicaciones con su **icono real**, volumen y silencio por aplicación, tarjeta y separadores | **sin selector de salida por aplicación**: Windows solo lo ofrece por una interfaz interna sin contrato estable; hay un botón a «Volumen de aplicaciones» de Windows. Se listan las sesiones no expiradas (activas primero), como el mezclador de Windows. «Opciones» desplegable → dos botones |
| **FOTO 3 · Sistema** | temperaturas CPU/GPU, uso de CPU y GPU con medidor y gráfica, memoria (presión, comprimida, disponible, comprometida, paginación) con su gráfica, actividad, dispositivos | la **temperatura de CPU casi nunca se puede leer sin un controlador con privilegios**: se muestra la zona térmica ACPI rotulada como aproximada, o «No disponible»; la de GPU solo con NVIDIA (NVML). «Presión de memoria» es una **estimación documentada** (Windows no la publica). «Archivos en caché» se sustituye por datos que Windows sí da. Dispositivos = USB conectados |
| **FOTO 4 · Red** | descarga/subida con el botón de unidad (Auto/KB/s/MB/s/Mb/s), gráfica, tráfico de la sesión, adaptador, tipo, IPv4, prueba de velocidad con progreso | «Apps usando red» son aplicaciones **con conexiones activas**, no su tráfico (Windows no lo da sin administrador); lo dice el panel |
| **FOTO 5 · Discos** | selector de volúmenes, uso con barra, capacidad, actividad en vivo con gráfica y acumulado, SMART, protección externa, herramientas | temperatura, desgaste y horas suelen exigir administrador: se muestran «No disponible» con su motivo, **sin copiarlos de otro disco**. «Expulsar» usa el verbo seguro del Explorador |

## Qué es completamente operativo y qué es limitado
| Función | Estado |
|---|---|
| Volumen y silencio de salida/entrada, selección de dispositivos | mismos servicios del Control Center (la salida y el volumen ya **PROBADOS EN PC**); micrófono y selección de entrada: POR VALIDAR EN TU PC |
| Volumen y silencio por aplicación, iconos reales | POR VALIDAR EN TU PC |
| CPU %, RAM, commit, uptime | POR VALIDAR EN TU PC (APIs estándar) |
| GPU %, temperatura GPU | solo NVIDIA con NVML; el uso, en otras GPU, por contadores de Windows. POR VALIDAR EN TU PC |
| Temperatura de CPU | **limitada**: aproximada o no disponible |
| Red: tasas, acumulado, adaptador, IPv4 | POR VALIDAR EN TU PC; la lógica de tasas (reinicios, pausas, cambio de adaptador) está probada |
| Prueba de velocidad | motor y estados: PRUEBA AUTOMÁTICA; red real: POR VALIDAR. Nunca arranca sola; pide confirmación; límite de 25 MB / 8 MB y de tiempo; cancelable |
| Discos y volúmenes, E/S | POR VALIDAR EN TU PC |
| SMART | **limitado**: solo salud general suele estar disponible sin administrador |
| Expulsión | POR VALIDAR con una unidad externa; nunca se fuerza |
| Tiempo | proveedor Open-Meteo (gratuito, sin clave): parser, caché, reintentos y estados PROBADOS; red real POR VALIDAR |
| Ubicación automática | opcional y apagada; **no funciona si el servicio de ubicación de Windows está bloqueado** (como en el PC de Felipe, por una política de Winhance, hasta que la revierta) |
| Ajustes por pestaña y Salir | órdenes del host: PRUEBA AUTOMÁTICA; ventana y cierre real: POR VALIDAR |

## Pruebas manuales necesarias en tu PC
Compila (`Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`, `.\tools\Build-Local.ps1`) y abre `.\dist\WinMac\WinMac.exe`.
1. **Barra:** deben verse el icono de WinMac y el del Tiempo (sin ciudad, solo la nube). Un clic en el planeta abre el panel.
2. **Pestañas:** recorre las cuatro; cierra y reabre: debe recordar la última. Comprueba que solo hay un panel.
3. **Sonido:** cambia salida y micrófono con los selectores; mueve el volumen; silencia con el icono del altavoz; con Spotify/Chrome sonando, mueve y silencia su fila y comprueba que el icono es el real. Abre y cierra una app con audio: la lista debe actualizarse en un par de segundos.
4. **Sistema:** valores plausibles frente al Administrador de tareas (CPU, GPU, memoria). Lee qué temperaturas dice no poder leer.
5. **Red:** descarga algo y mira la gráfica; alterna la unidad con el botón; pulsa la prueba de velocidad (pide confirmación y muestra progreso).
6. **Discos:** compara capacidades con el Explorador; mira la actividad al copiar un archivo; SMART: ¿qué muestra y qué dice no tener? Con una unidad USB: «Expulsar» (y prueba con la unidad en uso: debe negarse sin forzar).
7. **Tiempo:** Ajustes → «Panel de WinMac y Tiempo» → escribe tu ciudad y sal del campo: debe resolverla y mostrar «Ciudad: …». Abre el panel del Tiempo. Corta la red un momento y comprueba que sigue mostrando los datos marcados como desactualizados.
8. **Ajustes y Salir:** «Ajustes» abre la ventana con foco en el grupo de la pestaña. **«Salir» debe cerrar WinMac entero** (Barra, Dock, Isla); comprueba en el Administrador de tareas que no quedan procesos.
9. Pega el resultado de `.\dist\WinMac\WinMac.Bar.App.exe --selftest-system` (incluye la sonda de notificaciones).

## Diagnóstico SMART/NVMe por disco (`--selftest-system`)
Estado: **COMPILA + PRUEBAS AUTOMÁTICAS de la lógica pura (formato de las peticiones, interpretación y validación de respuestas, comparación de identidades); lo que responda cada SSD real SOLO se sabe ejecutándolo en el PC.** Nada de esto aparece en el panel Discos, que sigue mostrando solo «No disponible» con un motivo corto.

Qué imprime, por cada `\\.\PhysicalDriveN` (WMI, Windows y el propio disco; **solo lectura, sin administrador, no escribe en ningún disco**):
- Cada volumen preguntado al propio volumen (`IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS`) y comparado con `MSFT_Partition`.
- Cada `DeviceIoControl` por separado, con ruta, nivel de acceso (0 «solo consulta» y `GENERIC_READ`), propiedad pedida, resultado, **código de error Win32**, bytes devueltos y errores de validación de la respuesta: número de dispositivo, descriptor del dispositivo y del adaptador, temperatura (propiedad 52), NVMe Identify y registro de salud 0x02 por dispositivo y por adaptador.
- Identidad según `MSFT_PhysicalDisk`, `MSFT_Disk`, `Win32_DiskDrive` y la propia ruta, **comparadas por número de serie, tamaño y modelo** (no se supone que «disco N» sea el mismo en todas) y las fuentes que no lo conocen marcadas como AUSENTE.
- Cadena de dispositivos y controlador (Administrador de dispositivos + `ImagePath` del servicio, con empresa y versión del archivo), y los grupos de Espacios de almacenamiento.
- Fiabilidad según WMI (directa y por asociación) y las métricas finales con su **origen** (WMI, propiedad de temperatura o registro NVMe).

Corrección incluida: la propiedad de temperatura se pedía con el número 12 (`StorageDevicePowerProperty`); la correcta es 52 (`StorageDeviceTemperatureProperty`, según winioctl.h). Y un registro NVMe sin ningún dato verosímil se descarta entero (antes podía dar un «0 % usado» inventado).

## Errores que Windows registró de cada disco

En la sección SMART, el panel muestra «Errores (7 días)» con un punto rojo cuando el registro «Sistema» tiene errores de ese disco. Se leen los eventos 7, 51, 153 y 157 de `disk` y los eventos 11 y 129 de `stornvme`, `storahci` y `storport`. Solo lectura y sin administrador; la lectura se guarda 10 minutos.

- Un evento cuenta para un disco solo si su ruta lo nombra. `\Device\HarddiskN\…` es el disco N. `\Device\RaidPortN` es el puerto SCSI N, y se atribuye al disco que `Win32_DiskDrive.SCSIPort` sitúa en ese puerto, solo si es uno. Lo que no se puede atribuir se descarta; nunca se reparte entre discos.
- Es lo que Windows anotó, no una medida del disco: un disco «Saludable» puede tener errores registrados, y ese es justo el caso que interesa ver.
- `--selftest-system` lista los eventos con su ruta, el disco al que se atribuye cada uno y el mapa de puertos a discos.
