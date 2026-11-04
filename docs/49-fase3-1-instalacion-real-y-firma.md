# Hatsu Desktop — Fase 3.1: primera instalación real en Windows 11

Rama `feat/hatsu-phase3-installer-onboarding`, sobre `d454c0c` (código) y `6452c41` (documentación). NotchWin sigue fijado en `80bd29a`; IPC, letras sincronizadas, HFP, Peripherals y Cleanup internos: sin cambios. Sin PR, merge ni release.

> **Qué está establecido y qué no.** Lo que sigue distingue lo **OBSERVADO** por la persona usuaria en su PC, lo **DEDUCIDO DEL CÓDIGO** y lo **HIPÓTESIS**. El bloqueo del Dock **no está resuelto**: no se declarará resuelto hasta comprobarlo en ese mismo Windows físico.

## 1. Lo observado en el Windows 11 real
MSI `HatsuDesktop-2.0.0` (CI 41881545350) instalado. Barra y Dynamic Island abrieron; el Dock no; no apareció el asistente; Windows notificó que bloqueó parte de la aplicación por no poder confirmar quién publicó `HatsuDesktop.Dock.exe`; Smart App Control estaba activado; el equipo había ejecutado antes WinMac portable. No hay registros del mecanismo exacto.

**Actualización posterior (informada por la persona usuaria):** (a) **el Dock SÍ funciona**: sin reiniciar Hatsu ni Windows, «Reintentar» desde el icono de Hatsu lo abrió; (b) **Dynamic Island funciona** y está visible (no hay evidencia de que su aparición dependiera del reintento del Dock); (c) **nuevo problema**: la Barra se dibuja arriba pero los iconos del escritorio de Windows siguen empezando en la esquina superior izquierda, bajo la Barra; «Actualizar» en el escritorio no lo arregló.

## 2. Diagnóstico del bloqueo del Dock
**Evidencia inicial (previa a la actualización de arriba):** (a) la notificación de Windows **nombra el archivo** (`HatsuDesktop.Dock.exe`) y habla de «no poder confirmar quién lo publicó»; (b) Smart App Control activado; (c) el paquete **no está firmado** (ningún `.exe`, `.dll` propio ni el MSI llevan firma Authenticode). Eso es coherente con un bloqueo por falta de firma/reputación. **No está demostrado** que sea Smart App Control en concreto (podrían ser otras directivas), y **no se sabe por qué solo el Dock**.

**Lo que se auditó del paquete** (`tools/ci/Audit-Binaries.ps1`, artefacto `auditoria-binarios.md` y resumen del job): firma de cada ejecutable y del MSI, cabeceras PE (arquitectura, fecha de enlazado, subsistema, .NET/nativo, ASLR/NX/CFG), metadatos (producto, descripción, nombre original) y cuántos binarios propios hay sin firma. Se construyen igual los cuatro ejecutables (`dotnet publish` autocontenido en la misma carpeta, mismo manifiesto y mismo icono): **no hay una diferencia de construcción que explique** que solo el Dock se bloquee. La decisión de Smart App Control es por archivo y se basa en firma y reputación en la nube; no es reproducible en el CI.

**Hipótesis (no demostradas), en orden:** (1) cada archivo sin firma se evalúa por separado y el Dock cayó en la parte «sin reputación»; (2) comportamiento del archivo (el Dock manipula ventanas y miniaturas del sistema); (3) el archivo llegó con la «marca de la web» de otra manera que los demás. Lo único que lo confirma es el registro de Integridad de código del equipo afectado (eventos 3076/3077) → `tools/Diagnose-Install.ps1` (solo lectura).

**Lo que el producto ya hacía (WM-036) y ahora hace mejor:** si Windows rechaza **iniciar** un ejecutable, el host recibe un código (4551, 1260, 786, 577…) y lo registra en `host.log` con la causa y el código. Fase 3.1 lo conecta con la interfaz (sección 5). Un bloqueo que ocurre **al cargar una DLL** aparece como código de salida `0xC0000428` (también reconocido).

### 2.1 Lo que cambia el reintento exitoso
El Dock **se ejecuta** en ese Windows con el mismo archivo sin firmar. Por tanto **no está demostrado un bloqueo permanente** y la falta de firma **no puede darse por única causa** del fallo inicial. Es **compatible** con varias explicaciones transitorias, ninguna comprobada:

| Hipótesis (no demostrada) | Qué la distinguiría en el informe de diagnóstico |
|---|---|
| **Smart App Control / SmartScreen evalúan un archivo desconocido** la primera vez y el veredicto llega o cambia después | Eventos de Integridad de código 3076/3077 sobre `HatsuDesktop.Dock.exe` **con la hora** del primer intento y **ninguno** (o uno de auditoría) en el reintento |
| **Defender** analiza los archivos recién instalados (análisis en la nube del primer arranque) y bloquea o retrasa el acceso | Eventos 1116/1117 de Defender; códigos 5 (acceso denegado) o 32 (violación de uso compartido) en `host.log` en lugar de 4551/1260 |
| **Carrera con el final de la instalación:** el MSI ofrece «Abrir Hatsu Desktop ahora» y lanza el host **inmediatamente**; el Dock pudo intentarse mientras Windows aún analizaba/liberaba sus archivos | Hora del primer arranque pegada al fin del MSI; los 5 intentos del supervisor (esperas 1, 2, 4, 8 s ≈ 15 s) caben todos en esa ventana |
| **Reintentos demasiado rápidos:** la política actual (5 intentos en ~15 s) se rinde antes de que un veredicto tardío cambie | `host.log`: cinco «no se pudo lanzar» seguidos y `[dock] Failed`, y un arranque correcto cuando la persona pulsó «Reintentar» minutos después |
| **Bloqueo real por directiva** que luego se levantó (p. ej. decisión de reputación) | Igual que la primera fila |

**Lo que se conserva:** el supervisor, los 5 intentos con espera creciente y, sobre todo, **«Reintentar»/«Abrir»** (bandeja y, ahora, Ajustes), que es lo que resolvió el caso. **No se añaden reintentos automáticos más largos o infinitos**: cada intento bloqueado por Windows puede generar **otra notificación al usuario** y no se debe insistir contra una protección. La decisión de cambiar el calendario de reintentos queda para cuando el informe confirme cuál de las hipótesis ocurrió.

**Qué se hizo para no repetir la ceguera:** el informe de diagnóstico imprime las líneas del host sobre cada módulo con su hora y su código de Windows, y `Diagnose-Install.ps1` imprime los eventos de Integridad de código y Defender con su hora. Comparar ambas listas responde a la pregunta.

## 3. Diagnóstico del asistente que no apareció
**Deducido del código (no del registro del equipo):** en el equipo existía `%APPDATA%\WinMac\winmac.json` por la copia portable de WinMac. La decisión de la Fase 3 era «configuración existente ⇒ conservar y **no** mostrar nada». Por eso el asistente no salió, y por eso Barra e Isla (habilitadas en esa configuración) arrancaron sin esperar al asistente. **No era un fallo**, era una decisión de diseño que resultó insuficiente: el usuario no tenía ninguna indicación visible de que existía un asistente. Se confirma (o no) con `Diagnose-Install.ps1`, que imprime `asistente: estado = Existing · origen = winmac-upgrade`.

**Corrección:** ver sección 4. Además, el asistente no depende de ningún módulo: vive en el proceso del host (ventana propia, Direct2D), así que se puede abrir aunque Dock, Barra o Isla no arranquen (prueba de código + el flujo `--no-tray`/CI).

## 4. Onboarding por tipo de usuario
| Situación | Qué pasa |
|---|---|
| **Instalación nueva** (sin `winmac.json` ni datos heredados) | Asistente completo; **ningún módulo arranca** hasta «Aplicar y comenzar» u «Omitir» (verificado en CI real: ONB1/ONB2) |
| **Usuario con WinMac / Hatsu anterior** (hay configuración) | Se conserva **todo** y los módulos arrancan como siempre. Se le ofrece **una** «Bienvenida de migración»: *WinMac ahora es Hatsu Desktop · Tu configuración sigue como estaba* con **«Mantener mi configuración»** (no cambia nada) o **«Revisar componentes»** (mismo asistente con sus elecciones actuales; solo «Aplicar y comenzar» aplica) |
| **Hatsu Desktop ya configurado** | No se repite; se abre a voluntad en Ajustes › General › «Abrir el asistente de configuración» |
| Asistente a medias | Se retoma en la revisión |

La bienvenida de migración **no se repite** (cerrar la ventana equivale a «Mantener»), **no sale en el inicio automático** (`--background`) y se anota en `winmac.json › onboarding.welcomeShownUtc`. Nada se borra; no hace falta una instalación limpia para probarla (el estado `Existing` que dejó la Fase 3 también la recibe una vez).

## 5. Módulos que no arrancan
`Supervisor.Failures` guarda la **última causa establecida** de cada módulo (`FailureKind`): *no se encontró el programa · bloqueado por una directiva de seguridad · bloqueado por el antivirus · sin permiso · archivo no válido · se cerró inesperadamente · no respondió · desconocida*. Se distingue **«desactivado por el usuario»** (no es fallo, no aparece) de todo lo demás.

- **No se acusa a una protección concreta sin prueba.** El código de error 4551/1260/786/577 demuestra «una directiva de seguridad rechazó el archivo», no *cuál*. El texto dice eso: «Windows lo bloqueó mediante una directiva de seguridad» y explica que puede ser Smart App Control, Control de aplicaciones, AppLocker o una directiva de la organización. Una prueba comprueba que ninguna causa nombra «Smart App Control».
- **Dónde se ve:** (1) pantalla final del asistente: «No se pudo iniciar» + «Detalles técnicos» (código y explicación) + «Reintentar»; (2) Ajustes › General › **«Módulos con problemas»** con «Reintentar Dock» (solo si hay problema real); (3) fila del módulo en el panel de la bandeja («No se pudo iniciar · Bloqueado por una directiva de Windows»); (4) `host.log`.
- **Sin bucles:** la política existente de reintentos con espera creciente se mantiene: 5 intentos en 2 minutos y se detiene (probado: 5 líneas de «no se pudo lanzar», no más).
- **Nunca se declara aplicado** lo que no arrancó (el asistente queda «con incidencias»).

## 6. Diagnóstico local de primera instalación
Dos herramientas, ambas **solo lectura, locales y sin telemetría**; escriben un archivo que la persona revisa antes de compartirlo:
- `HatsuDesktop.exe --diagnose [--out f.txt] [--open]` y el botón **Ajustes › General › «Generar informe de diagnóstico»** (lo abre en el Bloc de notas).
- `Diagnose-Install.ps1` (en el paquete y en el MSI; Windows PowerShell 5.1 o 7; sin administrador): además lee firma Authenticode y «marca de descarga» de cada ejecutable, cabeceras PE, estado de Smart App Control (**solo lectura**) y los eventos de Integridad de código / Defender / AppLocker que nombren archivos de Hatsu Desktop.

Contenido: versión y ruta, tipo de instalación, componentes elegidos y **cuáles están en ejecución**, causa registrada de los que no, estado del asistente, configuración heredada, inicio automático, barra de tareas, firma. **Excluido:** ciudad, calendario, aplicaciones ancladas, contenidos. El perfil de usuario, el nombre de usuario y el nombre del equipo se sustituyen. Pruebas: el informe no cambia la configuración, el inicio automático ni la barra de tareas; no contiene datos personales.

## 7. Firma y distribución legítimas
**Lo que se sabe (fuentes consultadas: foros de Microsoft Q&A y de desarrolladores; no se pudo abrir la documentación oficial de Microsoft desde el entorno de esta sesión, así que conviene confirmarla):**
- Smart App Control bloquea los ejecutables **sin firma** (salvo los que su modelo en la nube considera seguros) y puede bloquear incluso **firmados** si el certificado no encadena a una raíz del programa de confianza de Microsoft o si no tienen reputación. **Una firma no garantiza el desbloqueo.**
- Un certificado **autofirmado** no es una solución: no encadena a una raíz reconocida. Añadirlo a las raíces de confianza del equipo es manipular la protección y **no se recomienda ni se hará**.
- No existe una excepción por aplicación; **desactivar Smart App Control es irreversible** (no se puede volver a activar sin reinstalar Windows). **No se le pedirá a nadie que lo desactive**, ni que añada exclusiones en Defender o SmartScreen.
- Se aconseja firmar **todos** los `.exe` y `.dll` propios **y el MSI**; el registro de Integridad de código dice qué archivo se bloqueó.

**Vías legítimas (de menor a mayor coste), para decidir:**
| Vía | Qué es | Condiciones / coste | Comentario |
|---|---|---|---|
| **A. SignPath Foundation** (programa para código abierto) | Firma gratuita con certificado de la Fundación; el CI envía los binarios a firmar | Proyecto de código abierto con licencia OSI (Apache-2.0 ✔), **repositorio público**, sin componentes propietarios, equipo con MFA, verificación de origen (la compilación debe poder trazarse a un commit y a un trabajo de CI), solo binarios propios. **El editor que Windows muestra es «SignPath Foundation»**, no Hatsu Desktop | Sin coste de certificado; requiere solicitud y aprobación, y que el repo sea público. Condiciones oficiales: confirmar en signpath.org antes de solicitar |
| **B. Servicio de firma de Microsoft** (antes «Trusted Signing», ahora «Artifact Signing») | Firma pública gestionada en Azure | **Disponibilidad limitada en vista previa:** hoy se informa de altas solo para organizaciones en EE. UU., Canadá, UE y Reino Unido y para individuos en EE. UU. y Canadá; puede estar cerrado a nuevos usuarios | Coste de la suscripción de Azure; **requiere autorización de costes** y comprobar la elegibilidad actual |
| **C. Certificado de firma de código de una CA comercial (OV/EV)** | Certificado emitido a tu nombre o razón social | Validación de identidad; **la clave debe estar en hardware/HSM** (token o servicio de firma en la nube); coste anual; EV mejora el trato de SmartScreen pero **no garantiza** Smart App Control | **Requiere autorización de costes** |
| **D. Microsoft Store / MSIX** | Las apps de la Store no pasan por Smart App Control | Cuenta de desarrollador (pago único) y empaquetado MSIX; **MSIX virtualiza `AppData`** y limita lo que hace un shell-extender (ya descartado en la Fase 2) | Incompatible con la arquitectura actual |
| **E. Autofirmado** | — | No resuelve Smart App Control | **Descartado** |

**Recomendación (sin asumir costes):** solicitar **A (SignPath Foundation)** *si* el repositorio puede ser público y se acepta que el editor sea la Fundación; en paralelo, comprobar si **B** admite al titular. Mientras tanto, la beta sigue sin firma: se documenta y no se evade. **Pendiente de tu autorización:** publicar el repositorio, solicitar el programa o adquirir/contratar cualquier certificado o servicio. Nada de esto se ha hecho.

Preparación técnica ya incluida (sin coste): auditoría de firma en CI (`auditoria-binarios.md` muestra «sin firma» para todo lo propio) y el MSI/binarios con metadatos coherentes (producto, descripción, versión). Cuando haya certificado se añade un paso de firma **antes** de empaquetar (binarios) y otro **después** (MSI).

## 8. Validación en el Windows físico (instrucciones)
1. **No desactives** Smart App Control, Defender ni SmartScreen. Instala el MSI nuevo (`HatsuDesktop-2.0.1-win-x64.msi`; actualiza al 2.0.0).
2. Abre Hatsu Desktop desde el menú Inicio. Con tu configuración de WinMac, debe aparecer la **bienvenida de migración** («WinMac ahora es Hatsu Desktop»). Elige «Mantener mi configuración» o «Revisar componentes».
3. Si el Dock sigue sin abrirse: Ajustes › General debe mostrar **«Módulos con problemas»** con el motivo, y el panel de la bandeja «No se pudo iniciar · …».
4. Genera el informe: Ajustes › General › «Generar informe de diagnóstico» (o clic derecho en `Diagnose-Install.ps1` › Ejecutar con PowerShell). **Léelo antes de compartirlo.** Busca en la sección de eventos de Integridad de código el archivo exacto y el evento (3076/3077).
5. Ese informe es lo que confirma —o no— que fue Smart App Control y qué archivo(s). Compártelo para decidir la vía de firma.
6. Comprueba que lo que sí arrancó (Barra, Isla, letras sincronizadas) sigue igual, y que la barra de tareas de Windows no quedó oculta.

## 9. Limitaciones pendientes
- **El bloqueo del Dock no está resuelto** y su causa exacta no está confirmada. Sin firma, Smart App Control puede seguir bloqueando cualquier archivo de cualquier compilación nueva.
- El CI no reproduce Smart App Control; las pruebas de «módulo bloqueado» usan un archivo que no es ejecutable (código 193) y, si el runner lo permite, un permiso denegado (código 5). El mapeo de 4551/1260 se prueba solo de forma unitaria.
- Ventana del asistente y de Ajustes: no probadas con ratón/foco en un Windows físico por quien implementa los cambios (solo por captura fuera de pantalla).
- La firma requiere decisiones y, posiblemente, costes que no se han asumido.
- Las fuentes sobre Smart App Control/Trusted Signing son de segunda mano (foros); la documentación oficial no fue accesible desde la sesión.

## 10. Reserva del espacio superior de la Barra (iconos del escritorio bajo la Barra)

### 10.1 Cómo funciona hoy (leído del código; `BarWindow.cs`, `WorkAreaGuard.cs`)
La Barra reserva el borde superior de **cada monitor** con la API estándar de barras de aplicación del shell: `SHAppBarMessage` → `ABM_NEW`, `ABM_QUERYPOS`, `ABM_SETPOS` con `ABE_TOP`, con el rectángulo del monitor y la altura de la Barra **en píxeles físicos** (proceso `PerMonitorV2`, escala del monitor de cada ventana). Al liberar o cerrar se llama a `ABM_REMOVE`. `WorkAreaGuard` (WM-003) comprueba cada pocos segundos que `rcWork` del monitor realmente excluye la franja y, si no, vuelve a registrarla con espera creciente (2, 4, 8… 30 s); también se vuelve a registrar al recibir `TaskbarCreated` (Explorer se reinició). Con el ajuste «Reservar el borde superior» desactivado la Barra se superpone y **no** reserva nada. **No usa `SPI_SETWORKAREA` ni toca el registro.** Mecanismo único: no hay mecanismos de reserva en conflicto.

### 10.2 Reservar espacio ≠ recolocar iconos
`SHAppBarMessage` cambia el **área de trabajo** (`rcWork`) del monitor; las ventanas maximizadas la respetan. Los **iconos del escritorio** los coloca Explorer: puede recolocar los que quedan fuera del área de trabajo cuando esta cambia, pero **los iconos que ya estaban en una posición no se recolocan siempre**, y eso es comportamiento del shell que Hatsu Desktop **no controla ni debe forzar** (hacerlo implicaría mover o reordenar iconos del usuario, y eso exige su consentimiento). Por eso **no se implementó** ninguna recolocación. `SPI_SETWORKAREA` es un mecanismo distinto y **incompatible** con AppBar si se usa a la vez (los dos modifican la misma área): no se mezclan.

### 10.3 Causas posibles de lo observado (ninguna confirmada)
1. **«Reservar el borde superior» está DESACTIVADO** en la configuración heredada de WinMac (la Barra se superpone por diseño). Sospecha razonable: el equipo traía `bar.json`/ajustes de pruebas anteriores. *Se comprueba con el informe: línea «Reservar el borde superior (ajuste)».*
2. **`ABM_NEW` falló** (la Barra lo registra como «ABM_NEW falló… sin reservar espacio»), p. ej. por arrancar antes de que el shell esté listo; el guardián debería repararlo, pero no hay prueba de que lo hiciera en ese PC. *Líneas de `bar.log` en el informe.*
3. **La reserva sí se aplicó, pero Explorer no recolocó los iconos que ya estaban ahí** (10.2). *El informe muestra «Windows reservó arriba N px»: si N > 0 es esta.*
4. **Otra barra o herramienta** compite por el borde superior. *Mismo dato.*

### 10.4 Qué se hizo
- **Diagnóstico de solo lectura** (`--diagnose` y `Diagnose-Install.ps1`): por monitor, la pantalla, la **banda que Windows reservó arriba y abajo**, la escala, el ajuste de reserva, si la Barra corre, y las líneas de `bar.log` sobre la reserva, con una «Lectura» que distingue «Windows sí/no tiene banda reservada». Esto separa las causas 1–4 con **un solo informe**.
- **Prueba real en CI** (`tools/ci/Test-Appbar.ps1`, informe `appbar-informe-<sha>`): arranca la Barra del paquete y comprueba que `SPI_GETWORKAREA` baja, que **vuelve exactamente** al original al cerrarse y tras un **cierre brusco** (proceso terminado), y que con «no reservar» no cambia. Es un runner efímero (1 monitor): **no** cubre dos monitores, DPI mixto, reinicio de Explorer, desconexión de monitores ni la posición de los iconos.
- **Después del diagnóstico se encontró y corrigió un defecto real** (10.7). La frase anterior de este apartado («no hay evidencia de un defecto») quedó superada por las mediciones del CI.

### 10.7 CAUSA RAÍZ (medida en Windows del CI) y corrección
**Experimentos** (`tools/ci/Test-Appbar.ps1`, mismo runner y misma sesión):
| Experimento | Resultado |
|---|---|
| AppBar mínimo según el protocolo documentado (control) | **reserva** (`antes=0; durante=28; después=0`) |
| 5 variantes que añaden una a una lo de la Barra (oculta al registrar, `NOACTIVATE`, `NOREDIRECTIONBITMAP`, ventana de 100×30, `SetWindowPos`, todo junto) | **todas reservan** → no es la ventana ni el orden de llamadas |
| **La Barra del paquete con la bandeja desactivada** (`ShowTrayIcons=false`) | **reserva** (`0,28,1024,720`) |
| **La Barra del paquete con la bandeja activada (valor por defecto)** | **NO reserva** (`0,0,1024,720`), con el guardián registrando «registrada: sí, pero el área de trabajo no excluye la barra» |

**Causa:** con la bandeja activada, la Barra crea una ventana propia de clase `Shell_TrayWnd`, siempre encima, para recibir los iconos de bandeja (`TrayHost`). `SHAppBarMessage` localiza a Explorer con `FindWindow("Shell_TrayWnd")` y devuelve **la primera** de la pila: la de la propia Barra. Las llamadas `ABM_NEW/QUERYPOS/SETPOS` de la Barra se «aceptaban» (de ahí «registrada: sí») pero nunca llegaban a Explorer de forma efectiva y Windows no recortaba el área de trabajo. Es exactamente el síntoma observado en el PC: Barra visible, iconos del escritorio debajo.

**Corrección:** durante cada operación de AppBar de la Barra (`NEW`, `QUERYPOS`, `SETPOS`, `REMOVE`) la ventana-proxy se aparta un instante (`HWND_NOTOPMOST`) para que `FindWindow` devuelva la de **Explorer**, y vuelve a la cima inmediatamente (`TrayHost.WithExplorerDirect`; el temporizador de reafirmación no actúa mientras dura). Los iconos de bandeja siguen funcionando. **No se mueve ni reordena ningún icono del escritorio.**

**Resultado en el CI (mismo runner):** la Barra con la bandeja activada **reserva** (`0,28,1024,720`); al cerrarse ordenadamente y tras un cierre brusco el área de trabajo **vuelve exactamente** a `y=0`; un segundo arranque vuelve a reservar; con «no reservar» no cambia. **Experimento 2:** una AppBar de **otro proceso** (el control) también reserva con la Barra y su proxy en marcha (`antes=28; durante=56`), así que **el Dock y otras barras ajenas no estaban afectados** por esto.

**Qué NO se ha demostrado:** que éste fuera el motivo en el PC de la persona usuaria (es el único hallazgo reproducido que coincide con el síntoma, pero su `bar.log` aún no se ha visto); que los iconos del escritorio se recoloquen en ese PC (depende de Explorer, 10.2); dos monitores, DPI mixto, reinicio de Explorer y desconexión de monitores (el CI tiene un monitor).

### 10.5 Restauración y casos límite (revisión de diseño, no probados en un PC real)
| Caso | Comportamiento esperado | Estado |
|---|---|---|
| Cierre ordenado | `ABM_REMOVE` y el área de trabajo vuelve | Probado en CI (runner) |
| Cierre brusco / caída | Windows libera la reserva de una ventana que ya no existe | Probado en CI (runner); en un PC real, **pendiente** |
| Reinicio de Explorer | Se registra de nuevo al llegar `TaskbarCreated` | Código existente; **sin prueba física** |
| Cambio de resolución o DPI | `Relayout` recoloca y vuelve a reservar | Código existente; **sin prueba física** |
| Desconexión/conexión de monitores | Una barra por monitor; las nuevas se registran, las que sobran se liberan | Código existente; **sin prueba física** |
| Desinstalar con la Barra abierta | El MSI pide cerrar el host (Restart Manager) antes de quitar archivos; la Barra libera su reserva al cerrarse | Probado en CI con el host abierto (no con la Barra real) |
| Iconos del escritorio | **No se mueven ni reordenan** | Garantizado por diseño |

## 11. HALLAZGOS DE LA INSTALACIÓN FÍSICA (resumen: confirmado · sospechado · pendiente)
Primera prueba real de `HatsuDesktop-2.0.0-win-x64.msi` (CI 41881545350) en el Windows 11 de la persona usuaria, con Smart App Control activado y WinMac portable usado antes.

### 11.1 CONFIRMADO (observado por la persona usuaria o demostrado)
- El MSI se instaló sin errores.
- Barra superior: visible. Dynamic Island: visible y funcionando. **Dock: funciona** tras pulsar «Reintentar» (sin reiniciar nada).
- En el primer arranque el Dock no apareció y Windows mostró una notificación diciendo que bloqueó `HatsuDesktop.Dock.exe` por no poder confirmar quién lo publicó.
- El asistente no apareció.
- Los iconos del escritorio quedan bajo la Barra; «Actualizar» no lo arregló.
- **Reproducido en el CI:** con la configuración por defecto la Barra del paquete no reservaba el borde superior (10.7) — el mismo síntoma, causa medida y corregida.
- **Del CI:** un ejecutable no válido bloqueaba el arranque del host (sección 12), corregido; un permiso denegado falla limpiamente (código 5).
- **Del paquete (CI):** ningún `.exe`/`.dll` propio ni el MSI llevan firma Authenticode.
- **Del código:** con una configuración previa (`winmac.json` de WinMac) la Fase 3 no mostraba el asistente **por diseño**; ya no es así (bienvenida de migración, sección 4).
- «Reintentar» funciona y es el mecanismo de recuperación a conservar.

### 11.2 SOSPECHADO (compatible con lo observado; sin prueba)
- Que el bloqueo inicial del Dock lo causara Smart App Control (o Defender/SmartScreen) por archivo sin firma ni reputación **en ese momento**; **no** es permanente (el Dock arrancó después).
- Que el bloqueo fuera transitorio (veredicto en la nube, análisis de archivos recién instalados o carrera con el arranque inmediato tras instalar) — hipótesis de 2.1.
- **Reserva del borde superior:** la causa más probable es la del apartado 10.7 (reproducida y corregida en el CI), pero **no se ha confirmado en ese PC**; siguen siendo posibles el ajuste desactivado en la configuración heredada, `ABM_NEW` fallido o que Explorer no recolocara los iconos ya situados (10.3).
- Que Barra e Isla arrancaran sin bloqueo por una evaluación de reputación distinta de la del Dock: **no se sabe**.
- Que la Isla apareciera «por reintentar el Dock»: **no se asume ninguna relación causal**.

### 11.3 PENDIENTE DE VALIDAR EN EL EQUIPO DE LA PERSONA USUARIA
1. Instalar `HatsuDesktop-2.0.1` (actualiza al 2.0.0). Comprobar la **bienvenida de migración** al abrir desde el menú Inicio, y que **los iconos del escritorio ya no quedan bajo la Barra** (si no, el informe de diagnóstico dice por qué).
2. Generar el informe (Ajustes › General › «Generar informe de diagnóstico») **antes y después** de reproducir cualquier fallo, y leerlo antes de compartirlo. En particular:
   - sección «Componentes elegidos y su estado» y las líneas `[dock] …` con **hora** y **código** de `host.log`;
   - sección de eventos de Integridad de código y Defender con **hora**;
   - sección «Reserva del borde superior»: **ajuste**, **«Windows reservó arriba N px»** y las líneas de `bar.log`.
3. Si el Dock vuelve a fallar al primer arranque: anotar la hora, si fue inmediatamente después de instalar, y si «Reintentar» lo abre; **no** desactivar Smart App Control, Defender ni SmartScreen.
4. Si los iconos siguen bajo la Barra: con el informe se sabrá si el ajuste está desactivado (activarlo en Ajustes › Barra superior › «Reservar el borde superior»), si falló `ABM_NEW`, o si Windows sí reservó pero no recolocó los iconos. **Ningún icono se mueve sin su consentimiento.**
5. Probar en ese PC lo que el CI no puede: dos monitores, escalas distintas, reinicio de Explorer (`taskkill /f /im explorer.exe` y volver a abrirlo desde el Administrador de tareas) y desconexión de un monitor.

## 12. Hallazgo del CI: un ejecutable no válido bloqueaba el arranque del host (corregido)
La prueba de «módulo que no se puede lanzar» (archivo de texto llamado `HatsuDesktop.Dock.exe`) mostró en el registro del host **que el Dock ni siquiera llegó a registrar un intento**, mientras que un ejecutable con permiso denegado sí falló limpiamente (error 5, «acceso denegado», `Failed`, los demás módulos siguen: **validado en Windows del CI**). Causa **más probable** (no probada con un volcado): `CreateProcess` sobre un archivo que no es un programa válido puede abrir el **cuadro de error del sistema** y esperar a que se cierre, y el host lanzaba los módulos **en su hilo de interfaz**, así que la bandeja, Ajustes y el asistente quedaban congelados.

Corregido en dos capas independientes: (1) el host llama a `SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX)` al empezar, de modo que un fallo de carga **devuelve el error** (193, etc.) en lugar de un cuadro modal; (2) el arranque de módulos corre **fuera del hilo de interfaz**, de modo que aunque un lanzamiento tarde o se bloquee, la bandeja, Ajustes y el asistente siguen respondiendo (y al cerrar no se espera más de 20 s a un lanzamiento atascado). Esto cumple el requisito de que el asistente sea accesible aunque Dock o Barra no puedan arrancar. La prueba `F31-1…3` valida el resultado en Windows: código 193 en el registro, 5 intentos como máximo, `Failed`, y los demás módulos en marcha.

*No se afirma* que esto fuera lo que pasó en el PC de la persona usuaria: allí Windows no mostró un cuadro modal, sino una notificación de bloqueo.

### 10.6 Observación del CI sobre la reserva (ronda de diagnóstico)
En el runner de Windows, `bar.log` de la Barra del paquete mostró **el mismo síntoma que en tu PC**: `AppBar reservado en …: (0,0)-(1024,28) px` y, segundos después, `el área de trabajo de … no excluye la barra (empieza en 0 px, barra hasta 28 px; registrada: sí): se vuelve a pedir la reserva`, repetido; más tarde `Explorer se reinició (TaskbarCreated)` y `ABM_NEW falló`. La lectura directa del área de trabajo (`SPI_GETWORKAREA`) tampoco cambió.
**Qué NO significa:** no demuestra un defecto del producto (el runner es una sesión virtual con un solo monitor y su shell puede no atender barras de aplicación). Por eso se añadió un **experimento de control** en `Test-Appbar.ps1`: una barra de aplicación mínima que sigue **al pie de la letra** el protocolo documentado (`ABM_NEW` → `ABM_QUERYPOS` → `ABM_SETPOS` → mover la ventana). Si el control **sí** cambia el área de trabajo y la Barra no, es un **defecto del producto** y la prueba falla; si el control tampoco la cambia, el runner no sirve para esto y la prueba se marca **no concluyente**. Resultado: ver «Resultados de CI» más abajo.
**Qué sí aporta:** confirma que el guardián existente detecta y registra exactamente el síntoma («registrada: sí pero el área de trabajo no excluye la barra»). Ese es el renglón de `bar.log` que hay que buscar en el informe de tu PC.


## 13. Resultados de CI (commit `627f633`, ejecución 41921205653: **4 jobs en verde**)
| Qué | Resultado | Límite |
|---|---|---|
| Pruebas unitarias (Host.Core 324, Bar.Core 620, resto sin cambios) | OK | — |
| MSI en el runner: instalar, actualizar (con el host abierto), bajar de versión (rechazado), desinstalar, convivencia con copia portable, **diagnóstico en Windows PowerShell 5.1 sin cambiar nada** | **74 comprobaciones, 0 fallos** | runner efímero con permisos elevados; la entrada de desinstalación quedó en HKLM (con un usuario sin privilegios debe ir a HKCU: pendiente) |
| **Reserva del borde superior** con la Barra real del paquete: reserva, restaura al cerrar y tras cierre brusco, 2.º arranque, «no reservar» | OK | un monitor, sin DPI mixto, sin reinicio de Explorer, **no mide los iconos del escritorio** |
| Experimento 2 (AppBar ajena con la Barra en marcha) | reserva (28→56) | idem |
| Módulo que no se puede lanzar (archivo no válido): 5 intentos máximo, `Failed`, código de Windows registrado, la Barra sigue en marcha, informe local | OK | ver 12 |
| Permiso denegado (error 5, «acceso denegado») | OK | depende de las ACL del runner |
| Asistente: instalación nueva (ningún módulo antes de decidir), omitir (Barra/Dock arrancan, Connect no, sin inicio automático), usuario existente → bienvenida de migración pendiente | OK | sin ventana (`--no-tray`); la ventana se revisó solo en capturas |
| Capturas del asistente (14 + escalas + detalle técnico), incluida la **bienvenida de migración** en claro y oscuro | generadas; revisadas a ojo las de migración y las anteriores | Apariencia/Resultado a 125 % y 150 % no revisadas a ojo |
| Integración con NotchWin (pin `80bd29a`), letras sincronizadas | OK | — |
| Auditoría de binarios | **26 binarios propios, 26 sin firma; MSI `NotSigned`**; 445 de 453 binarios ajenos firmados por Microsoft | confirma la falta de firma; **no explica por qué solo el Dock** |
| Job de Linux | verde en esta ejecución; una prueba de tiempos del supervisor (`RestartPolicyTests…`) falló una vez en una ejecución anterior y pasó en todas las demás: parece flaky por carga del runner |

**Nuevo MSI de prueba:** `HatsuDesktop-2.0.1-win-x64.msi` (118 MB, **sin firmar**; SHA-256 `e511592c…94b8` en el commit `627f633`; artefacto `hatsu-desktop-paquete-<sha>` de la ejecución 41921205653, 14 días). No se publica ni se versiona fuera de CI. Se instala **encima** del 2.0.0 (misma identidad de producto, versión mayor).
