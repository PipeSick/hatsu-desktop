using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Interop;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// The capture overlay (docs/26): covers the whole virtual screen, shows the FROZEN frame slightly dimmed (so our own interface is never captured and
/// what you select is exactly what you saw), and lets the user pick a region (drag, with its size in pixels), a window (the one under the cursor is
/// outlined), a monitor or all monitors from a toolbar like the mockup's. Esc or right click cancels; nothing is saved without a click/Enter.
/// </summary>
internal sealed class CaptureOverlay : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_KEYDOWN = 0x100, WM_SETCURSOR = 0x20;

    private readonly BarNative.WndProc _proc; private readonly Action<string> _log;
    private readonly Action<CaptureRect, CaptureMode, string?>? _done; private readonly Action _cancel;
    private readonly byte[] _frame; private readonly CaptureRect _screen;
    private readonly IReadOnlyList<CaptureMonitor> _monitors; private readonly IReadOnlyList<CaptureWindow> _windows;
    private IntPtr _hwnd; private D2DDevice? _dev; private ID2D1DeviceContext? _ctx; private ID2D1Bitmap? _frozen;
    private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _target; private IDCompositionDevice? _dcomp; private IDCompositionTarget? _dtarget; private IDCompositionVisual? _visual;
    private IDWriteFactory? _dw; private IDWriteTextFormat? _label; private ID2D1SolidColorBrush? _b;
    private CaptureMode _mode; private int _px, _py; private (int X, int Y)? _dragFrom; private bool _closed;

    public CaptureOverlay(byte[] frame, CaptureRect screen, IReadOnlyList<CaptureMonitor> monitors, IReadOnlyList<CaptureWindow> windows, CaptureMode mode,
        Action<CaptureRect, CaptureMode, string?> done, Action cancel, Action<string> log, bool record = false)
    {
        _record = record;
        _frame = frame; _screen = screen; _monitors = monitors; _windows = windows; _mode = mode; _done = done; _cancel = cancel; _log = log; _proc = WndProcImpl;
    }

    private readonly bool _record;
    public CaptureMode Mode => _mode;
    /// <summary>Toolbar buttons on screen (px), for the CI to click like a user.</summary>
    public IReadOnlyList<(CaptureMode Mode, CaptureRect Rect)> ToolbarButtons { get; private set; } = [];

    public void Show()
    {
        _hwnd = InstanceWindows.Create("WinMacCapture", 32515 /* IDC_CROSS */, _proc, WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "Hatsu Desktop captura", WS_POPUP, _screen.X, _screen.Y, _screen.W, _screen.H);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear la ventana de captura: " + Marshal.GetLastWin32Error());
        _dev = new D2DDevice(); _ctx = _dev.Context; _dev.SetDpi(1.0);   // 1 DIP = 1 physical px: the virtual screen can mix DPIs
        _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>(); _label = _dw.CreateTextFormat("Segoe UI Semibold", (float)(13 * PrimaryScale())); _label.WordWrapping = WordWrapping.NoWrap;
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false))
            _swap = factory.CreateSwapChainForComposition(_dev.D3D, new SwapChainDescription1 { Width = (uint)_screen.W, Height = (uint)_screen.H, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch });
        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(_dev.Dxgi); _dcomp.CreateTargetForHwnd(_hwnd, true, out _dtarget!).CheckError();
        _visual = _dcomp.CreateVisual(); _visual.SetContent(_swap); _dtarget.SetRoot(_visual); _dcomp.Commit();
        using (var surface = _swap.GetBuffer<IDXGISurface>(0))
            _target = _ctx.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _ctx.Target = _target;
        unsafe { fixed (byte* p = _frame) _frozen = _ctx.CreateBitmap(new SizeI(_screen.W, _screen.H), (IntPtr)p, (uint)(_screen.W * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96)); }
        BarNative.GetCursorPos(out var c); _px = c.X; _py = c.Y;
        BarNative.SetWindowPos(_hwnd, new IntPtr(-1), _screen.X, _screen.Y, _screen.W, _screen.H, 0x0040);
        BarNative.SetForegroundWindow(_hwnd);
        Render();
    }

    private double PrimaryScale() => _monitors.FirstOrDefault(m => m.Primary)?.Scale ?? 1.0;

    /// <summary>What a click/Enter would capture now.</summary>
    private CaptureRect Target() => CaptureGeometry.Target(_mode, _monitors, _windows, _px, _py, _dragFrom is { } f ? CaptureRect.FromPoints(f.X, f.Y, _px, _py) : null);

    private static RawRectF R(CaptureRect r, CaptureRect origin) => new(r.X - origin.X, r.Y - origin.Y, r.Right - origin.X, r.Bottom - origin.Y);
    private void Brush(float r, float g, float b, float a) => _b!.Color = new Color4(r, g, b, a);

    private void Render()
    {
        if (_ctx is null || _swap is null || _closed) return;
        try
        {
            var t = Target(); var all = new CaptureRect(0, 0, _screen.W, _screen.H);
            _ctx.BeginDraw(); _ctx.Clear(new Color4(0, 0, 0, 1));
            _ctx.DrawBitmap(_frozen!, R(_screen, _screen), 1, Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor, null);
            Brush(0, 0, 0, 0.38f); _ctx.FillRectangle(R(_screen, _screen), _b!);   // slight dim
            if (!t.IsEmpty)
            {
                var tr = R(t, _screen);
                _ctx.DrawBitmap(_frozen!, tr, 1, Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor, tr);   // the selection at full brightness
                if (_mode == CaptureMode.Window) { Brush(0.04f, 0.52f, 1f, 0.95f); _ctx.DrawRectangle(tr, _b!, 3); }
                else
                {
                    Brush(1, 1, 1, 0.95f); var dash = _dev!.Factory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = DashStyle.Dash });
                    _ctx.DrawRectangle(tr, _b!, 1.5f, dash); dash.Dispose();
                    if (_mode == CaptureMode.Region) foreach (var (hx, hy) in new[] { (tr.Left, tr.Top), (tr.Right, tr.Top), (tr.Left, tr.Bottom), (tr.Right, tr.Bottom) }) { Brush(1, 1, 1, 1); _ctx.FillEllipse(new Ellipse(new Vector2(hx, hy), 5, 5), _b!); }
                }

                string text = _mode == CaptureMode.Window ? (CaptureGeometry.WindowAt(_windows, _px, _py, _screen)?.Title ?? "") + "  ·  " + CaptureGeometry.Dimensions(t) : CaptureGeometry.Dimensions(t);
                Pill(text, tr.Left + 6, tr.Bottom + 8 + 30 > _screen.H ? tr.Top - 38 : tr.Bottom + 8);
            }

            DrawToolbar();
            _ctx.EndDraw(); _dcomp!.Commit(); _swap.Present(1, PresentFlags.None);
        }
        catch (Exception e) { _log("[captura] dibujo: " + e.GetType().Name + ": " + e.Message); }
    }

    private void Pill(string text, float x, float y)
    {
        using var l = _dw!.CreateTextLayout(text, _label!, 2000, 40);
        float w = l.Metrics.Width + 20, h = l.Metrics.Height + 10;
        Brush(0.1f, 0.1f, 0.12f, 0.88f); _ctx!.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x, y, x + w, y + h), RadiusX = h / 2, RadiusY = h / 2 }, _b!);
        Brush(1, 1, 1, 1); _ctx.DrawTextLayout(new Vector2(x + 10, y + 5), l, _b!);
    }

    /// <summary>Toolbar like the mockup: monitor · all monitors · window · region · the mode's name · Esc. At the bottom of the monitor under the cursor.</summary>
    private void DrawToolbar()
    {
        var mon = CaptureGeometry.MonitorAt(_monitors, _px, _py) ?? new CaptureMonitor(_screen, 1, true);
        float s = (float)mon.Scale, bw = 46 * s, h = 56 * s, pad = 8 * s, textW = 170 * s, w = pad + 4 * (bw + 6 * s) + textW + 50 * s;
        float x = mon.Bounds.X - _screen.X + (mon.Bounds.W - w) / 2, y = mon.Bounds.Bottom - _screen.Y - h - 70 * s;
        Brush(0.11f, 0.11f, 0.13f, 0.92f); _ctx!.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x, y, x + w, y + h), RadiusX = 14 * s, RadiusY = 14 * s }, _b!);
        Brush(1, 1, 1, 0.12f); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x, y, x + w, y + h), RadiusX = 14 * s, RadiusY = 14 * s }, _b!, 1);
        var buttons = new List<(CaptureMode, CaptureRect)>();
        var modes = new[] { CaptureMode.Monitor, CaptureMode.AllMonitors, CaptureMode.Window, CaptureMode.Region };
        for (int i = 0; i < modes.Length; i++)
        {
            float bx = x + pad + i * (bw + 6 * s), by = y + (h - bw) / 2;
            if (modes[i] == _mode) { Brush(0.04f, 0.52f, 1f, 1); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(bx, by, bx + bw, by + bw), RadiusX = 10 * s, RadiusY = 10 * s }, _b!); }
            ModeIcon(modes[i], bx + bw / 2, by + bw / 2, s);
            buttons.Add((modes[i], new CaptureRect((int)(bx + _screen.X), (int)(by + _screen.Y), (int)bw, (int)bw)));
        }

        string name = _record
            ? _mode switch { CaptureMode.Monitor => "Grabar pantalla", CaptureMode.AllMonitors => "Grabar todas", CaptureMode.Window => "Grabar ventana", _ => "Grabar región" }
            : _mode switch { CaptureMode.Monitor => "Capturar pantalla", CaptureMode.AllMonitors => "Todas las pantallas", CaptureMode.Window => "Capturar ventana", _ => "Capturar región" };
        using (var l = _dw!.CreateTextLayout(name, _label!, textW, h)) { Brush(1, 1, 1, 0.95f); _ctx.DrawTextLayout(new Vector2(x + pad + 4 * (bw + 6 * s) + 10 * s, y + (h - l.Metrics.Height) / 2), l, _b!); }
        using (var l = _dw.CreateTextLayout("Esc", _label!, 60, h)) { Brush(1, 1, 1, 0.6f); _ctx.DrawTextLayout(new Vector2(x + w - 42 * s, y + (h - l.Metrics.Height) / 2), l, _b!); }
        ToolbarButtons = buttons;
    }

    /// <summary>Small vector icons (no icon font needed): monitor, two monitors, window, dashed region.</summary>
    private void ModeIcon(CaptureMode m, float cx, float cy, float s)
    {
        Brush(1, 1, 1, 0.95f); float k = 9 * s;
        switch (m)
        {
            case CaptureMode.Monitor:
                _ctx!.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - k, cy - k * 0.75f, cx + k, cy + k * 0.5f), RadiusX = 2, RadiusY = 2 }, _b!, 1.6f * s);
                _ctx.DrawLine(new Vector2(cx - k * 0.4f, cy + k * 0.95f), new Vector2(cx + k * 0.4f, cy + k * 0.95f), _b!, 1.6f * s); break;
            case CaptureMode.AllMonitors:
                _ctx!.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - k * 1.15f, cy - k * 0.5f, cx - k * 0.05f, cy + k * 0.35f), RadiusX = 2, RadiusY = 2 }, _b!, 1.4f * s);
                _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx + k * 0.05f, cy - k * 0.5f, cx + k * 1.15f, cy + k * 0.35f), RadiusX = 2, RadiusY = 2 }, _b!, 1.4f * s); break;
            case CaptureMode.Window:
                _ctx!.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - k, cy - k * 0.75f, cx + k, cy + k * 0.75f), RadiusX = 2, RadiusY = 2 }, _b!, 1.6f * s);
                _ctx.DrawLine(new Vector2(cx - k, cy - k * 0.3f), new Vector2(cx + k, cy - k * 0.3f), _b!, 1.4f * s); break;
            default:
                var dash = _dev!.Factory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = DashStyle.Dash });
                _ctx!.DrawRectangle(new RawRectF(cx - k, cy - k * 0.75f, cx + k, cy + k * 0.75f), _b!, 1.6f * s, dash); dash.Dispose(); break;
        }
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch ((int)msg)
            {
                case WM_MOUSEMOVE: _px = _screen.X + (short)((long)l & 0xFFFF); _py = _screen.Y + (short)(((long)l >> 16) & 0xFFFF); Render(); return IntPtr.Zero;
                case WM_LBUTTONDOWN:
                {
                    int x = _screen.X + (short)((long)l & 0xFFFF), y = _screen.Y + (short)(((long)l >> 16) & 0xFFFF);
                    var hit = ToolbarButtons.FirstOrDefault(b => b.Rect.Contains(x, y));
                    if (hit.Rect.W > 0) { _mode = hit.Mode; _dragFrom = null; Render(); return IntPtr.Zero; }
                    if (_mode == CaptureMode.Region) { _dragFrom = (x, y); BarNative.SetCapture(_hwnd); }
                    return IntPtr.Zero;
                }
                case WM_LBUTTONUP:
                {
                    BarNative.ReleaseCapture();
                    int x = _screen.X + (short)((long)l & 0xFFFF), y = _screen.Y + (short)(((long)l >> 16) & 0xFFFF); _px = x; _py = y;
                    if (ToolbarButtons.Any(b => b.Rect.Contains(x, y)) && _dragFrom is null) return IntPtr.Zero;
                    var t = Target(); _dragFrom = null;
                    if (!t.IsEmpty) Finish(t); else Render();
                    return IntPtr.Zero;
                }
                case WM_RBUTTONUP: Cancel(); return IntPtr.Zero;
                case WM_KEYDOWN:
                    switch ((int)w)
                    {
                        case 0x1B: Cancel(); return IntPtr.Zero;
                        case 0x0D: { var t = Target(); if (!t.IsEmpty) Finish(t); return IntPtr.Zero; }
                        case 0x31: _mode = CaptureMode.Region; break;
                        case 0x32: _mode = CaptureMode.Window; break;
                        case 0x33: _mode = CaptureMode.Monitor; break;
                        case 0x34: _mode = CaptureMode.AllMonitors; break;
                    }

                    Render(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[captura] error: " + e.GetType().Name + ": " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private void Finish(CaptureRect t)
    {
        if (_closed) return;
        var title = _mode == CaptureMode.Window ? CaptureGeometry.WindowAt(_windows, _px, _py, _screen)?.Title : null;
        var mode = _mode; Close(); _done?.Invoke(t, mode, title);
    }

    private void Cancel() { if (_closed) return; Close(); _cancel(); }

    private void Close() { _closed = true; if (_hwnd != IntPtr.Zero) BarNative.ShowWindow(_hwnd, 0); }

    public void Dispose()
    {
        _closed = true;
        _frozen?.Dispose(); _target?.Dispose(); _visual?.Dispose(); _dtarget?.Dispose(); _dcomp?.Dispose(); _swap?.Dispose();
        _label?.Dispose(); _dw?.Dispose(); _b?.Dispose(); _dev?.Dispose();
        if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); InstanceWindows.Forget(_hwnd); _hwnd = IntPtr.Zero; }
    }
}
