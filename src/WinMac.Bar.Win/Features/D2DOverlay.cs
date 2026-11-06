using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Interop;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Superposición de Direct2D sobre una zona de la pantalla (ventana sin marco con superficie de composición, transparente). Con <c>clickThrough</c> el ratón la atraviesa y no roba el foco (zonas de Snap
/// mientras se arrastra); sin él recibe entrada (editor de zonas). La reutilizan los módulos que dibujan encima del escritorio; Launchpad y Mission Control tienen la suya por historia.
/// </summary>
internal sealed class D2DOverlay : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
    private static int _seq;
    private readonly string _cls; private readonly bool _clickThrough; private readonly Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? _user; private readonly Action<string> _log; private readonly BarNative.WndProc _proc;
    private IntPtr _hwnd; private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private int _wPx, _hPx; private double _scale = 1;

    public D2DOverlay(string name, bool clickThrough, Func<IntPtr, uint, IntPtr, IntPtr, IntPtr?>? messages, Action<string> log)
    { _cls = "WinMac" + name + Interlocked.Increment(ref _seq); _clickThrough = clickThrough; _user = messages; _log = log; _proc = WndProc; }

    public IntPtr Handle => _hwnd;
    public bool IsVisible { get; private set; }
    public double Scale => _scale;
    public CaptureRect Area { get; private set; }
    public ID2D1DeviceContext? Context => _dev?.Context;

    public void Show(CaptureRect area, double scale, bool focus)
    {
        _scale = scale; Area = area;
        if (_hwnd == IntPtr.Zero)
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = _cls, hCursor = BarNative.LoadCursor(IntPtr.Zero, _clickThrough ? 32512 : 32515) };
            BarNative.RegisterClassEx(ref cls);
            uint ex = WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | (_clickThrough ? WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT : 0);
            _hwnd = BarNative.CreateWindowEx(ex, _cls, "Hatsu Desktop", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la superposición: " + Marshal.GetLastWin32Error());
            _dev = new D2DDevice();
        }

        _dev!.SetDpi(scale);
        if (_swap is null || area.W != _wPx || area.H != _hPx) CreateSurface(area.W, area.H);
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), area.X, area.Y, area.W, area.H, (_clickThrough ? 0x0010u : 0u) | 0x0040u /* NOACTIVATE? | SHOWWINDOW */);
        IsVisible = true; if (focus) BarNative.SetForegroundWindow(_hwnd);
    }

    public void Hide() { if (!IsVisible) return; IsVisible = false; BarNative.ReleaseCapture(); BarNative.ShowWindow(_hwnd, 0); }

    /// <summary>Dibuja un cuadro. <paramref name="paint"/> recibe el contexto y el tamaño en DIP.</summary>
    public void Draw(Action<ID2D1DeviceContext, double, double> paint)
    {
        if (_swap is null || _dcomp is null || _dev is null || !IsVisible) return;
        try { var c = _dev.Context; c.BeginDraw(); c.Clear(new Vortice.Mathematics.Color4(0, 0, 0, 0)); paint(c, _wPx / _scale, _hPx / _scale); c.EndDraw(); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); }
        catch (Exception e) { _log("[superposición] dibujo: " + e.GetType().Name + ": " + e.Message); }
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
        _dev.Context.Target = _bmp;
    }

    private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try { if (_user?.Invoke(h, msg, w, l) is { } r) return r; } catch (Exception e) { _log("[superposición] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private void DisposeSurface()
    {
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _bmp = null; _visual?.Dispose(); _visual = null; _target?.Dispose(); _target = null; _dcomp?.Dispose(); _dcomp = null; _swap?.Dispose(); _swap = null;
    }

    public void Dispose()
    {
        IsVisible = false; DisposeSurface(); _dev?.Dispose(); _dev = null;
        if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
