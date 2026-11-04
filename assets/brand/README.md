# Recursos de marca de Hatsu Desktop

**Estado: PROVISIONAL.** Los archivos de esta carpeta son un recurso temporal, generado a propósito para que el producto tenga una identidad coherente mientras se decide el diseño definitivo. **No es un logotipo aprobado** y debe poder sustituirse sin tocar código.

| Archivo | Uso |
|---|---|
| `hatsu-provisional.ico` | Icono de los ejecutables (`ApplicationIcon`: Host, Barra, Dock, Connect) y de la bandeja. Tamaños 16–256 px. |
| `hatsu-provisional-512.png` | Vista previa / README. |

Descripción: «H» blanca redondeada sobre un cuadrado de esquinas suaves con degradado lavanda → violeta y un brillo tenue de cristal. Dibujada desde cero (sin logotipos de Apple ni de Microsoft ni iconografía de terceros). Funciona sobre fondo claro y oscuro porque lleva su propio fondo.

**Para sustituirlo:** reemplaza `hatsu-provisional.ico` por el definitivo (mismo nombre, o cambia la ruta `ApplicationIcon` de los cuatro `.csproj` y la prueba `RebrandingTests`). El logotipo de la barra superior es un glifo «H» de texto (`BarPainter.DrawLogo`).
