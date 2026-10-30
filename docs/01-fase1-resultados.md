# WinMac — Fase 1: incertidumbres resueltas y spikes

Estado: **ENTREGA PARA REVISIÓN. Los spikes compilan pero NO se han ejecutado.**
Fecha: 2026-10-30. Rama: `claude/vibrant-keller-7tyi6u`.

> **Límite honesto de esta entrega.** Esta sesión corre en Linux. Pude instalar el SDK de .NET 10 y
> **compilar** los spikes apuntando a `net10.0-windows` (compilación cruzada), lo que valida sintaxis y
> firmas de API, pero **no** ejecutarlos. Por eso **no hay todavía números de FPS, CPU ni memoria**; esos
> los producen los spikes en tus equipos (§3). La recomendación de §5 es por tanto **provisional**.

Convención: **[V]** verificado hoy en fuente (enlace al final) · **[C]** conocimiento previo no re-verificado ·
**[H]** hipótesis a probar · **[B]** verificado por compilación (no por ejecución).

---

## 1. Incertidumbres de la Fase 0 — qué se resolvió

| # | Incertidumbre | Resultado |
|---|---|---|
| U1 | ¿Versión de Windows App SDK? | Estable actual **2.6.4 (27-oct-2026)**; hay un canal experimental 2.5.4 [V]. Compatible hacia atrás hasta Win10 1809 y solo con versiones de Windows en soporte [V]. **Aún no confirmado** que su plantilla soporte `net10.0-windows10.0.19041.0`; los ejemplos oficiales hallados usan net8 [V]. → verificar al crear el proyecto WinUI (spike S3). |
| U2 | ¿.NET 10 sirve para el resto? | Los 6 proyectos (Win32, WPF, UIA) **compilan con SDK 10.0.114 y TFM `net10.0-windows`** [B]. |
| U3 | Licencia de Seelen UI | **AGPL-3.0** [V, fuente secundaria]. Copiar su código a un proyecto Apache-2.0 **no es compatible** → solo referencia de ideas/UX, **no se reutiliza código**. |
| U4 | Licencias de otros candidatos | Flow Launcher **MIT** [V]; PowerToys **MIT** [V]; Files **MIT** [V, Fase 0]; MScholtes/VirtualDesktop **MIT** [V]; MaximizeToVirtualDesktop **MIT** [V]. Everything SDK (voidtools): licencia propia, **no verificada** → no depender de él hasta leerla. FlaUI y Velopack: MIT según mi memoria, **sin verificar** [C]. |
| U5 | Escritorios virtuales en 24H2/25H2 | MScholtes mantiene `VirtualDesktop11-24H2.cs` (última versión citada: 1.21, ago-2025) [V]. **No** hay confirmación explícita de 25H2. MaximizeToVirtualDesktop declara soporte "21H2 a 24H2+", con **dos layouts de vtable** (pre-24H2 y 24H2+), prueba de humo al arrancar y modo degradado si falla [V]. Como nuestro mínimo es 24H2, solo necesitamos el layout 24H2+. Confirmación de 25H2: la mide S7 en tu equipo si lo tienes (§3). |
| U6 | "Botón verde" macOS | Precedente open source existente: MaximizeToVirtualDesktop mueve la ventana a un escritorio nuevo y la maximiza; restaura al cerrar; limpia escritorios huérfanos tras un crash [V]. Es **viable por COM no documentado**, con la fragilidad ya descrita. Patrón de diseño aprovechable: *rollback en cada paso + limpieza de huérfanos + modo degradado*. |
| U7 | Firma gratuita | **SignPath Foundation**: certificado gratuito para proyectos open source con licencia OSI y repo público; requiere build totalmente automatizado y mención del servicio en la documentación [V]. Apache-2.0 califica. Un proyecto afirma que elimina el aviso de SmartScreen, pero es **afirmación propia, no prueba independiente**: no lo prometemos. La elegibilidad real se decide en su formulario. |
| U8 | Distribución sin certificado | Opciones sin costo: ZIP portable + checksums SHA-256 + *attestations* de GitHub [C]; winget [C]; Scoop [C]. Los avisos de SmartScreen seguirán apareciendo sin firma [V, Fase 0]. Documentar en el README cómo verificar el hash. |
| U9 | Notificaciones de otras apps | `UserNotificationListener` requiere capacidad en el manifiesto y permiso del usuario [V]. Si funciona en apps **sin empaquetar** es **no confirmado** (hay un caso anecdótico que solo funcionó empaquetado) [V parcial]. → Spike futuro; si falla, el centro de notificaciones propio se limita a mostrar el de Windows. |
| U10 | Mínimo Windows 11 24H2 | Elimina la necesidad de soportar layouts COM antiguos. Mantiene `DWMWA_WINDOW_CORNER_PREFERENCE` (22000+) y backdrops (22621+) cubiertos [V/C]. |

Reglas derivadas para el repositorio (a revisar antes de aplicarlas):
- `THIRD_PARTY.md` con licencia y versión por dependencia; **prohibido** AGPL/GPL en el árbol de código.
- No se añade el texto de `LICENSE` (Apache-2.0) hasta tu revisión: lo tomaré del texto oficial, no de memoria.

---

## 2. Qué se construyó (en `spikes/`)

Solución `spikes/WinMac.Spikes.slnx`, .NET 10, DPI PerMonitorV2, `TreatWarningsAsErrors`, sin dependencias NuGet. [B]

| Spike | Pregunta que responde | Qué mide |
|---|---|---|
| **S7.PlatformProbe** | ¿Qué hay realmente en este equipo? | Build exacta (con UBR), monitores/DPI, API **documentada** de escritorios virtuales, `SHQueryUserNotificationState`, soporte DWM de esquinas/backdrop |
| **S1.WindowTracker** | ¿`SetWinEventHook` basta para seguir ventanas sin polling? | eventos/s, costo del handler p50/p95 en µs, CPU, y costo de un escaneo completo `EnumWindows` (la alternativa de polling), exactitud de la heurística "ventana de app" |
| **S5.TaskbarGuard** | ¿Se puede ocultar la barra y garantizar su restauración aunque el proceso muera? | Éxito/fallo de `ABM_SETSTATE` en Win11; tiempo de restauración tras `taskkill /F` (centinela independiente) |
| **S6.UiaMenus** | ¿Cuánto del menú de apps ajenas es legible? | Por app: ¿se halló MenuBar?, ítems de primer nivel, submenús del primero, ms |
| **S2.DockWpf** | Línea base de dock: cadencia, CPU, memoria, fidelidad | Frame-time p50/p95/p99/máx, % frames perdidos, CPU reposo y animación, memoria, arranque. Variantes `layered` (forma propia, vidrio simulado) y `dwm` (acrílico real del sistema, esquinas del sistema) |

`Common` aporta el modelo de magnificación y el barrido de cursor **deterministas**, para que cualquier
tecnología futura (WinUI, Win32+DirectComposition) se compare con exactamente la misma carga.

### Deliberadamente NO entregado
- **S3 (dock WinUI 3)**: el compilador XAML de WinUI solo funciona en Windows; no voy a entregar código que
  no pude compilar. Se crea en cuanto confirmes que puedes compilar en Windows, o en CI (`windows-latest`).
- **S4 (Win32 + DirectComposition/D3D11, vidrio por captura)**: ver §5; lo condiciona el resultado de S2.
- No se escribió nada del Dock real.

---

## 3. Cómo ejecutarlos (equipo físico Win11 24H2+, de preferencia con 2 monitores)

```powershell
cd spikes
dotnet build WinMac.Spikes.slnx -c Release
dotnet run -c Release --project S7.PlatformProbe
dotnet run -c Release --project S1.WindowTracker -- 60
dotnet run -c Release --project S6.UiaMenus -- 5        # repetir con 10+ apps
dotnet run -c Release --project S2.DockWpf -- --variant layered
dotnet run -c Release --project S2.DockWpf -- --variant dwm
dotnet run -c Release --project S2.DockWpf -- --variant dwm --interactive   # para juzgar visualmente
```
S5 es el único que toca el sistema (solo el autohide de la barra, reversible). **Léelo antes** y prueba
primero `status`; si algo queda mal, `restore` o activar manualmente en Configuración → Personalización → Barra de tareas.
```powershell
dotnet run -c Release --project S5.TaskbarGuard -- status
dotnet run -c Release --project S5.TaskbarGuard -- guarded-hide   # luego, desde otra consola: taskkill /F /IM S5.TaskbarGuard.exe
```
Los resultados quedan en `bench-results/*.json` (ignorados por git). Envíamelos o pega la salida.
Repetir S2 en: monitor a 100 %, 150 % y 200 % de escala, y con un monitor ≥120 Hz si lo hay.
Ejecuta S2 también en la VM **solo como dato adicional**: la VM suele usar render por software y no es
representativa de rendimiento.

### Criterios de aceptación propuestos (para tu aprobación)
| Métrica | Umbral | Aplica a |
|---|---|---|
| Frames > 1,5× presupuesto (16,7 ms a 60 Hz) | ≤ 1 % | S2 |
| Frame-time p99 | ≤ 2× presupuesto | S2 |
| CPU reposo | ≤ 0,5 % | S2 |
| CPU en animación continua | ≤ 8 % (un núcleo lógico normalizado) | S2 |
| Working set | ≤ 150 MB | S2 |
| Handler de eventos p95 | ≤ 200 µs | S1 |
| Restauración de la barra tras muerte forzada | ≤ 3 s, 10/10 intentos | S5 |
| Menús legibles en apps nativas (Win32/WPF) | ≥ 80 % de las probadas | S6 |

Son umbrales **propuestos**, no medidos; ajustaremos con los primeros datos reales.
Limitación declarada de S2: `CompositionTarget.Rendering` mide cadencia de render de WPF, no la presentación
real al monitor. Para el dato definitivo usar **PresentMon** [C, licencia a verificar].

---

## 4. Lo que la documentación ya dice sobre fidelidad visual (sin medir)

Esto es lo que más importa para tu criterio de fidelidad, y viene de fuentes, no de pruebas:

1. Backdrops del sistema: Mica es opaco y pensado para la capa base; **Acrílico se recomienda solo para
   superficies transitorias** (flyouts, menús) [V]. Un dock siempre visible es un caso límite de esa guía.
2. `DWMWA_WINDOW_CORNER_PREFERENCE` solo ofrece esquinas **del sistema** (no un radio arbitrario) [V/C].
   Un dock con radio propio exige otra técnica.
3. Existen vías no documentadas (p. ej. `SetWindowCompositionAttribute`) muy usadas para blur con forma propia
   [C]. **No las adoptamos** (regla: no depender de lo no documentado cuando hay alternativa; las
   interfaces de escritorios virtuales son la excepción justificada, y aisladas).
4. "Liquid Glass" real (refracción, brillo especular, lensing) **no existe como API nativa de Windows** [C].
   Reproducirlo implica un shader propio sobre una captura del fondo (Windows.Graphics.Capture + D3D11) [H].
   Es costoso en GPU y debe medirse (S4).

Escalera de fidelidad propuesta (cada peldaño se valida con spike antes de subir):

| Nivel | Técnica | Fidelidad | Riesgo/costo |
|---|---|---|---|
| F1 | Ventana propia + acrílico DWM + esquinas del sistema | Media-baja | Mínimo; forma fija |
| F2 | F1 + `SetWindowRgn` con radio propio | Media | Bordes sin antialias [H] |
| F3 | Ventana en capas + vidrio simulado por degradados/sombras (variante `layered` de S2) | Media | Sin blur real del fondo |
| F4 | Captura del fondo + shader D3D11 + DirectComposition (blur, saturación, borde especular, distorsión) | **Alta (Liquid Glass aproximado)** | GPU, batería, capturas protegidas (DRM/juegos) |

---

## 5. Recomendación técnica (PROVISIONAL, antes de medir)

Con evidencia medida **cero** sobre rendimiento, solo puedo recomendar el *camino de validación*:

1. **Ejecutar S7, S1, S5, S6, S2 ya.** Son baratos y desbloquean decisiones concretas (24H2/25H2, política de
   ventanas, seguridad de la barra, viabilidad de menús).
2. **Hipótesis de trabajo para el Dock y la Barra superior:** HWND propio en C# (Win32) con superficie
   DirectComposition + renderizado D3D11, porque es la única vía que puede llegar a F4 (tu criterio
   principal es fidelidad visual) sin depender de APIs no documentadas. WPF (`S2`) queda como **línea base y
   plan B** (F1–F3). WinUI 3 (S3) se reserva para ventanas de aplicación (Settings, Finder, Launchpad), donde sus
   controles aportan y la forma/transparencia personalizada pesa menos.
3. **Hipótesis refutables:** si S2-`layered` ya cumple los umbrales con fidelidad aceptable, WPF podría bastar
   para el dock en la v1 y F4 pasaría a una fase posterior. Si S2 incumple p99 o CPU, la hipótesis D3D/DComp gana peso.
4. **Siguiente iteración tras tus datos:** S3 (WinUI) y S4 (DComp+D3D11 con vidrio por captura, mismo `Common`)
   para decidir con números, y recién ahí el ADR-001.

### No adelantado
No avanzo a la Fase 2. No se hizo `push` de esta fase: queda en commit local para tu revisión
(tu autorización cubría solo el commit anterior de documentación).

---

## Fuentes (consultadas hoy)
- Canales y última versión estable de Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels · https://learn.microsoft.com/en-us/windows/apps/whats-new/whats-new-for-developers · https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview
- Backdrops: https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
- DWM attributes: https://learn.microsoft.com/en-us/windows/desktop/api/dwmapi/ne-dwmapi-dwmwindowattribute
- MScholtes/VirtualDesktop (MIT; `VirtualDesktop11-24H2.cs`): https://github.com/MScholtes/VirtualDesktop
- MaximizeToVirtualDesktop (MIT): https://github.com/xuejiaW/MaximizeToVirtualDesktop
- Seelen UI (AGPL-3.0, vía reseña): https://github.com/eythaann/Seelen-UI · https://hysenlabs.com/projects/eythaann-seelen-ui
- Flow Launcher (MIT): https://flow-launcher.com/ · PowerToys Run: https://learn.microsoft.com/en-us/windows/powertoys/run
- SignPath Foundation: https://about.signpath.io/product/open-source · https://signpath.org/
- Notification listener: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener
