# Criterios de aceptación definitivos — Fase 1 y selección de tecnología del Dock

Estado: **definitivos, pendientes de tu firma**. Una vez firmados, cambian solo con tu aprobación explícita.
Distinción obligatoria en todo informe: **Objetivo · Medición real · Hipótesis · Limitación**.

## A. Cierre de la Fase 1 (qué hay que entregar y comprobar)
| # | Criterio | Estado actual |
|---|---|---|
| A1 | Los tres prototipos compilan en CI Windows y en Linux (cruzado, salvo PRI/manifiesto) | Linux: **cumple** [B]; CI Windows: **pendiente** |
| A2 | `Run-Phase1Tests.ps1 -Runs 3` termina sin fallos en **tu** equipo físico y produce JSON de los 5 prototipos/variantes + `summary.md` | **Pendiente** |
| A3 | Capturas `rest/hover/hidden` de cada variante entregadas | **Pendiente** |
| A4 | Calibración contra tus capturas de Tahoe (docs/02-fidelidad.md §3) | **Pendiente** |
| A5 | Repetición con 100 % y ≥ 150 % de DPI, y con 2 monitores si hay | **Pendiente** |
| A6 | Informe con las 4 categorías separadas y sin tecnología declarada ganadora antes de A2–A5 | **Cumple** (esta entrega) |

## B. Umbrales de rendimiento (objetivos; `tools/thresholds.json`)
Se evalúan por prototipo sobre la **mediana de ≥ 3 ejecuciones**, a la tasa de refresco medida (presupuesto = 1000/Hz ms).

| Métrica | Umbral |
|---|---|
| Frames > 1,5× presupuesto, en barrido y en ocultar/mostrar | ≤ 1 % |
| Frame-time p99 (barrido) | ≤ 2× presupuesto |
| CPU en reposo (proceso, normalizado por núcleos lógicos) | ≤ 0,5 % |
| CPU en animación | ≤ 8 % |
| Working set | ≤ 150 MB |
| Costo de CPU de aplicar un frame, p95 (proxy de latencia) | ≤ 0,5× presupuesto |
| Arranque hasta primer render | ≤ 1500 ms |
| Estabilidad | 0 cierres inesperados en todas las ejecuciones |

Verificación externa obligatoria antes de la decisión final: **PresentMon** (frame pacing real) sobre al menos el mejor y el peor candidato.

## C. Puertas y regla de decisión
**Puerta G1 (eliminatoria, rendimiento y estabilidad):** el enfoque cumple B en ≥ 2 de 3 ejecuciones en el equipo físico, a 100 % y a ≥ 150 % DPI. Variante que no pasa G1 queda fuera salvo que el incumplimiento sea atribuible a algo corregible y documentado.

**Puerta G2 (eliminatoria, no invasividad):** solo APIs documentadas para la ruta de fidelidad elegida; sin inyección en procesos ajenos; sin modificar archivos del sistema. (Las interfaces COM de escritorios virtuales son una excepción ya aprobada, aislada y con degradación.)

**Puerta G3 (eliminatoria, convivencia):** sin degradar juegos/pantalla completa (la sonda `SHQueryUserNotificationState` debe poder ocultar la superficie) y sin sustituir el shell. Se verificará en Fase 2.

**Puntuación (solo entre variantes que pasan G1–G3):**
| Factor | Peso | Cómo se mide |
|---|---|---|
| Fidelidad visual | 40 % | Rúbrica 0–3 × 10 atributos (docs/02-fidelidad.md §4), sumada y normalizada |
| Rendimiento y recursos | 25 % | Cercanía al margen sobre los umbrales B (más margen = más puntos) |
| Integración nativa y estabilidad | 15 % | AppBar, multimonitor, DPI mixto, ventanas en pantalla completa: lista de comprobación pasa/falla |
| Mantenibilidad y barrera de contribución | 10 % | Líneas propias, dependencias, nº de conceptos a dominar, licencias compatibles con Apache-2.0 |
| Accesibilidad (UIA, teclado, contraste) | 10 % | Coste estimado de cubrir UIA + comprobación manual con Narrador |

**Regla de decisión:** gana la variante de mayor puntuación. Si dos quedan a < 10 puntos porcentuales, se prefiere la de menor riesgo de
mantenimiento y se documenta. **La decisión solo se toma tras revisar las mediciones reales contigo** (ADR-001). Mientras tanto:
**no hay tecnología ganadora**.

## D. Criterios de aceptación de fidelidad del dock final (referencia futura)
Rúbrica ≥ 24/30 en reposo, hover y ocultar/mostrar, con ningún atributo en 0, comparada contra tus capturas de referencia.
Los atributos que dependan de Liquid Glass real (3, 5) pueden quedar en ≥ 2 mientras se desarrolla F4, con plan documentado.

## E. Lo que NO es criterio
Número de funciones, líneas de código, ni preferencias de herramienta. Los prototipos no son un dock: **superar B no prueba que el dock final lo supere**.
