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
public sealed record SettingsInput(HostConfig Cfg, IReadOnlyDictionary<string, ModuleState> States, bool Autostart, string WeatherStatus = "")
{
    /// <summary>Installed extensions with their live status (docs/30). Null: the page says extensions are not available.</summary>
    public IReadOnlyList<(Extensions.InstalledExtension Ext, string Status)>? Extensions { get; init; }
}

/// <summary>Construye las páginas de Ajustes a partir de la configuración vigente. Puro: se prueba sin Windows.</summary>
public static class SettingsBuilder
{
    /// <summary>Opciones booleanas de la Barra: (campo del parche, texto, lectura). Un solo lugar; la ventana las pinta y aplica por tabla.</summary>
    public static readonly (string Field, string Text, Func<BarConfig, bool> Get)[] BarToggles =
    [
        ("showNetwork", "Wi-Fi / red", b => b.ShowNetwork), ("showVolume", "Volumen", b => b.ShowVolume), ("showBattery", "Batería", b => b.ShowBattery), ("showBatteryPercent", "Porcentaje de batería", b => b.ShowBatteryPercent),
        ("showBluetooth", "Bluetooth", b => b.ShowBluetooth), ("showControlCenter", "Control Center", b => b.ShowControlCenter), ("showSeconds", "Segundos en el reloj", b => b.ShowSeconds), ("showDate", "Fecha junto a la hora", b => b.ShowDate),
        ("use24Hour", "Reloj de 24 horas", b => b.Use24Hour), ("reserveSpace", "Reservar el borde superior", b => b.ReserveSpace), ("confirmDestructive", "Confirmar reiniciar, apagar y cerrar sesión", b => b.ConfirmDestructive),
        ("wifiDetails", "Detalle de Wi-Fi al tocar una red", b => b.WifiDetails), ("appMenus", "Menús de la app activa en la barra", b => b.AppMenus),
        ("ccWifi", "Wi-Fi", b => b.CcWifi), ("ccBluetooth", "Bluetooth", b => b.CcBluetooth), ("ccSound", "Sonido", b => b.CcSound), ("ccBrightness", "Brillo", b => b.CcBrightness),
        ("ccPower", "Energía", b => b.CcPower), ("ccAppearance", "Apariencia", b => b.CcAppearance), ("ccDisplay", "Pantallas", b => b.CcDisplay), ("ccNotifications", "Notificaciones", b => b.CcNotifications),
        ("showHub", "Icono de WinMac (panel con pestañas)", b => b.ShowHub), ("showWeather", "Icono del Tiempo", b => b.ShowWeather), ("weatherFahrenheit", "Grados Fahrenheit", b => b.WeatherFahrenheit),
        ("weatherAutoLocation", "Usar mi ubicación", b => b.WeatherAutoLocation),
        ("notificationCenter", "El reloj abre el Centro de Notificaciones", b => b.NotificationCenter), ("notificationsFromWindows", "Mostrar notificaciones de Windows", b => b.NotificationsFromWindows),
        ("notifRemoveFromWindows", "Borrar también en Windows", b => b.NotifRemoveFromWindows),
        ("showTrayIcons", "Iconos de apps en segundo plano", b => b.ShowTrayIcons),
        ("launchpadOnWinKey", "La tecla Windows abre Launchpad", b => b.LaunchpadOnWinKey),
        ("spotlightFiles", "Buscar también archivos", b => b.SpotlightFiles),
        ("captureHotkeys", "Atajos Ctrl+Mayús+3 / 4 / 5", b => b.CaptureHotkeys),
        ("captureCopyToClipboard", "Copiar también al portapapeles", b => b.CaptureCopyToClipboard),
    ];
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
            new("Comportamiento", [Bar(9, "El resto de ventanas no se superpone a la barra"), Bar(10), Bar(11), Bar(12, "Apps con menú de Windows o accesible; los menús originales siguen en la app")]),
            new("Launchpad", [Bar(29, "Solo al pulsarla sola; los atajos con Windows siguen igual. Ctrl+Esc abre el menú Inicio")], "Desactivado, la tecla Windows vuelve a abrir el menú Inicio."),
            new("Capturas", [
                Bar(31, "Ctrl+Mayús+3: pantalla · 4: región · 5: barra de captura · 6: grabar/detener. Si otro programa ya los usa, Windows los rechaza (queda en bar.log)"),
                new(RowKind.Segmented, "bar.capfmt", "Formato", Options: CaptureFormatNames, Selected: Index(CaptureFormatValues, bar.CaptureFormat)),
                Bar(32),
                new(RowKind.Field, "bar.capdir", "Carpeta", Text: bar.CaptureFolder ?? "", Placeholder: "Imágenes\\Capturas de pantalla"),
            ], "Las capturas se guardan en esa carpeta y aparecen en la galería de la Isla. Las grabaciones (solo vídeo, nunca el micrófono) van a Vídeos\\Screen Recordings."),
            new("Spotlight Pro y espacios de trabajo", [
                new(RowKind.Info, "bar.spotpro", "En Spotlight: acciones de WinMac, «ip», «red», «ping …» y «dns …» (solo con Intro), «ssh usuario@equipo», «> …» (nunca se ejecuta: abre la terminal o lo copia), «20% de 150»."),
                new(RowKind.Info, "bar.scripts", "Scripts propios: %APPDATA%\\WinMac\\spotlight-scripts.json. Se ejecutan con doble Intro y tus permisos, nunca elevados."),
                new(RowKind.Info, "bar.workspaces", "Espacios de trabajo: «Guardar espacio de trabajo», «Restaurar …», «Ventanas lado a lado» o «en cuadrícula». Se guardan en %APPDATA%\\WinMac\\workspaces.json; ninguna app se abre sola."),
            ]),
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
        notch.Add(new("Portapapeles", [
            new(RowKind.Field, "notch.cliplimit", "Elementos en el historial", Enabled: installed, Reason: why, Text: n.ClipboardLimit.ToString(), Placeholder: "50"),
            new(RowKind.Toggle, "notch.clipsecrets", "No guardar contraseñas ni claves", "Detecta claves privadas, tokens y contraseñas generadas; ignora siempre a los gestores de contraseñas", n.ClipboardSkipSecrets, installed, why),
            new(RowKind.Toggle, "notch.clippersist", "Conservar al reiniciar", n.ClipboardPersist ? "Activado: el historial se guarda cifrado para tu usuario de Windows (DPAPI)" : "Desactivado: nada del historial se guarda en disco", n.ClipboardPersist, installed, why),
            new(RowKind.Toggle, "notch.clipexcl", "Ignorar las apps de la lista", "Lo que copien no entra en el historial (se identifican por el nombre de su proceso)", n.ClipboardExclusionsOn, installed, why),
            new(RowKind.Field, "notch.clipapps", "Apps excluidas", Enabled: installed && n.ClipboardExclusionsOn, Reason: !installed ? why : "Activa «Ignorar las apps de la lista»", Text: string.Join(", ", NotchSettings.ParseApps(n.ClipboardExcludedApps)), Placeholder: "KeePass, 1Password, … (separadas por comas; vacío = lista por defecto)"),
            new(RowKind.Button, "notch.clipclear", "Borrar el historial del portapapeles", Enabled: installed, Reason: why),
        ], "Solo en este equipo: el historial nunca se sincroniza ni se registra."));
        notch.Add(new("Agentes de IA", [
            new(RowKind.Toggle, "notch.agents", "Agentes de IA", "Estado de tus agentes en el notch", n.AgentsEnabled, installed, why),
            new(RowKind.Field, "notch.poll", "Actualizar cada (s)", Enabled: installed, Reason: why, Text: n.PollIntervalSeconds.ToString(), Placeholder: "30"),
        ]));
        notch.Add(new("Pestañas", rows, "Activa, oculta y ordena las pestañas del notch."));
        notch.Add(new(null, [new(RowKind.Button, "notch.local", "Rutas y datos de la Isla…", Enabled: installed, Reason: why)]));
        var notchPage = new SettingsPage("notch", "Isla", "island", notch);

        _bar = null;
        return [general, barPage, hub, ncPage, notchPage, ExtensionsPage(input.Extensions)];
    }

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
        groups.Add(new("Instaladas", rows, "Cada extensión es un programa aparte: si falla, WinMac sigue funcionando. Solo puede hacer lo que indican sus permisos; se ejecuta con tus permisos de usuario (no es un entorno aislado para código desconocido: instala solo extensiones en las que confíes)."));
        groups.Add(new("Instalar", [new(RowKind.Field, "ext.install", "Carpeta de la extensión", Enabled: available, Reason: available ? null : unavailable, Text: "", Placeholder: "C:\\…\\mi-extension (con extension.json)")], "Se copia a la carpeta de extensiones de WinMac y queda desactivada hasta que la actives."));
        return new SettingsPage("ext", "Extensiones", "puzzle", groups);
    }
}
