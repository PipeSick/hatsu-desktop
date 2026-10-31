# Guía breve de validación funcional del Dock

Requisito: .NET 10 SDK, `git pull` hecho, PowerShell en la raíz del repo (la carpeta con `src` y `docs`). Sin administrador.

```powershell
dotnet build src/WinMac.Dock.App -c Release
$dock = "src\WinMac.Dock.App\bin\Release\net10.0-windows\WinMac.Dock.App.exe"
& $dock            # arranca el Dock (déjalo corriendo; abre otra PowerShell para los comandos --pin/--list-pinned)
```
Configuración: `%APPDATA%\WinMac\dock.json` · log: `%LOCALAPPDATA%\WinMac\logs\dock.log` · iconos en caché: `%LOCALAPPDATA%\WinMac\icons`.
Para cerrar el Dock: `& $dock --quit`. Anota cada resultado como OK / FALLO + qué viste.

## 1. Anclar y desanclar
1. Abre una app que no esté anclada (p. ej. Paint). Aparece en el Dock con indicador, sin anclar.
2. Clic derecho en su icono → **Anclar al Dock**. Ciérrala: el icono debe **quedarse**.
3. Clic derecho → **Quitar del Dock**. Con la app cerrada, el icono debe **desaparecer**; con la app abierta, queda como temporal.
4. CLI: `& $dock --pin "C:\Windows\System32\mspaint.exe"` y `& $dock --list-pinned` (el Dock en marcha lo muestra en ≤2 s); `& $dock --unpin "C:\Windows\System32\mspaint.exe"`.

## 2. Reordenar arrastrando
1. Arrastra un icono **anclado** a otra posición y suéltalo: los demás se apartan, sin magnificación durante el arrastre.
2. Suelta fuera del Dock / pulsa Esc a mitad: debe volver a su sitio (si Esc no funciona, anótalo).
3. Alternativa: clic derecho → **Mover a la izquierda / derecha**.
4. Un icono temporal (no anclado) no se puede arrastrar.

## 3. Abrir, activar y restaurar
1. App cerrada y anclada: clic → se lanza **una sola** vez (haz doble clic rápido: no debe duplicarse).
2. App abierta detrás de otras: clic → pasa al frente.
3. Minimiza la app: clic → se restaura y queda al frente. Clic derecho → **Abrir** hace lo mismo.
4. Anota si el foco queda de verdad en la app (escribe en ella).

## 4. Varias ventanas de una app
1. Abre 2–3 ventanas de la misma app (Bloc de notas con Ctrl+N, o Explorador).
2. Un solo icono con un solo indicador; clic derecho muestra las ventanas por título.
3. Elige una ventana: se activa **esa**. Minimiza otra y elígela: se restaura.
4. **Cerrar ventana: …** cierra solo esa. **Salir (cierra todas las ventanas)** cierra todas; con un documento sin guardar debe aparecer el aviso de guardar (no se fuerza nada).

## 5. Persistencia
1. Ancla, reordena, cierra el Dock con `--quit`, vuelve a abrirlo: mismo orden y mismas apps.
2. Con el Dock abierto edita `dock.json` (cambia el orden de dos entradas): se recarga en ≤2 s y el log dice "Configuración recargada".
3. Rompe el JSON a propósito (borra una llave): el Dock **no** debe caerse; restaura el archivo y vuelve a cargar.
4. Borra `%LOCALAPPDATA%\WinMac\icons`: los iconos se regeneran sin error.

## 6. MSIX/UWP
1. Averigua un AUMID: `Get-StartApps | Where-Object Name -match "Calculadora|Calculator|Configuración|Settings"`.
2. `& $dock --icon-test "<AUMID>" --out "$HOME\Desktop\uwp.png"`: el PNG debe ser el icono real, nítido y con transparencia.
3. `& $dock --pin "<AUMID>" --title "Prueba UWP"`: aparece con su icono. Clic: debe abrirla; con la app abierta, clic debe **activarla** (no abrir una segunda).
4. Abre la app UWP sin el Dock, luego mira el Dock: debe agruparse en **el mismo** icono anclado (no duplicarse).
5. Si falla el icono o la activación, pega la línea del log (`dock.log`) y el AUMID.

## 7. Pantalla completa
1. Pon un vídeo (YouTube/F11 en el navegador) o un juego en pantalla completa: el Dock se oculta y no tapa nada.
2. Sal de pantalla completa: el Dock vuelve (puede tardar ~1 s).
3. Con una ventana solo maximizada (no pantalla completa), el Dock sigue visible sobre la barra de tareas.

## Qué reportar
Una tabla `punto → OK/FALLO → observación` y, si hay fallo, las últimas líneas de `dock.log`. Las líneas `[dock] …` muestran los tamaños de ventana y búfer.
