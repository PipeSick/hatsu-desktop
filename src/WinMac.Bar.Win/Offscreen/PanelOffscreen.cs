using WinMac.Bar.Hub;
using WinMac.Bar.Notifications;
using WinMac.Bar.Win.Rendering;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;
using WinMac.SystemControl;
using Vortice.Direct2D1;

namespace WinMac.Bar.Win.Offscreen;

/// <summary>
/// Renderiza el panel multifunción (cada pestaña) y el del Tiempo FUERA de pantalla con el pintor real, con DATOS DE MUESTRA (los valores de las capturas de referencia), para comparar la composición
/// con ellas en CI o en cualquier equipo. No lee sensores ni red, no abre ventanas y NO valida lectura de hardware, posición ni ratón.
/// </summary>
public static class PanelOffscreen
{
    private sealed class SampleSys : ISystemMonitor
    {
        public SystemSample Read() => new(Reading<double>.Of(25), Reading<double>.Of(72), Reading<double>.Unavailable("Sin sensor de CPU accesible sin controlador"), Reading<double>.Of(54),
            new MemoryInfo(16UL << 30, (ulong)(4.05 * (1UL << 30)), (ulong)(4.72 * (1UL << 30)), 10UL << 30, 32UL << 30, 800UL << 20, 4UL << 30), null, new TimeSpan(1, 19, 0, 0), Reading<int>.Of(0));
    }

    private sealed class SampleNet : INetworkMonitor
    {
        private ulong _rx = 1_000_000, _tx = 100_000; private int _n;
        public NetCounters ReadCounters() { _n++; _rx += (ulong)(180_000 + 120_000 * Math.Sin(_n / 4.0) + 20_000 * (_n % 3)); _tx += (ulong)(1_000 + 900 * (1 + Math.Sin(_n / 3.0))); return new NetCounters(_rx, _tx, new AdapterInfo("Wi-Fi", "Adaptador Wi-Fi", "Wi-Fi", "192.168.20.175", 1_200_000_000)); }
        public IReadOnlyList<NetApp> ReadApps() => [];
    }

    private sealed class SampleDisks : IDiskMonitor
    {
        private ulong _r, _w; private int _n;
        public DisksSnapshot ReadCatalog() => DiskCatalog.Build([new PhysicalRec(0, "Disco NVMe de muestra", "SSD", "NVMe", 245UL << 30, new SmartInfo("Saludable", 37, 639, 1, 51.47e12, 18.2e12))], [new PartitionRec(0, "C")], [new VolumeRec("C:\\", "Windows", "NTFS", 245UL << 30, 57UL << 30, false)]);
        public IReadOnlyList<DiskIo> ReadIo() { _n++; _r += (ulong)(700_000 + 500_000 * Math.Sin(_n / 3.0) + (_n % 7 == 0 ? 6_000_000 : 0)); _w += (ulong)(22_000 + 20_000 * (_n % 4)); return [new DiskIo(0, _r, _w)]; }
        public ServiceResult Eject(string volumeLetter) => ServiceResult.Unavailable("Solo con unidades externas");
    }

    private sealed class SampleWeather : IWeatherSource
    {
        private const string Json = """{"current":{"time":"2026-11-01T06:00","temperature_2m":12.0,"weather_code":3,"is_day":1},"hourly":{"time":["2026-11-01T06:00","2026-11-01T07:00","2026-11-01T08:00","2026-11-01T09:00","2026-11-01T10:00"],"temperature_2m":[12,12,13,14,15],"weather_code":[3,3,61,3,3],"is_day":[1,1,1,1,1],"precipitation_probability":[0,0,10,0,0]},"daily":{"time":["2026-11-01"],"temperature_2m_max":[21],"temperature_2m_min":[12],"sunrise":["2026-11-01T06:42"],"sunset":["2026-11-01T20:15"]}}""";
        public Task<WeatherSnapshot> FetchAsync(GeoPoint place, IReadOnlyList<GeoPoint> others, CancellationToken ct) =>
            Task.FromResult(new WeatherSnapshot(OpenMeteoParser.ParseForecast(Json, place), [new CityBrief(new GeoPoint("Santiago", null, -33.45, -70.67), 12, 3, true), new CityBrief(new GeoPoint("Antofagasta", null, -23.65, -70.4), 17, 2, false), new CityBrief(new GeoPoint("Valparaíso", null, -33.05, -71.62), 15, 61, false)], DateTime.UtcNow));
        public Task<IReadOnlyList<GeoPoint>> SearchAsync(string name, CancellationToken ct) => Task.FromResult<IReadOnlyList<GeoPoint>>([]);
    }

    private sealed class NoIcons : IAppIcons { public byte[]? GetBgra(string exePath, int size) => null; }

    /// <summary>Notificaciones de MUESTRA para la captura del Centro de Notificaciones (no se lee nada de Windows).</summary>
    private sealed class SampleNotifications(DateTimeOffset now) : INotificationSource
    {
        public (NotificationAccess Access, string? Reason) GetAccess() => (NotificationAccess.Allowed, null);
        public Task<NotificationAccess> RequestAccessAsync() => Task.FromResult(NotificationAccess.Allowed);
        public IReadOnlyList<AppNotification> Read() =>
        [
            new(1, "Microsoft.OutlookForWindows", "Outlook", "Ana Pérez", "Te envié el informe del trimestre. ¿Lo revisamos mañana a primera hora antes de la reunión?", now.AddMinutes(-3)),
            new(2, "Microsoft.OutlookForWindows", "Outlook", "Equipo de soporte", "Tu solicitud #4821 se ha resuelto.", now.AddMinutes(-41)),
            new(3, "Microsoft.OutlookForWindows", "Outlook", "Boletín", "Novedades de noviembre", now.AddHours(-3)),
            new(4, "Spotify", "Spotify", "Ahora suena", "Canción de muestra — Artista", now.AddMinutes(-12)),
            new(5, "Windows.SystemToast.SecurityAndMaintenance", "Seguridad de Windows", "Examen completado", null, now.AddHours(-26)),
        ];
        public ServiceResult Remove(uint id) => ServiceResult.Ok;
        public ServiceResult ClearAll() => ServiceResult.Ok;
        public ServiceResult OpenApp(string appId) => ServiceResult.Ok;
        public bool StartWatching(Action changed, out string? why) { why = null; return true; }
        public void StopWatching() { }
    }

    /// <param name="kind">«hub» o «weather».</param>
    /// <param name="maxHeight">Altura máxima en DIP (como la de una pantalla de 1080p): si el contenido no cabe, se desplaza el centro. Infinito = sin límite.</param>
    /// <param name="scroll">Desplazamiento del contenido en DIP (solo con maxHeight).</param>
    /// <param name="bands">Dibuja las dos franjas de contraste del fondo sintético (para demostrar que las franjas son del fondo de prueba, no del panel).</param>
    public static string Render(string pngPath, string kind, HubTab tab, BarTheme theme, double scale, double maxHeight = double.PositiveInfinity, double scroll = 0, bool bands = false)
    {
        DateTime now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var services = new HubServices(new SampleSys(), new SampleNet(), new SampleDisks(), new SampleWeather(), null, new NoIcons(), null);
        var mon = new HubMonitor(services, a => a(), a => a(), _ => { }, () => now); var weather = new WeatherService(services.Weather, new WeatherCache(null), () => now, a => a(), a => a(), _ => { });
        for (int i = 0; i < 40; i++) { now = now.AddSeconds(1); mon.RefreshSystem(); mon.RefreshNetwork(withApps: false); mon.RefreshDisks(forceCatalog: i == 0); }
        weather.Configure(new GeoPoint("Molina", null, -35.1167, -71.2833), []); weather.Refresh();
        var snapshot = new SystemSnapshot(new AudioState(true, 75, false, [new("a", "Altavoces (Realtek Audio)", true), new("b", "Auriculares (USB)", false)]), WifiState.None("-"), BluetoothState.None("-"), BrightnessState.None("-"), PowerModeState.None("-"), AppearanceState.None("-"), false, null, false,
            new AudioInputState(true, 50, false, true, [new("m", "Micrófono (Realtek Audio)", true)]),
            new AudioSessionsState(true, [new("pid:1", 1, "Spotify", null, 100, false, false, true), new("pid:2", 2, "Discord", null, 100, false, false, true), new("pid:3", 3, "Google Chrome", null, 100, false, false, true), new("pid:4", 4, "Sonidos del sistema", null, 100, false, true, false)]));
        var cfg = new BarConfig { Use24Hour = false };
        var ctx = PanelContext.Initial(cfg, now) with { Sys = snapshot, Tab = tab, Hub = new HubData(mon, new SpeedTestState(SpeedTestStatus.Idle, null, null), weather.Current(), null, false) };
        PanelModel model;
        if (kind.StartsWith("notifications", StringComparison.Ordinal))   // «notifications» (con muestra, un grupo desplegado) o «notifications-off» (antes de activarlo)
        {
            bool on = kind != "notifications-off"; var local = new DateTimeOffset(new DateTime(2026, 11, 2, 10, 30, 0, DateTimeKind.Local));
            var store = new NotificationStore(new SampleNotifications(local), a => a(), a => a(), _ => { }, () => now); store.SetEnabled(on); store.Activate();
            ctx = ctx with { Now = local.LocalDateTime, ViewYear = 2026, ViewMonth = 11, Cfg = cfg with { NotificationsFromWindows = on, Use24Hour = true },
                Nc = new NcView(store.State, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Microsoft.OutlookForWindows" }, 356) };
            model = PanelBuilder.NotificationCenter(ctx); if (double.IsInfinity(maxHeight)) maxHeight = 1040;
        }
        else if (kind is "mixer" or "mixer-routing")
        {   // WM-035 (corrección de la iteración 6): DATOS DE MUESTRA del menú Sonido con nombres largos, dos sesiones de la misma app y, en «mixer-routing», el selector de salida por aplicación
            var outs = new List<AudioDevice> { new("a", "Altavoces (Realtek(R) Audio)", false), new("b", "Altavoces (PRO X Wireless Gaming Headset)", true) };
            var sessions = new List<AudioSession> { new("pid:1", 1, "Discord", null, 100, false, false, true), new("pid:1b", 11, "Discord", null, 98, false, false, true), new("pid:2", 2, "Microsoft Edge WebView2", null, 100, false, false, true), new("pid:3", 3, "Sonidos del sistema", null, 100, true, false, false) };
            bool routing = kind == "mixer-routing"; var map = sessions.Where(x => x.Pid > 0).ToDictionary(x => x.Pid, x => x.Pid == 2 ? "b" : (string?)null);
            var snap = snapshot with { Audio = new AudioState(true, 70, false, outs), OutputSwitchOk = true, CanRestoreOutput = true,
                Input = new AudioInputState(true, 85, false, true, [new("m", "Micrófono (PRO X Wireless Gaming Headset)", true), new("m2", "Micrófono (Realtek(R) Audio)", false)]),
                Sessions = new AudioSessionsState(true, sessions), Routing = routing ? new AppRoutingState(true, null, map) : null };
            model = PanelBuilder.SoundPanel(ctx with { Sys = snap, Cfg = cfg with { SoundMixer = true, AppOutputRouting = routing } });
        }
        else if (kind.StartsWith("phone", StringComparison.Ordinal))
        {   // It7: DATOS DE MUESTRA del panel del iPhone: conectado con lectura reciente, desconectado (última lectura vieja) o sin lectura
            var t = now;
            var phs = kind switch
            {
                "phone-off" => new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.Disconnected, DeviceName = "iPhone de Ana", Battery = 64, BatteryAtUtc = t.AddMinutes(-42), LastSeenUtc = t.AddMinutes(-40), BatteryStatus = WinMac.Connect.PhoneFeatureStatus.Active, NotificationsStatus = WinMac.Connect.PhoneFeatureStatus.Active },
                "phone-unknown" => new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.Connected, DeviceName = "iPhone de Ana", BatteryStatus = WinMac.Connect.PhoneFeatureStatus.Active, NotificationsStatus = WinMac.Connect.PhoneFeatureStatus.NotAuthorized },
                _ => new WinMac.Connect.PhoneState { Status = WinMac.Connect.PhoneLinkStatus.Connected, DeviceName = "iPhone de Ana", Battery = 82, BatteryAtUtc = t.AddMinutes(-1), BatteryStatus = WinMac.Connect.PhoneFeatureStatus.Active, NotificationsStatus = WinMac.Connect.PhoneFeatureStatus.Active },
            };
            model = PanelBuilder.PhonePanel(ctx with { Phone = phs, PhoneNotifCount = kind == "phone" ? 3 : 0 });
        }
        else if (kind == "apple") model = PanelBuilder.AppleMenu(ctx with { ForegroundApp = "Spotify", User = new SessionUser("Usuario de prueba", "EQUIPO-CI") });
        else if (kind is "battery" or "battery-partial")   // WM-010: DATOS DE MUESTRA del panel de energía; «battery-partial» = un controlador que no informa de ciclos ni capacidad de diseño
            model = PanelBuilder.BatteryPanel(ctx with { Battery = new BatteryInfo(true, 64, false, false, 3 * 3600 + 20 * 60),
                BatteryDetail = kind == "battery" ? new BatteryDetails(57_000, 52_440, 33_560, -9_800, 212) : new BatteryDetails(null, 52_440, 33_560, null, null) });
        else model = kind == "weather" ? PanelBuilder.WeatherPanel(ctx) : PanelBuilder.HubPanel(ctx);
        var frame = PanelLayout.Compute(model, maxHeight, scroll);
        using var dev = new D2DDevice(true); dev.SetDpi(scale); using var painter = new PanelPainter(dev) { Theme = theme };
        int pw = (int)Math.Ceiling(frame.Width * scale), ph = (int)Math.Ceiling(frame.Height * scale); var dockTheme = theme == BarTheme.Dark ? DockTheme.Dark : DockTheme.Light;
        using var under = SyntheticBackdrop.Create(dev, frame.Width, frame.Height, scale, dockTheme, bands); using var target = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale); var c = dev.Context; c.Target = target;
        c.BeginDraw(); c.DrawBitmap(under, (Vortice.RawRectF?)new Vortice.RawRectF(0, 0, (float)frame.Width, (float)frame.Height), 1f, BitmapInterpolationMode.Linear, (Vortice.RawRectF?)null); c.EndDraw();
        painter.Paint(frame, model, -1, clear: false);
        var rgba = BarOffscreen.Readback(dev, target, pw, ph, scale); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, PngWriter.EncodeRgba(rgba, pw, ph)); c.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, {kind} {(kind == "hub" ? tab.ToString() : "")}, tema {theme}, datos de muestra" + (kind.StartsWith("notifications", StringComparison.Ordinal) ? " (no son notificaciones reales)" : "") + (frame.View is { } v ? $", desplazable (contenido {v.ContentHeight:0} DIP, visible {v.Bottom - v.Top:0}, desplazado {v.Offset:0}/{v.MaxOffset:0})" : "");
    }
}
