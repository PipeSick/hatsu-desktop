# Registro de cambios

Formato libre por versión. Las entradas marcadas «sin validar» están implementadas y probadas automáticamente, pero nadie las ha visto funcionar en un Windows real. Nada de esto está publicado como versión estable.

## Sin publicar (rama de trabajo)
Versión del paquete: **2.0.10** (la Store exige una versión mayor en cada envío).
**Módulos de productividad** ([`docs/63`](docs/63-modulos-de-productividad.md)), cada uno con interruptor en *Ajustes › Módulos*, atajos configurables con captura de teclado y aviso de conflictos (incluidas las Capturas Ctrl+Mayús+3–6):
- Mission Control (escritorio actual, multimonitor sin validar), Snap (zonas, editor, distribuciones propias), Live Text (OCR local), Spotlight Actions, Gestures (esquinas y botones laterales), Shelf+ en la Isla (archivos, carpetas, texto, imágenes).
- Gestures: accesos rápidos para abrir aplicaciones instaladas o páginas web desde esquinas, botones laterales y atajos (hasta 6). Se retiró la acción de ocultar el Dock. Spotlight: «usar la distribución» acomoda las ventanas abiertas.

**Ajustes reorganizados (propuesta A):** 12 secciones en 5 grupos (Escritorio, Funciones, Contenido, Dispositivos, Avanzado) con buscador de ajustes, «Atajos y gestos» como página propia, Notificaciones y Portapapeles juntos, e «iPhone y periféricos». **Personalizar los iconos de la barra:** vista previa, «En la barra» con orden y «Disponibles para agregar». **El menú con pestañas pasó a ser iconos sueltos** de Rendimiento, Red y Discos (cada uno abre su panel). **Se quitó el monitor de agentes de IA** de la Isla (pestaña, sondeo y ajustes). **Ajustes 1:1 con el prototipo:** asa ⋮⋮ para arrastrar los iconos de la barra, píldoras «Agregar», «Cancelar/Guardar» y «Personalizar iconos…», etiqueta «Instalada». **Menú de Hatsu:** «Salir de Hatsu Desktop». **Gestures:** al abrir la lista de acciones, «Aplicación…» (busca entre las apps instaladas) y «Página web…» (pega el enlace).

**VPN en la barra** (opcional, *Ajustes › Barra › VPN*): icono de escudo que muestra el estado y abre un panel para conectar o desconectar las VPN de Windows (Configuración › Red › VPN) con las credenciales ya guardadas. No cubre apps de VPN de terceros. Sin validar en Windows real.

**Periféricos:** el panel muestra solo lo conectado y con lectura. Lectores opcionales (apagados por defecto) para ASUS ROG por el receptor ROG OMNI (medido con un teclado y un ratón reales, [`docs/64`](docs/64-perifericos-asus-rog.md)) y Logitech por HID++ (**probable, sin verificar**, [`docs/65`](docs/65-perifericos-logitech.md)); nombres editables y receptores no compatibles listados por nombre.

**Capturas:** se copian al portapapeles al tomarlas (default; se apaga en Ajustes) y la miniatura se puede arrastrar a otra aplicación.

**Hatsu Notch:** banner de descargas con ancho adaptable; clic en una notificación abre la app que la envió (ahora con la activación de Windows, que informa si falló, y con un registro en `%LOCALAPPDATA%/NotchWin/notificaciones.log`); protección frente al error del fondo translúcido.
**Dock:** × en cada ventana de la vista previa; «Reservar espacio» y la vista previa se aplican en vivo.
**Barra:** icono propio de Hatsu; panel de Limpieza rediseñado; las rutas largas de Spotlight se recortan con «…».
**Correcciones:** la Barra y el Dock ya no se esconden cuando una app marcada como «ocupada» (p. ej. ChatGPT) no cubre la pantalla entera.
**Ajustes:** barra de título con icono y colores propios; desplegables dentro de la ventana.

## 2.0.x
- Instalador MSI y asistente de primera ejecución; MSIX para Microsoft Store con la identidad de Partner Center ([`docs/48`](docs/48-fase3-instalador-onboarding.md), [`docs/52`](docs/52-msix-microsoft-store.md)).
- Rebranding de WinMac a Hatsu Desktop y migración del repositorio ([`docs/46`](docs/46-rebranding-hatsu-desktop.md), [`docs/54`](docs/54-migracion-repositorio-hatsu-desktop.md)).
- Peripherals y Cleanup; Dock por tema y bandeja ([`docs/44`](docs/44-iteracion-8-peripherals-cleanup.md)).
- Connect: batería, estado y notificaciones del iPhone ([`docs/40`](docs/40-winmac-connect-fases-1-2.md)).

## 2.0.0 (WinMac 2.0)
- Isla (Dynamic Island) y centro de actividades, capturas y grabación, portapapeles avanzado, espacios de trabajo, extensiones v1, configuración unificada ([`docs/25`](docs/25-isla-2-centro-de-actividades.md)–[`31`](docs/31-configuracion-unificada.md)).

## Fase 1
- Validación de tecnologías, Dock, barra superior, Host y Launchpad/Spotlight ([`docs/01`](docs/01-fase1-resultados.md), [`docs/15`](docs/15-f1-resultados.md)).
