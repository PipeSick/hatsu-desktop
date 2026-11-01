using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Bar.Win.Interop;
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
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_MOUSELEAVE = 0x2A3, WM_CAPTURECHANGED = 0x215;
    private readonly Action<PanelEvent> _onEvent; private readonly Action _onClosed; private readonly Action<string> _log; private readonly BarNative.WndProc _proc;
    private readonly PanelController _ctl = new();
    private IntPtr _hwnd; private D2DDevice? _dev; private PanelPainter? _painter; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private double _scale = 1; private int _wPx, _hPx; private bool _dirty, _open, _tracking; private BarTheme _theme;
    /// <summary>Mientras una acción abre un cuadro de diálogo propio, perder el foco NO debe cerrar el panel.</summary>
    public bool KeepOpen { get; set; }

    public PopoverWindow(Action<PanelEvent> onEvent, Action onClosed, Action<string> log) { _onEvent = onEvent; _onClosed = onClosed; _log = log; _proc = WndProcImpl; }

    public bool IsOpen => _open;
    public PanelKind? Kind => _open ? _ctl.Model.Kind : null;
    public (int X, int Y) Position { get; private set; }

    public void Show(PanelModel model, int anchorXpx, int barBottomPx, BarNative.RECT monitor, double scale, BarTheme theme, bool alignRight)
    {
        EnsureWindow(scale); _theme = theme; if (_painter is not null) _painter.Theme = theme;
        _ctl.SetModel(model); ResizeFor(model);
        int margin = (int)(8 * _scale), x = alignRight ? monitor.Right - _wPx - margin : Math.Clamp(anchorXpx - _wPx / 2, monitor.Left + margin, monitor.Right - _wPx - margin);
        int y = barBottomPx + (int)(4 * _scale); Position = (x, y);
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), x, y, _wPx, _hPx, 0x0040u /* SHOWWINDOW */);
        _open = true; _dirty = true; BarNative.SetForegroundWindow(_hwnd); Render();
    }

    public void SetModel(PanelModel model) { if (!_open) return; _ctl.SetModel(model); ResizeFor(model); _dirty = true; }

    public void Hide() { if (!_open) return; _open = false; BarNative.ReleaseCapture(); BarNative.ShowWindow(_hwnd, 0); _ctl.PointerLeave(); _onClosed(); }

    public void Pump() { if (_open && _dirty) Render(); }

    private void EnsureWindow(double scale)
    {
        if (_hwnd == IntPtr.Zero)
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = "WinMacPanel", hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
            BarNative.RegisterClassEx(ref cls);
            _hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "WinMacPanel", "WinMac panel", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana del panel: " + Marshal.GetLastWin32Error());
            _dev = new D2DDevice(); _painter = new PanelPainter(_dev);
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
        _painter.Paint(_ctl.Frame, _ctl.Model, _ctl.Hover); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); _dirty = false;
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch ((int)msg)
            {
                case WM_MOUSEMOVE:
                {
                    if (!_tracking) { var t = new BarNative.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<BarNative.TRACKMOUSEEVENT>(), dwFlags = 2 /* TME_LEAVE */, hwndTrack = _hwnd }; _tracking = BarNative.TrackMouseEvent(ref t); }
                    var ev = _ctl.PointerMove(X(l), Y(l)); _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero;
                }
                case WM_MOUSELEAVE: _tracking = false; if (!_ctl.Dragging) { _ctl.PointerLeave(); _dirty = true; } return IntPtr.Zero;
                case WM_LBUTTONDOWN: { BarNative.SetCapture(_hwnd); var ev = _ctl.PointerDown(X(l), Y(l)); _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero; }
                case WM_LBUTTONUP: { BarNative.ReleaseCapture(); var ev = _ctl.PointerUp(X(l), Y(l)); _dirty = true; if (ev is not null) _onEvent(ev); return IntPtr.Zero; }
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0 && _open && !KeepOpen: Hide(); return IntPtr.Zero;   // perdió el foco: como un menú
                case WM_KEYDOWN when (int)w == 0x1B: Hide(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[panel] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _scale;
    private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _scale;

    public void Dispose()
    {
        DisposeSurface(); _painter?.Dispose(); _dev?.Dispose(); if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
