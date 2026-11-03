using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Interop;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// Quick Look of a capture just saved (section 11). Two windows of the bar process:
/// <list type="bullet">
/// <item>the floating THUMBNAIL (layered GDI, never activated, excluded from capture): bottom-right above the Dock for 5 s, then it fades; the pointer on it keeps it;
/// a click opens the preview. It never opens the big view by itself.</item>
/// <item>the PREVIEW (Direct2D, like the editor): the image centred with Copiar, Editar, Anotar, Recortar, Compartir, Guardar como, Abrir, Eliminar.
/// Esc/Space close, Ctrl+C copies, Enter opens the editor. Also excluded from capture so a later capture never shows it.</item>
/// </list>
/// Every action is real and reuses what exists (the editor, the clipboard copy, Windows' share sheet, the Recycle Bin); nothing is decorative.
/// </summary>
internal sealed class QuickLookWindow : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
    private const int WM_PAINT = 0xF, WM_LBUTTONUP = 0x202, WM_MOUSEMOVE = 0x200, WM_MOUSELEAVE = 0x2A3, WM_KEYDOWN = 0x100, WM_CLOSE = 0x10, WM_RBUTTONUP = 0x205;

    private readonly string _path; private readonly byte[] _px; private readonly int _w, _h; private readonly CaptureMonitor _mon;
    private readonly Action<string, QuickLookWindow> _act; private readonly Action<string> _log; private readonly Action _gone;
    private readonly BarNative.WndProc _thumbProc, _prevProc;
    private IntPtr _thumb, _prev; private CaptureRect _thumbRect; private byte[]? _thumbPx; private bool _hover, _disposed;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // preview (Direct2D)
    private D2DDevice? _dev; private ID2D1DeviceContext? _ctx; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _target; private ID2D1Bitmap? _image;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _ui, _titleFmt; private ID2D1SolidColorBrush? _b; private float _s = 1;
    private CaptureRect _imgRect; private readonly List<(string Id, RawRectF Rect)> _buttons = []; private string? _hoverId; private string? _status;

    public QuickLookWindow(string path, byte[] px, int w, int h, CaptureMonitor mon, Action<string, QuickLookWindow> act, Action gone, Action<string> log)
    {
        _path = path; _px = px; _w = w; _h = h; _mon = mon; _act = act; _gone = gone; _log = log; _thumbProc = ThumbProc; _prevProc = PrevProc;
    }

    public string Path => _path;
    public byte[] Pixels => _px;
    public (int W, int H) Size => (_w, _h);
    public bool PreviewOpen => _prev != IntPtr.Zero;
    public CaptureRect ThumbBounds => _thumbRect;
    public IntPtr PreviewHandle => _prev;
    /// <summary>Toolbar buttons of the preview in client px (for the CI to click like a user).</summary>
    public IReadOnlyList<(string Id, RawRectF Rect)> Buttons => _buttons;

    // ───────────────────────────── thumbnail
    public void ShowThumbnail(CaptureRect? dock)
    {
        _thumbRect = QuickLook.ThumbRect(_mon, dock, _w, _h);
        _thumb = InstanceWindows.Create("WinMacQuickLookThumb", 32649 /* IDC_HAND */, _thumbProc, WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED, "Captura guardada", WS_POPUP, _thumbRect.X, _thumbRect.Y, _thumbRect.W, _thumbRect.H);
        if (_thumb == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la miniatura");
        ExcludeFromCapture(_thumb);
        _thumbPx = ThumbPixels(_thumbRect.W, _thumbRect.H);
        UpdateThumb(1);
        BarNative.ShowWindow(_thumb, 4 /* SW_SHOWNOACTIVATE */);
    }

    /// <summary>The scaled image with rounded corners and a hairline border, premultiplied for UpdateLayeredWindow.</summary>
    private byte[] ThumbPixels(int tw, int th)
    {
        var o = QuickLook.Scale(_px, _w, _h, tw, th); float r = 10 * (float)_mon.Scale;
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                int i = (y * tw + x) * 4;
                float dx = Math.Max(0, Math.Max(r - x - 0.5f, x + 0.5f - (tw - r))), dy = Math.Max(0, Math.Max(r - y - 0.5f, y + 0.5f - (th - r)));
                float d = MathF.Sqrt(dx * dx + dy * dy), cover = Math.Clamp(r - d + 0.5f, 0, 1);   // antialiased rounded corner
                bool edge = x == 0 || y == 0 || x == tw - 1 || y == th - 1 || (d > r - 1.5f && d <= r + 0.5f && (dx > 0 && dy > 0));
                if (edge) { o[i] = (byte)(o[i] * 0.55f + 115); o[i + 1] = (byte)(o[i + 1] * 0.55f + 115); o[i + 2] = (byte)(o[i + 2] * 0.55f + 115); }
                o[i] = (byte)(o[i] * cover); o[i + 1] = (byte)(o[i + 1] * cover); o[i + 2] = (byte)(o[i + 2] * cover); o[i + 3] = (byte)(255 * cover);
            }

        return o;
    }

    private void UpdateThumb(double alpha)
    {
        if (_thumb == IntPtr.Zero || _thumbPx is null) return;
        int w = _thumbRect.W, h = _thumbRect.H;
        var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var screen = GetDC(IntPtr.Zero); var mem = CreateCompatibleDC(screen);
        var dib = CreateDIBSection(screen, ref bi, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            Marshal.Copy(_thumbPx, 0, bits, w * h * 4);
            var old = SelectObject(mem, dib);
            var pt = new BarNative.POINT { X = _thumbRect.X, Y = _thumbRect.Y }; var size = new SIZE { cx = w, cy = h }; var src = new BarNative.POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = (byte)Math.Clamp(alpha * 255, 0, 255), AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(_thumb, screen, ref pt, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
            SelectObject(mem, old);
        }
        finally { DeleteObject(dib); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>Called from the bar's loop: fades and finally removes the thumbnail (unless the preview is open).</summary>
    public void Tick()
    {
        if (_disposed || _thumb == IntPtr.Zero) return;
        double t = _clock.Elapsed.TotalSeconds;
        if (QuickLook.Expired(t, _hover)) { CloseThumb(); if (_prev == IntPtr.Zero) Dispose(); return; }
        double a = QuickLook.Alpha(t, _hover); if (a < 1) UpdateThumb(a);
    }

    /// <summary>A new capture is starting: the thumbnail is excluded from capture anyway, and it goes away so it never covers what is being captured.</summary>
    public void HideForCapture() { CloseThumb(); if (_prev == IntPtr.Zero) Dispose(); }

    private void CloseThumb() { if (_thumb != IntPtr.Zero) { BarNative.DestroyWindow(_thumb); InstanceWindows.Forget(_thumb); _thumb = IntPtr.Zero; } }

    private IntPtr ThumbProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch ((int)msg)
            {
                case WM_MOUSEMOVE:
                    if (!_hover) { _hover = true; UpdateThumb(1); var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 2 /* TME_LEAVE */, hwndTrack = h }; TrackMouseEvent(ref tme); }
                    return IntPtr.Zero;
                case WM_MOUSELEAVE: _hover = false; _clock.Restart(); return IntPtr.Zero;   // a few more seconds after the pointer leaves
                case WM_LBUTTONUP: OpenPreview(); return IntPtr.Zero;
                case WM_RBUTTONUP: CloseThumb(); if (_prev == IntPtr.Zero) Dispose(); return IntPtr.Zero;   // right click dismisses it
            }
        }
        catch (Exception e) { _log("[quicklook] miniatura: " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    // ───────────────────────────── preview
    public void OpenPreview()
    {
        if (_prev != IntPtr.Zero) { BarNative.SetForegroundWindow(_prev); return; }
        CloseThumb();
        var (win, img) = QuickLook.PreviewRect(_mon, _w, _h); _imgRect = img; _s = (float)_mon.Scale;
        _prev = InstanceWindows.Create("WinMacQuickLook", 32512, _prevProc, WS_EX_TOOLWINDOW, "Vista previa — " + System.IO.Path.GetFileName(_path), WS_POPUP, win.X, win.Y, win.W, win.H);
        if (_prev == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la vista previa");
        ExcludeFromCapture(_prev);
        int round = 2 /* DWMWCP_ROUND */; DwmSetWindowAttribute(_prev, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
        int dark = 1; DwmSetWindowAttribute(_prev, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4);

        _dev = new D2DDevice(); _ctx = _dev.Context; _dev.SetDpi(1.0);
        _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _ui = _dw.CreateTextFormat("Segoe UI", 13 * _s); _ui.WordWrapping = WordWrapping.NoWrap;
        _titleFmt = _dw.CreateTextFormat("Segoe UI Semibold", 13.5f * _s); _titleFmt.WordWrapping = WordWrapping.NoWrap;
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForHwnd(_dev.D3D, _prev, new SwapChainDescription1 { Width = (uint)win.W, Height = (uint)win.H, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipDiscard });
        using (var surface = _swap.GetBuffer<IDXGISurface>(0))
            _target = _ctx.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _ctx.Target = _target;
        unsafe { fixed (byte* p = _px) _image = _ctx.CreateBitmap(new SizeI(_w, _h), (IntPtr)p, (uint)(_w * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96)); }
        BarNative.ShowWindow(_prev, 5); BarNative.SetForegroundWindow(_prev);
        Render();
        _log($"[quicklook] vista previa abierta ({_w}x{_h})");
        PreviewOpened?.Invoke();
    }

    public Action? PreviewOpened { get; set; }

    public void SetStatus(string? s) { _status = s; Render(); }

    private void Render()
    {
        if (_ctx is null || _swap is null || _disposed || _prev == IntPtr.Zero) return;
        try
        {
            BarNative.GetClientRect(_prev, out var rc); float W = rc.Right, H = rc.Bottom;
            _ctx.BeginDraw(); _ctx.Clear(new Color4(0.11f, 0.11f, 0.12f, 1));
            // title: file name and size; close button on the right
            _b!.Color = new Color4(1, 1, 1, 0.92f);
            using (var l = _dw!.CreateTextLayout(System.IO.Path.GetFileName(_path) + $"  ·  {_w} × {_h}", _titleFmt!, W - 90 * _s, 40 * _s)) { l.ParagraphAlignment = ParagraphAlignment.Center; _ctx.DrawTextLayout(new Vector2(18 * _s, 0), l, _b); }
            _buttons.Clear();
            var close = new RawRectF(W - 40 * _s, 8 * _s, W - 12 * _s, 34 * _s); _buttons.Add(("close", close));
            _b.Color = new Color4(1, 1, 1, _hoverId == "close" ? 0.18f : 0.08f); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = close, RadiusX = 7 * _s, RadiusY = 7 * _s }, _b);
            _b.Color = new Color4(1, 1, 1, 0.85f); float cx = (close.Left + close.Right) / 2, cy = (close.Top + close.Bottom) / 2, k = 5 * _s;
            _ctx.DrawLine(new Vector2(cx - k, cy - k), new Vector2(cx + k, cy + k), _b, 1.6f * _s); _ctx.DrawLine(new Vector2(cx - k, cy + k), new Vector2(cx + k, cy - k), _b, 1.6f * _s);
            // image
            _ctx.DrawBitmap(_image!, new RawRectF(_imgRect.X, _imgRect.Y, _imgRect.X + _imgRect.W, _imgRect.Y + _imgRect.H), 1, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null);
            // toolbar, centred
            float bh = 32 * _s, gap = 8 * _s, y = H - 46 * _s; var widths = new List<float>();
            foreach (var (_, label) in QuickLook.Actions) { using var m = _dw.CreateTextLayout(label, _ui!, 400, bh); widths.Add(m.Metrics.Width + 26 * _s); }
            float total = widths.Sum() + gap * (widths.Count - 1), x = Math.Max(12 * _s, (W - total) / 2);
            for (int i = 0; i < QuickLook.Actions.Length; i++)
            {
                var (id, label) = QuickLook.Actions[i]; var r = new RawRectF(x, y, x + widths[i], y + bh); _buttons.Add((id, r));
                bool danger = id == "delete";
                _b.Color = danger ? new Color4(1f, 0.27f, 0.23f, _hoverId == id ? 0.95f : 0.75f) : new Color4(1, 1, 1, _hoverId == id ? 0.22f : 0.11f);
                _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = r, RadiusX = 8 * _s, RadiusY = 8 * _s }, _b);
                _b.Color = new Color4(1, 1, 1, 0.95f);
                using var l = _dw.CreateTextLayout(label, _ui!, widths[i], bh); l.TextAlignment = TextAlignment.Center; l.ParagraphAlignment = ParagraphAlignment.Center; _ctx.DrawTextLayout(new Vector2(x, y), l, _b);
                x += widths[i] + gap;
            }

            if (_status is { } st)
            {
                _b.Color = new Color4(1, 1, 1, 0.6f);
                using var l = _dw.CreateTextLayout(st, _ui!, W - 36 * _s, 20 * _s); l.TextAlignment = TextAlignment.Center; _ctx.DrawTextLayout(new Vector2(18 * _s, y - 24 * _s), l, _b);
            }

            _ctx.EndDraw(); _swap.Present(1, PresentFlags.None);
        }
        catch (Exception e) { _log("[quicklook] dibujo: " + e.GetType().Name + ": " + e.Message); }
    }

    private string? Hit(IntPtr l) { float x = (short)((long)l & 0xFFFF), y = (short)(((long)l >> 16) & 0xFFFF); return _buttons.FirstOrDefault(b => x >= b.Rect.Left && x < b.Rect.Right && y >= b.Rect.Top && y < b.Rect.Bottom).Id; }

    private IntPtr PrevProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch ((int)msg)
            {
                case WM_PAINT: { var ps = new PAINTSTRUCT(); BeginPaint(h, ref ps); EndPaint(h, ref ps); Render(); return IntPtr.Zero; }
                case WM_MOUSEMOVE: { var id = Hit(l); if (id != _hoverId) { _hoverId = id; Render(); } return IntPtr.Zero; }
                case WM_LBUTTONUP: { if (Hit(l) is { } id) Do(id); return IntPtr.Zero; }
                case WM_KEYDOWN: { if (QuickLook.KeyAction((int)w, (GetKeyState(0x11) & 0x8000) != 0) is { } id) Do(id); return IntPtr.Zero; }
                case WM_CLOSE: Dispose(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[quicklook] vista previa: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private void Do(string id)
    {
        if (id == "close") { Dispose(); return; }
        _log("[quicklook] acción " + id);
        _act(id, this);
    }

    /// <summary>SOLO CI: la auditoría fotografía la pantalla con GDI, que respeta la exclusión; con esto se ve la miniatura en sus capturas (el registro lo dice).</summary>
    public bool ShowInCaptures { get; init; }

    private void ExcludeFromCapture(IntPtr h)
    {
        if (ShowInCaptures) { _log("[quicklook] excluida de capturas: no (solo CI, para fotografiarla)"); return; }
        _log("[quicklook] excluida de capturas: " + (SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */) ? "sí" : "no (Windows anterior a 10 2004)"));
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        CloseThumb();
        if (_prev != IntPtr.Zero) { BarNative.DestroyWindow(_prev); InstanceWindows.Forget(_prev); _prev = IntPtr.Zero; }
        _image?.Dispose(); _target?.Dispose(); _swap?.Dispose(); _b?.Dispose(); _ui?.Dispose(); _titleFmt?.Dispose(); _dw?.Dispose(); _dev?.Dispose();
        _gone();
    }

    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
    [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public BarNative.RECT rcPaint; public int fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref BarNative.POINT pt, ref SIZE size, IntPtr src, ref BarNative.POINT srcPt, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
}
