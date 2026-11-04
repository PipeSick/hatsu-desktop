# Hatsu Desktop — Fase 3.1: primera instalación real en Windows 11

Rama `feat/hatsu-phase3-installer-onboarding`, sobre `d454c0c` (código) y `6452c41` (documentación). NotchWin sigue fijado en `80bd29a`; IPC, letras sincronizadas, HFP, Peripherals y Cleanup internos: sin cambios. Sin PR, merge ni release.

> **Qué está establecido y qué no.** Lo que sigue distingue lo **OBSERVADO** por la persona usuaria en su PC, lo **DEDUCIDO DEL CÓDIGO** y lo **HIPÓTESIS**. El bloqueo del Dock **no está resuelto**: no se declarará resuelto hasta comprobarlo en ese mismo Windows físico.

## 1. Lo observado en el Windows 11 real
MSI `HatsuDesktop-2.0.0` (CI 41881545350) instalado. Barra y Dynamic Island abrieron; el Dock no; no apareció el asistente; Windows notificó que bloqueó parte de la aplicación por no poder confirmar quién publicó `HatsuDesktop.Dock.exe`; Smart App Control estaba activado; el equipo había ejecutado antes WinMac portable. No hay registros del mecanismo exacto.

## 2. Diagnóstico del bloqueo del Dock
**Evidencia:** (a) la notificación de Windows **nombra el archivo** (`HatsuDesktop.Dock.exe`) y habla de «no poder confirmar quién lo publicó»; (b) Smart App Control activado; (c) el paquete **no está firmado** (ningún `.exe`, `.dll` propio ni el MSI llevan firma Authenticode). Eso es coherente con un bloqueo por falta de firma/reputación. **No está demostrado** que sea Smart App Control en concreto (podrían ser otras directivas), y **no se sabe por qué solo el Dock**.

**Lo que se auditó del paquete** (`tools/ci/Audit-Binaries.ps1`, artefacto `auditoria-binarios.md` y resumen del job): firma de cada ejecutable y del MSI, cabeceras PE (arquitectura, fecha de enlazado, subsistema, .NET/nativo, ASLR/NX/CFG), metadatos (producto, descripción, nombre original) y cuántos binarios propios hay sin firma. Se construyen igual los cuatro ejecutables (`dotnet publish` autocontenido en la misma carpeta, mismo manifiesto y mismo icono): **no hay una diferencia de construcción que explique** que solo el Dock se bloquee. La decisión de Smart App Control es por archivo y se basa en firma y reputación en la nube; no es reproducible en el CI.

**Hipótesis (no demostradas), en orden:** (1) cada archivo sin firma se evalúa por separado y el Dock cayó en la parte «sin reputación»; (2) comportamiento del archivo (el Dock manipula ventanas y miniaturas del sistema); (3) el archivo llegó con la «marca de la web» de otra manera que los demás. Lo único que lo confirma es el registro de Integridad de código del equipo afectado (eventos 3076/3077) → `tools/Diagnose-Install.ps1` (solo lectura).

**Lo que el producto ya hacía (WM-036) y ahora hace mejor:** si Windows rechaza **iniciar** un ejecutable, el host recibe un código (4551, 1260, 786, 577…) y lo registra en `host.log` con la causa y el código. Fase 3.1 lo conecta con la interfaz (sección 5). Un bloqueo que ocurre **al cargar una DLL** aparece como código de salida `0xC0000428` (también reconocido).

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
