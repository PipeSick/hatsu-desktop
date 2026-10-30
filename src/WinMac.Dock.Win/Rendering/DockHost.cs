using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
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
    private const int WM_DESTROY = 2, WM_SETTINGCHANGE = 0x1A, WM_MOUSEACTIVATE = 0x21, WM_DISPLAYCHANGE = 0x7E, WM_NCHITTEST = 0x84, WM_LBUTTONUP = 0x202, WM_DPICHANGED = 0x2E0, WM_QUIT = 0x12;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private readonly DockController _ctl; private readonly DockConfig _cfg; private readonly DockMetrics _m; private readonly GlassStyle _glass;
    private readonly WindowTracker? _tracker; private readonly WindowActions? _actions; private readonly DockModelBuilder _builder; private readonly ShellIconSource _icons = new();
    private readonly Action<string> _log; private readonly bool _demo;
    private readonly WndProc _proc;
    private IntPtr _hwnd; private double _scale = 1; private int _winLeftPx, _winTopPx, _winWpx, _winHpx; private Native.RECT _monitor;
    private D2DDevice _dev = null!; private IDXGISwapChain1 _swap = null!; private ID2D1Bitmap1? _bmp;
    private IDCompositionDevice _dcomp = null!; private IDCompositionTarget _target = null!; private IDCompositionVisual _visual = null!;
    private DockPainter _painter = null!; private readonly WallpaperSource _wall = new(); private ID2D1Bitmap1? _blurred;
    private DockView _view = null!; private bool _force = true, _wasAnimating, _suppressedHidden;
    private DateTime _lastFs = DateTime.MinValue;

    public string Diagnostics { get; private set; } = "";

    public DockHost(DockConfig cfg, DockMetrics m, GlassStyle glass, bool demo, Action<string> log)
    {
        _cfg = cfg; _m = m; _glass = glass; _demo = demo; _log = log; _proc = WndProcImpl;
        _ctl = new DockController(m, cfg);
        _builder = new DockModelBuilder(cfg, DockDefaults.Title);
        if (!demo) { _tracker = new WindowTracker(); _actions = new WindowActions(_tracker) { Log = log }; _tracker.Changed += RefreshItems; }
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
            _tracker?.Pump();
            PollInputs();
            if (selfTest) { _force = true; if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("selftest: no se alcanzaron los frames en 10 s"); }
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.1); last = now;
            _view = _ctl.Step(dt);
            bool animating = _view.NeedsFrames;
            if (animating || _wasAnimating || _force) { Render(); _force = false; frames++; }
            _wasAnimating = animating;
            UpdateVisibility();
            if (frames >= maxFrames) return 0;
            if (!animating && !selfTest) Native.MsgWaitForMultipleObjectsEx(0, null, 50, Native.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);
        }
    }

    private void PollInputs()
    {
        if (!Native.GetCursorPos(out var p)) return;
        double sx = (p.X - _winLeftPx) / _scale, sy = (p.Y - _winTopPx) / _scale;
        bool inWin = p.X >= _winLeftPx && p.X < _winLeftPx + _winWpx && p.Y >= _winTopPx && p.Y < _winTopPx + _winHpx;
        _ctl.SetPointer(inWin ? new PointerSample(sx, sy) : null);
        _ctl.SetEdgeHot(p.Y >= _monitor.Bottom - 2 && p.X >= _winLeftPx && p.X < _winLeftPx + _winWpx);
        if ((DateTime.UtcNow - _lastFs).TotalMilliseconds > 1000) { _lastFs = DateTime.UtcNow; _ctl.SetSuppressed(FullscreenWatcher.ShouldSuppress()); }
    }

    private void UpdateVisibility()
    {
        bool hide = _view.Suppressed && _view.HideT > 0.995;
        if (hide != _suppressedHidden) { _suppressedHidden = hide; Native2.ShowWindow(_hwnd, hide ? 0 : 4 /* SW_SHOWNOACTIVATE */); }
    }

    private void RefreshItems()
    {
        var running = _demo ? [] : _tracker!.Snapshot();
        IReadOnlyList<DockItemModel> items = _demo ? DemoData.Items() : _builder.Build(running, _cfg.DownloadsPath);
        double oldW = _ctl.WindowWidth;
        _ctl.SetItems(items);
        if (Math.Abs(_ctl.WindowWidth - oldW) > 0.5 && _hwnd != IntPtr.Zero) ResizeWindow();
        _force = true;
    }

    // ------------------------------------------------------------------ ventana
    private void Create()
    {
        var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = Native2.GetModuleHandle(null), lpszClassName = "WinMacDock", hCursor = Native2.LoadCursor(IntPtr.Zero, 32512) };
        Native2.RegisterClassEx(ref cls);
        _hwnd = Native2.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "WinMacDock", "WinMac Dock", WS_POPUP, 0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());
        _dev = new D2DDevice(); Diagnostics = _dev.Description;
        _scale = Native.GetDpiForWindow(_hwnd) / 96.0; _dev.SetDpi(_scale);
        _painter = new DockPainter(_dev, _m, _glass, _icons);
        _ctl.SetItems(_demo ? DemoData.Items() : _builder.Build([], _cfg.DownloadsPath));
        CreateSurface();
        PlaceWindow(show: true);
        BuildBackdrop();
    }

    private void PlaceWindow(bool show)
    {
        var pt = new Native.POINT { X = 0, Y = 0 };
        var mi = new Native.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTOPRIMARY), ref mi);
        _monitor = mi.rcMonitor;
        _winWpx = (int)Math.Ceiling(_ctl.WindowWidth * _scale); _winHpx = (int)Math.Ceiling(_ctl.WindowHeight * _scale);
        _winLeftPx = mi.rcMonitor.Left + (mi.rcMonitor.Width - _winWpx) / 2;
        _winTopPx = mi.rcWork.Bottom - _winHpx + (int)(4 * _scale); // justo sobre la barra de tareas; el margen de sombra queda parcialmente debajo
        Native2.SetWindowPos(_hwnd, new IntPtr(-1), _winLeftPx, _winTopPx, _winWpx, _winHpx, show ? 0x0010u | 0x0040u /* NOACTIVATE | SHOWWINDOW */ : 0x0010u);
    }

    private void ResizeWindow()
    {
        _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null);
        _bmp?.Dispose(); _bmp = null; _dev.Context.Target = null;
        PlaceWindow(show: false);
        _swap.ResizeBuffers(0, (uint)_winWpx, (uint)_winHpx, Format.Unknown, SwapChainFlags.None);
        BindTarget(); BuildBackdrop(); _force = true;
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
    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch ((int)msg)
        {
            case WM_MOUSEACTIVATE: return (IntPtr)3; // MA_NOACTIVATE: un clic en el dock no roba el foco a la app activa
            case WM_NCHITTEST:
            {
                int x = (short)((long)l & 0xFFFF), y = (short)(((long)l >> 16) & 0xFFFF);
                return InteractiveAt((x - _winLeftPx) / _scale, (y - _winTopPx) / _scale) ? (IntPtr)1 : (IntPtr)(-1); // HTCLIENT / HTTRANSPARENT: el resto deja pasar el ratón
            }
            case WM_LBUTTONUP:
            {
                if (_view is null) return IntPtr.Zero;
                double x = (short)((long)l & 0xFFFF) / _scale;
                for (int i = 0; i < _view.Items.Length; i++)
                {
                    var it = _view.Items[i]; if (it.Model.Kind == DockItemKind.Separator) continue;
                    if (x >= it.X - 3 && x <= it.X + it.Size + 3) { var a = _ctl.OnClick(i); _actions?.Execute(a); _force = true; break; }
                }
                return IntPtr.Zero;
            }
            case WM_DPICHANGED: _scale = (int)((long)w & 0xFFFF) / 96.0; _dev.SetDpi(_scale); ResizeWindow(); return IntPtr.Zero;
            case WM_DISPLAYCHANGE: ResizeWindow(); return IntPtr.Zero;
            case WM_SETTINGCHANGE: if ((long)w == 0x14 /* SPI_SETDESKWALLPAPER */) { BuildBackdrop(); _force = true; } return IntPtr.Zero;
            case WM_DESTROY: Native2.PostQuitMessage(0); return IntPtr.Zero;
        }
        return Native2.DefWindowProc(h, msg, w, l);
    }

    /// <summary>Solo la barra (y los íconos ampliados) capturan el ratón; el resto de la ventana, transparente, lo deja pasar a las apps de abajo.</summary>
    private bool InteractiveAt(double x, double y)
    {
        if (_view is null || _view.HideT > 0.5) return false;
        double barBottom = _view.WindowHeight - _m.ShadowMargin, barTop = barBottom - _m.BarHeight;
        double headroom = _view.Items.Where(i => i.Model.Kind != DockItemKind.Separator).Select(i => Math.Max(0, i.Size - _m.BaseSize) + i.Lift).DefaultIfEmpty(0).Max();
        return x >= _view.BarLeft && x <= _view.BarLeft + _view.BarWidth && y >= barTop - headroom && y <= barBottom;
    }

    public string Report() => $"{Diagnostics}; ventana {_winWpx}x{_winHpx}px escala {_scale:0.##}; fondo={(_blurred is not null ? "sí" : "no")}; íconos {_icons.Loaded}/{_icons.Requested}; hooks={_tracker?.HookCount}";

    public void Dispose()
    {
        _tracker?.Dispose(); _blurred?.Dispose(); _wall.Dispose(); _painter?.Dispose(); _bmp?.Dispose(); _swap?.Dispose(); _dev?.Dispose();
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int c);
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint remove);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    }
}
