# Hatsu Desktop — Fase 3: instalador MSI y asistente de primera ejecución

Base: `9596170` (Fase 2). Rama `feat/hatsu-phase3-installer-onboarding`. NotchWin sin cambios (pin `tools/notch-pin.txt` = `80bd29a`). IPC, letras sincronizadas, HFP, Peripherals y Cleanup internos: **sin cambios**. Sin PR, merge ni release; nada se publica.

> **Qué está y qué no está validado.** El CI valida: compilación, pruebas unitarias, estructura del MSI y una instalación/actualización/desinstalación **reales en un runner de Windows efímero**. Eso **no** es una prueba en un Windows físico: la interacción real con el asistente (ratón, foco, DPI mixto, lector de pantalla), SmartScreen/antivirus y la experiencia en un equipo de verdad siguen **pendientes** (ver la checklist al final).

## 1. Tecnología del instalador
**WiX Toolset 5.x (esquema v4), MSI por usuario**, fijado en `5.0.2`. Se mantuvo la recomendación de la Fase 2 tras revisar la arquitectura real:

| Criterio | Resultado |
|---|---|
| Sin administrador | `Scope="perUser"`, carpeta `%LOCALAPPDATA%\Programs\Hatsu Desktop`. Nada en Archivos de programa, sin servicios, sin controladores. |
| Datos de usuario | Viven en `%APPDATA%\WinMac` y `%LOCALAPPDATA%\WinMac`; el MSI nunca los toca. (MSIX los virtualizaría: descartado, como en la Fase 2.) |
| Sistema de barra de tareas / shell | Sin sandbox: el host puede ocultar y restaurar la barra como hoy. |
| Actualizaciones | `MajorUpgrade` con `UpgradeCode` fijo; una versión nueva sustituye a la anterior, una anterior no sustituye a una nueva. |
| Código abierto | WiX es MS-RL; sin dependencias propietarias. Se fija 5.0.2 (se evita adoptar sin decisión una rama con otro modelo de soporte). |
| Reproducible en CI | `installer/Build-Msi.ps1`: ProductCode determinista, mismos archivos con fechas fijas (las del paquete ZIP). |

Alternativa descartada: Inno Setup (válida, pero no integra con Windows Installer: sin `msiexec`, sin directivas empresariales, sin Restart Manager).

## 2. Identidad de instalación
| Elemento | Valor / política |
|---|---|
| Nombre de producto | `Hatsu Desktop` (el nombre «WinMac» no aparece en el instalador, salvo en las rutas de datos que se conservan) |
| Fabricante | `Hatsu Desktop` |
| **UpgradeCode** | `ae38cbf7-6543-463e-9067-50e88f162fa1` — **fijo para siempre** (une todas las versiones). Cambiarlo rompería las actualizaciones. |
| **ProductCode** | Determinista: GUID de nombre (RFC 4122 v5, espacio de nombres = UpgradeCode) de `HatsuDesktop\|<versión>`. Una versión = un ProductCode (requisito de MSI para `MajorUpgrade`); dos compilaciones de la misma versión dan el mismo. Nunca aleatorio. |
| Versión MSI | `major.minor.build` (MSI ignora el cuarto campo); sale de `src/Directory.Build.props`. Subir la versión es obligatorio para que una actualización sea posible. |
| Ruta | `%LOCALAPPDATA%\Programs\Hatsu Desktop` |
| Arquitectura | x64. Una futura variante ARM64 usaría el **mismo** UpgradeCode con otro `-arch` y otro nombre de archivo; no se mezclan en una misma instalación. |
| Registro propio | Solo `HKCU\Software\Hatsu Desktop\Installer` (claves de los accesos directos) y la entrada estándar de «Aplicaciones instaladas» (HKCU). |
| Interfaz | `WixUI_FeatureTree` (licencia, «Personalizar», avisos de archivos en uso), español. Acceso directo del escritorio = característica opcional **desactivada** por defecto. Casilla final «Abrir Hatsu Desktop ahora». |

Política de actualización: sin actualizador en segundo plano, sin consultas ni descargas. Se actualiza ejecutando el MSI nuevo (o `msiexec /i`). Si el host está abierto, Restart Manager pide cerrarlo (con interfaz) o lo cierra (silencioso); si hubiera archivos en uso, MSI sustituye al reiniciar (código 3010). Un fallo a mitad de la instalación se revierte por completo (transacción de Windows Installer).

## 3. Convivencia con WinMac y con la copia portable
- **Un solo host a la vez:** la exclusión mutua de usuario (`Local\WinMac.Host`) es la misma de WinMac y de la copia portable.
- **Detección:** una segunda copia mira qué otros procesos `HatsuDesktop`/`WinMac` hay y de qué carpeta son (`HostCoexistence`, probado):
  - misma carpeta → como siempre, pide reabrir módulos cerrados y sale;
  - **otra carpeta, uso interactivo** → **pregunta** («Hatsu Desktop (o WinMac) ya está abierto desde: … ¿Cerrarla y usar esta?»); si aceptas, equivale a `--replace-running` (cierre ordenado, espera 20 s, nunca mata procesos);
  - **otra carpeta, inicio automático (`--background`) o pruebas** → esta copia se cierra sin tocar nada.
- **No se secuestra el inicio automático:** el MSI no crea ni cambia el valor `Run\WinMac`. Para adoptar la instalación MSI como principal: Ajustes › General › «Iniciar esta instalación con Windows» (confirmación) o `HatsuDesktop.exe --adopt-autostart`. La migración existente (`AutostartMigration`, `Migrate-FromWinMac.ps1`) se reutiliza sin cambios y **nunca** se ejecuta en silencio.
- **Si prefieres seguir con la portable:** no hay nada que lo impida; basta no usar la instalada. Ambas pueden estar en disco a la vez; solo una corre.
- **Limitaciones:** (1) comparten `%APPDATA%\WinMac`: si tienen versiones distintas, la más antigua ignora lo que no conoce (las secciones nuevas se ignoran, no se borran al leer, pero un guardado de la antigua puede omitirlas); (2) el valor de inicio automático es único (`Run\WinMac`): solo una copia lo posee; (3) el MSI no desinstala la portable ni una copia de WinMac, ni cierra la que no sea suya.

## 4. Componentes configurables y dependencias
| Componente | Qué es | Efecto real al desactivarlo |
|---|---|---|
| Dock | módulo (proceso) | no se lanza |
| Barra superior | módulo (proceso) | no se lanza |
| Dynamic Island | módulo NotchWin (proceso) | no se lanza (letras sincronizadas y música: intactas cuando está activa) |
| Hatsu Connect | módulo (proceso) | no se lanza; apagado por defecto |
| Hatsu Peripherals | **función de la Barra** (`showPeripherals`) | la Barra no lee ni guarda nada de periféricos; apagado por defecto |
| Hatsu Cleanup | **función de la Barra** (`showCleanup`) | sin icono ni acciones; nunca limpia por sí sola; apagado por defecto |

Dependencias (reglas puras en `OnboardingRules`, probadas): Peripherals y Cleanup necesitan la Barra (activarlos la activa; desactivar la Barra los desactiva, con aviso); ocultar la barra de tareas de Windows necesita el Dock; Connect funciona sin Barra pero su indicador vive en la Barra (aviso). No se promete soporte ASUS 2,4 GHz ni Bluetooth 2.0.

**Valores predeterminados verificados:** Dock, Barra e Isla activados; Connect, Peripherals y Cleanup desactivados; sin inicio automático; barra de tareas intacta. Son los de fábrica de `HostConfig` y de `BarConfig` (comprobado en pruebas).

**Apariencia (solo lo que existe):** tema del **Dock** (Sistema/Claro/Oscuro, `dock.json`), tamaño del Dock (preajustes), posición horizontal del Dock (izquierda/centro/derecha), fondo de la Barra (Vidrio/Transparente). El tema de la Barra y de la Isla **sigue siempre a Windows** (no hay selector real) y el asistente lo dice. Sin controles falsos.

## 5. El asistente (cinco pantallas, dibujado con Direct2D, sin navegador)
1. **Bienvenida** — «Tu escritorio, a tu estilo.» Comenzar / Omitir configuración. No modifica nada.
2. **Componentes** — seis tarjetas con interruptor, dependencias avisadas.
3. **Apariencia** — tema, tamaño, posición, fondo, con vista previa esquemática.
4. **Integración con Windows** — inicio automático y ocultar la barra de tareas (ambos **desactivados**), y cómo se cierra Hatsu Desktop (sigue en la bandeja; «Salir» está en su menú).
5. **Revisión** — resumen; «Aplicar y comenzar». Hasta aquí **no se ha aplicado nada**, ni al navegar atrás y adelante.

Salir sin aplicar: la ventana se puede cerrar o cancelar en cualquier momento antes de aplicar; el sistema no queda modificado. En una instalación nueva, los módulos esperan (desactivados en la configuración) y el asistente vuelve al siguiente arranque; Ajustes › General › «Abrir el asistente de configuración» lo reabre cuando quieras.

## 6. Persistencia y transacción
Estado en `winmac.json › onboarding` (`NotStarted`, `InProgress`, `Completed`, `Skipped`, `Existing`) con `Pending` (las elecciones, guardadas **antes** de aplicar). Las versiones anteriores ignoran la sección.

Orden de `OnboardingApplier` (probado con un doble que falla a propósito):
1. validar y normalizar (dependencias); 2. guardar las elecciones pendientes (si no se puede escribir el disco, se aborta sin aplicar nada); 3. `dock.json` (aviso si falla); 4. inicio automático (**esencial**); 5. configuración final por `ConfigService` (módulos, Barra, barra de tareas) — esto arranca y para módulos con el supervisor **existente**, no hay otro gestor; 6. **verificar** que cada módulo activado llega a `Running`; 7. solo si todo está bien se marca `Completed`.
- Fallo esencial (disco, registro bloqueado por una directiva) → **se restaura** configuración, `dock.json` e inicio automático; el asistente sigue pendiente; se informa y se puede **reintentar**.
- Módulo que no arranca o no está incluido → **no se declara éxito**: aviso por módulo, «Reintentar» o «Continuar de todos modos» (queda anotado en `warnings`).
- Cierre inesperado o reinicio tras guardar lo pendiente → el asistente se **retoma en la revisión** con esas elecciones.
- La ventana no se puede cerrar mientras se aplica.

**Omitir** = aplicar los valores seguros y marcar `Skipped` (no reaparece). **Usuario existente** (configuración de WinMac o de una versión previa de Hatsu Desktop) → `Existing`: no se muestra el asistente, no se toca nada, se conserva el inicio automático. **Actualización entre versiones de Hatsu Desktop** → `Completed` ya existe: no se repite.

## 7. Desinstalación
Desde «Aplicaciones instaladas» o `msiexec /x`. Antes de quitar archivos corre `HatsuDesktop.exe --uninstall-cleanup`: devuelve la barra de tareas de Windows si estaba oculta y quita **solo** el valor de inicio automático **si apunta a esta instalación**. Quita archivos, accesos directos y su entrada. **No** borra `%APPDATA%\WinMac`, `%LOCALAPPDATA%\WinMac` ni datos de NotchWin; no toca otra copia (portable o WinMac) ni su inicio automático; no hace limpieza agresiva del Registro. Borrar los datos personales es una acción aparte (`Uninstall-HatsuDesktop.ps1 -RemoveData` de la versión portable, con su confirmación).

## 8. Resultados de CI (commit `d6dfc53`, ejecución 41872300428: 4 jobs en verde)
| Qué | Resultado | Qué NO demuestra |
|---|---|---|
| Compilación y pruebas unitarias (Host.Core 243, resto sin cambios) | OK | — |
| Estructura del MSI (nombre, UpgradeCode, versión, fabricante, sin ALLUSERS, archivos incluidos, sin ejecutables antiguos) | OK | — |
| ProductCode determinista (3 versiones distintas; 2 compilaciones de la misma = mismo código) | OK | — |
| **Instalación real** en el runner (`msiexec /i`; 66 comprobaciones, 0 fallos) | OK | No es un Windows físico: runner efímero y con token elevado |
| Actualización 2.0.0→2.0.1, y 2.0.1→2.0.2 **con el host en ejecución** (código 0); rechazo de bajar de versión (1603); una sola entrada del producto | OK | Las tres versiones tienen el **mismo contenido** (solo cambia el número): no prueba una actualización con cambios reales de archivos |
| Desinstalación real: archivos, accesos directos y entrada fuera; datos y NotchWin intactos; inicio automático de otra copia intacto; el propio se quita; barra de tareas sin registro de recuperación | OK | — |
| Convivencia con una copia portable (`--background` no la toca; `--replace-running` la cierra y continúa) | OK | El diálogo de confirmación interactivo no se puede accionar en CI |
| Host real: instalación nueva (ningún módulo arranca antes de decidir), «omitir» (Barra y Dock arrancan por el supervisor, Connect no), usuario existente → `Existing` (ONB1–3) | OK | Con módulos de mentira; la ventana del asistente no se abre en CI |
| Capturas del asistente: 6 pantallas × claro/oscuro (`capturas-onboarding-<sha>`) | Generadas (1600×1120, WARP) | **No las he mirado**: CI comprueba que se generan, no que se vean bien. Hay que revisarlas a mano |
| Integración con NotchWin (pin `80bd29a`) | OK | — |

**Observación del runner:** con un token elevado, Windows Installer dejó la entrada de desinstalación en `HKLM\...\Uninstall\{ProductCode}` (el icono lo registra en `%APPDATA%\Microsoft\Installer\{ProductCode}\HatsuDesktop.ico`). Con un usuario sin privilegios debe ir a `HKCU`, pero **no está verificado**: es un punto de la checklist manual. Archivos, accesos directos y datos sí son por usuario (comprobado).

Artefactos descargables de la ejecución: `hatsu-desktop-paquete-<sha>` (ZIP portable + **MSI** + SHA-256 + ProductCode), `msi-informe-<sha>` (informe y registros `msiexec /l*v`), `capturas-onboarding-<sha>`, `capturas-it8-<sha>`, `integracion-notchwin-<sha>`. Temporales (14 días); no se publica nada.

En la prueba ONB2 el estado final queda `InProgress` y no `Skipped` porque ahí NotchWin no existe: la Isla activada no tiene ejecutable y el asistente lo **informa en vez de declarar éxito** (es el comportamiento buscado); en el paquete real la Isla sí está.

## 9. Limitaciones y riesgos pendientes
- Nada de esto se ha probado en un **Windows físico**; el runner de CI es efímero y administrador.
- El MSI **no está firmado**: SmartScreen puede avisar. Firmar requiere un certificado (decisión pendiente).
- Restart Manager depende de que el host responda al cierre de sesión (`WM_ENDSESSION` → salida ordenada, ya implementado); no se ha observado con un host real instalado fuera del runner.
- La interacción del asistente (ratón, teclado, foco, DPI mixto) se valida solo por el dibujo fuera de pantalla y la lógica; la ventana real queda para la prueba manual.
- Versiones distintas compartiendo `%APPDATA%\WinMac` (portable antigua + MSI nueva) pueden perder secciones nuevas al guardar la antigua.
- WiX 5.x se fija en 5.0.2; actualizarlo es una decisión a tomar.

## 10. Checklist de validación manual (Windows físico)
1. Instalar el MSI como usuario normal (sin administrador): ¿pide permisos? ¿aparece en «Aplicaciones instaladas» con el icono Flux, y la entrada está en `HKCU` (no `HKLM`)?
2. Menú Inicio: acceso directo con icono. Escritorio: **no** hay acceso directo salvo que se marque en «Personalizar».
3. Primer arranque (sin `%APPDATA%\WinMac`): aparece el asistente; **no** hay Dock ni Barra todavía; recorrer las cinco pantallas, ida y vuelta, y comprobar que no cambia nada hasta «Aplicar y comenzar».
4. Claro y oscuro de Windows, 100/150/200 % de DPI y dos monitores: textos, tarjetas e icono nítidos.
5. «Omitir configuración»: Dock, Barra e Isla arrancan; no hay inicio automático ni barra de tareas oculta.
6. Elegir todo (incluido ocultar la barra de tareas e iniciar con Windows): se aplica, se restaura al salir; cerrar sesión y volver a entrar.
7. Matar el host a mitad de «Aplicar» y volver a abrir: el asistente se retoma.
8. Con WinMac o la copia portable abierta: abrir la instalada → pregunta; «No» la deja intacta; «Sí» la cierra ordenadamente.
9. Actualizar con el host abierto (nuevo MSI de mayor versión): conserva elecciones, datos e inicio automático.
10. Desinstalar: barra de tareas visible, archivos y accesos directos fuera, datos y NotchWin intactos, el inicio automático de otra copia sigue.
11. NotchWin: música, controles y letras sincronizadas funcionan igual que con el ZIP.
12. Lector de pantalla y navegación solo con teclado en el asistente (Enter/Esc funcionan; Tab **no** está implementado todavía).

## 11. Decisiones que requieren aprobación
1. ¿Firmar el MSI (certificado de código)? Hoy no va firmado.
2. ¿Adoptar WiX 6 (cambia el modelo de soporte) o quedarse en 5.0.2?
3. ¿Un modo «reparar/modificar» en el MSI? Hoy `ARPNOMODIFY=1`; la configuración se cambia en Ajustes, no reinstalando.
4. ¿Añadir navegación por Tab y soporte de accesibilidad completo al asistente?
5. ¿Ofrecer un actualizador? Hoy no se consulta ni descarga nada.
6. ¿Publicar el MSI? Hoy no se publica nada; el artefacto de CI es temporal.

## 12. Correcciones visuales del asistente (ronda de revisión sobre las 12 capturas de CI)
Revisadas a ojo las capturas generadas por el CI (volcadas como JPEG reducidos al registro del job, porque la descarga de artefactos no estaba disponible para quien implementó los cambios; el artefacto `capturas-onboarding-<sha>` tiene los PNG a escala 2 y los demás escalados). Identidad Flux sin cambios.

| Defecto | Corrección | Comprobación |
|---|---|---|
| Apariencia: la nota del tema pisaba «Tamaño del Dock» | Todas las medidas salen de **una sola clase** (`OnboardingLayout`): cada texto reserva el alto que necesita y el siguiente bloque empieza debajo; controles apilados con separación | Prueba de geometría (sin solapes en 5 variantes × 5 pantallas), prueba específica del orden vertical, capturas claro/oscuro a 100/125/150/200 % |
| Resultado: los puntos de progreso invadían «Reintentar» | Puntos eliminados de Bienvenida y Resultado; en las demás pantallas viven en una franja propia entre los botones | Prueba: ningún cuadro entra en el pie de botones (y ≥ 492) ni los puntos comparten espacio con un botón |
| Resultado: aviso desalineado, lejos del módulo, mucho vacío | Mensaje **dentro** de la tarjeta, alineado con su margen interior (x = 64), justo bajo la lista; la tarjeta se ajusta a su contenido | Capturas + prueba con 5, 8 y 12 filas |
| Resultado: «desactivado» en verde | Indicador **neutro** (gris) para lo desactivado a propósito (`ApplyItem.Inactive`); verde solo para lo correcto, ámbar para incidencias, rojo para fallos | Prueba del aplicador + capturas |
| Tema claro: contraste bajo | Variantes oscuras de la paleta Flux solo para texto sobre superficies claras (acento `#6D28D9`, aviso `#9A4406`, verde `#166534`, error `#B91C1C`) | **Prueba de contraste WCAG AA (≥ 4,5:1)** sobre fondo y tarjeta en ambos temas; la prueba detectó un naranja a 4,23:1 y se corrigió |
| Componentes: dependencia de la Barra poco visible | Etiqueta **«Requiere Barra superior»** en Peripherals y Cleanup (la lógica de dependencias no cambió) | Prueba (solo esas dos) + capturas |
| Vista previa vertical | Escritorio **horizontal** 16:10 con barra, Isla, ventana de muestra y Dock según tema, tamaño y posición | Prueba de proporción + capturas |
| «No arrancó (estado: Failed)» | «**No se pudo iniciar**»; el detalle técnico (`estado del supervisor: Failed`) aparece aparte, en «Detalles técnicos» (plegado por defecto) | Pruebas + captura con el detalle abierto |
| «Casi listo» | «**Configuración aplicada con incidencias**» | Prueba del título |
| «Continuar de todos modos» sin explicar | El mensaje dice qué hace: cierra el asistente con lo que funciona; lo que no arrancó queda anotado y se puede reintentar o desactivar en Ajustes | Prueba de texto |
| Revisión: etiquetas «Dock» duplicadas | Filas «Dock» y «**Tamaño y posición del Dock**» | Prueba de etiquetas únicas |
| Bienvenida: «Omitir» ambiguo | «Omitir activa los componentes predeterminados (Dock, Barra superior y Dynamic Island). No cambia el inicio automático ni oculta la barra de tareas.» (la frase anterior, «nada se cambia en Windows», era imprecisa) | Prueba de texto |
| Botones | «Continuar de todos modos» ocupaba todo su botón: botones ensanchados | Prueba: cada etiqueta cabe en una línea dentro de su botón |

**Comprobaciones funcionales pedidas**
- *Inicio automático por defecto:* **desactivado**. La captura que lo mostraba «Sí» venía de mi muestra de prueba (hacía clic en el interruptor antes de dibujar), no del valor real. Ahora las capturas usan los valores seguros (+ Peripherals para ver la dependencia) y muestran «No». Pruebas: valores seguros con `Autostart=false`/`HideTaskbar=false`; y en CI real (ONB2) omitir **no crea** el valor `Run\WinMac`.
- *Fallo de Dynamic Island de las capturas:* es un **escenario de prueba inventado** en `RenderOffscreen` (ahora marcado en cada captura con «Captura de muestra · datos de prueba»). No es un problema del paquete: la Isla arranca de verdad bajo el host en la integración con NotchWin (CI en verde).

**Qué NO prueban las pruebas de geometría:** que el alto real de un texto en Segoe UI coincida con la estimación (por eso se revisaron las capturas), ni la estética, ni la interacción real con la ventana. Quedan para la validación manual (sección 10).

**Revisado a ojo en esta ronda:** las 12 capturas de revisión (6 pantallas × claro/oscuro, escala 2), Apariencia y Resultado a 100 % en ambos temas y el detalle técnico a 150 %. **No revisadas a ojo:** Apariencia/Resultado a 125 % y 150 % (están en el artefacto; la geometría es en DIP y las pruebas de redondeo a píxeles de 100/125/150/175/200 % pasan).
