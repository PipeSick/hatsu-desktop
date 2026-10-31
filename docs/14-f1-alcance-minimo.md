# F1 — alcance mínimo, criterios de aceptación y pendientes

Estado: **preparación, sin implementar.** Parte de la arquitectura aprobada en `docs/13` (commit `a7c9738`) y de las decisiones del usuario del 31-oct-2026.
Principio rector del usuario: **WinMac como escritorio estilo macOS Tahoe, sin infraestructura innecesaria.** Por eso F1 se recorta respecto al plan original.

## 1. Estado de las decisiones D1–D8
| # | Decisión | Estado tras tus instrucciones |
|---|---|---|
| D1 | Dónde vive NotchWin | **Resuelta**: componente externo fijado → monorepo con historial |
| D2 | Runtime .NET 8 vs 10 | **Resuelta como «evaluar, no migrar todavía»**: ver §4 (resultado de la evaluación) |
| D3 | Modificar NotchWin | **Resuelta con condición**: autorizada para F4, pero **tú apruebas los cambios concretos antes**; se te entregará una lista/parche para revisar |
| D4 | Instalador y actualizador | **Parcial**: fijaste «un instalador, un inicio, una configuración»; **falta elegir herramienta** (Inno Setup o WiX; Velopack u otro) y la vía de firma. **No bloquea F1; se decide antes de F5** |
| D5 | Notificaciones del sistema | **Resuelta**: fuera de la v1; solo eventos propios |
| D6 | Al cerrar WinMac | **Resuelta**: se cierran los módulos; «dejar abiertos» como opción en F6 (ya lo habías pedido así) |
| D7 | Tecnología de la UI de configuración | **Abierta, no urgente**: se decide al llegar a F6; hasta entonces basta archivo + menú de bandeja |
| D8 | Marcas | **Supuesto mío (confirma o corrige)**: la interfaz y los textos del producto no usan marcas de Apple («isla»/«notch», no «Dynamic Island»); los documentos solo las mencionan para describir |

Además asumí, para el `LICENSE`: titular **«WinMac contributors»** (igual que NotchWin con «NotchWin contributors»). Si prefieres tu nombre o el de una organización, se cambia en `NOTICE`.

## 2. F1 mínimo («host + IPC» que ya se puede usar)
F1 fusiona lo que en `docs/13` eran F1 y F2, **sin** lo que no hace falta para tener Barra y Dock bajo un solo arranque.

### Dentro de F1
1. **`WinMac.Ipc`** (net10, librería): tramas `uint32 LE + JSON UTF-8` (tope 1 MiB); sobre con versión; mensajes `hello` / `hello_ack` / `err` / `sub` / `pub` / `evt` / `ping` / `pong` / `shutdown` / `bye`; negociación `protoMin/protoMax`; campos desconocidos ignorados; **último valor retenido por tópico**; cola acotada por suscriptor (descarta lo antiguo); servidor y cliente sobre Named Pipe `WinMac.<SID>` solo del usuario actual; **token por sesión** (variable de entorno del proceso hijo, no línea de comandos); comprobación del proceso par en Windows; cliente con reconexión y *backoff*.
2. **`WinMac.exe` (host)**: instancia única (mutex `Local\WinMac.Host`; un segundo arranque sale sin duplicar nada); lee `%APPDATA%\WinMac\winmac.json` mínimo (`modules.dock/bar/notch.enabled`; **notch desactivado por defecto**; `autostart`); lanza **Barra → Dock** en un **Job Object** (`KILL_ON_JOB_CLOSE`); latido cada 5 s; reinicio con *backoff* 1-2-4…30 s ante cierre anormal; **cortacircuito** a las 5 caídas en 2 min (queda en `Failed`, sin bucle); un cierre limpio (`bye` + código 0) **no** se reinicia; parada ordenada (`shutdown`, ≤ 3 s por módulo, orden inverso); icono de bandeja mínimo con «Salir de WinMac»; `--quit`, `--background`, `--install-autostart` / `--remove-autostart` (una sola clave `HKCU\…\Run`); registro en `%LOCALAPPDATA%\WinMac\logs\host.log`.
3. **Barra y Dock `--hosted`**: se conectan, publican `module.ready`, responden al latido, cierran normalmente con `shutdown`; si el host desaparece **siguen funcionando solos** y reintentan conectar. Sin `--hosted` se comportan **exactamente como hoy**.
4. **Barra: `TaskbarCreated`** (si Explorer se reinicia, vuelve a registrar su AppBar): arreglo de robustez que ya estaba pendiente.

### Fuera de F1 (explícito)
Configuración central y migración (F3) · servicios compartidos y tópicos de datos (F3) · `req/res` · DPAPI/secretos · UI de configuración · instalador/actualizador/firma (F5) · **cualquier cambio en NotchWin** (F4) · `island.state` y hueco dinámico (F4) · varios monitores · notificaciones.
**Decisión de diseño recortada:** el token viaja por **variable de entorno** del hijo (no por herencia de handle como proponía `docs/13`): más simple y suficiente para el modelo de amenaza (mismo usuario); se endurece si hiciera falta.

### Orden de trabajo (cada paso se entrega y se aprueba por separado; la barra no espera)
1a `WinMac.Ipc` + pruebas → 1b host (lanza, vigila, cierra) con módulos de mentira → 1c `--hosted` en Barra y Dock + `TaskbarCreated`. En paralelo, y sin depender de F1: la validación manual de la barra (B1–B12) y sus ajustes.

## 3. Criterios de aceptación de F1
**Automáticos (CI; ✔ = también en Linux)**
| # | Criterio |
|---|---|
| A1 ✔ | Tramas: ida y vuelta, lecturas parciales, JSON inválido y tramas > 1 MiB se rechazan sin caída |
| A2 ✔ | Versionado: rango compatible acepta; sin solape → `err` y cierre; campos y mensajes desconocidos se ignoran con registro |
| A3 ✔ | Token incorrecto → conexión rechazada sin enviar datos; correcto → acepta |
| A4 ✔ | Último valor retenido: un suscriptor tardío lo recibe al instante; uno lento **no bloquea** al que publica; la cola no crece sin límite |
| A5 ✔ | Desconexión: el cliente lo detecta ≤ 3 s al caer el servidor y reconecta al volver; el servidor libera al cliente muerto |
| A6 (Windows) | El canal es del usuario actual y **no se abre ningún puerto TCP** (se compara la lista de puertos en escucha antes y después) |
| B1 (Windows) | Segundo `WinMac.exe` termina en ≤ 2 s sin duplicar módulos |
| B2 (Windows) | Se respetan los `enabled`; **con `notch` desactivado o ausente todo funciona**; un módulo sin ejecutable queda en `Failed` y **los demás siguen** |
| B3 (Windows) | `--quit`/«Salir»: cada módulo termina ≤ 3 s por su camino normal; la barra libera su AppBar; no queda ningún proceso de WinMac |
| B4 (Windows) | Si el host se mata de golpe (`taskkill /F`), los hijos desaparecen en ≤ 2 s (Job Object) |
| B5 (Windows) | Cierre anormal → reinicio con *backoff*; 5 en 2 min → `Failed` sin bucle; cierre limpio → no se reinicia |
| B6 (Windows) | Módulo sin latido 15 s → se reinicia |
| B7 | Si el host cae, los módulos siguen funcionando solos |
| B8 (Windows) | `--install-autostart` crea **una** clave `Run`; `--remove-autostart` la borra y no toca nada más |
| C1 | Se mantienen verdes Dock (110), Barra (23), Apps (25) y Common (12); el CI de Windows pasa; Dock y Barra **sin `--hosted` se comportan como antes** |
| C2 | Pruebas nuevas de IPC y de ciclo de vida (con procesos de mentira) incluidas en CI |

**Manuales en tu PC (≤ 10 min)**
M1 Abrir `WinMac.exe`: aparecen Barra y Dock. · M2 «Salir de WinMac»: desaparecen y el espacio superior se libera. · M3 Matar `WinMac.exe` desde el Administrador de tareas: Barra y Dock se van solos. · M4 Cerrar solo la barra (matarla): vuelve en pocos segundos. · M5 Activar/desactivar el inicio automático y comprobar en «Aplicaciones de inicio» de Windows que hay **una** entrada. · M6 Reiniciar Explorer: la barra recupera su espacio. · M7 Todo sin permisos de administrador y sin que Explorer se cierre.

**Hecho cuando:** A1–C2 en verde en CI, M1–M7 aprobadas por ti, documentación actualizada y commits autorizados por ti. **No** cambia ningún aspecto visual del Dock ni de la barra.

## 4. Evaluación .NET 8 vs .NET 10 (sin migrar nada)
- **WinMac:** todo en .NET 10.
- **NotchWin:** `NotchWin.Core` es lógica pura. **Probado hoy** en una copia aparte, sin tocar su repo: cambiando solo el `TargetFramework` a `net10.0` **pasan sus 41 pruebas**. La app WinUI 3 (net8 + Windows App SDK 1.6.250108002) **no se puede compilar aquí** (exige Windows + MSBuild de Visual Studio): su paso a .NET 10 queda **sin verificar** [V]. Dato a favor: el prototipo S3 de WinMac compiló en CI con .NET 10 y Windows App SDK 2.6.4 (que arrancara en el runner es otra cuestión, L1).
- **Plazo real:** [.NET 8 deja de recibir soporte el 10 de noviembre de 2026](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/), o sea **en diez días**. Una app autocontenida seguirá funcionando, pero **llevará dentro un runtime sin parches de seguridad**. Al ser un proceso aparte, mezclar 8 y 10 no causa problemas funcionales; el coste es el tamaño y la deuda de seguridad.
- **Recomendación:** (1) **ahora no migrar** (como pides); (2) al empezar F4, un **experimento acotado**: subir la app de NotchWin a .NET 10 en una rama de pruebas con CI de Windows (`msbuild`), con tu aprobación porque es tocar su repo; (3) al importarlo (F5), **.NET 10** salvo bloqueo. La librería `WinMac.Ipc` es solo .NET 10 en F1; si NotchWin siguiera en .NET 8 en F4, se multi-compila `net8.0;net10.0` (código puro, coste mínimo).

## 5. Licencias (hecho en esta preparación)
Añadidos `LICENSE` (Apache-2.0, texto oficial), `NOTICE` y `THIRD_PARTY.md` con las licencias **verificadas en los `.nuspec`**: Vortice/SharpGen MIT (se distribuyen), xunit Apache-2.0 y Test SDK MIT (solo pruebas), Windows App SDK con licencia propia de Microsoft (solo prototipo hoy; **previsto con NotchWin**). **Hallazgo legal para D4:** esa licencia permite redistribuir lo que el paquete copia junto a la app, pero exige que los usuarios finales acepten términos que protejan a Microsoft y una cláusula de indemnidad; **el instalador único tendrá que incluir un contrato de usuario final que lo cubra, y conviene revisarlo con asesoría legal antes de publicar** [V].

## 6. Probar NotchWin en Windows real (antes de F4) — acción tuya
No hay ningún release (el repo no tiene etiquetas), así que hay dos caminos:
- **A. Compilarlo en tu PC (recomendado):** Visual Studio 2022 o Build Tools con las cargas *Desarrollo de escritorio con .NET* y *Herramientas de compilación de la Plataforma universal de Windows*; clonar en una **ruta corta** (p. ej. `C:\src\notch-win`; con rutas largas el compilador XAML falla en silencio) y ejecutar el comando del README: `msbuild src\NotchWin.App\NotchWin.App.csproj /restore /p:Platform=x64`. (`dotnet build` no sirve.)
- **B. Que lo compile GitHub:** crear un release con una etiqueta (p. ej. `v0.1.0-pre1`) en `PipeSick/notch-win` dispara su workflow `release` y deja el artefacto **`NotchWin-unsigned`** en la página de esa ejecución (el último paso, `gh release create`, puede fallar si el release ya existía; el artefacto se genera antes).
**Ojo:** es un binario **sin firmar**; Smart App Control, si está en modo bloqueo, lo impedirá. Tu PC ya ejecutó binarios sin firmar de WinMac, así que probablemente no lo tengas en bloqueo [inferido].
**Qué mirar y contarme:** ¿aparece la píldora arriba al centro?; ¿se expande al hacer clic y se ve bien?; música (Spotify/YouTube) con portada y controles; temporizador; pestañas y ajustes desde la bandeja; **qué pasa sobre un juego o vídeo a pantalla completa** (esperable: se queda encima); consumo en reposo (CPU/RAM); cerrar desde la bandeja; si hay varios monitores, una isla por pantalla. Con eso decido qué cambios concretos proponerte para F4.

## 7. Qué necesito de ti
Ahora: (1) confirmar o corregir los **dos supuestos** (D8 y titular de la licencia); (2) **aprobar este alcance de F1** o recortarlo más; (3) cuando puedas, la **prueba de NotchWin** de §6. Más adelante, con fecha: D4 (antes de F5) y D7 (antes de F6).
