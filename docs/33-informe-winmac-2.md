# 33 · Informe final de WinMac 2.0

Leyenda: **IMPLEMENTADO** (código hecho) · **PROBADO** (pruebas unitarias en Linux/Windows) · **VALIDADO EN CI** (funcionando en Windows real del CI, con capturas o
registros) · **SIMULADO** (geometría o datos de prueba marcados como tales) · **PENDIENTE** · **NO VERIFICADO** (no comprobable sin el PC del usuario).
Evidencia principal: runs 41506791846, 41508563997, 41512913266 y 41514708139 (job «windows-integracion», auditoría visual con PNG en el registro).

## 1. Resumen
WinMac 2.0 añade al shell existente: Dynamic Island con centro de actividades, capturas con galería y editor, grabación de pantalla, Spotlight Pro, portapapeles avanzado,
espacios de trabajo, extensiones con permisos, configuración unificada, medición y paquete de distribución. Todo dentro de los procesos existentes (host, Barra, Dock,
Isla), sin aplicaciones aparte, sin ganchos invasivos, sin inyección, sin dependencias de pago. **Las letras sincronizadas no se han modificado.**

## 2. Estado por etapa
Ver la tabla de `docs/CLAUDE_HANDOFF.md` (se mantiene al día).

## 3. Dynamic Island — VALIDADO EN CI
Pestaña seleccionada azul (estilo ligero de WinUI), 6 pestañas visibles + «Más», cierre a petición de WinMac (orden `collapse`) antes de capturar/grabar/editar,
progreso real en actividades. Navegación por teclado dentro de la Isla: **PENDIENTE** (ventana sin activación por diseño).

## 4. Capturas — VALIDADO EN CI
Región (30), ventana (31), pantalla (32), todas (código + prueba). Pantalla congelada y atenuada, tamaño en píxeles, barra de modos como el mockup. Multimonitor con
coordenadas negativas y DPI mixtos: **SIMULADO** (pruebas de geometría). Corregido: la 2.ª captura colgaba la Barra (procedimiento de ventana de un objeto liberado).

## 5. Galería y editor — VALIDADO EN CI
Galería: búsqueda, orden, multiselección, vista ampliada, acciones reales, papelera (37–39). Editor: anotaciones con ratón y teclado (35) y «Guardar» crea
«… (editada).png» sin tocar el original (registro del run 41514708139). «Guardar como» en hilo propio: IMPLEMENTADO, NO VERIFICADO en CI (diálogo de archivos).

## 6. Multimedia y actividades reales — IMPLEMENTADO + PROBADO
GSMTC real (portada, título, artista, **álbum**, progreso, controles reales) — en el CI no hay reproductor: música **SIMULADA** y marcada «Datos de prueba».
Descargas: velocidad **medida** y navegador de origen; sin porcentaje ni tiempo restante (el total no se conoce: no se inventa).

## 7. Grabación — VALIDADO EN CI
MP4 H.264 sin audio (1,5 MB en 7 s, 1024×768), arranque en 638 ms, indicador visible (excluido del vídeo y por eso de las capturas del CI) que la detiene con un clic real,
actividad en la Isla y en el historial con fotograma (42, orientación correcta tras la corrección).

## 8. Spotlight Pro — VALIDADO EN CI
Acciones de WinMac, IP/red, ping real (19b) y DNS solo con Intro, SSH con argumentos validados, «> …» nunca se ejecuta (19c), scripts con doble Intro, «20% de 150» (19d),
vista previa a la derecha. Divisas y portapapeles en Spotlight: **PENDIENTE**.

## 9. Portapapeles — IMPLEMENTADO + PROBADO; NO VERIFICADO en CI
Detector de secretos, marcas de Windows de gestores de contraseñas, apps excluidas, límite, persistencia opcional cifrada con DPAPI (desactivada por defecto).
Edición de la lista de apps excluidas en Ajustes: **PENDIENTE**.

## 10. Workspace Manager — VALIDADO EN CI
Cuadrícula, guardar, lado a lado y restaurar con 4 ventanas reales (44–46). Otros monitores/DPI: **SIMULADO**. Nunca abre apps salvo `launchPath` del usuario.

## 11. Extensiones — VALIDADO EN CI
Canal propio, token por lanzamiento ligado al PID, sin reenvío entre extensiones, permisos del manifiesto, límites, reinicio acotado. Dos demos con la API real
conectadas como procesos aparte (47). API documentada en docs/30. No es un entorno aislado para código desconocido (documentado).

## 12. Configuración unificada — IMPLEMENTADO + PROBADO
Una ventana de Ajustes con todas las áreas; una configuración anterior carga con valores seguros (prueba). Aviso en Ajustes de atajos en conflicto: **PENDIENTE** (queda en `bar.log`).

## 13. Auditoría y regresiones — VALIDADO EN CI
Barra, Dock, Global Menu (abre y ejecuta), Launchpad, Spotlight, Isla (volumen, brillo, notificaciones, multimedia), NotchWin bajo el host, cierre ordenado: siguen pasando.
Letras: sin cambios; no verificables en el CI (sin reproductor) — **NO VERIFICADO** en ejecución, cubiertas por pruebas de regresión.

## 14. Rendimiento (medido, orientativo: runner sin GPU)
En reposo: host ~0,5–2,5 % de un núcleo y ~45 MB; Barra ~0,8 % y ~147 MB; Dock ~0,5 % y ~100–127 MB; Isla 1,4–10 % y ~170–184 MB; cada extensión demo ~0–3 % y ~30 MB.
Tras grabar, la Barra quedaba en ~670 MB (fotogramas en el montón de objetos grandes): se compacta al terminar (**IMPLEMENTADO, PENDIENTE de medir**).

## 15. Distribución — VALIDADO EN CI (nada publicado)
Zip de 75 MB reproducible (mismo SHA-256 en dos empaquetados), licencias, VERSION, SHA256SUMS, desinstalación limpia comprobada. Sin firma comercial (SmartScreen
puede avisar). **No se ha creado ninguna versión pública**: requiere tu autorización.

## 16. Seguridad, privacidad y limitaciones
Sin ganchos invasivos ni inyección; solo APIs documentadas. Nada graba ni ejecuta sin acción explícita; nunca micrófono; nada se eleva. Portapapeles nunca en registros.
Limitaciones: «No molestar» de Windows sin API; brillo DDC/CI sin avisos; apps con más privilegios no se pueden mover; el CI tiene un solo monitor y sin GPU.

## 17. Pendiente y siguientes pasos
1. Verificar en tu PC: varios monitores/DPI, letras con un reproductor real, portapapeles con gestores de contraseñas, rendimiento con GPU.
2. Portapapeles en Spotlight; lista de apps excluidas en Ajustes; aviso de atajos en conflicto; navegación por teclado en la Isla.
3. Refinamiento visual fino frente al mockup (tipografías y espaciados de la galería y el editor).
4. Publicar una versión solo si lo autorizas.
