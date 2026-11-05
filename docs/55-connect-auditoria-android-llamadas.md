# 55 — Hatsu Connect: auditoría, llamadas iPhone y Android (matriz de viabilidad)

Etiquetas: **OBSERVADO** (probado en dispositivo real, docs/39 y docs/41), **FUENTE** (documentación pública consultada), **INFERIDO**, **PENDIENTE-HW** (necesita dispositivo/permiso que no hay en CI).

## 1. Estado real de Hatsu Connect (iPhone)
| Capacidad | Estado | Evidencia |
|---|---|---|
| Descubrir/conectar BLE (WinRT público, `GattSession.MaintainConnection`) | Implementado | OBSERVADO |
| Batería (GATT 0x180F/0x2A19) | Implementado | OBSERVADO |
| Notificaciones (ANCS) | Implementado | OBSERVADO |
| Detección de llamada entrante + nombre/número (título ANCS) | Implementado (`CallTracker`, píldora en Hatsu Notch) | OBSERVADO |
| Contestada vs rechazada | No distinguible por ANCS (se muestra «terminada», nada inventado) | OBSERVADO/diseño |
| Acciones positivo/negativo ANCS (contestar/rechazar) | Anunciadas por el iPhone; **no ejecutadas** | OBSERVADO que se anuncian; ejecución PENDIENTE-HW |
| MAP (OBEX CONNECT 0xA0), PBAP, AMS | Sesión MAP verificada; PBAP/AMS presentes, sin uso | OBSERVADO |
| Marcar, audio PC (mic/altavoz) | **Bloqueado** (ver 2) | OBSERVADO |

## 2. Llamadas en iPhone: ruta HFP/telefonía y MSIX
- API pública de telefonía: `Windows.Devices.Bluetooth`/`PhoneLineTransportDevice` (HFP). Requiere capacidad **restringida** `phoneLineTransportManagement` (FUENTE). Sin paquete devolvió `DeniedBySystem` (OBSERVADO).
- Las capacidades restringidas requieren aprobación de Microsoft en Partner Center; declararla sin aprobación **bloquea el envío a la Store** (FUENTE). **Firmar no concede HFP**; la identidad MSIX sola tampoco hay evidencia de que lo haga (no se asume).
- HFP propio sobre RFCOMM choca con la pila HFP de Windows y el audio SCO exige controlador (excluido: fuera de lo seguro para Store/SAC).
- Phone Link ya mantiene la relación HFP/PhoneLine del PC; dos clientes compiten (INFERIDO; a validar PENDIENTE-HW).
- CI (runner sin Bluetooth ni iPhone) no puede probar nada de esto.

**Veredicto:** detectar y mostrar llamada = funcional (ya existe). Contestar/rechazar por ANCS = ruta pública plausible, **sin implementar hasta probarla con iPhone real**. Marcar y audio PC = **no viable** sin aprobación de la capacidad restringida; alternativa: delegar en Phone Link.

## 3. Android
| Capacidad | Sin app en el teléfono | Evidencia |
|---|---|---|
| Detectar/emparejar/identificar (Bluetooth clásico) | Sí, API pública (capacidad `bluetooth` ya declarada) | FUENTE; PENDIENTE-HW |
| Estado de conexión | Sí | FUENTE |
| Servicios SDP (HFP 0x111F, PBAP 0x112F, MAP 0x1132) | Enumerables | FUENTE; PENDIENTE-HW |
| Batería | Solo si expone GATT 0x180F (BLE); batería HFP clásica sin API documentada de Windows | FUENTE |
| Notificaciones / aviso de llamada | **No** (Android no tiene ANCS) | FUENTE |
| Llamadas (contestar/marcar/audio) | Misma barrera HFP/capacidad restringida que iPhone | FUENTE/INFERIDO |
| Llamadas vía app compañera | Phone Link lo logra con app «Link to Windows» (Android 10+) | FUENTE |

Sin app, Android se limita a conexión + identidad (+ batería si BLE). Detectar llamadas/notificaciones **exige app compañera** (o un `NotificationListenerService`/telefonía en el teléfono, con transporte propio). No se inicia desarrollo: propuesta futura = app mínima de código abierto, sin servicios externos, que reenvíe estado de llamada por BLE/RFCOMM propio; requiere justificación y revisión de políticas de Play antes de empezar.

## 4. Arquitectura compartida (mínima)
- `PhoneCapabilities` (nuevo, `WinMac.Connect.Core`): capacidades **derivadas de servicios realmente anunciados** (HFP AG, PBAP, MAP, A2DP, Battery, ANCS), con `Unknown/Advertised/Absent`; nunca de «es iPhone/Android». «Anunciado» ≠ «utilizable».
- `CallTracker` y la píldora de Hatsu Notch se reutilizan: un futuro origen Android solo tendría que producir los mismos eventos de llamada.
- No se añaden abstracciones adicionales hasta que exista un segundo origen real.

## 5. Pruebas manuales pendientes (Windows + hardware)
1. iPhone: llamada entrante muestra nombre/número en Notch; rechazar por ANCS (con consentimiento) y confirmar efecto.
2. Android: emparejar, ver estado, enumerar SDP, comprobar GATT 0x180F; comprobar que no se promete nada más.
3. Paquete Store-firmado: `PhoneLineTransportDevice` sigue `DeniedBySystem` (confirmar), y coexistencia con Phone Link.
4. Launchpad+Dock: ver docs de la rama (Win con 1 y 2 monitores, autohide on/off, Dock desactivado, matar Bar con Launchpad abierto, app a pantalla completa).

## 6. Siguiente decisión (del propietario)
Solicitar o no la capacidad `phoneLineTransportManagement` a Microsoft (único camino a marcar/audio PC oficial); y si se justifica una app Android compañera.
