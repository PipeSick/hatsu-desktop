# WinMac 2.0 — Tercera iteración: correcciones de la beta real (WM-022 … WM-027) y CI simplificado

Fecha: 2026-11-03 · Rama `claude/relaxed-turing-cijzko` (WinMac y NotchWin) · **Nada publicado.** Informe anterior: `docs/36-informe-beta-correccion.md` (no se modifica).

## Criterio de esta iteración
**CI verde = compilación y pruebas técnicas correctas.** No significa que la interfaz esté aprobada: la aprobación visual y de interacción la hace el usuario en su Windows real.
En la tabla: **Implementado**, **Validado técnicamente** (qué pruebas automáticas lo cubren) y **Pendiente de prueba real**.

## 1. CI simplificado (definitivo)
| Se mantiene (obligatorio) | Se retira del CI |
|---|---|
| Compilación de todos los proyectos y pruebas unitarias (Windows y Linux) | Auditoría visual con capturas (`Visual-Audit.ps1`) |
| Análisis sintáctico de todos los `.ps1` | Humo visual del Dock (renders PNG, bench), resumen visual |
| Pruebas del host con módulos de prueba (`Test-Host.ps1`, nuevo job `windows-host`) | Captura real de la bandeja y Launchpad simulado |
| Integración con NotchWin: Build-Local, arranque real bajo el host y cierre ordenado, sin capturas | Diagnóstico de letras contra los proveedores |
| Paquete **obligatorio**: reproducible (dos empaquetados, mismo SHA-256), desinstalación limpia y `Verify-Package.ps1` (contenido, autocontenido, sumas, arranque sin ventanas del `WinMac.exe` del ZIP) | Disparadores y diagnósticos temporales (evento de menú del Dock, registros del clic, `WINMAC_QUICKLOOK_IN_CAPTURES`) |

Los scripts retirados siguen en `tools/manual/` (README) como herramientas opcionales.

## 2. Hallazgos
| ID | Causa encontrada | Implementado | Validado técnicamente | Pendiente de prueba real |
|---|---|---|---|---|
| **WM-022** Menú W | El panel arrastraba el menú antiguo; la función de arcos siempre pedía el arco corto (iconos de Apagar, Reiniciar y Suspender deformados: Apagar parecía «+»); 320 DIP para seis mosaicos; fondo poco opaco | Un único Centro de Control (372 DIP): cabecera con avatar, nombre completo (dos líneas si hace falta), sesión y botón «Cuenta»; seis mosaicos reales; volumen y brillo en una tarjeta con su nombre; cinco acciones de energía de 44 DIP con etiqueta; pie con Ajustes de WinMac · Acerca de este PC · Forzar salida. Sin menú antiguo: «Reservar espacio arriba» y «Salir de la barra superior» ya están en Ajustes. Arcos corregidos (todos los paneles). Fondo casi opaco. Teclado: Tab/↑/↓ por cada control, Intro/Espacio, ← → en deslizadores, anillo de foco | Pruebas del modelo: estructura, sin menú antiguo, clics en cada botón de energía, cuenta y enlaces, orden del teclado, altura | Aspecto, contraste, iconos y DPI 100–200 % |
| **WM-023** Bandeja incompleta | WinMac recibe los iconos que las apps registran mientras está en marcha y los de las que responden a «TaskbarCreated». WhatsApp, Spotify, NVIDIA, Riot, Steam y Seguridad no volvieron a registrarse (o corren como administrador). Leer la lista de Explorer exigiría leer su memoria: no se hace | Segundo «TaskbarCreated» a los 10 s; nombre de la app bajo el puntero (tooltip en la línea de título); fila «Ver la bandeja de Windows» que pulsa el botón «Mostrar iconos ocultos» de Explorer por UI Automation (accesibilidad pública) y, si no está expuesto, abre Configuración › Barra de tareas | Pruebas: la fila existe siempre, teclado del desplegable | Cuántos iconos aparecen ahora y que la fila abra la bandeja de Windows 11 (el identificador `SystemTrayIcon` del botón no está documentado) |
| **WM-024** Diálogo blanco | `MessageBox` de Windows | `DarkDialog` (Dock.Win, reutilizable): oscuro, esquinas de DWM, marca de aviso, «Cancelar» por defecto (Intro, Esc y cerrar cancelan), Tab/← →, acción en rojo si destruye. Usado en borrar notificaciones («Eliminar notificaciones»), prueba de velocidad, reiniciar/apagar/cerrar sesión/forzar salida y avisos de la barra | Prueba: el botón dice «Eliminar notificaciones»; compila en el CI | Aspecto y teclado |
| **WM-025** Isla sin notificaciones / sin expansión | La pestaña solo mostraba el historial propio de NotchWin; las de Windows llegaban solo como píldora | La barra envía a la Isla la instantánea del centro de Windows cuando cambia (también con la píldora desactivada); la pestaña la combina con los eventos propios. Clic en la píldora → la Isla se abre en Notificaciones con ese aviso desplegado; cada fila se despliega: texto completo, app, «Abrir <app>». Sin cambios visuales en la Isla ni en la música o las letras | Pruebas en NotchWin (mezcla, texto completo, sin toasts repetidos, ocultar/olvidar, entrada no fiable) y en la barra (instantánea con ids, solo al cambiar, id en la píldora). NotchWin compila en el CI | Que aparezcan las de tu PC, abrir desde la píldora, convivencia con música |
| **WM-026** 0x80070490 al borrar | `UserNotificationListener.ClearNotifications()` falla con «elemento no encontrado» | Si falla, borrado una a una; después se relee Windows y solo cuenta como borrado lo que ya no está; quitar una que ya no existía no es error; mensajes comprensibles y el código COM solo en el registro | Pruebas con la fuente simulada: borrar todo falla → una a una; Windows conserva algunas → se dice cuántas y siguen; ya descartada → sin error | El borrado real en tu Windows |
| **WM-027** Panel de batería plano | — | Cabecera (porcentaje, estado, barra de nivel, potencia o tiempo), tarjetas de salud, «Detalles de batería» desplegable, modos como tarjetas con descripción y selección; casi opaco | Pruebas: datos de tu notebook como caso de prueba (no fijos), sin ciclos inventados, sin tarjetas sin datos, tiempo restante solo con batería | Aspecto y aplicar un modo |

## 3. Qué probar en tu PC (breve)
1. Icono W: un solo panel, sin texto cortado, iconos de energía correctos, «Cuenta», teclado (Tab, Intro, ← →).
2. Bandeja: cuántos iconos ves ahora frente a Windows; «Ver la bandeja de Windows».
3. Centro de notificaciones de WinMac: «Borrar todo» → diálogo oscuro; que se borren o que diga cuántas no pudo.
4. Isla › Notificaciones: lista con las de Windows; clic en una notificación entrante → se abre completa; con música sonando.
5. Batería (portátil): cabecera, tarjetas, «Detalles de batería», cambiar de modo.
6. Regresiones: Dock (menú y espacio reservado), Spotlight, Launchpad, Quick Look, letras.

## 4. Limitaciones conocidas
- Bandeja: no se pueden copiar todos los iconos de Explorer sin leer su memoria; la alternativa es abrir su propia bandeja. Las apps que corren como administrador no reciben el aviso de WinMac.
- «No molestar» sigue sin API pública: el mosaico abre su ajuste.
- La foto de la cuenta no se usa (iniciales).
- La Isla muestra lo que Windows guarda en su centro: lo que una app no deja allí no se puede ver.
- El clic derecho físico en el Dock (WM-017) sigue pendiente de tu prueba.

## 5. Beta privada
Artefacto del CI `winmac-paquete-<sha>` (ZIP autocontenido, verificado). No hay release ni publicación.
