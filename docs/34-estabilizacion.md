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

## 2. Estabilidad
Ver la sección «Estabilidad prolongada» del informe automático de cada run (ciclos de Isla, capturas, editor, grabación, Spotlight, cuadrícula y una
extensión cerrada a la fuerza).
