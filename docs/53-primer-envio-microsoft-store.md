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

## 2. Distribución limitada: ¿se puede probar antes de ser público? [MS/SEGUNDA MANO]
Partner Center ofrece **audiencia privada** («Private audience»): el producto solo lo ven/instalan los grupos de clientes que definas (por cuentas Microsoft) y las reseñas no se publican. Condiciones halladas: hay que elegirla **antes del primer envío** (una vez público ya no se puede volver a privado) y el grupo se define en Partner Center. Los *vuelos de paquetes* (package flights) son otro mecanismo, para versiones posteriores. **Importante:** incluso con audiencia privada el envío **pasa la certificación completa** (hasta ~3 días hábiles). No encontré (en este entorno) la documentación de MSIX/aplicaciones específica de «ocultar de la búsqueda con enlace directo»; si tu panel ofrece esa opción, también sirve. **Verifica en Partner Center → Precios y disponibilidad → Visibilidad.**
→ Recomendación: **primer envío con audiencia privada = solo tu cuenta Microsoft.**

## 3. Campos y materiales para Partner Center (tú los completas)
**Producto Hatsu Desktop (ID 9NJ0XLP6NXVJ)**
1. **Precios y disponibilidad:** gratis; mercados (mínimo Chile; recomiendo todos o los que quieras); **visibilidad: audiencia privada** con tu cuenta Microsoft; publicar «manualmente» tras certificar.
2. **Propiedades:** categoría (p. ej. Personalización / Utilidades); **URL de política de privacidad** (ver §4); sitio web (el repositorio); información de soporte (URL de incidencias de GitHub); requisitos del sistema (Windows 11, x64).
3. **Clasificación por edades:** cuestionario IARC (sin contenido, sin compras, sin chat, comparte ubicación solo si la activas).
4. **Paquetes:** subir `HatsuDesktop-2.0.1.0-store-unsigned.msix` **sin tocarlo** (la Store lo firma). Partner Center detectará `runFullTrust` y pedirá **justificación**.
5. **Ficha de la tienda** (es y en-us, ambos declarados en el manifiesto): nombre, descripción, novedades, capturas (mínimo 1; recomendado 1366×768 o mayores: las capturas reales del CI están en el artefacto `capturas-*`), logotipo 1:1 (300×300), palabras clave, copyright.
6. **Notas para certificación** (texto abajo).

**Justificación de `runFullTrust`** (campo de Partner Center, en inglés porque lo lee el revisor):
> Hatsu Desktop is a Win32 desktop shell enhancement (menu bar, dock and notch-style activity island) written in .NET. It is an existing, unpackaged Win32/.NET application packaged with MSIX; it cannot run inside AppContainer because it hosts top-level windows, registers an application desktop toolbar (SHAppBarMessage) to reserve screen space, enumerates other apps' windows, and launches its own helper processes (Bar, Dock, Notch, Connect). It needs full trust to work as designed. It does not install services or drivers, does not elevate, does not modify system files, and everything it changes (for example hiding the Windows taskbar when the user chooses to) is reverted on exit.

**Capacidades sensibles** (por si las pregunta): `userNotificationListener` = Centro de notificaciones de Hatsu (solo con permiso del usuario); `location` = clima por ubicación (opcional); `webcam` = espejo de la Isla (vista previa, sin grabar); `bluetooth` = módulo opcional Connect con el iPhone.

**Notas para el equipo de certificación** (en inglés):
> No account or sign-in is needed. Launch "Hatsu Desktop" from Start; a first-run wizard lets you choose components. The app adds a menu bar at the top, a dock at the bottom and an activity island; the Windows taskbar is hidden only if enabled in the wizard/settings and is restored on exit (tray menu → Quit, or run `HatsuDesktop.exe --quit`). Startup at sign-in is off by default and is a StartupTask the user enables. Optional features (clipboard history, notification center, location weather, iPhone Connect, camera mirror) are off or ask for Windows permission. Network: weather (open-meteo.com), speed test (cloudflare.com, only on user click), lyrics (music.163.com, only if enabled). No telemetry. Privacy policy URL is in the listing.

**Riesgo de certificación [SIN VERIFICAR]:** el revisor podría objetar ocultar la barra de tareas, el gancho de teclado (tecla Windows) o la vigilancia del portapapeles. Se mitiga con las notas y la política; si rechazan, el motivo llega en el informe y se ajusta (p. ej. desactivar por defecto una función en la versión Store).

## 4. URL de la política de privacidad
Debe ser una URL pública estable. Opciones (decides tú): (a) el archivo del repositorio público (`https://github.com/PipeSick/win11-macos/blob/<rama>/docs/privacy/POLITICA-DE-PRIVACIDAD.md`; hoy no hay `main` y no cambio ramas predeterminadas); (b) GitHub Pages (más limpio; requiere activarlo, acción tuya). Antes de publicarla hay que resolver los **[VERIFICAR]** del borrador y añadir un contacto.

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
