# Política de privacidad de Hatsu Desktop

**Última actualización:** 5 de noviembre de 2026 · **Editor:** PipeSick · **Contacto:** https://github.com/PipeSick/win11-macos/issues

Hatsu Desktop es software libre (licencia Apache-2.0) que añade a Windows 11 una barra de menús, un Dock y una «isla» de actividad (**Hatsu Notch**), además de un módulo opcional para el iPhone (**Connect**). Esta política explica qué lee Hatsu Desktop de tu equipo, qué guarda y qué sale a Internet. Cada afirmación se comprobó contra el código fuente publicado en https://github.com/PipeSick/win11-macos (y el código de Hatsu Notch incluido en el paquete).

## 1. En resumen
- **No hay cuentas, ni servidores del editor, ni telemetría, ni analítica, ni informes de fallos automáticos, ni publicidad.** El editor no recibe ningún dato tuyo. No encontré en el código ninguna comprobación de actualizaciones propia: las actualizaciones las gestiona Microsoft Store.
- Casi todo se procesa y se guarda **en tu equipo**. Lo único que sale a Internet está en la sección 4, y casi todo solo ocurre si activas la función correspondiente o pulsas un botón.
- Hatsu Desktop **no graba audio, vídeo, pantalla ni lo que escribes**.

## 2. Datos que se guardan en tu equipo
Dentro del paquete de Microsoft Store, las carpetas de datos de usuario de la aplicación son privadas del paquete y se eliminan al desinstalarla. Por compatibilidad conservamos temporalmente los nombres internos `WinMac` y `NotchWin` en estas carpetas.

| Qué | Dónde (nombre interno) | Contenido |
|---|---|---|
| Ajustes | `%APPDATA%\WinMac`, `%LOCALAPPDATA%\NotchWin\settings.json` | Tus preferencias (tema, módulos activos, ciudad elegida, tamaño del Dock, secciones de la isla, etc.) |
| Registros técnicos | `%LOCALAPPDATA%\WinMac\logs`, `%LOCALAPPDATA%\NotchWin\crash.log` | Estados y códigos de error y, en `crash.log`, el texto técnico de los errores no controlados; pueden incluir rutas de archivos y nombres de módulos o aplicaciones. Los registros de Connect solo contienen códigos de error, no contenido de notificaciones. Se quedan en tu equipo; solo salen de él si tú los adjuntas a una incidencia |
| Registro de letras | `%LOCALAPPDATA%\NotchWin\lyrics.log` | Estado de la búsqueda de letras, nombre del reproductor y duración (no título ni artista). Se borra al superar 64 KB. Solo existe si activas las letras |
| Historial de uso de agentes de IA (ver §3) | `%LOCALAPPDATA%\NotchWin\usage-history.json` | Gasto estimado por día (cifras) |
| Historial del portapapeles (opcional) | `%LOCALAPPDATA%\NotchWin\clipboard.dat` | **Solo si activas «conservar historial»** (desactivado por defecto): entradas fijadas y recientes, **cifradas para tu usuario de Windows (DPAPI)**. Las entradas que parecen secretos y las de aplicaciones excluidas no se guardan. Desactivar la opción borra el archivo |

## 3. Funciones que acceden a datos sensibles
Cada una se puede desactivar en Ajustes. Ninguna envía a Internet lo que lee, salvo lo indicado en la sección 4.
- **Teclado y ratón (ganchos de bajo nivel, `WH_KEYBOARD_LL` y `WH_MOUSE_LL`).** Sirven únicamente para detectar si pulsas la tecla Windows **sola** (para abrir el lanzador de Hatsu en lugar del menú Inicio) y si un clic de ratón interrumpe esa pulsación. El gancho examina cada tecla solo para esa decisión; no guarda, registra ni transmite lo que escribes, deja pasar todas las teclas y se puede desactivar. Se detiene al cerrar la aplicación. Además, Hatsu registra atajos globales (`RegisterHotKey`) para abrir el buscador y otras acciones.
- **Portapapeles (Hatsu Notch).** Mientras la isla está en marcha, observa lo que copias (texto e imágenes de hasta 8 MB) para mostrarte un historial en memoria, con límite configurable (50 por defecto). Por defecto **no se escribe en disco**. Omite contenido que parece secreto (claves privadas, tokens conocidos, contraseñas) y copias hechas desde gestores de contraseñas (KeePass, 1Password, Bitwarden, etc.; lista editable). Hay un botón para borrar el historial. La detección de secretos es una heurística y puede fallar: no copies datos extremadamente sensibles si no quieres que aparezcan en el historial.
- **Cámara (Hatsu Notch).** Está **desactivada por defecto**. Si activas la sección «Cámara» y abres su pestaña, muestra una vista previa en vivo en modo compartido de solo lectura. Hatsu Desktop **no graba ni guarda imágenes** y libera la cámara al salir de la pestaña. Windows pedirá permiso.
- **Letras sincronizadas (Hatsu Notch).** Desactivadas por defecto. Ver sección 4.
- **Agentes de IA (Hatsu Notch).** El módulo «Agentes» viene **desactivado por defecto** en las instalaciones nuevas de la versión de Microsoft Store; solo si lo activas tú en Ajustes (y tienes instalados Claude Code, Codex, GitHub Copilot CLI u OpenCode), **lee en tu equipo** los archivos de sesión que esas herramientas guardan en tu carpeta de usuario (`.claude`, `.codex`, `.copilot`, `.local/share/opencode`) para calcular uso y coste estimado. Si el archivo de configuración local de Claude Code (`.claude.json`) los incluye, muestra el correo y el plan de esa cuenta; el código no lee credenciales ni tokens. No usa la red. Puedes desactivarlo de nuevo en Ajustes. Quien ya tenía este módulo activado en una instalación anterior conserva su preferencia.
- **Notificaciones de Windows de otras aplicaciones.** Con la API pública `UserNotificationListener`, solo después de que concedas el permiso en Windows; se muestran en el Centro de notificaciones de Hatsu. Puedes denegarlo o revocarlo en Configuración de Windows.
- **Ubicación.** Solo si pides el clima por ubicación y Windows te lo permite; se usa para elegir tu ciudad.
- **iPhone (Connect, módulo opcional, desactivado por defecto).** Usa Bluetooth con APIs públicas de Windows para leer la batería y las notificaciones del iPhone. El contenido de las notificaciones se mantiene en memoria y no se escribe en disco ni en los registros (los registros solo guardan códigos de error); tú eliges mostrar el contenido completo, solo el título o solo la aplicación.
- **Música y multimedia.** Lee la sesión multimedia que Windows expone (título, artista y reproductor) para mostrarla y controlarla.
- **Ventanas, aplicaciones y archivos locales.** El Dock y la barra enumeran ventanas y aplicaciones abiertas; el buscador (Spotlight) consulta aplicaciones, archivos recientes y el índice de Windows Search; la isla observa las carpetas Descargas y Capturas de pantalla para mostrar el progreso de descargas y tus capturas; la barra muestra lecturas locales del sistema (CPU, memoria, red, discos, temperaturas). Nada de esto se transmite.
- **Estantería (Hatsu Notch).** Lista los archivos que arrastras; la lista es local.
- **Limpieza (Cleanup, opcional).** Analiza categorías conocidas (temporales del usuario, cachés de Windows, volcados y informes de errores, registros antiguos) y **solo elimina lo que confirmas**. Los archivos eliminados **no pasan por la Papelera** (borrado definitivo). Vaciar la Papelera es una acción aparte, con segunda confirmación, nunca marcada por defecto.
- **Barra de tareas de Windows.** Si lo activas, Hatsu la oculta mientras funciona y la restaura al salir.
- **Inicio automático.** Solo si lo activas; puedes cambiarlo también en Configuración › Aplicaciones › Inicio.

## 4. Comunicaciones de red
| Destino | Cuándo | Qué se envía |
|---|---|---|
| `api.open-meteo.com`, `geocoding-api.open-meteo.com` (Open-Meteo) | Al usar el clima o buscar una ciudad | Coordenadas o el texto de la ciudad que buscas |
| `speed.cloudflare.com` (Cloudflare) | Solo cuando pulsas el test de velocidad | Tráfico de medición; tu dirección IP es visible para el servicio como en cualquier conexión |
| `music.163.com` (NetEase Cloud Music) | Solo si activas las letras sincronizadas | Título y artista de la canción que suena (y se usa la duración localmente para elegir el resultado) |
| `lrclib.net` (LRCLIB) | Solo si activas las letras y NetEase no devuelve resultado | Título y artista de la canción que suena; el programa se identifica con su nombre («HatsuNotch») y versión (User-Agent) |

Esos servicios tienen sus propias políticas. Con **Connect**, todo el tráfico es Bluetooth local con tu iPhone. No encontré ninguna otra conexión de red en el código. Como todo cliente de red, esos servicios pueden ver tu dirección IP.

## 5. Permisos que declara el paquete
`runFullTrust` (aplicación de escritorio de Windows), notificaciones de otras aplicaciones, ubicación, Bluetooth y cámara. Cada uno se usa solo para la función descrita arriba y Windows puede pedirte permiso o permitirte revocarlo.

## 6. Menores, conservación y tus derechos
- El editor no recopila datos personales; no puede acceder a tus datos, corregirlos ni borrarlos porque no los tiene. Tú los controlas.
- No está dirigido específicamente a menores.
- **Para borrar todo:** desinstala Hatsu Desktop desde Configuración › Aplicaciones (se elimina el almacén de datos del paquete). Si usaste una versión anterior fuera de la Store, elimina además `%APPDATA%\WinMac`, `%LOCALAPPDATA%\WinMac` y `%LOCALAPPDATA%\NotchWin`.

## 7. Cambios
Si esta política cambia, se publicará aquí con una nueva fecha de actualización.
