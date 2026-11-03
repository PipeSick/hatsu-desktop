using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// WM-024: WinMac's own confirmation dialog (it replaces the white MessageBox in WinMac's main interface): dark, rounded (DWM), Segoe UI, a discreet warning
/// mark, «Cancelar» and the action (red when it destroys something). <b>Cancelar is the default</b>: Intro on the default focus, Esc and closing all cancel.
/// Tab / ← → move between the two buttons. Modal on the calling thread (own message loop), centred on the monitor under the pointer, at its DPI.
/// Native dialogs stay where Windows requires them (file pickers, UAC).
/// </summary>
public sealed class DarkDialog
{
    private const int WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_LBUTTONUP = 0x202, WM_MOUSEMOVE = 0x200, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_NULL = 0;
    private static readonly WndProc StaticProc = Proc; private static DarkDialog? _current; private static bool _registered;

    private readonly string _title, _text, _action; private readonly bool _danger, _onlyOk; private readonly Action<string> _log;
    private IntPtr _hwnd; private float _s = 1; private float _w = 420, _h = 160; private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private ID2D1SolidColorBrush? _b;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _titleF, _bodyF, _btnF; private bool _done, _result; private int _focus;   // 0 = Cancelar (default), 1 = action
    private int _hover = -1; private RawRectF _cancel, _ok;

    private DarkDialog(string title, string text, string action, bool danger, Action<string> log, bool onlyOk = false) { _title = title; _text = text; _action = action; _danger = danger; _log = log; _onlyOk = onlyOk; if (onlyOk) _focus = 1; }

    /// <summary>An information message with a single «Aceptar» (WinMac's notices, instead of the white MessageBox).</summary>
    public static void Tell(string title, string text, Action<string> log)
    {
        var d = new DarkDialog(title, text, "Aceptar", false, log, onlyOk: true);
        try { d.Run(); } catch (Exception e) { log("[diálogo] no se pudo mostrar: " + e.GetType().Name + ": " + e.Message); } finally { d.Destroy(); }
    }

    /// <summary>Asks and returns true only if the user chose the action.</summary>
    public static bool Ask(string title, string text, string action, bool danger, Action<string> log)
    {
        var d = new DarkDialog(title, text, action, danger, log);
        try { return d.Run(); }
        catch (Exception e) { log("[diálogo] no se pudo mostrar: " + e.GetType().Name + ": " + e.Message); return false; }   // when in doubt, nothing destructive happens
        finally { d.Destroy(); }
    }

    private bool Run()
    {
        var inst = GetModuleHandle(null);
        if (!_registered) { var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticProc, hInstance = inst, lpszClassName = "WinMacDialog", hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512) }; RegisterClassExW(ref cls); _registered = true; }
        GetCursorPos(out var cp); var mon = MonitorFromPoint(cp, 2); var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() }; GetMonitorInfoW(mon, ref mi);
        _s = GetDpiForMonitor(mon, 0, out uint dx, out _) == 0 && dx >= 96 ? dx / 96f : 1f;
        _dev = new D2DDevice(); _dev.SetDpi(_s); _b = _dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _titleF = _dw.CreateTextFormat("Segoe UI Semibold", 15); _bodyF = _dw.CreateTextFormat("Segoe UI", 13); _btnF = _dw.CreateTextFormat("Segoe UI Semibold", 13);
        _btnF.TextAlignment = TextAlignment.Center; _btnF.ParagraphAlignment = ParagraphAlignment.Center;
        using (var l = _dw.CreateTextLayout(_text, _bodyF, _w - 88, 1000)) _h = 24 + 22 + 8 + l.Metrics.Height + 24 + 34 + 20;   // title, text, buttons
        int wp = (int)Math.Ceiling(_w * _s), hp = (int)Math.Ceiling(_h * _s);
        int x = mi.rcWork.L + (mi.rcWork.R - mi.rcWork.L - wp) / 2, y = mi.rcWork.T + (mi.rcWork.B - mi.rcWork.T - hp) / 2 - (int)(40 * _s);
        _hwnd = CreateWindowExW(0x80 /* TOOLWINDOW */ | 0x8 /* TOPMOST */, "WinMacDialog", _title, 0x80000000 /* POPUP */, x, y, wp, hp, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx");
        _current = this;
        int round = 2; DwmSetWindowAttribute(_hwnd, 33, ref round, 4); int dark = 1; DwmSetWindowAttribute(_hwnd, 20, ref dark, 4);
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForHwnd(_dev.D3D, _hwnd, new SwapChainDescription1 { Width = (uint)wp, Height = (uint)hp, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential });
        using (var surface = _swap.GetBuffer<IDXGISurface>(0))
            _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96 * _s, 96 * _s, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
        ShowWindow(_hwnd, 5); SetForegroundWindow(_hwnd); Render();
        while (!_done && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessageW(ref msg); }
        return _result;
    }

    private void Render()
    {
        if (_dev is null || _swap is null) return;
        var c = _dev.Context; c.BeginDraw(); c.Clear(new Color4(0.13f, 0.13f, 0.15f, 1));
        // discreet warning mark
        _b!.Color = new Color4(1f, 0.72f, 0.16f, 1); c.FillEllipse(new Ellipse(new Vector2(36, 36), 12, 12), _b);
        _b.Color = new Color4(0.13f, 0.13f, 0.15f, 1); c.FillRectangle(new RawRectF(34.6f, 29, 37.4f, 38.5f), _b); c.FillEllipse(new Ellipse(new Vector2(36, 42), 1.6f, 1.6f), _b);
        _b.Color = new Color4(1, 1, 1, 0.96f); using (var l = _dw!.CreateTextLayout(_title, _titleF!, _w - 88, 24)) c.DrawTextLayout(new Vector2(60, 24), l, _b);
        _b.Color = new Color4(1, 1, 1, 0.74f); float textH;
        using (var l = _dw.CreateTextLayout(_text, _bodyF!, _w - 88, 1000)) { c.DrawTextLayout(new Vector2(60, 54), l, _b); textH = l.Metrics.Height; }
        // buttons, right-aligned: [Cancelar] [Action]
        float by = _h - 20 - 34, okW = Math.Max(110, Measure(_action) + 32), caW = Math.Max(96, Measure("Cancelar") + 32);
        _ok = new RawRectF(_w - 20 - okW, by, _w - 20, by + 34); _cancel = new RawRectF(_ok.Left - 10 - caW, by, _ok.Left - 10, by + 34);
        if (_onlyOk) _cancel = default; else Button(_cancel, "Cancelar", new Color4(1, 1, 1, _hover == 0 ? 0.22f : 0.13f), _focus == 0);
        var act = _danger ? new Color4(0.86f, 0.22f, 0.20f, _hover == 1 ? 1f : 0.88f) : new Color4(0.04f, 0.52f, 1f, _hover == 1 ? 1f : 0.88f);
        Button(_ok, _action, act, _focus == 1);
        c.EndDraw(); _swap.Present(1, PresentFlags.None);
    }

    private float Measure(string s) { using var l = _dw!.CreateTextLayout(s, _btnF!, 1000, 34); return l.Metrics.Width; }

    private void Button(RawRectF r, string label, Color4 fill, bool focused)
    {
        var c = _dev!.Context; var rr = new RoundedRectangle { Rect = r, RadiusX = 9, RadiusY = 9 };
        _b!.Color = fill; c.FillRoundedRectangle(rr, _b);
        if (focused) { _b.Color = new Color4(1, 1, 1, 0.85f); c.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(r.Left - 3, r.Top - 3, r.Right + 3, r.Bottom + 3), RadiusX = 11, RadiusY = 11 }, _b, 1.6f); }
        _b.Color = new Color4(1, 1, 1, 0.97f); using var l = _dw!.CreateTextLayout(label, _btnF!, r.Right - r.Left, r.Bottom - r.Top); c.DrawTextLayout(new Vector2(r.Left, r.Top), l, _b);
    }

    private int Hit(IntPtr l)
    {
        float x = (short)((long)l & 0xFFFF) / _s, y = (short)(((long)l >> 16) & 0xFFFF) / _s;
        bool In(RawRectF r) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        return In(_cancel) ? 0 : In(_ok) ? 1 : -1;
    }

    private void Finish(bool ok) { _result = ok; _done = true; PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); }

    private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        var me = _current; if (me is null || me._hwnd != h) return DefWindowProcW(h, msg, w, l);
        try
        {
            switch (msg)
            {
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0: me.Finish(false); return IntPtr.Zero;   // another window took over: the safe answer
                case WM_MOUSEMOVE: { int hv = me.Hit(l); if (hv != me._hover) { me._hover = hv; me.Render(); } return IntPtr.Zero; }
                case WM_LBUTTONUP: { int hv = me.Hit(l); if (hv == 1 || (hv == 0 && !me._onlyOk)) me.Finish(hv == 1); return IntPtr.Zero; }
                case WM_KEYDOWN:
                    switch ((int)w)
                    {
                        case 0x1B: me.Finish(false); break;                                            // Esc
                        case 0x0D or 0x20: me.Finish(me._focus == 1); break;                           // Intro / Espacio on the focused button (Cancelar by default)
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
        _bmp?.Dispose(); _swap?.Dispose(); _b?.Dispose(); _titleF?.Dispose(); _bodyF?.Dispose(); _btnF?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
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
