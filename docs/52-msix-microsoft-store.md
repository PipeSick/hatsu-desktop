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
