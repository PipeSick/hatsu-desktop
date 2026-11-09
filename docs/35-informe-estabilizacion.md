# 35 · Informe de la fase de estabilización de WinMac 2.0

Leyenda: **VALIDADO EN CI** (Windows real del CI, con capturas o registros) · **PROBADO** (pruebas unitarias o de integración) · **SIMULADO** · **NO VERIFICADO** · **PENDIENTE**.
Runner: Windows Server 2025, 4 núcleos, **sin GPU** (Direct2D en WARP), un monitor 1024×768, sin reproductor de música. Detalle técnico en docs/34.

## 1. Resumen
Se investigó y controló la memoria residual de la grabación, se probó la estabilidad con ciclos prolongados, y se completaron los pendientes: portapapeles en
Spotlight, ajustes completos del portapapeles, atajos con su estado real en Ajustes, teclado en la Isla y colocación de ventanas respecto al Dock.
Las letras sincronizadas **no se han tocado** (ningún archivo de letras modificado en esta fase).

## 2. Funcionalidades completadas
| Funcionalidad | Estado |
|---|---|
| Portapapeles en Spotlight (buscar, recientes, fijados, copiar, sin formato, quitar) | VALIDADO EN CI (run 41537664991: 2 resultados reales, vista previa) |
| Tab / Mayús+Tab en Spotlight | PROBADO (unidad); en la captura del CI no se ve el cambio de selección: NO VERIFICADO en ventana real |
| Ajustes del portapapeles: lista de apps excluidas editable, activar/desactivar, estado de persistencia, borrar historial | PROBADO + dibujado en CI (49c) |
| Atajos: estado real, duplicados, combinaciones de Windows, modificador configurable, restaurar | PROBADO + dibujado en CI (49a); el estado vivo solo se ve en la ventana real |
| Teclado en la Isla (Esc, ←/→, Ctrl+1…9, Tab, ↑/↓; solo tras abrirla con un clic) | VALIDADO EN CI con pulsaciones reales |
| Editor y galería entre la barra superior y el Dock | VALIDADO EN CI (34) |

## 3. Defectos corregidos
- Memoria tras grabar: asignaciones grandes por sesión y memoria nativa de Media Foundation a la espera de finalizadores (docs/34 §1).
- Fotograma de muestra decodificando el MP4 completo (caro en CPU y memoria) → miniatura de los propios fotogramas.
- La Isla abierta tapaba la superposición de captura y el editor (se cierra antes).
- La galería no compilaba tras un cambio (variable duplicada), detectado por el CI y corregido.
- Prueba del teclado que no comprobaba el estado inicial de la Isla.
- Subir solo documentación cancelaba la validación en curso (ahora no lanza el CI).

## 4. Commits
19 en WinMac (desde 92ad72d) y 9 en NotchWin (desde acf10e8), cada uno con un cambio coherente; ver `git log`.

## 5. Resultados de CI
Todos los runs de esta fase en verde salvo el 41522145590 (la galería no compilaba; corregido en el siguiente). Último: 41537664991, éxito.

## 6. Pruebas prolongadas
6 ciclos por run (Isla ×18, capturas, editor, grabaciones, Spotlight, cuadrícula, extensión cerrada a la fuerza) en 4 runs: **0 reinicios, 0 latidos perdidos**;
Spotlight visible en 183–232 ms de media; la extensión se reinició sola cada vez. Los «sin latido» antiguos de Test-Notch no se han reproducido en ningún run posterior.

## 7. Memoria antes y después (Barra, 1024×768)
| | Antes de la fase | Ahora |
|---|---|---|
| Tras grabar (WS) | ~670 MB (sin corrección) / ~435 MB (con GC forzado, retirado) | 176–191 MB tras 20 grabaciones, en reposo |
| Privada tras 20 grabaciones | 237 MB y subiendo | 116 → 132 MB, sin tendencia; un GC completo de diagnóstico la deja en 112 MB |
| Montón gestionado | 17 → 74 MB | ~27 MB plano |
| GDI / handles / hilos | — | 14 constante / vuelven / vuelven |

## 8. CPU
Grabando 1024×768 a 15 fps (WARP): de ~20 % a ~10–12 % de un núcleo tras quitar la decodificación del fotograma de muestra. En reposo: host 0,3–0,6 %, Barra 0,8–1,4 %,
Dock 0,5 %, Isla 0,3–0,6 %, extensiones demo ~0 %. Sin GPU real: valores orientativos.

## 9. Grabación
VALIDADO EN CI: 20/20 grabaciones seguidas, MP4 válido y orientado, cierre del archivo ~110 ms, cancelada y fallida (simulada) sin dejar nada retenido, detenida con el
indicador. Cerrar WinMac durante una grabación la detiene y cierra el archivo (código presente; en el CI ocurrió en un run anterior: «deteniendo (cierre de la barra)»).

## 10. Spotlight
VALIDADO EN CI: apps, acciones, calculadora, IP, ping real, «> …» sin ejecutar, porcentajes, portapapeles. SSH y scripts: PROBADO (unidad); NO VERIFICADO en CI
(no hay servidor SSH ni scripts registrados). Búsqueda de archivos de Windows: no disponible en el runner (limitación conocida).

## 11. Portapapeles
PROBADO: detector de secretos, exclusiones, persistencia DPAPI, consulta para Spotlight (también integración real del canal). VALIDADO EN CI: historial y búsqueda desde
Spotlight. NO VERIFICADO: comportamiento con gestores de contraseñas reales.
Nota honesta: en tres runs anteriores Spotlight recibió 0 resultados con la pestaña activa; en el último, con el número de entradas incluido en la respuesta, recibió
los 2 esperados. No se pudo determinar la causa de esos ceros (el diagnóstico de la Isla no llegó a escribirse en esos runs); queda **en observación**.

## 12. Extensiones
VALIDADO EN CI: dos demos como procesos aparte, actividades en la Isla, reinicio tras cierre forzado. PROBADO: manifiestos inválidos, permisos, ids con espacio de
nombres, límites, extensión que abusa (se detiene), canal con token por extensión sin reenvío. Una extensión que no responde (colgada sin cerrarse) no está
cubierta por una prueba real: PENDIENTE. El aislamiento por proceso no es un entorno aislado de seguridad.

## 13. Auditoría visual
Capturas 01–49 en cada run (PNG en el registro). Revisadas: Isla, centro de actividades, galería, editor, Spotlight (vista previa, ping, portapapeles), grabación
(fotograma), espacios de trabajo, extensiones, Ajustes nuevos.

## 14. Capturas reales
En el registro del job «windows-integracion» (líneas `AUDIT_PNG`) y en el artefacto `auditoria-visual-<sha>`.

## 15. Regresiones
Siguen pasando en cada run: Dock, barra, Global Menu (abre y ejecuta), Launchpad, Spotlight, Isla (volumen, brillo, notificaciones, multimedia de prueba), NotchWin bajo
el host, cierre ordenado, capturas, editor, grabación, espacios, extensiones. Pruebas: Bar.Core 402, Host.Core 114, NotchWin.Core 152, Ipc 31.

## 16. Limitaciones
Sin varios monitores físicos ni DPI reales (solo geometría SIMULADA), sin GPU, sin reproductor real ni letras con música real, sin gestores de contraseñas, sin
Windows Search en el runner. Los atajos ocupados por otro programa no dicen cuál; los que una app usa solo en su ventana no se detectan.

## 17. Paquete de distribución
VALIDADO EN CI: zip reproducible (mismo SHA-256 en dos empaquetados), licencias, VERSION, SHA256SUMS, desinstalación limpia (inicio automático y datos). Artefacto
temporal `winmac-paquete-<sha>`. Sin firma (SmartScreen puede avisar). **No se ha publicado ninguna versión.**

## 18. Problemas pendientes
1. Ceros intermitentes del portapapeles en Spotlight (en observación, ver §11).
2. Tab en la ventana real de Spotlight sin evidencia visual.
3. Extensión colgada (sin cerrar) sin prueba real.
4. Validación en un PC real: multimonitor, DPI, GPU, letras con música, gestores de contraseñas.

## 19. Recomendación para una primera versión de prueba
Apta como **versión de prueba (beta privada)** para el propio usuario: la estabilidad en CI es buena y la memoria está controlada. Antes de difundirla: probarla en un PC
real (sobre todo letras con música real, multimonitor y DPI), observar el punto 1 y publicar solo con autorización expresa. No es una versión de producción.
