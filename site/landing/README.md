# Landing de Hatsu Desktop

Sitio estático de una sola página: `index.html` (HTML y CSS en línea, sin scripts ni fuentes externas) y `img/`. Se sirve tal cual desde cualquier servidor web (nginx en un contenedor, Cloudflare Pages, GitHub Pages…): basta con apuntar la raíz del sitio a esta carpeta.

- Claro y oscuro automático (`prefers-color-scheme`), adaptable a móvil.
- La ilustración del encabezado **no** es una captura real; cámbiala por capturas reales cuando las haya.
- Los enlaces a la política de privacidad apuntan al repositorio; cámbialos por la URL publicada (GitHub Pages o tu dominio) cuando exista.
- El botón de la Microsoft Store usa el formato estándar `https://apps.microsoft.com/detail/9NJ0XLP6NXVJ` (ID de producto de `installer/msix/store-identity.json`). **No lo he podido abrir desde el entorno de desarrollo**: compruébalo. Si la app está con audiencia privada, ese enlace solo lo abrirán quienes estén en el grupo.
- No afirmes funciones sin validar: los módulos se están validando en Windows real y el texto lo dice.
