# 53 · Primer envío a Microsoft Store (preparación)

**Nada de esto se ha enviado.** No se publicó, no se envió a certificación, no hay releases ni compras. Smart App Control sigue activado. Etiquetas: **[MEDIDO]** CI de este repo · **[MS/SEGUNDA MANO]** leído en búsquedas, confirmar en Partner Center · **[PENDIENTE PC]** sin probar en tu Windows.

## 1. Lo que está listo
| Entregable | Dónde |
|---|---|
| Identidad oficial (`PipeSick.HatsuDesktop`, `CN=ACC0FCB8-…`, `PipeSick`, PFN `…_83tr6qzbbarj8`, Store ID `9NJ0XLP6NXVJ`) | `installer/msix/store-identity.json` (única fuente; un test falla si cambia o se duplica) |
| MSIX **sin firmar** para certificación | Artefacto de CI `msix-store-sin-firmar-<commit>` (`HatsuDesktop-2.0.1.0-store-unsigned.msix` + `.sha256`) |
| Copia **firmada solo para CI** (mismos bytes, certificado autofirmado efímero) | Solo existe dentro del runner; **no se sube** |
| Pruebas en CI: instalación, identidad, PFN exacta, Barra y Dock, cierre, inicio automático, desinstalación | `tools/ci/Test-Msix.ps1` (informe `msix-informe-<commit>`) |
| Inicio automático con `--background` | `StartupTask` + `LaunchArguments` (el host añade `--background` al detectar la activación). Fuera de MSIX no cambia nada (el MSI y `HKCU\Run` quedan como estaban) |
| Borrador de política de privacidad | `docs/privacy/POLITICA-DE-PRIVACIDAD.md` |

Capacidades declaradas (porque el código las usa): `runFullTrust` (restringida), `userNotificationListener`, `bluetooth`, `location`, `webcam`. Sin `unvirtualizedResources`. Alias de ejecución `HatsuDesktop.exe` para poder usar `--diagnose` y `--quit` desde una consola.

### Resultado medido en CI [MEDIDO] — run 41940742204, commit `b1912fa`
Paquete con la identidad oficial, instalado en el runner (certificado de prueba efímero con asunto = Publisher oficial): se instala; la ruta instalada es `PipeSick.HatsuDesktop_2.0.1.0_x64__83tr6qzbbarj8` (la PFN coincide con Partner Center); el host corre con identidad de paquete; Barra y Dock llegan a `Running` desde la carpeta del paquete; `--quit` cierra todo; el inicio automático (`StartupTask`) se activa y desactiva; se desinstala limpio. El MSI sigue pasando su prueba (≈2 min) y la de AppBar. El artefacto sin firmar (`msix-store-sin-firmar-b1912fa…`, 171 MB) se generó y se subió. Nota: las primeras filas del informe (capacidades, PFN exacta) no las vi una a una en el log; no apareció ningún error en el paso. Para verlas, descarga el artefacto `msix-informe-<commit>`.

### Cambios previos al primer envío (autorizados) [MEDIDO en CI — run 41957854894, commit `fd204d1`]
- **Agentes de IA desactivados de fábrica solo en la versión Store** y solo cuando no hay ajustes guardados (`NotchSettings.Factory`; con identidad de paquete `--package-info` informa `agentsDefault=off`). Siguen completos y se activan en Ajustes. Fuera de la Store y con ajustes guardados nada cambia. Hatsu Notch (`89dc14f` en `PipeSick/notch-win`, rama `feat/hatsu-notch-rebrand`) también arranca con el módulo apagado hasta que el host le envía el valor real.
- **Rebranding visible a «Hatsu Notch»:** textos de ajustes, onboarding, bandeja, diagnóstico y extensiones en Hatsu; en Hatsu Notch, bandeja, ventana de ajustes, textos, propiedades del ejecutable (descripción y producto) y User-Agent (`HatsuNotch/0.1`). Se conservan nombres internos (`NotchWin.App.exe`, carpetas `NotchWin`/`WinMac`, tópicos y protocolo IPC). «Dynamic Island» (marca de Apple) eliminado de los textos.
- **Cleanup:** aviso fijo en el panel y en el diálogo de confirmación: «Los archivos se eliminan de forma definitiva: no pasan por la Papelera de reciclaje y no se podrán recuperar.» Comportamiento sin cambios; una prueba falla si algún día pasa a la Papelera sin actualizar aviso y política.
- **Política:** contacto en GitHub Issues; versiones española e inglesa actualizadas con lo anterior; **no publicada**.
- **Comprobado dentro del MSIX instalado en CI:** PFN exacta, capacidades, `agentsDefault=off`, `NotchWin.App.exe` descrito como «Hatsu Notch», textos nuevos presentes en los binarios, hash de `HatsuDesktop.dll` igual al compilado en esa misma ejecución.

## 2. Audiencia privada para una app MSIX [MS/SEGUNDA MANO — no pude abrir learn.microsoft.com ni tu Partner Center]
Según la página de Microsoft «Choose visibility options for MSIX app» (leída en resultados de búsqueda), el apartado **Precios y disponibilidad → Visibilidad** de una app MSIX permite **Audiencia privada**:
- Hay que indicar al menos un **grupo de clientes** con cuentas Microsoft personales (no cuentas de trabajo/escuela). Quien no esté en el grupo no ve la ficha ni puede descargar.
- Con audiencia privada **no se puede programar fecha de lanzamiento**; el paso a público es una decisión posterior.
- **Si un envío se hace público, ya no se puede volver a privado** en envíos posteriores → elegirla desde el primer envío.
- Sigue pasando **la certificación completa**; las reseñas del periodo privado no se publican.
- Tras publicarse en privado, los testers entran con un **enlace directo a la ficha** (con sesión de Microsoft) o buscando en la Store.
**Lo que no puedo confirmar:** que tu cuenta nueva muestre esa opción (la ves al crear el envío). Hay un reporte de la comunidad de testers que ven «Producto no encontrado» tras publicar en privado; si pasa, revisa el grupo y espera unos minutos tras «publicado».
→ **Comprobación previa sin riesgo:** en Partner Center abre el envío → *Precios y disponibilidad* → ¿aparece «Audiencia privada»? Si no aparece, **no envíes nada** y avísame.

## 3. Campos y materiales para Partner Center (tú los completas)
**Producto Hatsu Desktop (ID 9NJ0XLP6NXVJ)**
1. **Precios y disponibilidad:** gratis; mercados (mínimo Chile; recomiendo todos o los que quieras); **visibilidad: audiencia privada** con tu cuenta Microsoft; publicar «manualmente» tras certificar.
2. **Propiedades:** categoría (p. ej. Personalización / Utilidades); **URL de política de privacidad** (ver §4); sitio web (el repositorio); información de soporte (URL de incidencias de GitHub); requisitos del sistema (Windows 11, x64).
3. **Clasificación por edades:** cuestionario IARC (sin contenido, sin compras, sin chat, comparte ubicación solo si la activas).
4. **Paquetes:** subir `HatsuDesktop-2.0.1.0-store-unsigned.msix` **sin tocarlo** (la Store lo firma). Partner Center detectará `runFullTrust` y pedirá **justificación**.
5. **Ficha de la tienda** (es y en-us, ambos declarados en el manifiesto): nombre, descripción, novedades, capturas (mínimo 1; recomendado 1366×768 o mayores: las capturas reales del CI están en el artefacto `capturas-*`), logotipo 1:1 (300×300), palabras clave, copyright.
6. **Notas para certificación** (texto abajo).

**Justificación de `runFullTrust`** (campo de Partner Center, en inglés porque lo lee el revisor):
> Hatsu Desktop is a Win32 desktop shell enhancement (menu bar, dock and the Hatsu Notch activity island) written in .NET. It is an existing, unpackaged Win32/.NET application packaged with MSIX; it cannot run inside AppContainer because it hosts top-level windows, registers an application desktop toolbar (SHAppBarMessage) to reserve screen space, enumerates other apps' windows, and launches its own helper processes (Bar, Dock, Notch, Connect). It needs full trust to work as designed. It does not install services or drivers, does not elevate, does not modify system files, and everything it changes (for example hiding the Windows taskbar when the user chooses to) is reverted on exit.

**Capacidades sensibles** (por si las pregunta): `userNotificationListener` = Centro de notificaciones de Hatsu (solo con permiso del usuario); `location` = clima por ubicación (opcional); `webcam` = espejo de la Isla (vista previa, sin grabar); `bluetooth` = módulo opcional Connect con el iPhone.

**Notas para el equipo de certificación** (en inglés):
> No account or sign-in is needed. Launch "Hatsu Desktop" from Start; a first-run wizard lets you choose components. The app adds a menu bar at the top, a dock at the bottom and an activity island; the Windows taskbar is hidden only if enabled in the wizard/settings and is restored on exit (tray menu → Quit, or run `HatsuDesktop.exe --quit`). Startup at sign-in is off by default and is a StartupTask the user enables. Optional features (clipboard history, notification center, location weather, iPhone Connect, camera mirror) are off or ask for Windows permission. Network: weather (open-meteo.com), speed test (cloudflare.com, only on user click), lyrics (music.163.com, only if enabled). No telemetry. Privacy policy URL is in the listing.

**Riesgo de certificación [SIN VERIFICAR]:** el revisor podría objetar ocultar la barra de tareas, el gancho de teclado (tecla Windows) o la vigilancia del portapapeles. Se mitiga con las notas y la política; si rechazan, el motivo llega en el informe y se ajusta (p. ej. desactivar por defecto una función en la versión Store).

## 4. URL de la política de privacidad (GitHub Pages)
**URL propuesta:** `https://pipesick.github.io/win11-macos/privacy/` (español) y `https://pipesick.github.io/win11-macos/privacy/en/` (inglés). Es pública, sin inicio de sesión y estable mientras el repositorio mantenga su nombre (no lo renombro). Alternativa más permanente: un repositorio `PipeSick/pipesick.github.io` (URL `https://pipesick.github.io/hatsu-desktop/privacy/`), si prefieres no depender del nombre del repo.
**Estado: preparado e inerte, NO publicado.** `tools/site/build_site.py` genera el sitio a partir de `docs/privacy/*.md`; `.github/workflows/pages.yml` solo corre a mano (`workflow_dispatch`). Para publicar, tras aprobar el contenido final:
1. GitHub → repositorio → *Settings → Pages → Build and deployment → Source: **GitHub Actions***.
2. *Actions → pages-politica-de-privacidad → Run workflow* (rama `feat/hatsu-phase3-installer-onboarding` o la que decidas; en repos con reglas de entorno, aprueba `github-pages` si lo pide).
3. Abrir la URL y comprobarla sin sesión iniciada. Pegarla en Partner Center → Propiedades → Política de privacidad.
Si la rama de entorno `github-pages` solo permite la rama predeterminada, habrá que permitir esta rama en *Settings → Environments → github-pages* (no cambio la rama predeterminada).

**Nombre visible del componente:** en la ficha y en la política la isla se llama **Hatsu Notch**. Los nombres internos `NotchWin`/`WinMac` siguen en carpetas de datos, procesos y menús de la interfaz (hoy algunos textos del programa aún dicen «Isla (NotchWin)»; cambiarlos es un cambio de código aparte que no hice).

## 5. Qué puedo automatizar y qué haces tú
| Paso | Quién |
|---|---|
| Empaquetar, comprobar identidad, probar en CI, mantener la identidad única | Yo (automatizado en CI) |
| Redactar política, notas, justificaciones, textos de ficha | Yo (borradores) |
| Resolver los [VERIFICAR] de la política | Yo, si me dices que proceda |
| Publicar la política en una URL; elegir contacto | **Tú** |
| Descargar el artefacto sin firmar de Actions y comprobar el SHA-256 | **Tú** |
| Crear el envío en Partner Center, elegir audiencia privada, subir el paquete, pegar textos, **enviar a certificación** | **Tú** (yo no puedo ni debo) |
| Instalar desde la Store en tu Windows con SAC y probar | **Tú** |
| Interpretar resultados y corregir | Yo |

## 6. Instrucciones exactas del primer envío (cuando decidas hacerlo)
1. GitHub → *Actions* → ejecución del commit indicado → *Artifacts* → `msix-store-sin-firmar-<commit>` → descargar y descomprimir.
2. PowerShell: `Get-FileHash .\HatsuDesktop-2.0.1.0-store-unsigned.msix -Algorithm SHA256` y comparar con el `.sha256`.
3. Partner Center → Hatsu Desktop → *Iniciar envío*. **Antes de nada**, Precios y disponibilidad → visibilidad **privada** (solo tú).
4. Completar los campos del §3; subir el `.msix` (debe aceptar sin errores de identidad: nombre/editor ya coinciden); pegar la justificación de `runFullTrust` y las notas de certificación.
5. *Enviar a certificación*. Espera (hasta ~3 días hábiles). Revisa el informe.
6. Al aprobarse, abre el enlace de la Store con tu cuenta, instala.

## 7. Pruebas en tu PC con Smart App Control activado (todas **PENDIENTE PC**)
1. Antes: `Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-CodeIntegrity/Operational'; Id=3077,3033} -MaxEvents 20 | Format-List TimeCreated,Id,Message` (referencia).
2. Instala desde la Store e inicia Hatsu. Repite el comando: ¿aparecen 3077/3033 con `HatsuDesktop.dll` o con las DLL de Vortice/SharpGen? **Esta es la prueba decisiva y la única que responde si SAC acepta el paquete.**
3. Si arranca: Barra reserva el borde superior; Dock; bandeja; atajos; Connect con iPhone; Isla con letras sincronizadas; cámara; notificaciones; ocultar y restaurar la barra de tareas; `HatsuDesktop.exe --diagnose`; activar/desactivar inicio automático en la app y en *Configuración › Aplicaciones › Inicio*; reiniciar sesión y comprobar que arranca en segundo plano (`--background`); desinstalar.
4. Copia/pega el resultado y los eventos.

## 8. Lo que NO está demostrado
Que SAC acepte los binarios sin firma dentro de un paquete firmado por la Store; la certificación; todo lo marcado PENDIENTE PC; que `StartupTask` entregue la activación que el host reconoce (la detección usa `AppInstance.GetActivatedEventArgs`, con prueba unitaria de la lógica pero no de la activación real).
