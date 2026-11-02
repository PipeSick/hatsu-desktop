# 21 · Isla informativa: volumen, brillo, notificaciones y prioridades

Estado: implementado y probado automáticamente (lógica pura + recorrido real Barra → host → Isla en el CI de Windows). **Pendiente de validar en un escritorio físico** (ver §5).

## 1. Recorrido

```
Windows ──► Barra (WinMac.Bar.App) ──evento «notch.feed»──► host (WinMac.exe) ──► Isla (NotchWin.App)
            detecta                     (IPC local, token)                      PillArbiter decide · NotchWindow dibuja
```

La Barra ya tenía los servicios de audio, brillo y notificaciones, así que detecta allí y la Isla solo muestra. Nada nuevo en el IPC: un módulo emite un evento y el host lo reparte
a los módulos suscritos. Sin WinMac (NotchWin independiente) no llega nada y la Isla funciona como antes.

| Qué | Cómo se detecta (Barra) | Archivo |
|---|---|---|
| Volumen y silencio | `IAudioEndpointVolume::RegisterControlChangeNotify` (aviso de Core Audio, sin sondeo); sigue a la salida predeterminada con `IMMNotificationClient` | `WinMac.System.Win/AudioService.Watch.cs` |
| Brillo, panel interno | evento WMI `WmiMonitorBrightnessEvent` (root\wmi), hilo propio bloqueado en `NextEvent` | `WinMac.System.Win/BrightnessService.Watch.cs` |
| Brillo, monitores externos | **DDC/CI no avisa de cambios.** Solo se anuncia el valor que el usuario aplica desde el deslizador de la barra (cuando el monitor lo acepta). Los cambios con los botones del monitor no se ven | `PanelOperations.BrightnessApplied` |
| Notificaciones de Windows | Las mismas de la barra (`UserNotificationListener`). Windows no envía el evento de cambios a esta app (medido, docs/20), así que se relee **cada 3 s** y solo las nuevas (la primera lectura es la base; se ignoran las de hace más de 2 min; como mucho 3 de golpe) | `BarHost.Notch.cs`, `NewNotificationTracker` |

Privacidad y restricciones respetadas:
- Las notificaciones solo se leen si en la barra está activado «Mostrar notificaciones de Windows» y Windows dio permiso. Con «Notificaciones» apagado en la Isla, la barra **no** las lee ni las envía.
- No se envían en pantalla completa/presentación/«ocupado» (`SHQueryUserNotificationState` ≠ `QUNS_ACCEPTS_NOTIFICATIONS`). **Limitación:** el «No molestar» de Windows 11 no tiene API documentada; con él activo las notificaciones siguen apareciendo en la Isla (y en el Centro de Notificaciones de WinMac).
- El texto viaja solo por la tubería local con token; nunca a disco ni a registros.

## 2. Prioridades (`NotchWin.Core/Pill/PillArbiter.cs`, `PillPolicy`)

| Tipo | Prioridad | Duración | Comportamiento |
|---|---|---|---|
| Volumen | 40 | ajuste (2 s) | se actualiza en el sitio; reinicia su tiempo; nunca hace cola |
| Brillo | 40 | ajuste (2 s) | igual que el volumen |
| Notificación | 30 | ajuste (5 s) | cola por orden de llegada; si la interrumpe el volumen o el brillo, **vuelve** con el tiempo que le quedaba (mín. 1,5 s) |
| Eventos de NotchWin (agente, temporizador, descarga) | 20 | 3,2 s | cola; reanudable |
| Pista nueva | 10 | 3,2 s | solo la última |

Además: la misma notificación repetida en 10 s se muestra una vez; cola máxima de 6 (se descarta lo más antiguo del tipo menos importante); con la Isla abierta el árbitro se congela (las notificaciones esperan, los indicadores se descartan). Sin nada pendiente vuelve el contenido habitual (pista o píldora en reposo).

Todo el comportamiento está en `PillPolicy.FromSettings`; ninguna otra parte del código tiene duraciones propias.

## 3. Apariencia

Píldora cerrada con la altura de la barra: indicador de 280 DIP (símbolo de Segoe Fluent Icons, barra de nivel de 5 px y porcentaje; en silencio todo en gris y «Silencio»), notificación de 380 × 52 DIP (icono de la app de 28 px con esquinas redondeadas o su inicial, título en negrita y una línea de texto). Las transiciones usan la animación de tamaño que ya tenía la Isla; un cambio de nivel no cambia el tamaño (no hay parpadeo al mantener la tecla).

## 4. Ajustes (WinMac → Isla → «Notch cerrado»)

`pillVolume`, `pillBrightness`, `pillNotifications` (activados), `pillHudSeconds` (2, 1–10), `pillNotifySeconds` (5, 2–30), `pillAnimations` (activado). Se guardan en `winmac.json` (sección `notch`) y llegan a la Isla y a la barra por `config.notch`.

## 5. Validación

Automática:
- NotchWin.Core: 18 pruebas (`PillTests.cs`): cambios seguidos de volumen, silencio, brillo, brillo sin valor (no se inventa), notificaciones simultáneas, prioridades e interrupción con reanudación, vuelta al contenido habitual, repeticiones, inundación, ajustes en vivo, Isla abierta, reinicio.
- WinMac: `NotchFeedTests` (5: notificaciones nuevas, base, ráfagas, opciones, No molestar), `NotchPillSettingsTests` (2).
- CI de Windows (`Test-Notch.ps1`): con `WINMAC_PILL_SELFTEST` la barra REAL envía una secuencia de muestra por el canal real (el runner no tiene salida de audio, panel interno ni permiso de notificaciones) y la Isla REAL (WinUI 3) anota lo que muestra y con qué tamaño; se comprueban volumen, silencio, brillo, notificación con icono y crecimiento, interrupción y reanudación y la vuelta al estado habitual. Capturas `notch-pill-*.png` en el artefacto `integracion-notchwin`.

Pendiente en el PC (no se puede en CI):
- Teclas de volumen del teclado, control de volumen de Windows y otra app cambiando el volumen → indicador.
- Cambiar la salida predeterminada (auriculares) y volver a cambiar el volumen.
- Portátil: teclas de brillo. Sobremesa: deslizador de brillo de la barra con un monitor DDC/CI.
- Recibir una notificación real (p. ej. un correo) con «Mostrar notificaciones de Windows» activado.
- Aspecto real del indicador y de la notificación sobre fondos claros y oscuros.
