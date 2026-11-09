# 52 · MSIX y Microsoft Store (Fase 3.2)

Estado: **prototipo experimental en CI. No se envió nada a la Store, no se publicó ningún release, el MSI sigue intacto y Smart App Control sigue activado.** Etiquetas: **[MEDIDO]** CI de este repo · **[MS/SEGUNDA MANO]** leído en resultados de búsqueda (learn.microsoft.com no es accesible desde el entorno; hay que confirmarlo en la página oficial) · **[SIN VERIFICAR]**.

## 0. Cuatro cosas distintas (no mezclar)
| Concepto | Qué es | ¿Resuelve los eventos 3077/3033? |
|---|---|---|
| Firma del **paquete** MSIX | Firma de todo el `.msix` (la hace la Store, o un certificado propio) | No por sí sola: un certificado de prueba/autofirmado no es de confianza en ningún PC |
| Firma de **ejecutables y DLL** | Authenticode de cada `.exe/.dll` (SignPath, cert comercial) | Sí cuenta para SAC, pero es otra vía (docs/50, 51) |
| **Certificación** de la Store | Revisión automática y manual de Microsoft (hasta ~3 días hábiles [MS/SEGUNDA MANO]) | No es firma; es el requisito para que la Store firme |
| **Aceptación por SAC** | Reputación en la nube o firma válida, evaluada al cargar cada módulo | Es lo que importa. Comunidad: los paquetes firmados por la Store se permiten; **que los `.exe/.dll` sin firma *dentro* de un paquete de la Store carguen NO está confirmado** [SIN VERIFICAR] |

Por tanto: **no afirmo que MSIX solucione 3077/3033.** Solo se sabrá con el paquete firmado por la Store en tu Windows con SAC activo.

## 1. Auditoría de viabilidad por componente
Un MSIX con `runFullTrust` es un proceso Win32 normal (no AppContainer): ganchos globales, `SHAppBarMessage`, ventanas, tuberías con nombre y procesos hijo funcionan igual. Lo que cambia es la **virtualización** y el **inicio automático**.

| Componente | Efecto de MSIX | Estado |
|---|---|---|
| `HatsuDesktop.exe/.dll` | Se ejecuta desde `C:\Program Files\WindowsApps\…` (solo lectura) con identidad de paquete | Probado en CI (ver §3) |
| Dock, Barra, Connect, NotchWin | Procesos hijo del paquete, lanzados por ruta junto al host | Barra y Dock probados en CI; NotchWin e IPC: ver §3 |
| AppBar / TrayHost (`Shell_TrayWnd`) | API de Win32 sin cambios; el aislamiento no la bloquea | Reserva de borde ya probada fuera de MSIX; **empaquetada: requiere tu PC** |
| Letras sincronizadas de NotchWin | Sin cambios: solo red y UI | Sin tocar |
| Vortice/SharpGen, Windows App SDK | Bibliotecas de la carpeta; WinAppSDK va autocontenido en la carpeta (no como dependencia de paquete) | Incluido en el paquete; arranque probado en CI |
| Inicio automático | **`HKCU\Run` no se honra empaquetado** → `StartupTask` del manifiesto | Implementado (`Autostart.cs`); activación real **requiere tu PC** |
| Configuración persistente (`%APPDATA%\WinMac`) | Escrituras **redirigidas** a un almacén del paquete; las lecturas ven lo existente | Medido en CI (§3) |
| Cambio de tema del sistema (HKCU\…\Personalize) | Con virtualización por defecto **no llega a Explorer** | Medido en CI (§3); solución: `unvirtualizedResources` (restringida) |
| Desinstalación | Quita el paquete y su almacén; no ejecuta acciones propias | Sin acción personalizada que migrar |
| Bluetooth (Connect) | Declarada capacidad `bluetooth` | **Requiere tu PC con un iPhone** |
| Ocultar la barra de tareas, teclado/atajos globales, portapapeles (NotchWin) | Funcionan, pero son **comportamientos que la revisión de la Store puede cuestionar** [SIN VERIFICAR] | Riesgo de certificación, no técnico |

## 2. Camino de la Store [MS/SEGUNDA MANO]
- Cuenta de **desarrollador individual: gratuita** (anuncio de Microsoft, sept. 2025); exige cuenta Microsoft personal y verificación de identidad con documento oficial y selfie. Una tabla antigua de terceros listaba Chile con tarifa; **comprueba en el registro real** si te cobra algo antes de pagar.
- El paquete **no se firma tú**: se sube el MSIX sin firmar (o `.msixupload`) y la Store lo firma tras certificar. **Costo: 0** si el registro individual es gratuito para Chile.
- `runFullTrust` es capacidad restringida: Partner Center pide una justificación (basta decir que es una aplicación Win32 empaquetada). `unvirtualizedResources` también lo es y es una justificación más difícil.
- Identidad (`Name`, `Publisher`) la asigna Partner Center al reservar el nombre; hay que copiarla al manifiesto (parámetros de `Build-Msix.ps1`).
- Certificación: hasta ~3 días hábiles [MS/SEGUNDA MANO]; el primer envío puede rechazarse y repetirse.
- Hace falta política de privacidad (URL) porque hay red y capacidades sensibles; descripción honesta de hooks, portapapeles, ocultar barra de tareas.
- Distribución limitada / pruebas antes de publicar: la Store admite ocultar el producto de la búsqueda y compartirlo por enlace directo, y vuelos de paquetes para grupos privados [SIN VERIFICAR en este entorno: no hallé fuente]. Es la vía para probar el paquete **firmado por la Store** sin publicarlo al público.

## 3. Prototipo: qué se midió en CI (run 41933489756, commit `ea48b01`) [MEDIDO]
Runner de GitHub (no es tu PC, **sin Smart App Control**, certificado de prueba autofirmado instalado solo allí). El MSI no se tocó y su prueba sigue pasando (3 min, sin colgarse; la prueba de AppBar también).

Dos variantes empaquetadas desde el mismo `dist/WinMac` (171,6 MB cada una), instaladas con `Add-AppxPackage`:

| Comprobación | `Default` | `Unvirtualized` |
|---|---|---|
| Se instala; paquete completo en `WindowsApps` (host, DLL, Barra, Dock, Connect, NotchWin, Vortice, SharpGen) | OK | OK |
| El host corre **con identidad de paquete** | OK | OK |
| Barra y Dock reales llegan a `Running` desde la carpeta del paquete; sin errores de carga de bibliotecas | OK | OK |
| Cierre ordenado (`--quit`) y desinstalación limpia | OK | OK |
| Inicio automático empaquetado (`StartupTask`): activar y desactivar | OK (activable en CI) | OK |
| Escritura en `HKCU\…\Themes\Personalize` (cambio de tema del sistema) | **la ve el sistema** | la ve |
| Escritura en `%APPDATA%` | **NO la ve el sistema** (queda en el almacén del paquete) | la ve |
| Escritura en `HKCU\…\Run` | **NO la ve** (por eso `StartupTask`) | la ve |

Conclusiones medidas:
1. **Hatsu arranca y funciona como MSIX de plena confianza** (Barra + Dock + procesos hijo + cierre + desinstalación) en CI. No apareció ninguna restricción que impida ser «shell de escritorio».
2. **No hace falta `unvirtualizedResources`** (capacidad restringida): el cambio de tema funciona con la variante por defecto. Lo único virtualizado es `%APPDATA%`, que solo usan los procesos del propio paquete (todos comparten el mismo almacén). Efecto: herramientas externas (`Diagnose-Install.ps1`, `Migrate-FromWinMac.ps1`) no ven los datos del paquete.
3. Se recomienda la variante `Default`: menos permisos restringidos que justificar ante la Store.

### Qué NO se probó (requiere tu PC o la Store)
- **Smart App Control**: el runner no lo tiene; un certificado de prueba no es de confianza. **Esto no demuestra nada sobre 3077/3033.**
- Que los `.exe/.dll` sin firma *dentro* de un paquete firmado por la Store carguen bajo SAC.
- AppBar/reserva de borde, bandeja y ocultar la barra de tareas **empaquetados** (probados solo sin MSIX).
- Bluetooth/iPhone (Connect), letras sincronizadas de NotchWin (red) y atajos/portapapeles con identidad de paquete.
- Que `StartupTask` abra Hatsu en segundo plano al iniciar sesión (hoy el manifiesto no pasa `--background`; sin él, un arranque sin argumentos no es lo mismo; verificar en tu PC).
- Migración de datos de una instalación MSI/zip anterior (lectura de `%APPDATA%\WinMac` existente desde el paquete).
- Certificación de la Store (la política puede objetar ocultar la barra de tareas, ganchos de teclado o vigilancia del portapapeles).

## 4. Respuestas
1. **¿Puede Hatsu funcionar completamente mediante MSIX?** Técnicamente sí en lo medido (arranque, Barra, Dock, hijos, cierre, tema, inicio automático). Faltan por comprobar en tu PC AppBar, bandeja, Bluetooth, NotchWin y datos previos. Sin bloqueo fundamental encontrado.
2. **¿Qué falta para un paquete válido para la Store?** Identidad (`Name`/`Publisher`) de Partner Center; versión con 4.ª cifra 0 (ya); política de privacidad con URL; justificación de `runFullTrust`; capturas/descripción; revisar que `--background` llegue al `StartupTask`; resolver la certificación. Se sube **sin firmar**: la Store lo firma.
3. **Pruebas completadas / pendientes:** ver §3. Pendientes (todas requieren tu PC con paquete **firmado por la Store**): SAC, AppBar, Bluetooth, NotchWin, `StartupTask`, migración.

## 5. Camino más rápido y comparación
| Vía | Costo | Tiempo realista | Qué demuestra para SAC | Riesgos |
|---|---|---|---|---|
| **MSIX por la Store** | 0 si el registro individual es gratuito para Chile (comprobar) | cuenta + verificación de identidad (hoy) → certificación hasta ~3 días hábiles [MS/SEGUNDA MANO] → probar en tu PC | Paquete firmado por Microsoft; **binarios internos sin firma: sin confirmar** | Política (hooks, ocultar barra), `runFullTrust` |
| SignPath Foundation | 0 | Semanas: hacer público/maduro el proyecto, `main`, release, política, solicitud y aprobación (docs/51) | Firma de **propios**; 8 DLL ajenas siguen sin firma; SAC además usa reputación | Rechazo por poca visibilidad; publicador = SignPath |
| Cert comercial / Azure Artifact Signing | Pago (comercial) o no disponible en Chile (Azure) | Días | Firma válida; no garantiza SAC | Gasto; requiere tu autorización |

**Más rápido y realista:** MSIX por la Store. Se puede **combinar** más adelante: firmar los binarios propios con SignPath *dentro* del MSIX no cambia el flujo de la Store. Si la Store-firmada falla bajo SAC por los binarios internos, ese es el momento de pedir SignPath.

## 6. Qué tienes que hacer tú (yo no envío nada a la Store)
1. Crear la cuenta de **desarrollador individual** en Partner Center con tu cuenta Microsoft (verificación con documento y selfie). Confirmar si cobra algo para Chile **antes** de pagar.
2. **Reservar el nombre** «Hatsu Desktop» y copiarme el `Package/Identity/Name` y `Publisher` que te asigne.
3. Darme una URL de **política de privacidad** (o autorizo a redactarla; hay que publicarla en algún sitio).
4. Autorizarme a: generar el paquete **sin firmar** con esa identidad (artefacto de CI) para que tú lo subas a Partner Center, **sin publicarlo al público** (restringir/ocultar la ficha mientras pruebas).
5. Instalar desde la Store el paquete firmado en tu Windows con SAC activo y pasarme el resultado (eventos 3077/3033 sí/no).
