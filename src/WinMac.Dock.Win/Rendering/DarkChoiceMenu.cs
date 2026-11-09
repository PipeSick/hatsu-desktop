using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinMac.Dock.Win.Rendering;

/// <summary>Una opción del desplegable: su Id (lo que se devuelve), el texto COMPLETO y si es la elegida ahora.</summary>
public sealed record ChoiceMenuItem(string Id, string Text, bool Checked);

/// <summary>
/// WM-035 (iteración 6): desplegable de selección propio (dispositivos de audio, salida por aplicación) en lugar del menú nativo blanco de Windows. Respeta el tema oscuro (o
/// el claro, si el panel lo es), se coloca contra el control que lo abrió con <see cref="ChoiceMenuLayout"/> (pura y probada), es tan ancho como hace falta para ver los nombres
/// completos, nunca sale del área de trabajo de su monitor y se desplaza si hay muchas opciones. Modal (bucle de mensajes propio, como TrackPopupMenu): se cierra al elegir, con Esc,
/// al pulsar fuera o al perder el foco. Teclado: ↑ ↓ Inicio Fin Intro Esc; rueda del ratón. No toca ningún otro proceso.
/// </summary>
public static class DarkChoiceMenu
{
    /// <summary>Muestra el menú junto a <paramref name="anchorPx"/> (píxeles de pantalla) y devuelve el Id elegido, o null.</summary>
    public static string? Show(IReadOnlyList<ChoiceMenuItem> items, PxRect anchorPx, double scale, bool dark, Action<string> log)
    {
        if (items.Count == 0) return null;
        var w = new Window(items, anchorPx, scale, dark, log);
        try { return w.Run(); }
        catch (Exception e) { log("Desplegable: " + e.GetType().Name + ": " + e.Message); return null; }
        finally { w.Destroy(); }
    }

    private sealed class Window
    {
        private const int WM_ACTIVATE = 0x6, WM_KEYDOWN = 0x100, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_MOUSEMOVE = 0x200, WM_MOUSEWHEEL = 0x20A, WM_PAINT = 0xF, WM_CLOSE = 0x10, WM_NULL = 0;
        private static readonly WndProc StaticProc = Proc; private static Window? _current; private static bool _registered;

        private readonly IReadOnlyList<ChoiceMenuItem> _items; private readonly PxRect _anchor; private readonly double _scale; private readonly bool _dark; private readonly Action<string> _log;
        private IntPtr _hwnd; private D2DDevice? _dev; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private ID2D1SolidColorBrush? _b;
        private IDWriteFactory? _dw; private IDWriteTextFormat? _body; private bool _done; private string? _result;
        private ChoiceMenuPlacement _place; private int _first, _hover = -1; private int _wPx, _hPx;

        public Window(IReadOnlyList<ChoiceMenuItem> items, PxRect anchor, double scale, bool dark, Action<string> log) { _items = items; _anchor = anchor; _scale = scale; _dark = dark; _log = log; }

        public string? Run()
        {
            var inst = GetModuleHandle(null);
            if (!_registered) { var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticProc, hInstance = inst, lpszClassName = "WinMacChoiceMenu", hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)32512) }; RegisterClassExW(ref cls); _registered = true; }

            _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _body = _dw.CreateTextFormat("Segoe UI", 13.5f); _body.WordWrapping = WordWrapping.NoWrap; _body.ParagraphAlignment = ParagraphAlignment.Center;
            var widths = _items.Select(i => { using var l = _dw.CreateTextLayout(i.Text, _body, 10000, 40); return (double)l.Metrics.Width; }).ToList();   // anchos reales de los nombres (DIP)
            var pt = new POINT { X = (_anchor.Left + _anchor.Right) / 2, Y = (_anchor.Top + _anchor.Bottom) / 2 };
            var mon = MonitorFromPoint(pt, 2); var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() }; GetMonitorInfoW(mon, ref mi);
            _place = ChoiceMenuLayout.Compute(widths, _anchor, new PxRect(mi.rcWork.L, mi.rcWork.T, mi.rcWork.R, mi.rcWork.B), _scale);
            _wPx = _place.Rect.Width; _hPx = _place.Rect.Height;
            int cur = _items.ToList().FindIndex(i => i.Checked); _hover = cur; if (cur >= _place.VisibleRows) _first = Math.Min(cur, _items.Count - _place.VisibleRows);

            _hwnd = CreateWindowExW(0x80 /* TOOLWINDOW */ | 0x8 /* TOPMOST */, "WinMacChoiceMenu", "Opciones", 0x80000000 /* POPUP */, _place.Rect.Left, _place.Rect.Top, _wPx, _hPx, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("no se pudo crear el desplegable");
            _current = this;
            int round = 2; DwmSetWindowAttribute(_hwnd, 33, ref round, 4); int dk = _dark ? 1 : 0; DwmSetWindowAttribute(_hwnd, 20, ref dk, 4);
            _dev = new D2DDevice(); _dev.SetDpi(_scale); _b = _dev.Context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            Surface(); ShowWindow(_hwnd, 5); SetForegroundWindow(_hwnd); Render();

            // Si Windows no da el primer plano, no llega WM_ACTIVATE y un clic fuera lo dejaría abierto: un temporizador también lo cierra, como cualquier menú.
            SetTimer(_hwnd, (UIntPtr)1, 50, IntPtr.Zero);
            bool wasDown = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
            while (!_done && GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == 0x113 /* WM_TIMER */ && msg.hwnd == _hwnd)
                {
                    short l = GetAsyncKeyState(0x01), r = GetAsyncKeyState(0x02);
                    bool down = ((l | r) & 0x8000) != 0, pressedSince = ((l | r) & 1) != 0;
                    if (((down && !wasDown) || (pressedSince && !down)) && GetCursorPos(out var cp) && WindowFromPoint(cp) != _hwnd) _done = true;
                    wasDown = down; continue;
                }

                TranslateMessage(ref msg); DispatchMessageW(ref msg);
            }

            KillTimer(_hwnd, (UIntPtr)1);
            return _result;
        }

        private int RowAt(int yPx) { int i = (yPx - _place.PadPx) / Math.Max(1, _place.RowHeightPx); return yPx < _place.PadPx || i < 0 || i >= _place.VisibleRows ? -1 : _first + i; }

        private void Render()
        {
            if (_dev is null || _swap is null) return;
            try
            {
                var c = _dev.Context; c.BeginDraw();
                var bgc = _dark ? ChoiceMenuPalette.DarkBackground : ChoiceMenuPalette.LightBackground; var fgc = _dark ? ChoiceMenuPalette.DarkText : ChoiceMenuPalette.LightText;
                var bg = new Color4((float)bgc.R, (float)bgc.G, (float)bgc.B, 1); var fg = new Color4((float)fgc.R, (float)fgc.G, (float)fgc.B, _dark ? (float)ChoiceMenuPalette.DarkTextAlpha : 1f);
                c.Clear(bg); float W = (float)(_wPx / _scale), rowH = (float)(_place.RowHeightPx / _scale), pad = (float)(_place.PadPx / _scale);
                for (int k = 0; k < _place.VisibleRows && _first + k < _items.Count; k++)
                {
                    int i = _first + k; var it = _items[i]; float y = pad + k * rowH; bool hot = i == _hover;
                    if (hot) { _b!.Color = new Color4((float)ChoiceMenuPalette.Accent.R, (float)ChoiceMenuPalette.Accent.G, (float)ChoiceMenuPalette.Accent.B, 1f); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(6, y + 1, W - 6, y + rowH - 1), RadiusX = 6, RadiusY = 6 }, _b); }
                    _b!.Color = hot ? new Color4(1, 1, 1, 1) : fg;
                    if (it.Checked) { float cx = 17, cy = y + rowH / 2; c.DrawLine(new Vector2(cx - 4, cy), new Vector2(cx - 1, cy + 3.5f), _b, 1.7f); c.DrawLine(new Vector2(cx - 1, cy + 3.5f), new Vector2(cx + 5, cy - 4), _b, 1.7f); }
                    using var l = _dw!.CreateTextLayout(it.Text, _body!, Math.Max(1, W - (float)ChoiceMenuLayout.CheckColumnDip - 10 - 8), rowH);
                    using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell);
                    c.DrawTextLayout(new Vector2((float)ChoiceMenuLayout.CheckColumnDip + 4, y), l, _b);
                }

                if (_place.VisibleRows < _items.Count)
                {   // hay más opciones de las que caben: una barra fina a la derecha
                    float track = _place.VisibleRows * rowH, th = Math.Max(20, track * _place.VisibleRows / _items.Count), ty = pad + (track - th) * _first / Math.Max(1, _items.Count - _place.VisibleRows);
                    _b!.Color = _dark ? new Color4(1, 1, 1, 0.28f) : new Color4(0, 0, 0, 0.28f); c.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(W - 6, ty, W - 3, ty + th), RadiusX = 1.5f, RadiusY = 1.5f }, _b);
                }

                c.EndDraw(); _swap.Present(1, PresentFlags.None);
            }
            catch (Exception e) { _log("Desplegable: dibujo: " + e.GetType().Name + ": " + e.Message); }
        }

        private void Move(int to) { to = Math.Clamp(to, 0, _items.Count - 1); _hover = to; if (to < _first) _first = to; else if (to >= _first + _place.VisibleRows) _first = to - _place.VisibleRows + 1; Render(); }
        private void Choose(int i) { if (i < 0 || i >= _items.Count) return; _result = _items[i].Id; _done = true; PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); }

        private void Surface()
        {
            _dev!.Context.Target = null; _bmp?.Dispose(); _swap?.Dispose();
            using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
                _swap = factory.CreateSwapChainForHwnd(_dev.D3D, _hwnd, new SwapChainDescription1 { Width = (uint)_wPx, Height = (uint)_hPx, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential });
            using var surface = _swap.GetBuffer<IDXGISurface>(0);
            _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
            _dev.Context.Target = _bmp;
        }

        private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            var me = _current; if (me is null || me._hwnd != h) return DefWindowProcW(h, msg, w, l);
            try
            {
                int Y() => (short)(((long)l >> 16) & 0xFFFF);
                switch (msg)
                {
                    case WM_ACTIVATE when ((long)w & 0xFFFF) == 0: me._done = true; return IntPtr.Zero;
                    case WM_MOUSEMOVE: { int i = me.RowAt(Y()); if (i != me._hover) { me._hover = i; me.Render(); } return IntPtr.Zero; }
                    case WM_LBUTTONUP or WM_RBUTTONUP: me.Choose(me.RowAt(Y())); return IntPtr.Zero;
                    case WM_MOUSEWHEEL: { int d = (short)(((long)w >> 16) & 0xFFFF); int max = Math.Max(0, me._items.Count - me._place.VisibleRows); me._first = Math.Clamp(me._first + (d > 0 ? -1 : 1), 0, max); me.Render(); return IntPtr.Zero; }
                    case WM_KEYDOWN:
                        switch ((int)w)
                        {
                            case 0x1B: me._done = true; PostMessageW(h, WM_NULL, IntPtr.Zero, IntPtr.Zero); break;
                            case 0x26: me.Move((me._hover < 0 ? me._items.Count : me._hover) - 1); break;
                            case 0x28: me.Move(me._hover + 1); break;
                            case 0x24: me.Move(0); break;
                            case 0x23: me.Move(me._items.Count - 1); break;
                            case 0x0D or 0x20: me.Choose(me._hover); break;
                        }

                        return IntPtr.Zero;
                    case WM_PAINT: ValidateRect(h, IntPtr.Zero); me.Render(); return IntPtr.Zero;
                    case WM_CLOSE: me._done = true; return IntPtr.Zero;
                }
            }
            catch (Exception e) { me._log("Desplegable: " + e.Message); me._done = true; }
            return DefWindowProcW(h, msg, w, l);
        }

        public void Destroy()
        {
            if (_dev is not null) _dev.Context.Target = null;
            _bmp?.Dispose(); _swap?.Dispose(); _b?.Dispose(); _body?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
            if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
            if (ReferenceEquals(_current, this)) _current = null;
        }

        private delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASSEX { public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
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
}
