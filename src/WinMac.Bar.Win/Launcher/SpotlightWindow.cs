using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Apps;
using WinMac.Bar.Launcher;
using WinMac.Bar.Win.Interop;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Launcher;

/// <summary>
/// Ventana de Spotlight: panel de vidrio centrado en el monitor del ratón (a un cuarto de la altura), campo de búsqueda grande y resultados por secciones con iconos reales.
/// Toma el foco para escribir; se cierra con Esc, al perder el foco o al abrir un resultado. La interacción está en <see cref="SpotlightController"/>.
/// </summary>
internal sealed class SpotlightWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONUP = 0x202, WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104;

    private readonly SpotlightController _ctl = new();
    private readonly Action<SpotCommand, bool> _onCommand; private readonly Action<string> _log; private readonly BarNative.WndProc _proc;
    private IntPtr _hwnd; private D2DDevice? _dev; private ID2D1DeviceContext? _ctx;
    private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _field, _title, _sub, _section, _glyph; private ID2D1SolidColorBrush? _solid;
    private readonly WallpaperSource _wall = new(); private ID2D1Bitmap1? _blurred;
    private readonly Dictionary<string, (IconBitmap Src, ID2D1Bitmap? Bmp)> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private double _scale = 1; private int _wPx, _hPx, _x, _y; private PxRect _monitor; private bool _open, _dirty;

    /// <param name="onCommand">Orden del controlador; el segundo valor dice si cambió el texto (hay que volver a buscar).</param>
    public SpotlightWindow(Action<SpotCommand, bool> onCommand, Action<string> log) { _onCommand = onCommand; _log = log; _proc = WndProcImpl; }

    public SpotlightController Controller => _ctl;
    public bool IsOpen => _open;
    public Func<SpotItem, IconBitmap?>? Icons { get; set; }
    private readonly List<(string Id, LpRect Rect)> _toolButtons = [];

    public void Show(PxRect monitor, double scale)
    {
        EnsureWindow(scale); _monitor = monitor; _ctl.Reset(); _open = true;
        Relayout(force: true);
        BarNative.SetForegroundWindow(_hwnd);
    }

    public void Hide()
    {
        if (!_open) return; _open = false; _ctl.Reset(); BarNative.ShowWindow(_hwnd, 0);
        _log($"[spotlight] cerrado: {Relayouts} cambio(s) de tamaño, {Surfaces} superficie(s) creadas");   // WM-021: one surface per open
        Relayouts = 0; Surfaces = 0;
    }

    /// <summary>Llegaron resultados nuevos (o un icono): cambia la altura si hace falta y se redibuja.</summary>
    public void Refresh() { if (_open) Relayout(force: false); }

    private int _winH;

    /// <summary>
    /// WM-021: the composition surface (swap chain, DirectComposition target and glass backdrop) is created ONCE per open at the tallest size the panel can
    /// have; a change in the number of results only resizes the window, which clips the surface. Recreating them on every height change showed an empty
    /// frame each time — the flicker seen while typing.
    /// </summary>
    private void Relayout(bool force)
    {
        int w = (int)Math.Ceiling(SpotlightController.Width * _scale), h = (int)Math.Ceiling(_ctl.Height * _scale), hMax = (int)Math.Ceiling(SpotlightController.MaxHeight * _scale);
        if (force || w != _wPx || hMax != _hPx)
        {
            CreateSurface(w, hMax);
            _x = _monitor.X + (_monitor.W - w) / 2; _y = _monitor.Y + (int)(_monitor.H * 0.22);
            BuildBackdrop(); _winH = -1;
        }

        _dirty = true; Render();   // draw first, then show the new size: never an empty frame
        if (h != _winH) { BarNative.SetWindowPos(_hwnd, new IntPtr(-1), _x, _y, w, h, 0x0040u /* SHOWWINDOW */ | 0x0010u /* NOACTIVATE */); _winH = h; Relayouts++; }
    }

    /// <summary>CI diagnostics: how many times the window changed size / the surface was rebuilt while open.</summary>
    public int Relayouts { get; private set; }
    public int Surfaces { get; private set; }

    public void Pump() { if (_open && _dirty) Render(); }

    private void EnsureWindow(double scale)
    {
        if (_hwnd == IntPtr.Zero)
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = BarNative.GetModuleHandle(null), lpszClassName = "WinMacSpotlight", hCursor = BarNative.LoadCursor(IntPtr.Zero, 32512) };
            BarNative.RegisterClassEx(ref cls);
            _hwnd = BarNative.CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "WinMacSpotlight", "Spotlight", WS_POPUP, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana de Spotlight: " + Marshal.GetLastWin32Error());
            _dev = new D2DDevice(); _ctx = _dev.Context; _solid = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            _dw = DWrite.DWriteCreateFactory<IDWriteFactory>();
            _field = _dw.CreateTextFormat("Segoe UI Light", 24); _field.ParagraphAlignment = ParagraphAlignment.Center; _field.WordWrapping = WordWrapping.NoWrap;
            _title = _dw.CreateTextFormat("Segoe UI", 14); _title.WordWrapping = WordWrapping.NoWrap;
            _sub = _dw.CreateTextFormat("Segoe UI", 11.5f); _sub.WordWrapping = WordWrapping.NoWrap;
            _section = _dw.CreateTextFormat("Segoe UI Semibold", 11); _section.TextAlignment = TextAlignment.Trailing; _section.WordWrapping = WordWrapping.NoWrap;
            _glyph = _dw.CreateTextFormat("Segoe Fluent Icons", 18); _glyph.TextAlignment = TextAlignment.Center; _glyph.ParagraphAlignment = ParagraphAlignment.Center;
        }

        if (Math.Abs(scale - _scale) > 0.001) { _scale = scale; _dev!.SetDpi(scale); _wPx = _hPx = 0; }
    }

    private void CreateSurface(int w, int h)
    {
        DisposeSurface(); _wPx = w; _hPx = h; Surfaces++;
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForComposition(_dev!.D3D, new SwapChainDescription1
            {
                Width = (uint)Math.Max(1, w), Height = (uint)Math.Max(1, h), Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
                SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch,
            });
        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi); _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
        _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _target.SetRoot(_visual); _dcomp.Commit();
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _ctx!.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _ctx.Target = _bmp;
    }

    private void BuildBackdrop()
    {
        if (_dev is null) return;
        _blurred?.Dispose(); _blurred = null;
        var previous = _dev.Context.Target;
        try
        {
            if (!_wall.Load(_dev.Context) || _wall.Bitmap is null) return;
            var region = new RectD(_x - _monitor.X, _y - _monitor.Y, _wPx, _hPx);
            var src = WallpaperMapping.SourceRectFor(_wall.Style, _wall.Width, _wall.Height, _monitor.W, _monitor.H, region);
            _blurred = GlassBackdrop.Build(_dev, _wall.Bitmap, src, _wPx / _scale, _hPx / _scale, _scale, GlassStyle.For(DockTheme.Dark) with { BlurSigma = 30 });
        }
        catch (Exception e) { _log("[spotlight] vidrio no disponible: " + e.Message); _blurred = null; }
        finally { _dev.Context.Target = previous; }
    }

    private static RawRectF R(double x, double y, double w, double h) => new((float)x, (float)y, (float)(x + w), (float)(y + h));
    private void Brush(Color4 c) => _solid!.Color = c;

    private void Render()
    {
        if (_ctx is null || _swap is null || _dcomp is null || _dw is null) return;
        try
        {
            double W = SpotlightController.Width, H = _ctl.Height; const float rad = 16;
            _ctx.BeginDraw(); _ctx.Clear(new Color4(0, 0, 0, 0));
            var shape = new RoundedRectangle { Rect = R(0.5, 0.5, W - 1, H - 1), RadiusX = rad, RadiusY = rad };
            using (var geo = _dev!.Factory.CreateRoundedRectangleGeometry(shape))
            {
                if (_blurred is not null)
                {
                    _ctx.PushLayer(new LayerParameters1 { ContentBounds = R(0, 0, W, H), GeometricMask = geo, MaskTransform = Matrix3x2.Identity, Opacity = 1, LayerOptions = LayerOptions1.None }, null!);
                    _ctx.DrawBitmap(_blurred, R(0, 0, W, H), 1, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null); _ctx.PopLayer();
                }

                Brush(new Color4(0.11f, 0.11f, 0.13f, _blurred is null ? 0.97f : 0.62f)); _ctx.FillGeometry(geo, _solid!);
                Brush(new Color4(1, 1, 1, 0.14f)); _ctx.DrawGeometry(geo, _solid!, 1);
            }

            // Campo
            Brush(new Color4(1, 1, 1, 0.75f)); _ctx.DrawEllipse(new Ellipse(new Vector2(28, 26), 8, 8), _solid!, 2); _ctx.DrawLine(new Vector2(34, 32), new Vector2(40, 38), _solid!, 2.4f);
            bool empty = _ctl.Query.Length == 0;
            Brush(new Color4(1, 1, 1, empty ? 0.45f : 0.96f));
            using (var l = _dw.CreateTextLayout(empty ? "Búsqueda de Spotlight" : _ctl.Query, _field!, (float)(W - 76), (float)SpotlightController.FieldH))
            {
                _ctx.DrawTextLayout(new Vector2(54, 0), l, _solid!);
                if (!empty) { l.HitTestTextPosition((uint)_ctl.Query.Length, false, out float cx, out _, out _); Brush(new Color4(0.04f, 0.52f, 1f, 1)); _ctx.FillRectangle(R(54 + cx + 1, 16, 2, 26), _solid!); }
            }

            // Resultados
            if (_ctl.Results.Count > 0) { Brush(new Color4(1, 1, 1, 0.10f)); _ctx.FillRectangle(R(0, SpotlightController.FieldH, W, 1), _solid!); }
            string? lastSection = null;
            for (int i = 0; i < _ctl.Results.Count; i++)
            {
                var it = _ctl.Results[i]; var r = _ctl.Row(i);
                if (i == _ctl.Selected) { Brush(new Color4(0.04f, 0.52f, 1f, 0.92f)); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(r.X, r.Y, r.W, r.H), RadiusX = 8, RadiusY = 8 }, _solid!); }
                else if (i == _ctl.Hover) { Brush(new Color4(1, 1, 1, 0.08f)); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(r.X, r.Y, r.W, r.H), RadiusX = 8, RadiusY = 8 }, _solid!); }
                DrawItemIcon(it, new LpRect(r.X + 10, r.Y + 7, 32, 32));
                Brush(new Color4(1, 1, 1, 0.96f));
                using (var l = _dw.CreateTextLayout(it.Title, _title!, (float)(r.W - 160), 20)) _ctx.DrawTextLayout(new Vector2((float)(r.X + 52), (float)(r.Y + (it.Subtitle is null ? 13 : 5))), l, _solid!);
                if (it.Subtitle is not null) { Brush(new Color4(1, 1, 1, i == _ctl.Selected ? 0.8f : 0.55f)); using var l = _dw.CreateTextLayout(it.Subtitle, _sub!, (float)(r.W - 160), 16); _ctx.DrawTextLayout(new Vector2((float)(r.X + 52), (float)(r.Y + 25)), l, _solid!); }
                if (it.Section != lastSection) { Brush(new Color4(1, 1, 1, i == _ctl.Selected ? 0.85f : 0.45f)); using var l = _dw.CreateTextLayout(it.Section, _section!, 100, 16); _ctx.DrawTextLayout(new Vector2((float)(r.X + r.W - 104), (float)(r.Y + 15)), l, _solid!); lastSection = it.Section; }
            }

            if (_ctl.Results.Count == 0 && _ctl.Query.Length > 0)
            {
                Brush(new Color4(1, 1, 1, 0.45f));
                using var l = _dw.CreateTextLayout("Sin resultados", _title!, (float)SpotlightController.ListW, 30); l.TextAlignment = TextAlignment.Center;
                _ctx.DrawTextLayout(new Vector2(0, (float)(SpotlightController.FieldH + 40)), l, _solid!);
            }

            DrawPreview();
            _ctx.EndDraw(); _dcomp.Commit(); _swap.Present(1, PresentFlags.None); _dirty = false;
        }
        catch (Exception e) { _log("[spotlight] dibujo: " + e.GetType().Name + ": " + e.Message); _dirty = false; }
    }

    /// <summary>Spotlight Pro (docs/27): preview of the selected result on the right — icon, name, details and the keys that act on it.</summary>
    private void DrawPreview()
    {
        if (_ctl.Current is not { } it || _dw is null) return;
        var p = _ctl.PreviewRect;
        Brush(new Color4(1, 1, 1, 0.10f)); _ctx!.FillRectangle(R(p.X, p.Y, 1, p.H), _solid!);
        float x = (float)(p.X + 20), w = (float)(p.W - 36), y = (float)(p.Y + 14);
        bool tool = it.Kind == SpotKind.Tool && (it.Target.StartsWith("tool:ping", StringComparison.Ordinal) || it.Target.StartsWith("tool:dns", StringComparison.Ordinal));
        if (tool)
        {
            // WM-021: compact header (small icon + title on one row) so destination, state, packets, latency and errors fit above the buttons
            DrawItemIcon(it, new LpRect(x, y, 30, 30));
            Brush(new Color4(1, 1, 1, 0.96f));
            using (var l = _dw.CreateTextLayout(it.Title, _title!, w - 40, 30)) { l.ParagraphAlignment = ParagraphAlignment.Center; _ctx.DrawTextLayout(new Vector2(x + 40, y), l, _solid!); }
            y += 40;
        }
        else
        {
            DrawItemIcon(it, new LpRect(x, y, 56, 56)); y += 68;
            Brush(new Color4(1, 1, 1, 0.96f));
            using (var l = _dw.CreateTextLayout(it.Title, _title!, w, 60)) { l.WordWrapping = WordWrapping.Wrap; _ctx.DrawTextLayout(new Vector2(x, y), l, _solid!); y += l.Metrics.Height + 4; }
            Brush(new Color4(1, 1, 1, 0.5f));
            using (var l = _dw.CreateTextLayout(it.Section, _sub!, w, 18)) { _ctx.DrawTextLayout(new Vector2(x, y), l, _solid!); y += 24; }
        }

        var (lines, keys) = SpotPro.Preview(it);
        float textLimit = (float)(p.Y + p.H - (tool ? 70 : 30));   // tools: room for the button row and the key hint below the text, never on top of it
        foreach (var line in lines.Take(8))
        {
            Brush(new Color4(1, 1, 1, 0.78f));
            using var l = _dw.CreateTextLayout(line, _sub!, w, 40); l.WordWrapping = WordWrapping.Wrap;
            if (y + l.Metrics.Height > textLimit) break;
            _ctx.DrawTextLayout(new Vector2(x, y), l, _solid!); y += l.Metrics.Height + 3;
        }

        // Tool panel (ping / DNS): Repetir · Cancelar (while running) · Copiar — real actions on the real result
        _toolButtons.Clear();
        if (tool)
        {
            bool running = it.Details?.Any(d => d.StartsWith("Estado: Enviando", StringComparison.Ordinal) || d.StartsWith("Estado: Resolviendo", StringComparison.Ordinal)) == true;
            var buttons = new List<(string Id, string Label)> { (running ? "cancel" : "repeat", running ? "Cancelar" : (it.Details is null ? "Ejecutar" : "Repetir")) };
            if (it.Details is { Count: > 0 } && !running) buttons.Add(("copy", "Copiar"));
            float bx = x, by = Math.Min(y + 8, (float)(p.Y + p.H - 62));
            foreach (var (id, label) in buttons)
            {
                using var bl = _dw.CreateTextLayout(label, _sub!, 200, 26); float bw = bl.Metrics.Width + 22;
                Brush(id == "cancel" ? new Color4(1f, 0.27f, 0.23f, 0.85f) : new Color4(1, 1, 1, 0.14f));
                _ctx!.FillRoundedRectangle(new RoundedRectangle { Rect = R(bx, by, bw, 26), RadiusX = 7, RadiusY = 7 }, _solid!);
                Brush(new Color4(1, 1, 1, 0.95f)); _ctx.DrawTextLayout(new Vector2(bx + 11, by + (26 - bl.Metrics.Height) / 2), bl, _solid!);
                _toolButtons.Add((id, new LpRect(bx, by, bw, 26))); bx += bw + 8;
            }
        }

        bool pending = _ctl.Pending >= 0 && _ctl.Pending == _ctl.Selected;
        string hint = pending ? "Pulsa Intro otra vez para ejecutar (Esc o escribir cancela)" : keys;
        Brush(pending ? new Color4(1f, 0.62f, 0.04f, 1) : new Color4(1, 1, 1, 0.5f));
        using (var l = _dw.CreateTextLayout(hint, _sub!, w, 36)) { l.WordWrapping = WordWrapping.Wrap; _ctx.DrawTextLayout(new Vector2(x, (float)(p.Y + p.H - l.Metrics.Height - 6)), l, _solid!); }
    }

    private void DrawItemIcon(SpotItem it, LpRect r)
    {
        var px = Icons?.Invoke(it);
        if (px is not null && BitmapFor(it.Target, px) is { } bmp)
        {
            var (x, y, w, h) = IconLayout.AspectFit(px.Width, px.Height, r.X, r.Y, r.W, r.H);
            _ctx!.DrawBitmap(bmp, R(x, y, w, h), 1, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null); return;
        }

        string glyph = it.Kind switch { SpotKind.Calc or SpotKind.Convert => "", SpotKind.Setting => "", SpotKind.Folder or SpotKind.Path => "", SpotKind.File => "", _ => "" };
        Brush(new Color4(1, 1, 1, 0.14f)); _ctx!.FillRoundedRectangle(new RoundedRectangle { Rect = R(r.X, r.Y, r.W, r.H), RadiusX = 8, RadiusY = 8 }, _solid!);
        Brush(new Color4(1, 1, 1, 0.9f)); using var l = _dw!.CreateTextLayout(glyph, _glyph!, (float)r.W, (float)r.H); _ctx.DrawTextLayout(new Vector2((float)r.X, (float)r.Y), l, _solid!);
    }

    private ID2D1Bitmap? BitmapFor(string key, IconBitmap px)
    {
        if (_bitmaps.TryGetValue(key, out var e) && ReferenceEquals(e.Src, px)) return e.Bmp;
        e.Bmp?.Dispose(); ID2D1Bitmap? bmp = null;
        try
        {
            unsafe
            {
                fixed (byte* p = px.Bgra)
                    bmp = _ctx!.CreateBitmap(new SizeI(px.Width, px.Height), (IntPtr)p, (uint)(px.Width * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
            }
        }
        catch (Exception) { bmp = null; }
        if (_bitmaps.Count > 300) { foreach (var v in _bitmaps.Values) v.Bmp?.Dispose(); _bitmaps.Clear(); }
        _bitmaps[key] = (px, bmp); return bmp;
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (!_open && (int)msg is WM_MOUSEMOVE or WM_LBUTTONUP or WM_KEYDOWN or WM_CHAR) return IntPtr.Zero;
            switch ((int)msg)
            {
                case WM_MOUSEMOVE: if (_ctl.PointerMove(X(l), Y(l)).Kind != SpotCommandKind.None) { _dirty = true; } return IntPtr.Zero;
                case WM_LBUTTONUP:
                {
                    var tb = _toolButtons.FirstOrDefault(b => b.Rect.Contains(X(l), Y(l)));
                    if (tb.Id is not null && _ctl.Current is { } cur) { _onCommand(new SpotCommand(SpotCommandKind.ToolAction, cur, tb.Id), false); return IntPtr.Zero; }
                    var c = _ctl.Click(X(l), Y(l)); if (c.Kind != SpotCommandKind.None) _onCommand(c, false); return IntPtr.Zero;
                }
                case WM_KEYDOWN or WM_SYSKEYDOWN:
                {
                    bool ctrl = (BarNative.GetKeyState(0x11) & 0x8000) != 0, shift = (BarNative.GetKeyState(0x10) & 0x8000) != 0;
                    var (c, changed) = _ctl.Key((int)w, ctrl, shift); _dirty = true;
                    if (c.Kind != SpotCommandKind.None || changed) _onCommand(c, changed);
                    return IntPtr.Zero;
                }
                case WM_CHAR: if (_ctl.Char((char)(int)w)) { _dirty = true; _onCommand(SpotCommand.Redraw, true); } return IntPtr.Zero;
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0 && _open: _onCommand(SpotCommand.Close, false); return IntPtr.Zero;
                case 0x2E0 /* WM_DPICHANGED */: return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[spotlight] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _scale;
    private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _scale;

    private void DisposeSurface()
    {
        if (_ctx is not null) _ctx.Target = null;
        _bmp?.Dispose(); _bmp = null; _visual?.Dispose(); _visual = null; _target?.Dispose(); _target = null; _dcomp?.Dispose(); _dcomp = null; _swap?.Dispose(); _swap = null;
    }

    public void Dispose()
    {
        foreach (var v in _bitmaps.Values) v.Bmp?.Dispose(); _bitmaps.Clear();
        _blurred?.Dispose(); _wall.Dispose(); DisposeSurface();
        _field?.Dispose(); _title?.Dispose(); _sub?.Dispose(); _section?.Dispose(); _glyph?.Dispose(); _solid?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
