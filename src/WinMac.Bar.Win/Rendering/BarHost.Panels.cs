using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Shell;
using WinMac.Calendar;
using WinMac.SystemControl;
using WinMac.SystemControl.Win;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// La barra «que controla Windows»: menú Apple con acciones reales, paneles (Control Center, Wi-Fi, Bluetooth, sonido, energía, calendario, pantallas), menús EXPERIMENTALES de la app activa
/// y aplicación en vivo de la configuración central. Todo el trabajo lento va a hilos de trabajo (<see cref="SystemHub"/>); aquí solo se decide qué mostrar y se traducen los eventos.
/// </summary>
public sealed partial class BarHost
{
    private SystemHub _hub = null!; private PopoverWindow? _popover; private PanelContext _pctx = null!; private PanelKind _pkind; private int _anchorX; private bool _alignRight;
    private readonly Queue<Action> _uiQueue = new(); private readonly object _cfgLock = new(); private BarConfig? _pendingCfg;
    private IReadOnlyList<AppMenuTop> _appMenuTops = []; private IReadOnlyList<string>? _appMenuLabels; private IntPtr _appMenuWindow;
    private DateTime _lastPanelRefresh = DateTime.MinValue, _lastEvents = DateTime.MinValue; private int _panelTicks;

    /// <summary>Envía un cambio de ajustes al host (modo integrado). null = modo independiente: se guarda en bar.json.</summary>
    public Action<object>? PersistPatch { get; set; }

    /// <summary>Servicios de Windows que usa la barra (los sustituyen las pruebas).</summary>
    public SystemServices Services => _hub.Services;

    private void InitPanels(SystemServices? services)
    {
        _hub = new SystemHub(services ?? SystemServicesFactory.CreateReal(), Post, _log);
        _hub.Changed += OnHubChanged;
    }

    // ───────────────────────────── hilo de la barra
    private void Post(Action a) { lock (_uiQueue) _uiQueue.Enqueue(a); var h = _hwnd; if (h != IntPtr.Zero) BarNative.PostMessage(h, WM_APP_UI, IntPtr.Zero, IntPtr.Zero); }

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
        return new BarState(DateTime.Now, _app?.Name, _battery, vol, net, bt, _appMenuLabels);
    }

    private void OnHubChanged() { _ctl.State = MakeState(); _force = true; if (_popover is { IsOpen: true }) RebuildPanel(); }

    private void PanelsTick()
    {
        _popover?.Pump();
        if (_popover is not { IsOpen: true }) return;
        var now = DateTime.UtcNow; if ((now - _lastPanelRefresh).TotalSeconds < 2.5) return;
        _lastPanelRefresh = now; _panelTicks++;
        switch (_pkind)
        {
            case PanelKind.Wifi: _hub.RefreshWifi(scan: _panelTicks % 2 == 0); break;   // el escaneo de Windows se pide cada ~5 s
            case PanelKind.Bluetooth: _hub.RefreshBluetooth(true); break;
            case PanelKind.Sound or PanelKind.ControlCenter: _hub.RefreshAudio(); break;
            case PanelKind.Battery: _battery = SystemProviders.Battery(); _hub.RefreshSlow(); break;
        }

        if (_pkind == PanelKind.Calendar && (now - _lastEvents).TotalSeconds > 60) LoadEvents();
        RebuildPanel();
    }

    // ───────────────────────────── paneles
    private void OpenPanelFromBar(BarAction a, BarItemFrame? item)
    {
        if (a.Panel is not PanelKind kind) { if (a.Uri is not null) BarNative.ShellExecuteW(IntPtr.Zero, "open", a.Uri, null, null, 1); return; }
        if (_popover is { IsOpen: true } && _pkind == kind) { _popover.Hide(); return; }   // segundo clic en el mismo icono = cerrar
        int cx = item is null ? _winLeftPx + _winWpx / 2 : _winLeftPx + (int)((item.X + item.W / 2) * _scale);
        try { OpenPanel(kind, cx, alignRight: kind == PanelKind.ControlCenter); }
        catch (Exception e)
        {
            _log("[bar] no se pudo abrir el panel: " + e.GetType().Name + ": " + e.Message);   // nunca dejar al usuario sin acción: se abre la página de Configuración
            if (a.Uri is not null) BarNative.ShellExecuteW(IntPtr.Zero, "open", a.Uri, null, null, 1);
        }
    }

    private void OpenPanel(PanelKind kind, int anchorX, bool alignRight)
    {
        _popover ??= new PopoverWindow(OnPanelEvent, OnPanelClosed, _log);
        _pkind = kind; _anchorX = anchorX; _alignRight = alignRight; _panelTicks = 0; _lastPanelRefresh = DateTime.UtcNow;
        _pctx = PanelContext.Initial(_cfg, DateTime.Now) with { Sys = _hub.Snapshot, Battery = _battery };
        var model = PanelBuilder.Build(kind, _pctx);
        _popover.Show(model, anchorX, _winTopPx + _winHpx, _monitor, _scale, _theme, alignRight);
        RefreshFor(kind, firstOpen: true);
    }

    private void NavigatePanel(PanelKind kind)
    {
        if (_popover is not { IsOpen: true }) return;
        _pkind = kind; _pctx = _pctx with { Message = null, Busy = new Dictionary<string, string>() };
        _popover.SetModel(PanelBuilder.Build(kind, _pctx with { Sys = _hub.Snapshot, Battery = _battery, Now = DateTime.Now })); RefreshFor(kind, firstOpen: false);
    }

    private void RefreshFor(PanelKind kind, bool firstOpen)
    {
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
        _pctx = _pctx with { Sys = _hub.Snapshot, Battery = _battery, Now = DateTime.Now, Cfg = _cfg };
        _popover.SetModel(PanelBuilder.Build(_pkind, _pctx));
    }

    private void OnPanelClosed() { _force = true; }

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

    private void SetMessage(string? m) { _pctx = _pctx with { Message = m }; }
    private void SetBusy(string key, string? text)
    {
        var d = new Dictionary<string, string>(_pctx.Busy); if (text is null) d.Remove(key); else d[key] = text; _pctx = _pctx with { Busy = d };
    }

    /// <summary>Operación del sistema fuera del hilo de interfaz, con «ocupado» visible y el error (con su motivo) si falla. <paramref name="refresh"/> relee el estado real al terminar.</summary>
    private void Op(string busyKey, string busyText, Func<ServiceResult> op, Action refresh)
    {
        SetBusy(busyKey, busyText); SetMessage(null); RebuildPanel();
        _hub.Run(op, r =>
        {
            SetBusy(busyKey, null); if (!r.IsOk) SetMessage(r.Message ?? "La operación falló"); refresh(); RebuildPanel();
        });
    }

    private void OnPanelEvent(PanelEvent e)
    {
        try
        {
            if (e.Kind is PanelEventKind.SliderChange or PanelEventKind.SliderCommit) { OnSlider(e); return; }
            string id = e.Id;
            if (id == "back") { NavigatePanel(PanelKind.ControlCenter); return; }
            if (id.StartsWith("open:ms-settings:") || id.StartsWith("open:ms-windows-store:")) { _popover?.Hide(); BarNative.ShellExecuteW(IntPtr.Zero, "open", id[5..], null, null, 1); return; }
            if (id.StartsWith("open:")) { var k = id[5..] switch { "wifi" => PanelKind.Wifi, "bluetooth" => PanelKind.Bluetooth, "sound" => PanelKind.Sound, "battery" => PanelKind.Battery, "display" => PanelKind.Display, _ => PanelKind.ControlCenter }; NavigatePanel(k); return; }
            var s = _hub.Snapshot; var svc = _hub.Services;
            switch (id)
            {
                case "notifications": _popover?.Hide(); _hub.Run(() => { Thread.Sleep(250); return svc.Session.OpenNotificationCenter(); }, r => { if (!r.IsOk) ShowInfo("Notificaciones", r.Message); }); return;
                case "appearance": { bool dark = !s.Appearance.Dark; _hub.Patch(x => x with { Appearance = x.Appearance with { Dark = dark } }); Op("appearance", "Cambiando…", () => svc.Appearance.SetDark(dark), _hub.RefreshFast); return; }
                case "wifi-radio": { bool on = s.Wifi.RadioOn != true; Op("wifi-radio", on ? "Activando…" : "Desactivando…", () => svc.Wifi.SetRadio(on), () => { _hub.RefreshWifi(true); }); return; }
                case "bt-radio": { bool on = !s.Bluetooth.RadioOn; Op("bt-radio", on ? "Activando…" : "Desactivando…", () => svc.Bluetooth.SetRadio(on), () => _hub.RefreshBluetooth(true)); return; }
                case "mute": { bool m = !s.Audio.Muted; Op("mute", "…", () => svc.Audio.SetMuted(m), _hub.RefreshAudio); return; }
                case "audio-restore": { var prev = _hub.AudioImpl?.PreviousDefaultId; if (prev is null) return; Op("audio-restore", "Restaurando…", () => svc.Audio.SetDefaultOutput(prev), () => { _hub.RefreshAudio(); _hub.RefreshSlow(); }); return; }
                case "prev": case "next": { var (y, m) = CalendarGrid.Shift(_pctx.ViewYear, _pctx.ViewMonth, id == "next" ? 1 : -1); _pctx = _pctx with { ViewYear = y, ViewMonth = m }; RebuildPanel(); return; }
            }

            if (id.StartsWith("day:") && DateOnly.TryParseExact(id[4..], "yyyy-MM-dd", out var day)) { _pctx = _pctx with { SelectedDay = day }; RebuildPanel(); return; }
            if (id.StartsWith("net:") && _cfg.WifiDetails)   // con detalles activados la fila se despliega (la acción está dentro)
            { _pctx = _pctx with { ExpandedNet = _pctx.ExpandedNet == id[4..] ? null : id[4..] }; RebuildPanel(); return; }
            if (id.StartsWith("net:") || id.StartsWith("netact:"))
            {
                string ssid = id[(id.IndexOf(':') + 1)..], busy = "net:" + ssid;
                var net = s.Wifi.Networks.FirstOrDefault(n => n.Ssid == ssid); if (net is null) return;
                if (net.Connected) Op(busy, "Desconectando…", svc.Wifi.Disconnect, () => _hub.RefreshWifi(true));
                else Op(busy, "Conectando…", () => svc.Wifi.Connect(net.ProfileName ?? "", net.Ssid), () => { _hub.RefreshWifi(true); });
                return;
            }

            if (id.StartsWith("out:")) { var dev = id[4..]; Op(id, "Cambiando…", () => svc.Audio.SetDefaultOutput(dev), () => { _hub.RefreshAudio(); _hub.RefreshSlow(); }); return; }
            if (id.StartsWith("power:") && Enum.TryParse<PowerMode>(id[6..], out var pm)) { _hub.Patch(x => x with { Power = new PowerModeState(true, pm) }); Op(id, "Cambiando…", () => svc.Power.SetMode(pm), _hub.RefreshFast); return; }
            if (id.StartsWith("topology:") && Enum.TryParse<DisplayTopology>(id[9..], out var topo)) { _popover?.Hide(); _hub.Run(() => svc.Display.SetTopology(topo), r => { if (!r.IsOk) ShowInfo("Pantallas", r.Message); }); return; }
        }
        catch (Exception ex) { _log("[bar] evento de panel falló: " + ex.GetType().Name + ": " + ex.Message); }
    }

    private void OnSlider(PanelEvent e)
    {
        int v = (int)Math.Round(e.Value); var svc = _hub.Services; bool commit = e.Kind == PanelEventKind.SliderCommit;
        if (e.Id == "volume")
        {
            _hub.Patch(s => s with { Audio = s.Audio with { Volume = v } });   // la posición del deslizador sigue al puntero; al soltar se relee el valor REAL
            _hub.SetLatest("volume", v, svc.Audio.SetVolume, r => { if (!r.IsOk) { SetMessage(r.Message); } if (commit || !r.IsOk) _hub.RefreshAudio(); RebuildPanel(); });
        }
        else if (e.Id == "brightness")
        {
            var mon = _hub.Snapshot.Brightness.Monitors.ToList(); if (mon.Count == 0) return;
            _hub.Patch(s => s with { Brightness = s.Brightness with { Monitors = s.Brightness.Monitors.Select(m => m with { Percent = v }).ToList() } });
            _hub.SetLatest("brightness", v, p => { ServiceResult last = ServiceResult.Ok; foreach (var m in mon) { last = svc.Brightness.Set(m.Id, p); if (!last.IsOk) break; } return last; },
                r => { if (!r.IsOk) SetMessage(r.Message); if (commit || !r.IsOk) _hub.RefreshBrightness(true); RebuildPanel(); });
        }

        RebuildPanel();
    }

    // ───────────────────────────── cuadros de diálogo propios
    private void ShowInfo(string title, string? text) { if (string.IsNullOrWhiteSpace(text)) return; ModalBox(title, text!, 0x40 /* ICONINFORMATION | OK */); }

    private bool Confirm(string title, string text) => ModalBox(title, text, 0x4 | 0x30 | 0x100) == 6;   // YESNO | ICONWARNING | DEFBUTTON2 (por defecto «No»)

    private int ModalBox(string title, string text, uint flags)
    {
        if (_popover is not null) _popover.KeepOpen = true; _modal = true;
        try { return BarNative.MessageBoxW(_hwnd, text, title, flags | 0x40000 /* TOPMOST */ | 0x10000 /* SETFOREGROUND */); }
        finally { _modal = false; if (_popover is not null) _popover.KeepOpen = false; }
    }

    // ───────────────────────────── menú Apple
    private void ShowAppleMenu(BarItemFrame? item)
    {
        IntPtr menu = BarNative.CreatePopupMenu(); IntPtr prev = BarNative.GetForegroundWindow();
        string? appName = SessionService.ForegroundProcessName();
        try
        {
            _popover?.Hide();
            const uint Grayed = 0x1, Sep = 0x800;
            void Add(SystemActionKind k, bool enabled = true, string? label = null) => BarNative.AppendMenuW(menu, enabled ? 0u : Grayed, (UIntPtr)(2000 + (int)k), label ?? SystemActions.Get(k).Label);
            void Line() => BarNative.AppendMenuW(menu, Sep, UIntPtr.Zero, null);
            Add(SystemActionKind.About); Line();
            Add(SystemActionKind.Settings); Add(SystemActionKind.Store); Line();
            Add(SystemActionKind.ForceQuit, appName is not null, appName is null ? "Forzar salida (no hay una app que se pueda cerrar)" : $"Forzar salida de «{appName}»…"); Line();
            Add(SystemActionKind.Sleep); Add(SystemActionKind.Restart); Add(SystemActionKind.Shutdown); Line();
            Add(SystemActionKind.Lock); Add(SystemActionKind.LogOff); Line();
            BarNative.AppendMenuW(menu, _cfg.ReserveSpace ? 0x8u : 0u, (UIntPtr)1, "Reservar espacio arriba");
            BarNative.AppendMenuW(menu, 0, (UIntPtr)2, "Salir de la barra superior");
            int x = _winLeftPx + (int)((item?.X ?? 0) * _scale), y = _winTopPx + _winHpx;
            BarNative.SetForegroundWindow(_hwnd);
            _modal = true; int cmd;
            try { cmd = BarNative.TrackPopupMenuEx(menu, 0x100 | 0x80 | 0x2 /* RETURNCMD|NONOTIFY|RIGHTBUTTON */, x, y, _hwnd, IntPtr.Zero); } finally { _modal = false; }
            BarNative.PostMessage(_hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            if (prev != IntPtr.Zero && prev != _hwnd) BarNative.SetForegroundWindow(prev);   // la acción se ejecuta con el foco de vuelta en la app que lo tenía
            if (cmd == 1) SetReserve(!_cfg.ReserveSpace);
            else if (cmd == 2) _quitting = true;
            else if (cmd >= 2000) RunSystemAction((SystemActionKind)(cmd - 2000), appName);
        }
        catch (Exception e) { _log("Menú falló: " + e.Message); }
        finally { BarNative.DestroyMenu(menu); }
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

    // ───────────────────────────── menús de la app activa (EXPERIMENTAL)
    private void RefreshAppMenus()
    {
        if (!_cfg.AppMenus) { _appMenuTops = []; _appMenuLabels = null; return; }
        var h = BarNative.GetForegroundWindow(); if (h == IntPtr.Zero) return;
        BarNative.GetWindowThreadProcessId(h, out uint pid); if (pid == (uint)Environment.ProcessId) return;   // un clic en la propia barra no cambia los menús
        var cls = AppMenuService.ClassOf(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") { _appMenuTops = []; _appMenuLabels = null; return; }
        var tops = AppMenuService.Read(h); _appMenuTops = tops; _appMenuWindow = h; _appMenuLabels = tops.Count > 0 ? tops.Select(t => t.Text).ToList() : null;
    }

    private void ShowAppMenu(int index, BarItemFrame? item)
    {
        if (index < 0 || index >= _appMenuTops.Count || _appMenuWindow == IntPtr.Zero) return;
        _popover?.Hide();
        int x = _winLeftPx + (int)((item?.X ?? 0) * _scale), y = _winTopPx + _winHpx; IntPtr prev = BarNative.GetForegroundWindow();
        _modal = true;
        try { var r = AppMenuService.Show(_hwnd, _appMenuWindow, _appMenuTops[index], x, y); if (!r.IsOk) _log("[bar] menú de la app: " + r.Message); }
        finally { _modal = false; if (prev != IntPtr.Zero && prev != _hwnd) BarNative.SetForegroundWindow(prev); }
    }

    // ───────────────────────────── configuración central en vivo
    /// <summary>Aplica una configuración nueva (la recibe del host). Seguro desde cualquier hilo; se aplica en el hilo de la barra y solo toca lo que cambió.</summary>
    public void ApplyConfig(BarConfig c) { lock (_cfgLock) _pendingCfg = c; var h = _hwnd; if (h != IntPtr.Zero) BarNative.PostMessage(h, WM_APP_CONFIG, IntPtr.Zero, IntPtr.Zero); }

    private void ApplyPendingConfig()
    {
        BarConfig? c; lock (_cfgLock) { c = _pendingCfg; _pendingCfg = null; }
        if (c is null || c == _cfg) return;
        var old = _cfg; _cfg = c; _ctl.Config = c;
        if (old.ReserveSpace != c.ReserveSpace) PlaceWindow();
        if (old.Background != c.Background) { _painter.Background = c.Background; BuildBackdrop(); }
        if (old.Theme != c.Theme) { _theme = ResolveTheme(); _painter.Theme = _theme; BuildBackdrop(); }
        if (old.AppMenus != c.AppMenus) { if (c.AppMenus) RefreshAppMenus(); else { _appMenuTops = []; _appMenuLabels = null; } }
        if (old.CalendarIcsPath != c.CalendarIcsPath && _pkind == PanelKind.Calendar && _popover is { IsOpen: true }) LoadEvents();
        _nextClock = DateTime.MinValue; UpdateState(force: false); _force = true;
        if (_popover is { IsOpen: true }) RebuildPanel();
    }

    private void DisposePanels() { _popover?.Dispose(); _popover = null; }
}
