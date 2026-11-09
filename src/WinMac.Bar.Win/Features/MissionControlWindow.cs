using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Apps;
using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using WinMac.Bar.Win.Interop;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Una ventana de Mission Control sobre UN monitor (hay una por monitor; todas comparten el <see cref="McController"/>). Dibuja el fondo oscurecido, el campo de búsqueda y una tarjeta por ventana del monitor con su
/// miniatura (capturas del Dock, <c>PrintWindow</c>: instantáneas, no vídeo), su aplicación y su título. Sin miniatura todavía (o ventana protegida/minimizada sin captura previa): inicial de la app.
/// Estilo Golden Gate: vidrio oscuro, bordes finos, esquinas redondeadas. La lógica de entrada está en <see cref="McController"/> (probada en Linux).
/// </summary>
internal sealed class MissionControlWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104;
    private const double FadeMs = 140;

    private readonly McController _ctl; private readonly int _index; private readonly CaptureMonitor _mon; private readonly Func<long, IconBitmap?> _thumbs; private readonly Action<McCommand> _onCommand; private readonly Action<string> _log;
    private readonly BarNative.WndProc _proc; private readonly string _cls; private readonly Func<IntPtr, bool> _isSibling;
    private IntPtr _hwnd; private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _label, _search, _initial; private ID2D1SolidColorBrush? _solid;
    private readonly WallpaperSource _wall = new(); private ID2D1Bitmap1? _blurred; private readonly Dictionary<long, (IconBitmap Src, ID2D1Bitmap? Bmp)> _tcache = [];
    private double _scale = 1; private int _wPx, _hPx; private bool _open, _tracking, _dirty; private long _openedAt; private IReadOnlyList<McCard> _cards = [];

    private static int _seq;
    public MissionControlWindow(McController ctl, int index, CaptureMonitor mon, Func<long, IconBitmap?> thumbs, Action<McCommand> onCommand, Action<string> log, Func<IntPtr, bool> isSibling)
    { _isSibling = isSibling; _ctl = ctl; _index = index; _mon = mon; _thumbs = thumbs; _onCommand = onCommand; _log = log; _proc = WndProcImpl; _cls = "WinMacMission" + Interlocked.Increment(ref _seq); }

    public IntPtr Handle => _hwnd;
    public bool IsOpen => _open;
    public void Invalidate() { if (_open) _dirty = true; }

    public void Show(bool focus)
    {
        _scale = _mon.Scale; var b = _mon.Bounds;
        if (_hwnd == IntPtr.Zero)
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = _cls, hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
            BarNative.RegisterClassEx(ref cls);
            _hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW, _cls, "Mission Control", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana de Mission Control: " + Marshal.GetLastWin32Error());
            _dev = new D2DDevice(); _dev.SetDpi(_scale);
            _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _solid = _dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            _label = _dw.CreateTextFormat("Segoe UI", 12.5f); _label.ParagraphAlignment = ParagraphAlignment.Center; _label.WordWrapping = WordWrapping.NoWrap;
            _search = _dw.CreateTextFormat("Segoe UI", 15); _search.ParagraphAlignment = ParagraphAlignment.Center; _search.TextAlignment = TextAlignment.Center;
            _initial = _dw.CreateTextFormat("Segoe UI Semibold", 40); _initial.TextAlignment = TextAlignment.Center; _initial.ParagraphAlignment = ParagraphAlignment.Center;
        }

        CreateSurface(b.W, b.H); BuildBackdrop();
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), b.X, b.Y, b.W, b.H, 0x0040u /* SHOWWINDOW */);
        _open = true; _dirty = true; _openedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        if (focus) BarNative.SetForegroundWindow(_hwnd);
        Render();
    }

    public void Hide() { if (!_open) return; _open = false; BarNative.ReleaseCapture(); BarNative.ShowWindow(_hwnd, 0); foreach (var e in _tcache.Values) e.Bmp?.Dispose(); _tcache.Clear(); DropBackdrop(); DisposeSurface(); }

    public void Pump() { if (_open && (_dirty || System.Diagnostics.Stopwatch.GetElapsedTime(_openedAt).TotalMilliseconds < FadeMs + 30)) Render(); }

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

    private void BuildBackdrop()
    {
        DropBackdrop(); var previous = _dev!.Context.Target;
        try
        {
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) return;
            var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _wPx, _hPx, new RectD(0, 0, _wPx, _hPx));
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _wPx / _scale, _hPx / _scale, _scale, GlassStyle.For(DockTheme.Dark) with { BlurSigma = 36 });
        }
        catch (Exception e) { _log("[mission] fondo desenfocado no disponible: " + e.Message); DropBackdrop(); }
        finally { _dev.Context.Target = previous; }
    }

    private void DropBackdrop() { _blurred?.Dispose(); _blurred = null; }

    private static RawRectF R(McRect r) => new((float)r.X, (float)r.Y, (float)r.Right, (float)r.Bottom);
    private void Brush(Color4 c) => _solid!.Color = c;

    private void Render()
    {
        if (_swap is null || _dcomp is null || _dev is null) return;
        double w = _wPx / _scale, h = _hPx / _scale;
        double ms = System.Diagnostics.Stopwatch.GetElapsedTime(_openedAt).TotalMilliseconds; float a = (float)Math.Clamp(ms / FadeMs, 0, 1); a = 1 - (1 - a) * (1 - a);
        _cards = _ctl.CardsFor(_index, w, h); var ctx = _dev.Context;
        try
        {
            ctx.BeginDraw(); ctx.Clear(new Color4(0, 0, 0, 0));
            if (_blurred is not null) ctx.DrawBitmap(_blurred, new RawRectF(0, 0, (float)w, (float)h), a, BitmapInterpolationMode.Linear, null);
            Brush(new Color4(0.02f, 0.02f, 0.05f, (_blurred is null ? 0.86f : 0.52f) * a)); ctx.FillRectangle(new RawRectF(0, 0, (float)w, (float)h), _solid!);
            // búsqueda
            var s = new McRect((w - 360) / 2, McLayout.SearchTop, 360, McLayout.SearchH);
            Brush(new Color4(1, 1, 1, 0.14f * a)); ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(s), RadiusX = (float)(s.H / 2), RadiusY = (float)(s.H / 2) }, _solid!);
            Brush(new Color4(1, 1, 1, 0.22f * a)); ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(s), RadiusX = (float)(s.H / 2), RadiusY = (float)(s.H / 2) }, _solid!, 1);
            bool q = _ctl.Query.Length > 0; Brush(new Color4(1, 1, 1, (q ? 0.95f : 0.55f) * a));
            using (var l = _dw!.CreateTextLayout(q ? _ctl.Query : "Buscar ventanas…", _search!, (float)(s.W - 24), (float)s.H)) ctx.DrawTextLayout(new Vector2((float)s.X + 12, (float)s.Y), l, _solid!);
            if (_ctl.Filtered.Count == 0)
            {
                Brush(new Color4(1, 1, 1, 0.7f * a)); using var l = _dw.CreateTextLayout(_ctl.Total == 0 ? "No hay ventanas abiertas en este escritorio" : "Sin resultados", _search!, (float)w, 40); ctx.DrawTextLayout(new Vector2(0, (float)h / 2 - 20), l, _solid!);
            }

            foreach (var c in _cards) DrawCard(ctx, c, a);
            ctx.EndDraw(); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); _dirty = false;
        }
        catch (Exception e) { _log("[mission] dibujo: " + e.GetType().Name + ": " + e.Message); }
    }

    private void DrawCard(ID2D1DeviceContext ctx, McCard c, float a)
    {
        bool sel = c.Index == _ctl.Selected, hover = c.Index == _ctl.HoverIndex; float dim = c.Win.Minimized ? 0.55f : 1f;
        if (sel || hover) { Brush(new Color4(1, 1, 1, (sel ? 0.18f : 0.10f) * a)); ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(c.Card), RadiusX = 14, RadiusY = 14 }, _solid!); }
        var tr = new RoundedRectangle { Rect = R(c.Thumb), RadiusX = 10, RadiusY = 10 };
        Brush(new Color4(0.12f, 0.12f, 0.15f, 0.95f * a)); ctx.FillRoundedRectangle(tr, _solid!);
        var bmp = ThumbFor(c.Win.Hwnd);
        if (bmp is not null)
        {
            using var geo = ctx.Factory.CreateRoundedRectangleGeometry(tr);
            ctx.PushLayer(new LayerParameters1 { ContentBounds = R(c.Thumb), GeometricMask = geo, MaskAntialiasMode = AntialiasMode.PerPrimitive, MaskTransform = Matrix3x2.Identity, Opacity = 1 }, null!);
            ctx.DrawBitmap(bmp, R(c.Thumb), a * dim, BitmapInterpolationMode.Linear, null); ctx.PopLayer();
        }
        else
        {
            var ini = string.IsNullOrEmpty(c.Win.App) ? "?" : char.ToUpperInvariant(c.Win.App[0]).ToString();
            Brush(new Color4(1, 1, 1, 0.55f * a)); using var l = _dw!.CreateTextLayout(ini, _initial!, (float)c.Thumb.W, (float)c.Thumb.H); ctx.DrawTextLayout(new Vector2((float)c.Thumb.X, (float)c.Thumb.Y), l, _solid!);
        }

        Brush(new Color4(1, 1, 1, (sel ? 0.85f : 0.22f) * a)); ctx.DrawRoundedRectangle(tr, _solid!, sel ? 2f : 1f);
        string text = (c.Win.App.Length > 0 ? c.Win.App + " — " : "") + c.Win.Title + (c.Win.Minimized ? "  (minimizada)" : "");
        Brush(new Color4(1, 1, 1, 0.95f * a * dim));
        using (var l = _dw!.CreateTextLayout(text, _label!, (float)c.Label.W, (float)c.Label.H)) { l.TextAlignment = TextAlignment.Center; using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell); ctx.DrawTextLayout(new Vector2((float)c.Label.X, (float)c.Label.Y), l, _solid!); }
        if (sel || hover)
        {
            var x = c.Close; bool hot = hover && _ctl.HoverClose;
            Brush(hot ? new Color4(0.95f, 0.25f, 0.25f, a) : new Color4(0, 0, 0, 0.6f * a)); ctx.FillEllipse(new Ellipse(new Vector2((float)(x.X + x.W / 2), (float)(x.Y + x.H / 2)), 10, 10), _solid!);
            Brush(new Color4(1, 1, 1, a)); float cx = (float)(x.X + x.W / 2), cy = (float)(x.Y + x.H / 2);
            ctx.DrawLine(new Vector2(cx - 3.5f, cy - 3.5f), new Vector2(cx + 3.5f, cy + 3.5f), _solid!, 1.6f); ctx.DrawLine(new Vector2(cx + 3.5f, cy - 3.5f), new Vector2(cx - 3.5f, cy + 3.5f), _solid!, 1.6f);
        }
    }

    private ID2D1Bitmap? ThumbFor(long hwnd)
    {
        IconBitmap? src; try { src = _thumbs(hwnd); } catch (Exception) { src = null; }
        if (src is null) return null;
        if (_tcache.TryGetValue(hwnd, out var c) && ReferenceEquals(c.Src, src)) return c.Bmp;
        if (_tcache.Remove(hwnd, out var old)) old.Bmp?.Dispose();
        ID2D1Bitmap? bmp = null;
        try { unsafe { fixed (byte* p = src.Bgra) bmp = _dev!.Context.CreateBitmap(new SizeI(src.Width, src.Height), (IntPtr)p, (uint)(src.Width * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96)); } }
        catch (Exception) { bmp = null; }
        _tcache[hwnd] = (src, bmp); return bmp;
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (!_open) return BarNative.DefWindowProc(h, msg, w, l);
            switch ((int)msg)
            {
                case WM_MOUSEMOVE:
                    if (!_tracking) { var t = new BarNative.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<BarNative.TRACKMOUSEEVENT>(), dwFlags = 2, hwndTrack = _hwnd }; _tracking = BarNative.TrackMouseEvent(ref t); }
                    Do(_ctl.PointerMove(_cards, X(l), Y(l))); return IntPtr.Zero;
                case 0x2A3: _tracking = false; return IntPtr.Zero;
                case WM_LBUTTONUP: Do(_ctl.Click(_cards, X(l), Y(l))); return IntPtr.Zero;
                case WM_RBUTTONUP: _onCommand(McCommand.Close); return IntPtr.Zero;
                case WM_KEYDOWN or WM_SYSKEYDOWN: Do(_ctl.Key((int)w)); return IntPtr.Zero;
                case WM_CHAR: Do(_ctl.Char((char)(int)w)); return IntPtr.Zero;
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0:   // perdió el foco: se cierra, salvo que el foco pase a otra ventana de Mission Control (otro monitor)
                    if (!_isSibling(l)) _onCommand(McCommand.Close);
                    return IntPtr.Zero;
                case 0x2E0: return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[mission] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private void Do(McCommand c)
    {
        if (c.Kind == McCmdKind.None) return;
        if (c.Kind == McCmdKind.Redraw) { _dirty = true; _onCommand(c); } else _onCommand(c);
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
        foreach (var e in _tcache.Values) e.Bmp?.Dispose(); _tcache.Clear(); DropBackdrop(); _wall.Dispose(); DisposeSurface();
        _label?.Dispose(); _search?.Dispose(); _initial?.Dispose(); _solid?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
