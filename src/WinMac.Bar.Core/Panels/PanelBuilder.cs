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
    BatteryDetails? BatteryDetail = null, SessionUser? User = null, bool BatteryDetailsOpen = false)
{
    public static PanelContext Initial(BarConfig cfg, DateTime now) => new(SystemSnapshot.Empty, cfg, BatteryInfo.None, now, [], now.Year, now.Month, null, new Dictionary<string, string>(), null);
}

/// <summary>WM-009: who is signed in, for the header of the WinMac panel. Name from Windows (display name or user name); the avatar is the initials.</summary>
/// <param name="PictureKey">Icon key of the Windows account picture (64×64) when it could be read; null → initials.</param>
public sealed record SessionUser(string Name, string Machine, string? PictureKey = null)
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
        // WM-022: UN solo Centro de Control. Cabecera con la cuenta, seis controles reales, volumen y brillo en una tarjeta, las cinco acciones de energía
        // grandes y un pie compacto. Las opciones del menú antiguo que eran ajustes (reservar espacio, salir de la barra) viven en Ajustes.
        var s = c.Sys; var w = new List<Widget>();
        var user = c.User ?? new SessionUser(Environment.UserName, Environment.MachineName);
        w.Add(new Widget(WidgetKind.UserHeader, "apple:user", user.Name, "Sesión iniciada en " + user.Machine, Data: new UserHeaderData(user.Initials, "open:ms-settings:yourinfo", user.PictureKey)));
        int col = 0;
        void Tile(Widget t) { w.Add(t with { Col = col }); col = 1 - col; }
        Tile(new Widget(WidgetKind.Tile, "open:wifi", "Wi-Fi", !s.Wifi.Available ? "No disponible" : s.Wifi.Connected ? s.Wifi.Ssid : s.Wifi.Reason is not null ? "Requiere ubicación" : "Sin conexión", On: s.Wifi.Available && s.Wifi.Connected, Enabled: s.Wifi.Available, Reason: s.Wifi.Reason, Glyph: "wifi", Bars: s.Wifi.Bars));
        Tile(new Widget(WidgetKind.Tile, "open:bluetooth", "Bluetooth", !s.Bluetooth.Available ? "No disponible" : s.Bluetooth.RadioOn ? (s.Bluetooth.ConnectedCount > 0 ? $"{s.Bluetooth.ConnectedCount} conectado(s)" : "Activado") : "Desactivado", On: s.Bluetooth.Available && s.Bluetooth.RadioOn, Enabled: s.Bluetooth.Available, Reason: s.Bluetooth.Reason, Glyph: "bluetooth"));
        // «No molestar»: Windows no ofrece una API pública para activarlo; el mosaico abre su ajuste (no finge un interruptor)
        Tile(new Widget(WidgetKind.Tile, "open:ms-settings:notifications", "No molestar", "Abrir en Windows", Glyph: "moon"));
        // 3.2: acción real (cambia el tema de las apps y de Windows, como «Elige tu modo»); muestra el estado leído, también si se cambió fuera de WinMac
        Tile(new Widget(WidgetKind.Tile, "appearance", "Modo oscuro", AppearanceText(s.Appearance), On: s.Appearance.Available && s.Appearance.Dark, Enabled: s.Appearance.Available, Reason: s.Appearance.Reason, Glyph: "appearance"));
        Tile(new Widget(WidgetKind.Tile, "open:display", "Pantallas", "Duplicar o extender", Glyph: "display"));
        Tile(new Widget(WidgetKind.Tile, "open:battery", "Energía", s.Power.Available ? PowerModeState.Label(s.Power.Mode) : c.Battery.Present ? $"Batería {c.Battery.Percent}%" : "No disponible", Glyph: "power", On: c.Battery.Present && c.Battery.Charging));
        Tile(new Widget(WidgetKind.Tile, "open:ms-settings:", "Configuración", "De Windows", Glyph: "gear"));
        int cardFrom = w.Count;
        w.Add(new Widget(WidgetKind.Slider, "volume", "Sonido", Value: s.Audio.Volume, Enabled: s.Audio.Available, Reason: s.Audio.Reason, Glyph: s.Audio.Muted ? "sound-muted" : "sound"));
        w.Add(new Widget(WidgetKind.Slider, "brightness", "Brillo", Value: s.Brightness.Percent, Enabled: s.Brightness.Available, Reason: s.Brightness.Reason, Glyph: "sun"));
        int cardTo = w.Count - 1;
        w.Add(new Widget(WidgetKind.ActionStrip, "apple:session", "", Data: new ActionStripData([
            new("apple:Sleep", "moon", "Suspender"), new("apple:Restart", "restart", "Reiniciar"), new("apple:Shutdown", "onoff", "Apagar"),
            new("apple:LogOff", "logout", "Cerrar sesión"), new("apple:Lock", "lock", "Bloquear")])));
        w.Add(new Widget(WidgetKind.LinkRow, "apple:links", "", Data: new LinkRowData([
            new("apple:winmac-settings", "Ajustes de WinMac", true),
            new("apple:" + SystemActionKind.About, "Acerca de este PC", true),
            new("apple:" + SystemActionKind.ForceQuit, "Forzar salida…", c.ForegroundApp is not null)])));
        Msg(w, c);
        return new PanelModel(PanelKind.AppleMenu, "WinMac", w, AppleWidth, Card: (cardFrom, cardTo), Pad: 12, Spacing: 8);
    }

    public const double AppleWidth = 372;

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
        // WM-023: WinMac only sees the icons apps register while it runs (or again when told «TaskbarCreated»); some apps never re-register and Windows does not
        // let a third party read Explorer's list without reading its memory. The last row opens Windows' own overflow, so no icon is out of reach.
        var winTray = new Widget(WidgetKind.Row, "open:windows-tray", "Ver la bandeja de Windows", "Incluye las apps que no se registraron en WinMac", Glyph: "chevron");
        if (icons.Count == 0) return new PanelModel(PanelKind.Tray, "Apps en segundo plano", [new Widget(WidgetKind.Note, "tray-none", "WinMac aún no ha recibido iconos de apps."), winTray], 260, Pad: 8, Spacing: 2);
        var cells = icons.Select(t => new TrayCellData(t.Key, TrayIconPrefix + t.Icon, string.IsNullOrWhiteSpace(t.Tip) ? "App sin nombre" : t.Tip.Split('\n', 2)[0].Trim())).ToList();
        int cols = Math.Min(TrayColumns, cells.Count);
        // WM-004: title like the mock-up, a bit more room per icon; the panel keeps WinMac's glass and the apps' own actions (click = open, right click = their menu)
        return new PanelModel(PanelKind.Tray, "Apps en segundo plano", [new Widget(WidgetKind.Caption, "tray-title", "Apps en segundo plano"), new Widget(WidgetKind.TrayGrid, "tray-grid", Data: new TrayGridData(cells, cols)), winTray], Math.Max(260, cols * PanelLayout.TrayCell + 2 * pad), Pad: pad, Spacing: 2);
    }

    public static PanelModel ControlCenter(PanelContext c)
    {
        var s = c.Sys; var cfg = c.Cfg; var w = new List<Widget> { new(WidgetKind.Header, "title", "Centro de control") };
        int col = 0;
        void Tile(Widget t) { w.Add(t with { Col = col }); col = 1 - col; }
        if (cfg.CcWifi) Tile(new Widget(WidgetKind.Tile, "open:wifi", "Wi-Fi", BusyOr(c, "wifi", !s.Wifi.Available ? "No disponible" : s.Wifi.Connected ? s.Wifi.Ssid : s.Wifi.Reason is not null ? "Requiere ubicación" : "Sin conexión"), On: s.Wifi.Available && s.Wifi.Connected, Enabled: s.Wifi.Available, Reason: s.Wifi.Reason, Glyph: "wifi", Bars: s.Wifi.Bars));
        if (cfg.CcBluetooth) Tile(new Widget(WidgetKind.Tile, "open:bluetooth", "Bluetooth", !s.Bluetooth.Available ? "No disponible" : s.Bluetooth.RadioOn ? (s.Bluetooth.ConnectedCount > 0 ? $"{s.Bluetooth.ConnectedCount} conectado(s)" : "Activado") : "Desactivado", On: s.Bluetooth.Available && s.Bluetooth.RadioOn, Enabled: s.Bluetooth.Available, Reason: s.Bluetooth.Reason, Glyph: "bluetooth"));
        if (cfg.CcAppearance) Tile(new Widget(WidgetKind.Tile, "appearance", "Modo oscuro", AppearanceText(s.Appearance), On: s.Appearance.Available && s.Appearance.Dark, Enabled: s.Appearance.Available, Reason: s.Appearance.Reason, Glyph: "moon"));
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

    /// <summary>
    /// Menú Sonido del icono de volumen (iteración 4: mezclador). Orden: volumen general con %, silencio, salida predeterminada, entrada predeterminada,
    /// volumen por aplicación (sesiones reales de Windows: icono, nombre, deslizador, %, silencio y actividad) y ajustes avanzados de Windows.
    /// La salida POR APLICACIÓN no tiene API pública en Windows: no se ofrece un selector que lo aparente; se abre el mezclador de Windows, que sí la tiene.
    /// Con <see cref="BarConfig.SoundMixer"/> desactivado queda el menú de antes (volumen y salida). El centro se desplaza si no cabe: la cabecera y los ajustes quedan fijos.
    /// </summary>
    public static PanelModel SoundPanel(PanelContext c)
    {
        var s = c.Sys.Audio; var w = new List<Widget> { Back("Sonido") };
        bool mixer = c.Cfg.SoundMixer;
        w.Add(new Widget(WidgetKind.Slider, "volume", "Volumen", Sub: mixer && s.Available ? $"{s.Volume} %" : null, Value: s.Volume, Enabled: s.Available, Reason: s.Reason, Glyph: s.Muted ? "sound-muted" : "sound"));
        if (s.Available)
        {
            w.Add(new Widget(WidgetKind.Row, "mute", "Silenciar", s.Muted ? "Silenciado" : "Con sonido", On: s.Muted));
            w.Add(new Widget(WidgetKind.Header, "outputs", "Salida", Glyph: "section"));
            bool canSwitch = c.Sys.OutputSwitchOk;
            foreach (var o in s.Outputs) w.Add(new Widget(WidgetKind.Row, "out:" + o.Id, o.Name, BusyOr(c, "out:" + o.Id, o.IsDefault ? "Predeterminada" : canSwitch ? "Toca para usar esta salida" : null), On: o.IsDefault, Enabled: canSwitch || o.IsDefault, Glyph: o.IsDefault ? "check" : null));
            if (!canSwitch) w.Add(new Widget(WidgetKind.Note, "noswitch", c.Sys.OutputSwitchReason ?? "No se puede cambiar la salida desde WinMac en este equipo."));
            if (c.Sys.CanRestoreOutput) w.Add(new Widget(WidgetKind.Button, "audio-restore", "Volver a la salida anterior"));
        }

        if (mixer)
        {
            var mic = c.Sys.Mic;
            w.Add(new Widget(WidgetKind.Header, "inputs", "Entrada", Glyph: "section"));
            if (!mic.Available) w.Add(new Widget(WidgetKind.Note, "in-none", mic.Reason ?? "No hay micrófono disponible."));
            else
            {
                bool inSwitch = c.Sys.OutputSwitchOk;   // el mismo mecanismo (IPolicyConfig) cambia la entrada: si no está, tampoco se aparenta
                foreach (var d in mic.Devices) w.Add(new Widget(WidgetKind.Row, "in:" + d.Id, d.Name, BusyOr(c, "in:" + d.Id, d.IsDefault ? "Predeterminada" : inSwitch ? "Toca para usar esta entrada" : null), On: d.IsDefault, Enabled: inSwitch || d.IsDefault, Glyph: d.IsDefault ? "check" : null));
                if (mic.Devices.Count == 0) w.Add(new Widget(WidgetKind.Note, "in-none", "No hay micrófonos activos."));
                w.Add(new Widget(WidgetKind.Slider, "mic", "", Sub: mic.VolumeControllable ? $"{mic.Volume} %" : null, Value: mic.Volume, Enabled: mic.VolumeControllable, Reason: mic.VolumeControllable ? null : mic.Reason,
                    Glyph: mic.Muted ? "mic-muted" : "mic", Data: "mic-mute"));
            }

            var ses = c.Sys.AppSessions;
            w.Add(new Widget(WidgetKind.Header, "apps", "Por aplicación", Glyph: "section"));
            if (!ses.Available) w.Add(new Widget(WidgetKind.Note, "apps-none", ses.Reason ?? "No se pudieron leer las aplicaciones con audio."));
            else if (ses.Sessions.Count == 0) w.Add(new Widget(WidgetKind.Note, "apps-none", "Ninguna aplicación tiene sesión de audio ahora."));
            else foreach (var app in ses.Sessions.Take(MixerMaxApps)) w.Add(new Widget(WidgetKind.AppRow, app.Id, app.Name, Value: app.Volume, Data: new AppRowData(app.ExePath, app.Name, app.Volume, app.Muted, app.Active)));
            w.Add(new Widget(WidgetKind.Note, "app-output", "Windows no ofrece una API pública para elegir la salida de cada aplicación; se hace en su mezclador."));
        }

        Msg(w, c);
        int pinBottom = 1;
        if (mixer) { w.Add(new Widget(WidgetKind.Button, "open:ms-settings:apps-volume", "Salida por aplicación (mezclador de Windows)…")); pinBottom++; }
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:sound", "Configuración de sonido de Windows…"));
        return new PanelModel(PanelKind.Sound, "Sonido", w, PinTop: 1, PinBottom: pinBottom);
    }

    /// <summary>
    /// Estado real del modo oscuro (3.2): el mosaico cambia a la vez el tema de las apps y el de Windows (lo mismo que «Elige tu modo» en Configuración).
    /// Si el usuario los tiene distintos (modo «Personalizado» de Windows) se dice cuál es cuál en vez de fingir un sí o un no. El tema de WinMac es otro ajuste.
    /// </summary>
    public static string AppearanceText(AppearanceState a) => !a.Available ? "No disponible"
        : a.SystemDark is { } sys && sys != a.Dark ? (a.Dark ? "Mixto: apps oscuras" : "Mixto: apps claras")
        : a.Dark ? "Activado" : "Desactivado";

    /// <summary>Aplicaciones como máximo en el mezclador del menú Sonido (el resto, en el mezclador de Windows).</summary>
    public const int MixerMaxApps = 12;

    /// <summary>
    /// WM-027 (on WM-010's real data): a hero with the percentage, state, level bar and charge power; health cards (health, full-charge and design capacity);
    /// «Detalles de batería» folds the secondary figures (time left, cycles, chemistry, rate); the power modes are cards with a short description.
    /// Only what Windows and the battery report: a value that is not reported is not shown (and the details say what is missing).
    /// </summary>
    public static PanelModel BatteryPanel(PanelContext c)
    {
        var b = c.Battery; var p = c.Sys.Power; var d = c.BatteryDetail; var w = new List<Widget> { Back("Energía") };
        if (b.Present)
        {
            string? power = d?.RateMW is { } rate && rate != 0 ? (rate > 0 ? "Cargando a " : "Consumo ") + BatteryText.MWh(Math.Abs(rate)).Replace(" Wh", " W") : null;
            string? left = !b.Plugged ? BatteryText.TimeLeft(b.SecondsLeft) : null;
            w.Add(new Widget(WidgetKind.BatteryHero, "battery", $"{b.Percent} %", BatteryText.State(b), On: b.Charging, Value: b.Percent, Data: new BatteryHeroData(power ?? left)));
            var cards = new List<StatCard>();
            if (d?.HealthPercent is { } health) cards.Add(new("Salud", health + " %", health >= 80 ? "good" : health >= 60 ? "warn" : "bad"));
            if (d?.FullMWh is { } full) cards.Add(new("Plena carga", BatteryText.MWh(full), null));
            if (d?.DesignMWh is { } design) cards.Add(new("Diseño", BatteryText.MWh(design), null));
            if (cards.Count > 0) w.Add(new Widget(WidgetKind.StatCards, "bat-cards", "", Data: new StatCardsData(cards)));
            if (d is null) w.Add(new Widget(WidgetKind.Note, "bat-reading", "Leyendo los datos de la batería…"));
            else
            {
                w.Add(new Widget(WidgetKind.Row, "bat:details", "Detalles de batería", null, On: c.BatteryDetailsOpen, Glyph: c.BatteryDetailsOpen ? "down" : "chevron"));
                if (c.BatteryDetailsOpen)
                {
                    if (!b.Plugged) w.Add(new Widget(WidgetKind.KeyValue, "bat-left", "Tiempo restante", left?.Replace("Quedan ", "") ?? "Windows lo está calculando"));
                    if (d.RateMW is { } r2 && r2 != 0) w.Add(new Widget(WidgetKind.KeyValue, "bat-rate", r2 > 0 ? "Potencia de carga" : "Consumo", BatteryText.MWh(Math.Abs(r2)).Replace(" Wh", " W")));
                    if (d.RemainingMWh is { } rem) w.Add(new Widget(WidgetKind.KeyValue, "bat-rem", "Energía restante", BatteryText.MWh(rem)));
                    if (d.FullMWh is { } f2) w.Add(new Widget(WidgetKind.KeyValue, "bat-full", "Capacidad a plena carga", BatteryText.MWh(f2)));
                    if (d.DesignMWh is { } d2) w.Add(new Widget(WidgetKind.KeyValue, "bat-design", "Capacidad de diseño", BatteryText.MWh(d2)));
                    if (d.HealthPercent is { } h2) w.Add(new Widget(WidgetKind.KeyValue, "bat-health", "Salud (plena carga / diseño)", h2 + " %"));
                    if (d.Cycles is { } cycles) w.Add(new Widget(WidgetKind.KeyValue, "bat-cycles", "Ciclos de carga", cycles.ToString()));
                    if (d.Chemistry is { } chem) w.Add(new Widget(WidgetKind.KeyValue, "bat-chem", "Química", chem));
                    if (d.Cycles is null || d.HealthPercent is null) w.Add(new Widget(WidgetKind.Note, "bat-missing", "La batería no informa de " + (d.Cycles is null && d.HealthPercent is null ? "sus ciclos ni de su capacidad" : d.Cycles is null ? "sus ciclos" : "su capacidad de diseño") + "."));
                }
            }
        }
        else w.Add(new Widget(WidgetKind.Info, "battery", "Sin batería", "Equipo de sobremesa o batería no detectada"));
        w.Add(new Widget(WidgetKind.Header, "modes", "Modo de energía", Glyph: "section"));
        foreach (var m in Enum.GetValues<PowerMode>())
            w.Add(new Widget(WidgetKind.ModeCard, "power:" + m, PowerModeState.Label(m), BusyOr(c, "power:" + m, ModeDescription(m)), On: p.Available && p.Mode == m, Enabled: p.Available, Reason: p.Reason));
        if (!p.Available) w.Add(new Widget(WidgetKind.Note, "reason", p.Reason ?? "El modo de energía no está disponible"));
        Msg(w, c);
        w.Add(new Widget(WidgetKind.Button, "open:ms-settings:batterysaver", "Abrir configuración de batería…"));
        return new PanelModel(PanelKind.Battery, "Energía", w, BatteryWidth);
    }

    public const double BatteryWidth = 340;

    public static string ModeDescription(PowerMode m) => m switch
    {
        PowerMode.BestEfficiency => "Más autonomía; el equipo rinde menos",
        PowerMode.Balanced => "Windows equilibra consumo y rendimiento",
        PowerMode.Performance => "Más rendimiento con algo más de consumo",
        _ => "Todo el rendimiento; la batería dura menos",
    };

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
