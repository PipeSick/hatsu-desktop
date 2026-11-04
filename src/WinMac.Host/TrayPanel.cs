using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Dock;
using WinMac.Dock.Win.Rendering;
using WinMac.Dock.Win.Shell;

namespace WinMac.Host.App;

/// <summary>
/// WM-011: WinMac's own tray panel — dark, compact, rounded (DWM), drawn with Direct2D like Settings, at the system DPI. It opens next to the tray icon above
/// the taskbar, follows the theme of WinMac (dark), closes on Esc or when it loses the focus, and works with the keyboard (↑ ↓ Intro). Same real actions as the old
/// menu plus restarting a module; «Salir» is the orderly shutdown that restores the desktop.
/// </summary>
internal sealed class TrayPanel : IDisposable
{
    private const int WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_LBUTTONUP = 0x202, WM_MOUSEMOVE = 0x200, WM_PAINT = 0xF, WM_CLOSE = 0x10;
    private static readonly Native.WndProc StaticProc = WndProc;   // registered once for the class: never collected
    private readonly Action<string> _act; private readonly Action<string> _log;
    private IntPtr _hwnd; private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private ID2D1SolidColorBrush? _b;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _title, _body, _small; private double _scale = 1;
    private IReadOnlyList<TrayPanelModel.Item> _items = []; private int _focus = -1; private string? _hover;
    private UiPalette _p = UiPalette.Dark;   // It7: sigue el tema de APLICACIONES de Windows (claro/oscuro), también si cambia con el panel abierto
    private static Color4 C(Rgba c) => new(c.R, c.G, c.B, c.A);
    private static bool _registered;

    public TrayPanel(Action<string> act, Action<string> log) { _act = act; _log = log; }

    public bool IsOpen => _hwnd != IntPtr.Zero;

    public void Show(IReadOnlyList<TrayPanelModel.Item> items, int cursorX, int cursorY)
    {
        Close();
        _items = items; _focus = -1; _hover = null; _p = UiPalette.For(SystemTheme.Current());
        uint dpi = Native.GetDpiForSystem(); if (dpi < 96) dpi = 96; _scale = dpi / 96.0;
        int w = (int)Math.Round(TrayPanelModel.Width * _scale), h = (int)Math.Round(TrayPanelModel.Height(items) * _scale);
        // Above the cursor (the taskbar is at the bottom); kept inside the screen
        int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1);
        int x = Math.Clamp(cursorX - w + (int)(24 * _scale), 8, Math.Max(8, sw - w - 8)), y = cursorY - h - (int)(12 * _scale); if (y < 8) y = Math.Min(cursorY + (int)(12 * _scale), sh - h - 8);
        var inst = Native.GetModuleHandle(null);
        if (!_registered) { var cls = new Native.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(), lpfnWndProc = StaticProc, hInstance = inst, lpszClassName = "WinMacTrayPanel", hCursor = Native.LoadCursorW(IntPtr.Zero, (IntPtr)32512) }; Native.RegisterClassEx(ref cls); _registered = true; }
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW | 0x8 /* TOPMOST */, "WinMacTrayPanel", "WinMac", Native.WS_POPUP, x, y, w, h, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("no se pudo crear el panel de la bandeja");
        _current = this;
        int round = 2; Native.DwmSetWindowAttribute(_hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE: ROUND */, ref round, 4);
        int dark = _p.IsDark ? 1 : 0; Native.DwmSetWindowAttribute(_hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);
        _dev = new D2DDevice(); _dev.SetDpi(_scale); Fonts();
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForHwnd(_dev.D3D, _hwnd, new SwapChainDescription1 { Width = (uint)w, Height = (uint)h, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential });
        using (var surface = _swap.GetBuffer<IDXGISurface>(0))
            _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
        Native.ShowWindow(_hwnd, 5); Native.SetForegroundWindow(_hwnd);
        Render();
    }

    private static TrayPanel? _current;

    private void Render()
    {
        if (_dev is null || _swap is null || _hwnd == IntPtr.Zero) return;
        try { Draw(); _swap.Present(1, PresentFlags.None); }
        catch (Exception e) { _log("[host] panel de la bandeja: dibujo: " + e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>SOLO CI: dibuja el panel con estados de muestra fuera de pantalla (mismo código de dibujo) y lo guarda como PNG.</summary>
    public static string RenderOffscreen(string png, IReadOnlyList<TrayPanelModel.Item> items, double scale, bool dark = true)
    {
        var p = new TrayPanel(_ => { }, _ => { }) { _items = items, _scale = scale, _focus = 4, _p = dark ? UiPalette.Dark : UiPalette.Light };
        int w = (int)Math.Ceiling(TrayPanelModel.Width * scale), h = (int)Math.Ceiling(TrayPanelModel.Height(items) * scale);
        p._dev = new D2DDevice(true); p._dev.SetDpi(scale); p.Fonts();
        using var target = GlassBackdrop.CreateTarget(p._dev.Context, w, h, scale); p._dev.Context.Target = target;
        p.Draw();
        using var cpu = GlassBackdrop.CreateTarget(p._dev.Context, w, h, scale, readable: true); cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read); var bgra = new byte[map.Pitch * h];
        try { Marshal.Copy(map.Bits, bgra, 0, bgra.Length); } finally { cpu.Unmap(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png))!);
        File.WriteAllBytes(png, WinMac.Dock.Imaging.PngWriter.EncodeRgba(WinMac.Dock.Imaging.PngWriter.BgraPremulToRgba(bgra, (int)map.Pitch, w, h), w, h));
        string desc = p._dev.Description; p._dev.Context.Target = null; p.Close(); p._dev?.Dispose();
        return $"{w}x{h}px, {desc}";
    }

    private void Fonts()
    {
        _b = _dev!.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _title = _dw.CreateTextFormat("Segoe UI Semibold", 14); _body = _dw.CreateTextFormat("Segoe UI", 13); _small = _dw.CreateTextFormat("Segoe UI", 11.5f);
        foreach (var f in new[] { _title, _body, _small }) { f.WordWrapping = WordWrapping.NoWrap; f.ParagraphAlignment = ParagraphAlignment.Center; }
    }

    private void Draw()
    {
        {
            var c = _dev!.Context; c.BeginDraw(); c.Clear(C(_p.Window));
            float W = (float)TrayPanelModel.Width; var focusId = _focus >= 0 ? TrayPanelModel.Focusable(_items).ElementAtOrDefault(_focus) : null;
            foreach (var (i, y0, hh) in TrayPanelModel.Layout(_items))
            {
                float y = (float)y0, h = (float)hh, x = (float)TrayPanelModel.Pad + 4;
                switch (i.Kind)
                {
                    case TrayPanelModel.Kind.Header:
                        _b!.Color = C(_p.Ink(0.95f)); Txt(i.Text, _title!, x + 4, y + 6, W - 40, 22);
                        _b.Color = C(_p.Ink(0.55f)); Txt(i.Sub!, _small!, x + 4, y + 28, W - 40, 18);
                        break;
                    case TrayPanelModel.Kind.Module:
                    {
                        var dot = TrayPanelModel.StateColor(i.State ?? ModuleState.Disabled) switch { "green" => new Color4(0.2f, 0.78f, 0.35f, 1), "amber" => new Color4(1f, 0.7f, 0.1f, 1), "red" => new Color4(1f, 0.27f, 0.23f, 1), _ => new Color4(0.55f, 0.55f, 0.58f, 1) };
                        _b!.Color = dot; c.FillEllipse(new Ellipse(new Vector2(x + 8, y + h / 2), 4.5f, 4.5f), _b);
                        _b.Color = C(_p.Ink(0.92f)); Txt(i.Text, _body!, x + 22, y + 5, W - 140, 20);
                        _b.Color = C(_p.Ink(0.5f)); Txt(i.Sub!, _small!, x + 22, y + 24, W - 140, 16);
                        if (i.ActionLabel is { } al)
                        {
                            var (bx, bw) = TrayPanelModel.ButtonX(i); bool hot = _hover == i.Action || focusId == i.Action;
                            _b.Color = C(_p.Fill(hot ? 0.24f : 0.11f)); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF((float)bx, y + 10, (float)(bx + bw), y + h - 10), RadiusX = 7, RadiusY = 7 }, _b);
                            _b.Color = C(_p.Ink(0.95f)); using var l = _dw!.CreateTextLayout(al, _small!, (float)bw, h - 20); l.TextAlignment = TextAlignment.Center; c.DrawTextLayout(new Vector2((float)bx, y + 10), l, _b);
                        }
                        break;
                    }
                    case TrayPanelModel.Kind.Separator:
                        _b!.Color = C(_p.Fill(0.10f)); c.FillRectangle(new RawRectF(x + 4, y + h / 2, W - x - 4, y + h / 2 + 1), _b); break;
                    default:
                    {
                        bool hot = _hover == i.Action || focusId == i.Action;
                        if (hot) { _b!.Color = i.Danger ? new Color4(1f, 0.27f, 0.23f, 0.85f) : new Color4(0.04f, 0.52f, 1f, 0.9f); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x - 2, y + 2, W - x + 2, y + h - 2), RadiusX = 6, RadiusY = 6 }, _b); }
                        _b!.Color = hot ? C(_p.OnColor()) : C(_p.Ink(i.Danger ? 0.95f : 0.9f));
                        if (i.Sub is null) Txt(i.Text, _body!, x + 6, y, W - 30, h);
                        else { Txt(i.Text, _body!, x + 6, y + 4, W - 30, 20); _b.Color = hot ? C(_p.OnColor(0.85f)) : C(_p.Ink(0.5f)); Txt(i.Sub, _small!, x + 6, y + 23, W - 30, 16); }
                        break;
                    }
                }
            }

            c.EndDraw();
        }
    }

    private void Txt(string s, IDWriteTextFormat f, float x, float y, float w, float h)
    {
        using var l = _dw!.CreateTextLayout(s, f, w, h); using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell);
        _dev!.Context.DrawTextLayout(new Vector2(x, y), l, _b!);
    }

    private (double X, double Y) Dip(IntPtr l) => ((short)((long)l & 0xFFFF) / _scale, (short)(((long)l >> 16) & 0xFFFF) / _scale);

    private static IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        var me = _current; if (me is null || me._hwnd != h) return Native.DefWindowProc(h, msg, w, l);
        try
        {
            switch (msg)
            {
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0: me.Close(); return IntPtr.Zero;   // lost the focus: closes, like a menu
                case 0x1A /* WM_SETTINGCHANGE */: me._p = UiPalette.For(SystemTheme.Current()); { int dk = me._p.IsDark ? 1 : 0; Native.DwmSetWindowAttribute(h, 20, ref dk, 4); } me.Render(); return IntPtr.Zero;   // el tema de Windows cambió con el panel abierto
                case WM_MOUSEMOVE: { var (x, y) = me.Dip(l); var hit = TrayPanelModel.Hit(me._items, x, y); if (hit != me._hover) { me._hover = hit; me.Render(); } return IntPtr.Zero; }
                case WM_LBUTTONUP: { var (x, y) = me.Dip(l); if (TrayPanelModel.Hit(me._items, x, y) is { } a) me.Run(a); return IntPtr.Zero; }
                case WM_KEYDOWN:
                {
                    var f = TrayPanelModel.Focusable(me._items);
                    switch ((int)w)
                    {
                        case 0x1B: me.Close(); break;                                                     // Esc
                        case 0x28: me._focus = (me._focus + 1) % f.Count; me.Render(); break;            // ↓
                        case 0x26: me._focus = me._focus <= 0 ? f.Count - 1 : me._focus - 1; me.Render(); break;   // ↑
                        case 0x0D when me._focus >= 0: me.Run(f[me._focus]); break;                      // Intro
                    }

                    return IntPtr.Zero;
                }
                case WM_PAINT: Native.ValidateRect(h, IntPtr.Zero); me.Render(); return IntPtr.Zero;
                case WM_CLOSE: me.Close(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { me._log("[host] panel de la bandeja: " + e.Message); }
        return Native.DefWindowProc(h, msg, w, l);
    }

    private void Run(string action) { Close(); _log("[host] bandeja: " + action); _act(action); }

    public void Close()
    {
        var h = _hwnd; _hwnd = IntPtr.Zero;
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _bmp = null; _swap?.Dispose(); _swap = null; _b?.Dispose(); _b = null; _title?.Dispose(); _body?.Dispose(); _small?.Dispose(); _dw?.Dispose(); _dw = null; _dev?.Dispose(); _dev = null;
        if (h != IntPtr.Zero) Native.DestroyWindow(h);
    }

    public void Dispose() => Close();
}
