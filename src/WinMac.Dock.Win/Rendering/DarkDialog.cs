using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Dock.Win.Shell;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// Diálogo de confirmación PROPIO de WinMac (sustituye al MessageBox blanco; WM-024, rediseñado en la iteración 7 con la referencia aprobada):
/// contenedor de esquinas amplias, icono de la acción sobre una superficie suavemente coloreada, título, descripción, separador sutil y dos botones grandes.
/// Sigue el tema de APLICACIONES de Windows (claro/oscuro) también si cambia con el diálogo abierto. <b>Cancelar es la acción predeterminada</b>: Intro sobre el foco inicial,
/// Esc y cerrar cancelan; Tab y ← → mueven el foco (anillo fino y visible); un clic solo activa si se pulsa y se suelta sobre el mismo botón; si otra ventana toma el foco, se cancela.
/// La lógica de cada acción NO cambia: este componente solo pregunta y devuelve sí/no. Modal en el hilo que lo llama (bucle propio), en el monitor del puntero y a su DPI.
/// Los diálogos nativos de Windows (selectores de archivo, UAC) y los menús contextuales de terceros no se tocan.
/// </summary>
public sealed class DarkDialog
{
    private const int WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_MOUSEMOVE = 0x200, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_NULL = 0, WM_SETTINGCHANGE = 0x1A;
    private const float Margin = 28;   // margen transparente alrededor del diálogo: la sombra
    private static readonly WndProc StaticProc = Proc; private static DarkDialog? _current; private static bool _registered;

    private readonly string _title, _text, _action, _cancelText; private readonly bool _danger, _onlyOk; private readonly ConfirmKind _kind; private readonly Action<string> _log;
    private IntPtr _hwnd; private float _s = 1; private ConfirmFrame _f = null!; private UiPalette _p = UiPalette.Dark;
    private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private ID2D1SolidColorBrush? _b; private ID2D1StrokeStyle? _round;
    private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _titleF, _bodyF, _btnF; private bool _done, _result; private int _focus;   // 0 = Cancelar (predeterminado), 1 = acción
    private int _hover = -1, _down = -1;

    private DarkDialog(string title, string text, string action, bool danger, Action<string> log, ConfirmKind kind, bool onlyOk = false, string cancel = "Cancelar")
    { _title = title; _text = text; _action = action; _danger = danger; _log = log; _kind = kind; _onlyOk = onlyOk; _cancelText = cancel; _focus = onlyOk ? 1 : 0; }

    /// <summary>Un aviso con un solo «Aceptar» (los avisos de WinMac, en vez del MessageBox blanco).</summary>
    public static void Tell(string title, string text, Action<string> log, ConfirmKind kind = ConfirmKind.Info)
    {
        var d = new DarkDialog(title, text, "Aceptar", false, log, kind, onlyOk: true);
        try { d.Run(); } catch (Exception e) { log("[diálogo] no se pudo mostrar: " + e.GetType().Name + ": " + e.Message); } finally { d.Destroy(); }
    }

    /// <summary>Pregunta y devuelve true solo si el usuario eligió la acción. Ante cualquier fallo, false (nada destructivo ocurre).</summary>
    public static bool Ask(string title, string text, string action, bool danger, Action<string> log, ConfirmKind kind = ConfirmKind.Warning)
    {
        var d = new DarkDialog(title, text, action, danger, log, kind);
        try { return d.Run(); }
        catch (Exception e) { log("[diálogo] no se pudo mostrar: " + e.GetType().Name + ": " + e.Message); return false; }
        finally { d.Destroy(); }
    }

    private bool Run()
    {
        var inst = GetModuleHandle(null); _p = UiPalette.For(SystemTheme.Current());
        if (!_registered) { var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticProc, hInstance = inst, lpszClassName = "WinMacDialog2", hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512) }; RegisterClassExW(ref cls); _registered = true; }
        GetCursorPos(out var cp); var mon = MonitorFromPoint(cp, 2); var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() }; GetMonitorInfoW(mon, ref mi);
        _s = GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 && dx >= 96 ? dx / 96f : 1f;
        _dev = new D2DDevice(); _dev.SetDpi(_s); _b = _dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _round = _dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
        Prepare();
        int wp = (int)Math.Ceiling((_f.Width + 2 * Margin) * _s), hp = (int)Math.Ceiling((_f.Height + 2 * Margin) * _s);
        int x = mi.rcWork.L + (mi.rcWork.R - mi.rcWork.L - wp) / 2, y = mi.rcWork.T + (mi.rcWork.B - mi.rcWork.T - hp) / 2 - (int)(30 * _s);
        _hwnd = CreateWindowExW(0x80 /* TOOLWINDOW */ | 0x8 /* TOPMOST */ | 0x200000 /* NOREDIRECTIONBITMAP */, "WinMacDialog2", _title, 0x80000000 /* POPUP */, x, y, wp, hp, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx");
        _current = this;
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForComposition(_dev.D3D, new SwapChainDescription1 { Width = (uint)wp, Height = (uint)hp, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch });
        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi); _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
        _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _target.SetRoot(_visual); _dcomp.Commit();
        using (var surface = _swap.GetBuffer<IDXGISurface>(0))
            _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96 * _s, 96 * _s, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
        ShowWindow(_hwnd, 5); SetForegroundWindow(_hwnd); Render();
        while (!_done && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
        return _result;
    }

    /// <summary>Tipografía y reparto del diálogo (compartido por la ventana real y la vista previa de CI).</summary>
    private void Prepare()
    {
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _titleF = _dw.CreateTextFormat("Segoe UI Semibold", 20); _bodyF = _dw.CreateTextFormat("Segoe UI", 13.5f); _btnF = _dw.CreateTextFormat("Segoe UI Semibold", 14);
        _btnF.TextAlignment = TextAlignment.Center; _btnF.ParagraphAlignment = ParagraphAlignment.Center;
        float textW = ConfirmLayout.Width - (ConfirmLayout.Pad + ConfirmLayout.Tile + ConfirmLayout.TileGap) - ConfirmLayout.Pad, th, bh;
        using (var l = _dw.CreateTextLayout(_title, _titleF, textW, 200)) th = l.Metrics.Height;
        using (var l = _dw.CreateTextLayout(_text, _bodyF, textW, 1000)) bh = l.Metrics.Height;
        _f = ConfirmLayout.Compute(th, bh, _onlyOk);
    }

    /// <summary>
    /// SOLO PARA CI: tamaño (px) de la vista previa del diálogo a esa escala. La vista previa usa el pintor real con los textos dados; no abre ninguna ventana ni valida foco, teclado o posición.
    /// </summary>
    public static (int W, int H) PreviewSize(string title, string text, bool onlyOk, double scale, D2DDevice dev)
    {
        var d = new DarkDialog(title, text, "x", false, _ => { }, ConfirmKind.Warning, onlyOk); d._dev = dev; d.Prepare();
        try { return ((int)Math.Ceiling((d._f.Width + 2 * Margin) * scale), (int)Math.Ceiling((d._f.Height + 2 * Margin) * scale)); } finally { d._dev = null; d.Destroy(); }
    }

    /// <summary>SOLO PARA CI: dibuja el diálogo en el destino del dispositivo (ya asignado, a la escala del dispositivo), con el tema indicado.</summary>
    public static void Preview(D2DDevice dev, string title, string text, string action, bool danger, ConfirmKind kind, DockTheme theme, bool onlyOk = false, int focus = 0, int hover = -1)
    {
        var d = new DarkDialog(title, text, action, danger, _ => { }, kind, onlyOk) { _p = UiPalette.For(theme) };
        d._dev = dev; d._b = dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); d._round = dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
        d.Prepare(); d._focus = onlyOk ? 1 : focus; d._hover = hover;
        try { var c = dev.Context; c.BeginDraw(); d.Paint(c); c.EndDraw(); } finally { d._swap = null; d._b?.Dispose(); d._round?.Dispose(); d._b = null; d._round = null; d._titleF?.Dispose(); d._bodyF?.Dispose(); d._btnF?.Dispose(); d._dw?.Dispose(); }
    }

    private Color4 C(Rgba c) => new(c.R, c.G, c.B, c.A);

    private void Render()
    {
        if (_dev is null || _swap is null) return;
        var c = _dev.Context; c.BeginDraw(); Paint(c); c.EndDraw(); _swap.Present(1, PresentFlags.None);
    }

    private void Paint(ID2D1DeviceContext c)
    {
        c.Clear(new Color4(0, 0, 0, 0));
        c.Transform = Matrix3x2.CreateTranslation(Margin, Margin);
        var body = new RawRectF(0, 0, _f.Width, _f.Height);
        for (int i = 6; i >= 1; i--)   // sombra suave bajo el contenedor
        { _b!.Color = new Color4(0, 0, 0, (_p.IsDark ? 0.07f : 0.035f)); float g = i * 3.2f; c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(-g, 8 - g * 0.3f, _f.Width + g, _f.Height + 8 + g), RadiusX = _f.Radius + g, RadiusY = _f.Radius + g }, _b); }
        _b!.Color = C(_p.Window); c.FillRoundedRectangle(new RoundedRectangle { Rect = body, RadiusX = _f.Radius, RadiusY = _f.Radius }, _b);
        _b.Color = C(_p.Fill(0.10f)); c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(0.5f, 0.5f, _f.Width - 0.5f, _f.Height - 0.5f), RadiusX = _f.Radius, RadiusY = _f.Radius }, _b, 1f);

        var (tileC, glyphC, actC, actHover) = ConfirmStyle.Colors(_kind, _danger, _p);
        _b.Color = C(tileC); c.FillRoundedRectangle(new RoundedRectangle { Rect = R(_f.Tile), RadiusX = 15, RadiusY = 15 }, _b);
        DrawGlyph(_kind, _f.Tile.X + _f.Tile.W / 2, _f.Tile.Y + _f.Tile.H / 2, 11.5f, C(glyphC));

        _b.Color = C(_p.Ink(0.97f)); using (var l = _dw!.CreateTextLayout(_title, _titleF!, _f.Title.W, _f.Title.H)) c.DrawTextLayout(new Vector2(_f.Title.X, _f.Title.Y), l, _b);
        _b.Color = C(_p.Ink(0.70f)); using (var l = _dw.CreateTextLayout(_text, _bodyF!, _f.Body.W, _f.Body.H)) c.DrawTextLayout(new Vector2(_f.Body.X, _f.Body.Y), l, _b);
        _b.Color = C(_p.Fill(0.10f)); c.FillRectangle(new RawRectF(ConfirmLayout.Pad, _f.SeparatorY, _f.Width - ConfirmLayout.Pad, _f.SeparatorY + 1), _b);

        if (!_onlyOk) Button(_f.Cancel, _cancelText, _hover == 0 ? (_p.IsDark ? Blend(_p.Surface, 1.18f) : Blend(_p.Surface, 0.94f)) : _p.Surface, _p.Ink(0.95f), _focus == 0, down: _down == 0);
        Button(_f.Action, _action, _hover == 1 ? actHover : actC, _p.OnColor(), _focus == 1, down: _down == 1);
        c.Transform = Matrix3x2.Identity;
    }

    private static Rgba Blend(Rgba c, float k) => new(Math.Clamp(c.R * k, 0, 1), Math.Clamp(c.G * k, 0, 1), Math.Clamp(c.B * k, 0, 1), c.A);
    private static RawRectF R(DipRect r) => new(r.X, r.Y, r.Right, r.Bottom);

    private void Button(DipRect r, string label, Rgba fill, Rgba ink, bool focused, bool down)
    {
        var c = _dev!.Context; var rr = new RoundedRectangle { Rect = R(r), RadiusX = _f.ButtonRadius, RadiusY = _f.ButtonRadius };
        _b!.Color = C(down ? Blend(fill, 0.92f) : fill); c.FillRoundedRectangle(rr, _b);
        if (focused)
        {   // foco visible pero discreto: un anillo fino del color de acento separado 2 DIP del botón
            _b.Color = C(_p.IsDark ? new Rgba(0.55f, 0.75f, 1f, 0.95f) : new Rgba(0.05f, 0.40f, 0.85f, 0.95f));
            c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(r.X - 3, r.Y - 3, r.Right + 3, r.Bottom + 3), RadiusX = _f.ButtonRadius + 3, RadiusY = _f.ButtonRadius + 3 }, _b, 1.6f);
        }

        _b.Color = C(ink); using var l = _dw!.CreateTextLayout(label, _btnF!, r.W, r.H); c.DrawTextLayout(new Vector2(r.X, r.Y), l, _b);
    }

    // ── iconos de la acción (vectoriales propios, sin recursos de terceros)
    private ID2D1PathGeometry Arc(float cx, float cy, float rad, double a0, double a1)
    {
        double sweep = ((a1 - a0) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        var g = _dev!.Factory.CreatePathGeometry(); using var s = g.Open(); Vector2 P(double a) => new((float)(cx + rad * Math.Cos(a)), (float)(cy + rad * Math.Sin(a)));
        s.BeginFigure(P(a0), FigureBegin.Hollow); s.AddArc(new ArcSegment { Point = P(a1), Size = new Size(rad, rad), SweepDirection = SweepDirection.Clockwise, ArcSize = sweep > Math.PI ? ArcSize.Large : ArcSize.Small }); s.EndFigure(FigureEnd.Open); s.Close(); return g;
    }

    private void Ln(float x1, float y1, float x2, float y2, Color4 col, float w = 2.4f) { _b!.Color = col; _dev!.Context.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), _b, w, _round); }
    private void Stroke(ID2D1Geometry g, Color4 col, float w = 2.4f) { _b!.Color = col; _dev!.Context.DrawGeometry(g, _b, w, _round); }

    private void DrawGlyph(ConfirmKind k, float cx, float cy, float s, Color4 col)
    {
        var c = _dev!.Context; _b!.Color = col;
        switch (k)
        {
            case ConfirmKind.Power:
            { using var a = Arc(cx, cy + 1, s * 0.95f, -Math.PI / 2 + 0.62, -Math.PI / 2 - 0.62); Stroke(a, col); Ln(cx, cy - s * 1.05f, cx, cy - s * 0.1f, col); break; }
            case ConfirmKind.Restart:
            { using var a = Arc(cx, cy, s * 0.95f, -Math.PI / 2 + 0.35, -Math.PI / 2 - 0.35 + 2 * Math.PI - 0.3); Stroke(a, col); Ln(cx + 1, cy - s * 0.95f, cx + s * 0.5f, cy - s * 1.35f, col); Ln(cx + 1, cy - s * 0.95f, cx + s * 0.55f, cy - s * 0.55f, col); break; }
            case ConfirmKind.SignOut:
            {
                c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - s, cy - s, cx + s * 0.1f, cy + s), RadiusX = 2.5f, RadiusY = 2.5f }, _b, 2.2f, _round!);
                Ln(cx - s * 0.2f, cy, cx + s * 1.1f, cy, col); Ln(cx + s * 0.6f, cy - s * 0.45f, cx + s * 1.1f, cy, col); Ln(cx + s * 0.6f, cy + s * 0.45f, cx + s * 1.1f, cy, col); break;
            }
            case ConfirmKind.Trash:
            {
                c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - s * 0.7f, cy - s * 0.45f, cx + s * 0.7f, cy + s * 1.0f), RadiusX = 2.2f, RadiusY = 2.2f }, _b, 2.2f, _round!);
                Ln(cx - s * 0.95f, cy - s * 0.55f, cx + s * 0.95f, cy - s * 0.55f, col); Ln(cx - s * 0.3f, cy - s * 0.55f, cx - s * 0.3f, cy - s * 0.95f, col); Ln(cx + s * 0.3f, cy - s * 0.55f, cx + s * 0.3f, cy - s * 0.95f, col); Ln(cx - s * 0.3f, cy - s * 0.95f, cx + s * 0.3f, cy - s * 0.95f, col);
                Ln(cx - s * 0.25f, cy - s * 0.05f, cx - s * 0.25f, cy + s * 0.6f, col, 1.8f); Ln(cx + s * 0.25f, cy - s * 0.05f, cx + s * 0.25f, cy + s * 0.6f, col, 1.8f); break;
            }
            case ConfirmKind.ForceQuit: c.DrawEllipse(new Ellipse(new Vector2(cx, cy), s, s), _b, 2.2f, _round!); Ln(cx - s * 0.45f, cy - s * 0.45f, cx + s * 0.45f, cy + s * 0.45f, col); Ln(cx + s * 0.45f, cy - s * 0.45f, cx - s * 0.45f, cy + s * 0.45f, col); break;
            case ConfirmKind.Info: c.DrawEllipse(new Ellipse(new Vector2(cx, cy), s, s), _b, 2.2f, _round!); Ln(cx, cy - s * 0.05f, cx, cy + s * 0.5f, col); c.FillEllipse(new Ellipse(new Vector2(cx, cy - s * 0.45f), 1.5f, 1.5f), _b); break;
            default:   // Warning: triángulo con signo de exclamación
            {
                var g = _dev!.Factory.CreatePathGeometry(); using (var sk = g.Open()) { sk.BeginFigure(new Vector2(cx, cy - s * 0.95f), FigureBegin.Hollow); sk.AddLine(new Vector2(cx + s * 1.05f, cy + s * 0.8f)); sk.AddLine(new Vector2(cx - s * 1.05f, cy + s * 0.8f)); sk.EndFigure(FigureEnd.Closed); sk.Close(); }
                Stroke(g, col, 2.2f); g.Dispose(); Ln(cx, cy - s * 0.3f, cx, cy + s * 0.25f, col); c.FillEllipse(new Ellipse(new Vector2(cx, cy + s * 0.55f), 1.5f, 1.5f), _b); break;
            }
        }
    }

    private int Hit(IntPtr l)
    {
        float x = (short)((long)l & 0xFFFF) / _s - Margin, y = (short)(((long)l >> 16) & 0xFFFF) / _s - Margin;
        return !_onlyOk && _f.Cancel.Contains(x, y) ? 0 : _f.Action.Contains(x, y) ? 1 : -1;
    }

    private void Finish(bool ok) { _result = ok; _done = true; PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); }

    private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        var me = _current; if (me is null || me._hwnd != h) return DefWindowProcW(h, msg, w, l);
        try
        {
            switch (msg)
            {
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0: me.Finish(false); return IntPtr.Zero;   // otra ventana tomó el foco: la respuesta segura
                case WM_SETTINGCHANGE: me._p = UiPalette.For(SystemTheme.Current()); me.Render(); return IntPtr.Zero;   // el tema de Windows cambió con el diálogo abierto
                case WM_MOUSEMOVE: { int hv = me.Hit(l); if (hv != me._hover) { me._hover = hv; me.Render(); } return IntPtr.Zero; }
                case WM_LBUTTONDOWN: me._down = me.Hit(l); if (me._down >= 0) me._focus = me._down; me.Render(); return IntPtr.Zero;
                case WM_LBUTTONUP: { int hv = me.Hit(l), dn = me._down; me._down = -1; if (hv >= 0 && hv == dn) me.Finish(hv == 1); else me.Render(); return IntPtr.Zero; }   // pulsar y soltar sobre el mismo botón
                case WM_KEYDOWN:
                    switch ((int)w)
                    {
                        case 0x1B: me.Finish(false); break;                                            // Esc
                        case 0x0D or 0x20: me.Finish(me._focus == 1); break;                           // Intro / Espacio sobre el botón con foco (Cancelar de inicio)
                        case 0x09 or 0x25 or 0x27 when !me._onlyOk: me._focus = 1 - me._focus; me.Render(); break;      // Tab / ← →
                    }

                    return IntPtr.Zero;
                case WM_PAINT: ValidateRect(h, IntPtr.Zero); me.Render(); return IntPtr.Zero;
                case WM_CLOSE: me.Finish(false); return IntPtr.Zero;
            }
        }
        catch (Exception e) { me._log("[diálogo] " + e.Message); me.Finish(false); }
        return DefWindowProcW(h, msg, w, l);
    }

    private void Destroy()
    {
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _visual?.Dispose(); _target?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose(); _round?.Dispose(); _b?.Dispose(); _titleF?.Dispose(); _bodyF?.Dispose(); _btnF?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASSEX { public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] private struct RC { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public uint cbSize; public RC rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool ValidateRect(IntPtr h, IntPtr r);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, IntPtr id);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr m, int type, out uint x, out uint y);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
}
