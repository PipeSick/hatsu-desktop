using System.Diagnostics;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Shell;
using WinMac.Dock;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// La barra superior de WinMac: UNA ventana por monitor activo (<see cref="BarWindow"/>), todas en el mismo hilo y con la misma configuración, estado del sistema y panel desplegable.
/// Este anfitrión detecta los cambios de pantallas SIN reiniciar (conectar/desconectar, duplicar/extender, resolución, DPI), crea y libera las barras que correspondan, y reparte a cada una el
/// espacio que la Isla necesita en SU monitor. En modo espejo (duplicar) no hay barras repetidas. Reposo = sin render: el bucle espera con tiempo límite de 50 ms.
/// </summary>
public sealed partial class BarHost : IDisposable
{
    private const int WM_QUIT = 0x12, WM_APP_YIELD = 0x8002, WM_APP_UI = 0x8003, WM_APP_CONFIG = 0x8004;
    public const string QuitEventName = @"Local\WinMac.Bar.Quit";
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    private BarConfig _cfg; private readonly string? _cfgPath; private readonly Action<string> _log;
    private readonly ActiveAppProvider? _app; private readonly EventWaitHandle? _quit;
    private readonly List<BarWindow> _wins = [];   // el monitor principal primero
    private readonly TopologyWatcher _topo = new(SettleTime);
    private HubTarget? _hubTarget; private volatile IntPtr _hubHwnd;
    private volatile bool _quitting; private BarTheme _theme;
    private DateTime _lastBt = DateTime.MinValue, _lastBattery = DateTime.MinValue, _lastVolume = DateTime.MinValue, _lastNetwork = DateTime.MinValue, _lastTheme = DateTime.MinValue, _nextClock = DateTime.MinValue;
    private DateTime _nextTopoPoll = DateTime.MinValue, _fastPollUntil = DateTime.MinValue, _retryReconcileAfter = DateTime.MinValue;
    private BatteryInfo _battery = BatteryInfo.None; private NetworkInfo _network = NetworkInfo.Unknown;

    private readonly object _yieldLock = new(); private IReadOnlyList<PxRect> _holes = [];

    internal BarConfig Config => _cfg;
    internal BarTheme Theme => _theme;
    internal IReadOnlyList<PxRect> Holes { get { lock (_yieldLock) return _holes; } }

    public string Diagnostics => _wins.Count > 0 ? _wins[0].Diagnostics : "";

    /// <summary>Número de barras (monitores con barra) en este momento.</summary>
    public int BarCount => _wins.Count;

    /// <summary>
    /// Espacio de pantalla que otra ventana «siempre encima» (la Isla) necesita libre: cada barra recorta SU ventana donde ese espacio la toca, sin cubrirlo ni quedarse con sus clics y sin depender
    /// del orden Z. Un hueco de un monitor no recorta la barra de otro. Lista vacía = ventanas completas. Seguro desde cualquier hilo (se aplica en el hilo de la barra).
    /// </summary>
    public void SetYield(IReadOnlyList<PxRect> holes)
    {
        lock (_yieldLock) _holes = holes;
        var h = _hubHwnd; if (h != IntPtr.Zero) BarNative.PostMessage(h, WM_APP_YIELD, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Pide el cierre normal de la barra (lo usa el host de WinMac vía <c>--hosted</c>). Seguro desde otro hilo.</summary>
    public void RequestQuit() => _quitting = true;

    private long _lastTickMs = Environment.TickCount64; private volatile bool _modal;
    /// <summary>¿La interfaz sigue viva? El bucle principal avanza al menos cada 50 ms; un menú contextual abierto (bucle modal de Windows) cuenta como vivo. El host lo usa para el latido.</summary>
    public bool Responsive => _modal || Environment.TickCount64 - Volatile.Read(ref _lastTickMs) < 10_000;
    /// <summary>true si alguna barra tiene el AppBar registrado.</summary>
    public bool Reserved => _wins.Any(w => w.Registered);

    public BarHost(BarConfig cfg, string? configPath, Action<string> log, bool trackActiveApp = true, WinMac.SystemControl.SystemServices? services = null, WinMac.Bar.Hub.HubServices? hubServices = null)
    {
        _cfg = cfg; _cfgPath = configPath; _log = log; InitPanels(services, hubServices);
        _theme = ResolveTheme();
        if (trackActiveApp) _app = new ActiveAppProvider();
        try { _quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName); } catch (Exception e) { log("No se pudo crear el evento de cierre: " + e.Message); }
    }

    // ------------------------------------------------------------------ ciclo de vida
    public int Run(int maxFrames = int.MaxValue, bool selfTest = false)
    {
        CreateHub();
        StartTray();
        StartLauncher();
        StartSpotlight();
        Reconcile(initial: true);
        ApplyPendingConfig();   // una configuración que llegó del host antes de que existieran las ventanas
        var clock = Stopwatch.StartNew(); double last = 0; int frames = 0;
        while (true)
        {
            while (BarNative.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { if (msg.message == WM_QUIT) return 0; BarNative.TranslateMessage(ref msg); BarNative.DispatchMessage(ref msg); }
            Volatile.Write(ref _lastTickMs, Environment.TickCount64);
            DrainUi(); _app?.Pump(); PollTopology(); foreach (var w in _wins.ToArray()) Guard(w, w.Poll); Housekeeping(); PanelsTick();
            if (_quitting) return 0;
            if (selfTest && clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("selftest: no se alcanzaron los frames en 10 s");
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.1); last = now;
            bool rendered = false, needs = false;
            foreach (var w in _wins.ToArray()) { Guard(w, () => rendered |= w.Frame(dt, selfTest)); needs |= w.NeedsFrames; }
            if (rendered) frames++;
            if (frames >= maxFrames) return 0;
            if (!needs && !selfTest) BarNative.MsgWaitForMultipleObjectsEx(0, null, _popover?.Animating == true || LauncherAnimating ? 8u : 50u, BarNative.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);   // entrada/salida del panel lateral: pasos cortos solo mientras dura
        }
    }

    private int _quarantines; private DateTime _lastQuarantine = DateTime.MinValue;

    /// <summary>
    /// Cada barra es independiente: si la de un monitor falla (dispositivo de gráficos perdido, ventana destruida por fuera…), se libera SOLO esa y se vuelve a crear en el siguiente repaso de monitores;
    /// las demás siguen. Si fallan demasiadas veces seguidas no se insiste: se deja caer el proceso para que el host lo reinicie limpio.
    /// </summary>
    private void Guard(BarWindow w, Action body)
    {
        try { body(); }
        catch (Exception e)
        {
            _log($"[bar] la barra de {w.Info.Id} falló y se recreará: {e.GetType().Name}: {e.Message}");
            var nowU = DateTime.UtcNow; if (nowU - _lastQuarantine > TimeSpan.FromMinutes(1)) _quarantines = 0; _lastQuarantine = nowU;   // fallos aislados no se acumulan
            if (++_quarantines > 5) throw;
            _wins.Remove(w); if (ReferenceEquals(_popSource, w)) { _popSource = null; _popover?.Hide(); } if (ReferenceEquals(_actionSource, w)) _actionSource = null;
            try { w.Dispose(); } catch (Exception) { /* ya estaba rota */ }
            _topo.MarkApplied("", DateTime.UtcNow); _nextTopoPoll = DateTime.MinValue;   // la huella aplicada ya no coincide con la realidad: el próximo repaso recrea la barra que falta
        }
    }

    private BarTheme ResolveTheme() => _cfg.Theme switch { BarThemeMode.Dark => BarTheme.Dark, BarThemeMode.Light => BarTheme.Light, _ => SystemTheme.Current() == DockTheme.Dark ? BarTheme.Dark : BarTheme.Light };

    // ------------------------------------------------------------------ monitores
    /// <summary>Una barra por monitor activo; el modo espejo (mismos límites) cuenta como UN monitor. Crea, recoloca o libera las que hagan falta.</summary>
    private void Reconcile(bool initial)
    {
        IReadOnlyList<MonitorSnapshot> snaps;
        try { snaps = MonitorEnumerator.ForBars(); }
        catch (Exception e) { _log("[bar] no se pudieron leer los monitores: " + e.GetType().Name + ": " + e.Message); _retryReconcileAfter = DateTime.UtcNow.AddSeconds(5); return; }

        var wanted = snaps.Select(s => s.Info).ToList(); string signature = MonitorPlan.Signature(wanted);
        var diff = MonitorPlan.Diff(wanted, _wins.ToDictionary(w => w.Info.Id, w => w.Info));
        bool failed = false;
        if (!diff.IsEmpty)
        {
            _log($"[bar] monitores: {wanted.Count} activo(s) [{string.Join("; ", wanted.Select(m => $"{m.Id} {m.Bounds.W}x{m.Bounds.H}@({m.Bounds.X},{m.Bounds.Y}) x{m.Scale:0.##}{(m.Primary ? " principal" : "")}"))}]; crear={diff.Add.Count} liberar={diff.Remove.Count} recolocar={diff.Update.Count}");
            if (_popover is { IsOpen: true }) _popover.Hide();   // el panel abierto apuntaba a una disposición que ya no existe
            foreach (var id in diff.Remove)
            {
                var w = _wins.First(x => x.Info.Id == id); _wins.Remove(w);
                if (ReferenceEquals(_popSource, w)) _popSource = null;
                if (ReferenceEquals(_actionSource, w)) _actionSource = null;
                try { w.Dispose(); } catch (Exception e) { _log($"[bar] liberar la barra de {id} falló: {e.GetType().Name}: {e.Message}"); }
            }

            foreach (var info in diff.Update)
            {
                try { _wins.First(x => x.Info.Id == info.Id).Relayout(snaps.First(s => s.Info.Id == info.Id)); }
                catch (Exception e) { failed = true; _log($"[bar] recolocar la barra de {info.Id} falló: {e.GetType().Name}: {e.Message}"); }
            }

            foreach (var info in diff.Add)
            {
                var w = new BarWindow(this, snaps.First(s => s.Info.Id == info.Id), _cfg, MakeState(), _log);
                try { w.Create(); _wins.Add(w); }
                catch (Exception e) { failed = true; _log($"[bar] crear la barra de {info.Id} falló: {e.GetType().Name}: {e.Message}"); try { w.Dispose(); } catch (Exception) { /* a medio crear */ } }
            }

            var ordered = _wins.OrderByDescending(w => w.Info.Primary).ThenBy(w => w.Info.Bounds.X).ThenBy(w => w.Info.Bounds.Y).ToList(); _wins.Clear(); _wins.AddRange(ordered);
            foreach (var w in _wins) w.ApplyYield();   // el espacio de la Isla se reparte entre las barras que hay ahora
            UpdateState(force: initial);
        }

        if (failed) _retryReconcileAfter = DateTime.UtcNow.AddSeconds(5); else _topo.MarkApplied(signature, DateTime.UtcNow);
    }

    private void PollTopology()
    {
        var now = DateTime.UtcNow; if (now < _nextTopoPoll) return;
        _nextTopoPoll = now.AddMilliseconds(now < _fastPollUntil ? 250 : 1500);   // tras un aviso se mira más seguido; en reposo, cada tanto por si el aviso se perdió
        string sig; try { sig = MonitorPlan.Signature(MonitorEnumerator.ForBars().Select(s => s.Info)); } catch (Exception) { return; }
        if (now >= _retryReconcileAfter && _topo.ShouldReconcile(sig, now)) Reconcile(initial: false);
    }

    /// <summary>Windows avisó de un cambio de pantallas (llega a cada ventana de barra). Se espera a que se asiente antes de reconciliar.</summary>
    internal void NotifyDisplayChange()
    {
        var now = DateTime.UtcNow; _topo.Notify(now); _fastPollUntil = now.AddSeconds(8); _nextTopoPoll = DateTime.MinValue;
    }

    /// <summary>Descripción legible de los monitores y de qué barras les tocan (para <c>--selftest-monitors</c> y los registros). No cambia nada.</summary>
    public static IReadOnlyList<string> DescribeMonitors()
    {
        var lines = new List<string>(); var all = MonitorEnumerator.Enumerate(); var bars = MonitorEnumerator.ForBars().Select(s => s.Info.Id).ToHashSet();
        lines.Add($"Windows reporta {all.Count} monitor(es); {bars.Count} llevará(n) barra.");
        foreach (var s in all)
        {
            var m = s.Info;
            lines.Add($"  {(bars.Contains(m.Id) ? "[BARRA]" : "[espejo]")} {m.Id}: {m.Bounds.W}x{m.Bounds.H} en ({m.Bounds.X},{m.Bounds.Y}), área de trabajo {m.Work.W}x{m.Work.H}, escala {m.Scale:0.##}{(m.Primary ? ", PRINCIPAL" : "")}");
        }

        return lines;
    }

    // ------------------------------------------------------------------ estado del sistema
    private void Housekeeping()
    {
        if (_quit is not null && _quit.WaitOne(0)) { _quitting = true; return; }
        var now = DateTime.Now; bool changed = false;
        if (now >= _nextClock) { _nextClock = now + ClockFormat.UntilNextChange(now, _cfg); changed = true; }
        if ((now - _lastBattery).TotalSeconds >= 20) { _lastBattery = now; var b = SystemProviders.Battery(); if (b != _battery) { _battery = b; changed = true; } }
        if ((now - _lastVolume).TotalMilliseconds >= 500) { _lastVolume = now; _hub.RefreshAudio(); }   // la lectura corre en un hilo de trabajo; el resultado llega por Changed
        if ((now - _lastNetwork).TotalSeconds >= 5) { _lastNetwork = now; _hub.RefreshWifi(false); var n = SystemProviders.Network(); if (n.Kind != _network.Kind) { _network = n; changed = true; } }
        if ((now - _lastBt).TotalSeconds >= 15) { _lastBt = now; _hub.RefreshBluetooth(false); }
        if (_app is not null && _app.TakeChanged()) { changed = true; RefreshAppMenus(); }
        if ((now - _lastTheme).TotalSeconds >= 5) { _lastTheme = now; var t = ResolveTheme(); if (t != _theme) SetThemeAll(t); }
        if (changed) UpdateState(force: false);
    }

    private void SetThemeAll(BarTheme t) { _theme = t; foreach (var w in _wins) w.SetTheme(t); }

    private void UpdateState(bool force)
    {
        SetStateAll();
        if (force) { _battery = SystemProviders.Battery(); _network = SystemProviders.Network(); _hub.RefreshAudio(); _hub.RefreshWifi(false); _hub.RefreshBluetooth(false); SetStateAll(); }
    }

    private void SetStateAll() { var s = MakeState(); foreach (var w in _wins) w.SetState(s); }

    // ------------------------------------------------------------------ ventana de servicio (mensajes entre hilos)
    private sealed class HubTarget(BarHost host) : IWndTarget
    {
        public IntPtr OnMessage(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            try
            {
                switch ((int)msg)
                {
                    case WM_APP_YIELD: foreach (var win in host._wins.ToArray()) win.ApplyYield(); return IntPtr.Zero;
                    case WM_APP_UI: host.DrainUi(); return IntPtr.Zero;
                    case WM_APP_CONFIG: host.ApplyPendingConfig(); return IntPtr.Zero;
                }
            }
            catch (Exception e) { host._log($"[bar] mensaje de servicio {msg:X} falló: {e.GetType().Name}: {e.Message}"); }
            return BarNative.DefWindowProc(h, msg, w, l);
        }
    }

    /// <summary>Ventana invisible que solo recibe mensajes (no es una barra): para despertar el hilo de la barra desde otros hilos aunque no haya ninguna barra visible un instante.</summary>
    private void CreateHub()
    {
        if (_hubHwnd != IntPtr.Zero) return;
        WndRouter.EnsureClass("WinMacBarHub");
        var h = BarNative.CreateWindowEx(0, "WinMacBarHub", "WinMac Barra (servicio)", 0, 0, 0, 0, 0, new IntPtr(-3) /* HWND_MESSAGE */, IntPtr.Zero, BarNative.GetModuleHandle(null), IntPtr.Zero);
        if (h == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana de servicio de la barra.");
        _hubTarget = new HubTarget(this); WndRouter.Register(h, _hubTarget); _hubHwnd = h;
    }

    // ------------------------------------------------------------------ acciones
    internal void OnBarAction(BarWindow src, BarAction a, BarItemFrame? item) => Execute(src, a, item);

    private void Execute(BarWindow src, BarAction a, BarItemFrame? item)
    {
        _actionSource = src;
        try
        {
            switch (a.Kind)
            {
                case BarActionKind.OpenSettings: if (a.Uri is not null) BarNative.ShellExecuteW(IntPtr.Zero, "open", a.Uri, null, null, 1); break;
                case BarActionKind.Quit: _quitting = true; break;
                case BarActionKind.ShowMenu: ShowAppleMenu(src, item); break;
                case BarActionKind.OpenPanel: OpenPanelFromBar(src, a, item); break;
                case BarActionKind.ShowAppMenu: ShowAppMenu(src, a.Index, item); break;
            }
        }
        catch (Exception e) { _log($"Acción {a.Kind} falló: {e.GetType().Name}: {e.Message}"); }
    }

    public string Report() => $"{Diagnostics}; {_wins.Count} barra(s): {string.Join(" | ", _wins.Select(w => w.Report()))}; tema={_theme}; batería={_battery.Present}; volumen={_hub.Snapshot.Audio.Available}; red={_network.Kind}";

    public void Dispose()
    {
        StopNotchFeed();
        StopSpotlight();
        StopLauncher();
        StopTray();
        DisposePanels();
        foreach (var w in _wins) { try { w.Dispose(); } catch (Exception) { /* seguimos liberando las demás */ } }
        _wins.Clear();
        if (_hubHwnd != IntPtr.Zero) { WndRouter.Unregister(_hubHwnd); BarNative.DestroyWindow(_hubHwnd); _hubHwnd = IntPtr.Zero; }
        _app?.Dispose(); _quit?.Dispose();
    }
}
