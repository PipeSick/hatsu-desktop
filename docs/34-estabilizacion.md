# 34 · Estabilización, memoria y rendimiento (fase posterior a WinMac 2.0)

Todas las cifras salen del runner de Windows del CI (Windows Server 2025, 4 núcleos, **sin GPU**: Direct2D en WARP; un monitor 1024×768).
Son orientativas para un PC real, pero sí sirven para comparar antes y después y para ver tendencias.

## 1. Memoria residual de la grabación

### Investigación
| Run | Qué había | Resultado |
|---|---|---|
| 41514708139 | 2 arrays de 3 MB por fotograma (copia + volteo), nuevos en cada fotograma | Barra ~670 MB tras grabar |
| 41516149777 | igual + `GC.Collect` forzado al terminar | ~435 MB (se retira: ocultaba el problema) |
| 41518254400 | pool acotado de fotogramas + DIB de abajo arriba reutilizado | 10/10 grabaciones, 1 búfer por grabación, 0 fotogramas saltados; privada 109 → 167 MB en 11 grabaciones |
| 41520038459 | igual, 20 grabaciones + memoria gestionada en el registro | montón gestionado **17 → 74 MB**, «en uso» 14 → 79 MB, solo 7 GC gen2: ~3 MB por grabación = **un fotograma 1024×768** |
| 41522145590 | fotograma congelado de la superposición reutilizado + pool conservado entre grabaciones | ver tabla siguiente |

**Causa**: no era una fuga de recursos nativos (GDI constante, handles e hilos estables), sino **asignaciones grandes por sesión** en el montón de objetos
grandes de .NET: el fotograma congelado de la pantalla en cada captura/grabación (3 MB) y un pool nuevo por grabación. Como el montón de objetos grandes solo
se recoge en GC completos y la Barra apenas asigna en reposo, esa basura se acumulaba. Corrección: reutilizar ambos (sin `GC.Collect`).

### Resultado (run 41522145590; la Isla no compiló en ese run, pero la medición es de la Barra)
| Momento | Working Set | Privada | Hilos | Handles | GDI | USER |
|---|---|---|---|---|---|---|
| reposo inicial | 143,5 | 135,0 | 35 | 1206 | 48 | 42 |
| +1 grabación | 155,2 | 146,6 | 45 | 1253 | 48 | 51 |
| +5 | 156,8 | 150,1 | 43 | 1249 | 48 | 52 |
| +10 | 160,1 | 154,5 | 43 | 1308 | 48 | 55 |
| +20 | 170,1 | 166,9 | 44 | 1285 | 48 | 54 |
| 20 s de reposo | 145,3 | 142,3 | 31 | 1251 | 48 | 44 |
| tras una cancelada | 149,3 | 147,2 | 32 | 1254 | 48 | 44 |
| tras una fallida (simulada) | 149,8 | 147,2 | 31 | 1247 | 48 | 43 |
| otros 20 s de reposo | 145,2 | 143,3 | 28 | 1224 | 48 | 39 |
| tras GC completo (solo diagnóstico) | 141,9 | 138,1 | 28 | 1119 | 48 | 39 |

Memoria gestionada durante las 20 grabaciones: montón 26,4 → 29,7 MB, en uso 18,9 → 21,3 MB (plano). El GC completo de diagnóstico solo libera 3 MB:
**no hay memoria retenida**. Durante la grabación la Barra sube ~10–25 MB (el codificador de Windows) y en reposo vuelve a la cifra inicial.
**Criterio**: tras 20 grabaciones, en reposo, Working Set dentro de ±5 % del inicial y montón gestionado sin tendencia: cumplido (145 MB frente a 143,5 MB).
Cierre del archivo tras detener: 224–328 ms de media. CPU durante la grabación (1024×768, 15 fps, WARP): 16–22 % de un núcleo.

### Segunda causa: memoria nativa a la espera de finalizadores
Con lo anterior, la memoria gestionada quedó plana, pero la privada todavía podía saltar (run 41528267129: 110–122 MB durante 15 grabaciones, 212 MB a las 20) y un
GC completo de diagnóstico la devolvía **exactamente** al valor inicial (116 MB). Era la canalización de Media Foundation (búferes del codificador) que pertenece a
objetos WinRT cuyos envoltorios .NET solo la sueltan al finalizarse. Correcciones: el fotograma de muestra ya no se obtiene decodificando el MP4 con
`Windows.Media.Editing` (ahora es una miniatura de 320 px tomada de los propios fotogramas; además la CPU de grabación bajó de ~20 % a ~12 % y el cierre del archivo de
~320 a ~110 ms) y, al terminar, se sueltan las referencias nativas de `MediaStreamSource`, `MediaTranscoder` y el resultado de preparación. Nada de GC forzado.

## 2. Estabilidad
Prueba prolongada en cada run (`Visual-Audit.ps1`, sección 1i): 6 ciclos de abrir/cerrar la Isla ×3 y cambiar de pestaña, captura de pantalla, abrir y cerrar el
editor, grabación de 2 s, Spotlight (abrir, escribir, cerrar), cuadrícula de ventanas y, en el ciclo 3, **cerrar a la fuerza un proceso de extensión**.

| Run | Duración | Reinicios / latidos perdidos | Errores en bar.log | Spotlight (orden → visible) | Extensión cerrada |
|---|---|---|---|---|---|
| 41520038459 | 99 s | 0 | 1 (no identificado en ese run) | 221 ms media, 234 máx. | reiniciada sola (1/3) y reconectada |
| 41525142172 | 121 s | 0 | 0 | 232 ms, 331 máx. | reiniciada y reconectada |
| 41528267129 | 95 s | 0 | 1: «Windows Search no disponible» (limitación del runner) | 190 ms, 203 máx. | reiniciada y reconectada |

Los reinicios por «sin latido» vistos antes en Test-Notch (run 41510942250) **no se han vuelto a reproducir** en 6 runs posteriores. Sí se identificó y corrigió una
causa real de bloqueo de la Barra: los diálogos modales (MessageBox, Guardar como) en su hilo detenían su latido y el host la reiniciaba; ahora no hay ninguno en
ese hilo. La otra causa de reinicio corregida: el procedimiento de ventana de la primera superposición de captura, ya liberado, que colgaba la segunda captura.

## 3. Teclado en la Isla
Validado con pulsaciones reales (run 41528267129): clic ⇒ «teclado activado primer-plano=True» ⇒ → cambia de sección ⇒ Esc la cierra. En el run 41525142172 falló
la prueba, no la Isla: no comprobaba que la Isla estuviera cerrada antes del clic y ese clic pudo cerrarla en vez de abrirla. Ahora lo comprueba y lo registra.
