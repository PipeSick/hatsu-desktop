using WinMac.Calendar;
using WinMac.SystemControl;

namespace WinMac.Bar;

/// <summary>Última lectura de los servicios del sistema (la rellena un hilo de trabajo; la interfaz solo lee).</summary>
public sealed record SystemSnapshot(AudioState Audio, WifiState Wifi, BluetoothState Bluetooth, BrightnessState Brightness, PowerModeState Power, AppearanceState Appearance,
    bool OutputSwitchOk, string? OutputSwitchReason, bool CanRestoreOutput, AudioInputState? Input = null, AudioSessionsState? Sessions = null)
{
    public AudioInputState Mic => Input ?? AudioInputState.None("Leyendo…");
    public AudioSessionsState AppSessions => Sessions ?? AudioSessionsState.None("Leyendo…");
    public static SystemSnapshot Empty { get; } = new(AudioState.None("Leyendo…"), WifiState.None("Leyendo…"), BluetoothState.None("Leyendo…"), BrightnessState.None("Leyendo…"),
        PowerModeState.None("Leyendo…"), AppearanceState.None("Leyendo…"), false, "Leyendo…", false);
}

/// <summary>Estado de interfaz de los paneles: mes visible, día elegido, operaciones en curso («Conectando…») y el último error.</summary>
public sealed record PanelContext(SystemSnapshot Sys, BarConfig Cfg, BatteryInfo Battery, DateTime Now, IReadOnlyList<CalendarEvent> Events, int ViewYear, int ViewMonth,
    DateOnly? SelectedDay, IReadOnlyDictionary<string, string> Busy, string? Message, string? ExpandedNet = null, Hub.HubTab Tab = Hub.HubTab.Sound, Hub.HubData? Hub = null, NcView? Nc = null, string? ForegroundApp = null, IReadOnlyList<Tray.TrayIcon>? Tray = null,
    BatteryDetails? BatteryDetail = null, SessionUser? User = null)
{
    public static PanelContext Initial(BarConfig cfg, DateTime now) => new(SystemSnapshot.Empty, cfg, BatteryInfo.None, now, [], now.Year, now.Month, null, new Dictionary<string, string>(), null);
}

/// <summary>WM-009: who is signed in, for the header of the WinMac panel. Name from Windows (display name or user name); the avatar is the initials.</summary>
public sealed record SessionUser(string Name, string Machine)
{
    public string Initials => string.Concat(Name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0]))) is { Length: > 0 } i ? i : "?";
}

/// <summary>
/// Del estado real del equipo a los elementos del panel. Regla de oro: lo que no tiene respaldo se muestra DESHABILITADO con el motivo (o se oculta si el usuario lo apagó en los ajustes);
/// nunca un control que parezca funcionar sin hacerlo.
/// </summary>
public static partial class PanelBuilder
{
    public static PanelModel Build(PanelKind kind, PanelContext c) => kind switch
    {
        PanelKind.Wifi => WifiPanel(c),
        PanelKind.Bluetooth => BluetoothPanel(c),
        PanelKind.Sound => SoundPanel(c),
        PanelKind.Battery => BatteryPanel(c),
        PanelKind.Calendar => CalendarPanel(c),
        PanelKind.Display => DisplayPanel(c),
        PanelKind.Hub => HubPanel(c),
        PanelKind.Weather => WeatherPanel(c),
        PanelKind.Notifications => NotificationCenter(c),
        PanelKind.AppleMenu => AppleMenu(c),
        PanelKind.Tray => TrayMenu(c),
        _ => ControlCenter(c),
    };

    private static string? BusyOr(PanelContext c, string id, string? normal) => c.Busy.TryGetValue(id, out var b) ? b : normal;
    private static void Msg(List<Widget> w, PanelContext c) { if (!string.IsNullOrWhiteSpace(c.Message)) w.Add(new Widget(WidgetKind.Note, "msg", c.Message!)); }
    private static Widget Back(string title) => new(WidgetKind.Header, "back", title, Glyph: "back");

    public const double MenuWidth = 252;

    /// <summary>
    /// Menú de la manzana con aspecto de macOS (antes era el menú nativo de Windows): las mismas acciones reales del sistema, filas compactas, separadores finos y las dos
    /// opciones propias de WinMac al final. «Forzar salida» nombra la app en primer plano al abrir el menú y se atenúa si no hay una que se pueda cerrar.
    /// </summary>
    public static PanelModel AppleMenu(PanelContext c)
    {
        // WM-009: Control Center de WinMac: quién ha iniciado sesión, controles reales, acciones de sesión y energía con confirmación y las opciones del menú de antes.
        var s = c.Sys; var cfg = c.Cfg; var w = new List<Widget>(); int sep = 0;
        void Line() => w.Add(new Widget(WidgetKind.Separator, "apple-sep" + sep++));
        var user = c.User ?? new SessionUser(Environment.UserName, Environment.MachineName);
        w.Add(new Widget(WidgetKind.UserHeader, "apple:user", user.Name, "Sesión en " + user.Machine, Data: new UserHeaderData(user.Initials, "apple:Shutdown")));
        int col = 0;
        void Tile(Widget t) { w.Add(t with { Col = col }); col = 1 - col; }
        Tile(new Widget(WidgetKind.Tile, "open:wifi", "Wi-Fi", !s.Wifi.Available ? "No disponible" : s.Wifi.Connected ? s.Wifi.Ssid : s.Wifi.Reason is not null ? "Requiere ubicación" : "Sin conexión", On: s.Wifi.Available && s.Wifi.Connected, Enabled: s.Wifi.Available, Reason: s.Wifi.Reason, Glyph: "wifi", Bars: s.Wifi.Bars));
        Tile(new Widget(WidgetKind.Tile, "open:bluetooth", "Bluetooth", !s.Bluetooth.Available ? "No disponible" : s.Bluetooth.RadioOn ? (s.Bluetooth.ConnectedCount > 0 ? $"{s.Bluetooth.ConnectedCount} conectado(s)" : "Activado") : "Desactivado", On: s.Bluetooth.Available && s.Bluetooth.RadioOn, Enabled: s.Bluetooth.Available, Reason: s.Bluetooth.Reason, Glyph: "bluetooth"));
        // «No molestar»: Windows no ofrece una API pública para activarlo; el mosaico abre su ajuste (no finge un interruptor)
        Tile(new Widget(WidgetKind.Tile, "open:ms-settings:notifications", "No molestar", "Ajuste de Windows", Glyph: "moon"));
        Tile(new Widget(WidgetKind.Tile, "open:display", "Pantallas", "Duplicar / extender", Glyph: "display"));
        Tile(new Widget(WidgetKind.Tile, "open:battery", "Modo de energía", s.Power.Available ? PowerModeState.Label(s.Power.Mode) : c.Battery.Present ? $"Batería {c.Battery.Percent}%" : "No disponible", Glyph: "power", On: c.Battery.Present && c.Battery.Charging));
        Tile(new Widget(WidgetKind.Tile, "open:ms-settings:", "Configuración", "De Windows", Glyph: "gear"));
        w.Add(new Widget(WidgetKind.Slider, "volume", "", Value: s.Audio.Volume, Enabled: s.Audio.Available, Reason: s.Audio.Reason, Glyph: s.Audio.Muted ? "sound-muted" : "sound"));   // compact: the icon says what it is
        w.Add(new Widget(WidgetKind.Slider, "brightness", "", Value: s.Brightness.Percent, Enabled: s.Brightness.Available, Reason: s.Brightness.Reason, Glyph: "sun"));
        w.Add(new Widget(WidgetKind.ActionStrip, "apple:session", "", Data: new ActionStripData([
            new("apple:Sleep", "moon", "Suspender"), new("apple:Restart", "restart", "Reiniciar"), new("apple:Shutdown", "power", "Apagar"),
            new("apple:LogOff", "logout", "Cerrar sesión"), new("apple:Lock", "lock", "Bloquear")])));
        Line();
        w.Add(new Widget(WidgetKind.MenuItem, "apple:" + SystemActionKind.About, SystemActions.Get(SystemActionKind.About).Label));
        w.Add(new Widget(WidgetKind.MenuItem, "apple:" + SystemActionKind.ForceQuit, c.ForegroundApp is { } app ? $"Forzar salida de «{app}»…" : "Forzar salida…", Enabled: c.ForegroundApp is not null));
        Line();
        w.Add(new Widget(WidgetKind.MenuItem, "apple:reserve", "Reservar espacio arriba", On: c.Cfg.ReserveSpace));
        w.Add(new Widget(WidgetKind.MenuItem, "apple:quitbar", "Salir de la barra superior"));
        Msg(w, c);
        return new PanelModel(PanelKind.AppleMenu, "WinMac", w, AppleWidth, Pad: 10, Spacing: 4);
    }

    public const double AppleWidth = 320;

    public const int TrayColumns = 5;
    /// <summary>Prefijo de la clave de icono de una app en segundo plano (el resto es su HICON); el anfitrión la resuelve con los píxeles del icono.</summary>
    public const string TrayIconPrefix = "tray-icon:";

    /// <summary>
    /// Desplegable de las apps en segundo plano, como el de la flechita de la bandeja de Windows: una cuadrícula de iconos (hasta 5 por fila), con el vidrio de los demás paneles.
    /// Clic = lo mismo que un clic en su icono de la bandeja (abrirla); clic derecho = su menú.
    /// </summary>
    public static PanelModel TrayMenu(PanelContext c)
    {
        const double pad = 6;
        var icons = c.Tray ?? [];
        if (icons.Count == 0) return new PanelModel(PanelKind.Tray, "Apps en segundo plano", [new Widget(WidgetKind.Note, "tray-none", "No hay apps en segundo plano.")], 200, Pad: 8, Spacing: 0);
        var cells = icons.Select(t => new TrayCellData(t.Key, TrayIconPrefix + t.Icon, string.IsNullOrWhiteSpace(t.Tip) ? "App sin nombre" : t.Tip.Split('\n', 2)[0].Trim())).ToList();
        int cols = Math.Min(TrayColumns, cells.Count);
        return new PanelModel(PanelKind.Tray, "Apps en segundo plano", [new Widget(WidgetKind.TrayGrid, "tray-grid", Data: new TrayGridData(cells, cols))], cols * PanelLayout.TrayCell + 2 * pad, Pad: pad, Spacing: 0);
    }

    public static PanelModel ControlCenter(PanelContext c)
    {
        var s = c.Sys; var cfg = c.Cfg; var w = new List<Widget> { new(WidgetKind.Header, "title", "Centro de control") };
        int col = 0;
        void Tile(Widget t) { w.Add(t with { Col = col }); col = 1 - col; }
        if (cfg.CcWifi) Tile(new Widget(WidgetKind.Tile, "open:wifi", "Wi-Fi", BusyOr(c, "wifi", !s.Wifi.Available ? "No disponible" : s.Wifi.Connected ? s.Wifi.Ssid : s.Wifi.Reason is not null ? "Requiere ubicación" : "Sin conexión"), On: s.Wifi.Available && s.Wifi.Connected, Enabled: s.Wifi.Available, Reason: s.Wifi.Reason, Glyph: "wifi", Bars: s.Wifi.Bars));
        if (cfg.CcBluetooth) Tile(new Widget(WidgetKind.Tile, "open:bluetooth", "Bluetooth", !s.Bluetooth.Available ? "No disponible" : s.Bluetooth.RadioOn ? (s.Bluetooth.ConnectedCount > 0 ? $"{s.Bluetooth.ConnectedCount} conectado(s)" : "Activado") : "Desactivado", On: s.Bluetooth.Available && s.Bluetooth.RadioOn, Enabled: s.Bluetooth.Available, Reason: s.Bluetooth.Reason, Glyph: "bluetooth"));
        if (cfg.CcAppearance) Tile(new Widget(WidgetKind.Tile, "appearance", "Modo oscuro", s.Appearance.Available ? (s.Appearance.Dark ? "Activado" : "Desactivado") : "No disponible", On: s.Appearance.Available && s.Appearance.Dark, Enabled: s.Appearance.Available, Reason: s.Appearance.Reason, Glyph: "moon"));
        if (cfg.CcDisplay) Tile(new Widget(WidgetKind.Tile, "open:display", "Pantallas", "Duplicar / extender", Glyph: "display"));
        if (cfg.CcPower) Tile(new Widget(WidgetKind.Tile, "open:battery", c.Battery.Present ? "Batería" : "Energía", c.Battery.Present ? $"{c.Battery.Percent}%" : (s.Power.Available ? PowerModeState.Label(s.Power.Mode) : "No disponible"), Enabled: true, Glyph: "power", On: c.Battery.Present && c.Battery.Charging));
        if (cfg.CcNotifications) Tile(cfg.NotificationCenter
            ? new Widget(WidgetKind.Tile, "nc:open", "Notificaciones", "Centro de WinMac", Glyph: "bell")
            : new Widget(WidgetKind.Tile, "notifications", "Notificaciones", "Abrir centro de Windows", Glyph: "bell"));
        if (col == 1) w.Add(new Widget(WidgetKind.Separator, "pad"));   // número impar de mosaicos: cierra la fila
        if (cfg.CcSound) w.Add(new Widget(WidgetKind.Slider, "volume", "Sonido", Value: s.Audio.Volume, Enabled: s.Audio.Available, Reason: s.Audio.Reason, Glyph: s.Audio.Muted ? "sound-muted" : "sound"));
        if (cfg.CcSound && s.Audio.Available) w.Add(new Widget(WidgetKind.Row, "open:sound", "Salida de audio", s.Audio.Outputs.FirstOrDefault(o => o.IsDefault)?.Name ?? "Dispositivo predeterminado", Glyph: "chevron"));
        if (cfg.CcBrightness) w.Add(new Widget(WidgetKind.Slider, "brightness", BrightnessLabel(s.Brightness), Value: s.Brightness.Percent, Enabled: s.Brightness.Available, Reason: s.Brightness.Reason, Glyph: "sun"));
        Msg(w, c);
        return new PanelModel(PanelKind.ControlCenter, "Centro de control", w);
    }

    /// <summary>WM-016: which screens the slider moves — the laptop's own panel (WMI) or external monitors (DDC/CI) — so it is clear what it controls.</summary>
    public static string BrightnessLabel(BrightnessState b) => b.Monitors.Count switch
    {
        0 => "Brillo",
        1 => b.Monitors[0].Id.StartsWith("wmi:", StringComparison.Ordinal) ? "Brillo · pantalla integrada" : "Brillo · monitor externo (DDC/CI)",
        var n => b.Monitors.Any(m => m.Id.StartsWith("wmi:", StringComparison.Ordinal)) ? $"Brillo · integrada y {n - 1} externa(s)" : $"Brillo · {n} monitores (DDC/CI)",
    };

    public static PanelModel WifiPanel(PanelContext c)
    {
        var s = c.Sys.Wifi; var w = new List<Widget> { Back("Wi-Fi") };
        if (!s.Available) { w.Add(new Widget(WidgetKind.Note, "reason", s.Reason ?? "Wi-Fi no disponible")); w.Add(new Widget(WidgetKind.Button, "open:ms-settings:network", "Abrir configuración de red…")); return new PanelModel(PanelKind.Wifi, "Wi-Fi", w); }
        w.Add(new Widget(WidgetKind.Tile, "wifi-radio", "Wi-Fi", BusyOr(c, "wifi-radio", s.RadioOn is null ? "Estado de la radio desconocido" : s.RadioOn == true ? "Activado" : "Desactivado"),
            On: s.RadioOn == true, Enabled: s.RadioOn is not null, Reason: s.RadioOn is null ? "Windows no informa el estado de la radio" : null, Glyph: "wifi", Bars: s.Bars));
        if (s.RadioOn == false) w.Add(new Widget(WidgetKind.Note, "off", "La radio Wi-Fi está desactivada."));
        else if (s.Reason is not null && s.Networks.Count == 0) { w.Add(new Widget(WidgetKind.Note, "reason", s.Reason)); w.Add(new Widget(WidgetKind.Button, "open:ms-settings:privacy-location", "Abrir permisos de ubicación…")); }
        else
        {
            w.Add(new Widget(WidgetKind.Header, "networks", "Redes", Glyph: "section"));
            if (s.Networks.Count == 0) w.Add(new Widget(WidgetKind.Note, "none", "Buscando redes…"));
            var shown = s.Networks.Take(6).ToList();
            if (c.Cfg.WifiDetails && c.ExpandedNet is { } ex)   // con una red desplegada caben menos filas; la desplegada siempre se ve
            {
                shown = s.Networks.Take(4).ToList();
                if (shown.All(x => x.Ssid != ex) && s.Networks.FirstOrDefault(x => x.Ssid == ex) is { } found) shown.Add(found);
            }
            foreach (var n in shown)   // el panel no se desplaza
            {
                bool details = c.Cfg.WifiDetails, open = details && c.ExpandedNet == n.Ssid;
                string sub = c.Busy.TryGetValue("net:" + n.Ssid, out var b) ? b
                    : details ? (open ? "▾ " : "▸ ") + (n.Connected ? "Conectado" : n.HasProfile ? "Conocida" : n.Secured ? "Con clave" : "Abierta") + (n.BandText is { } bd ? " · " + bd : "")
                    : n.Connected ? "Conectado · toca para desconectar" : n.HasProfile ? "Red conocida" : n.Secured ? "Requiere contraseña: conéctala desde Configuración" : "Abierta: conéctala desde Configuración";
                // Con detalles activados tocar la fila la despliega (acordeón); la acción de conectar/desconectar vive dentro, no se dispara por accidente.
                w.Add(new Widget(WidgetKind.Row, "net:" + n.Ssid, n.Ssid, sub, On: n.Connected, Enabled: details || n.Connected || n.HasProfile, Bars: n.Bars, Glyph: n.Secured ? "lock" : null));
                if (!open) continue;
                w.Add(new Widget(WidgetKind.Note, "d-signal:" + n.Ssid, $"Señal: {n.Signal} %" + (n.RssiDbm is { } r ? $" ({r} dBm)" : "")));
                if (n.BandText is { } band) w.Add(new Widget(WidgetKind.Note, "d-band:" + n.Ssid, $"Banda: {band}"));
                w.Add(new Widget(WidgetKind.Note, "d-sec:" + n.Ssid, "Seguridad: " + (n.Secured ? "protegida" : "abierta") + (n.HasProfile ? " · perfil guardado" : "")));
                if (n.Connected) w.Add(new Widget(WidgetKind.Button, "netact:" + n.Ssid, "Desconectar"));
                else if (n.HasProfile) w.Add(new Widget(WidgetKind.Button, "netact:" + n.Ssid, "Conectar"));
                else w.Add(new Widget(WidgetKind.Note, "d-new:" + n.Ssid, "Red nueva: conéctala desde Configuración de Windows."));
            }
        }

        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:network-wifi", "Abrir configuración de Wi-Fi…"));
        return new PanelModel(PanelKind.Wifi, "Wi-Fi", w);
    }

    public static PanelModel BluetoothPanel(PanelContext c)
    {
        var s = c.Sys.Bluetooth; var w = new List<Widget> { Back("Bluetooth") };
        if (!s.Available) { w.Add(new Widget(WidgetKind.Note, "reason", s.Reason ?? "Bluetooth no disponible")); w.Add(new Widget(WidgetKind.Button, "open:ms-settings:bluetooth", "Abrir configuración de Bluetooth…")); return new PanelModel(PanelKind.Bluetooth, "Bluetooth", w); }
        w.Add(new Widget(WidgetKind.Tile, "bt-radio", "Bluetooth", BusyOr(c, "bt-radio", s.RadioOn ? "Activado" : "Desactivado"), On: s.RadioOn, Glyph: "bluetooth"));
        if (s.RadioOn)
        {
            w.Add(new Widget(WidgetKind.Header, "devices", "Dispositivos emparejados", Glyph: "section"));
            if (s.Devices.Count == 0) w.Add(new Widget(WidgetKind.Note, "none", "No hay dispositivos emparejados."));
            foreach (var d in s.Devices) w.Add(new Widget(WidgetKind.Info, "dev:" + d.Id, d.Name, d.Connected ? "Conectado" : "No conectado", On: d.Connected));
            w.Add(new Widget(WidgetKind.Note, "hint", "Conectar o desconectar un dispositivo concreto solo es posible desde Configuración de Windows."));
        }

        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:bluetooth", "Abrir configuración de Bluetooth…"));
        return new PanelModel(PanelKind.Bluetooth, "Bluetooth", w);
    }

    public static PanelModel SoundPanel(PanelContext c)
    {
        var s = c.Sys.Audio; var w = new List<Widget> { Back("Sonido") };
        w.Add(new Widget(WidgetKind.Slider, "volume", "Volumen", Value: s.Volume, Enabled: s.Available, Reason: s.Reason, Glyph: s.Muted ? "sound-muted" : "sound"));
        if (s.Available)
        {
            w.Add(new Widget(WidgetKind.Row, "mute", "Silenciar", s.Muted ? "Silenciado" : "Con sonido", On: s.Muted));
            w.Add(new Widget(WidgetKind.Header, "outputs", "Salida", Glyph: "section"));
            bool canSwitch = c.Sys.OutputSwitchOk;
            foreach (var o in s.Outputs) w.Add(new Widget(WidgetKind.Row, "out:" + o.Id, o.Name, BusyOr(c, "out:" + o.Id, o.IsDefault ? "Predeterminada" : canSwitch ? "Toca para usar esta salida" : null), On: o.IsDefault, Enabled: canSwitch || o.IsDefault, Glyph: o.IsDefault ? "check" : null));
            if (!canSwitch) w.Add(new Widget(WidgetKind.Note, "noswitch", c.Sys.OutputSwitchReason ?? "No se puede cambiar la salida desde WinMac en este equipo."));
            if (c.Sys.CanRestoreOutput) w.Add(new Widget(WidgetKind.Button, "audio-restore", "Volver a la salida anterior"));
        }

        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:sound", "Abrir configuración de sonido…"));
        return new PanelModel(PanelKind.Sound, "Sonido", w);
    }

    /// <summary>
    /// WM-010: only what Windows and the battery report. Percent, state and Windows' time estimate always; capacities, health and cycles only when the
    /// battery driver gives them (many do not report cycles). Nothing estimated, nothing from the mock-ups.
    /// </summary>
    public static PanelModel BatteryPanel(PanelContext c)
    {
        var b = c.Battery; var p = c.Sys.Power; var d = c.BatteryDetail; var w = new List<Widget> { Back("Energía") };
        if (b.Present)
        {
            w.Add(new Widget(WidgetKind.Info, "battery", $"Batería {b.Percent}%", BatteryText.State(b), On: b.Charging));
            if (!b.Plugged && BatteryText.TimeLeft(b.SecondsLeft) is { } left) w.Add(new Widget(WidgetKind.KeyValue, "bat-left", "Tiempo restante", left.Replace("Quedan ", "")));
            else if (!b.Plugged) w.Add(new Widget(WidgetKind.KeyValue, "bat-left", "Tiempo restante", "Windows lo está calculando"));
            if (d is not null)
            {
                if (d.FullMWh is { } full) w.Add(new Widget(WidgetKind.KeyValue, "bat-full", "Capacidad a plena carga", BatteryText.MWh(full)));
                if (d.DesignMWh is { } design) w.Add(new Widget(WidgetKind.KeyValue, "bat-design", "Capacidad de diseño", BatteryText.MWh(design)));
                if (d.HealthPercent is { } health) w.Add(new Widget(WidgetKind.KeyValue, "bat-health", "Salud (plena carga / diseño)", health + " %"));
                if (d.Cycles is { } cycles) w.Add(new Widget(WidgetKind.KeyValue, "bat-cycles", "Ciclos de carga", cycles.ToString()));
                if (d.RateMW is { } rate) w.Add(new Widget(WidgetKind.KeyValue, "bat-rate", rate > 0 ? "Carga a" : "Consumo", BatteryText.MWh(Math.Abs(rate)).Replace(" Wh", " W")));
                if (d.Cycles is null || d.HealthPercent is null) w.Add(new Widget(WidgetKind.Note, "bat-missing", "La batería no informa de " + (d.Cycles is null && d.HealthPercent is null ? "sus ciclos ni de su capacidad" : d.Cycles is null ? "sus ciclos" : "su capacidad de diseño") + "."));
            }
            else w.Add(new Widget(WidgetKind.Note, "bat-reading", "Leyendo los datos de la batería…"));
        }
        else w.Add(new Widget(WidgetKind.Info, "battery", "Sin batería", "Equipo de sobremesa o batería no detectada"));
        w.Add(new Widget(WidgetKind.Header, "modes", "Modo de energía", Glyph: "section"));
        foreach (var m in Enum.GetValues<PowerMode>())
            w.Add(new Widget(WidgetKind.Row, "power:" + m, PowerModeState.Label(m), BusyOr(c, "power:" + m, null), On: p.Available && p.Mode == m, Enabled: p.Available, Reason: p.Reason, Glyph: p.Available && p.Mode == m ? "check" : null));
        if (!p.Available) w.Add(new Widget(WidgetKind.Note, "reason", p.Reason ?? "El modo de energía no está disponible"));
        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:batterysaver", "Abrir configuración de batería…"));
        return new PanelModel(PanelKind.Battery, "Energía", w);
    }

    public static PanelModel DisplayPanel(PanelContext c)
    {
        var w = new List<Widget> { Back("Pantallas") };
        w.Add(new Widget(WidgetKind.Row, "topology:Duplicate", "Duplicar pantallas", "Misma imagen en todas"));
        w.Add(new Widget(WidgetKind.Row, "topology:Extend", "Extender escritorio", "Cada pantalla con su espacio"));
        w.Add(new Widget(WidgetKind.Row, "topology:InternalOnly", "Solo pantalla principal"));
        w.Add(new Widget(WidgetKind.Row, "topology:ExternalOnly", "Solo segunda pantalla"));
        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:display", "Abrir configuración de pantalla…"));
        return new PanelModel(PanelKind.Display, "Pantallas", w);
    }

    public static PanelModel CalendarPanel(PanelContext c)
    {
        string lang = c.Cfg.Language; var today = DateOnly.FromDateTime(c.Now);
        var days = CalendarGrid.Month(c.ViewYear, c.ViewMonth, today, c.Events);
        var w = new List<Widget>
        {
            new(WidgetKind.Nav, "nav", CalendarGrid.MonthTitle(c.ViewYear, c.ViewMonth, lang)),
            new(WidgetKind.Month, "month", Data: new MonthData(days, CalendarGrid.WeekdayHeaders(lang), c.SelectedDay)),
        };
        var sel = c.SelectedDay ?? today;
        var evs = CalendarGrid.EventsOn(c.Events, sel);
        w.Add(new Widget(WidgetKind.Header, "events", sel == today ? "Hoy" : sel.ToString("dd/MM/yyyy"), Glyph: "section"));
        if (string.IsNullOrWhiteSpace(c.Cfg.CalendarIcsPath)) w.Add(new Widget(WidgetKind.Note, "noics", "Para ver eventos, indica un archivo .ics en Ajustes de WinMac."));
        else if (evs.Count == 0) w.Add(new Widget(WidgetKind.Note, "noevents", "Sin eventos."));
        foreach (var e in evs.Take(5)) w.Add(new Widget(WidgetKind.Info, "ev:" + e.Title + e.Start.Ticks, e.Title, e.AllDay ? "Todo el día" : e.Start.ToString("HH:mm") + (e.End > e.Start ? "–" + e.End.ToString("HH:mm") : "")));
        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:dateandtime", "Abrir configuración de fecha y hora…"));
        return new PanelModel(PanelKind.Calendar, "Calendario", w);
    }
}
