using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Apps;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Interop;
using WinMac.Dock.Win.Shell;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// Ventana Win32 + DirectComposition que aloja el Dock. Elección PROVISIONAL de tecnología (ver docs/04-adr-001-provisional.md): el render vive
/// en <see cref="DockPainter"/> y el comportamiento en <see cref="DockController"/>, de modo que sustituir el host por WPF/WinUI 3 no toca la lógica.
/// Reposo = sin render: el bucle espera mensajes con tiempo límite de 50 ms (≈20 Hz) para sondear cursor/pantalla completa/ventanas.
/// </summary>
public sealed class DockHost : IDisposable
{
    private const int WM_DESTROY = 2, WM_SETTINGCHANGE = 0x1A, WM_MOUSEACTIVATE = 0x21, WM_DISPLAYCHANGE = 0x7E, WM_NCHITTEST = 0x84, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MOUSEMOVE = 0x200, WM_DPICHANGED = 0x2E0, WM_QUIT = 0x12;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private DockController _ctl; private DockConfig _cfg; private DockMetrics _m; private readonly GlassStyle _glass;
    private readonly WindowTracker? _tracker; private readonly WindowActions? _actions; private readonly DockModelBuilder _builder;
    private readonly IIconSource _icons; private readonly AppIconSource? _appIcons; private readonly WindowThumbnails? _thumbs;
    private bool _pressPreview;
    private readonly string? _cfgPath; private readonly LaunchGuard _launch = new(); private readonly EventWaitHandle? _quit;
    private DateTime? _saveDue; private DateTime _cfgStamp = DateTime.MinValue, _lastCfgCheck = DateTime.MinValue; private volatile bool _quitting;
    private int _pressed = -1; private System.Drawing.Point _pressPx; private bool _pressDrag;
    private readonly Action<string> _log; private readonly bool _demo;
    private readonly WndProc _proc;
    private IntPtr _hwnd; private double _scale = 1; private int _winLeftPx, _winTopPx, _winWpx, _winHpx; private Native.RECT _monitor;
    private D2DDevice _dev = null!; private IDXGISwapChain1 _swap = null!; private ID2D1Bitmap1? _bmp;
    private IDCompositionDevice _dcomp = null!; private IDCompositionTarget _target = null!; private IDCompositionVisual _visual = null!;
    private DockPainter _painter = null!; private readonly WallpaperSource _wall = new(); private ID2D1Bitmap1? _blurred;
    private DockView _view = null!; private bool _force = true, _wasAnimating, _suppressedHidden;
    private DateTime _lastFs = DateTime.MinValue;

    public string Diagnostics { get; private set; } = "";

    private readonly Native.RECT? _screen; private bool _monitorGone;

    /// <summary>Los monitores activos (px de pantalla), sin repetir los espejos: uno por Dock.</summary>
    public static IReadOnlyList<(int Left, int Top, int Right, int Bottom)> Monitors() => Native.MonitorRects().Select(r => (r.Left, r.Top, r.Right, r.Bottom)).ToList();
    private static int _classCounter;
    /// <summary>El monitor de este Dock ya no existe (se desconectó o cambió la distribución): su bucle termina y el coordinador crea los que hagan falta.</summary>
    public bool MonitorGone => _monitorGone;

    /// <summary>Pide el cierre normal del dock (lo usa el host de WinMac vía <c>--hosted</c>). Seguro desde otro hilo: el bucle lo atiende en ≤ 50 ms.</summary>
    public void RequestQuit() => _quitting = true;

    private long _lastTickMs = Environment.TickCount64; private volatile bool _modal;
    /// <summary>¿La interfaz sigue viva? El bucle principal avanza al menos cada 50 ms; un menú contextual abierto (bucle modal de Windows) cuenta como vivo. El host lo usa para el latido.</summary>
    public bool Responsive => _modal || Environment.TickCount64 - Volatile.Read(ref _lastTickMs) < 10_000;

    /// <param name="configPath">Si se indica, las apps ancladas y su orden se guardan aquí (escritura atómica, con retardo) y se recargan si el archivo cambia desde fuera.</param>
    /// <param name="monitor">Monitor de este Dock (px de pantalla). null = el principal. Con varias pantallas hay un Dock por monitor, cada uno en su hilo.</param>
    public DockHost(DockConfig cfg, DockMetrics m, GlassStyle glass, bool demo, Action<string> log, string? configPath = null, (int Left, int Top, int Right, int Bottom)? monitor = null)
    {
        _cfg = cfg; _glass = glass; _demo = demo; _log = log; _proc = WndProcImpl; _cfgPath = demo ? null : configPath;
        _screen = monitor is { } mm ? new Native.RECT { Left = mm.Left, Top = mm.Top, Right = mm.Right, Bottom = mm.Bottom } : null;
        // Vista previa de ventanas: la ventana crece hacia arriba y a los lados (zona transparente que deja pasar el ratón); la barra sigue igual y centrada.
        _m = Compose(m, cfg); _appliedLayout = DockSettings.LayoutKey(cfg);
        _ctl = new DockController(_m, cfg);
        _builder = new DockModelBuilder(cfg, DockDefaults.Title);
        if (demo) _icons = new NoIconSource();        // los íconos de demostración (letras) son solo para pruebas automatizadas y --demo
        else
        {
            _appIcons = new AppIconSource(log: _log); _icons = _appIcons;
            _thumbs = new WindowThumbnails { Log = log }; _ctl.ThumbAspect = _thumbs.Aspect;
            _tracker = new WindowTracker(); _actions = new WindowActions(_tracker) { Log = log }; _tracker.Changed += RefreshItems;
            try { _quit = new EventWaitHandle(false, EventResetMode.ManualReset, DockDefaults.QuitEventName); } catch (Exception e) { log("No se pudo crear el evento de cierre: " + e.Message); }
        }
        if (_cfgPath is not null && File.Exists(_cfgPath)) _cfgStamp = File.GetLastWriteTimeUtc(_cfgPath);
    }

    // ------------------------------------------------------------------ ciclo de vida
    public int Run(int maxFrames = int.MaxValue, bool selfTest = false)
    {
        Create();
        RefreshItems();
        var clock = Stopwatch.StartNew(); double last = 0; int frames = 0;
        while (true)
        {
            while (Native2.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { if (msg.message == WM_QUIT) return 0; Native2.TranslateMessage(ref msg); Native2.DispatchMessage(ref msg); }
            Volatile.Write(ref _lastTickMs, Environment.TickCount64);
            _tracker?.Pump();
            PollInputs();
            Housekeeping();
            CiMenuTick();
            if (_quitting || _monitorGone) return 0;
            if (selfTest) { _force = true; if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("selftest: no se alcanzaron los frames en 10 s"); }
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.1); last = now;
            _view = _ctl.Step(dt);
            UpdateHitRegion();
            PrefetchThumbnails();
            bool animating = _view.NeedsFrames;
            if (animating || _wasAnimating || _force) { Render(); _force = false; frames++; }
            _wasAnimating = animating;
            UpdateVisibility();
            if (frames >= maxFrames) return 0;
            if (!animating && !selfTest) Native.MsgWaitForMultipleObjectsEx(0, null, 50, Native.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);
        }
    }

    /// <summary>Pide capturas de las ventanas del ícono bajo el puntero (o de la vista previa abierta) para que estén listas al aparecer el panel; sin esperar.</summary>
    private void PrefetchThumbnails()
    {
        if (_thumbs is null) return;
        var item = _view.Preview?.Item ?? (_view.HoverIndex >= 0 && _view.HoverIndex < _view.Items.Length ? _view.Items[_view.HoverIndex].Model : null);
        if (item?.Windows is { Count: > 0 } ws && item.Kind == DockItemKind.App) foreach (var w in ws.Take(PreviewSpec.MaxCards)) _thumbs.Request(w.Hwnd, w.Minimized);
        if (_thumbs.TakeDirty()) _force = true;
    }

    private double ScreenWidthDip() => (_screen is { } t ? t.Width : Native2.GetSystemMetrics(0 /* SM_CXSCREEN */)) / (Native2.GetDpiForSystem() / 96.0);

    private void PollInputs()
    {
        if (!Native.GetCursorPos(out var p)) return;
        double sx = (p.X - _winLeftPx) / _scale, sy = (p.Y - _winTopPx) / _scale;
        bool inWin = p.X >= _winLeftPx && p.X < _winLeftPx + _winWpx && p.Y >= _winTopPx && p.Y < _winTopPx + _winHpx;
        _ctl.SetPointer(inWin ? new PointerSample(sx, sy) : null);
        _pointerOver = inWin && _view is not null && (InteractiveAt(sx, sy) || _view.HoverIndex >= 0);
        _ctl.SetEdgeHot(p.Y >= _monitor.Bottom - 2 && p.X >= _winLeftPx && p.X < _winLeftPx + _winWpx);
        if ((DateTime.UtcNow - _lastFs).TotalMilliseconds > 1000) { _lastFs = DateTime.UtcNow; _ctl.SetSuppressed(FullscreenWatcher.ShouldSuppress()); }
    }

    private void UpdateVisibility()
    {
        bool hide = _view.Suppressed && _view.HideT > 0.995;
        if (hide != _suppressedHidden) { _suppressedHidden = hide; Native2.ShowWindow(_hwnd, hide ? 0 : 4 /* SW_SHOWNOACTIVATE */); }
    }

    /// <summary>Añade a las métricas la holgura de la vista previa (la ventana crece hacia arriba y a los lados; la barra no cambia).</summary>
    private DockMetrics Compose(DockMetrics m, DockConfig cfg) => !_demo && cfg.PreviewsEnabled ? m with { PreviewHeadroom = PreviewSpec.Headroom(m), PreviewMaxWidth = Math.Min(PreviewSpec.MaxPanelWidth, Math.Max(PreviewSpec.MinPanelW + 40, ScreenWidthDip() - 2 * m.ShadowMargin - 24)) } : m;

    private (double, DockAlignment, double, double) _appliedLayout;

    /// <summary>Aplica en vivo un cambio de tamaño, alineación o altura: el tamaño reconstruye controlador y painter con las métricas nuevas; lo demás solo recoloca la ventana.</summary>
    private void ApplyLayoutChange()
    {
        var key = DockSettings.LayoutKey(_cfg);
        if (key == _appliedLayout)
        {
            if ((_cfg.ReserveSpace && !_cfg.AutoHide) != _reserving && _hwnd != IntPtr.Zero) PlaceWindow(show: false);   // only the reservation changed
            return;
        }
        bool sizeChanged = key.Item1 != _appliedLayout.Item1; _appliedLayout = key;
        if (_hwnd == IntPtr.Zero) { _ctl.Config = _cfg; return; }
        _pressed = -1; _pressDrag = false;
        if (sizeChanged)
        {
            var items = _ctl.Items; _m = Compose(DockMetrics.ForIconSize(_cfg.IconSize), _cfg);
            _ctl = NewController(); _ctl.SetItems(items);
            var old = _painter; _painter = new DockPainter(_dev, _m, _glass, _icons, _thumbs); old.Dispose();
        }
        else _ctl.Config = _cfg;
        ResizeWindow(); _view = _ctl.Step(0); _force = true;
        _log($"[dock] ajustes aplicados: tamaño {_cfg.IconSize:0}, alineación {_cfg.Alignment}, altura +{_cfg.BottomOffset:0}");
    }

    private DockController NewController() { var c = new DockController(_m, _cfg); if (_thumbs is not null) c.ThumbAspect = _thumbs.Aspect; return c; }

    private void RefreshItems()
    {
        var running = _demo ? [] : _tracker!.Snapshot();
        IReadOnlyList<DockItemModel> items = _demo ? DemoData.Items() : _builder.Build(running, _cfg.DownloadsPath);
        foreach (var it in items) if (it.Running) _launch.Clear(it.Id);
        double oldW = _ctl.WindowWidth;
        _ctl.SetItems(items);
        if (Math.Abs(_ctl.WindowWidth - oldW) > 0.5 && _hwnd != IntPtr.Zero) ResizeWindow();
        _force = true;
    }

    // ------------------------------------------------------------------ ventana
    private void Create()
    {
        // Una clase por Dock: la clase de ventana guarda el procedimiento de quien la registra, y con varios Docks en el proceso (uno por monitor) los mensajes
        // de todos acabarían en el primero.
        string clsName = "WinMacDock" + (Interlocked.Increment(ref _classCounter) == 1 ? "" : _classCounter.ToString());
        var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = Native2.GetModuleHandle(null), lpszClassName = clsName, hCursor = Native2.LoadCursor(IntPtr.Zero, 32512) };
        Native2.RegisterClassEx(ref cls);
        int ox = _screen?.Left ?? 0, oy = _screen?.Top ?? 0;   // nace en SU monitor: así su DPI es el de ese monitor
        _hwnd = Native2.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, clsName, "WinMac Dock", WS_POPUP, ox, oy, 100, 100, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());
        _dev = new D2DDevice(); Diagnostics = _dev.Description;
        _scale = Native.GetDpiForWindow(_hwnd) / 96.0; _dev.SetDpi(_scale);
        _painter = new DockPainter(_dev, _m, _glass, _icons, _thumbs);
        _ctl.SetItems(_demo ? DemoData.Items() : _builder.Build([], _cfg.DownloadsPath));
        CreateSurface();
        PlaceWindow(show: true);
        BuildBackdrop();
        LogSizes("inicio");
    }

    private void PlaceWindow(bool show)
    {
        var pt = _screen is { } t ? new Native.POINT { X = t.Left + t.Width / 2, Y = t.Top + t.Height / 2 } : new Native.POINT { X = 0, Y = 0 };
        var mi = new Native.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTOPRIMARY), ref mi);
        if (_screen is { } want && (mi.rcMonitor.Left != want.Left || mi.rcMonitor.Top != want.Top || mi.rcMonitor.Right != want.Right || mi.rcMonitor.Bottom != want.Bottom))
        {
            _monitorGone = true; _log($"[dock] el monitor ({want.Left},{want.Top} {want.Width}x{want.Height}) ya no existe: este Dock se cierra"); return;
        }
        _monitor = mi.rcMonitor;
        _winWpx = (int)Math.Ceiling(_ctl.WindowWidth * _scale); _winHpx = (int)Math.Ceiling(_ctl.WindowHeight * _scale);
        // WM-001: reserve the Dock's strip (AppBar) so maximized windows end above it; with auto-hide or the option off, nothing is reserved
        _appBar ??= new DockAppBar(_hwnd, WM_APPBAR, _log);
        if (_cfg.ReserveSpace && !_cfg.AutoHide && !_demo)
        {
            int band = (int)Math.Ceiling((_m.BarHeight + 6 + DockSettings.ClampBottom(_cfg.BottomOffset)) * _scale);
            int bottom = _appBar.Reserve(mi.rcMonitor, band);
            mi.rcWork.Bottom = bottom;   // place the Dock on its own strip, not above it
        }
        else _appBar.Remove();
        _reserving = _appBar.Registered;
        // Posición según la alineación y la altura elegidas (centro + altura normal = la de siempre: centrada justo sobre la barra de tareas).
        (_winLeftPx, _winTopPx) = DockSettings.Place(new PxRect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom), new PxRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom), _winWpx, _winHpx, _scale, _m, _cfg);
        Native2.SetWindowPos(_hwnd, new IntPtr(-1), _winLeftPx, _winTopPx, _winWpx, _winHpx, show ? 0x0010u | 0x0040u /* NOACTIVATE | SHOWWINDOW */ : 0x0010u);
    }

    private void ResizeWindow()
    {
        _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null);
        _bmp?.Dispose(); _bmp = null; _dev.Context.Target = null; _regionKey = "";   // región nueva para el tamaño nuevo
        PlaceWindow(show: false);
        // Se recrea la superficie completa en vez de ResizeBuffers: su resultado no se comprobaba y, si fallaba, el búfer conservaba el ancho
        // anterior y la barra quedaba recortada por la derecha cuando cambiaba el número de apps.
        _visual?.Dispose(); _target?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose();
        CreateSurface();
        BuildBackdrop(); _force = true;
        LogSizes("redimensionado");
    }

    /// <summary>Diagnóstico: ancho lógico del modelo, ventana real y búfer del swap chain (deben coincidir en píxeles).</summary>
    private void LogSizes(string why)
    {
        try
        {
            Native.GetWindowRect(_hwnd, out var r); var d = _swap.Description1;
            _log($"[dock] {why}: modelo={_ctl.WindowWidth:0.#}dip escala={_scale:0.##} esperado={_winWpx}x{_winHpx}px ventana={r.Right - r.Left}x{r.Bottom - r.Top}px búfer={d.Width}x{d.Height}px");
        }
        catch (Exception e) { _log("[dock] LogSizes falló: " + e.Message); }
    }

    // ------------------------------------------------------------------ gráficos
    private void CreateSurface()
    {
        _winWpx = (int)Math.Ceiling(_ctl.WindowWidth * _scale); _winHpx = (int)Math.Ceiling(_ctl.WindowHeight * _scale);
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
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp?.Dispose();
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
    }

    private void BuildBackdrop()
    {
        _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null);
        if (_cfg.GlassTier == GlassTier.Tint) return;
        try
        {
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) { _log("Fondo de pantalla no disponible: se usa vidrio sin fondo."); return; }
            var region = new RectD(_winLeftPx - _monitor.Left, _winTopPx - _monitor.Top, _winWpx, _winHpx);
            var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _monitor.Width, _monitor.Height, region);
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _ctl.WindowWidth, _ctl.WindowHeight, _scale, _glass);
            _painter.SetBackdrop(_blurred);
            _dev.Context.Target = _bmp;
        }
        catch (Exception e) { _log("BuildBackdrop falló: " + e.Message); _blurred = null; _painter.SetBackdrop(null); _dev.Context.Target = _bmp; }
    }

    private void Render()
    {
        // El ocultar/mostrar se hace con un desplazamiento del Visual (sin mover la ventana): el painter dibuja siempre «a posición completa».
        var v = _view with { HideT = 0 };
        _painter.Paint(v, _cfg.GlassTier);
        _visual.SetOffsetY((float)(_view.HideT * _m.HideDistance * _scale));
        _dcomp.Commit();
        _swap.Present(1, PresentFlags.None);
    }

    // ------------------------------------------------------------------ mensajes
    private DockAppBar? _appBar; private bool _reserving;
    private const int WM_APPBAR = 0x8000 + 0x51;   // WM_APP + 0x51: AppBar notifications
    private uint _taskbarCreated; private bool? _diagInteractive;

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (_taskbarCreated == 0) _taskbarCreated = Native2.RegisterWindowMessageW("TaskbarCreated");
        if (msg == _taskbarCreated && _taskbarCreated != 0) { _appBar?.Forget(); PlaceWindow(show: false); return IntPtr.Zero; }   // Explorer restarted
        if (msg == WM_APPBAR) { if ((long)w == 1 /* ABN_POSCHANGED */) PlaceWindow(show: false); return IntPtr.Zero; }
        switch ((int)msg)
        {
            case WM_MOUSEACTIVATE: return (IntPtr)3; // MA_NOACTIVATE: un clic en el dock no roba el foco a la app activa
            case WM_NCHITTEST:
            {
                int x = (short)((long)l & 0xFFFF), y = (short)(((long)l >> 16) & 0xFFFF);
                if (MenuDiag)
                {
                    bool ia = InteractiveAt((x - _winLeftPx) / _scale, (y - _winTopPx) / _scale);
                    if (ia != _diagInteractive) { _diagInteractive = ia; _log($"[dock] diag ratón en {x},{y}: {(ia ? "interactivo" : "transparente")} (ventana en {_winLeftPx},{_winTopPx}, escala {_scale:0.##}, oculto {_view?.HideT ?? -1:0.##}, barra {_view?.BarLeft ?? 0:0}+{_view?.BarWidth ?? 0:0} alto {_view?.WindowHeight ?? 0:0})"); }
                }

                return InteractiveAt((x - _winLeftPx) / _scale, (y - _winTopPx) / _scale) ? (IntPtr)1 : (IntPtr)(-1); // HTCLIENT / HTTRANSPARENT: el resto deja pasar el ratón
            }
            case WM_LBUTTONDOWN:
            {
                if (_view is null) return IntPtr.Zero;
                if (_ctl.PreviewContains((short)((long)l & 0xFFFF) / _scale, (short)(((long)l >> 16) & 0xFFFF) / _scale)) { _pressPreview = true; _pressed = -1; return IntPtr.Zero; }   // tarjeta de la vista previa
                _pressed = HitIndex((short)((long)l & 0xFFFF) / _scale); _pressDrag = false;
                _pressPx = new System.Drawing.Point((short)((long)l & 0xFFFF), (short)(((long)l >> 16) & 0xFFFF)); Native2.SetCapture(h);
                return IntPtr.Zero;
            }
            case WM_MOUSEMOVE:
            {
                if (_pressed < 0 || (Native2.GetKeyState(0x01) & 0x8000) == 0) return IntPtr.Zero;   // solo con el botón izquierdo pulsado
                int px = (short)((long)l & 0xFFFF), py = (short)(((long)l >> 16) & 0xFFFF);
                if (!_pressDrag && Math.Abs(px - _pressPx.X) + Math.Abs(py - _pressPx.Y) > 8 * _scale && _ctl.CanDrag(_pressed)) _pressDrag = _ctl.BeginDrag(_pressed);
                if (_pressDrag) { _ctl.UpdateDrag(px / _scale); _force = true; }
                return IntPtr.Zero;
            }
            case WM_LBUTTONUP:
            {
                Native2.ReleaseCapture();
                if (_view is null) return IntPtr.Zero;
                if (_pressPreview) { _pressPreview = false; var card = _ctl.PreviewClick(); if (card is not null) ExecuteAction(card); _force = true; return IntPtr.Zero; }
                int pressed = _pressed; _pressed = -1;
                if (_pressDrag)
                {
                    _pressDrag = false; var order = _ctl.EndDrag(true);
                    if (order is not null)
                    {
                        if (_ctl.LastDropTemporary) { _builder.SetRecentOrder(order); RefreshItems(); }       // las temporales: orden solo mientras el dock está abierto
                        else { _cfg = DockPinning.ReorderTo(_cfg, order); OnConfigChanged(); }
                    }
                    _force = true; return IntPtr.Zero;
                }
                int hit = HitIndex((short)((long)l & 0xFFFF) / _scale);
                if (hit >= 0 && hit == pressed) { _ctl.DismissPreview(); ExecuteAction(_ctl.OnClick(hit)); }   // clic = pulsar y soltar sobre el mismo ícono
                _force = true; return IntPtr.Zero;
            }
            case WM_RBUTTONUP:
            {
                _log($"[dock] clic derecho en {(short)((long)l & 0xFFFF)},{(short)(((long)l >> 16) & 0xFFFF)} (cliente)");
                if (_view is null || _ctl.PreviewContains((short)((long)l & 0xFFFF) / _scale, (short)(((long)l >> 16) & 0xFFFF) / _scale)) return IntPtr.Zero;
                _ctl.DismissPreview();
                int hit = HitIndex((short)((long)l & 0xFFFF) / _scale);
                var entries = hit >= 0 ? _ctl.MenuFor(hit) : DockMenu.ForBackground(_ctl.Config);
                var chosen = ShowMenu(entries); if (chosen is not null) ExecuteAction(chosen);
                _force = true; return IntPtr.Zero;
            }
            case WM_DPICHANGED: _scale = (int)((long)w & 0xFFFF) / 96.0; _dev.SetDpi(_scale); ResizeWindow(); return IntPtr.Zero;
            case WM_DISPLAYCHANGE: ResizeWindow(); return IntPtr.Zero;
            case WM_SETTINGCHANGE: if ((long)w == 0x14 /* SPI_SETDESKWALLPAPER */) { BuildBackdrop(); _force = true; } return IntPtr.Zero;
            case WM_DESTROY: Native2.PostQuitMessage(0); return IntPtr.Zero;
        }
        return Native2.DefWindowProc(h, msg, w, l);
    }

    /// <summary>Índice del ítem bajo X (coordenada de ventana en DIP) o -1. Ignora separadores.</summary>
    private int HitIndex(double x)
    {
        if (_view is null) return -1;
        for (int i = 0; i < _view.Items.Length; i++)
        {
            var it = _view.Items[i]; if (it.Model.Kind == DockItemKind.Separator) continue;
            if (x >= it.X - 3 && x <= it.X + it.Size + 3) return i;
        }
        return -1;
    }

    /// <summary>Punto único de ejecución de acciones. Cada una está aislada: un fallo se registra y el Dock sigue.</summary>
    private void ExecuteAction(DockAction a)
    {
        try
        {
            switch (a.Kind)
            {
                case DockActionKind.None: return;
                case DockActionKind.Quit: _quitting = true; return;
                case DockActionKind.Pin: if (a.App is not null) { _cfg = DockPinning.Pin(_cfg, a.App, DockDefaults.Title(a.App)); OnConfigChanged(); } return;
                case DockActionKind.Unpin: if (a.App is not null) { _cfg = DockPinning.Unpin(_cfg, a.App.Id); OnConfigChanged(); } return;
                case DockActionKind.MoveLeft: if (a.App is not null) { _cfg = DockPinning.MoveBy(_cfg, a.App.Id, -1); OnConfigChanged(); } return;
                case DockActionKind.MoveRight: if (a.App is not null) { _cfg = DockPinning.MoveBy(_cfg, a.App.Id, +1); OnConfigChanged(); } return;
                case DockActionKind.SetIconSize: _cfg = DockSettings.WithIconSize(_cfg, a.Number); OnConfigChanged(); return;
                case DockActionKind.SetAlignment: _cfg = DockSettings.WithAlignment(_cfg, (DockAlignment)(int)a.Number); OnConfigChanged(); return;
                case DockActionKind.NudgeBottom: _cfg = DockSettings.NudgeBottom(_cfg, a.Number); OnConfigChanged(); return;
                case DockActionKind.ResetBottom: _cfg = DockSettings.ResetBottom(_cfg); OnConfigChanged(); return;
                case DockActionKind.Launch:
                    if (a.ItemId is not null && !_launch.TryBegin(a.ItemId)) return;   // evita duplicados por clics repetidos mientras arranca
                    break;
            }
            _actions?.Execute(a);
        }
        catch (Exception e) { _log($"Acción {a.Kind} falló: {e.GetType().Name}: {e.Message}"); }
    }

    private void OnConfigChanged()
    {
        _builder.Config = _cfg; ApplyLayoutChange(); RefreshItems(); _saveDue = DateTime.UtcNow.AddMilliseconds(400);   // guardado con retardo: agrupa varios cambios seguidos
    }

    /// <summary>Tareas periódicas baratas: señal de cierre, iconos nuevos, guardado diferido y recarga si la configuración cambió desde fuera (p. ej. --pin).</summary>
    private void Housekeeping()
    {
        if (_quit is not null && _quit.WaitOne(0)) { _quitting = true; return; }
        if (_appIcons is not null && _appIcons.TakeDirty()) _force = true;
        var now = DateTime.UtcNow;
        if (_cfgPath is null) return;
        if (_saveDue is DateTime due && now >= due)
        {
            _saveDue = null;
            if (_cfg.TrySave(_cfgPath)) { try { _cfgStamp = File.GetLastWriteTimeUtc(_cfgPath); } catch (Exception) { } } else _log("No se pudo guardar la configuración de apps ancladas.");
        }
        else if ((now - _lastCfgCheck).TotalSeconds >= 2 && _saveDue is null)
        {
            _lastCfgCheck = now;
            try
            {
                var stamp = File.Exists(_cfgPath) ? File.GetLastWriteTimeUtc(_cfgPath) : DateTime.MinValue;
                if (stamp != _cfgStamp && stamp != DateTime.MinValue) { _cfgStamp = stamp; _cfg = DockSettings.Sanitize(DockConfig.LoadOrDefault(_cfgPath, () => _cfg)); _builder.Config = _cfg; ApplyLayoutChange(); RefreshItems(); _log("Configuración recargada desde el archivo."); }
            }
            catch (Exception) { }
        }
    }

    /// <summary>WM-017: el menú oscuro de WinMac (mismas entradas y acciones); el nativo de Windows queda solo como respaldo si el oscuro no se puede crear.</summary>
    private DockAction? ShowMenu(IReadOnlyList<MenuEntry> entries)
    {
        try
        {
            Native.GetCursorPos(out var cp); _modal = true;
            _log($"[dock] menú oscuro: abriendo en {cp.X},{cp.Y} ({entries.Count} entradas)");
            var r = DarkMenuWindow.Show(entries, cp.X, cp.Y, _scale, _log);
            _log("[dock] menú oscuro: " + (r is null ? "cerrado sin elegir" : "eligió " + r.Kind));
            return r;
        }
        catch (Exception e) { _log("Menú oscuro no disponible, se usa el de Windows: " + e.GetType().Name + ": " + e.Message); }
        finally { _modal = false; }
        return ShowNativeMenu(entries);
    }

    private static readonly bool MenuDiag = Environment.GetEnvironmentVariable("WINMAC_MENU_DIAG") == "1";

    /// <summary>
    /// SOLO CI (WINMAC_MENU_DIAG=1): el evento «Local\WinMacDockMenuTest» abre el menú del primer icono exactamente como el clic derecho (mismas entradas, mismo
    /// ShowMenu). En el runner el clic derecho inyectado no llega a la ventana del Dock aunque esta lo marca interactivo; así se valida el menú en sí.
    /// </summary>
    private EventWaitHandle? _ciMenu;
    private void CiMenuTick()
    {
        if (!MenuDiag) return;
        try { _ciMenu ??= new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WinMacDockMenuTest"); } catch (Exception) { return; }
        if (!_ciMenu.WaitOne(0) || _view is null) return;
        _log("[dock] menú de prueba del CI pedido");
        var entries = _view.Items.Count() > 0 ? _ctl.MenuFor(0) : DockMenu.ForBackground(_ctl.Config);
        var chosen = ShowMenu(entries); if (chosen is not null) ExecuteAction(chosen);
        _force = true;
    }

    /// <summary>Menú contextual NATIVO de Windows (respaldo), con submenús y marcas de selección. Devuelve la acción elegida o null.</summary>
    private DockAction? ShowNativeMenu(IReadOnlyList<MenuEntry> entries)
    {
        var actions = new List<DockAction?>();   // el id de cada comando del menú es su posición + 1
        IntPtr Build(IReadOnlyList<MenuEntry> list)
        {
            IntPtr hm = Native2.CreatePopupMenu();
            foreach (var e in list)
            {
                if (e.IsSeparator) { Native2.AppendMenuW(hm, 0x800 /* MF_SEPARATOR */, UIntPtr.Zero, null); continue; }
                string text = e.Text.Replace("&", "&&"); uint off = e.Enabled && !e.IsHeader ? 0u : 0x3u /* GRAYED|DISABLED */;
                if (e.Children is { Count: > 0 } kids) { IntPtr sub = Build(kids); Native2.AppendMenuW(hm, 0x10 /* MF_POPUP */ | off, (UIntPtr)(nuint)sub, text); }
                else { actions.Add(e.Action); Native2.AppendMenuW(hm, off | (e.Checked ? 0x8u /* MF_CHECKED */ : 0u), (UIntPtr)(uint)actions.Count, text); }
            }
            return hm;
        }
        IntPtr menu = IntPtr.Zero;
        try
        {
            menu = Build(entries); if (menu == IntPtr.Zero) return null;
            Native.GetCursorPos(out var p);
            Native.SetForegroundWindow(_hwnd);   // requisito documentado de TrackPopupMenu: sin esto el menú no se cierra al pulsar fuera
            _modal = true;
            int cmd = Native2.TrackPopupMenuEx(menu, 0x100 | 0x80 | 0x20 | 0x4 | 0x2 /* RETURNCMD|NONOTIFY|BOTTOMALIGN|CENTERALIGN|RIGHTBUTTON */, p.X, p.Y, _hwnd, IntPtr.Zero);
            Native.PostMessage(_hwnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            return cmd > 0 && cmd <= actions.Count ? actions[cmd - 1] : null;
        }
        catch (Exception e) { _log("Menú contextual falló: " + e.Message); return null; }
        finally { _modal = false; if (menu != IntPtr.Zero) Native2.DestroyMenu(menu); }   // DestroyMenu destruye también los submenús
    }

    private bool _pointerOver; private string _regionKey = "";

    /// <summary>
    /// Limita la ventana, para el ratón, a la barra y lo que de verdad se dibuja (<see cref="DockHitRegion"/>): fuera de esa región Windows entrega el clic a la app de debajo.
    /// Solo se llama a SetWindowRgn cuando la región cambia (al pasar el puntero, al ocultarse o con la vista previa), no en cada fotograma.
    /// </summary>
    private void UpdateHitRegion()
    {
        if (_view is null || _hwnd == IntPtr.Zero) return;
        var rects = DockHitRegion.Compute(_view, _m, _pointerOver, _ctl.IsDragging);
        var px = rects.Select(r => (L: (int)Math.Floor(r.X * _scale), T: (int)Math.Floor(r.Y * _scale), R: (int)Math.Ceiling(r.Right * _scale), B: (int)Math.Ceiling(r.Bottom * _scale))).ToList();
        string key = string.Join(";", px);
        if (key == _regionKey) return;
        _regionKey = key;
        IntPtr rgn = Native2.CreateRectRgn(0, 0, 0, 0);
        foreach (var (l, t, r, b) in px) { var part = Native2.CreateRectRgn(l, t, r, b); Native2.CombineRgn(rgn, rgn, part, 2 /* RGN_OR */); Native2.DeleteObject(part); }
        if (Native2.SetWindowRgn(_hwnd, rgn, true) == 0) Native2.DeleteObject(rgn);   // si Windows la acepta, pasa a ser suya
    }

    /// <summary>Solo la barra (y los íconos ampliados) capturan el ratón; el resto de la ventana, transparente, lo deja pasar a las apps de abajo.</summary>
    private bool InteractiveAt(double x, double y)
    {
        if (_view is null || _view.HideT > 0.5) return false;
        if (_ctl.PreviewContains(x, y)) return true;   // el panel de vista previa también captura el ratón
        double barBottom = _view.WindowHeight - _m.ShadowMargin, barTop = barBottom - _m.BarHeight;
        double headroom = _view.Items.Where(i => i.Model.Kind != DockItemKind.Separator).Select(i => Math.Max(0, i.Size - _m.BaseSize) + i.Lift).DefaultIfEmpty(0).Max();
        return x >= _view.BarLeft && x <= _view.BarLeft + _view.BarWidth && y >= barTop - headroom && y <= barBottom;
    }

    public string Report() => $"{Diagnostics}; ventana {_winWpx}x{_winHpx}px escala {_scale:0.##}; fondo={(_blurred is not null ? "sí" : "no")}; íconos {(_appIcons is null ? "n/a" : $"cargados {_appIcons.Service.Stats.Loaded}, fallidos {_appIcons.Service.Stats.Failed + _appIcons.Service.Stats.TimedOut + _appIcons.Service.Stats.Rejected}")}; hooks={_tracker?.HookCount}";

    public void Dispose()
    {
        if (_saveDue is not null && _cfgPath is not null) _cfg.TrySave(_cfgPath);   // guarda lo pendiente al cerrar
        try { _appBar?.Remove(); } catch (Exception) { }   // WM-001: give the work area back
        _tracker?.Dispose(); _thumbs?.Dispose(); _appIcons?.Dispose(); _quit?.Dispose(); _blurred?.Dispose(); _wall.Dispose(); _painter?.Dispose(); _bmp?.Dispose(); _swap?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) Native2.DestroyWindow(_hwnd);
    }

    // ------------------------------------------------------------------ P/Invoke de ventana (separado de Native para mantener esa clase mínima)
    private delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX { public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    private static class Native2
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint remove);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, int id);
        [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr m);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr m, uint flags, UIntPtr id, string? text);
        [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr m, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    }
}
