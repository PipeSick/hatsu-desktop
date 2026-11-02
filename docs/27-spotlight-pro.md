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
**Portapapeles** (desde la fase de estabilización): «portapapeles» o «cb texto» muestran el historial de la Isla primero; cualquier búsqueda incluye hasta 3 coincidencias.
Spotlight pide a la Isla (`clip.query` por el canal interno de WinMac; las extensiones no tienen acceso a ese canal) y recibe **solo vistas previas de una línea**
(máx. 160 caracteres, imágenes como «Imagen») de lo que el historial ya admitió con sus reglas de privacidad. Intro copia, **Ctrl+Intro copia sin formato**,
**Ctrl+Supr** lo quita del historial; lo hace la propia Isla. Con la pestaña Portapapeles desactivada no devuelve nada. Nada de esto va a los registros.
«Pegar» automáticamente no se ofrece: WinMac no simula pulsaciones en otras apps.

Teclado: ↑/↓ y **Tab / Mayús+Tab** mueven la selección (la vista previa la sigue); Intro, Ctrl+Intro, Esc (borra y cierra).
