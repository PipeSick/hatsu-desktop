namespace WinMac.Host;

/// <summary>
/// Organización de Ajustes (propuesta A): las secciones se agrupan en cinco bloques con nombre por lo que haces. Se parte de las páginas que construye <see cref="SettingsBuilder.Build"/> y solo se
/// reordenan, se renombran y se juntan: ninguna fila cambia de identificador ni se pierde.
/// </summary>
public static partial class SettingsBuilder
{
    private static readonly Dictionary<string, string> Subtitles = new()
    {
        ["general"] = "Configura el comportamiento general de Hatsu y sus componentes.",
        ["bar"] = "Elementos, indicadores y comportamiento de la barra superior.",
        ["dock"] = "Acceso rápido a tus aplicaciones y ventanas.",
        ["notch"] = "Centro contextual con tus actividades, controles y avisos.",
        ["modules"] = "Activa, desactiva y configura las funciones adicionales de Hatsu.",
        ["gestures"] = "Esquinas activas, botones del ratón y atajos de teclado.",
        ["spot"] = "Búsqueda rápida de aplicaciones, archivos y acciones.",
        ["nc"] = "Centro de notificaciones y historial del portapapeles.",
        ["capture"] = "Capturas de pantalla y grabación: atajos, formato y destino.",
        ["weather"] = "Ubicación, unidades y dónde se muestra el tiempo.",
        ["connect"] = "Batería y notificaciones del iPhone por Bluetooth, sin apps adicionales.",
        ["periph"] = "Teclados, ratones y auriculares del PC: batería y avisos.",
        ["ext"] = "Extensiones, diagnóstico y herramientas de recuperación.",
    };

    // ── Settings V2: composición de cada página (vistas previas, galería de módulos y columnas). Solo añade filas de presentación o duplica interruptores con otro id («card:…» → misma acción);
    //    ninguna fila real se quita ni cambia de id.
    private static readonly HashSet<string> HalfPages = ["bar", "dock", "notch", "weather", "capture"];

    private static (string, string, List<string>) R(string t, string s, params string[] chips) => (t, s, [.. chips]);

    private static string ConnectionText(WinMac.Bar.Notch.DeviceInfo d) => d.Connection switch { "bluetooth" => "Bluetooth", "usb" => "USB", "dongle" => "Receptor 2,4 GHz", _ => "Conexión desconocida" } + (d.Type switch { "mouse" => " · Ratón", "keyboard" => " · Teclado", "headset" => " · Auriculares", "gamepad" => " · Mando", _ => "" });

    private static IReadOnlyList<SettingsPage> Compose(List<SettingsPage> pages)
    {
        SettingsRow? Find(string id) => pages.SelectMany(p => p.Groups).SelectMany(g => g.Rows).FirstOrDefault(r => r.Id == id);
        static string After(SettingsRow? r) { var t = r?.Title ?? ""; int i = t.LastIndexOf(": ", StringComparison.Ordinal); return i >= 0 ? t[(i + 2)..] : "Nada"; }
        var result = new List<SettingsPage>();
        foreach (var page in pages)
        {
            var groups = page.Groups.ToList();
            switch (page.Id)
            {
                case "bar" when Find("bar.preview") is { } bp:
                {   // la vista previa pasa a su propio grupo (ancho completo) para que las demás secciones se repartan en dos columnas
                    groups = groups.Select(g => g.Rows.Contains(bp) ? g with { Rows = g.Rows.Where(r => r != bp).ToList() } : g).ToList();
                    groups.Insert(0, new SettingsGroup(null, [bp]));
                    break;
                }
                case "dock":
                {
                    var fine = Find("dock.fine"); var mag = Find("dock.magnify"); var zr = Find("dock.zoom"); int zoom10 = zr is null ? 17 : (int)Math.Round((zr.SliderMin + zr.Value / 100 * (zr.SliderMax - zr.SliderMin)) * 10);
                    if (fine is not null) groups.Insert(0, new SettingsGroup(null, [new SettingsRow(RowKind.Preview, "dock.preview", "Vista previa del Dock", Value: fine?.Value ?? 50, On: mag?.On ?? true, Selected: zoom10, SliderMin: fine?.SliderMin ?? 32, SliderMax: fine?.SliderMax ?? 96, Unit: fine?.Unit)]));
                    break;
                }

                case "notch" when Find("notch.pos") is { Enabled: true }:
                    groups.Insert(0, new SettingsGroup(null, [new SettingsRow(RowKind.Preview, "notch.preview", "Vista previa de la Isla", Options: ["Música", "Descarga", "Temporizador", "Notificación"], Text: Find("notch.pos")?.Options is { } po && Find("notch.pos") is { } pr && pr.Selected < po.Count ? po[pr.Selected] : "Centro")]));
                    break;
                case "gestures":
                    groups.Insert(0, new SettingsGroup(null, [new SettingsRow(RowKind.Preview, "gest.preview", "Esquinas activas", Options: [After(Find("gest.pick:gestCornerTL")), After(Find("gest.pick:gestCornerTR")), After(Find("gest.pick:gestCornerBL")), After(Find("gest.pick:gestCornerBR"))], On: Find("bar:gesturesEnabled")?.On ?? false)]));
                    break;
                case "connect":
                {
                    bool on = Find("mod:connect")?.On ?? false; var ph = _devices?.Phone; string? nm = string.IsNullOrWhiteSpace(ph?.DeviceName) ? null : ph!.DeviceName;
                    string battery = ph?.Battery is { } b && ph.BatteryAtUtc is { } at && DateTime.UtcNow - at < TimeSpan.FromMinutes(15) ? $"Batería {b} %" : ph?.Battery is { } old ? $"Última lectura {old} %" : "Batería sin lectura";
                    (string t, string s, List<string> chips) = !on ? R("Hatsu Connect desactivado", "Actívalo para ver aquí tu iPhone: batería y notificaciones por Bluetooth, sin apps adicionales.", "Desactivado")
                        : ph is null ? R("Esperando a Hatsu Connect", "Todavía no hay información del módulo.", "Iniciando")
                        : ph.Status switch
                        {
                            WinMac.Connect.PhoneLinkStatus.NoDevice => R("Sin iPhone emparejado", "Empareja tu iPhone en la configuración de Bluetooth de Windows (la guía está más abajo).", "Sin emparejar"),
                            WinMac.Connect.PhoneLinkStatus.Connecting => R(nm ?? "iPhone", "Conectando…", "Conectando"),
                            WinMac.Connect.PhoneLinkStatus.Connected => R(nm ?? "iPhone", "Conectado por Bluetooth", "Conectado", battery),
                            WinMac.Connect.PhoneLinkStatus.Disconnected => R(nm ?? "iPhone", "Fuera de alcance, apagado o con el Bluetooth desactivado.", "Desconectado", battery),
                            WinMac.Connect.PhoneLinkStatus.NoBluetooth => R("Bluetooth no disponible", "El Bluetooth del PC está apagado o no existe.", "Sin Bluetooth"),
                            _ => R(nm ?? "iPhone", string.IsNullOrWhiteSpace(ph.Message) ? "No se pudo conectar." : ph.Message!, "Error de conexión"),
                        };
                    if (on && ph is { NotificationsStatus: WinMac.Connect.PhoneFeatureStatus.NotAuthorized }) chips = [.. chips, "Permiso de notificaciones pendiente en el iPhone"];
                    groups.Insert(0, new SettingsGroup(null, [new SettingsRow(RowKind.Hero, "iphone.card", t, s, Options: chips, Icon: "phone")]));
                    break;
                }

                case "periph":
                {
                    var devs = _devices?.Devices ?? []; bool periphOn = Find("bar:showPeripherals")?.On ?? false;
                    if (devs.Count > 0)
                        groups.Insert(0, new SettingsGroup("Dispositivos detectados", devs.Select((d, i) => new SettingsRow(RowKind.Device, "periph.dev:" + i, d.Name, ConnectionText(d), Value: d.Percent ?? -1, On: d.Charging, Enabled: d.Connected, Icon: d.Type,
                            Text: !d.Connected ? "Desconectado" : d.Percent is { } p ? p + " %" + (d.Charging ? " · Cargando" : "") : "Sin dato de batería")).ToList(), "Solo se muestra lo que el dispositivo informa. Sin dato de batería no se pone ningún porcentaje.", Cards: true));
                    else groups.Insert(0, new SettingsGroup("Dispositivos detectados", [new SettingsRow(RowKind.Info, "periph.none", !periphOn ? "Activa «Icono de Periféricos en la barra» (más abajo) para que Hatsu lea tus dispositivos; apagado no se lee nada." : _devices is { DevicesReceived: true } ? "No se detectó ningún periférico (o están ocultos)." : "Esperando la primera lectura de la Barra…")]));
                    break;
                }

                case "modules":
                {
                    (string Id, string Icon, string Desc)[] mods =
                    [
                        ("mod:bar", "bar", "Reloj, indicadores y el panel de Hatsu."), ("mod:dock", "dock", "Apps abiertas y ancladas, con vista previa."), ("mod:notch", "island", "Música, actividades y avisos en la parte superior."),
                        ("mod:connect", "phone", "Batería y notificaciones del iPhone por Bluetooth."), ("bar:mcEnabled", "grid", "Vista general de ventanas y escritorios."), ("bar:snapEnabled", "snap", "Ajusta ventanas a zonas al arrastrarlas."),
                        ("bar:liveTextEnabled", "text", "Reconoce y copia texto de cualquier parte de la pantalla."), ("bar:spotActionsEnabled", "search", "Acciones de Hatsu desde Spotlight."),
                        ("bar:gesturesEnabled", "gesture", "Esquinas activas, botones del ratón y atajos."), ("shelf.enabled", "shelf", "Estantería de archivos, texto e imágenes en la Isla."),
                    ];
                    var cards = mods.Select(m => Find(m.Id) is { } r ? r with { Id = "card:" + m.Id, Icon = m.Icon, Subtitle = m.Desc, CanUp = false, CanDown = false } : null).Where(r => r is not null).Select(r => r!).ToList();
                    int active = cards.Count(c => c.On && c.Enabled);
                    if (cards.Count > 0) groups.Insert(0, new SettingsGroup($"{cards.Count} módulos · {active} activos · {cards.Count - active} desactivados", cards, "Cada módulo es un proceso o función aparte. Su configuración está más abajo.", Cards: true));
                    break;
                }
            }

            if (HalfPages.Contains(page.Id)) groups = groups.Select(g => g.Rows.Count == 1 && g.Rows[0].Kind == RowKind.Preview && g.Rows[0].Id != "bar.preview" || g.Rows.Any(r => r.Kind == RowKind.Preview) ? g : g with { Half = true }).ToList();
            // Como en los mockups: arriba de Barra, Dock e Isla, el interruptor del módulo (misma acción que el de Módulos y General).
            (string Mod, string Title, string Desc)? head = page.Id switch
            {
                "bar" => ("mod:bar", "Activar Barra superior", "Mostrar la barra en la parte superior de la pantalla"),
                "dock" => ("mod:dock", "Activar Dock", "Mostrar el Dock con tus apps abiertas y ancladas"),
                "notch" => ("mod:notch", "Activar Isla (Hatsu Notch)", "Mostrar la isla en la parte superior de la pantalla"),
                _ => null,
            };
            if (head is { } h && Find(h.Mod) is { } modRow)
                groups.Insert(0, new SettingsGroup(null, [modRow with { Id = "hdr:" + h.Mod, Title = h.Title, Subtitle = h.Desc, CanUp = false, CanDown = false }]));
            result.Add(page with { Groups = groups });
        }

        return result;
    }

    internal static IReadOnlyList<SettingsPage> Organize(List<SettingsPage> pages)
    {
        SettingsPage? Get(string id) => pages.FirstOrDefault(p => p.Id == id);
        var result = new List<SettingsPage>();
        void Add(SettingsPage? p, string? section, string? title = null, string? glyph = null) { if (p is null) return; result.Add(p with { Section = section, Title = title ?? p.Title, Glyph = glyph ?? p.Glyph }); }

        // Atajos y gestos: la tarjeta de Gestures sale de Módulos a su propia página (es lo que más se busca).
        var modules = Get("modules"); SettingsPage? gestures = null;
        if (modules is not null)
        {
            var g = modules.Groups.FirstOrDefault(x => x.Rows.Any(r => r.Id == "bar:gesturesEnabled"));
            if (g is not null) { gestures = new SettingsPage("gestures", "Atajos y gestos", "gear", [g]); modules = modules with { Groups = modules.Groups.Where(x => x != g).ToList() }; }
        }

        // iPhone y periféricos: el grupo de Periféricos sale de la Barra superior y se junta con Hatsu Connect.
        var bar = Get("bar"); var connect = Get("connect"); SettingsGroup? periph = null;
        if (bar is not null)
        {
            periph = bar.Groups.FirstOrDefault(x => x.Header == "Periféricos");
            if (periph is not null) bar = bar with { Groups = bar.Groups.Where(x => x != periph).ToList() };
        }

        // Settings V2: iPhone y Periféricos son páginas independientes (las filas conservan sus ids; solo cambian de página).
        SettingsPage? periphPage = periph is null ? null : new SettingsPage("periph", "Periféricos", "periph", [periph with { Header = null }]);

        // Notificaciones y portapapeles: dos páginas pequeñas pasan a una.
        var nc = Get("nc"); var clip = Get("clip"); SettingsPage? both = null;
        if (nc is not null && clip is not null)
        {
            var clipGroups = clip.Groups.Select((g, i) => i == 0 && string.IsNullOrEmpty(g.Header) ? g with { Header = "Portapapeles" } : g).ToList();
            both = new SettingsPage("nc", "Notificaciones y portapapeles", "bell", [.. nc.Groups, .. clipGroups]);
        }

        Add(Get("general"), null);
        Add(bar, "Escritorio"); Add(Get("dock"), "Escritorio", "Dock"); Add(Get("notch"), "Escritorio", "Isla (Hatsu Notch)");
        Add(modules, "Funciones"); Add(gestures, "Funciones"); Add(Get("spot"), "Funciones", "Búsqueda (Spotlight)");
        Add(both ?? nc, "Contenido"); Add(Get("capture"), "Contenido"); Add(Get("weather"), "Contenido", "Clima");
        Add(connect, "Dispositivos", "iPhone (Hatsu Connect)", "phone"); Add(periphPage, "Dispositivos");
        Add(Get("ext"), "Avanzado", "Extensiones y diagnóstico");
        foreach (var p in pages.Where(p => result.All(r => r.Id != p.Id) && p.Id != "clip")) result.Add(p with { Section = "Avanzado" });   // cualquier página nueva no se pierde
        // Diagnóstico: los dos botones salen de General y viven en «Extensiones y diagnóstico» (mismo id, misma acción).
        string[] diagIds = ["diagnose.run", "logs.open"];
        var diagRows = result.SelectMany(p => p.Groups).SelectMany(g => g.Rows).Where(r => diagIds.Contains(r.Id)).ToList();
        if (diagRows.Count > 0 && result.Any(p => p.Id == "ext"))
            for (int i = 0; i < result.Count; i++)
            {
                var pg = result[i];
                if (pg.Id == "ext") result[i] = pg with { Groups = [.. pg.Groups, new SettingsGroup("Diagnóstico", diagRows, "El informe se guarda en tu equipo y no se envía nada.")] };
                else if (pg.Groups.Any(g => g.Rows.Any(r => diagIds.Contains(r.Id))))
                    result[i] = pg with { Groups = pg.Groups.Select(g => g with { Rows = g.Rows.Where(r => !diagIds.Contains(r.Id)).ToList() }).Where(g => g.Rows.Count > 0).ToList() };
            }
        return Compose(result.Select(p => p with { Subtitle = p.Subtitle ?? Subtitles.GetValueOrDefault(p.Id) }).ToList());
    }
}
