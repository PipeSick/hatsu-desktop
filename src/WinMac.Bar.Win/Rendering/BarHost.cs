using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Apps.Win;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Shell;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Ventana de la barra superior: Win32 + DirectComposition + Direct2D (misma base que el Dock). Reserva el borde superior con un AppBar real (SHAppBarMessage, opcional) y lo libera
/// siempre al salir (ABM_REMOVE; si el proceso muere, Windows restaura el área de trabajo). No toca la barra de tareas. Reposo = sin render: el bucle espera con tiempo límite de 50 ms.
/// </summary>
public sealed class BarHost : IDisposable
{
    private const int WM_DESTROY = 2, WM_MOUSEACTIVATE = 0x21, WM_DISPLAYCHANGE = 0x7E, WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_DPICHANGED = 0x2E0, WM_QUIT = 0x12, WM_APP_BAR = 0x8001;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const string QuitEventName = @"Local\WinMac.Bar.Quit";

    private BarConfig _cfg; private readonly string? _cfgPath; private readonly Action<string> _log; private readonly BarMetrics _m = new();
    private readonly BarController _ctl; private readonly BarNative.WndProc _proc;
    private readonly ActiveAppProvider? _app; private readonly EventWaitHandle? _quit;
    private IntPtr _hwnd; private double _scale = 1; private int _winLeftPx, _winTopPx, _winWpx, _winHpx; private BarNative.RECT _monitor;
    private D2DDevice _dev = null!; private IDXGISwapChain1 _swap = null!; private ID2D1Bitmap1? _bmp, _blurred;
    private IDCompositionDevice _dcomp = null!; private IDCompositionTarget _target = null!; private IDCompositionVisual _visual = null!;
    private BarPainter _painter = null!; private readonly WallpaperSource _wall = new(); private BarView _view = null!;
    private bool _force = true, _registered, _hiddenForFullscreen; private volatile bool _quitting; private uint _taskbarCreated; private BarTheme _theme;
    private DateTime _lastFs = DateTime.MinValue, _lastBattery = DateTime.MinValue, _lastVolume = DateTime.MinValue, _lastNetwork = DateTime.MinValue, _lastTheme = DateTime.MinValue, _nextClock = DateTime.MinValue;
    private BatteryInfo _battery = BatteryInfo.None; private VolumeInfo _volume = VolumeInfo.None; private NetworkInfo _network = NetworkInfo.Unknown;
    private double? _px, _py;

    public string Diagnostics { get; private set; } = "";

    /// <summary>Pide el cierre normal de la barra (lo usa el host de WinMac vía <c>--hosted</c>). Seguro desde otro hilo.</summary>
    public void RequestQuit() => _quitting = true;

    private long _lastTickMs = Environment.TickCount64; private volatile bool _modal;
    /// <summary>¿La interfaz sigue viva? El bucle principal avanza al menos cada 50 ms; un menú contextual abierto (bucle modal de Windows) cuenta como vivo. El host lo usa para el latido.</summary>
    public bool Responsive => _modal || Environment.TickCount64 - Volatile.Read(ref _lastTickMs) < 10_000;
    /// <summary>true = registrar el AppBar. Se puede cambiar en marcha desde el menú.</summary>
    public bool Reserved => _registered;

    public BarHost(BarConfig cfg, string? configPath, Action<string> log, bool trackActiveApp = true)
    {
        _cfg = cfg; _cfgPath = configPath; _log = log; _proc = WndProcImpl;
        _ctl = new BarController(_m, cfg, BarState.Empty(DateTime.Now));
        if (trackActiveApp) _app = new ActiveAppProvider();
        try { _quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName); } catch (Exception e) { log("No se pudo crear el evento de cierre: " + e.Message); }
    }

    // ------------------------------------------------------------------ ciclo de vida
    public int Run(int maxFrames = int.MaxValue, bool selfTest = false)
    {
        Create();
        var clock = Stopwatch.StartNew(); double last = 0; int frames = 0;
        while (true)
        {
            while (BarNative.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { if (msg.message == WM_QUIT) return 0; BarNative.TranslateMessage(ref msg); BarNative.DispatchMessage(ref msg); }
            Volatile.Write(ref _lastTickMs, Environment.TickCount64);
            _app?.Pump(); PollInputs(); Housekeeping();
            if (_quitting) return 0;
            if (selfTest) { _force = true; if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("selftest: no se alcanzaron los frames en 10 s"); }
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.1); last = now;
            _view = _ctl.Step(dt);
            if (_view.NeedsFrames || _force) { if (!_hiddenForFullscreen) Render(); _force = false; frames++; }
            if (frames >= maxFrames) return 0;
            if (!_view.NeedsFrames && !selfTest) BarNative.MsgWaitForMultipleObjectsEx(0, null, 50, BarNative.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);
        }
    }

    private void Create()
    {
        var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = "WinMacBar", hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
        BarNative.RegisterClassEx(ref cls);
        _hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "WinMacBar", "WinMac Barra superior", WS_POPUP, 0, 0, 100, 30, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());
        _taskbarCreated = BarNative.RegisterWindowMessageW("TaskbarCreated");   // Explorer se reinició: hay que volver a registrar el AppBar
        _dev = new D2DDevice(); Diagnostics = _dev.Description;
        _scale = BarNative.GetDpiForWindow(_hwnd) / 96.0; _dev.SetDpi(_scale);
        _theme = ResolveTheme(); _painter = new BarPainter(_dev, _m) { Theme = _theme, Background = _cfg.Background };
        _ctl.Measure = (t, b) => _painter.Measure(t, b);
        Metrics(); _ctl.SetWidth(_monitor.Width / _scale);
        CreateSurface(); PlaceWindow(); BuildBackdrop(); UpdateState(force: true);
        LogSizes("inicio");
    }

    /// <summary>Ancho/alto reales en píxeles: la barra ocupa todo el ancho del monitor principal.</summary>
    private void Metrics()
    {
        var mi = new BarNative.MONITORINFO { cbSize = (uint)Marshal.SizeOf<BarNative.MONITORINFO>() };
        BarNative.GetMonitorInfo(BarNative.MonitorFromPoint(new BarNative.POINT(), BarNative.MONITOR_DEFAULTTOPRIMARY), ref mi);
        _monitor = mi.rcMonitor; _winWpx = _monitor.Width; _winHpx = (int)Math.Ceiling(_m.Height * _scale);
    }

    private BarTheme ResolveTheme() => _cfg.Theme switch { BarThemeMode.Dark => BarTheme.Dark, BarThemeMode.Light => BarTheme.Light, _ => SystemTheme.Current() == DockTheme.Dark ? BarTheme.Dark : BarTheme.Light };

    // ------------------------------------------------------------------ AppBar
    private void PlaceWindow()
    {
        Metrics(); _winLeftPx = _monitor.Left; _winTopPx = _monitor.Top;
        if (_cfg.ReserveSpace) AppBarSetPos(); else AppBarRemove();
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), _winLeftPx, _winTopPx, _winWpx, _winHpx, 0x0010u | 0x0040u /* NOACTIVATE | SHOWWINDOW */);
    }

    private void AppBarSetPos()
    {
        var abd = new BarNative.APPBARDATA { cbSize = (uint)Marshal.SizeOf<BarNative.APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = WM_APP_BAR };
        if (!_registered) { _registered = BarNative.SHAppBarMessage(BarNative.ABM_NEW, ref abd) != 0; if (!_registered) { _log("[bar] ABM_NEW falló: la barra se muestra superpuesta, sin reservar espacio."); return; } }
        abd.uEdge = BarNative.ABE_TOP; abd.rc = new BarNative.RECT { Left = _monitor.Left, Top = _monitor.Top, Right = _monitor.Right, Bottom = _monitor.Top + _winHpx };
        BarNative.SHAppBarMessage(BarNative.ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + _winHpx;   // QUERYPOS ajusta el borde; la altura la fijamos nosotros
        BarNative.SHAppBarMessage(BarNative.ABM_SETPOS, ref abd);
        _winLeftPx = abd.rc.Left; _winTopPx = abd.rc.Top; _winWpx = abd.rc.Width;
        _log($"[bar] AppBar reservado: ({abd.rc.Left},{abd.rc.Top})-({abd.rc.Right},{abd.rc.Bottom}) px");
    }

    private void AppBarRemove()
    {
        if (!_registered) return;
        var abd = new BarNative.APPBARDATA { cbSize = (uint)Marshal.SizeOf<BarNative.APPBARDATA>(), hWnd = _hwnd };
        BarNative.SHAppBarMessage(BarNative.ABM_REMOVE, ref abd); _registered = false; _log("[bar] AppBar liberado.");
    }

    // ------------------------------------------------------------------ gráficos
    private void CreateSurface()
    {
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        _swap = factory.CreateSwapChainForComposition(_dev.D3D, new SwapChainDescription1
        {
            Width = (uint)_winWpx, Height = (uint)_winHpx, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch,
        });
        BindTarget();
        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi);
        _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
        _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _target.SetRoot(_visual); _dcomp.Commit();
    }

    private void BindTarget()
    {
        using var surface = _swap.GetBuffer<IDXGISurface>(0); _bmp?.Dispose();
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
    }

    /// <summary>Cambio de monitor/DPI: se recrea la superficie completa (no ResizeBuffers, lección aprendida en el Dock).</summary>
    private void ResizeWindow()
    {
        _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null); _bmp?.Dispose(); _bmp = null; _dev.Context.Target = null;
        PlaceWindow(); _visual?.Dispose(); _target?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose();
        CreateSurface(); _ctl.SetWidth(_winWpx / _scale); BuildBackdrop(); _force = true; LogSizes("redimensionado");
    }

    private void BuildBackdrop()
    {
        _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null);
        if (_cfg.Background != BarBackground.Glass) return;
        try
        {
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) { _log("Fondo de pantalla no disponible: vidrio sin fondo."); return; }
            var region = new RectD(0, 0, _monitor.Width, _winHpx); var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _monitor.Width, _monitor.Height, region);
            var style = GlassStyle.For(_theme == BarTheme.Dark ? DockTheme.Dark : DockTheme.Light) with { BlurSigma = 16 };
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _winWpx / _scale, _winHpx / _scale, _scale, style); _painter.SetBackdrop(_blurred); _dev.Context.Target = _bmp;
        }
        catch (Exception e) { _log("BuildBackdrop falló: " + e.Message); _blurred = null; _painter.SetBackdrop(null); _dev.Context.Target = _bmp; }
    }

    private void Render() { _painter.Paint(_view); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); }

    private void LogSizes(string why)
    {
        try { var d = _swap.Description1; _log($"[bar] {why}: escala={_scale:0.##} esperado={_winWpx}x{_winHpx}px búfer={d.Width}x{d.Height}px tema={_theme} fondo={_cfg.Background}"); }
        catch (Exception e) { _log("[bar] LogSizes falló: " + e.Message); }
    }

    // ------------------------------------------------------------------ estado del sistema
    private void PollInputs()
    {
        if (!BarNative.GetCursorPos(out var p)) return;
        bool inside = p.X >= _winLeftPx && p.X < _winLeftPx + _winWpx && p.Y >= _winTopPx && p.Y < _winTopPx + _winHpx;
        double? x = inside ? (p.X - _winLeftPx) / _scale : null, y = inside ? (p.Y - _winTopPx) / _scale : null;
        if (x != _px || y != _py) { _px = x; _py = y; _ctl.SetPointer(x, y); }
        if ((DateTime.UtcNow - _lastFs).TotalMilliseconds > 1000)
        {
            _lastFs = DateTime.UtcNow; bool fs = FullscreenWatcher.ShouldSuppress();
            if (fs != _hiddenForFullscreen) { _hiddenForFullscreen = fs; BarNative.ShowWindow(_hwnd, fs ? 0 : 4 /* SW_SHOWNOACTIVATE */); _force = true; }
        }
    }

    private void Housekeeping()
    {
        if (_quit is not null && _quit.WaitOne(0)) { _quitting = true; return; }
        var now = DateTime.Now; bool changed = false;
        if (now >= _nextClock) { _nextClock = now + ClockFormat.UntilNextChange(now, _cfg); changed = true; }
        if ((now - _lastBattery).TotalSeconds >= 20) { _lastBattery = now; var b = SystemProviders.Battery(); if (b != _battery) { _battery = b; changed = true; } }
        if ((now - _lastVolume).TotalMilliseconds >= 500) { _lastVolume = now; var v = SystemProviders.Volume(); if (v != _volume) { _volume = v; changed = true; } }
        if ((now - _lastNetwork).TotalSeconds >= 5) { _lastNetwork = now; var n = SystemProviders.Network(); if (n != _network) { _network = n; changed = true; } }
        if (_app is not null && _app.TakeChanged()) changed = true;
        if ((now - _lastTheme).TotalSeconds >= 5) { _lastTheme = now; var t = ResolveTheme(); if (t != _theme) { _theme = t; _painter.Theme = t; BuildBackdrop(); _force = true; } }
        if (changed) UpdateState(force: false);
    }

    private void UpdateState(bool force)
    {
        _ctl.State = new BarState(DateTime.Now, _app?.Name, _battery, _volume, _network); _force = true;
        if (force) { _battery = SystemProviders.Battery(); _volume = SystemProviders.Volume(); _network = SystemProviders.Network(); _ctl.State = new BarState(DateTime.Now, _app?.Name, _battery, _volume, _network); }
    }

    // ------------------------------------------------------------------ mensajes
    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (_taskbarCreated != 0 && msg == _taskbarCreated)
        {
            _log("[bar] Explorer se reinició (TaskbarCreated): se vuelve a registrar el AppBar.");
            _registered = false; if (_cfg.ReserveSpace) PlaceWindow(); _force = true; return IntPtr.Zero;
        }
        switch ((int)msg)
        {
            case WM_MOUSEACTIVATE: return (IntPtr)3;   // MA_NOACTIVATE: un clic en la barra no roba el foco a la app activa
            case WM_MOUSEMOVE: { double x = (short)((long)l & 0xFFFF) / _scale, y = (short)(((long)l >> 16) & 0xFFFF) / _scale; _px = x; _py = y; _ctl.SetPointer(x, y); return IntPtr.Zero; }
            case WM_LBUTTONUP: { double x = (short)((long)l & 0xFFFF) / _scale; Execute(_ctl.OnClick(x), _ctl.ItemAt(x)); _force = true; return IntPtr.Zero; }
            case WM_RBUTTONUP: { Execute(new BarAction(BarActionKind.ShowMenu), null); _force = true; return IntPtr.Zero; }
            case WM_APP_BAR:
                if ((long)w == BarNative.ABN_POSCHANGED && _registered) { PlaceWindow(); _force = true; }
                else if ((long)w == BarNative.ABN_FULLSCREENAPP) BarNative.SetWindowPos(_hwnd, (long)l != 0 ? new IntPtr(1) /* HWND_BOTTOM */ : new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE|NOMOVE|NOACTIVATE */);
                return IntPtr.Zero;
            case WM_DPICHANGED: _scale = (int)((long)w & 0xFFFF) / 96.0; _dev.SetDpi(_scale); ResizeWindow(); return IntPtr.Zero;
            case WM_DISPLAYCHANGE: ResizeWindow(); return IntPtr.Zero;
            case WM_DESTROY: BarNative.PostQuitMessage(0); return IntPtr.Zero;
        }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private void Execute(BarAction a, BarItemFrame? item)
    {
        try
        {
            switch (a.Kind)
            {
                case BarActionKind.OpenSettings: if (a.Uri is not null) BarNative.ShellExecuteW(IntPtr.Zero, "open", a.Uri, null, null, 1); break;
                case BarActionKind.Quit: _quitting = true; break;
                case BarActionKind.ShowMenu: ShowMenu(item); break;
            }
        }
        catch (Exception e) { _log($"Acción {a.Kind} falló: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Menú NATIVO de Windows. Devuelve el foco a la app que lo tenía (TrackPopupMenu exige que nuestra ventana sea la de primer plano).</summary>
    private void ShowMenu(BarItemFrame? item)
    {
        IntPtr menu = BarNative.CreatePopupMenu(); IntPtr prev = BarNative.GetForegroundWindow();
        try
        {
            BarNative.AppendMenuW(menu, 0x1 /* GRAYED */, (UIntPtr)100, "WinMac — barra superior");
            BarNative.AppendMenuW(menu, 0x800, UIntPtr.Zero, null);
            BarNative.AppendMenuW(menu, _cfg.ReserveSpace ? 0x8u : 0u, (UIntPtr)1, "Reservar espacio arriba");
            BarNative.AppendMenuW(menu, 0x800, UIntPtr.Zero, null);
            BarNative.AppendMenuW(menu, 0, (UIntPtr)2, "Salir de la barra superior");
            int x = _winLeftPx + (int)((item?.X ?? 0) * _scale), y = _winTopPx + _winHpx;
            BarNative.SetForegroundWindow(_hwnd);
            _modal = true; int cmd;
            try { cmd = Native2Track(menu, x, y); } finally { _modal = false; }
            BarNative.PostMessage(_hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 1) { _cfg = _cfg with { ReserveSpace = !_cfg.ReserveSpace }; _ctl.Config = _cfg; if (_cfgPath is not null) _cfg.TrySave(_cfgPath); PlaceWindow(); }
            else if (cmd == 2) _quitting = true;
        }
        catch (Exception e) { _log("Menú falló: " + e.Message); }
        finally { BarNative.DestroyMenu(menu); if (prev != IntPtr.Zero && prev != _hwnd) BarNative.SetForegroundWindow(prev); }
    }

    private int Native2Track(IntPtr menu, int x, int y) => BarNative.TrackPopupMenuEx(menu, 0x100 | 0x80 | 0x2 /* RETURNCMD|NONOTIFY|RIGHTBUTTON */, x, y, _hwnd, IntPtr.Zero);

    public string Report() => $"{Diagnostics}; ventana {_winWpx}x{_winHpx}px escala {_scale:0.##}; AppBar={(_registered ? "sí" : "no")}; tema={_theme}; fondo={(_blurred is not null ? "sí" : "no")}; batería={_battery.Present}; volumen={_volume.Available}; red={_network.Kind}";

    public void Dispose()
    {
        AppBarRemove(); _app?.Dispose(); _quit?.Dispose(); _blurred?.Dispose(); _wall.Dispose(); _painter?.Dispose(); _bmp?.Dispose(); _swap?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) BarNative.DestroyWindow(_hwnd);
    }
}
