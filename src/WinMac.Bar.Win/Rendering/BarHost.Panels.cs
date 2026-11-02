using WinMac.Bar.Win.Interop;
using WinMac.Bar.Hub;
using WinMac.Bar.Notifications;
using WinMac.Bar.Win.Shell.Notifications;
using WinMac.Bar.Win.Shell;
using WinMac.Bar.Win.Shell.Hub;
using WinMac.Calendar;
using WinMac.SystemControl;
using WinMac.SystemControl.Win;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// La barra «que controla Windows»: menú Apple con acciones reales, paneles (Control Center, Wi-Fi, Bluetooth, sonido, energía, calendario, pantallas), menús EXPERIMENTALES de la app activa
/// y aplicación en vivo de la configuración central. Todo el trabajo lento va a hilos de trabajo (<see cref="SystemHub"/>); aquí solo se decide qué mostrar y se traducen los eventos.
/// </summary>
public sealed partial class BarHost : IPanelUi
{
    private SystemHub _hub = null!; private PanelOperations _ops = null!; private PopoverWindow? _popover; private PanelContext _pctx = null!; private PanelKind _pkind; private int _anchorX; private bool _alignRight;
    // ── panel multifunción y Tiempo
    private HubServices _hubSvc = null!; private IDisposable? _hubOwner; private HubMonitor _hubMon = null!; private SpeedTestRunner _speed = null!; private WeatherService _weather = null!; private HubRuntime _rt = null!;
    private GeoPoint? _autoPlace; private bool _locating, _locationTried; private DateTime _lastHubTick = DateTime.MinValue, _lastWeatherPoll = DateTime.MinValue; private int _hubTicks;
    /// <summary>Órdenes al host de WinMac (modo integrado): «settings» (con sección) y «quit». null = la barra corre sola.</summary>
    public Action<string, string?>? HostCommand { get; set; }
    private BarWindow? _popSource;      // la barra (monitor) donde se abrió el panel desplegable
    private BarWindow? _actionSource;   // la barra donde se hizo el último clic: dueña de los cuadros de diálogo y de los menús
    private readonly Queue<Action> _uiQueue = new(); private readonly object _cfgLock = new(); private BarConfig? _pendingCfg;
    private IReadOnlyList<AppMenuTop> _appMenuTops = []; private IReadOnlyList<string>? _appMenuLabels; private IntPtr _appMenuWindow;
    private DateTime _lastPanelRefresh = DateTime.MinValue, _lastEvents = DateTime.MinValue; private int _panelTicks;
    // ── Centro de Notificaciones (una sola fuente y un solo almacén por proceso de barra, compartidos por todos los monitores)
    private NotificationStore _nc = null!; private WinNotificationSource? _ncSrc; private DateTime _lastNcPoll = DateTime.MinValue;
    /// <summary>Cada cuánto se relee con el panel abierto cuando Windows no envía el evento de cambios.</summary>
    private static readonly TimeSpan NcPollEvery = TimeSpan.FromSeconds(4);

    /// <summary>Envía un cambio de ajustes al host (modo integrado). null = modo independiente: se guarda en bar.json.</summary>
    public Action<object>? PersistPatch { get; set; }

    /// <summary>Servicios de Windows que usa la barra (los sustituyen las pruebas).</summary>
    public SystemServices Services => _hub.Services;

    private void InitPanels(SystemServices? services, HubServices? hubServices)
    {
        _hub = new SystemHub(services ?? SystemServicesFactory.CreateReal(), Post, _log);
        if (hubServices is null) { var (s, owner) = HubServicesFactory.CreateReal(); _hubSvc = s; _hubOwner = owner; } else _hubSvc = hubServices;
        void Bg(Action a) => Task.Run(a);
        _hubMon = new HubMonitor(_hubSvc, Bg, Post, _log, () => DateTime.UtcNow); _speed = new SpeedTestRunner(_hubSvc.SpeedTest, Bg, Post);
        string cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "weather-cache.json");
        _weather = new WeatherService(_hubSvc.Weather, new WeatherCache(cache), () => DateTime.UtcNow, Bg, Post, _log); _rt = new HubRuntime(_hubMon, _speed, _weather);
        try { _ncSrc = new WinNotificationSource(_log); } catch (Exception e) { _log("[notificaciones] fuente no disponible: " + e.GetType().Name + ": " + e.Message); }
        _nc = new NotificationStore(_ncSrc, Bg, Post, _log, () => DateTime.UtcNow); _nc.SetEnabled(_cfg.NotificationsFromWindows); _nc.Changed += OnNcChanged;
        _hub.Changed += OnHubChanged; _ops = new PanelOperations(_hub, this, _log, rt: _rt, nc: _nc);
        _hubMon.Changed += OnHubPanelDataChanged; _speed.Changed += OnHubPanelDataChanged; _weather.Changed += () => { SetStateAll(); OnHubPanelDataChanged(); };
        ConfigureWeather();
    }

    private void OnHubPanelDataChanged() { if (_popover is { IsOpen: true } && _pkind is PanelKind.Hub or PanelKind.Weather or PanelKind.Notifications) RebuildPanel(); }
    private void OnNcChanged() { if (_popover is { IsOpen: true } && _pkind == PanelKind.Notifications) RebuildPanel(); }

    /// <summary>Iconos del panel: los de las apps de las notificaciones (en memoria, los entregó Windows al leer) y los de ejecutables (multifunción).</summary>
    private byte[]? PanelIcon(string key) =>
        key.StartsWith(PanelBuilder.NcIconPrefix, StringComparison.Ordinal) ? _ncSrc?.CachedIcon(key[PanelBuilder.NcIconPrefix.Length..])
        : key.StartsWith(PanelBuilder.TrayIconPrefix, StringComparison.Ordinal) ? TrayIconBgra(key)
        : _hubSvc.Icons.GetBgra(key, 32);

    /// <summary>Ciudad y adicionales desde la configuración central. La ubicación automática es OPT-IN: solo si el usuario la activó, se pide una vez por activación, y si Windows no la da se usa la ciudad elegida.</summary>
    private void ConfigureWeather()
    {
        var city = GeoPoint.Parse(_cfg.WeatherCity); var favs = GeoPoint.ParseList(_cfg.WeatherFavorites);
        if (!_cfg.WeatherAutoLocation) { _autoPlace = null; _locationTried = false; }
        _weather.Configure(_cfg.WeatherAutoLocation && _autoPlace is not null ? _autoPlace : city, favs);
        if (_cfg.WeatherAutoLocation && _autoPlace is null && !_locating && !_locationTried && _hubSvc.Location is { } loc)
        {
            _locating = true; _locationTried = true;
            Task.Run(async () =>
            {
                GeoPoint? p = null; try { p = await loc.GetAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception e) { _log("[tiempo] ubicación: " + e.GetType().Name + ": " + e.Message); }
                Post(() => { _locating = false; _autoPlace = p; if (p is null) _log("[tiempo] la ubicación no está disponible (permiso o servicio de Windows): se usa la ciudad elegida"); ConfigureWeather(); _weather.Refresh(); });
            });
        }
    }

    private HubData MakeHubData() => new(_hubMon, _speed.State, _weather.Current(), _pctx?.Hub?.SelectedVolume, _cfg.Use24Hour);

    /// <summary>Con el panel abierto lee SOLO lo de la pestaña activa, con la frecuencia configurada; con el panel cerrado no se lee nada de esto.</summary>
    private void RefreshHubTab(bool first)
    {
        switch (_pctx.Tab)
        {
            case HubTab.Sound: _hub.RefreshAudio(); _hub.RefreshInput(); _hub.RefreshSessions(); break;
            case HubTab.System: _hubMon.RefreshSystem(); break;
            case HubTab.Network: _hubMon.RefreshNetwork(withApps: first || _hubTicks % 3 == 0); break;
            default: _hubMon.RefreshDisks(forceCatalog: false); break;
        }
    }

    private void HubTick()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastWeatherPoll).TotalSeconds >= 60) { _lastWeatherPoll = now; _weather.Refresh(); }   // el propio servicio solo pide de verdad cada 15 min
        if (_popover is not { IsOpen: true } || _pkind != PanelKind.Hub || (now - _lastHubTick).TotalMilliseconds < _cfg.HubRefreshMs) return;
        _lastHubTick = now; _hubTicks++; RefreshHubTab(first: false);
    }

    // ───────────────────────────── hilo de la barra
    private void Post(Action a) { lock (_uiQueue) _uiQueue.Enqueue(a); var h = _hubHwnd; if (h != IntPtr.Zero) BarNative.PostMessage(h, WM_APP_UI, IntPtr.Zero, IntPtr.Zero); }

    private void DrainUi()
    {
        while (true)
        {
            Action? a; lock (_uiQueue) { if (_uiQueue.Count == 0) return; a = _uiQueue.Dequeue(); }
            try { a(); } catch (Exception e) { _log("[bar] acción pendiente falló: " + e.GetType().Name + ": " + e.Message); }
        }
    }

    private BarState MakeState()
    {
        var s = _hub.Snapshot; var a = s.Audio;
        var vol = a.Available ? new VolumeInfo(true, a.Volume, a.Muted) : VolumeInfo.None;
        var net = _network.Kind == NetKind.Wifi && s.Wifi.Available && s.Wifi.Connected ? _network with { Ssid = s.Wifi.Ssid, Bars = s.Wifi.Bars } : _network;
        var bt = s.Bluetooth.Available ? new BluetoothInfo(true, s.Bluetooth.RadioOn, s.Bluetooth.ConnectedCount) : BluetoothInfo.None;
        var wx = _weather.Current(); var wbar = wx.Snapshot is { } sn ? new WeatherBarInfo(WeatherCodes.Icon(sn.Main.Code, sn.Main.IsDay), TempFormat.Bar(sn.Main.TempC, _cfg.WeatherFahrenheit)) : WeatherBarInfo.None;
        return new BarState(DateTime.Now, _app?.Name, _battery, vol, net, bt, _appMenuLabels, wbar, _cfg.ShowTrayIcons ? _trayStore.Visible : null, TrayOpen: _popover is { IsOpen: true } && _pkind == PanelKind.Tray);
    }

    private void OnHubChanged() { SetStateAll(); if (_popover is { IsOpen: true }) RebuildPanel(); }

    private void PanelsTick()
    {
        _popover?.Pump(); HubTick(); TrayTick(); NotchTick(); LauncherTick(); SpotlightTick(); MenuDiag();
        if (_popover is not { IsOpen: true }) return;
        var now = DateTime.UtcNow;
        if (_pkind == PanelKind.Notifications && _nc.NeedsPolling && now - _lastNcPoll >= NcPollEvery) { _lastNcPoll = now; _nc.Refresh(); }   // sin evento de Windows: relectura suave, solo con el panel abierto
        if ((now - _lastPanelRefresh).TotalSeconds < 2.5) return;
        _lastPanelRefresh = now; _panelTicks++;
        switch (_pkind)
        {
            case PanelKind.Wifi: _hub.RefreshWifi(scan: _panelTicks % 2 == 0); break;   // el escaneo de Windows se pide cada ~5 s
            case PanelKind.Bluetooth: _hub.RefreshBluetooth(true); break;
            case PanelKind.Sound or PanelKind.ControlCenter: _hub.RefreshAudio(); break;
            case PanelKind.Battery: _battery = SystemProviders.Battery(); _hub.RefreshSlow(); break;
        }

        if (_pkind is PanelKind.Calendar or PanelKind.Notifications && (now - _lastEvents).TotalSeconds > 60) LoadEvents();
        RebuildPanel();
    }

    // ───────────────────────────── paneles
    private void OpenPanelFromBar(BarWindow src, BarAction a, BarItemFrame? item)
    {
        if (a.Panel is not PanelKind kind) { if (a.Uri is not null) BarNative.ShellExecuteW(IntPtr.Zero, "open", a.Uri, null, null, 1); return; }
        if (_popover is { IsOpen: true } && _pkind == kind && ReferenceEquals(_popSource, src)) { _popover.Hide(); return; }   // segundo clic en el mismo icono de la misma barra = cerrar; en otra barra = abrirlo ahí
        int cx = item is null ? src.Left + src.MonitorRect.W / 2 : src.Left + (int)((item.X + item.W / 2) * src.Scale);
        try { OpenPanel(src, kind, cx, alignRight: kind == PanelKind.ControlCenter); }
        catch (Exception e)
        {
            _log("[bar] no se pudo abrir el panel: " + e.GetType().Name + ": " + e.Message);   // nunca dejar al usuario sin acción: se abre la página de Configuración
            if (a.Uri is not null) BarNative.ShellExecuteW(IntPtr.Zero, "open", a.Uri, null, null, 1);
        }
    }

    private void OpenPanel(BarWindow src, PanelKind kind, int anchorX, bool alignRight)
    {
        _popover ??= new PopoverWindow(OnPanelEvent, OnPanelClosed, _log, PanelIcon);
        _popSource = src; _pkind = kind; _anchorX = anchorX; _alignRight = alignRight; _panelTicks = 0; _lastPanelRefresh = DateTime.UtcNow;
        _pctx = PanelContext.Initial(_cfg, DateTime.Now) with { Sys = _hub.Snapshot, Battery = _battery, Tab = _cfg.HubTab, Tray = _trayStore.Visible }; _pctx = _pctx with { Hub = MakeHubData() };
        if (kind == PanelKind.Notifications) { OpenNotificationCenter(src); return; }
        var model = PanelBuilder.Build(kind, _pctx);
        _popover.Show(model, anchorX, src.Top + src.Height, src.MonitorRect, src.Scale, _theme, alignRight);   // se abre en el monitor donde se hizo clic, escalado a SU DPI
        if (kind == PanelKind.Tray) SetStateAll();   // la flecha pasa a apuntar arriba
        RefreshFor(kind, firstOpen: true);
    }

    /// <summary>
    /// Centro de Notificaciones: panel lateral en el borde derecho del monitor de la barra pulsada, a SU DPI, entre la barra y el final del área de trabajo (no tapa la barra ni la de tareas).
    /// Al abrirse se lee una vez (y se escucha el evento de Windows si existe); el Tiempo usa el servicio compartido (respeta su caché de 15 min: no hay consultas extra a Open-Meteo).
    /// </summary>
    private void OpenNotificationCenter(BarWindow src)
    {
        _battery = SystemProviders.Battery();
        double widthDip = NcGeometry.Width(src.MonitorRect.W / src.Scale);
        var rect = NcGeometry.Place(src.Top + src.Height, src.MonitorRect, src.Info.Work, src.Scale, widthDip);
        _pctx = _pctx with { Battery = _battery, Nc = new NcView(_nc.State, new HashSet<string>(StringComparer.OrdinalIgnoreCase), widthDip) };
        _popover!.ShowSide(PanelBuilder.Build(PanelKind.Notifications, _pctx), rect, src.MonitorRect, src.Scale, _theme, _cfg.Background == BarBackground.Glass);
        _lastNcPoll = DateTime.UtcNow; _nc.Activate(); _weather.Refresh(); LoadEvents();
    }

    private void NavigatePanel(PanelKind kind)
    {
        if (_popover is not { IsOpen: true }) return;
        if (kind == PanelKind.Notifications && _popSource is { } from && _pkind != PanelKind.Notifications)   // desde el Control Center: se reabre como panel lateral en el mismo monitor
        {
            _popover.Hide(); OpenPanel(from, PanelKind.Notifications, 0, alignRight: true); return;
        }

        _pkind = kind; _pctx = _pctx with { Message = null, Busy = new Dictionary<string, string>() };
        _popover.SetModel(PanelBuilder.Build(kind, _pctx with { Sys = _hub.Snapshot, Battery = _battery, Now = DateTime.Now })); RefreshFor(kind, firstOpen: false);
    }

    private void RefreshFor(PanelKind kind, bool firstOpen)
    {
        if (kind == PanelKind.Weather) { _weather.Refresh(); return; }
        if (kind == PanelKind.Hub) { _hubMon.StartSession(); _hubTicks = 0; _lastHubTick = DateTime.UtcNow; RefreshHubTab(first: true); return; }
        _hub.RefreshAudio();
        if (kind is PanelKind.ControlCenter or PanelKind.Wifi) _hub.RefreshWifi(scan: true);
        if (kind is PanelKind.ControlCenter or PanelKind.Bluetooth) _hub.RefreshBluetooth(true);
        if (kind is PanelKind.ControlCenter or PanelKind.Sound or PanelKind.Battery) _hub.RefreshSlow();
        if (kind == PanelKind.Calendar) LoadEvents();
        _ = firstOpen;
    }

    private void RebuildPanel()
    {
        if (_popover is not { IsOpen: true }) return;
        // El menú de una app no sale de PanelBuilder ni usa el contexto de los paneles (que puede no existir aún): se rehace desde su propio árbol.
        // Antes caía aquí, _pctx era null → NullReferenceException en el bucle de la barra → la barra se cerraba y el host la reiniciaba (P1, visto en el CI).
        if (_pkind == PanelKind.AppMenu) { if (_amTop is not null) _popover.SetModel(WinMac.Bar.Menus.AppMenuPanel.Build(_amTop, _amPath)); return; }
        _pctx = _pctx with { Sys = _hub.Snapshot, Battery = _battery, Now = DateTime.Now, Cfg = _cfg, Hub = MakeHubData(), Nc = _pctx.Nc is { } v ? v with { State = _nc.State } : null, Tray = _trayStore.Visible };
        _popover.SetModel(PanelBuilder.Build(_pkind, _pctx));
    }

    private void OnPanelClosed() { if (_pkind == PanelKind.Notifications) _nc.Deactivate(); _popSource = null; SetStateAll(); foreach (var w in _wins) w.Redraw(); }   // la flecha de las apps en segundo plano vuelve a apuntar abajo

    private void LoadEvents()
    {
        _lastEvents = DateTime.UtcNow; var path = _cfg.CalendarIcsPath;
        if (string.IsNullOrWhiteSpace(path)) { if (_pctx.Events.Count > 0) { _pctx = _pctx with { Events = [] }; } return; }
        Task.Run(() =>
        {
            IReadOnlyList<CalendarEvent> list;
            try { list = IcsParser.Parse(File.ReadAllText(path)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { list = []; }   // archivo ausente o bloqueado a mitad de sincronización: día vacío
            Post(() => { _pctx = _pctx with { Events = list }; RebuildPanel(); });
        });
    }

    // Las acciones de los paneles (PanelOperations, en el núcleo y con pruebas) piden a esta clase lo que solo ella puede hacer: reconstruir el panel, navegar, cerrar, avisar y abrir URIs.
    PanelContext IPanelUi.Ctx { get => _pctx; set => _pctx = value; }
    void IPanelUi.Rebuild() => RebuildPanel();
    void IPanelUi.Navigate(PanelKind kind) => NavigatePanel(kind);
    void IPanelUi.HidePanel() => _popover?.Hide();
    void IPanelUi.ShowInfo(string title, string? text) => ShowInfo(title, text);
    void IPanelUi.OpenUri(string uri) => BarNative.ShellExecuteW(IntPtr.Zero, "open", uri, null, null, 1);
    void IPanelUi.TabChanged(HubTab tab) { _hubTicks = 0; _lastHubTick = DateTime.UtcNow; RefreshHubTab(first: true); }
    bool IPanelUi.Confirm(string title, string text) => Confirm(title, text);

    void IPanelUi.OpenSettings(string? section)
    {
        _popover?.Hide();
        if (HostCommand is { } hc) hc("settings", section); else ShowInfo("Ajustes", "Los ajustes de WinMac se abren desde WinMac.exe (esta barra se ejecutó por separado).");
    }

    /// <summary>«Salir» cierra WinMac COMPLETO por el cierre ordenado del host (que cierra Barra, Dock e Isla). Si la barra corre sola, no hay nada más que cerrar que ella misma.</summary>
    void IPanelUi.QuitWinMac() { _popover?.Hide(); if (HostCommand is { } hc) hc("quit", null); else _quitting = true; }

    void IPanelUi.PatchConfig(object patch)
    {
        try
        {
            var n = _cfg.ApplyPatch(System.Text.Json.JsonSerializer.SerializeToElement(patch));
            if (PersistPatch is not null) PersistPatch(patch); else if (_cfgPath is not null) n.TrySave(_cfgPath);
            lock (_cfgLock) _pendingCfg = n; ApplyPendingConfig();
        }
        catch (Exception e) { _log("[bar] no se pudo guardar el ajuste: " + e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>Menú nativo de selección junto al puntero (dispositivos de audio…). El panel no se cierra mientras está abierto.</summary>
    void IPanelUi.ShowChoices(string title, IReadOnlyList<Choice> choices, Action<string> onPick)
    {
        if (choices.Count == 0 || _popover is null) return;
        IntPtr menu = BarNative.CreatePopupMenu(); int cmd = 0; IntPtr owner = _popover.Hwnd;
        try
        {
            for (int i = 0; i < choices.Count; i++) BarNative.AppendMenuW(menu, choices[i].Checked ? 0x8u /* MF_CHECKED */ : 0u, (UIntPtr)(uint)(i + 1), choices[i].Text);
            BarNative.GetCursorPos(out var p); _popover.KeepOpen = true; _modal = true; BarNative.SetForegroundWindow(owner);
            cmd = BarNative.TrackPopupMenuEx(menu, 0x100 | 0x80 | 0x2 /* RETURNCMD|NONOTIFY|RIGHTBUTTON */, p.X, p.Y, owner, IntPtr.Zero);
        }
        finally { _modal = false; _popover.KeepOpen = false; BarNative.DestroyMenu(menu); }
        BarNative.SetForegroundWindow(owner);
        if (cmd > 0 && cmd <= choices.Count) onPick(choices[cmd - 1].Id);
    }

    private void OnPanelEvent(PanelEvent e)
    {
        if (_pkind == PanelKind.AppleMenu) { AppleAction(e); return; }
        if (_pkind == PanelKind.Tray) { TrayPanelAction(e); return; }
        if (_pkind == PanelKind.AppMenu) { AppMenuAction(e); return; }
        _ops.Handle(e);
    }

    // ───────────────────────────── cuadros de diálogo propios
    private void ShowInfo(string title, string? text) { if (string.IsNullOrWhiteSpace(text)) return; ModalBox(title, text!, 0x40 /* ICONINFORMATION | OK */); }

    private bool Confirm(string title, string text) => ModalBox(title, text, 0x4 | 0x30 | 0x100) == 6;   // YESNO | ICONWARNING | DEFBUTTON2 (por defecto «No»)

    private int ModalBox(string title, string text, uint flags)
    {
        if (_popover is not null) _popover.KeepOpen = true; _modal = true;
        try { return BarNative.MessageBoxW(_actionSource?.Hwnd ?? (_wins.Count > 0 ? _wins[0].Hwnd : IntPtr.Zero), text, title, flags | 0x40000 /* TOPMOST */ | 0x10000 /* SETFOREGROUND */); }
        finally { _modal = false; if (_popover is not null) _popover.KeepOpen = false; }
    }

    // ───────────────────────────── menú Apple
    private IntPtr _appleReturn;

    /// <summary>
    /// Menú de la manzana con el aspecto de macOS (panel propio de WinMac en vez del menú nativo de Windows), pegado al icono y en el monitor de la barra pulsada.
    /// Al elegir una opción, el foco vuelve a la app que lo tenía y la acción se ejecuta sobre ella (Forzar salida, confirmaciones).
    /// </summary>
    private void ShowAppleMenu(BarWindow src, BarItemFrame? item)
    {
        try
        {
            if (_popover is { IsOpen: true } && _pkind == PanelKind.AppleMenu && ReferenceEquals(_popSource, src)) { _popover.Hide(); return; }   // segundo clic: cerrar
            _appleReturn = BarNative.GetForegroundWindow();
            _popover ??= new PopoverWindow(OnPanelEvent, OnPanelClosed, _log, PanelIcon);
            _popSource = src; _pkind = PanelKind.AppleMenu;
            _pctx = PanelContext.Initial(_cfg, DateTime.Now) with { ForegroundApp = SessionService.ForegroundProcessName() };
            var model = PanelBuilder.AppleMenu(_pctx);
            int left = src.Left + (int)((item?.X ?? 0) * src.Scale), anchor = left + (int)Math.Round(model.Width * src.Scale / 2);   // el borde izquierdo del menú bajo el icono
            _popover.Show(model, anchor, src.Top + src.Height, src.MonitorRect, src.Scale, _theme, alignRight: false);
        }
        catch (Exception e) { _log("Menú falló: " + e.Message); }
    }

    private void AppleAction(PanelEvent e)
    {
        if (e.Kind != PanelEventKind.Click || !e.Id.StartsWith("apple:", StringComparison.Ordinal)) return;
        string id = e.Id[6..]; var prev = _appleReturn; string? app = _pctx.ForegroundApp; _appleReturn = IntPtr.Zero;
        _popover?.Hide();
        if (prev != IntPtr.Zero && !_wins.Any(w => w.Hwnd == prev) && BarNative.IsWindow(prev)) BarNative.SetForegroundWindow(prev);   // la acción se ejecuta con el foco de vuelta en la app que lo tenía
        if (id == "reserve") SetReserve(!_cfg.ReserveSpace);
        else if (id == "quitbar") _quitting = true;
        else if (Enum.TryParse<SystemActionKind>(id, out var k)) RunSystemAction(k, app);
    }

    private void RunSystemAction(SystemActionKind k, string? appName)
    {
        var info = SystemActions.Get(k);
        if (SystemActions.NeedsConfirmation(k, _cfg.ConfirmDestructive) && !Confirm(info.ConfirmTitle ?? info.Label, string.Format(info.ConfirmText ?? "¿Continuar?", appName ?? "la app activa"))) return;
        _hub.Run(() => _hub.Services.Session.Run(k), r => { if (!r.IsOk) ShowInfo(info.Label, r.Message); });
    }

    private void SetReserve(bool on)
    {
        var n = _cfg with { ReserveSpace = on };
        if (PersistPatch is not null) PersistPatch(new { reserveSpace = on }); else if (_cfgPath is not null) n.TrySave(_cfgPath);
        lock (_cfgLock) _pendingCfg = n; ApplyPendingConfig();   // se aplica ya; si el host la devuelve igual, no hay cambio
    }

    // ───────────────────────────── menús de la app activa (Global Menu, opción A; docs/23)
    // Se leen FUERA del hilo de la barra (una app colgada no la congela) y se guardan por ventana unos segundos. Los menús originales siguen en la app.
    private WinMac.Bar.Menus.AppMenus _appMenus = WinMac.Bar.Menus.AppMenus.Empty; private int _appMenuGen; private string? _appMenuName;
    private WinMac.Bar.Menus.AppMenuEntry? _amTop; private IReadOnlyList<int> _amPath = []; private int _amTopIndex = -1;
    private readonly Dictionary<IntPtr, (DateTime At, WinMac.Bar.Menus.AppMenus Menus)> _appMenuCache = [];

    private void RefreshAppMenus()
    {
        if (!_cfg.AppMenus) { ClearAppMenus(); return; }
        var h = BarNative.GetForegroundWindow(); if (h == IntPtr.Zero) return;
        BarNative.GetWindowThreadProcessId(h, out uint pid); if (pid == (uint)Environment.ProcessId) return;   // un clic en la propia barra no cambia los menús
        var cls = AppMenuService.ClassOf(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") { ClearAppMenus(); return; }
        _appMenuWindow = h; int gen = ++_appMenuGen;
        if (_appMenuCache.TryGetValue(h, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(8)) { SetAppMenus(c.Menus); return; }
        var tops = AppMenuService.Read(h);   // solo lectura de la barra de menús Win32 (rápida, sin mensajes a la app)
        if (tops.Count > 0) { SetAppMenus(Cache(h, new(WinMac.Bar.Menus.AppMenuSource.Win32, tops.Select(t => new WinMac.Bar.Menus.AppMenuEntry(t.Text, 0, Children: null)).ToList()))); return; }
        SetAppMenus(WinMac.Bar.Menus.AppMenus.Empty);
        Task.Run(() =>   // UI Automation puede tardar (árboles grandes): en segundo plano, con tope
        {
            var t = Task.Run(() => WinMac.Bar.Win.Menus.AppMenuReader.ReadAutomation(h));
            var names = t.Wait(TimeSpan.FromSeconds(2)) ? t.Result : [];
            Post(() =>
            {
                var menus = names.Count == 0 ? WinMac.Bar.Menus.AppMenus.Empty : new WinMac.Bar.Menus.AppMenus(WinMac.Bar.Menus.AppMenuSource.Automation, names.Select(n => new WinMac.Bar.Menus.AppMenuEntry(n, 0)).ToList());
                Cache(h, menus);
                if (gen == _appMenuGen) { SetAppMenus(menus); UpdateState(force: true); }
            });
        });
    }

    private WinMac.Bar.Menus.AppMenus Cache(IntPtr h, WinMac.Bar.Menus.AppMenus m)
    {
        if (_appMenuCache.Count > 64) _appMenuCache.Clear();
        _appMenuCache[h] = (DateTime.UtcNow, m); return m;
    }

    private void SetAppMenus(WinMac.Bar.Menus.AppMenus m)
    {
        _appMenus = m; _appMenuTops = m.Source == WinMac.Bar.Menus.AppMenuSource.Win32 ? AppMenuService.Read(_appMenuWindow) : []; _appMenuLabels = m.Labels;
        string name = _app?.Name ?? "";
        if (name != _appMenuName || m.Tops.Count > 0) { _appMenuName = name; _log($"[menús] {name}: {(m.Tops.Count == 0 ? "sin menús" : $"{m.Tops.Count} menús ({m.Source}): " + string.Join(", ", m.Tops.Select(t => t.Text)))}"); }
    }

    private void ClearAppMenus() { _appMenus = WinMac.Bar.Menus.AppMenus.Empty; _appMenuTops = []; _appMenuLabels = null; }

    private void ShowAppMenu(BarWindow src, int index, BarItemFrame? item)
    {
        if (MenuDiagOn) _log($"[menús] clic en el menú {index} (hay {_appMenus.Tops.Count}, Win32 {_appMenuTops.Count}, ventana {(_appMenuWindow == IntPtr.Zero ? "ninguna" : "sí")}, panel abierto {_popover?.IsOpen == true} {_pkind})");
        if (index < 0 || index >= _appMenus.Tops.Count || _appMenuWindow == IntPtr.Zero) return;
        if (_popover is { IsOpen: true } && _pkind == PanelKind.AppMenu && _amTopIndex == index) { _popover.Hide(); return; }   // segundo clic: cerrar
        _popover?.Hide();
        int x = src.Left + (int)((item?.X ?? 0) * src.Scale), y = src.Top + src.Height; var app = _appMenuWindow; var top = _appMenus.Tops[index];
        if (_appMenus.Source == WinMac.Bar.Menus.AppMenuSource.Automation)
        {
            Task.Run(() => { if (!WinMac.Bar.Win.Menus.AppMenuReader.ExpandAutomation(app, index, top.Text)) _log("[menús] la app no abrió su menú «" + top.Text + "»"); });
            return;
        }

        if (index >= _appMenuTops.Count) return;
        var tree = WinMac.Bar.Win.Menus.AppMenuReader.ReadWin32(app, _appMenuTops[index].SubMenu, _appMenuTops[index].Index, top.Text);
        if (tree is null)
        {
            if (MenuDiagOn) _log($"[menús] «{top.Text}»: árbol ilegible → menú nativo de la app");
            // La app dibuja sus menús o no respondió a tiempo: se usa SU menú nativo (el mismo de su ventana), que sí sabe dibujarse.
            IntPtr prev = BarNative.GetForegroundWindow(); _modal = true;
            try { var r = AppMenuService.Show(src.Hwnd, app, _appMenuTops[index], x, y); if (!r.IsOk) _log("[bar] menú de la app: " + r.Message); }
            finally { _modal = false; if (prev != IntPtr.Zero && !_wins.Any(w => w.Hwnd == prev)) BarNative.SetForegroundWindow(prev); }
            return;
        }

        _amTop = tree; _amPath = []; _amTopIndex = index;
        _popover ??= new PopoverWindow(OnPanelEvent, OnPanelClosed, _log, PanelIcon);
        if (MenuDiagOn) { _popover.FocusDiag = _log; _log($"[menús] abrir «{top.Text}»: árbol con {tree.Children?.Count ?? 0} entradas"); }
        _popSource = src; _pkind = PanelKind.AppMenu;
        var model = WinMac.Bar.Menus.AppMenuPanel.Build(tree, _amPath);
        _popover.Show(model, x + (int)Math.Round(model.Width * src.Scale / 2), y, src.MonitorRect, src.Scale, _theme, alignRight: false);
    }

    /// <summary>
    /// SOLO CI (WINMAC_MENU_DIAG=1): dónde están en la pantalla las etiquetas de los menús de la app y, con un menú abierto, sus filas; así la auditoría visual pulsa
    /// con el ratón como un usuario. Sin la variable no se registra nada.
    /// </summary>
    private static readonly bool MenuDiagOn = Environment.GetEnvironmentVariable("WINMAC_MENU_DIAG") == "1";
    private void InitMenuDiag() { if (MenuDiagOn) WinMac.Bar.Win.Menus.AppMenuReader.Diag = _log; }
    private string? _menuDiagLast; private DateTime _menuDiagAt;
    private void MenuDiag()
    {
        if (!MenuDiagOn || DateTime.UtcNow - _menuDiagAt < TimeSpan.FromMilliseconds(400)) return;
        _menuDiagAt = DateTime.UtcNow;
        var parts = new List<string>();
        foreach (var w in _wins)
            foreach (var f in w.Ctl.Frame.Items.Where(i => i.Item.Kind is BarItemKind.AppMenu or BarItemKind.AppName))
                parts.Add($"{f.Item.Kind}:{f.Item.Text}@{w.Left + (int)((f.X + f.W / 2) * w.Scale)},{w.Top + w.Height / 2}");
        if (_popover is { IsOpen: true } && _pkind == PanelKind.AppMenu)
            foreach (var r in _popover.DiagRows()) parts.Add($"fila:{r.Id}:{r.Text}@{r.X},{r.Y}");
        var line = string.Join(" | ", parts);
        if (line != _menuDiagLast) { _menuDiagLast = line; _log("[menús] diag " + line); }
    }

    private void AppMenuAction(PanelEvent e)
    {
        if (_amTop is null || _popover is null) return;
        if (e.Id == "back") { if (_amPath.Count > 0) { _amPath = _amPath.Take(_amPath.Count - 1).ToList(); _popover.SetModel(WinMac.Bar.Menus.AppMenuPanel.Build(_amTop, _amPath)); } return; }
        var (cmd, open) = WinMac.Bar.Menus.AppMenuPanel.Click(_amTop, _amPath, e.Id);
        if (open is not null) { _amPath = open; _popover.SetModel(WinMac.Bar.Menus.AppMenuPanel.Build(_amTop, _amPath)); return; }
        if (cmd <= 0) return;
        var app = _appMenuWindow; _popover.Hide(); _amTopIndex = -1;
        WinMac.Bar.Win.Menus.AppMenuReader.Run(app, cmd);
        _log("[menús] orden " + cmd + " enviada a la app");
    }

    // ───────────────────────────── configuración central en vivo
    /// <summary>Aplica una configuración nueva (la recibe del host). Seguro desde cualquier hilo; se aplica en el hilo de la barra y solo toca lo que cambió.</summary>
    public void ApplyConfig(BarConfig c) { lock (_cfgLock) _pendingCfg = c; var h = _hubHwnd; if (h != IntPtr.Zero) BarNative.PostMessage(h, WM_APP_CONFIG, IntPtr.Zero, IntPtr.Zero); }

    private void ApplyPendingConfig()
    {
        BarConfig? c; lock (_cfgLock) { c = _pendingCfg; _pendingCfg = null; }
        if (c is null || c == _cfg) return;
        var old = _cfg; _cfg = c;
        foreach (var w in _wins) w.ApplyConfig(old, c);   // todas las barras comparten la configuración central y la aplican en vivo
        if (old.Theme != c.Theme) SetThemeAll(ResolveTheme());
        if (old.AppMenus != c.AppMenus) { if (c.AppMenus) RefreshAppMenus(); else ClearAppMenus(); }
        if (old.WeatherCity != c.WeatherCity || old.WeatherFavorites != c.WeatherFavorites || old.WeatherAutoLocation != c.WeatherAutoLocation) { ConfigureWeather(); _weather.Refresh(); }
        if (old.CalendarIcsPath != c.CalendarIcsPath && _pkind is PanelKind.Calendar or PanelKind.Notifications && _popover is { IsOpen: true }) LoadEvents();
        if (old.LaunchpadOnWinKey != c.LaunchpadOnWinKey) ApplyLauncherConfig();
        if (old.SpotlightHotkey != c.SpotlightHotkey) ApplySpotlightConfig();
        if (old.ShowTrayIcons != c.ShowTrayIcons) { if (c.ShowTrayIcons) StartTray(); else StopTray(); }
        if (old.NotificationsFromWindows != c.NotificationsFromWindows) { _nc.SetEnabled(c.NotificationsFromWindows); if (c.NotificationsFromWindows && _popover is { IsOpen: true } && _pkind == PanelKind.Notifications && !_nc.Active) _nc.Activate(); }
        _nextClock = DateTime.MinValue; UpdateState(force: false);
        if (_popover is { IsOpen: true }) RebuildPanel();
    }

    private void DisposePanels() { _nc?.Deactivate(); _ncSrc?.Dispose(); _ncSrc = null; _popover?.Dispose(); _popover = null; _hubOwner?.Dispose(); _hubOwner = null; }
}
