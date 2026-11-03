using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Bar.Notifications;
using WinMac.Bar.Win.Interop;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Ventana flotante de los paneles (Control Center, Wi-Fi, calendario…). Se abre bajo la barra, toma el foco (como cualquier menú de Windows) y se cierra al perderlo o con Esc.
/// Toda la lógica de interacción vive en <see cref="PanelController"/> (probada en Linux); esta clase solo traduce mensajes de Windows, dibuja y devuelve los eventos al host.
/// La superficie se recrea al cambiar de tamaño (no ResizeBuffers, lección del Dock).
/// </summary>
internal sealed class PopoverWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_MOUSELEAVE = 0x2A3, WM_CAPTURECHANGED = 0x215, WM_MOUSEWHEEL = 0x20A;
    private readonly Action<PanelEvent> _onEvent; private readonly Action _onClosed; private readonly Action<string> _log; private readonly BarNative.WndProc _proc;
    private readonly PanelController _ctl = new();
    private IntPtr _hwnd; private D2DDevice? _dev; private PanelPainter? _painter; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private double _scale = 1; private int _wPx, _hPx; private bool _dirty, _open, _tracking; private BarTheme _theme;
    /// <summary>Mientras una acción abre un cuadro de diálogo propio, perder el foco NO debe cerrar el panel.</summary>
    public bool KeepOpen { get; set; }

    private PxRect _monitor; private int _barBottom;
    /// <summary>Alto máximo del panel en DIP: lo que queda del monitor bajo la barra, con un margen. Si el contenido no cabe (1080p, DPI alto), se desplaza.</summary>
    private static double MaxHeightDip(PxRect monitor, int barBottomPx, double scale) => Math.Max(240, (monitor.Bottom - barBottomPx) / scale - 16);

    private readonly Func<string, byte[]?>? _icons;
    public PopoverWindow(Action<PanelEvent> onEvent, Action onClosed, Action<string> log, Func<string, byte[]?>? icons = null) { _onEvent = onEvent; _onClosed = onClosed; _log = log; _icons = icons; _proc = WndProcImpl; }
    internal IntPtr Hwnd => _hwnd;
    /// <summary>Diagnóstico (CI): filas del panel abierto con su centro en píxeles de pantalla.</summary>
    internal IEnumerable<(string Id, string Text, int X, int Y)> DiagRows() =>
        _ctl.Frame.Items.Select(f => (f.W.Id, f.W.Text + (f.W.Sub == "›" ? " ›" : "") + (f.W.Enabled ? "" : " (deshabilitada)"), Position.X + (int)((f.X + f.Width / 2) * _scale), Position.Y + (int)((f.Y + f.Height / 2) * _scale)));

    public bool IsOpen => _open;
    public PanelKind? Kind => _open ? _ctl.Model.Kind : null;
    public (int X, int Y) Position { get; private set; }

    public void Show(PanelModel model, int anchorXpx, int barBottomPx, PxRect monitor, double scale, BarTheme theme, bool alignRight)
    {
        EnsureWindow(scale); _theme = theme; if (_painter is not null) _painter.Theme = theme;
        if (_side || _anim is not null) { _anim = null; _side = false; DropBackdrop(); SetOffset(0); }   // un panel normal tras el lateral: sin vidrio ni desplazamiento
        _ctl.SetMaxHeight(MaxHeightDip(monitor, barBottomPx, scale)); _monitor = monitor; _barBottom = barBottomPx; _ctl.SetModel(model); ResizeFor(model);
        var (x, y) = PopoverPlacement.Place(anchorXpx, barBottomPx, _wPx, _hPx, monitor, _scale, alignRight); Position = (x, y);   // siempre entero dentro del monitor de la barra pulsada
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), x, y, _wPx, _hPx, 0x0040u /* SHOWWINDOW */);
        _open = true; _dirty = true; _shownAt = Environment.TickCount64; bool fgOk = BarNative.SetForegroundWindow(_hwnd); Render();
        FocusDiag?.Invoke($"[panel] {model.Kind} abierto en {x},{y} ({_wPx}x{_hPx}); primer plano concedido: {fgOk}");
    }

    /// <summary>Diagnóstico (CI, WINMAC_MENU_DIAG): por qué se abre y se cierra un panel. null = sin registro.</summary>
    internal Action<string>? FocusDiag { get; set; }
    private long _shownAt;

    // ───────────────────────────── modo lateral (Centro de Notificaciones)
    private bool _side, _glass; private IntPtr _prevForeground; private (long Start, bool Opening)? _anim;
    private readonly WallpaperSource _wall = new(); private ID2D1Bitmap1? _blurred; private string? _blurKey;

    /// <summary>Hay una animación de entrada o salida en curso: el bucle de la barra debe dar fotogramas seguidos hasta que termine.</summary>
    public bool Animating => _anim is not null;

    /// <summary>
    /// Abre el panel pegado al borde derecho, en el rectángulo dado (px; <see cref="NcGeometry.Place"/>), con la altura completa y entrada deslizante.
    /// La animación solo mueve la capa de DirectComposition (no se redibuja en cada fotograma) y se omite si Windows tiene desactivadas las animaciones.
    /// </summary>
    public void ShowSide(PanelModel model, PxRect rect, PxRect monitor, double scale, BarTheme theme, bool glass)
    {
        EnsureWindow(scale); _theme = theme; if (_painter is not null) _painter.Theme = theme;
        if (!_open && BarNative.GetForegroundWindow() is var fg && fg != _hwnd) _prevForeground = fg;
        _side = true; _glass = glass; _monitor = monitor; _barBottom = rect.Y;
        _ctl.SetMaxHeight(rect.H / scale); _ctl.SetModel(model); ResizeFor(model);
        int x = rect.Right - _wPx; Position = (x, rect.Y);
        BuildBackdrop(rect);
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), x, rect.Y, _wPx, _hPx, 0x0040u /* SHOWWINDOW */);
        bool wasOpen = _open; _open = true; _dirty = true; BarNative.SetForegroundWindow(_hwnd);
        if (!wasOpen && AnimationsOn()) { _anim = (System.Diagnostics.Stopwatch.GetTimestamp(), true); SetOffset(_wPx); } else { _anim = null; SetOffset(0); }
        Render();
    }

    /// <summary>Vidrio: el fondo de pantalla tras el panel, desenfocado UNA vez por apertura (no se desenfoca lo que hay detrás en vivo: es caro). Sin fondo disponible, la tarjeta es opaca.</summary>
    private void BuildBackdrop(PxRect rect)
    {
        if (_painter is null || _dev is null) return;
        if (!_glass) { DropBackdrop(); return; }
        string key = $"{WallpaperSource.CurrentPath()}|{rect}|{_theme}|{_scale:0.###}";
        if (key == _blurKey && _blurred is not null) { _painter.Backdrop = _blurred; return; }
        DropBackdrop();
        var previous = _dev.Context.Target;
        try
        {
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) return;
            var region = new RectD(rect.X - _monitor.X, rect.Y - _monitor.Y, rect.W, rect.H);
            var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _monitor.W, _monitor.H, region);
            var style = GlassStyle.For(_theme == BarTheme.Dark ? DockTheme.Dark : DockTheme.Light) with { BlurSigma = 28 };
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _wPx / _scale, _hPx / _scale, _scale, style); _blurKey = key; _painter.Backdrop = _blurred;
        }
        catch (Exception e) { _log("[panel] vidrio no disponible: " + e.Message); DropBackdrop(); }
        finally { _dev.Context.Target = previous; }
    }

    private void DropBackdrop() { if (_painter is not null) _painter.Backdrop = null; _blurred?.Dispose(); _blurred = null; _blurKey = null; }

    private void SetOffset(double px) { if (_visual is null || _dcomp is null) return; _visual.SetOffsetX((float)Math.Round(px)); _dcomp.Commit(); }

    /// <summary>«Mostrar animaciones en Windows» (Accesibilidad → Efectos visuales). Si está apagado, el panel aparece y desaparece sin deslizarse.</summary>
    private static bool AnimationsOn() { try { return !BarNative.SystemParametersInfoW(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out int on, 0) || on != 0; } catch (Exception) { return true; } }

    public void SetModel(PanelModel model) { if (!_open) return; _ctl.SetModel(model); ResizeFor(model); _dirty = true; }

    public void Hide()
    {
        if (!_open) return; _open = false; BarNative.ReleaseCapture(); _ctl.PointerLeave();
        bool hadFocus = BarNative.GetForegroundWindow() == _hwnd;
        if (_side && AnimationsOn() && _visual is not null) _anim = (System.Diagnostics.Stopwatch.GetTimestamp(), false);   // sale deslizándose; la ventana se oculta al terminar
        else { _anim = null; BarNative.ShowWindow(_hwnd, 0); }
        if (_side && hadFocus && _prevForeground != IntPtr.Zero && BarNative.IsWindow(_prevForeground)) BarNative.SetForegroundWindow(_prevForeground);   // cerrado con Esc, «×» o el reloj: el foco vuelve a donde estaba
        _prevForeground = IntPtr.Zero; _onClosed();
        if (!_side) return;
        if (_anim is null) { _side = false; DropBackdrop(); }
    }

    public void Pump()
    {
        if (_anim is { } a)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - a.Start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, dur = a.Opening ? NcGeometry.OpenMs : NcGeometry.CloseMs;
            SetOffset(NcGeometry.SlideOffset(ms, dur, _wPx, a.Opening));
            if (ms >= dur)
            {
                _anim = null;
                if (!a.Opening && !_open) { BarNative.ShowWindow(_hwnd, 0); SetOffset(0); _side = false; DropBackdrop(); }
            }
        }

        if (_open && _dirty) Render();
    }

    private void EnsureWindow(double scale)
    {
        if (_hwnd == IntPtr.Zero)
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = "WinMacPanel", hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
            BarNative.RegisterClassEx(ref cls);
            _hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "WinMacPanel", "WinMac panel", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana del panel: " + Marshal.GetLastWin32Error());
            _dev = new D2DDevice(); _painter = new PanelPainter(_dev) { IconProvider = _icons };
        }

        if (Math.Abs(scale - _scale) > 0.001) { _scale = scale; _dev!.SetDpi(scale); _wPx = _hPx = 0; }
    }

    private void ResizeFor(PanelModel model)
    {
        var f = _ctl.Frame; int w = (int)Math.Ceiling(f.Width * _scale), h = (int)Math.Ceiling(f.Height * _scale);
        if (w == _wPx && h == _hPx && _swap is not null) return;
        _wPx = w; _hPx = h; DisposeSurface();
        _swap = CreateSwap(w, h); _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev!.Dxgi); _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
        _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _target.SetRoot(_visual); _dcomp.Commit();
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
        if (_open) BarNative.SetWindowPos(_hwnd, IntPtr.Zero, Position.X, Position.Y, w, h, 0x0004u | 0x0010u /* NOZORDER | NOACTIVATE */);
        _dirty = true;
    }

    private IDXGISwapChain1 CreateSwap(int w, int h)
    {
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        return factory.CreateSwapChainForComposition(_dev!.D3D, new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, w), Height = (uint)Math.Max(1, h), Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch,
        });
    }

    private void DisposeSurface()
    {
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _bmp = null; _visual?.Dispose(); _visual = null; _target?.Dispose(); _target = null; _dcomp?.Dispose(); _dcomp = null; _swap?.Dispose(); _swap = null;
    }

    private void Render()
    {
        if (_painter is null || _swap is null || _dcomp is null) return;
        _painter.HoverPoint = _ctl.Pointer; _painter.TrayFocus = _ctl.TrayFocus; _painter.Focus = _ctl.Focus; _painter.Paint(_ctl.Frame, _ctl.Model, _ctl.Hover); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); _dirty = false;
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (!_open && (int)msg is WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP or WM_MOUSEWHEEL) return IntPtr.Zero;   // saliendo (animación): no responde a clics
            switch ((int)msg)
            {
                case WM_MOUSEMOVE:
                {
                    if (!_tracking) { var t = new BarNative.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<BarNative.TRACKMOUSEEVENT>(), dwFlags = 2 /* TME_LEAVE */, hwndTrack = _hwnd }; _tracking = BarNative.TrackMouseEvent(ref t); }
                    _lastX = X(l); _lastY = Y(l); var ev = _ctl.PointerMove(_lastX, _lastY); _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero;
                }
                case WM_MOUSEWHEEL:
                {
                    int delta = (short)(((long)w >> 16) & 0xFFFF); if (_ctl.Scroll(-delta / 120.0 * 48)) { var ev = _ctl.PointerMove(_lastX, _lastY); _dirty = true; if (ev is not null) _onEvent(ev); }   // una muesca = 48 DIP
                    return IntPtr.Zero;
                }
                case WM_MOUSELEAVE: _tracking = false; if (!_ctl.Dragging) { _ctl.PointerLeave(); _dirty = true; } return IntPtr.Zero;
                case WM_LBUTTONDOWN: { BarNative.SetCapture(_hwnd); var ev = _ctl.PointerDown(X(l), Y(l)); _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero; }
                case WM_LBUTTONUP: { BarNative.ReleaseCapture(); var ev = _ctl.PointerUp(X(l), Y(l)); _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero; }
                case 0x205 /* WM_RBUTTONUP */: { var ev = _ctl.RightClick(X(l), Y(l)); if (ev is not null) _onEvent(ev); return IntPtr.Zero; }   // menú de una app en segundo plano
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0 && _open && !KeepOpen:   // perdió el foco: como un menú
                    if (FocusDiag is not null) { var fg = BarNative.GetForegroundWindow(); var sb = new System.Text.StringBuilder(64); BarNative.GetClassNameW(fg, sb, 64); BarNative.GetWindowThreadProcessId(fg, out uint lp); FocusDiag($"[panel] se cierra al perder el foco: primer plano ahora {sb} (pid {lp}, la barra es {Environment.ProcessId}) tras {(Environment.TickCount64 - _shownAt)} ms; {Environment.StackTrace.Split('\n').Skip(1).Take(14).Aggregate("", (a, s) => a + " ← " + s.Trim().Replace("at WinMac.", ""))}"); }
                    Hide(); return IntPtr.Zero;
                case WM_KEYDOWN when (int)w == 0x1B: Hide(); return IntPtr.Zero;
                case WM_KEYDOWN:   // WM-004: grid of background apps; WM-022: Tab/arrows/Intro over the controls of the other panels
                {
                    bool shift = (BarNative.GetKeyState(0x10) & 0x8000) != 0;
                    var ev = _ctl.Model.Kind == PanelKind.Tray ? _ctl.TrayKey((int)w, shift) : _ctl.FocusKey((int)w, shift);
                    _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero;
                }
                case 0x2E0 /* WM_DPICHANGED */: return IntPtr.Zero;   // el tamaño y el escalado del panel los fija Show() según el monitor de destino: Windows no debe redimensionarlo por su cuenta al moverlo entre monitores
            }
        }
        catch (Exception e) { _log("[panel] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private double _lastX, _lastY;
    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _scale;
    private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _scale;

    public void Dispose()
    {
        DropBackdrop(); _wall.Dispose(); DisposeSurface(); _painter?.Dispose(); _dev?.Dispose(); if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
