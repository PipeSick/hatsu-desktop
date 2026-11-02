using WinMac.Bar;

namespace WinMac.Host;

public enum RowKind { Toggle, Segmented, Field, Slider, Button, Info }

/// <summary>
/// Una fila de la ventana de Ajustes. No hay controles decorativos: lo que no se puede usar aparece atenuado con su <see cref="Reason"/>.
/// <see cref="Index"/> identifica la posición de una pestaña de la Isla (filas «notch.sec:i»).
/// </summary>
public sealed record SettingsRow(RowKind Kind, string Id, string Title, string? Subtitle = null, bool On = false, bool Enabled = true, string? Reason = null,
    IReadOnlyList<string>? Options = null, int Selected = 0, string? Text = null, string? Placeholder = null, double Value = 0, bool CanUp = false, bool CanDown = false);

public sealed record SettingsGroup(string? Header, IReadOnlyList<SettingsRow> Rows, string? Footer = null);
public sealed record SettingsPage(string Id, string Title, string Glyph, IReadOnlyList<SettingsGroup> Groups);

/// <param name="WeatherStatus">Texto de estado de la búsqueda de ciudad (qué se eligió, cómo pedir otra).</param>
public sealed record SettingsInput(HostConfig Cfg, IReadOnlyDictionary<string, ModuleState> States, bool Autostart, string WeatherStatus = "");

/// <summary>Construye las páginas de Ajustes a partir de la configuración vigente. Puro: se prueba sin Windows.</summary>
public static class SettingsBuilder
{
    /// <summary>Opciones booleanas de la Barra: (campo del parche, texto, lectura). Un solo lugar; la ventana las pinta y aplica por tabla.</summary>
    public static readonly (string Field, string Text, Func<BarConfig, bool> Get)[] BarToggles =
    [
        ("showNetwork", "Wi-Fi / red", b => b.ShowNetwork), ("showVolume", "Volumen", b => b.ShowVolume), ("showBattery", "Batería", b => b.ShowBattery), ("showBatteryPercent", "Porcentaje de batería", b => b.ShowBatteryPercent),
        ("showBluetooth", "Bluetooth", b => b.ShowBluetooth), ("showControlCenter", "Control Center", b => b.ShowControlCenter), ("showSeconds", "Segundos en el reloj", b => b.ShowSeconds), ("showDate", "Fecha junto a la hora", b => b.ShowDate),
        ("use24Hour", "Reloj de 24 horas", b => b.Use24Hour), ("reserveSpace", "Reservar el borde superior", b => b.ReserveSpace), ("confirmDestructive", "Confirmar reiniciar, apagar y cerrar sesión", b => b.ConfirmDestructive),
        ("wifiDetails", "Detalle de Wi-Fi al tocar una red", b => b.WifiDetails), ("appMenus", "Menús de la app activa (experimental)", b => b.AppMenus),
        ("ccWifi", "Wi-Fi", b => b.CcWifi), ("ccBluetooth", "Bluetooth", b => b.CcBluetooth), ("ccSound", "Sonido", b => b.CcSound), ("ccBrightness", "Brillo", b => b.CcBrightness),
        ("ccPower", "Energía", b => b.CcPower), ("ccAppearance", "Apariencia", b => b.CcAppearance), ("ccDisplay", "Pantallas", b => b.CcDisplay), ("ccNotifications", "Notificaciones", b => b.CcNotifications),
        ("showHub", "Icono de WinMac (panel con pestañas)", b => b.ShowHub), ("showWeather", "Icono del Tiempo", b => b.ShowWeather), ("weatherFahrenheit", "Grados Fahrenheit", b => b.WeatherFahrenheit),
        ("weatherAutoLocation", "Usar mi ubicación", b => b.WeatherAutoLocation),
        ("notificationCenter", "El reloj abre el Centro de Notificaciones", b => b.NotificationCenter), ("notificationsFromWindows", "Mostrar notificaciones de Windows", b => b.NotificationsFromWindows),
        ("notifRemoveFromWindows", "Borrar también en Windows", b => b.NotifRemoveFromWindows),
        ("showTrayIcons", "Iconos de apps en segundo plano", b => b.ShowTrayIcons),
        ("launchpadOnWinKey", "La tecla Windows abre Launchpad", b => b.LaunchpadOnWinKey),
        ("spotlightFiles", "Buscar también archivos", b => b.SpotlightFiles),
    ];
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
    };

    private static int Index(string[] arr, string v) => Math.Max(0, Array.FindIndex(arr, x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase)));
    private static SettingsRow Bar(int i, string? subtitle = null, string? title = null) => new(RowKind.Toggle, "bar:" + BarToggles[i].Field, title ?? BarToggles[i].Text, subtitle, BarToggles[i].Get(_bar!));
    [ThreadStatic] private static BarConfig? _bar;   // solo durante Build (evita pasar el parámetro por cada fila)

    public static IReadOnlyList<SettingsPage> Build(SettingsInput input)
    {
        var cfg = input.Cfg; var n = cfg.Notch ?? new NotchSettings(); _bar = cfg.Bar ?? new BarConfig(); var bar = _bar;
        bool installed = !(input.States.TryGetValue("notch", out var ns) && ns == ModuleState.NotInstalled);
        const string noNotch = "La Isla (NotchWin) no está instalada junto a WinMac.";
        bool On(string m) => cfg.Modules.TryGetValue(m, out var mc) ? mc.Enabled : true;

        var general = new SettingsPage("general", "General", "gear",
        [
            new("Módulos", [
                new(RowKind.Toggle, "mod:bar", "Barra superior", "Reloj, Wi-Fi, volumen y el panel de WinMac", On("bar")),
                new(RowKind.Toggle, "mod:dock", "Dock", "Apps abiertas y ancladas, con vista previa de ventanas", On("dock")),
                new(RowKind.Toggle, "mod:notch", "Isla", "Notch con música, agentes de IA y más", installed && On("notch"), installed, installed ? null : noNotch),
            ], "Cada módulo es un proceso aparte: si uno falla, los demás siguen."),
            new("Sistema", [new(RowKind.Toggle, "autostart", "Iniciar con Windows", "Abre WinMac al iniciar sesión", input.Autostart)]),
        ]);

        var barPage = new SettingsPage("bar", "Barra superior", "bar",
        [
            new("Apariencia", [
                new(RowKind.Segmented, "bar.bg", "Fondo", Options: SettingsBuilder.BackgroundNames, Selected: Index(BackgroundValues, bar.Background.ToString())),
                new(RowKind.Segmented, "bar.theme", "Tema", Options: ThemeNames, Selected: Index(ThemeValues, bar.Theme.ToString())),
            ]),
            new("Elementos", [Bar(0), Bar(1), Bar(2), Bar(3), Bar(4), Bar(5), Bar(6), Bar(7), Bar(8), Bar(28, "Los de la bandeja de Windows; siguen también en la barra de tareas")]),
            new("Comportamiento", [Bar(9, "El resto de ventanas no se superpone a la barra"), Bar(10), Bar(11), Bar(12, "Solo apps con menú estándar de Windows")]),
            new("Launchpad", [Bar(29, "Solo al pulsarla sola; los atajos con Windows siguen igual. Ctrl+Esc abre el menú Inicio")], "Desactivado, la tecla Windows vuelve a abrir el menú Inicio."),
            new("Spotlight", [
                new(RowKind.Segmented, "bar.spot", "Atajo", Options: SpotKeyNames, Selected: Index(SpotKeyValues, bar.SpotlightHotkey.ToString())),
                Bar(30, "Recientes de Windows y el índice de Windows Search; no indexa nada por su cuenta"),
            ], "Alt+Espacio sustituye al menú de ventana de Windows mientras WinMac está abierto."),
            new("Calendario", [new(RowKind.Field, "bar.ics", "Archivo .ics", Text: bar.CalendarIcsPath ?? "", Placeholder: "Sin archivo")], "Opcional. Solo lectura: el calendario muestra los eventos del archivo."),
            new("Control Center", [Bar(13), Bar(14), Bar(15), Bar(16), Bar(17), Bar(18), Bar(19), Bar(20)], "Qué controles aparecen en el panel de la Barra."),
        ]);

        var hub = new SettingsPage("hub", "Panel y Tiempo", "globe",
        [
            new("Iconos de la barra", [Bar(21), Bar(22)]),
            new("Panel de WinMac", [
                new(RowKind.Field, "hub.ms", "Leer sensores cada (ms)", "Solo mientras el panel está abierto", Text: bar.HubRefreshMs.ToString(), Placeholder: "1000"),
                new(RowKind.Segmented, "hub.rate", "Unidad de red", Options: RateNames, Selected: Index(RateValues, bar.RateUnit.ToString())),
            ]),
            new("Tiempo", [
                new(RowKind.Field, "hub.city", "Ciudad", Text: WinMac.Bar.Hub.GeoPoint.Parse(bar.WeatherCity)?.Name ?? "", Placeholder: "Escribe una ciudad…"),
                new(RowKind.Field, "hub.favs", "Otras ciudades", Text: string.Join(", ", WinMac.Bar.Hub.GeoPoint.ParseList(bar.WeatherFavorites).Select(g => g.Name)), Placeholder: "Santiago, Valparaíso"),
                Bar(23), Bar(24, "Windows pedirá permiso de ubicación"),
            ], string.IsNullOrWhiteSpace(input.WeatherStatus) ? "Escribe el nombre y elige de la lista. Si hay varias con el mismo nombre, añade el país: «Molina, Chile»." : input.WeatherStatus),
        ]);

        var wrows = NcWidgetRows(bar); int enabledCount = wrows.Count(r => r.On);
        var ncPage = new SettingsPage("nc", "Notificaciones", "bell",
        [
            new("Centro de Notificaciones", [
                Bar(25, "Desactivado, el reloj abre el calendario de antes"),
                Bar(26, "Con el permiso de Windows. Solo en memoria: no se guarda en disco"),
                Bar(27, "Desactivado, «×» y «Ocultar» solo las ocultan en WinMac durante la sesión"),
            ], "Las notificaciones que se ven son las que Windows guarda en su centro (Win+N)."),
            new("Widgets", wrows.Select((r, i) => new SettingsRow(RowKind.Toggle, "nc.w:" + r.Kind.ToString().ToLowerInvariant(), NcWidgetNames[r.Kind], null, r.On,
                CanUp: r.On && i > 0, CanDown: r.On && i < enabledCount - 1)).ToList(), "La batería solo aparece en equipos con batería. El Tiempo usa la ciudad del panel del Tiempo."),
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
            new(RowKind.Toggle, "notch.lyrics", "Letras sincronizadas", "Envía título y artista a NetEase para buscar la letra", n.LyricsEnabled, installed, why),
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
        notch.Add(new(null, [new(RowKind.Button, "notch.local", "Rutas y datos de la Isla…", Enabled: installed, Reason: why)]));
        var notchPage = new SettingsPage("notch", "Isla", "island", notch);

        _bar = null;
        return [general, barPage, hub, ncPage, notchPage];
    }
}
