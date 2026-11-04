# Política de privacidad de Hatsu Desktop

> **BORRADOR para revisión del propietario. No está publicado.** Cada afirmación de abajo está respaldada por el código del repositorio (referencias entre corchetes); lo que no pude verificar está marcado **[VERIFICAR]** y debe resolverse o quitarse antes de publicar.
> Última actualización: *(fecha de publicación)* · Editor: PipeSick · Contacto: *(correo o página de incidencias de GitHub que elijas)*

Hatsu Desktop es software libre (Apache-2.0) que añade una barra de menús, un Dock y una «Isla» a Windows 11. **No tiene cuentas, no tiene servidores propios y no incluye telemetría, analítica ni informes de fallos automáticos.** Lo que sigue explica qué lee Hatsu de tu PC, qué guarda y qué sale a Internet.

## 1. Lo que NO hace
- No envía telemetría, estadísticas de uso ni informes de errores a nadie. El editor no recibe ningún dato tuyo.
- No usa publicidad ni compartes datos con terceros para fines comerciales.
- No graba audio, ni vídeo, ni pantalla, ni pulsaciones de teclado (ver §3).

## 2. Datos que se guardan en tu PC
- **Ajustes** (tema, módulos activos, ciudad elegida, tamaño del Dock, etc.) en archivos de configuración locales de tu perfil de usuario (`%APPDATA%\WinMac`; empaquetado, el mismo contenido dentro del almacén privado de la aplicación). Son datos tuyos: puedes borrarlos desinstalando la aplicación o eliminando esa carpeta.
- **Registros técnicos** (`%LOCALAPPDATA%\WinMac\logs`): estados y errores del programa para diagnóstico. Se quedan en tu PC; el informe de diagnóstico (`--diagnose`) se genera solo cuando tú lo pides y **no se envía a ningún sitio**.
- **Historial del portapapeles de la Isla:** por defecto solo vive en memoria mientras la Isla está en marcha [NotchWin `ClipboardWatcher`: «Nothing is written to disk»]. Hatsu ofrece un ajuste para conservarlo y límites/exclusiones (omitir contraseñas y aplicaciones concretas, por defecto activado) **[VERIFICAR: qué hace exactamente «conservar historial» en la versión empaquetada antes de publicar esta frase]**. Hay un botón para borrarlo.
- **Notificaciones de tu iPhone** (módulo opcional «Connect», desactivado por defecto): el contenido se mantiene solo en memoria, nada en disco ni en los registros; tú eliges si mostrar el contenido completo, solo el título o solo la aplicación [docs/40].

## 3. Funciones sensibles y por qué las usan
Todas son visibles en la interfaz y se pueden desactivar. Ninguna envía lo que ve a Internet salvo lo indicado en §4.
- **Gancho de teclado de bajo nivel y atajos globales** (`WH_KEYBOARD_LL`, `RegisterHotKey`): solo para detectar la tecla Windows pulsada sola (abrir el lanzador) y tus atajos. Dejan pasar todas las teclas, no guardan ni registran lo que escribes, y se pueden apagar en Ajustes. Mueren con el proceso [`WinKeyHook.cs`].
- **Portapapeles:** la Isla observa lo que copias para mostrarte un historial (texto e imágenes pequeñas). Solo se usa dentro de tu PC.
- **Notificaciones de Windows de otras apps** (API pública `UserNotificationListener`): se muestran en el Centro de notificaciones de Hatsu solo si concedes el permiso; puedes denegarlo en Configuración de Windows [`WinNotificationSource.cs`].
- **Ubicación** (API de Windows): solo si activas el clima por ubicación y lo permites en Windows; se usa para elegir tu ciudad [`WinLocation.cs`].
- **Cámara:** solo para el espejo de la Isla (vista previa en vivo cuando abres esa pestaña). No se graba ni se guarda; se libera al salir de la pestaña [NotchWin `CameraPreviewService`].
- **Bluetooth:** para leer la batería y las notificaciones de tu iPhone en el módulo opcional Connect, con la API pública de Windows.
- **Lista de ventanas y procesos, carpetas de Descargas y Capturas de pantalla:** el Dock y la Isla leen nombres de ventanas/archivos para mostrarlos; no se transmiten.
- **Limpieza (Cleanup):** si usas este módulo, analiza categorías conocidas (archivos temporales, etc.) para proponerte qué limpiar; solo elimina lo que tú confirmas. No transmite nada. **[VERIFICAR contra docs/44 §3 la lista exacta de categorías y si va a la Papelera]**
- **Ocultar la barra de tareas de Windows:** si lo activas, Hatsu la oculta mientras funciona y la restaura al salir.
- **Inicio automático:** solo si tú lo activas (o lo permites en Configuración › Aplicaciones › Inicio).

## 4. Conexiones a Internet (solo estas)
| Destino | Cuándo | Qué se envía |
|---|---|---|
| `api.open-meteo.com` y `geocoding-api.open-meteo.com` (Open-Meteo) | Si usas el clima | Coordenadas o el nombre de la ciudad que buscas |
| `speed.cloudflare.com` (Cloudflare) | Solo cuando pulsas el test de velocidad | Tráfico de prueba de velocidad (tu IP es visible para Cloudflare como en cualquier conexión) |
| `music.163.com` (NetEase) | Solo si activas las letras sincronizadas | Título y artista de la canción en reproducción para buscar la letra |

Esos servicios aplican sus propias políticas de privacidad. Hatsu no usa ningún otro servidor. No hay comprobación de actualizaciones propia (las actualizaciones las gestiona Microsoft Store).

## 5. Menores, retención y tus derechos
- Hatsu no recopila datos personales en servidores del editor, por lo que no hay nada que el editor pueda acceder, corregir o borrar por ti: tus datos están en tu PC y los controlas tú.
- No está dirigido específicamente a menores.
- Para borrar todo: desinstala Hatsu Desktop y elimina `%APPDATA%\WinMac` y `%LOCALAPPDATA%\WinMac` si existen.

## 6. Cambios y contacto
Si esta política cambia, se actualizará aquí con la nueva fecha. Contacto: *(correo / URL de incidencias)*.
