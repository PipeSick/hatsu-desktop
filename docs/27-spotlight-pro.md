# 27 · Spotlight Pro

Lista de resultados a la izquierda y **vista previa** a la derecha (detalles y las teclas que actúan). Proveedores puros en `Bar.Core/Launcher/SpotPro.cs`.

| Escribe | Resultado | Cuándo actúa |
|---|---|---|
| captura, grabar, launchpad, ajustes… | acciones de WinMac | Intro |
| guardar espacio, lado a lado, cuadrícula, restaurar | espacios de trabajo | Intro |
| ip, red | dirección IP / adaptadores (datos locales, sin tráfico) | Intro copia |
| ping host, dns host | herramienta de red (host validado, 3 ecos de 2 s / DNS 4 s, cancelable) | **solo con Intro**; otro Intro copia el resultado |
| ssh usuario@equipo[:puerto] | cliente OpenSSH de Windows en su ventana, argumentos uno a uno, sin shell | Intro |
| > texto | «Abrir Terminal» (sin pasarle nada) o «Copiar» | **nunca se ejecuta lo escrito** |
| 20% de 150, 12*12, 10 km en mi, 2 gb en mb, 90 min en h | calculadora y conversiones | Intro copia |
| nombre de un script registrado | script del usuario (`%APPDATA%\WinMac\spotlight-scripts.json`) | **doble Intro**; con tus permisos, nunca elevado |

Divisas: **no** se ofrecen (no hay fuente gratuita fiable integrada con fecha de cotización; se documenta como pendiente).
Un proveedor lento nunca bloquea: lo local aparece al instante y lo lento (Windows Search, ping, DNS) llega después y se descarta si la búsqueda cambió.
Portapapeles en Spotlight: pendiente (el historial vive en la Isla).
