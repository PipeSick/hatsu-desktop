# F1 — host + IPC: resultados, limitaciones y guía de validación en Windows

Estado: **implementado y verificado en el CI de Windows** (run 19, commit `abd1a47`: 3 jobs en verde, 228 pruebas y 26 de 26 comprobaciones de `Test-Host.ps1`, incluidas la Barra y el Dock REALES bajo el host). **Todavía no se ha ejecutado en tu PC**: la guía manual M1–M11 (§5) sigue pendiente. Alcance aprobado: `docs/14`. Sin cambios visuales en el Dock ni en la Barra. Verificación local previa: 26 componentes sin fallos ni advertencias.

## 1. Qué se hizo (tres incrementos, un commit cada uno)
| Incremento | Entrega | Proyectos |
|---|---|---|
| **1a** Canal | Named Pipe del usuario actual, tramas `uint32 + JSON`, sobre versionado y negociación `protoMin/Max`, token por sesión, último valor retenido por tópico, colas acotadas (control con prioridad), cliente con reconexión, `ModuleHostLink` | `WinMac.Ipc` |
| **1b** Host | `Supervisor` (arranque en orden, latido, reinicio 1-2-4…30 s, cortacircuito 5 en 2 min, cierre voluntario sin reinicio, cierre ordenado con escalada), `winmac.json` mínimo, `WinMac.exe` (instancia única, Job Object, bandeja mínima, `--quit`, autoarranque) | `WinMac.Host.Core`, `WinMac.Host`, `WinMac.FakeModule` (solo pruebas) |
| **1c** Módulos | `--hosted` en Dock y Barra (se enlazan, cierran con `shutdown`, avisan `bye`), instancia única de la Barra, `TaskbarCreated` (la Barra recupera su AppBar si Explorer se reinicia), respuesta al latido condicionada a que la interfaz **esté viva** | `WinMac.Dock.App`, `WinMac.Bar.App`, `WinMac.Dock.Win`, `WinMac.Bar.Win` |

Fuera de F1, tal como se aprobó: configuración central, servicios compartidos, instalador, NotchWin.

## 2. Cómo se comporta (reglas que pediste)
| Situación | Resultado |
|---|---|
| El usuario cierra WinMac (bandeja → «Salir de WinMac» o `WinMac.exe --quit`) | El host manda `shutdown` a los módulos **en orden inverso** (Dock → Barra); cada uno cierra por su camino normal (la Barra libera su AppBar). Si alguno no responde en 3 s: petición cortés de cierre a sus ventanas; solo como último recurso se termina **nuestro propio proceso** |
| Falla el Dock o la Barra | El host **no toca al otro**; reinicia al caído con esperas de 1, 2, 4… hasta 30 s; tras **5 caídas en 2 min** lo deja en `Failed` (sin bucle) y lo muestra en el menú de la bandeja |
| El módulo se cierra a propósito (su propio menú «Salir») | Avisa `bye` y sale con código 0: **no se reinicia** |
| Un módulo se cuelga | Si su interfaz deja de avanzar (o el proceso no contesta) 15 s, se le reinicia |
| El host muere de golpe (`taskkill /F`, fallo) | El **Job Object** hace que Windows cierre a los hijos (y libera su AppBar) |
| Se pierde solo el canal (host colgado, reinicio del servidor) | Los módulos **siguen funcionando** y reintentan conectar |
| Dock/Barra sin `--hosted` | Funcionan **exactamente como antes** (independientes) |
| Un segundo `WinMac.exe` | No duplica nada y sale solo; **si hay módulos cerrados o caídos, le pide al host que los reabra**. La Barra también tiene instancia única (el Dock ya la tenía) |
| Cerraste un módulo (o quedó en `Failed`) | Menú de la bandeja → **«Abrir Dock» / «Abrir Barra superior»** (solo aparece si está cerrado o caído); también sirve lanzar `WinMac.exe` otra vez. Reinicia el historial de caídas |
| NotchWin | No interviene: está desactivado por defecto y ausente; nada depende de él |

## 3. Pruebas
| Suite | Pruebas | Dónde corre |
|---|---|---|
| `WinMac.Ipc.Tests` (**27**) | tramas, versiones, token, retención, suscriptor lento, desconexión/reconexión, comprobación de salud, token fuera del entorno | Linux ✔ · Windows ✔ |
| `WinMac.Host.Core.Tests` (**31**) | 22 unitarias (relojes y procesos de mentira) + 9 de extremo a extremo con **procesos reales** y el canal real | Linux ✔ · Windows ✔ |
| `tools/ci/Test-Host.ps1` (**26 comprobaciones**, todas PASS) | `WinMac.exe` real: instancia única, cierre ordenado, host muerto → hijos muertos (Job Object), caída/cortacircuito, módulo sin ejecutable, cierre voluntario, autoarranque (una sola clave), sin puertos TCP, **Barra y Dock REALES bajo el host** | Windows ✔ (solo CI) |
| Dock 110 · Barra 23 · Apps 25 · Common 12 | sin cambios | Linux ✔ · Windows ✔ (antes de F1) |

### Criterios de aceptación de `docs/14`
| Criterio | Estado |
|---|---|
| A1–A5 (tramas, versiones, token, retención/lentos, desconexión) | ✔ Linux |
| A6 (sin puertos TCP) | ✔ `Test-Host.ps1` |
| B1 instancia única · B3 cierre ordenado · B4 host muerto · B8 autoarranque | ✔ `Test-Host.ps1` (B3/B4 también con Barra y Dock reales) |
| B2 módulos habilitados/sin ejecutable · B5 caídas/cortacircuito | ✔ Linux (procesos reales) · ✔ `WinMac.exe` en Windows |
| B6 módulo colgado | ✔ Linux (procesos reales, con la comprobación de interfaz viva); **sin prueba con `WinMac.exe` en Windows** |
| B7 | **Reinterpretado** (ver §4): el canal que se pierde no tumba a los módulos; el host que muere sí los cierra (Job Object), como pediste |
| C1 sin regresiones | ✔ Linux · ✔ CI Windows (Dock 110, Barra 23, Apps 25, Common 12 sin cambios) |
| C2 pruebas nuevas | ✔ |

## 4. Limitaciones y lo que NO está verificado
- **Verificado en el runner de Windows (sin pantalla real ni usuario):** Job Object, `--hosted` de la Barra y el Dock reales, cierre ordenado, instancia única, inicio automático, ningún puerto TCP, y que el icono de la bandeja se crea.
- **Sin verificar todavía (solo tu PC):** el aspecto y el menú de la bandeja, `WM_CLOSE` cortés sobre ventanas reales, **`TaskbarCreated` al reiniciar Explorer**, el inicio automático al iniciar sesión, la Barra con AppBar reservando espacio bajo el host y el Dock con ventanas reales abiertas. Eso es la guía M1–M11.
- **Lo que la comprobación en CI no demuestra:** el runner no tiene pantalla ni apps abiertas, así que «Running» significa «el módulo arrancó, se conectó y contesta», no que se vea bien.
- **B7 y tus aclaraciones:** «si el host termina inesperadamente, los hijos se cierran» (Job Object) y «los módulos funcionan solos sin `--hosted`» son compatibles; lo que **no** hacen los módulos es sobrevivir a la muerte del proceso host. Lo que sí sobreviven es la **pérdida del canal** con el host vivo.
- **Detección de cuelgues:** se comprueba que la interfaz sigue avanzando (el bucle de mensajes) y que el proceso contesta; un menú contextual abierto cuenta como «vivo». No detecta un módulo que avanza pero dibuja mal.
- **Token por variable de entorno** (en vez de herencia de handle, como proponía `docs/13`): el módulo la borra al leerla, así que las apps que lance el Dock no la heredan. El canal solo acepta clientes con ese token y, en Windows, cuyo PID sea el de un módulo lanzado por el host.
- **`PIPE_REJECT_REMOTE_CLIENTS`** (rechazo de clientes por red) lo aplica .NET por defecto según creo recordar, pero **no está verificado [V]**; el pipe además es solo del usuario actual y no se abre ningún puerto TCP (esto último sí lo comprueba el CI).
- **Sesiones múltiples del mismo usuario:** el nombre del canal incluye el número de sesión de Windows para que dos sesiones (p. ej. escritorio remoto) no se mezclen.
- **Si el host muere de golpe**, el Job Object cierra los módulos sin aviso: lo último que el Dock tuviera pendiente de guardar (se guarda con 0,4 s de retardo) podría perderse.
- **Bandeja:** icono genérico de Windows y menú mínimo (estado de cada módulo y «Salir»). No hay interfaz de configuración (F6) ni lectura de `dock.json`/`bar.json` por el host (F3). `winmac.json` se edita a mano.
- **Windows App SDK / NotchWin / instalador:** sin tocar (F4/F5).

## 5. Guía de validación manual en Windows (≈ 15 min)
**Preparación** (una vez; necesita el SDK de .NET 10, el mismo del Dock; abre PowerShell en la carpeta del repositorio, tras `git pull` de la rama):
```
powershell -ExecutionPolicy Bypass -File tools\Build-Local.ps1
```
Deja todo en `dist\WinMac\`. **Cierra antes** cualquier Dock o Barra que tuvieras abiertos (`WinMac.Dock.App.exe --quit`, `WinMac.Bar.App.exe --quit`).

| # | Qué hacer | Qué debe pasar |
|---|---|---|
| M1 | `dist\WinMac\WinMac.exe` | Aparecen la **Barra superior** y el **Dock**. Un icono de WinMac en la bandeja (puede estar en el menú «^») |
| M2 | Clic derecho en el icono de la bandeja | El menú muestra «Barra superior: en ejecución», «Dock: en ejecución», «Isla: desactivado» y «Salir de WinMac» |
| M3 | «Salir de WinMac» | Dock y Barra desaparecen; **las ventanas maximizadas recuperan el espacio de arriba**; el icono de la bandeja se va; en el Administrador de tareas no queda `WinMac*` |
| M4 | Inicia de nuevo; en el Administrador de tareas mata **solo** `WinMac.Bar.App.exe` («Finalizar tarea») | La Barra **vuelve sola** en pocos segundos; el Dock no parpadea ni se reinicia |
| M5 | Con todo en marcha, mata `WinMac.exe` («Finalizar tarea») | **Barra y Dock desaparecen solos** (Job Object); no queda nada |
| M6 | Inicia otra vez y lanza `dist\WinMac\WinMac.exe` una segunda vez | La segunda no hace nada visible y no duplica nada |
| M7 | `dist\WinMac\WinMac.exe --install-autostart`; mira *Configuración → Aplicaciones → Inicio* | Aparece **una** entrada «WinMac». `--remove-autostart` la quita. (Opcional: cerrar sesión y volver a entrar con ella activada) |
| M8 | Con WinMac en marcha: reinicia Explorer (Administrador de tareas → «Explorador de Windows» → Reiniciar) | La barra superior **recupera su espacio reservado** y el icono de la bandeja reaparece |
| M9 | Cierra **solo** la barra desde su menú (clic derecho → «Salir de la barra superior») | **No vuelve** (cierre voluntario); el Dock sigue |
| M10 | Sin permisos de administrador y sin que Explorer se cierre en ningún momento (salvo M8) | Todo lo anterior funciona así |
| M11 | Sin el host: `dist\WinMac\WinMac.Dock.App.exe` y `WinMac.Bar.App.exe` por separado | Funcionan como antes (con sus `--quit`) |
| M12 | Cierra el Dock (o la Barra) desde su menú; clic derecho en la bandeja | Aparece «Abrir Dock» (o «Abrir Barra superior»); al pulsarlo vuelve. También vuelve si lanzas `WinMac.exe` otra vez |

**Si algo falla**, pega las últimas líneas de `%LOCALAPPDATA%\WinMac\logs\host.log` (host), `bar.log` y `dock.log`.
**Para desactivar un módulo** crea `%APPDATA%\WinMac\winmac.json`, por ejemplo `{ "modules": { "dock": { "enabled": false } } }`, y reinicia WinMac.

## 6. Siguiente paso
Cerrada F1 (tu validación M1–M11 y el CI de Windows en verde), se retoma la **barra superior** y, después, F3 (servicios compartidos) y F4 (NotchWin, con los cambios concretos que tú apruebas antes).
