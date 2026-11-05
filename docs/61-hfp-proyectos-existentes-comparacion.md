# 61 — HFP en Windows: lo que enseñan los proyectos existentes, comparación con Hatsu y correcciones

Etiquetas: **DOC** documentación oficial (Microsoft) · **CÓDIGO** leído en el repositorio · **README/NOTAS** afirmaciones del autor · **ISSUE** informe de usuarios · **INFERIDO** · **PEND.** requiere hardware/autorización.
Fuente de lectura: raw.githubusercontent.com y páginas de GitHub (learn.microsoft.com sigue sin resolverse desde la sesión; la documentación oficial se leyó por resultados de búsqueda de sus páginas, citados abajo).

## 1. Proyectos estudiados
| Proyecto | Licencia | Qué demuestra |
|---|---|---|
| [MYSTRAVIL/klangbruecke](https://github.com/MYSTRAVIL/klangbruecke) (`docs/FINDINGS.md`, `src/Klangbruecke/Bluetooth/CallTransportService.cs`) | **«Personal project, unlicensed»** (todos los derechos reservados) | README: «una llamada real se enruta al PC con audio en los dos sentidos» (Win10 19045, MSIX con certificado autofirmado; el autor reconoce que ConnectAsync es inestable y que parte de la API de enrutado no existe en Win10) |
| [BestOwl/MyPhone](https://github.com/BestOwl/MyPhone) (`MyPhone/Device/DeviceCallServiceProvider.cs`, `MyPhone.Demo/MyPhone.Demo.HFP/HFP.cs`, `Package.appxmanifest`, [issue #26](https://github.com/BestOwl/MyPhone/issues/26)) | **MIT** © NightOwl 2021 | Secuencia de registro y filtro de líneas; llamadas **salientes**; **no** gestiona llamadas entrantes ni audio en el código leído. Su propio README: las API «solo sirven para el control de llamadas»; el audio SCO propio fracasó |
| [surya-sk/my-phone](https://github.com/surya-sk/my-phone) | **sin licencia** | Declara `phoneCall`, `phoneLineTransportManagement`, `phoneCallSystem`, `uap13:windows.phoneCallActivation`; no está en la Store (zip con certificado autofirmado). Usa una clave comunitaria de Limited Access Feature (**no se usa aquí**, ver §6) |
| Otros citados en el issue #26: Thy Phone, CallCenter, Grid3, Intel Unison, Sefirah | no revisados | Referencias de que existen apps que lo logran; sin código leído |

**Sobre la afirmación de Klangbruecke:** se verifica en su documentación, no en un resultado reproducido por nosotros. FINDINGS §12 dice que antes de esa compilación «los resultados anteriores eran de Thy Phone» (otra app), así que el autor corrigió un falso positivo; lo declarado ahora es una llamada contestada en el teléfono y llevada al PC con audio bidireccional, con el nivel del micrófono de comunicaciones como causa de mala voz de salida (hipótesis). Es evidencia **de un autor, en un equipo, sideloaded**, no una capacidad demostrada para una app de la Store.

## 2. Qué hacen (CÓDIGO / NOTAS)
**Secuencia común (ambos):** `PhoneLineTransportDevice.GetDeviceSelector(Bluetooth)` → `DeviceInformation.FindAllAsync` → `FromId` → **`RequestAccessAsync()`** → `IsRegistered()` → **`RegisterApp()`** → `ConnectAsync()` → recién entonces `PhoneCallManager.RequestStoreAsync()` → `RequestLineWatcher()` → líneas.
- **MyPhone** filtra la línea con **`line.TransportDeviceId == transport.DeviceId`** (no solo «Bluetooth») y descubre las líneas **después** de conectar.
- **Klangbruecke**: asigna el dispositivo **antes** de cualquier `await`; anota en el registro **antes** de `RegisterApp`; **relee `IsRegistered()` después** («RegisterApp puede volver sin hacer nada»); **no evalúa el bool de `ConnectAsync`** («False incluso cuando las llamadas se enrutan»); `UnregisterApp` solo si `IsRegistered`; un bucle de reconciliación cada 30 s; una sola conexión en vuelo; relaciona el transporte con el teléfono por **dirección Bluetooth**.
- **Su hallazgo clave:** el fallo original era `E_ACCESSDENIED` en `RegisterApp` por **no llamar antes a `RequestAccessAsync`**.
- **Manifiesto:** MyPhone = `runFullTrust` + **`phoneLineTransportManagement`** + `internetClient` (sin `bluetooth`); Klangbruecke = `phoneLineTransportManagement` + `runFullTrust`; my-phone añade `phoneCall`, `phoneCallSystem`, `windows.phoneCallActivation`.
- **Control de llamadas:** Klangbruecke no dibuja nada: cuando la app tiene el rol, **Windows muestra su propia ventana de aceptar/rechazar y en llamada** (silenciar, colgar). Para llevar el audio de una llamada contestada en el teléfono al PC, apunta a `PhoneCall.ChangeAudioDevice(LocalDevice)` (contrato de llamadas 6: **existe en Windows 11, no en Windows 10**) y dice que la PC **no** recibe el audio sola aunque el teléfono tenga todo activado.
- **Audio:** durante la llamada **no aparecen** endpoints «Hands-Free» en el enumerador; Windows codifica en SCO el **dispositivo de captura de comunicaciones predeterminado**; la voz saliente mala fue el nivel del micrófono (~8 %). El registro es lo que hace que el teléfono **ofrezca el PC como dispositivo de audio** (Windows no ofrece el rol de manos libres por sí solo).
- **Problemas conocidos:** emparejamiento con IRK obsoleto (eventos BTHUSB 35/16/24) → quitar y volver a emparejar; el **Asistente de concentración** (reglas «jugando» / «pantalla completa») desconecta y reconecta el enlace HFP; otra app con el rol (Enlace Móvil) → `DeniedBySystem`; en Windows 11 `RegisterApp` sin efecto o denegado ([issue #26](https://github.com/BestOwl/MyPhone/issues/26), Q&A de Microsoft sobre la compilación 26300).

## 3. Comparación con Hatsu (antes → ahora)
| Paso | Klangbruecke | MyPhone | Hatsu antes (930e439) | Hatsu ahora |
|---|---|---|---|---|
| Enumerar transportes | selector + FindAll | selector(Bluetooth) | igual | igual (+ comprobación de contrato 6) |
| RequestAccess antes de Register | sí | sí | sí | sí |
| Anotar antes de RegisterApp | sí | no | no | **sí** |
| Releer `IsRegistered` tras registrar | **sí** | no | **no (error)**: daba «Listo» si no lanzaba excepción | **sí** (`RegistrationPlanner`) |
| Evaluar el bool de `ConnectAsync` | no (solo lo registra) | lo usa como éxito | ignorado | ignorado y registrado |
| Líneas | — | filtra `TransportDeviceId == DeviceId` | solo «Bluetooth» (**error**: otro teléfono/otra app) | **filtra por `DeviceId` de los transportes registrados** |
| Registro perdido (re-emparejar, otra app) | bucle 30 s | no | **no** | **reconciliación cada 30 s + nuevo registro** |
| Reconexión HFP | sí | no | no | **reintento con límite (45 s sin línea)** |
| `UnregisterApp` al salir | solo si registrado | sí | solo los registrados «por nosotros» | **todo transporte que siga registrado** |
| Windows 10 | detecta ausencia del contrato | — | habría fallado sin explicar | **mensaje claro (requiere Windows 11)** |
| Acciones | UI de Windows | — | `PhoneCall.*` | `PhoneCall.*` + **pasar al PC si el audio quedó en el teléfono** |
| Capacidades | `phoneLineTransportManagement` | idem | `phoneCall` (A) o + restringida (B) | solo el paquete **B** (restringida) |
| ANCS | — | — | complementa | complementa; **el sistema manda**: una llamada con id del sistema no la cambia ANCS, ANCS no puede cerrar una llamada activa |

**Errores corregidos en Hatsu:** (1) estado «Listo» sin comprobar el registro; (2) filtro de líneas insuficiente; (3) sin vigilancia del registro ni reconexión; (4) sin guarda de contrato 6; (5) sin ruta para una llamada contestada en el teléfono; (6) mensajes de causa que no distinguían «otra app tiene el rol», «falta la capacidad» y «RegisterApp sin efecto».

## 4. ¿Hace falta explícitamente…? (respuestas)
- `Connect()`/`ConnectAsync()`: ambos proyectos lo llaman tras registrar; su resultado no es fiable → se invoca, no se evalúa. La señal de éxito es `IsRegistered()` + que aparezca la línea.
- Registrar antes: **sí**, y la línea solo es visible tras registrar (DOC: «Registration is required before the app can see the PhoneLine for the device»; por usuario).
- Seleccionar transporte concreto: sí → filtro por `DeviceId`.
- Esperar estado / obtener línea tras conectar: sí (el vigilante de líneas se arranca después).
- Activar ruta de audio: **sí**, `ChangeAudioDevice(LocalDevice)` si la llamada quedó en el teléfono; Hatsu no maneja endpoints.
- Mantener sesión y reconexiones: sí (reconciliación).

## 5. Capacidad restringida: lo que se sabe
- DOC: la capacidad listada para `PhoneLineTransportDevice.RegisterApp` es `phoneLineTransportManagement`; el registro es por usuario y permanece hasta `UnregisterApp` o hasta quitar el dispositivo.
- `phoneCall` **no basta**: ningún proyecto que funciona omite la restringida, y la documentación oficial no asocia el registro a `phoneCall`. No hay evidencia de que `RegisterApp` funcione sin ella.
- Distribución de los proyectos: MyPhone y Klangbruecke, **sideloading** (certificado autofirmado); my-phone, zip autofirmado. **No encontré** ninguna app de la Store que declare `phoneLineTransportManagement` (sí aparece en el manifiesto de MyPhone una identidad con prefijo numérico, pero eso no prueba publicación).
- Sideloading: la documentación general de capacidades dice que se puede declarar sin aprobación; **en Windows 11** Klangbruecke reporta «registro denegado en sideloading» en su equipo (sección 8 de FINDINGS) y el issue #26 reporta `DeniedBySystem`/sin efecto hasta con la capacidad. Es decir: **ni siquiera el sideloading con la capacidad está garantizado en Windows 11**. NO VERIFICADO si un paquete de la Store con la capacidad **aprobada** cambia eso.
- Limited Access Feature: hilos contradictorios. Un comentarista dice que Microsoft respondió (oct. 2024) que la API salió de la lista LAF y ya no requiere token; Klangbruecke prueba con token inválido y el registro funciona (en Win10). **No se usa ninguna clave comunitaria ni se altera el registro de Windows** (el `BypassRegistration` de HKLM que se menciona es una modificación del sistema fuera de una app de Store y no se implementa).

### Ruta de autorización (propietario)
1. Presentar a Partner Center (vuelo Beta primero) el paquete `…-beta-callcontrol-unsigned.msix` (CI; capacidades verificadas: `runFullTrust`, `phoneLineTransportManagement`, `phoneCall`). El envío con una capacidad restringida exige la **justificación** del envío (DOC: «you must provide info during the app submission process»).
2. Texto sugerido en docs/60 §3. Conviene añadir: app de código abierto (enlace), función opt-in desactivada por defecto, sin datos fuera del equipo, `UnregisterApp` al desactivar.
3. Si Microsoft aprueba: probar en Windows 11 con teléfono real (este es el primer momento en que la hipótesis «Store + capacidad aprobada» se pone a prueba). Si rechaza: Stable sigue sin la función; ANCS (nombre, rechazo) y avisos no se ven afectados.
4. Riesgo para la certificación: declarar la capacidad sin aprobación **bloquea el envío**; por eso no se declaró en ningún paquete publicable.

## 6. Licencias y reutilización
- **Klangbruecke: sin licencia → no se copió nada.** Se estudió el comportamiento descrito en sus documentos y el orden de llamadas a API públicas (hechos técnicos); el código de Hatsu es propio.
- **MyPhone: MIT.** Tampoco se copió código; las ideas (filtrar por `TransportDeviceId`, descubrir líneas tras conectar) se reimplementaron. Si algún día se copia texto de su código, hay que conservar «Copyright (c) 2021 NightOwl» y el permiso MIT. Se les agradece como referencia.
- **my-phone: sin licencia** → solo lectura de su README.
- Ninguna dependencia nueva; sin servicios externos.

## 7. Estado
| Elemento | Estado |
|---|---|
| Secuencia de registro corregida, vigilancia, filtro, reconexión, contrato 6, pasar al PC | implementado, probado en lógica pura (Connect.Core 58 pruebas, incl. `RegistrationPlanner`) |
| Registro/Conexión/Líneas/Audio con teléfono real | **PEND.** (hardware + capacidad aprobada) |
| Audio bidireccional | **NO verificado** |
| Android | misma ruta HFP; sin ANCS; **PEND.** |
| Ventana de llamada propia de Windows | puede aparecer junto a la píldora de Hatsu cuando la app tiene el rol (NOTAS de Klangbruecke) — comportamiento a validar |
