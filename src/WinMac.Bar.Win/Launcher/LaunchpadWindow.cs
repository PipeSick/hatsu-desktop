using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Apps;
using WinMac.Bar.Launcher;
using WinMac.Bar.Win.Interop;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Launcher;

/// <summary>
/// Ventana de Launchpad: cubre el monitor del ratón, encima de todo, toma el foco (para escribir la búsqueda) y se cierra con Esc, con un clic en el fondo, al abrir una app
/// o al perder el foco. Toda la interacción está en <see cref="LaunchpadController"/> (probada en Linux); aquí solo se traducen los mensajes de Windows y se dibuja.
/// </summary>
internal sealed class LaunchpadWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MOUSEWHEEL = 0x20A, WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104;
    private const double FadeMs = 160;

    private readonly LaunchpadController _ctl = new();
    private readonly Action<LpCommand> _onCommand; private readonly Action<string> _log; private readonly BarNative.WndProc _proc;
    private IntPtr _hwnd; private D2DDevice? _dev; private LaunchpadPainter? _painter;
    private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private readonly WallpaperSource _wall = new(); private ID2D1Bitmap1? _blurred;
    private double _scale = 1; private int _wPx, _hPx; private PxRect _monitor; private bool _open, _dirty, _tracking; private long _openedAt; private bool _fading;

    public LaunchpadWindow(Action<LpCommand> onCommand, Action<string> log) { _onCommand = onCommand; _log = log; _proc = WndProcImpl; }

    public LaunchpadController Controller => _ctl;
    public bool IsOpen => _open;
    public bool Animating => _fading;
    public Func<InstalledApp, IconBitmap?>? Icons { get; set; }

    public void Show(PxRect monitor, double scale, bool animate)
    {
        EnsureWindow(scale);
        bool resized = monitor.W != _wPx || monitor.H != _hPx || monitor.X != _monitor.X || monitor.Y != _monitor.Y;
        _monitor = monitor;
        _ctl.SetSize(monitor.W / _scale, monitor.H / _scale); _ctl.Reset();
        if (resized || _swap is null) CreateSurface(monitor.W, monitor.H);
        BuildBackdrop();
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), monitor.X, monitor.Y, monitor.W, monitor.H, 0x0040u /* SHOWWINDOW */);
        _open = true; _dirty = true; _fading = animate; _openedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        BarNative.SetForegroundWindow(_hwnd);
        Render();
    }

    public void Hide()
    {
        if (!_open) return; _open = false; _fading = false; _ctl.Reset();
        BarNative.ReleaseCapture(); BarNative.ShowWindow(_hwnd, 0);
    }

    /// <summary>La lista cambió (catálogo releído, app ocultada, orden nuevo) o llegó un icono: se redibuja.</summary>
    public void Invalidate() { if (_open) _dirty = true; }

    public void Pump() { if (_open && (_dirty || _fading)) Render(); }

    private void EnsureWindow(double scale)
    {
        if (_hwnd == IntPtr.Zero)
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = "WinMacLaunchpad", hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
            BarNative.RegisterClassEx(ref cls);
            _hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "WinMacLaunchpad", "Launchpad", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana de Launchpad: " + Marshal.GetLastWin32Error());
            _dev = new D2DDevice(); _painter = new LaunchpadPainter(_dev) { Icons = a => Icons?.Invoke(a) };
        }

        if (Math.Abs(scale - _scale) > 0.001) { _scale = scale; _dev!.SetDpi(scale); _wPx = _hPx = 0; DropBackdrop(); }
    }

    private void CreateSurface(int w, int h)
    {
        DisposeSurface(); _wPx = w; _hPx = h;
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForComposition(_dev!.D3D, new SwapChainDescription1
            {
                Width = (uint)Math.Max(1, w), Height = (uint)Math.Max(1, h), Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch,
            });
        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi); _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
        _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _target.SetRoot(_visual); _dcomp.Commit();
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp; DropBackdrop();
    }

    /// <summary>El fondo de pantalla del monitor, desenfocado una vez por apertura (como el panel lateral). Sin fondo disponible, un velo oscuro opaco.</summary>
    private void BuildBackdrop()
    {
        if (_dev is null || _painter is null) return;
        DropBackdrop();
        var previous = _dev.Context.Target;
        try
        {
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) return;
            var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _monitor.W, _monitor.H, new RectD(0, 0, _monitor.W, _monitor.H));
            var style = GlassStyle.For(DockTheme.Dark) with { BlurSigma = 36 };
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _wPx / _scale, _hPx / _scale, _scale, style); _painter.Backdrop = _blurred;
        }
        catch (Exception e) { _log("[launchpad] fondo desenfocado no disponible: " + e.Message); DropBackdrop(); }
        finally { _dev.Context.Target = previous; }
    }

    private void DropBackdrop() { if (_painter is not null) _painter.Backdrop = null; _blurred?.Dispose(); _blurred = null; }

    private void Render()
    {
        if (_painter is null || _swap is null || _dcomp is null) return;
        float opacity = 1;
        if (_fading)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _openedAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            opacity = (float)Math.Clamp(ms / FadeMs, 0, 1); opacity = 1 - (1 - opacity) * (1 - opacity);   // salida suave
            if (ms >= FadeMs) { _fading = false; opacity = 1; }
        }

        try { _painter.Paint(_ctl, opacity); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); _dirty = false; }
        catch (Exception e) { _log("[launchpad] dibujo: " + e.GetType().Name + ": " + e.Message); _fading = false; }
    }

    private void Do(LpCommand c)
    {
        if (c.Kind == LpCommandKind.None) return;
        _dirty = true;
        if (c.Kind != LpCommandKind.Redraw) _onCommand(c);
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (!_open && (int)msg is WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP or WM_RBUTTONUP or WM_MOUSEWHEEL or WM_KEYDOWN or WM_CHAR) return IntPtr.Zero;
            switch ((int)msg)
            {
                case WM_MOUSEMOVE:
                    if (!_tracking) { var t = new BarNative.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<BarNative.TRACKMOUSEEVENT>(), dwFlags = 2, hwndTrack = _hwnd }; _tracking = BarNative.TrackMouseEvent(ref t); }
                    Do(_ctl.PointerMove(X(l), Y(l))); return IntPtr.Zero;
                case 0x2A3 /* WM_MOUSELEAVE */: _tracking = false; return IntPtr.Zero;
                case WM_LBUTTONDOWN: BarNative.SetCapture(_hwnd); Do(_ctl.PointerDown(X(l), Y(l))); return IntPtr.Zero;
                case WM_LBUTTONUP: BarNative.ReleaseCapture(); Do(_ctl.PointerUp(X(l), Y(l))); return IntPtr.Zero;
                case WM_RBUTTONUP: Do(_ctl.RightClick(X(l), Y(l))); return IntPtr.Zero;
                case WM_MOUSEWHEEL: Do(_ctl.Wheel((short)(((long)w >> 16) & 0xFFFF))); return IntPtr.Zero;
                case WM_KEYDOWN or WM_SYSKEYDOWN: Do(_ctl.Key((int)w)); return IntPtr.Zero;
                case WM_CHAR: Do(_ctl.Char((char)(int)w)); return IntPtr.Zero;
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0 && _open: _onCommand(LpCommand.Close); return IntPtr.Zero;   // perdió el foco (otra ventana, Alt+Tab, la tecla Windows otra vez)
                case 0x2E0 /* WM_DPICHANGED */: return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[launchpad] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _scale;
    private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _scale;

    private void DisposeSurface()
    {
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _bmp = null; _visual?.Dispose(); _visual = null; _target?.Dispose(); _target = null; _dcomp?.Dispose(); _dcomp = null; _swap?.Dispose(); _swap = null;
    }

    public void Dispose()
    {
        DropBackdrop(); _wall.Dispose(); DisposeSurface(); _painter?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
