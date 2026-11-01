using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Shell;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// La barra de UN monitor: su ventana (Win32 + DirectComposition + Direct2D), su superficie, su AppBar, su recorte para la Isla y su puntero. Cada monitor tiene la suya, con la resolución,
/// la posición y el escalado DPI de ese monitor; comparten la configuración, el estado del sistema y los paneles a través de <see cref="BarHost"/>. Todo corre en el hilo de la barra.
/// </summary>
internal sealed class BarWindow : IWndTarget, IDisposable
{
    private const int WM_MOUSEACTIVATE = 0x21, WM_DISPLAYCHANGE = 0x7E, WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_DPICHANGED = 0x2E0, WM_APP_BAR = 0x8001;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const string ClassName = "WinMacBar";

    private readonly BarHost _host; private readonly BarMetrics _m = new(); private readonly Action<string> _log;
    private IntPtr _hmon; private BarNative.RECT _monitor;
    private double _scale = 1; private int _winLeftPx, _winTopPx, _winWpx, _winHpx;
    private D2DDevice _dev = null!; private IDXGISwapChain1 _swap = null!; private ID2D1Bitmap1? _bmp, _blurred;
    private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private BarPainter _painter = null!; private readonly WallpaperSource _wall = new(); private BarView? _view;
    private bool _force = true, _registered, _hiddenForFullscreen, _zOrdered, _resizing, _disposed; private uint _taskbarCreated; private BarTheme _theme;
    private DateTime _lastFs = DateTime.MinValue; private double? _px, _py;

    public BarWindow(BarHost host, MonitorSnapshot snap, BarConfig cfg, BarState initial, Action<string> log)
    {
        _host = host; _log = log; Info = snap.Info; _hmon = snap.Handle; _monitor = snap.Rect; _scale = snap.Info.Scale; _theme = host.Theme;
        Ctl = new BarController(_m, cfg, initial);
    }

    public BarController Ctl { get; }
    public MonitorInfo Info { get; private set; }
    public IntPtr Hwnd { get; private set; }
    public string Diagnostics { get; private set; } = "";
    public double Scale => _scale;
    public int Left => _winLeftPx;
    public int Top => _winTopPx;
    public int Height => _winHpx;
    public bool Registered => _registered;
    public bool NeedsFrames { get; private set; }
    /// <summary>Monitor de esta barra (px de pantalla): donde se abren sus paneles y menús.</summary>
    public PxRect MonitorRect => new(_monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height);
    /// <summary>Ventana de la barra en px de pantalla (la que recorta la Isla).</summary>
    public PxRect WindowRect => new(_winLeftPx, _winTopPx, _winWpx, _winHpx);

    // ------------------------------------------------------------------ ciclo de vida
    public void Create()
    {
        WndRouter.EnsureClass(ClassName);
        Hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "WinMac Barra superior", WS_POPUP, _monitor.Left, _monitor.Top, 100, 30, IntPtr.Zero, IntPtr.Zero, BarNative.GetModuleHandle(null), IntPtr.Zero);
        if (Hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());
        WndRouter.Register(Hwnd, this);
        _taskbarCreated = BarNative.RegisterWindowMessageW("TaskbarCreated");   // Explorer se reinició: hay que volver a registrar el AppBar
        _dev = new D2DDevice(); Diagnostics = _dev.Description;
        uint dpi = BarNative.GetDpiForWindow(Hwnd); _scale = dpi >= 48 ? dpi / 96.0 : Info.Scale; _dev.SetDpi(_scale);   // la ventana nace en el origen de SU monitor: su DPI es el de ese monitor
        _painter = new BarPainter(_dev, _m) { Theme = _theme, Background = _host.Config.Background };
        Ctl.Measure = (t, b) => _painter.Measure(t, b);
        Metrics(); Ctl.SetWidth(_monitor.Width / _scale);
        CreateSurface(); PlaceWindow(); BuildBackdrop();
        LogSizes("inicio");
    }

    /// <summary>La topología cambió y este monitor sigue existiendo pero con otra resolución, posición o escalado: se recoloca y se recrea la superficie (no ResizeBuffers, lección del Dock).</summary>
    public void Relayout(MonitorSnapshot snap)
    {
        Info = snap.Info; _hmon = snap.Handle; _monitor = snap.Rect; _scale = snap.Info.Scale; _dev.SetDpi(_scale);
        ResizeWindow();
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { AppBarRemove(); } catch (Exception) { /* la ventana se destruye igual */ }
        if (Hwnd != IntPtr.Zero) { WndRouter.Unregister(Hwnd); BarNative.SetWindowRgn(Hwnd, IntPtr.Zero, false); }
        try { if (_dev is not null) _dev.Context.Target = null; } catch (Exception) { /* dispositivo ya perdido */ }
        _blurred?.Dispose(); _wall.Dispose(); _painter?.Dispose(); _bmp?.Dispose(); _visual?.Dispose(); _target?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose(); _dev?.Dispose();
        _blurred = null; _bmp = null; _visual = null; _target = null; _dcomp = null;
        if (Hwnd != IntPtr.Zero) { BarNative.DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
    }

    // ------------------------------------------------------------------ geometría y AppBar
    /// <summary>La barra ocupa todo el ancho de SU monitor, con la altura de la barra escalada al DPI de ese monitor.</summary>
    private void Metrics() { _winWpx = _monitor.Width; _winHpx = (int)Math.Ceiling(_m.Height * _scale); }

    private void PlaceWindow()
    {
        Metrics(); _winLeftPx = _monitor.Left; _winTopPx = _monitor.Top;
        if (_host.Config.ReserveSpace) AppBarSetPos(); else AppBarRemove();
        BarNative.SetWindowPos(Hwnd, new IntPtr(-1), _winLeftPx, _winTopPx, _winWpx, _winHpx, 0x0010u | 0x0040u | (_zOrdered ? 0x0004u /* NOZORDER */ : 0u) /* NOACTIVATE | SHOWWINDOW [| NOZORDER] */);
        _zOrdered = true; ApplyYield();   // la ventana se movió o cambió de tamaño: volver a recortar
    }

    private void AppBarSetPos()
    {
        var abd = new BarNative.APPBARDATA { cbSize = (uint)Marshal.SizeOf<BarNative.APPBARDATA>(), hWnd = Hwnd, uCallbackMessage = WM_APP_BAR };
        if (!_registered) { _registered = BarNative.SHAppBarMessage(BarNative.ABM_NEW, ref abd) != 0; if (!_registered) { _log($"[bar] ABM_NEW falló en {Info.Id}: la barra se muestra superpuesta, sin reservar espacio."); return; } }
        abd.uEdge = BarNative.ABE_TOP; abd.rc = new BarNative.RECT { Left = _monitor.Left, Top = _monitor.Top, Right = _monitor.Right, Bottom = _monitor.Top + _winHpx };
        BarNative.SHAppBarMessage(BarNative.ABM_QUERYPOS, ref abd);
        abd.rc.Bottom = abd.rc.Top + _winHpx;   // QUERYPOS ajusta el borde; la altura la fijamos nosotros
        BarNative.SHAppBarMessage(BarNative.ABM_SETPOS, ref abd);
        _winLeftPx = abd.rc.Left; _winTopPx = abd.rc.Top; _winWpx = abd.rc.Width;
        _log($"[bar] AppBar reservado en {Info.Id}: ({abd.rc.Left},{abd.rc.Top})-({abd.rc.Right},{abd.rc.Bottom}) px");
    }

    private void AppBarRemove()
    {
        if (!_registered || Hwnd == IntPtr.Zero) return;
        var abd = new BarNative.APPBARDATA { cbSize = (uint)Marshal.SizeOf<BarNative.APPBARDATA>(), hWnd = Hwnd };
        BarNative.SHAppBarMessage(BarNative.ABM_REMOVE, ref abd); _registered = false; _log($"[bar] AppBar liberado en {Info.Id}.");
    }

    /// <summary>Recorta la ventana donde la Isla de ESTE monitor necesita el espacio. Los huecos de otros monitores no la tocan (se intersecan con la ventana de esta barra).</summary>
    public void ApplyYield()
    {
        if (Hwnd == IntPtr.Zero) return;
        var holes = _host.Holes; var win = WindowRect;
        if (!YieldRegions.Touches(win, holes)) { BarNative.SetWindowRgn(Hwnd, IntPtr.Zero, true); return; }   // nada que ceder: ventana completa
        var rgn = BarNative.CreateRectRgn(0, 0, 0, 0);
        foreach (var p in YieldRegions.Visible(win, holes))
        {
            var piece = BarNative.CreateRectRgn(p.X - win.X, p.Y - win.Y, p.Right - win.X, p.Bottom - win.Y);
            BarNative.CombineRgn(rgn, rgn, piece, 2 /* RGN_OR */); BarNative.DeleteObject(piece);
        }
        if (BarNative.SetWindowRgn(Hwnd, rgn, true) == 0) BarNative.DeleteObject(rgn);   // si falla, la región sigue siendo nuestra
    }

    // ------------------------------------------------------------------ gráficos
    private void CreateSurface()
    {
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        _swap = factory.CreateSwapChainForComposition(_dev.D3D, new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, _winWpx), Height = (uint)Math.Max(1, _winHpx), Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch,
        });
        BindTarget();
        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi);
        _dcomp.CreateTargetForHwnd(Hwnd, true, out _target!).CheckError();
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
        if (_resizing) return; _resizing = true;   // mover la ventana puede provocar WM_DPICHANGED dentro de esta misma operación
        try
        {
            _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null); _bmp?.Dispose(); _bmp = null; _dev.Context.Target = null;
            PlaceWindow(); _visual?.Dispose(); _target?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose();
            CreateSurface(); Ctl.SetWidth(_winWpx / _scale); BuildBackdrop(); _force = true; LogSizes("redimensionado");
        }
        finally { _resizing = false; }
    }

    private void BuildBackdrop()
    {
        _blurred?.Dispose(); _blurred = null; _painter.SetBackdrop(null);
        if (_host.Config.Background != BarBackground.Glass) return;
        try
        {
            // Nota: Windows guarda UN fondo de pantalla en esta lectura; todas las barras usan esa imagen para el vidrio (con fondos distintos por monitor el tinte es aproximado).
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) { _log("Fondo de pantalla no disponible: vidrio sin fondo."); return; }
            var region = new RectD(0, 0, _monitor.Width, _winHpx); var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _monitor.Width, _monitor.Height, region);
            var style = GlassStyle.For(_theme == BarTheme.Dark ? DockTheme.Dark : DockTheme.Light) with { BlurSigma = 16 };
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _winWpx / _scale, _winHpx / _scale, _scale, style); _painter.SetBackdrop(_blurred); _dev.Context.Target = _bmp;
        }
        catch (Exception e) { _log("BuildBackdrop falló: " + e.Message); _blurred = null; _painter.SetBackdrop(null); _dev.Context.Target = _bmp; }
    }

    private void Render() { if (_view is null) return; _painter.Paint(_view); _dcomp!.Commit(); _swap.Present(1, PresentFlags.None); }

    private void LogSizes(string why)
    {
        try { var d = _swap.Description1; _log($"[bar] {why} ({Info.Id}): escala={_scale:0.##} esperado={_winWpx}x{_winHpx}px búfer={d.Width}x{d.Height}px posición=({_winLeftPx},{_winTopPx}) tema={_theme} fondo={_host.Config.Background}"); }
        catch (Exception e) { _log("[bar] LogSizes falló: " + e.Message); }
    }

    // ------------------------------------------------------------------ estado y entrada
    public void SetState(BarState s) { Ctl.State = s; _force = true; }
    public void Redraw() { _force = true; }

    public void SetTheme(BarTheme t) { _theme = t; _painter.Theme = t; BuildBackdrop(); _force = true; }

    /// <summary>Cambios de la configuración central en vivo: solo se toca lo que cambió.</summary>
    public void ApplyConfig(BarConfig old, BarConfig c)
    {
        Ctl.Config = c;
        if (old.ReserveSpace != c.ReserveSpace) PlaceWindow();
        if (old.Background != c.Background) { _painter.Background = c.Background; BuildBackdrop(); }
        _force = true;
    }

    /// <summary>Puntero y pantalla completa de ESTE monitor.</summary>
    public void Poll()
    {
        if (BarNative.GetCursorPos(out var p))
        {
            bool inside = p.X >= _winLeftPx && p.X < _winLeftPx + _winWpx && p.Y >= _winTopPx && p.Y < _winTopPx + _winHpx;
            double? x = inside ? (p.X - _winLeftPx) / _scale : null, y = inside ? (p.Y - _winTopPx) / _scale : null;
            if (x != _px || y != _py) { _px = x; _py = y; Ctl.SetPointer(x, y); }
        }

        if ((DateTime.UtcNow - _lastFs).TotalMilliseconds > 1000)
        {
            _lastFs = DateTime.UtcNow;
            // Una app a pantalla completa en OTRO monitor no debe esconder esta barra: solo se oculta si la app en primer plano está en este monitor.
            bool fs = FullscreenWatcher.ShouldSuppress() && BarNative.MonitorFromWindow(BarNative.GetForegroundWindow(), BarNative.MONITOR_DEFAULTTONEAREST) == _hmon;
            if (fs != _hiddenForFullscreen) { _hiddenForFullscreen = fs; BarNative.ShowWindow(Hwnd, fs ? 0 : 4 /* SW_SHOWNOACTIVATE */); _force = true; }
        }
    }

    /// <summary>Un paso de animación y, si hace falta, un fotograma. Devuelve true si dibujó.</summary>
    public bool Frame(double dt, bool selfTest)
    {
        if (_disposed) return false;
        if (selfTest) _force = true;
        _view = Ctl.Step(dt); NeedsFrames = _view.NeedsFrames;
        if (!NeedsFrames && !_force) return false;
        if (!_hiddenForFullscreen) Render();
        _force = false; return true;
    }

    // ------------------------------------------------------------------ mensajes
    public IntPtr OnMessage(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (_taskbarCreated != 0 && msg == _taskbarCreated)
            {
                _log("[bar] Explorer se reinició (TaskbarCreated): se vuelve a registrar el AppBar.");
                _registered = false; if (_host.Config.ReserveSpace) PlaceWindow(); _force = true; return IntPtr.Zero;
            }

            switch ((int)msg)
            {
                case WM_MOUSEACTIVATE: return (IntPtr)3;   // MA_NOACTIVATE: un clic en la barra no roba el foco a la app activa
                case WM_MOUSEMOVE: { double x = (short)((long)l & 0xFFFF) / _scale, y = (short)(((long)l >> 16) & 0xFFFF) / _scale; _px = x; _py = y; Ctl.SetPointer(x, y); return IntPtr.Zero; }
                case WM_LBUTTONUP: { double x = (short)((long)l & 0xFFFF) / _scale; _host.OnBarAction(this, Ctl.OnClick(x), Ctl.ItemAt(x)); _force = true; return IntPtr.Zero; }
                case WM_RBUTTONUP: _host.OnBarAction(this, new BarAction(BarActionKind.ShowMenu), null); _force = true; return IntPtr.Zero;
                case WM_APP_BAR:
                    if ((long)w == BarNative.ABN_POSCHANGED && _registered) { PlaceWindow(); _force = true; }
                    else if ((long)w == BarNative.ABN_FULLSCREENAPP) BarNative.SetWindowPos(Hwnd, (long)l != 0 ? new IntPtr(1) /* HWND_BOTTOM */ : new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE|NOMOVE|NOACTIVATE */);
                    return IntPtr.Zero;
                case WM_DPICHANGED: if (!_resizing) { _scale = (int)((long)w & 0xFFFF) / 96.0; _dev.SetDpi(_scale); ResizeWindow(); } return IntPtr.Zero;
                case WM_DISPLAYCHANGE: _host.NotifyDisplayChange(); return IntPtr.Zero;   // el anfitrión espera a que Windows termine de acomodar las pantallas y reconcilia
            }
        }
        catch (Exception e) { _log($"[bar] mensaje {msg:X} falló en {Info.Id}: {e.GetType().Name}: {e.Message}"); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    public string Report() => $"{Info.Id}: {Diagnostics}; ventana {_winWpx}x{_winHpx}px en ({_winLeftPx},{_winTopPx}) escala {_scale:0.##}; AppBar={(_registered ? "sí" : "no")}; fondo={(_blurred is not null ? "sí" : "no")}";
}
