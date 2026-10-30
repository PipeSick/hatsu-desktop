# WinMac — Fase 1: incertidumbres resueltas y spikes

Estado: **v2 — PARA REVISIÓN. Todo compila y la lógica compartida tiene pruebas unitarias; NINGÚN prototipo se ha ejecutado todavía en Windows.**
Fecha: 2026-10-30. Rama: `claude/vibrant-keller-7tyi6u`.

> **Límite honesto de esta entrega.** Esta sesión corre en Linux. Pude instalar el SDK de .NET 10 y
> **compilar** todo apuntando a `net10.0-windows` (compilación cruzada), lo que valida sintaxis y firmas de API,
> pero **no** ejecutar los prototipos. Por eso **no hay todavía números de FPS, CPU ni memoria ni capturas reales**:
> los produce `tools\Run-Phase1Tests.ps1` en tus equipos. **No se declara tecnología ganadora.**
> Cuatro categorías se mantienen separadas en todo el informe: **Objetivo** (umbral deseado) · **Medición real**
> (la que produzca el script en tu equipo; hoy: ninguna) · **Hipótesis** (a probar) · **Limitación** (conocida).

Convención: **[V]** verificado hoy en fuente (enlace al final) · **[C]** conocimiento previo no re-verificado ·
**[H]** hipótesis a probar · **[B]** verificado por compilación (no por ejecución).

---

## 1. Incertidumbres de la Fase 0 — qué se resolvió

| # | Incertidumbre | Resultado |
|---|---|---|
| U1 | ¿Versión de Windows App SDK? | Estable actual **2.6.4 (27-oct-2026)**; hay un canal experimental 2.5.4 [V]. Compatible hacia atrás hasta Win10 1809 y solo con versiones de Windows en soporte [V]. Los ejemplos oficiales hallados usan net8 [V], pero **`net10.0-windows10.0.19041.0` + WinAppSDK 2.6.4 restaura y compila** (hasta ensamblado, en Linux) [B]; ver §3. |
| U2 | ¿.NET 10 sirve para el resto? | Todos los proyectos (Win32, WPF, UIA, WinUI 3, DirectComposition) **compilan con SDK 10.0.114 y TFM `net10.0-windows`** [B]. |
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

## 2. Qué se entrega en la v2

| Entregable | Dónde | Estado |
|---|---|---|
| Paquete de pruebas (un solo script PowerShell) | `tools/Run-Phase1Tests.ps1` + `tools/thresholds.json` | Sintaxis validada con PowerShell 7.6 en Linux; lógica de resumen probada con datos **sintéticos** (no reales); **no ejecutado en Windows** |
| Instrucciones sencillas | `docs/INSTRUCCIONES-PRUEBAS.md` | Listo |
| Prototipo mínimo WPF | `spikes/S2.DockWpf` (variantes `layered`, `dwm`) | Compila [B] |
| Prototipo mínimo WinUI 3 | `spikes/S3.DockWinUI` (variantes `acrylic`, `plain`) | Compila hasta el ensamblado [B]; ver §3 |
| Prototipo mínimo Win32 + DirectComposition/D3D11/D2D | `spikes/S4.DockDComp` (variante `glass`) | Compila [B] |
| Escena, geometría y guion de medición idénticos para los 3 | `spikes/Common` + 12 pruebas unitarias | **12/12 pasan** (ejecutadas en Linux) |
| Evaluación de fidelidad y vista de geometría | `docs/02-fidelidad.md`, `docs/img/*.svg` | Listo (la vista es la *intención*, no una captura) |
| Criterios de aceptación definitivos y regla de decisión | `docs/03-criterios-aceptacion.md` | Para tu firma |
| Spikes de Fase 1 previos (S1, S5, S6, S7) | `spikes/` | Compilan; **S5 jamás lo ejecuta el script** |

Los tres prototipos dibujan **la misma escena** (14 ítems: 12 apps con 5 abiertas, separador, carpeta, papelera),
con la misma magnificación, el mismo barrido de cursor, el mismo guion de ocultar/mostrar y los mismos íconos
generados por código (squircle + degradado + letra; **sin recursos de Apple**).

Guion de medición idéntico: calentamiento 2 s → **reposo 10 s sin bucle de render** → **barrido 15 s** → **ocultar/mostrar ×3**.
Métricas por prototipo: frame-time p50/p95/p99/máx y % de frames perdidos (barrido y ocultar), CPU en reposo y en
animación, working set, arranque hasta primer render, costo de CPU de aplicar cada frame (proxy de latencia).

---

## 3. Validación de compilación de WinUI 3 (pedida en el punto 7.3)

| Comprobación | Resultado | Evidencia |
|---|---|---|
| Restaurar `Microsoft.WindowsAppSDK 2.6.4` + `Microsoft.Windows.SDK.BuildTools` para `net10.0-windows10.0.19041.0`, win-x64, **sin empaquetar** (`WindowsPackageType=None`) | **OK** [B] | build en Linux, 0 errores |
| Compilar C# (App, Window, `DesktopAcrylicBackdrop`, `AppWindow`, `CompositionTarget.Rendering`, `Path`/`PolyLineSegment`) contra las proyecciones de WinUI | **OK** [B] | 0 errores, 0 advertencias (`TreatWarningsAsErrors`) |
| Compatibilidad `.NET 10` + WinAppSDK 2.6.4 + TFM `net10.0-windows10.0.19041.0` | **OK a nivel de compilación** [B] (antes: no confirmado) | resuelve y compila |
| Generación de PRI (`makepri.exe`) y fusión de manifiesto (`mt.exe`) para autocontenido | **No verificable en Linux**: son ejecutables de Windows. El proyecto los desactiva solo fuera de Windows (`AppxGeneratePriEnabled`, `WindowsAppSDKSelfContained` condicionados al SO) | errores `Exec format error` observados antes de condicionar |
| Compilador XAML | **No ejercitado**: el prototipo se escribió **sin archivos .xaml** (todo en código). Un dock real con XAML requiere compilar en Windows/CI | — |
| Ejecución | **No hecha** | — |

Limitación: «compila» ≠ «funciona». Lo que sigue siendo **hipótesis**: que una ventana WinUI 3 sin backdrop no admite fondo
transparente (variante `plain` existe precisamente para comprobarlo visualmente), y que el modo autocontenido arranca
y funciona en 24H2.

---

## 4. Comparación técnica de los tres enfoques

Estado de evidencia: **documental/conceptual [C/H] + compilación [B]. Cero mediciones.** Se resumen diferencias estructurales; los números salen del script.

| Aspecto | WPF | WinUI 3 (WinAppSDK) | Win32 + DirectComposition/D3D11/D2D |
|---|---|---|---|
| **Estado de compilación hoy** | OK [B] | OK hasta ensamblado [B] | OK [B] (depende de Vortice.* 3.8.3) |
| **Líneas de código del prototipo** (indicativo de costo) | 160 líneas | 171 líneas | 252 líneas (incluye ~35 de P/Invoke) |
| **Dependencias externas** | ninguna (parte del SDK de .NET) | Windows App SDK 2.6.4 (NuGet; autocontenido añade el runtime al paquete) | Vortice.* (bindings DirectX, MIT [C]) |
| **Transparencia con forma propia** | `AllowsTransparency=true` (ventana en capas) [C] | **No hay ventana transparente nativa** [C] (hipótesis a comprobar con `plain`) | Sí: swap chain de composición con alfa premultiplicado [C] |
| **Material del sistema (acrílico DWM)** | Solo con `AllowsTransparency=false`: cubre **toda** la ventana [C] | `DesktopAcrylicBackdrop`: cubre **toda** la ventana [V: API documentada] | No se usa en el prototipo; blur real exigiría captura del fondo + shader (S4b) [H] |
| **Esquinas/radio propio** | Con ventana en capas, sí; con acrílico DWM, solo esquinas del sistema [V/C] | Esquinas del sistema, o región (`SetWindowRgn`) sin antialias [H] | Total |
| **Sombra de la barra** | `DropShadowEffect` (costo a medir; en ventana en capas puede ser por CPU) [H] | No implementada en el prototipo | Aproximada por capas en D2D (no gaussiana) |
| **Animación de magnificación** | Layout WPF por frame, hilo UI | Layout XAML por frame, hilo UI (con Composition disponible para animaciones declarativas, no usada aquí) | Redibujo D2D por frame; control total del pacing (vsync) |
| **Ocultar/mostrar** | Mueve la ventana | Mueve la ventana | Anima offset del Visual (no mueve la ventana) |
| **Accesibilidad (UIA)** | Proveedores automáticos para controles [C] | Proveedores automáticos para controles [C] | **Hay que implementarla a mano** [C] |
| **Mantenimiento / contribuciones** | Madura, muchos desarrolladores; código en maduración lenta [C] | Activa (2.6.4, 27-oct-2026) [V]; ecosistema menor [C] | Más código propio y conocimiento DirectX; barrera de entrada mayor [C] |
| **Techo de fidelidad visual (documentado)** | F1–F3 | F1–F2 (según hipótesis) | **F1–F4** (única vía a Liquid Glass aproximado sin APIs no documentadas) [H] |
| **Idoneidad natural en WinMac** | Plan B / UI de utilidades | Settings, Finder, Launchpad (controles, accesibilidad) | Dock, barra superior, Mission Control (superficies de alta fidelidad) |

Lectura honesta: la tabla describe **qué cabe esperar y qué hay que comprobar**, no un veredicto.
**No se declara ganadora ninguna tecnología.** Las hipótesis más decisivas (H1 WPF layered+shadow rinde a 60 FPS en 150 % DPI; H2 WinUI `plain`
es opaco; H3 D3D11/DComp mantiene CPU ≤ 8 % con 5+ efectos) se aceptan o refutan con los JSON del script.

---

## 5. Prototipos visuales mínimos

Qué dibuja cada prototipo (resumen; lo que **no** hacen está en el JSON de cada ejecución en `FeaturesMissing`):

| Atributo | S2 WPF `layered` | S2 WPF `dwm` | S3 WinUI `acrylic` | S3 WinUI `plain` | S4 DComp `glass` |
|---|---|---|---|---|---|
| Geometría compartida / squircle | ✔ | ✔ | ✔ | ✔ | ✔ |
| Barra con forma propia | ✔ | ✘ (acrílico cubre toda la ventana) | ✘ (ídem) | ? (depende de la transparencia) | ✔ |
| Material translúcido | simulado (degradado) | **acrílico real** | **acrílico real** | tinte plano | simulado (degradado) |
| Blur real del fondo con forma de barra | ✘ | ✘ | ✘ | ✘ | ✘ (S4b) |
| Borde especular | ✔ | ✔ | ✔ (uniforme) | ✔ (uniforme) | ✔ |
| Sombra | ✔ (DropShadow) | ✘ | ✘ | ✘ | aprox. por capas |
| Indicadores de app abierta / separador | ✔ | ✔ | ✔ | ✔ | ✔ |
| Magnificación | ✔ | ✔ | ✔ | ✔ | ✔ |
| Ocultar/mostrar animado | ✔ | ✔ | ✔ | ✔ | ✔ |

Capturas reales: las genera el script (`screenshots/<prototipo>-<variante>-{rest,hover,hidden}.png`). Vista de la **geometría
objetivo** (no de ningún prototipo): `docs/img/geometria-{reposo,hover,oculto}.svg`.

---

## 6. Criterios de aceptación definitivos

Ver `docs/03-criterios-aceptacion.md` (umbrales de rendimiento, criterios de fidelidad con rúbrica, puertas de decisión,
peso de cada factor y regla de desempate). Los umbrales de rendimiento son **objetivos**; ningún resultado los ha verificado.

---

## 7. Lo comprobado y lo pendiente

**Comprobado (con evidencia de esta sesión)**
- [B] Los 9 proyectos .NET de `spikes/` compilan en .NET 10.0.114, incluido WinUI 3 con WinAppSDK 2.6.4 (hasta ensamblado) y Vortice 3.8.3.
- [B] 12 pruebas unitarias de la lógica compartida pasan (geometría sin solapes, ventana suficiente para magnificación máxima, separador, barrido, squircle, guion de medición, percentiles).
- [B] `Run-Phase1Tests.ps1` supera el análisis sintáctico de PowerShell 7.6; su generación de resumen produjo resultados correctos con datos sintéticos.
- [V] Versión estable de Windows App SDK (2.6.4), licencias de Seelen UI (AGPL), MIT de Flow Launcher/PowerToys/MScholtes/MaximizeToVirtualDesktop/Files, vía de firma gratuita SignPath (condiciones), requisitos del listener de notificaciones (ver §1).

**Pendiente (requiere tu Windows)**
- Ejecutar el script (≥ 3 repeticiones, 100 % y ≥ 150 % DPI, con y sin 2.º monitor) → mediciones reales.
- Comprobar visualmente `plain` (¿transparencia en WinUI?), `dwm`/`acrylic` (acrílico a todo el rectángulo) y la fidelidad con tus capturas de referencia de Tahoe.
- Compilar S3 en Windows (PRI/manifiesto/autocontenido) y ejecutarlo.
- PresentMon para frame pacing real (S2/S3/S4).
- Verificar licencias pendientes: Everything SDK, FlaUI, Velopack, Vortice (MIT según mi memoria), PresentMon.
- S5 (barra de tareas) en VM con snapshot; S6 con 10+ apps; S7 (build/25H2 escritorios virtuales).
- Decidir el ADR-001 **solo** tras lo anterior. **No se avanza a la Fase 2.**

---

## Fuentes (consultadas)
- Canales y última versión estable de Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels · https://learn.microsoft.com/en-us/windows/apps/whats-new/whats-new-for-developers · https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview
- Backdrops: https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops
- DWM attributes: https://learn.microsoft.com/en-us/windows/desktop/api/dwmapi/ne-dwmapi-dwmwindowattribute
- MScholtes/VirtualDesktop (MIT): https://github.com/MScholtes/VirtualDesktop · MaximizeToVirtualDesktop (MIT): https://github.com/xuejiaW/MaximizeToVirtualDesktop
- Seelen UI (AGPL-3.0, vía reseña): https://github.com/eythaann/Seelen-UI · https://hysenlabs.com/projects/eythaann-seelen-ui
- Flow Launcher (MIT): https://flow-launcher.com/ · PowerToys Run: https://learn.microsoft.com/en-us/windows/powertoys/run
- SignPath Foundation: https://about.signpath.io/product/open-source · https://signpath.org/
- Notification listener: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener
- Paquetes NuGet consultados: Microsoft.WindowsAppSDK 2.6.4, Vortice.* 3.8.3 (api.nuget.org)
