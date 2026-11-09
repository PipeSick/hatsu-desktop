using WinMac.Bar;

namespace WinMac.Host;

public enum RowKind { Toggle, Segmented, Field, Slider, Button, Info }

/// <summary>
/// Una fila de la ventana de Ajustes. No hay controles decorativos: lo que no se puede usar aparece atenuado con su <see cref="Reason"/>.
/// <see cref="Index"/> identifica la posición de una pestaña de la Isla (filas «notch.sec:i»).
/// </summary>
public sealed record SettingsRow(RowKind Kind, string Id, string Title, string? Subtitle = null, bool On = false, bool Enabled = true, string? Reason = null,
    IReadOnlyList<string>? Options = null, int Selected = 0, string? Text = null, string? Placeholder = null, double Value = 0, bool CanUp = false, bool CanDown = false,
    double SliderMin = 0, double SliderMax = 100, string? Unit = null)
{
    /// <summary>What the slider shows for a 0–100 position: the real value in its unit (WM-019: the Dock's fine size showed «29» for 48 points).</summary>
    public string SliderLabel(double v) => Math.Round(SliderMin + Math.Clamp(v, 0, 100) / 100 * (SliderMax - SliderMin)) + (Unit is null ? "" : " " + Unit);
}

public sealed record SettingsGroup(string? Header, IReadOnlyList<SettingsRow> Rows, string? Footer = null);
public sealed record SettingsPage(string Id, string Title, string Glyph, IReadOnlyList<SettingsGroup> Groups, string? Section = null);

/// <summary>Vista del diagnóstico de llamadas (Ajustes › Hatsu Connect): lo da el host; puro para probar la página sin Windows.</summary>
public sealed record ConnectDiagView(string Status = "Sin ejecutar", IReadOnlyList<string>? Summary = null, bool Running = false, bool HasReport = false, bool Observing = false, string ObserveStatus = "Detenida", string CallControlStatus = "sin información (el módulo Connect no ha informado)");

/// <param name="WeatherStatus">Texto de estado de la búsqueda de ciudad (qué se eligió, cómo pedir otra).</param>
public sealed record SettingsInput(HostConfig Cfg, IReadOnlyDictionary<string, ModuleState> States, bool Autostart, string WeatherStatus = "")
{
    /// <summary>Configuración del Dock (su propio archivo dock.json). Null: no se pudo leer; la página lo dice y no muestra controles falsos.</summary>
    public WinMac.Dock.DockConfig? Dock { get; init; }
    /// <summary>Hatsu Desktop: ruta de OTRA instalación (p. ej. el WinMac antiguo) que Windows inicia hoy con la sesión; null si no hay. Con valor, Ajustes ofrece cambiarla a esta (con confirmación).</summary>
    public string? AutostartForeignPath { get; init; }
    /// <summary>Permiso de ubicación de Windows para las apps de escritorio, leído sin pedirlo: «Permitido», «Denegado» o vacío si no se sabe.</summary>
    public string LocationPermission { get; init; } = "";
    /// <summary>Installed extensions with their live status (docs/30). Null: the page says extensions are not available.</summary>
    public IReadOnlyList<(Extensions.InstalledExtension Ext, string Status)>? Extensions { get; init; }
    /// <summary>WM-034: estado real de la ocultación de la barra de tareas («Oculta», «Suspendida: …»). Vacío si no se sabe.</summary>
    public string TaskbarStatus { get; init; } = "";
    /// <summary>Fase 3.1: causa conocida de cada módulo que no arrancó (vacío si todo va bien). Un módulo DESACTIVADO por el usuario no aparece aquí.</summary>
    public IReadOnlyDictionary<string, ModuleFailure> ModuleFailures { get; init; } = new Dictionary<string, ModuleFailure>();
    /// <summary>Diagnóstico de Bluetooth y llamadas (solo lectura). Null: sin servicio (p. ej. pruebas): la página muestra un estado vacío.</summary>
    public ConnectDiagView ConnectDiag { get; init; } = new();
}

/// <summary>Construye las páginas de Ajustes a partir de la configuración vigente. Puro: se prueba sin Windows.</summary>
public static partial class SettingsBuilder
{
    /// <summary>Opciones booleanas de la Barra: (campo del parche, texto, lectura). Un solo lugar; la ventana las pinta y aplica por tabla.</summary>
    public static readonly (string Field, string Text, Func<BarConfig, bool> Get)[] BarToggles =
    [
        ("showNetwork", "Wi-Fi / red", b => b.ShowNetwork), ("showVolume", "Volumen", b => b.ShowVolume), ("showBattery", "Batería", b => b.ShowBattery), ("showBatteryPercent", "Porcentaje de batería", b => b.ShowBatteryPercent),
        ("showBluetooth", "Bluetooth", b => b.ShowBluetooth), ("showControlCenter", "Control Center", b => b.ShowControlCenter), ("showSeconds", "Segundos en el reloj", b => b.ShowSeconds), ("showDate", "Fecha junto a la hora", b => b.ShowDate),
        ("use24Hour", "Reloj de 24 horas", b => b.Use24Hour), ("reserveSpace", "Reservar el borde superior", b => b.ReserveSpace), ("confirmDestructive", "Confirmar reiniciar, apagar y cerrar sesión", b => b.ConfirmDestructive),
        ("wifiDetails", "Detalle de Wi-Fi al tocar una red", b => b.WifiDetails), ("appMenus", "Menús de la app en la barra (avanzado)", b => b.AppMenus),
        ("ccWifi", "Wi-Fi", b => b.CcWifi), ("ccBluetooth", "Bluetooth", b => b.CcBluetooth), ("ccSound", "Sonido", b => b.CcSound), ("ccBrightness", "Brillo", b => b.CcBrightness),
        ("ccPower", "Energía", b => b.CcPower), ("ccAppearance", "Apariencia", b => b.CcAppearance), ("ccDisplay", "Pantallas", b => b.CcDisplay), ("ccNotifications", "Notificaciones", b => b.CcNotifications),
        ("showHub", "Iconos de Rendimiento, Red y Discos", b => b.ShowHub), ("showWeather", "Icono del Tiempo", b => b.ShowWeather), ("weatherFahrenheit", "Grados Fahrenheit", b => b.WeatherFahrenheit),
        ("weatherAutoLocation", "Usar mi ubicación", b => b.WeatherAutoLocation),
        ("notificationCenter", "El reloj abre el Centro de Notificaciones", b => b.NotificationCenter), ("notificationsFromWindows", "Mostrar notificaciones de Windows", b => b.NotificationsFromWindows),
        ("notifRemoveFromWindows", "Borrar también en Windows", b => b.NotifRemoveFromWindows),
        ("showTrayIcons", "Iconos de apps en segundo plano", b => b.ShowTrayIcons),
        ("launchpadOnWinKey", "La tecla Windows abre Launchpad", b => b.LaunchpadOnWinKey),
        ("spotlightFiles", "Buscar también archivos", b => b.SpotlightFiles),
        ("captureHotkeys", "Atajos Ctrl+Mayús+3 / 4 / 5", b => b.CaptureHotkeys),
        ("captureCopyToClipboard", "Copiar también al portapapeles", b => b.CaptureCopyToClipboard),
        ("captureQuickLook", "Miniatura flotante al guardar", b => b.CaptureQuickLook),
        ("soundMixer", "Mezclador en el menú Sonido", b => b.SoundMixer),
        ("showPhone", "Indicador del iPhone en la barra superior", b => b.ShowPhone),
        ("appOutputRouting", "Salida por aplicación (experimental)", b => b.AppOutputRouting),
        ("showPhonePercent", "Porcentaje junto al iPhone", b => b.ShowPhonePercent),
        ("showPowerWithoutBattery", "Icono de corriente en equipos sin batería", b => b.ShowPowerWithoutBattery),
        ("showTaskManager", "Botón del Administrador de tareas", b => b.ShowTaskManager),
        ("showPeripherals", "Icono de Periféricos en la barra", b => b.ShowPeripherals),
        ("peripheralAlerts", "Avisar cuando la batería de un periférico esté baja", b => b.PeripheralAlerts),
        ("peripheralAsusRog", "Leer la batería de periféricos ASUS ROG (receptor 2,4 GHz)", b => b.PeripheralAsusRog),
        ("peripheralLogitech", "Leer la batería de periféricos Logitech (receptor)", b => b.PeripheralLogitech),
        ("showCleanup", "Icono de Limpieza en la barra", b => b.ShowCleanup),
        ("showHubSystem", "Icono de Rendimiento (CPU, memoria, temperaturas)", b => b.ShowHubSystem),
        ("showHubNetwork", "Icono de Red (velocidad y conexiones)", b => b.ShowHubNetwork),
        ("showHubDisks", "Icono de Discos (espacio y estado)", b => b.ShowHubDisks),
        ("showVpn", "Icono de VPN en la barra (conexiones VPN de Windows)", b => b.ShowVpn),
        ("cleanupIsland", "Mostrar el progreso de la limpieza en Hatsu Notch", b => b.CleanupIsland),
        ("mcEnabled", "Mission Control", b => b.McEnabled),
        ("snapEnabled", "Snap", b => b.SnapEnabled),
        ("spotActionsEnabled", "Spotlight Actions", b => b.SpotActionsEnabled),
        ("liveTextEnabled", "Live Text", b => b.LiveTextEnabled),
        ("gesturesEnabled", "Gestures", b => b.GesturesEnabled),
        ("ltKeepLineBreaks", "Conservar los saltos de línea", b => b.LtKeepLineBreaks),
        ("ltAutoCopy", "Copiar el texto al reconocerlo", b => b.LtAutoCopy),
        ("ltAutoShelf", "Guardar el texto reconocido en la estantería", b => b.LtAutoShelf),
    ];
    public static readonly string[] CaptureDelayNames = ["No", "3 s", "5 s", "10 s"];
    public static readonly int[] CaptureDelayValues = [0, 3, 5, 10];
    public static readonly string[] CaptureFormatNames = ["PNG", "JPG"], CaptureFormatValues = ["Png", "Jpg"];
    public static readonly string[] SpotKeyNames = ["Alt+Espacio", "Ctrl+Espacio", "Desactivado"], SpotKeyValues = ["AltSpace", "CtrlSpace", "Off"];

    public static readonly Dictionary<WinMac.Bar.Notifications.NcWidget, string> NcWidgetNames = new()
    {
        [WinMac.Bar.Notifications.NcWidget.Calendar] = "Calendario", [WinMac.Bar.Notifications.NcWidget.Weather] = "Tiempo", [WinMac.Bar.Notifications.NcWidget.Battery] = "Batería",
    };

    /// <summary>Widgets en el orden de las filas: los activos en su orden y después los inactivos.</summary>
    public static IReadOnlyList<(WinMac.Bar.Notifications.NcWidget Kind, bool On)> NcWidgetRows(BarConfig bar)
    {
        var on = WinMac.Bar.Notifications.NcWidgets.Parse(bar.NcWidgets);
        return on.Select(k => (k, true)).Concat(Enum.GetValues<WinMac.Bar.Notifications.NcWidget>().Where(k => !on.Contains(k)).Select(k => (k, false))).ToList();
    }

    public static readonly string[] RateNames = ["Auto", "KB/s", "MB/s", "Mb/s"], RateValues = ["Auto", "KBps", "MBps", "Mbps"];
    public static readonly string[] BackgroundNames = ["Vidrio", "Transparente"], BackgroundValues = ["Glass", "Transparent"], ThemeNames = ["Sistema", "Claro", "Oscuro"], ThemeValues = ["System", "Light", "Dark"];
    public static readonly string[] PositionNames = ["Centro", "Izquierda", "Derecha"], PlayerNames = ["Spotify", "Feishin", "Otro"];
    public static readonly Dictionary<string, string> SectionNames = new()
    {
        ["music"] = "Música", ["agents"] = "Agentes de IA", ["timer"] = "Temporizador", ["calendar"] = "Calendario", ["downloads"] = "Descargas", ["notifications"] = "Notificaciones",
        ["shelf"] = "Estantería", ["camera"] = "Cámara (espejo)", ["system"] = "Recursos del sistema", ["clipboard"] = "Portapapeles",
        ["activity"] = "En curso", ["captures"] = "Capturas", ["history"] = "Historial",
    };

    private static List<SettingsRow> HotkeyRows(BarConfig bar)
    {
        var rows = new List<SettingsRow>
        {
            new(RowKind.Segmented, "bar.capmods", "Modificadores de las capturas", "Con 3 pantalla · 4 región · 5 barra de captura · 6 grabar", Options: WinMac.Bar.Launcher.HotkeyAudit.CaptureModifierNames,
                Selected: Math.Max(0, Array.IndexOf(WinMac.Bar.Launcher.HotkeyAudit.CaptureModifierIds, bar.CaptureModifiers)), Enabled: bar.CaptureHotkeys, Reason: bar.CaptureHotkeys ? null : "Atajos de capturas desactivados"),
        };
        var items = HotkeyStatus.Items;
        if (items.Count == 0) rows.Add(new(RowKind.Info, "bar.keys.none", "La Barra todavía no ha informado de sus atajos (se muestra al arrancar)."));
        for (int i = 0; i < items.Count; i++)
        {
            var k = items[i];
            string state = k.Problem is { } p ? (k.Registered ? "⚠ " : "✗ ") + p : "✓ activo";
            rows.Add(new(RowKind.Info, "bar.key:" + i, $"{k.Keys} — {k.Purpose} ({k.Module}): {state}"));
        }

        rows.Add(new(RowKind.Button, "bar.keysreset", "Restaurar los atajos predeterminados"));
        return rows;
    }

    private static int Index(string[] arr, string v) => Math.Max(0, Array.FindIndex(arr, x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)));
    private static SettingsRow Bar(int i, string? subtitle = null, string? title = null) => new(RowKind.Toggle, "bar:" + BarToggles[i].Field, title ?? BarToggles[i].Text, subtitle, BarToggles[i].Get(_bar!));
    private static SettingsRow BarF(string field, string? subtitle = null) => Bar(Array.FindIndex(BarToggles, t => t.Field == field), subtitle);   // por nombre: añadir opciones al final de la tabla no desplaza a las demás
    [ThreadStatic] private static BarConfig? _bar;   // solo durante Build (evita pasar el parámetro por cada fila)

    /// <summary>Fase 3.1: un módulo que no arrancó se ve y se puede reintentar sin abrir la bandeja. Solo aparece si hay un problema real (Failed/Crashed); lo desactivado a propósito no lo es.</summary>
    private static IEnumerable<SettingsGroup> ProblemGroups(SettingsInput input)
    {
        var rows = new List<SettingsRow>();
        foreach (var (id, st) in input.States.Where(kv => kv.Value is ModuleState.Failed or ModuleState.Crashed).OrderBy(kv => kv.Key))
        {
            string name = TrayPanelModel.Label(id); input.ModuleFailures.TryGetValue(id, out var f);
            string sub = st == ModuleState.Crashed ? "Se está reiniciando tras una caída" : "No se pudo iniciar" + (f is null ? "" : " · " + f.Reason);
            rows.Add(new(RowKind.Button, "module.retry:" + id, "Reintentar " + name, sub));
        }
        if (rows.Count > 0) yield return new("Módulos con problemas", rows, "El informe de diagnóstico (más abajo) incluye el detalle técnico. Hatsu Desktop no cambia las protecciones de Windows.");
    }

    public static IReadOnlyList<SettingsPage> Build(SettingsInput input)
    {
        var cfg = input.Cfg; var n = cfg.Notch ?? NotchSettings.Factory; _bar = cfg.Bar ?? new BarConfig(); var bar = _bar;
        bool installed = !(input.States.TryGetValue("notch", out var ns) && ns == ModuleState.NotInstalled);
        const string noNotch = "Hatsu Notch no está instalado junto a Hatsu Desktop.";
        bool On(string m) => cfg.Modules.TryGetValue(m, out var mc) ? mc.Enabled : true;

        // WM-013/014: una sección por tema, en el orden en que se usan. Cada fila está en UN sitio; los textos se parten en líneas (sin «…»).
        var general = new SettingsPage("general", "General", "gear",
        [
            new("Módulos", [
                new(RowKind.Toggle, "mod:bar", "Barra superior", "Reloj, indicadores y el panel de Hatsu Desktop", On("bar")),
                new(RowKind.Toggle, "mod:dock", "Dock", "Apps abiertas y ancladas, con vista previa de ventanas", On("dock")),
                new(RowKind.Toggle, "mod:connect", "Hatsu Connect", "Batería y notificaciones del iPhone (opcional)", On("connect")),
                new(RowKind.Toggle, "mod:notch", "Hatsu Notch", "Música, actividades y más en la parte superior", installed && On("notch"), installed, installed ? null : noNotch),
            ], "Cada módulo es un proceso aparte: si uno falla, los demás siguen."),
            .. ProblemGroups(input),
            new("Sistema", [
                new(RowKind.Toggle, "autostart", "Iniciar con Windows", "Abre Hatsu Desktop al iniciar sesión", input.Autostart),
                new(RowKind.Button, "onboarding.rerun", "Abrir el asistente de configuración", "No cambia nada hasta que confirmes"),
                new(RowKind.Button, "diagnose.run", "Generar informe de diagnóstico", "Se guarda en tu equipo y se abre para que lo revises; no se envía nada"),
                Bar(10, "Pide confirmación antes de reiniciar, apagar o cerrar la sesión"),
                .. (input.AutostartForeignPath is { } other ? [new SettingsRow(RowKind.Button, "autostart.adopt", "Iniciar esta instalación con Windows", "Hoy Windows inicia otra copia: " + other + ". Se cambia solo si lo confirmas; la carpeta anterior no se borra.")] : Array.Empty<SettingsRow>()),
            ]),
        ]);

        var dockPage = DockPage(input.Dock, On("dock"), input);

        var barPage = new SettingsPage("bar", "Barra superior", "bar",
        [
            new("Apariencia", [
                new(RowKind.Segmented, "bar.bg", "Fondo", Options: SettingsBuilder.BackgroundNames, Selected: Index(BackgroundValues, bar.Background.ToString())),
            ]),
            new("Elementos", [Bar(0), Bar(1), Bar(2), Bar(3), Bar(4), Bar(5), Bar(6), Bar(7), Bar(8), Bar(28, "Los de la bandeja de Windows; siguen también en la barra de tareas"),
                BarF("showPhonePercent", "Si el iPhone está conectado pero Windows no da una lectura reciente, se muestra «—» en vez de un número viejo"),
                BarF("showPowerWithoutBattery", "Solo si el equipo no tiene batería y está conectado a la corriente; sin porcentaje. Apagado, el módulo de batería no aparece"),
                BarF("showTaskManager", "Abre el Administrador de tareas de Windows"),
                BarF("showHubSystem", "Solo se muestra si «Iconos de Rendimiento, Red y Discos» está activado"),
                BarF("showHubNetwork", "Solo se muestra si «Iconos de Rendimiento, Red y Discos» está activado"),
                BarF("showHubDisks", "Solo se muestra si «Iconos de Rendimiento, Red y Discos» está activado"),
                new(RowKind.Button, "bar.orderreset", "Restaurar orden predeterminado", "Arrastra los iconos de la barra para cambiar su orden")]),
            new("Comportamiento", [
                Bar(9, "Las ventanas maximizadas empiezan debajo de la barra y no quedan tapadas"),
                Bar(11),
                Bar(12, "Solo para apps con menú de Windows o accesible; los menús originales siguen en la app"),
            ]),
            new("Sonido", [Bar(34, "Micrófono y volumen de cada aplicación en el mismo menú del icono de volumen. Desactivado, no se leen ni el micrófono ni las apps"), Bar(36, "Elegir altavoces o audífonos para cada app desde el menú Sonido. Usa una interfaz interna de Windows (la de su propio mezclador): cada cambio se comprueba y, si Windows no lo acepta, se dice")]),
            new("Centro de control", [Bar(13), Bar(14), Bar(15), Bar(16), Bar(17), Bar(18), Bar(19), Bar(20)], "Qué controles aparecen en el panel de la Barra."),
            new("Periféricos", [
                BarF("showPeripherals", "Batería de ratón, teclado y auriculares que Windows y el propio dispositivo informan (Bluetooth estándar y HID). Apagado: no se lee nada y no queda nada en memoria"),
                BarF("peripheralAsusRog", "Teclado y ratón ROG con el receptor ROG OMNI. A diferencia de los demás lectores, ESTE ENVÍA al receptor la misma consulta de 64 bytes que Armoury Crate (solo lee la batería). Probado con Strix Scope II 96 y Keris II Origin; otros modelos no están verificados"),
                BarF("peripheralLogitech", "PROBABLE, SIN VERIFICAR: escrito con la documentación pública del protocolo HID++ y sin ningún dispositivo Logitech para probarlo. Pregunta al receptor (solo lectura) cómo se llama cada dispositivo y cuánta batería tiene"),
                BarF("peripheralAlerts", "Una sola vez por dispositivo al bajar del umbral; vuelve a avisar solo tras cargarlo. Sale por las notificaciones de Hatsu Notch"),
                new(RowKind.Field, "periph.asus.kb", "Nombre del teclado ROG", "El receptor no dice el modelo. Si lo escribes aquí, aparece con ese nombre (por ejemplo, tu modelo exacto)", Text: bar.PeripheralAsusKeyboardName, Placeholder: "Teclado ROG"),
                new(RowKind.Field, "periph.asus.mouse", "Nombre del ratón ROG", "Igual que el teclado: el nombre que quieras para tu ratón", Text: bar.PeripheralAsusMouseName, Placeholder: "Ratón ROG"),
                new(RowKind.Field, "periph.thr", "Umbral de batería baja (%)", "Entre 5 y 50", Text: bar.PeripheralAlertThreshold.ToString(), Placeholder: "20"),
                new(RowKind.Button, "periph.unhide", "Mostrar los dispositivos ocultos"),
            ], "Los receptores 2,4 GHz y el software de cada fabricante (Armoury Crate, Synapse…) no se consultan: sin interfaz documentada no se inventa nada."),
            new("Limpieza", [
                BarF("showCleanup", "Analiza primero y pide confirmación antes de borrar. Nunca limpia por sí sola ni en segundo plano; no toca documentos, descargas ni proyectos"),
                BarF("cleanupIsland", "Análisis y limpieza como actividad de Hatsu Notch; no sustituye la música, las letras ni las notificaciones"),
            ]),
            new("VPN", [
                BarF("showVpn", "Muestra las conexiones VPN de Windows (Configuración › Red › VPN) y las conecta o desconecta con las credenciales que Windows ya guardó. No funciona con apps de VPN de terceros (WireGuard, NordVPN…)"),
            ]),
            new("Panel de Hatsu Desktop", [
                Bar(21),
                new(RowKind.Field, "hub.ms", "Leer sensores cada (ms)", "Solo mientras el panel está abierto", Text: bar.HubRefreshMs.ToString(), Placeholder: "1000"),
                new(RowKind.Segmented, "hub.rate", "Unidad de red", Options: RateNames, Selected: Index(RateValues, bar.RateUnit.ToString())),
            ]),
            new("Atajos de teclado", HotkeyRows(bar), "Estado real: lo que Windows aceptó al registrarlos. Windows no dice qué programa tiene un atajo rechazado, y los atajos que otra app usa solo dentro de su ventana no se pueden detectar."),
        ]);

        var capturePage = new SettingsPage("capture", "Capturas y grabación", "camera",
        [
            new("Capturas", [
                Bar(31, "Ctrl+Mayús+3: pantalla · 4: región · 5: barra de captura · 6: grabar o detener"),
                new(RowKind.Segmented, "bar.capdelay", "Retardo", "Para capturar menús abiertos: cuenta atrás antes de la captura", Options: CaptureDelayNames, Selected: Math.Max(0, Array.IndexOf(CaptureDelayValues, bar.CaptureDelaySeconds))),
                new(RowKind.Segmented, "bar.capfmt", "Formato", Options: CaptureFormatNames, Selected: Index(CaptureFormatValues, bar.CaptureFormat)),
                Bar(32),
                Bar(33, "Unos segundos en la esquina; un clic abre la vista previa con acciones"),
                new(RowKind.Field, "bar.capdir", "Carpeta", Text: bar.CaptureFolder ?? "", Placeholder: "Imágenes\\Capturas de pantalla"),
            ], "Si otro programa ya usa un atajo, Windows lo rechaza y se indica en «Atajos de teclado». Las capturas aparecen en la galería de Hatsu Notch."),
            new("Grabación de pantalla", [
                new(RowKind.Info, "bar.rec", "Solo vídeo (nunca el micrófono ni el sonido), siempre con indicador visible y solo cuando tú la inicias. Se guarda en Vídeos\\Screen Recordings."),
            ]),
        ]);

        var spotPage = new SettingsPage("spot", "Spotlight y Launchpad", "search",
        [
            new("Spotlight", [
                new(RowKind.Segmented, "bar.spot", "Atajo", Options: SpotKeyNames, Selected: Index(SpotKeyValues, bar.SpotlightHotkey.ToString())),
                Bar(30, "Recientes de Windows y el índice de Windows Search; no indexa nada por su cuenta"),
            ], "Alt+Espacio sustituye al menú de ventana de Windows mientras Hatsu Desktop está abierto."),
            new("Herramientas", [
                new(RowKind.Info, "bar.spotpro", "Acciones de Hatsu Desktop, «ip», «red», «ping …» y «dns …» (solo con Intro), «ssh usuario@equipo», «> …» (nunca se ejecuta: abre la terminal o lo copia) y cálculos como «20% de 150»."),
                new(RowKind.Info, "bar.scripts", "Scripts propios: %APPDATA%\\WinMac\\spotlight-scripts.json. Se ejecutan con doble Intro y tus permisos, nunca elevados."),
                new(RowKind.Info, "bar.workspaces", "Espacios de trabajo: «Guardar espacio de trabajo», «Restaurar …», «Ventanas lado a lado» o «en cuadrícula». Ninguna app se abre sola."),
            ]),
            new("Launchpad", [Bar(29, "Solo al pulsarla sola; los atajos con Windows siguen igual. Ctrl+Esc abre el menú Inicio")], "Desactivado, la tecla Windows vuelve a abrir el menú Inicio."),
        ]);

        var wrows = NcWidgetRows(bar); int enabledCount = wrows.Count(r => r.On);
        var ncPage = new SettingsPage("nc", "Notificaciones", "bell",
        [
            new("Centro de Notificaciones", [
                Bar(25, "Desactivado, el reloj abre el calendario de antes"),
                Bar(26, "Con el permiso de Windows. Solo en memoria: no se guarda en disco"),
                Bar(27, "Desactivado, «×» y «Borrar» solo las ocultan en Hatsu Desktop durante la sesión"),
            ], "Se muestran todas las que Windows guarda en su centro (Win+N) y deja leer; las que una app no envía ahí no se pueden ver."),
            new("Widgets", wrows.Select((r, i) => new SettingsRow(RowKind.Toggle, "nc.w:" + r.Kind.ToString().ToLowerInvariant(), NcWidgetNames[r.Kind], null, r.On,
                CanUp: r.On && i > 0, CanDown: r.On && i < enabledCount - 1)).ToList(), "La batería solo aparece en equipos con batería. El Tiempo usa la ubicación de «Clima y ciudades»."),
            new("Calendario", [new(RowKind.Field, "bar.ics", "Archivo .ics", "Opcional. Solo lectura: se muestran los eventos del archivo", Text: bar.CalendarIcsPath ?? "", Placeholder: "Sin archivo")]),
        ]);

        var weatherPage = WeatherPage(bar, input);

        var clipPage = new SettingsPage("clip", "Portapapeles", "clipboard",
        [
            new("Historial", [
                new(RowKind.Field, "notch.cliplimit", "Elementos en el historial", Enabled: installed, Reason: installed ? null : noNotch, Text: n.ClipboardLimit.ToString(), Placeholder: "50"),
                new(RowKind.Toggle, "notch.clippersist", "Conservar al reiniciar", n.ClipboardPersist ? "Activado: el historial se guarda cifrado para tu usuario de Windows (DPAPI)" : "Desactivado: nada del historial se guarda en disco", n.ClipboardPersist, installed, installed ? null : noNotch),
                new(RowKind.Button, "notch.clipclear", "Borrar el historial del portapapeles", Enabled: installed, Reason: installed ? null : noNotch),
            ], "Solo en este equipo: el historial nunca se sincroniza ni se registra."),
            new("Privacidad", [
                new(RowKind.Toggle, "notch.clipsecrets", "No guardar contraseñas ni claves", "Detecta claves privadas, tokens y contraseñas generadas; ignora siempre a los gestores de contraseñas", n.ClipboardSkipSecrets, installed, installed ? null : noNotch),
                new(RowKind.Toggle, "notch.clipexcl", "Ignorar las apps de la lista", "Lo que copien no entra en el historial (se identifican por el nombre de su proceso)", n.ClipboardExclusionsOn, installed, installed ? null : noNotch),
                new(RowKind.Field, "notch.clipapps", "Apps excluidas", Enabled: installed && n.ClipboardExclusionsOn, Reason: !installed ? noNotch : "Activa «Ignorar las apps de la lista»", Text: string.Join(", ", NotchSettings.ParseApps(n.ClipboardExcludedApps)), Placeholder: "KeePass, 1Password, … (separadas por comas)"),
            ]),
        ]);

        var rows = new List<SettingsRow>();
        for (int i = 0; i < n.Sections.Count; i++) { var s = n.Sections[i]; rows.Add(new(RowKind.Toggle, "notch.sec:" + i, SectionNames.TryGetValue(s.Id, out var nm) ? nm : s.Id, null, s.Enabled, installed, null, CanUp: installed && i > 0, CanDown: installed && i < n.Sections.Count - 1)); }
        string? why = installed ? null : noNotch; bool custom = n.MusicPlayer == "Custom";
        var notch = new List<SettingsGroup>();
        if (!installed) notch.Add(new(null, [new(RowKind.Info, "notch.none", noNotch)]));
        notch.Add(new("Apariencia", [
            new(RowKind.Segmented, "notch.pos", "Posición", Enabled: installed, Reason: why, Options: PositionNames, Selected: Index(NotchSettings.Positions, n.Position)),
            new(RowKind.Slider, "notch.glass", "Vidrio", Enabled: installed, Reason: why, Value: n.GlassLevel),
        ]));
        notch.Add(new("Música", [
            new(RowKind.Toggle, "notch.music", "Música", "Muestra lo que suena en tu reproductor", n.MusicEnabled, installed, why),
            new(RowKind.Segmented, "notch.player", "Reproductor", Enabled: installed, Reason: why, Options: PlayerNames, Selected: Index(NotchSettings.Players, n.MusicPlayer)),
            new(RowKind.Field, "notch.path", "Ruta del reproductor", Enabled: installed && custom, Reason: installed && !custom ? "Solo con «Otro»" : why, Text: n.CustomPlayerPath ?? "", Placeholder: "C:\\…\\reproductor.exe"),
            new(RowKind.Toggle, "notch.lyrics", "Letras sincronizadas", "Envía título, artista y duración a NetEase y, si no la tiene, a LRCLIB para buscar la letra", n.LyricsEnabled, installed, why),
            new(RowKind.Toggle, "notch.pill", "Letra en el notch cerrado", null, n.LyricsInPill, installed && n.LyricsEnabled, installed && !n.LyricsEnabled ? "Activa las letras sincronizadas" : why),
        ]));
        notch.Add(new("Notch cerrado", [
            new(RowKind.Toggle, "notch.pillvol", "Volumen", "Muestra el nivel al subir, bajar o silenciar", n.PillVolume, installed, why),
            new(RowKind.Toggle, "notch.pillbright", "Brillo", "Pantalla integrada y el deslizador de la barra (los monitores externos no avisan)", n.PillBrightness, installed, why),
            new(RowKind.Toggle, "notch.pillnotif", "Notificaciones", "Las de Windows; requiere «Mostrar notificaciones de Windows» en la barra", n.PillNotifications, installed, why),
            new(RowKind.Field, "notch.hudsec", "Indicadores durante (s)", Enabled: installed, Reason: why, Text: n.PillHudSeconds.ToString(), Placeholder: "2"),
            new(RowKind.Field, "notch.notifsec", "Notificaciones durante (s)", Enabled: installed, Reason: why, Text: n.PillNotifySeconds.ToString(), Placeholder: "5"),
            new(RowKind.Toggle, "notch.pillanim", "Animaciones", "La píldora crece y vuelve con una transición", n.PillAnimations, installed, why),
        ], "Lo que el notch muestra un momento sin abrirse."));
        notch.Add(new("Agentes de IA", [
            new(RowKind.Toggle, "notch.agents", "Agentes de IA", "Estado de tus agentes en el notch", n.AgentsEnabled, installed, why),
            new(RowKind.Field, "notch.poll", "Actualizar cada (s)", Enabled: installed, Reason: why, Text: n.PollIntervalSeconds.ToString(), Placeholder: "30"),
        ]));
        notch.Add(new("Pestañas", rows, "Activa, oculta y ordena las pestañas del notch."));
        notch.Add(new(null, [new(RowKind.Button, "notch.local", "Rutas y datos de Hatsu Notch…", Enabled: installed, Reason: why)]));
        var notchPage = new SettingsPage("notch", "Hatsu Notch", "island", notch);

        var connectPage = ConnectPage(input, On("connect"));
        _bar = null;
        var pages = new List<SettingsPage> { general, dockPage, barPage, notchPage, capturePage, spotPage };
        if (ModulesPage(cfg, installed) is { } modulesPage) pages.Add(modulesPage);
        pages.AddRange([ncPage, weatherPage, clipPage, connectPage, ExtensionsPage(input.Extensions)]);
        return Organize(pages);
    }

    /// <summary>WM-019 / WM-001: tamaño del Dock (preajustes compactos y ajuste fino) y comportamiento. Escribe en dock.json, que el Dock vuelve a leer al cambiar.</summary>
    private static SettingsPage DockPage(WinMac.Dock.DockConfig? d, bool enabled, SettingsInput input)
    {
        if (d is null) return new SettingsPage("dock", "Escritorio y Dock", "dock", [new(null, [new(RowKind.Info, "dock.none", "No se pudo leer la configuración del Dock (dock.json). Ábrelo una vez para crearla.")]), TaskbarGroup(input)]);
        string? off = enabled ? null : "El Dock está desactivado en General";
        var presets = WinMac.Dock.DockSettings.Presets;
        int sel = Array.FindIndex(presets.ToArray(), p => Math.Abs(p.Size - d.IconSize) < 0.5);
        var names = presets.Select(p => p.Name).ToList(); if (sel < 0) { names.Add("Otro"); sel = names.Count - 1; }
        return new SettingsPage("dock", "Escritorio y Dock", "dock",
        [
            new("Tamaño", [
                new(RowKind.Segmented, "dock.size", "Tamaño de los iconos", $"{d.IconSize:0} puntos (a 150 % de escala, 1,5 veces más píxeles)", Enabled: enabled, Reason: off, Options: names, Selected: sel),
                new(RowKind.Slider, "dock.fine", "Ajuste fino", $"De {WinMac.Dock.DockSettings.MinIconSize:0} a {DockFineMax:0} puntos", Enabled: enabled, Reason: off, Value: DockFineValue(d.IconSize), SliderMin: WinMac.Dock.DockSettings.MinIconSize, SliderMax: DockFineMax, Unit: "pt"),
                new(RowKind.Toggle, "dock.magnify", "Ampliación al pasar el ratón", null, d.MagnificationEnabled, enabled, off),
            ], "El tamaño se ajusta a la escala de cada monitor (DPI): el mismo valor se ve igual de grande en todas las pantallas."),
            new("Comportamiento", [
                new(RowKind.Toggle, "dock.autohide", "Ocultar automáticamente", "Aparece al llevar el ratón al borde inferior", d.AutoHide, enabled, off),
                new(RowKind.Toggle, "dock.reserve", "Reservar espacio para el Dock", "Las ventanas maximizadas terminan encima del Dock y sus controles quedan visibles", d.ReserveSpace && !d.AutoHide, enabled && !d.AutoHide, !enabled ? off : d.AutoHide ? "Con ocultación automática el Dock no ocupa espacio" : null),
                new(RowKind.Toggle, "dock.previews", "Vista previa de ventanas", "Al pasar el ratón sobre una app abierta", d.PreviewsEnabled, enabled, off),
                new(RowKind.Toggle, "dock.minimize", "Clic en la app activa la minimiza", "Como en Windows; desactivado, como en macOS", d.ClickActiveMinimizes, enabled, off),
            ], "Al salir de Hatsu Desktop el espacio reservado se devuelve a Windows."),
            TaskbarGroup(input),
        ]);
    }

    /// <summary>WM-034: independiente del Dock (las cuatro combinaciones con «Ocultar automáticamente» del Dock son válidas).</summary>
    private static SettingsGroup TaskbarGroup(SettingsInput input)
    {
        var rows = new List<SettingsRow> { new(RowKind.Toggle, "host.hidetaskbar", "Ocultar barra de tareas de Windows", "En todas las pantallas, mientras Hatsu Desktop está abierto. Se restaura al desactivarlo y al salir", input.Cfg.HideTaskbar) };
        if (input.Cfg.HideTaskbar && input.TaskbarStatus.Length > 0) rows.Add(new(RowKind.Info, "host.taskbarstate", "Ahora: " + input.TaskbarStatus));
        return new SettingsGroup("Barra de tareas de Windows", rows,
            "No se cierra ni se modifica Explorer: se ocultan sus ventanas y se activa temporalmente su «Ocultar automáticamente» para que no reserve espacio; al restaurar vuelve tu ajuste. " +
            "Si Hatsu Desktop falla, un proceso guardián la devuelve. Si algo queda mal: ejecuta «HatsuDesktop.exe --restore-taskbar», reinicia el Explorador o cierra sesión.");
    }

    /// <summary>
    /// WinMac Connect (docs/39, fases 1 y 2): batería, estado y notificaciones del iPhone por Bluetooth, sin app en el iPhone y sin Phone Link. Todo opcional:
    /// el módulo viene APAGADO y cada función se puede quitar. Solo controles que hacen algo de verdad; contestar o rechazar llamadas NO se ofrece (sin validar).
    /// </summary>
    private static SettingsPage ConnectPage(SettingsInput input, bool on)
    {
        var c = input.Cfg.Connect ?? new WinMac.Connect.ConnectSettings(); var bar = input.Cfg.Bar ?? new BarConfig();
        string? off = on ? null : "Activa Hatsu Connect arriba";
        return new("connect", "Hatsu Connect", "phone",
        [
            new("Hatsu Connect", [
                new(RowKind.Toggle, "connect.module", "Conectar con el iPhone", "Batería y notificaciones por Bluetooth. Sin app en el iPhone ni Phone Link", on),
            ], "Un módulo aparte, como la Barra y el Dock: si falla, lo demás sigue. Desactivado, no usa el Bluetooth ni consume nada."),
            new("iPhone", [
                new(RowKind.Field, "connect.device", "Nombre del iPhone", "Parte del nombre que muestra Windows. Vacío: el primer iPhone emparejado", Text: c.DeviceName ?? "", Placeholder: "Automático"),
                new(RowKind.Button, "connect.bt", "Emparejar o quitar en Configuración de Bluetooth…"),
                new(RowKind.Info, "connect.pair", "Para emparejar: Configuración › Bluetooth y dispositivos › Agregar dispositivo › Bluetooth › tu iPhone; compara el código en las dos pantallas. En el iPhone, Ajustes › Bluetooth › (i) junto a este PC › activa «Compartir notificaciones del sistema»."),
            ]),
            new("Qué mostrar", [
                new(RowKind.Toggle, "connect.battery", "Batería y estado de conexión", "El porcentaje real que da el iPhone; sin conexión no se muestra un porcentaje viejo", c.Battery, on, off),
                Bar(35, "Solo aparece con datos reales del iPhone"),
                new(RowKind.Toggle, "connect.notifs", "Notificaciones del iPhone", "En el Centro de Notificaciones de Hatsu Desktop; las que ya tenías entran sin avisos", c.Notifications, on, off),
                new(RowKind.Toggle, "connect.calls", "Avisos de llamadas", "Entrantes y perdidas. Contestar y rechazar desde el PC aún no está disponible", c.Calls, on && c.Notifications, on ? null : off),
                new(RowKind.Toggle, "connect.island", "También en Hatsu Notch", "Pestaña Notificaciones de Hatsu Notch", c.Island, on && c.Notifications, on ? null : off),
                new(RowKind.Toggle, "connect.alerts", "Aviso emergente para las nuevas", "En Hatsu Notch, como las de Windows", c.Alerts, on && c.Notifications && c.Island, on ? null : off),
            ]),
            new("Llamadas completas desde el PC", [
                new(RowKind.Toggle, "connect.callcontrol", "Contestar, rechazar y colgar desde Hatsu Notch, con el audio en el PC", "Windows lleva la llamada del teléfono (iPhone o Android) a tus altavoces y micrófono. Registra a Hatsu como cliente de telefonía mientras esté activo", c.CallControl, on && c.Calls, on ? null : off),
                new(RowKind.Info, "connect.callcontrol.status", "Estado del control de llamadas: " + input.ConnectDiag.CallControlStatus),
                new(RowKind.Info, "connect.callcontrol.why", "Necesita el teléfono emparejado en Windows (manos libres/HFP) y que Windows conceda a Hatsu la telefonía Bluetooth: es una capacidad restringida que aprueba Microsoft. Si Windows no la concede, no se muestra ningún botón de contestar y Hatsu sigue avisando de las llamadas como siempre. Al apagarlo, Hatsu deshace su registro."),
                new(RowKind.Info, "connect.callcontrol.audio", "Audio: Windows usa tus dispositivos predeterminados de COMUNICACIONES (Configuración › Sistema › Sonido › Más opciones de sonido › pestañas Reproducción y Grabación). Revisa que el micrófono y los audífonos de comunicaciones sean los que quieres y que el nivel del micrófono no esté muy bajo."),
                new(RowKind.Info, "connect.callcontrol.focus", "Si la llamada se corta al abrir un juego o una app a pantalla completa, desactiva en Windows las reglas automáticas del Asistente de concentración (jugando / pantalla completa): reinician la conexión de manos libres."),
            ], "Si usas Enlace Móvil (Phone Link) con el mismo teléfono, desactiva esta opción o cierra Enlace Móvil: los dos quieren gestionar la misma llamada."),
            new("Privacidad", [
                new(RowKind.Segmented, "connect.content", "Contenido de las notificaciones", "Lo que no se elige ni siquiera se pide al iPhone", Options: ContentNames, Selected: (int)c.Content, Enabled: on && c.Notifications, Reason: on ? null : off),
            ], "El contenido vive solo en memoria mientras Hatsu Desktop está abierto: no se guarda en disco, no se registra y no sale del PC. Quitar el iPhone en Windows borra el vínculo."),
            DiagnosticsGroup(input.ConnectDiag),
            new("Qué no hace (todavía)", [
                new(RowKind.Info, "connect.m1", "Mensajes (SMS e iMessage): fase posterior. Ahora Hatsu Desktop no lee, envía ni borra mensajes."),
                new(RowKind.Info, "connect.m2", "Hablar por el PC (micrófono y altavoces): solo con «Llamadas completas desde el PC» y si Windows concede a Hatsu la telefonía Bluetooth (aprobación de Microsoft)."),
                new(RowKind.Info, "connect.m3", "Estado de carga del iPhone: Bluetooth solo da el porcentaje. Portapapeles universal y Handoff: dependen de servicios privados de Apple."),
            ]),
        ]);
    }

    /// <summary>
    /// Diagnóstico avanzado para validar Bluetooth y llamadas con un teléfono real desde la versión de la Store (docs/58). SOLO LECTURA: no contesta, no cuelga, no marca, no conecta
    /// ni desconecta Bluetooth. No hay botones de contestar/rechazar: no existe un mecanismo con audio del PC verificado.
    /// </summary>
    private static SettingsGroup DiagnosticsGroup(ConnectDiagView d)
    {
        var rows = new List<SettingsRow>
        {
            new(RowKind.Button, "connect.diag.run", d.Running ? "Ejecutando el diagnóstico…" : "Ejecutar diagnóstico de Bluetooth y llamadas", "Solo lectura. No cambia conexiones ni contesta llamadas", Enabled: !d.Running),
            new(RowKind.Info, "connect.diag.status", "Estado: " + d.Status),
        };
        int i = 0;
        foreach (var l in d.Summary ?? []) rows.Add(new(RowKind.Info, "connect.diag.sum:" + i++, l));
        rows.Add(new(RowKind.Button, "connect.diag.open", "Abrir el informe completo", Enabled: d.HasReport && !d.Running, Reason: d.HasReport ? null : "Ejecuta el diagnóstico primero"));
        rows.Add(new(RowKind.Button, "connect.diag.copy", "Copiar el informe al portapapeles", Enabled: d.HasReport && !d.Running, Reason: d.HasReport ? null : "Ejecuta el diagnóstico primero"));
        rows.Add(new(RowKind.Button, "connect.diag.save", "Guardar una copia…", Enabled: d.HasReport && !d.Running, Reason: d.HasReport ? null : "Ejecuta el diagnóstico primero"));
        rows.Add(new(RowKind.Toggle, "connect.diag.observe", "Observar llamadas del sistema (temporal)", "Solo lectura: registra lo que Windows informa durante una llamada real. Se apaga solo a los 15 min. Estado: " + d.ObserveStatus, d.Observing));
        rows.Add(new(RowKind.Button, "connect.diag.exp:hfp", "Experimento: registrar y conectar HFP…", "MODIFICA temporalmente el estado de Windows (se deshace solo). Pide confirmación", Enabled: !d.Running));
        rows.Add(new(RowKind.Button, "connect.diag.exp:audio", "Experimento: pasar el audio de una llamada activa al PC…", "Necesita una llamada ya contestada. Pide confirmación", Enabled: !d.Running));
        rows.Add(new(RowKind.Button, "connect.diag.exp:restore", "Deshacer el registro de Hatsu Desktop en el transporte telefónico…", "Solo quita el registro de esta app", Enabled: !d.Running));
        rows.Add(new(RowKind.Info, "connect.diag.ios", "Prueba con iPhone: 1) comprueba que está conectado por Bluetooth; 2) ejecuta el diagnóstico; 3) activa «Observar»; 4) recibe una llamada y comprueba el nombre; 5) contesta en el teléfono: NO debe aparecer «Llamada finalizada»; 6) cuelga, desactiva «Observar» y vuelve a ejecutar/copiar el informe."));
        rows.Add(new(RowKind.Info, "connect.diag.android", "Prueba con Android: 1) empareja el teléfono por Bluetooth en Windows; 2) ejecuta el diagnóstico y revisa servicios (HFP, PBAP, MAP) y transportes telefónicos; 3) activa «Observar», haz una llamada real y mira qué informa Windows. Android no usa ANCS: en Android no se mostrará el aviso de llamada de Hatsu."));
        return new SettingsGroup("Diagnóstico de llamadas (avanzado)", rows,
            "El informe se queda en este PC: sin notificaciones, mensajes, audio, historial ni números completos; direcciones Bluetooth y rutas anonimizadas. Nada se envía a ningún servidor. Contestar y hablar desde el PC sigue sin estar disponible (docs/57).");
    }

    public static readonly string[] ContentNames = ["Completo", "Solo título", "Solo la app"];

    public const double DockFineMax = 96;
    public static double DockFineValue(double size) => Math.Clamp((size - WinMac.Dock.DockSettings.MinIconSize) / (DockFineMax - WinMac.Dock.DockSettings.MinIconSize) * 100, 0, 100);
    public static double DockFineSize(double value) => Math.Round(WinMac.Dock.DockSettings.MinIconSize + Math.Clamp(value, 0, 100) / 100 * (DockFineMax - WinMac.Dock.DockSettings.MinIconSize));

    /// <summary>WM-015: ubicación principal (automática con Windows o ciudad elegida), lista editable de ciudades adicionales, unidades, fuente y permiso.</summary>
    private static SettingsPage WeatherPage(BarConfig bar, SettingsInput input)
    {
        bool auto = bar.WeatherAutoLocation; var city = WinMac.Bar.Hub.GeoPoint.Parse(bar.WeatherCity);
        var favs = WinMac.Bar.Hub.GeoPoint.ParseList(bar.WeatherFavorites);
        string perm = input.LocationPermission switch { "" => "", var p => " Permiso de ubicación de Windows: " + p + "." };
        string state = auto ? $"Ubicación de Windows; si no está disponible se usa {(city is null ? "la ciudad elegida (ninguna todavía)" : city.Name)}.{perm}" : city is null ? "Sin ciudad: el Tiempo no hace peticiones." : $"Ciudad elegida: {city.Name}.";
        var favRows = favs.Select((g, i) => new SettingsRow(RowKind.Toggle, "wx.fav:" + i, g.Name, "Desactiva para quitarla · flechas para ordenar", true, CanUp: i > 0, CanDown: i < favs.Count - 1)).ToList<SettingsRow>();
        if (favRows.Count == 0) favRows.Add(new(RowKind.Info, "wx.nofavs", "Sin ciudades adicionales. Añade una abajo: aparecerán en el panel del Tiempo."));
        favRows.Add(new(RowKind.Field, "hub.favadd", "Añadir ciudad", Text: "", Placeholder: "Escribe una ciudad y pulsa Intro"));
        return new SettingsPage("weather", "Clima y ciudades", "globe",
        [
            new("Ubicación principal", [
                new(RowKind.Segmented, "wx.src", "Origen", auto ? "Windows puede pedir permiso de ubicación la primera vez" : null, Options: WeatherSourceNames, Selected: auto ? 0 : 1),
                new(RowKind.Field, "hub.city", auto ? "Ciudad de respaldo" : "Ciudad", Text: city?.Name ?? "", Placeholder: "Escribe una ciudad…"),
                new(RowKind.Info, "wx.state", "Ahora: " + state),
            ], string.IsNullOrWhiteSpace(input.WeatherStatus) ? "Escribe el nombre y elige de la lista. Si hay varias con el mismo nombre, añade el país: «Molina, Chile»." : input.WeatherStatus),
            new("Ciudades adicionales", favRows),
            new("Unidades y barra", [Bar(23), Bar(22)], "Datos de Open-Meteo (sin cuenta ni clave). Solo se envían las coordenadas de las ciudades."),
        ]);
    }

    public static readonly string[] WeatherSourceNames = ["Automática", "Ciudad elegida"];

    /// <summary>Extensions (docs/30): each one with its permissions and live state; off by default after installing; install from a folder, uninstall.</summary>
    private static SettingsPage ExtensionsPage(IReadOnlyList<(Extensions.InstalledExtension Ext, string Status)>? exts)
    {
        var groups = new List<SettingsGroup>();
        bool available = exts is not null; const string unavailable = "Las extensiones no están disponibles en esta sesión";
        var rows = new List<SettingsRow>();
        foreach (var (e, status) in exts ?? [])
        {
            if (e.Manifest is not { } m) { rows.Add(new(RowKind.Info, "ext.bad:" + Path.GetFileName(e.Folder), Path.GetFileName(e.Folder) + " — no se puede usar: " + e.Error)); continue; }
            rows.Add(new(RowKind.Toggle, "ext:" + m.Id, m.Name + " " + m.Version, $"Puede: {WinMac.Host.Extensions.ExtensionManifest.Describe(m.Permissions)} · {(e.Enabled ? status : "Desactivada")}", e.Enabled));
            rows.Add(new(RowKind.Button, "ext.uninstall:" + m.Id, "Desinstalar " + m.Name));
        }

        if (rows.Count == 0) rows.Add(new(RowKind.Info, "ext.empty", "No hay extensiones instaladas."));
        groups.Add(new("Instaladas", rows, "Cada extensión es un programa aparte: si falla, Hatsu Desktop sigue funcionando. Solo puede hacer lo que indican sus permisos; se ejecuta con tus permisos de usuario (no es un entorno aislado para código desconocido: instala solo extensiones en las que confíes)."));
        groups.Add(new("Instalar", [new(RowKind.Field, "ext.install", "Carpeta de la extensión", Enabled: available, Reason: available ? null : unavailable, Text: "", Placeholder: "C:\\…\\mi-extension (con extension.json)")], "Se copia a la carpeta de extensiones de Hatsu Desktop y queda desactivada hasta que la actives."));
        return new SettingsPage("ext", "Extensiones", "puzzle", groups);
    }
}
