using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Dock;
using WinMac.Dock.Win.Rendering;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// «Acerca de este PC» PROPIO (It7): compacto y minimalista, inspirado en la sencillez de «Acerca de este Mac» sin copiar ningún recurso de Apple. Muestra el modelo real del equipo,
/// procesador, memoria, gráficos, Windows y la versión de WinMac (<see cref="AboutFormatter"/>); lo que Windows no informa dice «No disponible». «Más información» despliega
/// detalles y «Información de Windows…» abre la pantalla original de Windows. Sigue el tema de aplicaciones de Windows, también en caliente. Esc o perder el foco lo cierra.
/// </summary>
public sealed class AboutWindow
{
    private const int WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_MOUSEMOVE = 0x200, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_NULL = 0, WM_SETTINGCHANGE = 0x1A;
    private const float Margin = 28;
    private static readonly WndProc StaticProc = Proc; private static AboutWindow? _current; private static bool _registered;

    private readonly AboutView _v; private readonly Action _openWindows; private readonly Action<string> _log;
    private IntPtr _hwnd; private float _s = 1; private AboutFrame _f = null!; private UiPalette _p = UiPalette.Dark; private bool _expanded, _done; private int _hover = -1, _down = -1, _focus = 0;
    private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private ID2D1SolidColorBrush? _b; private ID2D1StrokeStyle? _round;
    private IDCompositionDevice? _dcomp; private IDCompositionTarget? _target; private IDCompositionVisual? _visual;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _titleF, _subF, _labelF, _valueF, _btnF; private IReadOnlyList<AboutRow> _rows = [];

    private AboutWindow(AboutView v, Action openWindows, Action<string> log) { _v = v; _openWindows = openWindows; _log = log; }

    /// <summary>Muestra la ventana (modal en el hilo que llama, con su propio bucle). <paramref name="openWindowsInfo"/> abre la información original de Windows.</summary>
    public static void Show(AboutView view, Action openWindowsInfo, Action<string> log)
    {
        var w = new AboutWindow(view, openWindowsInfo, log);
        try { w.Run(); } catch (Exception e) { log("[acerca de] no se pudo mostrar: " + e.GetType().Name + ": " + e.Message); } finally { w.Destroy(); }
    }

    /// <summary>SOLO CI: tamaño (px) y dibujo de la ventana con DATOS DE MUESTRA, fuera de pantalla. No valida foco, teclado, posición ni lectura real del equipo.</summary>
    public static (int W, int H) PreviewSize(AboutView v, bool expanded, double scale, D2DDevice dev)
    {
        var w = new AboutWindow(v, () => { }, _ => { }) { _expanded = expanded, _dev = dev }; w.Prepare();
        try { return ((int)Math.Ceiling((w._f.Width + 2 * Margin) * scale), (int)Math.Ceiling((w._f.Height + 2 * Margin) * scale)); } finally { w._dev = null; w.Destroy(); }
    }

    public static void Preview(D2DDevice dev, AboutView v, bool expanded, DockTheme theme)
    {
        var w = new AboutWindow(v, () => { }, _ => { }) { _expanded = expanded, _dev = dev, _p = UiPalette.For(theme), _focus = -1 };
        w._b = dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); w._round = dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round }); w.Prepare();
        try { var c = dev.Context; c.BeginDraw(); w.Paint(c); c.EndDraw(); } finally { w._b?.Dispose(); w._round?.Dispose(); w._titleF?.Dispose(); w._subF?.Dispose(); w._labelF?.Dispose(); w._valueF?.Dispose(); w._btnF?.Dispose(); w._dw?.Dispose(); }
    }

    private IReadOnlyList<AboutRow> Rows => _expanded ? _v.Main.Concat(_v.More).ToList() : _v.Main;

    private void Prepare()
    {
        _dw ??= DWrite.DWriteCreateFactory<IDWriteFactory>();
        _titleF ??= _dw.CreateTextFormat("Segoe UI Semibold", 22); _subF ??= _dw.CreateTextFormat("Segoe UI", 13); _labelF ??= _dw.CreateTextFormat("Segoe UI", 12.5f); _valueF ??= _dw.CreateTextFormat("Segoe UI", 13.5f); _btnF ??= _dw.CreateTextFormat("Segoe UI Semibold", 13.5f);
        _titleF.TextAlignment = TextAlignment.Center; _subF.TextAlignment = TextAlignment.Center; _btnF.TextAlignment = TextAlignment.Center; _btnF.ParagraphAlignment = ParagraphAlignment.Center;
        _rows = Rows; float valueW = AboutLayout.Width - 2 * AboutLayout.Pad - AboutLayout.LabelW - 12; var hs = new List<float>();
        foreach (var r in _rows) { using var l = _dw.CreateTextLayout(r.Value, _valueF, valueW, 200); hs.Add(Math.Max(22, l.Metrics.Height + 4)); }
        float th; using (var l = _dw.CreateTextLayout(_v.Title, _titleF, AboutLayout.Width - 2 * AboutLayout.Pad, 200)) th = l.Metrics.Height;
        _f = AboutLayout.Compute(th, _v.Subtitle is not null, hs);
    }

    private void Run()
    {
        var inst = GetModuleHandle(null); _p = UiPalette.For(SystemTheme.Current());
        if (!_registered) { var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticProc, hInstance = inst, lpszClassName = "WinMacAbout", hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512) }; RegisterClassExW(ref cls); _registered = true; }
        GetCursorPos(out var cp); var mon = MonitorFromPoint(cp, 2); var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() }; GetMonitorInfoW(mon, ref mi);
        _s = GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 && dx >= 96 ? dx / 96f : 1f;
        _dev = new D2DDevice(); _dev.SetDpi(_s); _b = _dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _round = _dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
        Prepare();
        int wp = (int)Math.Ceiling((_f.Width + 2 * Margin) * _s), hp = (int)Math.Ceiling((_f.Height + 2 * Margin) * _s);
        int x = mi.rcWork.L + (mi.rcWork.R - mi.rcWork.L - wp) / 2, y = mi.rcWork.T + (mi.rcWork.B - mi.rcWork.T - hp) / 2 - (int)(20 * _s);
        _hwnd = CreateWindowExW(0x80 | 0x8 | 0x200000, "WinMacAbout", "Acerca de este PC", 0x80000000, x, y, wp, hp, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx");
        _current = this; CreateSurface(wp, hp);
        ShowWindow(_hwnd, 5); SetForegroundWindow(_hwnd); Render();
        while (!_done && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
    }

    private void CreateSurface(int wp, int hp)
    {
        if (_dev is null) return; _dev.Context.Target = null; _bmp?.Dispose(); _bmp = null;
        if (_swap is null)
        {
            using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
            _swap = factory.CreateSwapChainForComposition(_dev.D3D, new SwapChainDescription1 { Width = (uint)wp, Height = (uint)hp, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch });
            _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi); _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
            _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _target.SetRoot(_visual); _dcomp.Commit();
        }
        else _swap.ResizeBuffers(0, (uint)wp, (uint)hp, Format.Unknown, SwapChainFlags.None);
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96 * _s, 96 * _s, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
    }

    /// <summary>«Más información» despliega o pliega los detalles: la ventana cambia de alto, centrada donde estaba.</summary>
    private void Toggle()
    {
        _expanded = !_expanded; float oldH = _f.Height; Prepare();
        int wp = (int)Math.Ceiling((_f.Width + 2 * Margin) * _s), hp = (int)Math.Ceiling((_f.Height + 2 * Margin) * _s);
        GetWindowRect(_hwnd, out var r); int ny = r.T - (int)Math.Round((_f.Height - oldH) * _s / 2);
        SetWindowPos(_hwnd, IntPtr.Zero, r.L, Math.Max(0, ny), wp, hp, 0x14 /* NOZORDER | NOACTIVATE */); CreateSurface(wp, hp); Render();
    }

    private Color4 C(Rgba c) => new(c.R, c.G, c.B, c.A);
    private static RawRectF R(DipBox r) => new(r.X, r.Y, r.Right, r.Bottom);

    private void Render() { if (_dev is null || _swap is null) return; try { var c = _dev.Context; c.BeginDraw(); Paint(c); c.EndDraw(); _swap.Present(1, PresentFlags.None); } catch (Exception e) { _log("[acerca de] dibujo: " + e.GetType().Name + ": " + e.Message); } }

    private void Paint(ID2D1DeviceContext c)
    {
        c.Clear(new Color4(0, 0, 0, 0)); c.Transform = Matrix3x2.CreateTranslation(Margin, Margin); float W = _f.Width, H = _f.Height;
        for (int i = 6; i >= 1; i--) { _b!.Color = new Color4(0, 0, 0, _p.IsDark ? 0.07f : 0.035f); float g = i * 3.2f; c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(-g, 8 - g * 0.3f, W + g, H + 8 + g), RadiusX = 22 + g, RadiusY = 22 + g }, _b); }
        _b!.Color = C(_p.Window); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(0, 0, W, H), RadiusX = 22, RadiusY = 22 }, _b);
        _b.Color = C(_p.Fill(0.10f)); c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(0.5f, 0.5f, W - 0.5f, H - 0.5f), RadiusX = 22, RadiusY = 22 }, _b, 1f);
        DrawComputer(_f.Icon);
        _b.Color = C(_p.Ink(0.97f)); using (var l = _dw!.CreateTextLayout(_v.Title, _titleF!, _f.Title.W, _f.Title.H)) c.DrawTextLayout(new Vector2(_f.Title.X, _f.Title.Y), l, _b);
        if (_v.Subtitle is not null) { _b.Color = C(_p.Ink(0.55f)); using var l = _dw.CreateTextLayout(_v.Subtitle, _subF!, _f.Subtitle.W, _f.Subtitle.H); c.DrawTextLayout(new Vector2(_f.Subtitle.X, _f.Subtitle.Y), l, _b); }
        for (int i = 0; i < _rows.Count && i < _f.Rows.Count; i++)
        {
            var r = _f.Rows[i]; var row = _rows[i];
            _b.Color = C(_p.Ink(0.55f)); using (var l = _dw.CreateTextLayout(row.Label, _labelF!, _f.LabelW, r.H)) { l.TextAlignment = TextAlignment.Trailing; c.DrawTextLayout(new Vector2(r.X, r.Y + 2), l, _b); }
            _b.Color = C(row.Available ? _p.Ink(0.95f) : _p.Ink(0.5f)); using (var l = _dw.CreateTextLayout(row.Value, _valueF!, r.Right - _f.ValueX, r.H)) c.DrawTextLayout(new Vector2(_f.ValueX, r.Y), l, _b);
        }

        _b.Color = C(_p.Fill(0.10f)); c.FillRectangle(new RawRectF(AboutLayout.Pad, _f.SeparatorY, W - AboutLayout.Pad, _f.SeparatorY + 1), _b);
        var mb = _f.MoreButton; var fill = _hover == 0 ? (_p.IsDark ? new Rgba(_p.Surface.R * 1.18f, _p.Surface.G * 1.18f, _p.Surface.B * 1.18f) : new Rgba(_p.Surface.R * 0.94f, _p.Surface.G * 0.94f, _p.Surface.B * 0.94f)) : _p.Surface;
        _b.Color = C(_down == 0 ? new Rgba(fill.R * 0.93f, fill.G * 0.93f, fill.B * 0.93f) : fill); c.FillRoundedRectangle(new RoundedRectangle { Rect = R(mb), RadiusX = 11, RadiusY = 11 }, _b);
        if (_focus == 0) { _b.Color = C(_p.IsDark ? new Rgba(0.55f, 0.75f, 1f, 0.95f) : new Rgba(0.05f, 0.40f, 0.85f, 0.95f)); c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(mb.X - 3, mb.Y - 3, mb.Right + 3, mb.Bottom + 3), RadiusX = 14, RadiusY = 14 }, _b, 1.6f); }
        _b.Color = C(_p.Ink(0.95f)); using (var l = _dw.CreateTextLayout(_expanded ? "Menos información" : "Más información", _btnF!, mb.W, mb.H)) c.DrawTextLayout(new Vector2(mb.X, mb.Y), l, _b);
        var lk = _f.WindowsLink; _b.Color = C(_hover == 1 || _focus == 1 ? (_p.IsDark ? new Rgba(0.60f, 0.80f, 1f) : new Rgba(0.0f, 0.36f, 0.80f)) : (_p.IsDark ? new Rgba(0.45f, 0.72f, 1f) : p2()));
        using (var l = _dw.CreateTextLayout("Información de Windows…", _btnF!, lk.W, lk.H)) c.DrawTextLayout(new Vector2(lk.X, lk.Y), l, _b);
        if (_focus == 1) { _b.Color = C(_p.IsDark ? new Rgba(0.55f, 0.75f, 1f, 0.95f) : new Rgba(0.05f, 0.40f, 0.85f, 0.95f)); c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(lk.X + lk.W / 2 - 110, lk.Y - 2, lk.X + lk.W / 2 + 110, lk.Bottom + 2), RadiusX = 8, RadiusY = 8 }, _b, 1.4f); }
        c.Transform = Matrix3x2.Identity;
    }

    private Rgba p2() => new(0.0f, 0.40f, 0.85f);

    /// <summary>Un ordenador de sobremesa/portátil genérico dibujado con trazos (sin recursos de terceros).</summary>
    private void DrawComputer(DipBox ic)
    {
        var c = _dev!.Context; float cx = ic.X + ic.W / 2, top = ic.Y; var ink = C(_p.Ink(0.62f)); _b!.Color = ink;
        c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 30, top + 2, cx + 30, top + 38), RadiusX = 5, RadiusY = 5 }, _b, 2.2f, _round!);
        c.DrawLine(new Vector2(cx, top + 38), new Vector2(cx, top + 47), _b, 2.2f, _round); c.DrawLine(new Vector2(cx - 13, top + 48), new Vector2(cx + 13, top + 48), _b, 2.2f, _round);
        _b.Color = C(_p.Fill(0.08f)); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 28, top + 4, cx + 28, top + 36), RadiusX = 4, RadiusY = 4 }, _b);
    }

    private int Hit(IntPtr l) { float x = (short)((long)l & 0xFFFF) / _s - Margin, y = (short)(((long)l >> 16) & 0xFFFF) / _s - Margin; return _f.MoreButton.Contains(x, y) ? 0 : _f.WindowsLink.Contains(x, y) ? 1 : -1; }
    private void Activate(int i) { if (i == 0) Toggle(); else if (i == 1) { try { _openWindows(); } catch (Exception e) { _log("[acerca de] no se pudo abrir la información de Windows: " + e.Message); } _done = true; PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); } }

    private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        var me = _current; if (me is null || me._hwnd != h) return DefWindowProcW(h, msg, w, l);
        try
        {
            switch (msg)
            {
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0: me._done = true; PostMessageW(h, WM_NULL, IntPtr.Zero, IntPtr.Zero); return IntPtr.Zero;
                case WM_SETTINGCHANGE: me._p = UiPalette.For(SystemTheme.Current()); me.Render(); return IntPtr.Zero;   // el tema de Windows cambió con la ventana abierta
                case WM_MOUSEMOVE: { int hv = me.Hit(l); if (hv != me._hover) { me._hover = hv; me.Render(); } return IntPtr.Zero; }
                case WM_LBUTTONDOWN: me._down = me.Hit(l); me.Render(); return IntPtr.Zero;
                case WM_LBUTTONUP: { int hv = me.Hit(l), dn = me._down; me._down = -1; if (hv >= 0 && hv == dn) me.Activate(hv); else me.Render(); return IntPtr.Zero; }
                case WM_KEYDOWN:
                    switch ((int)w)
                    {
                        case 0x1B: me._done = true; PostMessageW(h, WM_NULL, IntPtr.Zero, IntPtr.Zero); break;
                        case 0x09 or 0x25 or 0x27 or 0x26 or 0x28: me._focus = 1 - me._focus; me.Render(); break;
                        case 0x0D or 0x20: me.Activate(me._focus); break;
                    }

                    return IntPtr.Zero;
                case WM_PAINT: ValidateRect(h, IntPtr.Zero); me.Render(); return IntPtr.Zero;
                case WM_CLOSE: me._done = true; return IntPtr.Zero;
            }
        }
        catch (Exception e) { me._log("[acerca de] " + e.Message); me._done = true; }
        return DefWindowProcW(h, msg, w, l);
    }

    private void Destroy()
    {
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _visual?.Dispose(); _target?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose(); _round?.Dispose(); _b?.Dispose(); _titleF?.Dispose(); _subF?.Dispose(); _labelF?.Dispose(); _valueF?.Dispose(); _btnF?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
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
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
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
