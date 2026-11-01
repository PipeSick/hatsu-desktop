# Propuesta técnica: Centro de notificaciones propio (solo propuesta; no implementado)

Alcance: separada del resto. **No hay centro de notificaciones todavía**: el mosaico «Notificaciones» del Control Center solo abre el centro nativo de Windows (Win+N).

**Aviso de verificación:** desde la sesión de trabajo no se pudo consultar `learn.microsoft.com` (red restringida), y las búsquedas web no confirmaron los requisitos de la API de lectura. Lo marcado **[NO VERIFICADO]** es conocimiento previo que hay que comprobar con una sonda en un equipo real; lo marcado **[VERIFICADO]** salió de una fuente consultada.

## 1. Qué se puede obtener sin técnicas invasivas
| Fuente | Qué da | Requisitos y límites |
|---|---|---|
| **Notificaciones propias de WinMac** (avisos de módulos caídos, fin de la prueba de velocidad, unidad expulsada, actualización disponible…) | todo: las genera WinMac | **[VERIFICADO]** las notificaciones locales del Windows App SDK funcionan en apps con y sin paquete y no exigen identidad de paquete (las **push** sí); lo único especial en apps sin paquete es la activación. Aun así, para lo propio basta un bus interno de eventos: no hace falta tocar Windows |
| **`UserNotificationListener`** (WinRT, `Windows.UI.Notifications.Management`) | leer las notificaciones tipo *toast* de OTRAS apps (aplicación de origen, título y cuerpo del texto, hora, id), descartarlas y recibir el aviso de cambios | **[NO VERIFICADO]** exige el permiso del usuario (Configuración → Privacidad → Notificaciones) y, según recuerdo de la documentación, **identidad de paquete** (MSIX o paquete disperso). Para una app Win32 sin paquete puede fallar. **Esto se resuelve midiendo** (§3) |
| **Centro de acción nativo (Win+N)** | nada que leer; solo se abre | es lo que hace hoy el mosaico |

## 2. Lo que NO se hará (invasivo o frágil)
- **Leer `wpndatabase.db`** (la base SQLite de notificaciones de Windows): sin contrato ni esquema estable, **[VERIFICADO]** es lo que usan herramientas forenses y lectores de terceros, pero es un archivo del sistema; leerlo de otro modo que la API es invasivo y puede dar contenido sin el consentimiento que Windows pide.
- **Automatización de interfaz o enganches** sobre las ventanas de los *toast*: frágil, depende de la versión y roza el espionaje.
- **Interceptar o suprimir** notificaciones antes de mostrarse: Windows no lo permite a una app normal; no existe «No molestar/Foco» controlable (ya anotado en F5).

## 3. Primer paso: medir, no suponer
`WinMac.Bar.App --selftest-system` ya incluye una línea («Notificaciones de otras apps…») que solo consulta `UserNotificationListener.Current.GetAccessStatus()`: **no pide permiso ni lee ninguna notificación**. Su resultado en tu PC decide el camino:
- *estado de acceso = …* → la API es utilizable desde una app sin paquete: se puede seguir (con consentimiento explícito).
- *no disponible para esta app (excepción)* → hace falta identidad de paquete.

## 4. Diseño propuesto
1. **`INotificationSource`** (núcleo puro): produce `WinMacNotification(Id, Origen, Título, Texto, Hora, Leída)`. Fuentes: **Interna** (siempre) y **Windows** (opcional, apagada por defecto).
2. **Separación clara de procedencia:** las del propio WinMac llevan `Origen = WinMac` y se muestran siempre; las de otras apps llevan el nombre real de la aplicación y solo existen si el usuario activó la fuente de Windows.
3. **Panel** con la misma arquitectura que el multifunción (un icono, popover, tarjeta, filas), historial local limitado (p. ej. 100) y botón «Borrar».
4. **Privacidad:** el contenido de otras apps nunca sale del equipo, no se escribe en registros ni en la configuración, y se puede desactivar la fuente en cualquier momento (se borra el historial). Sin consentimiento de Windows no se lee nada.
5. **Si exige paquete:** un **paquete disperso** (identidad con ubicación externa) permite seguir sin empaquetar la aplicación, pero implica firmar y registrar un paquete: es del terreno del instalador, que queda fuera por ahora. Alternativa sin paquete: mostrar solo las notificaciones propias y mantener el acceso al centro de Windows.

## 5. Fases
- **P0 (barata, sin riesgo):** centro con solo fuentes internas + bus de eventos de WinMac.
- **P1:** leer el resultado de la sonda de §3 en tu PC.
- **P2 (solo si P1 es favorable):** fuente de Windows con consentimiento, apagada por defecto, y su pantalla de permiso.
- **P3 (solo si hace falta identidad):** decidir sobre el paquete disperso junto con el instalador.
