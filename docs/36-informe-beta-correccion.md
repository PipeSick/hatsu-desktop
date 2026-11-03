# WinMac 2.0 — Fase de corrección integral de la beta (WM-001 … WM-021 + Quick Look)

Fecha: 2026-11-03 · Rama `claude/relaxed-turing-cijzko` (WinMac y NotchWin) · **Nada publicado.** WinMac sigue siendo gratuito y de código abierto.

Leyenda de estado (no se declara validado lo que solo pasó una prueba unitaria):

- **VALIDADO EN CI** — comprobado en el runner de Windows con WinMac real y capturas de la pantalla real.
- **CI (render)** — dibujado por el código real fuera de pantalla con datos de MUESTRA en el CI; la interacción no se probó.
- **PROBADO (unidad)** — lógica con pruebas automáticas; sin prueba en Windows.
- **PENDIENTE PC REAL** — necesita hardware o una situación que el runner no tiene.

## Estado de cada hallazgo

| ID | Hallazgo | Solución (arquitectura) | Estado |
|---|---|---|---|
| WM-001 | El Dock tapa los controles de apps maximizadas | El Dock es una AppBar de Windows (`DockAppBar`: ABM_NEW/QUERYPOS/SETPOS/REMOVE), se reregistra si Explorer se reinicia (TaskbarCreated), sigue ABN_POSCHANGED y libera la franja al salir. Con autoocultación no reserva. Opción en Ajustes › Escritorio y Dock | **VALIDADO EN CI**: Bloc de notas maximizado termina en 642 px y la franja del Dock empieza en 634 (8 px = borde invisible de DWM). Multimonitor y 150 %: PENDIENTE PC REAL |
| WM-002 | Capturar menús abiertos los cierra | Cuenta atrás 3/5/10 s (Ajustes, Spotlight, `--capture region-delay5`); indicador excluido de la captura y que nunca toma el foco; la pantalla se copia antes de mostrar ninguna interfaz | **VALIDADO EN CI**: menú Archivo del Bloc de notas abierto con el teclado, a los 3 s la pantalla congelada lo contiene (30a/30b). No se promete para menús que se cierran solos |
| WM-003 | La barra superior tapa menús si la barra de tareas no se oculta al primer inicio | `WorkAreaGuard`: compara la reserva pedida con el área de trabajo real del monitor y la vuelve a pedir (2-4-8…30 s) si Explorer no la aplicó. Ninguna configuración de Windows se cambia; ABM_REMOVE al salir | PROBADO (unidad) · PENDIENTE PC REAL (primer arranque al iniciar sesión, reinicio de Explorer, varios monitores) |
| WM-004 | Bandeja de segundo plano con UI disonante | El desplegable es el panel de WinMac: título, iconos de 20 px, teclado (flechas, Intro, tecla de menú/Mayús+F10). Los menús de cada app son los suyos (cambiarlos exigiría inyectar código: no se hace) | PROBADO (unidad) · el desplegable real ya se fotografía en el job «captura real de los iconos» |
| WM-005 | El menú «Más» de la Isla se cierra solo | Los flyouts son otra ventana: `HoldOpenWhile` y guardas del temporizador de salida (NotchWin) | VALIDADO EN CI (selftest «más abierto / eligió») |
| WM-006 | Iconos multimedia incorrectos | Iconos vectoriales (pausa de dos barras, siguiente sin recorte) | VALIDADO EN CI (capturas de la Isla) |
| WM-007 | Música duplicada en la Isla expandida | Se oculta el encabezado repetido en la pestaña Música; sin rediseño | VALIDADO EN CI |
| WM-008 | Sin letra para «Fighting Myself — Linkin Park» | Diagnóstico en CI (`tools/ci/Lyrics-Diag.ps1`): NetEase **sí** tiene la letra sincronizada (id 2032310865, 52 líneas) y el filtro la acepta con esos metadatos. Cambios mínimos y justificados: un rechazo de NetEase (code -460 desde algunas redes) ya no se guarda como «sin letra»; reintento con el título sin sufijos («- Remastered», «(feat.)»); LRCLIB como segunda fuente solo si NetEase no la tiene (el diagnóstico confirma que la tiene); `lyrics.log` local sin título ni artista. Motor, caché, sincronía, vistas y animaciones intactos | PROBADO (unidad, 7 pruebas nuevas). **PENDIENTE PC REAL**: reproducir con el Spotify del probador y mirar `%LOCALAPPDATA%\NotchWin\lyrics.log` |
| WM-009 | Menú W como lista clásica | Panel Centro de Control: usuario (nombre de Windows, iniciales) y sesión, botón de apagado, mosaicos reales (Wi-Fi, Bluetooth, No molestar → abre su ajuste porque Windows no tiene API pública, Pantallas, Modo de energía, Configuración), volumen y brillo, Suspender/Reiniciar/Apagar/Cerrar sesión/Bloquear con las confirmaciones de siempre, Ajustes de WinMac, Acerca de, Forzar salida | CI (render) + PROBADO (unidad). Foto de la cuenta: no se usa (solo iniciales) |
| WM-010 | Batería sin detalles | Tiempo restante (GetSystemPowerStatus), capacidades y tasa (Windows.Devices.Power), ciclos y química (IOCTL_BATTERY_QUERY_INFORMATION). Salud solo si diseño y plena carga son reales; lo que no se informa se dice. Nada de las cifras del maqueta | CI (render, datos de MUESTRA completos y parciales) + PROBADO (unidad) · PENDIENTE PC REAL (portátil) |
| WM-011 | Menú de la bandeja de WinMac blanco y básico | Panel oscuro propio (Direct2D, DWM): estado general, cada módulo con su estado y acción (Reiniciar/Abrir/Activar), Ajustes, Reiniciar los módulos, Salir (cierre ordenado). Teclado; menú clásico solo de respaldo | CI (render 49i) + PROBADO (unidad) · interacción PENDIENTE PC REAL (el CI corre el host sin icono) |
| WM-012 | Notificaciones limitadas «3 de 4» | El recuento contaba solo la tarjeta visible de un grupo plegado; ahora cuenta todo lo accesible y el aviso aparece solo ante un límite real (60 apps / 150 tarjetas, desplazable) | PROBADO (unidad) |
| WM-013/014 | Ajustes confusos y truncados | Diez secciones: General · Escritorio y Dock · Barra superior · Isla · Capturas y grabación · Spotlight y Launchpad · Notificaciones · Clima y ciudades · Portapapeles · Extensiones. Cada ajuste en un solo sitio (prueba), subtítulos en varias líneas, segmentos con ancho para su etiqueta, barra lateral sin recortes | VALIDADO EN CI (renders 49a–49h revisados; dos defectos vistos allí corregidos) |
| WM-015 | Clima: ubicación y ciudades confusas | Origen Automática/Ciudad elegida, ciudad de respaldo, estado actual, permiso de ubicación de Windows leído sin pedirlo, ciudades adicionales con quitar/ordenar y «Añadir ciudad» con el buscador | VALIDADO EN CI (render) + PROBADO (unidad) |
| WM-016 | Brillo en portátil | Panel interno por WMI con System.Management (antes COM tardío `dynamic`, sin garantías en .NET y sin motivos); si nada es controlable se dan los dos motivos; el deslizador dice si mueve la pantalla integrada, monitores DDC/CI o ambos | PROBADO (unidad) · **PENDIENTE PC REAL** (el runner no tiene panel interno) |
| WM-017 | Menú del Dock nativo | Menú oscuro propio con las mismas entradas y acciones, submenús dentro del panel, teclado; nativo de respaldo | PROBADO (unidad). **NO VALIDADO EN CI**: el clic derecho simulado no llega a la ventana del Dock (en investigación; diagnóstico añadido) |
| WM-018 | Launchpad demasiado grande | Panel compacto centrado (buscador, 3–6 columnas × hasta 4 filas, puntos de página); fuera del panel se atenúa y un clic cierra | VALIDADO EN CI (captura 11, búsqueda 12) · a 1024×768 el panel llega al borde del Dock |
| WM-019 | Dock enorme a 150 % | Preajustes compactos 36/48/64/80 pt (predeterminado 48, antes 60), migración v4 de tamaños guardados, ajuste fino 28–96 pt en Ajustes; el tamaño sigue el DPI de cada monitor | VALIDADO EN CI (render de Ajustes) · aspecto a 150 % PENDIENTE PC REAL |
| WM-020 | Global Menu duplicado | Desactivado por defecto (migración v3 de la configuración); opción avanzada | VALIDADO EN CI |
| WM-021 (anexo) | Spotlight parpadea y el ping no funciona | Superficie creada una vez (1 superficie por apertura medida en CI); refrescos sin cancelar la herramienta; ping real de 4 ecos progresivos con destino, estado, paquetes, pérdida, latencia, errores, Ejecutar/Repetir/Cancelar/Copiar; sin consolas | **VALIDADO EN CI**: 127.0.0.1 4/4 (<1 ms), google.com 0/4 (el runner bloquea ICMP saliente), nombre inexistente con error de DNS, 10.255.255.1 sin respuesta; tecleo rápido sin recrear la superficie. 150 %: PENDIENTE PC REAL |
| Quick Look (§11) | Miniatura tras capturar y vista previa | Miniatura abajo a la derecha sobre el Dock visible, 5 s y fundido, el puntero la mantiene; nunca abre sola la vista grande; vista previa centrada con Copiar, Editar, Anotar, Recortar, Compartir (hoja de Windows), Guardar como, Abrir, Eliminar (Papelera). Esc/Espacio, Ctrl+C, Intro. Excluida de capturas posteriores (WDA_EXCLUDEFROMCAPTURE); en la galería de la Isla, Espacio ya abría la vista ampliada | **VALIDADO EN CI**: miniatura, vista previa y Esc (32c/32d). Posición corregida tras verla en el CI (salía arriba). Copiar: imagen real de 1024×768 en el portapapeles (run 41625325738). Exclusión real: PENDIENTE PC REAL (en el CI se desactiva para poder fotografiarla, y el registro lo dice) |

## Otras correcciones de esta fase
- Ajustes mostraban «A» como título (DefWindowProc ANSI) → `DefWindowProcW` (host y Dock).
- Paquete de la beta autocontenido: no pide instalar .NET. `WinMac-2.0.0-<sha>-win-x64.zip` = 171,6 MB, generado dos veces en el CI con el mismo SHA-256 (reproducible).

## Rendimiento (runner sin GPU, WARP: orientativo)
Medición en reposo de `Visual-Audit.ps1` (10 s, 4 núcleos), antes (3f729c7) → después (e402ab6):

| Proceso | Tras la Isla: CPU % / WS MB | Al final de toda la auditoría: CPU % / WS MB / privada MB |
|---|---|---|
| WinMac (host) | 1,56 / 49,2 → 0,94 / 48,4 | 0,16 / 28,7 / 21,7 → 0,31 / 29,6 / 21,8 |
| Barra | 1,09 / 148,5 → 0,78 / 149,0 | 0,16 / 362,1 / 325,7 → 0,47 / 315,1 / 310,0 |
| Dock | 0,62 / 128,3 → 0,16 / 128,1 | 0,00 / 67,3 / 92,3 → 0,31 / 57,3 / 87,7 |
| Isla | 4,84 / 163,2 → 4,22 / 162,7 | 0,62 / 152,9 / 102,0 → 1,25 / 153,8 / 101,2 |

Spotlight: 1 superficie DirectComposition por apertura y 4 cambios de tamaño (antes se recreaba al escribir). Sin regresiones de memoria visibles;
la memoria de la Barra al final sigue alta tras grabar y capturar (conocido desde docs/35).

## Pendiente y riesgos (no se ocultan)
- PC real: WM-003 al iniciar sesión, WM-016 en portátil, WM-008 con el Spotify del probador, WM-010 en portátil, 150 % de DPI y varios monitores en Dock, Quick Look y Spotlight, exclusión de capturas de Quick Look.
- El job «captura real de los iconos de la bandeja» falló una vez (3f729c7) en la comprobación del estado interno y pasó en las siguientes ejecuciones: intermitente, en observación.
- «No molestar» de Windows no tiene API pública: el mosaico abre su ajuste.
- Foto de la cuenta en el panel W: no implementada (iniciales).
- Indicador de notificaciones pendientes en la barra: no añadido en esta fase.
- La interacción del panel de la bandeja del host (WM-011) no se puede probar en CI (el host corre sin icono).

## Beta privada
El artefacto del CI `winmac-paquete-<sha>` contiene el ZIP autocontenido. No se ha creado ninguna release ni se ha publicado nada.
