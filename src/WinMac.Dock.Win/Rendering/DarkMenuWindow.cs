using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// WM-017: the Dock's dark context menu window. Same entries and actions as before (<see cref="DarkMenuModel"/>), drawn with Direct2D on a rounded DWM popup at the
/// Dock's DPI, above the pointer. Modal like TrackPopupMenu (its own message loop): closes on a choice, Esc, a click outside (it loses the activation) or when
/// another app takes the focus. Keyboard: ↑ ↓ Intro → ← Esc. No other process is touched.
/// </summary>
internal sealed class DarkMenuWindow
{
    private const int WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MOUSEMOVE = 0x200, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_NULL = 0;
    private static readonly WndProc StaticProc = Proc; private static DarkMenuWindow? _current; private static bool _registered;

    private readonly DarkMenuModel _m; private readonly double _scale; private readonly Action<string> _log;
    private IntPtr _hwnd; private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private ID2D1SolidColorBrush? _b;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _body, _head; private bool _done; private DockAction? _result; private int _wPx, _hPx, _x, _bottom;

    private DarkMenuWindow(IReadOnlyList<MenuEntry> entries, double scale, Action<string> log) { _m = new DarkMenuModel(entries); _scale = scale; _log = log; }

    /// <summary>Shows the menu above (x, y) and returns the chosen action, or null.</summary>
    public static DockAction? Show(IReadOnlyList<MenuEntry> entries, int x, int y, double scale, Action<string> log)
    {
        var w = new DarkMenuWindow(entries, scale, log);
        try { return w.Run(x, y); }
        finally { w.Destroy(); }
    }

    private DockAction? Run(int cx, int cy)
    {
        var inst = GetModuleHandle(null);
        if (!_registered) { var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticProc, hInstance = inst, lpszClassName = "WinMacDockMenu", hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512) }; RegisterClassExW(ref cls); _registered = true; }
        _x = cx; _bottom = cy - (int)(10 * _scale);
        Size(out int x, out int y);
        _hwnd = CreateWindowExW(0x80 /* TOOLWINDOW */ | 0x8 /* TOPMOST */, "WinMacDockMenu", "Menú del Dock", 0x80000000 /* POPUP */, x, y, _wPx, _hPx, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("no se pudo crear el menú");
        _current = this;
        int round = 2; DwmSetWindowAttribute(_hwnd, 33, ref round, 4); int dark = 1; DwmSetWindowAttribute(_hwnd, 20, ref dark, 4);
        _dev = new D2DDevice(); _dev.SetDpi(_scale); _b = _dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _body = _dw.CreateTextFormat("Segoe UI", 13); _head = _dw.CreateTextFormat("Segoe UI Semibold", 12);
        foreach (var f in new[] { _body, _head }) { f.WordWrapping = WordWrapping.NoWrap; f.ParagraphAlignment = ParagraphAlignment.Center; }
        Surface();
        ShowWindow(_hwnd, 5); SetForegroundWindow(_hwnd); Render();
        // If Windows does not give the menu the foreground, it never receives WM_ACTIVATE and a click elsewhere would leave it open (and the Dock waiting):
        // a 50 ms timer also closes it when a mouse button goes down outside it, as any menu does.
        SetTimer(_hwnd, (UIntPtr)1, 50, IntPtr.Zero);
        bool wasDown = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
        while (!_done && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == 0x113 /* WM_TIMER */ && msg.hwnd == _hwnd)
            {
                short l = GetAsyncKeyState(0x01), r = GetAsyncKeyState(0x02);
                bool down = ((l | r) & 0x8000) != 0, pressedSince = ((l | r) & 1) != 0;   // bit 0: pressed since the last call (a very short click)
                if (((down && !wasDown) || (pressedSince && !down)) && GetCursorPos(out var cp) && WindowFromPoint(cp) != _hwnd) _done = true;
                wasDown = down; continue;
            }

            TranslateMessage(ref msg); DispatchMessageW(ref msg);
        }

        KillTimer(_hwnd, (UIntPtr)1);

        return _result;
    }

    /// <summary>Window size for the current rows: centred on the pointer horizontally, its bottom just above it, kept on the monitor.</summary>
    private void Size(out int x, out int y)
    {
        _wPx = (int)Math.Ceiling(_m.Width * _scale); _hPx = (int)Math.Ceiling(_m.Height * _scale);
        var mon = MonitorFromPoint(new POINT { X = _x, Y = _bottom }, 2); var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() }; GetMonitorInfoW(mon, ref mi);
        x = Math.Clamp(_x - _wPx / 2, mi.rcWork.L + 4, Math.Max(mi.rcWork.L + 4, mi.rcMonitor.R - _wPx - 4));
        y = Math.Max(mi.rcMonitor.T + 4, _bottom - _hPx);
    }

    private void Surface()
    {
        _dev!.Context.Target = null; _bmp?.Dispose(); _swap?.Dispose();
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForHwnd(_dev.D3D, _hwnd, new SwapChainDescription1 { Width = (uint)_wPx, Height = (uint)_hPx, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential });
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
    }

    private void Render()
    {
        if (_dev is null || _swap is null) return;
        try
        {
            var c = _dev.Context; c.BeginDraw(); c.Clear(new Color4(0.14f, 0.14f, 0.16f, 1));
            float W = (float)_m.Width, p = (float)DarkMenuModel.Pad; var rows = _m.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                var e = rows[i]; float y = (float)_m.RowY(i), h = (float)DarkMenuModel.HeightOf(e);
                if (e.IsSeparator) { _b!.Color = new Color4(1, 1, 1, 0.1f); c.FillRectangle(new RawRectF(p + 8, y + h / 2, W - p - 8, y + h / 2 + 1), _b); continue; }
                bool back = _m.InSubmenu && i == 0, hot = i == _m.Focus;
                if (hot) { _b!.Color = new Color4(0.04f, 0.52f, 1f, 0.92f); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(p, y + 1, W - p, y + h - 1), RadiusX = 5, RadiusY = 5 }, _b); }
                if (e.IsHeader && !back) { _b!.Color = new Color4(1, 1, 1, 0.5f); Txt(e.Text, _head!, p + 12, y, W - 2 * p - 24, h); continue; }
                _b!.Color = new Color4(1, 1, 1, hot ? 1f : e.Enabled ? 0.92f : 0.35f);
                if (e.Checked) { float cx = p + 12, cy = y + h / 2; c.DrawLine(new Vector2(cx - 4, cy), new Vector2(cx - 1, cy + 3), _b, 1.6f); c.DrawLine(new Vector2(cx - 1, cy + 3), new Vector2(cx + 5, cy - 4), _b, 1.6f); }
                Txt(e.Text, _body!, p + 24, y, W - 2 * p - 44, h);
                if (e.Children is { Count: > 0 }) { float cx = W - p - 14, cy = y + h / 2; c.DrawLine(new Vector2(cx - 2, cy - 4), new Vector2(cx + 2, cy), _b, 1.5f); c.DrawLine(new Vector2(cx + 2, cy), new Vector2(cx - 2, cy + 4), _b, 1.5f); }
            }

            c.EndDraw(); _swap.Present(1, PresentFlags.None);
        }
        catch (Exception e) { _log("Menú del Dock: dibujo: " + e.GetType().Name + ": " + e.Message); }
    }

    private void Txt(string s, IDWriteTextFormat f, float x, float y, float w, float h)
    {
        using var l = _dw!.CreateTextLayout(s, f, Math.Max(1, w), h); using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell);
        _dev!.Context.DrawTextLayout(new Vector2(x, y), l, _b!);
    }

    private void Handle((DarkMenuModel.Outcome Result, DockAction? Action) r)
    {
        switch (r.Result)
        {
            case DarkMenuModel.Outcome.Chosen: _result = r.Action; _done = true; break;
            case DarkMenuModel.Outcome.Closed: _done = true; break;
            case DarkMenuModel.Outcome.Navigated: Size(out int x, out int y); SetWindowPos(_hwnd, IntPtr.Zero, x, y, _wPx, _hPx, 0x14 /* NOZORDER|NOACTIVATE */); Surface(); Render(); break;
            default: Render(); break;
        }

        if (_done) PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);   // wakes the loop so it ends now
    }

    private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        var me = _current; if (me is null || me._hwnd != h) return DefWindowProcW(h, msg, w, l);
        try
        {
            double Y() => (short)(((long)l >> 16) & 0xFFFF) / me._scale;
            switch (msg)
            {
                case WM_ACTIVATE when ((long)w & 0xFFFF) == 0: me._done = true; return IntPtr.Zero;   // click outside / another app: close, like a menu
                case WM_MOUSEMOVE: { int i = me._m.RowAt(Y()); if (i != me._m.Focus) { me._m.Hover(i); me.Render(); } return IntPtr.Zero; }
                case WM_LBUTTONUP or WM_RBUTTONUP: me.Handle(me._m.Activate(me._m.RowAt(Y()))); return IntPtr.Zero;
                case WM_KEYDOWN: me.Handle(me._m.Key((int)w)); return IntPtr.Zero;
                case WM_PAINT: ValidateRect(h, IntPtr.Zero); me.Render(); return IntPtr.Zero;
                case WM_CLOSE: me._done = true; return IntPtr.Zero;
            }
        }
        catch (Exception e) { me._log("Menú del Dock: " + e.Message); me._done = true; }
        return DefWindowProcW(h, msg, w, l);
    }

    private void Destroy()
    {
        if (_dev is not null) _dev.Context.Target = null;
        _bmp?.Dispose(); _swap?.Dispose(); _b?.Dispose(); _body?.Dispose(); _head?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
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
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursorW(IntPtr inst, IntPtr id);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr h, UIntPtr id);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr m, ref MONITORINFO mi);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
}
