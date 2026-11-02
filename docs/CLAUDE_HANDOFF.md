# Traspaso para Claude (léelo primero tras un /clear)

Actualizado: 2026-11-02. Complementa `docs/ESTADO-SESION.md` (contexto anterior: repos, PC del usuario, reglas de trabajo).

## Plan en curso (orden obligatorio, pedido por el usuario)
1. **NotchWin: volumen, brillo, notificaciones y prioridades** — implementado (docs/21). Ver «Estado por etapa».
2. Launchpad y tecla Windows (índice de apps reutilizable).
3. Spotlight (reutiliza el índice y el lanzador del Launchpad).
4. Global Menu, opción A (menús de la app activa en la barra, sin quitar los originales).
5. Validación general y este traspaso con el informe final.

Reglas: commits separados por etapa; validar en el CI de Windows; nada de hooks invasivos, inyección ni cambios permanentes de Windows;
nunca tocar resolución, monitores, red, audio ni reiniciar el PC del usuario; documentar lo que no se puede probar en CI.

## Cómo trabajar
- `apt-get install -y dotnet-sdk-10.0` si falta. Pruebas de lógica en Linux: `dotnet test src/WinMac.Bar.Core.Tests` (y Host.Core, Dock.Core, Apps.Core…), `dotnet test /home/user/notch-win/tests/NotchWin.Core.Tests`.
- NotchWin (WinUI) solo compila en Windows: lo valida su CI (`PipeSick/notch-win`, workflow `build`) y el job de integración de WinMac, que compila el commit de `tools/notch-pin.txt`.
  Al cambiar NotchWin: push a su rama `claude/relaxed-turing-cijzko`, luego actualizar `tools/notch-pin.txt` en WinMac.
- Logs del CI: `mcp__github__get_job_logs` con `tail_lines` pequeño (los registros con imágenes en base64 son enormes).

## Estado por etapa (informe)
| Etapa | Implementado | Compilado | Probado automáticamente | CI de Windows (real) | Pendiente en el PC |
|---|---|---|---|---|---|
| 0. Bandeja (sesión anterior) | sí | sí | sí | run 75: 3/3 iconos en el desplegable | clics con apps reales |
| 1. Isla: volumen, brillo, notificaciones, prioridades | sí | sí (NotchWin CI + Build-Local) | 18 (NotchWin) + 7 (WinMac) | run de 4d8c7d3: integración en verde (barra real → host → Isla real con secuencia de muestra) | teclas de volumen/brillo, notificación real, aspecto |
| 2. Launchpad + tecla Windows | sí | sí | 19 | 11/11 (índice real de 126 apps, abrir, «notepad»+Intro, Esc) | tecla Windows física, varias pantallas |
| 3. Spotlight | sí | sí | 22 | 6/6 (12*12 → 144, notepad, Esc). Windows Search NO disponible en el runner (0x80041820): solo recientes | atajo físico, Windows Search con archivos reales |
| 4. Global Menu (opción A) | sí | sí | 5 | run de 8b65a09 en verde (barra real descubre los menús del Bloc de notas) | clic en Bloc de notas, Paint, VS Code |

Commits principales: NotchWin 14c374b, 58d14d9, e810ec0, 5bb75b9, 7b87512; WinMac c529e5c, 9a50ec4, 4a0b889, 21c9540, c4ad5ec, 1cca724, 4d8c7d3 y el de este traspaso.

Causa de los cierres de la Isla en el CI: `AppWindow.IsShownInSwitchers = false` lanza NotImplementedException en Windows Server 2025 (código ANTERIOR a esta sesión); ahora se tolera (NotchWin 7b87512). NotchWin escribe `%LOCALAPPDATA%\NotchWin\crash.log` si se cierra.

## Auditoría visual (docs/24, run 41457877425)
Capturas reales del runner con WinMac completo (`tools/ci/Visual-Audit.ps1`, artefacto `auditoria-visual-<sha>`; las PNG también van en el registro como
`AUDIT_PNG|nombre|base64` y se pueden decodificar descargando el registro del job con `curl -sL https://api.github.com/repos/PipeSick/win11-macos/actions/jobs/<id>/logs`).
Correcto a ojo: Isla (cerrada, volumen, silencio, brillo, notificación, regreso, expandida), Launchpad (fondo, cuadrícula, búsqueda), Spotlight (todos los estados), menús en la barra.
Problemas (sin corregir, a decidir con el usuario): **P0 la píldora de la Isla puede quedarse atascada (temporizador que vence antes de hora; CI intermitente en rojo por esto)**; **P1 el desplegable de Global Menu no se abre**; P2 la Isla expandida no se cerró con ratón simulado; P3 iconos de Launchpad
lentos o ausentes; P4 «System» de Windows Terminal como menú; P5 coincidencias flojas en Spotlight; P6–P8 decisiones de diseño (Isla negra con barra clara, Launchpad tapa la barra,
Dock sobre la barra de tareas).

## Corrección de los defectos de la auditoría (docs/24 §Correcciones)
P0, P1, P3, P4 y P5 corregidos y verificados con capturas reales; P2 no es un defecto (era el ratón simulado). Commits: NotchWin cc855be; WinMac 0619ef5, 66f671e, 2c9a241,
f567e5f, 4e3982b, fae2bb8/3d4673d, cefedf1 (más los de diagnóstico). Diagnóstico solo-CI disponible: WINMAC_MENU_DIAG (posiciones, apertura de paneles y quién toma el foco,
barras de menús accesibles), «CIERRE INESPERADO» y «ERROR» en bar.log, crash.log de NotchWin.

## WinMac 2.0 — fase 1 (docs/25)
Centro de actividades en la Isla: modelo `NotchWin.Core/Activities` (almacén, API interna `notch.activity`, `CompactPolicy` con la regla de las
letras intacta, `CaptureLibrary`), interfaz `NotchWindow.Activities.cs` + XAML (encabezado multimedia, pestañas En curso/Capturas/Historial),
fuentes reales en `App.Activities.cs`. Auditoría visual secciones 20–29 en `Visual-Audit.ps1`. NotchWin fijado en 02c2613.
**Letras sincronizadas: NO tocar** (petición expresa del usuario); el código de letras está en NotchWindow.Extras.cs y Core/Lyrics.
Pendiente: píldora azul de la pestaña seleccionada (sale gris), motor de capturas (siguiente fase: WinMac Screenshot).

## WinMac 2.0 completo — plan en curso (petición del usuario, orden de ejecución sin pausas)
1. Correcciones de la Isla (pestaña azul, navegación adaptable)  2. WinMac Screenshot (región/ventana/pantalla/monitor/todos, galería, editor)
3. Multimedia y actividades reales  4. Grabación  5. Spotlight Pro  6. Portapapeles avanzado  7. Workspace Manager  8. Extensiones
9. Configuración unificada  10. Refinamiento visual  11. Auditoría integral  12. Optimización  13. Distribución (sin publicar nada).
Prioridad absoluta: NO tocar las letras sincronizadas (NotchWindow.Extras.cs, Core/Lyrics). Estado de cada etapa: ver la tabla de abajo (se actualiza).

### Estado de las etapas (actualizado al subir cada cambio)
| Etapa | Estado | Dónde | Evidencia CI |
|---|---|---|---|
| 1 Isla: pestaña azul, 6 visibles + «Más» | VALIDADO EN CI | NotchWindow.Sections.cs (TabLook, MaxPrimaryTabs) | capturas 25–29 (run 41486008568) |
| 2 Capturas región/ventana/pantalla/todas | VALIDADO EN CI (1 monitor; multimonitor SIMULADO en pruebas) | Bar.Core/Capture, Bar.Win/Capture, BarHost.Capture.cs | 30–32 (run 41506791846); bug «2.ª captura cuelga la barra» corregido (InstanceWindows) |
| 2 Galería completa (buscar, ordenar, multiselección, ampliar, renombrar, papelera) | VALIDADO EN CI (abrir, buscar, seleccionar, ampliar) | notch-win Views/GalleryWindow.cs, Core CaptureLibrary | 37–39 |
| 2 Editor (recortar, lápiz, flecha, rect., texto, resaltar, deshacer, copiar, guardar copia, guardar como) | Dibujo VALIDADO EN CI; guardar: corregido en 75d1558, PENDIENTE de verificar | Bar.Core/Capture/CaptureEdit.cs, Bar.Win/Capture/CaptureEditor.cs | 34–36 |
| 3 Descargas: velocidad medida y origen; álbum en el encabezado | IMPLEMENTADO + PROBADO (unidad); sin ETA (el total no se conoce) | notch-win DownloadTracker, GsmtcMediaProvider | — |
| 4 Grabación MP4 sin audio, indicador excluido del vídeo, actividad en la Isla | IMPLEMENTADO; PENDIENTE de CI (el run anterior no llegó por el reinicio de la barra) | ScreenRecorder.cs, RecordingIndicator.cs, BarHost.Record.cs | 40–43 |
| 5 Spotlight Pro (acciones, IP/red, ping/DNS con Intro, SSH, «> …» nunca ejecuta, scripts con doble Intro, %, vista previa) | VALIDADO EN CI (ping real 127.0.0.1) | Bar.Core/Launcher/SpotPro.cs, BarHost.SpotPro.cs, SpotlightWindow.DrawPreview | 16–19d |
| 6 Portapapeles: secretos, exclusiones, límite, persistencia DPAPI opcional | IMPLEMENTADO + PROBADO (unidad); NO VERIFICADO en CI | notch-win Core/Clipboard/ClipboardPrivacy.cs, App.Clipboard.cs; host NotchSettings.Clipboard* | — |
| 7 Workspace Manager | IMPLEMENTADO + PROBADO (geometría simulada); PENDIENTE de CI | Bar.Core/Workspaces, Bar.Win/Workspaces, BarHost.Workspaces.cs | 44–46 |
| 8–13 | PENDIENTE | — | — |

Notas: los diálogos modales en el hilo de la barra detienen su latido y el host la reinicia → nunca usar MessageBox/GetSaveFileName en ese hilo.
Órdenes de prueba de la barra: `--capture region|window|screen|all`, `--record region|window|screen|stop`, `--edit <ruta>`, `--workspace save|restore|side|grid`.

## Siguiente paso exacto
1. Validación manual en el PC (lista en docs/24). P6–P8 son decisiones de diseño pendientes de que el usuario decida.
2. Validación manual en el PC (docs/21 §5, docs/22, docs/23).
3. Mejoras posibles: submenús con estilo propio para apps UI Automation; Win11 «No molestar» (sin API documentada).

## Limitaciones conocidas
- «No molestar» de Windows 11 no tiene API documentada (solo pantalla completa/presentación se respetan).
- Brillo de monitores externos (DDC/CI): sin aviso de cambios; solo el deslizador de la barra.
- Alt+Espacio sustituye al menú de ventana de Windows mientras WinMac está abierto (configurable).
- Tecla Windows: el gancho no ve pantallas seguras (UAC) ni ventanas de administrador si WinMac no lo es.
- Global Menu: apps sin barra de menús (Office, navegadores) solo muestran su nombre; en apps UI Automation el desplegable es el de la propia app.

## Dónde está cada cosa (nuevo en esta sesión)
- Isla: `notch-win/src/NotchWin.Core/Pill/` (árbitro, formato), `NotchWindow.Pill.cs`; barra: `BarHost.Notch.cs`, `AudioService.Watch.cs`, `BrightnessService.Watch.cs`, `Bar.Core/Notch/NotchFeed.cs`.
- Launchpad/Spotlight: `Apps.Core/Catalog/` (índice, búsqueda, preferencias), `Apps.Win/AppsFolderReader.cs` (lectura + lanzador), `Bar.Core/Launcher/` (tecla Windows, controladores, motor de Spotlight),
  `Bar.Win/Launcher/` (gancho, atajo, ventanas, Windows Search), `BarHost.Launcher.cs`, `BarHost.Spotlight.cs`.
- Ajustes nuevos: `NotchSettings.Pill*`, `BarConfig.LaunchpadOnWinKey/SpotlightHotkey/SpotlightFiles` (página de ajustes del host).
- Pruebas solo-CI: variable `WINMAC_PILL_SELFTEST` (Isla), `WinMac.Bar.App --launchpad/--spotlight` (abrir desde fuera).
