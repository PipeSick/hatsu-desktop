// S4 — Prototipo mínimo del Dock en Win32 + DirectComposition + Direct3D11/Direct2D (bindings Vortice).
// Uso:  S4.DockDComp [--variant glass] [--pose rest|hover|hidden] [--interactive] [--quick] [--out DIR]
//   glass : vidrio SIMULADO dibujado en GPU (degradados, borde especular, sombra por capas). SIN blur real del fondo todavía:
//           el blur con captura del fondo (nivel F4) es una fase posterior (S4b) que se decide con los datos de este spike.
// Ventana sin bitmap de redirección (WS_EX_NOREDIRECTIONBITMAP) + swap chain de composición con alfa premultiplicado.
// El ocultar/mostrar anima el offset del Visual de DirectComposition (no mueve la ventana).
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Spikes.Common;
using AlphaMode = Vortice.DCommon.AlphaMode;

var opt = CliOptions.Parse(args, "glass");
using var app = new DockApp(opt);
return app.Run();

internal sealed class DockApp : IDisposable
{
    private const int WM_APP_POST = 0x8001, WM_DESTROY = 2, WM_KEYDOWN = 0x100, WM_MOUSEMOVE = 0x200, WM_MOUSELEAVE = 0x2A3, WM_CLOSE = 0x10;
    private readonly CliOptions _opt; private readonly BenchRun _run;
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly Win32.WndProc _proc; private IntPtr _hwnd;
    private double _scale = 1, _winW, _winH; private int _px, _py;
    private double? _mouseX; private bool _frames, _tracking;

    private ID3D11Device _d3d = null!; private IDXGISwapChain1 _swap = null!; private ID2D1Factory1 _d2dFactory = null!;
    private ID2D1DeviceContext _ctx = null!; private IDCompositionDevice _dcomp = null!; private IDCompositionTarget _target = null!; private IDCompositionVisual _visual = null!;
    private IDWriteTextFormat _text = null!; private ID2D1PathGeometry _squircle = null!;
    private ID2D1SolidColorBrush _white = null!, _dot = null!, _sepBrush = null!;
    private ID2D1LinearGradientBrush[] _iconFill = null!; private ID2D1LinearGradientBrush _barFill = null!, _barBorder = null!;
    private ID2D1SolidColorBrush[] _shadowLayers = null!;
    private ID2D1Bitmap1? _targetBmp;
    private DockPose _pose;

    public DockApp(CliOptions opt) { _opt = opt; _run = new BenchRun("S4.DockDComp", opt); _proc = WndProcImpl; }

    private DockPose StaticPose() => _opt.Pose switch
    {
        Pose.Hover => new DockPose(DockScene.PadX + 5 * (DockScene.BaseSize + DockScene.Gap) + DockScene.BaseSize / 2, 0),
        Pose.Hidden => new DockPose(null, 1),
        _ => new DockPose(null, 0),
    };

    public int Run()
    {
        CreateWindow(); CreateGraphics();
        _pose = StaticPose(); Render(_pose);
        _run.MarkFirstRender();
        if (_opt.Interactive) _frames = true;
        else if (_opt.IsBench) _ = _run.RunIdleAsync(a => { _posted.Enqueue(a); Win32.PostMessage(_hwnd, WM_APP_POST, IntPtr.Zero, IntPtr.Zero); }, () => _frames = true);

        while (true)
        {
            if (_frames)
            {
                while (Win32.PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { if (m.message == 0x12) return 0; Win32.TranslateMessage(ref m); Win32.DispatchMessage(ref m); }
                if (!DrainAndFrame()) return 0;
            }
            else
            {
                if (Win32.GetMessage(out var m, IntPtr.Zero, 0, 0) <= 0) return 0; // bloquea sin consumir CPU en reposo
                Win32.TranslateMessage(ref m); Win32.DispatchMessage(ref m);
            }
        }
    }

    private bool DrainAndFrame()
    {
        while (_posted.TryDequeue(out var a)) a();
        if (_opt.Interactive) { _pose = new DockPose(_mouseX, 0); Render(_pose); return true; }
        var pose = _run.Frame();
        if (pose is null)
        {
            Console.WriteLine("-> " + _run.Finish(_opt.Variant,
                ["geometría compartida", "squircle", "barra con forma propia", "borde especular (degradado)", "sombra (aproximada por capas)", "indicadores de app abierta", "separador", "magnificación", "ocultar/mostrar (offset de Visual DComp, sin mover ventana)"],
                ["blur real del fondo (S4b: captura + shader)", "refracción/lensing", "sombra gaussiana real"],
                "Cadencia = tiempo entre Present() con vsync (más cercano a la presentación real que Rendering de WPF/WinUI, pero igualmente verificar con PresentMon)."));
            return false;
        }
        long t0 = _run.ApplyBegin(); Render(pose.Value); _run.ApplyEnd(t0);
        return true;
    }

    // ---------------------------------------------------------------- ventana
    private void CreateWindow()
    {
        var cls = new Win32.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = Win32.GetModuleHandle(null), lpszClassName = "WinMacS4", hCursor = Win32.LoadCursor(IntPtr.Zero, 32512) };
        Win32.RegisterClassEx(ref cls);
        _hwnd = Win32.CreateWindowEx(Win32.WS_EX_NOREDIRECTIONBITMAP | Win32.WS_EX_TOPMOST | Win32.WS_EX_TOOLWINDOW, "WinMacS4", "WinMac S4", Win32.WS_POPUP,
            0, 0, 100, 100, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        _scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        Win32.SystemParametersInfo(0x30 /* SPI_GETWORKAREA */, 0, out var wa, 0);
        _winW = DockScene.WindowWidth; _winH = DockScene.WindowHeight;
        int w = (int)(_winW * _scale), h = (int)(_winH * _scale);
        _px = wa.l + ((wa.r - wa.l) - w) / 2; _py = wa.b - h;
        Win32.SetWindowPos(_hwnd, new IntPtr(-1), _px, _py, w, h, 0x0040 /* SHOWWINDOW */);
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch ((int)msg)
        {
            case WM_APP_POST: while (_posted.TryDequeue(out var a)) a(); return IntPtr.Zero;
            case WM_KEYDOWN when (int)w == 0x1B: Win32.DestroyWindow(h); return IntPtr.Zero;
            case WM_MOUSEMOVE when _opt.Interactive:
                int x = (short)((long)l & 0xFFFF);
                _mouseX = x / _scale - (_winW - DockScene.RestWidth) / 2;
                if (!_tracking) { var t = new Win32.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(), dwFlags = 2, hwndTrack = h }; Win32.TrackMouseEvent(ref t); _tracking = true; }
                return IntPtr.Zero;
            case WM_MOUSELEAVE: _mouseX = null; _tracking = false; return IntPtr.Zero;
            case WM_DESTROY: Win32.PostQuitMessage(0); return IntPtr.Zero;
        }
        return Win32.DefWindowProc(h, msg, w, l);
    }

    // ---------------------------------------------------------------- gráficos
    private void CreateGraphics()
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, [Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0], out _d3d!).CheckError();
        using var dxgiDevice = _d3d.QueryInterface<IDXGIDevice>();
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        int pw = (int)(_winW * _scale), ph = (int)(_winH * _scale);
        _swap = factory.CreateSwapChainForComposition(_d3d, new SwapChainDescription1
        {
            Width = (uint)pw, Height = (uint)ph, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0), SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied, Scaling = Scaling.Stretch,
        });
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using var d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _ctx = d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _ctx.SetDpi((float)(96 * _scale), (float)(96 * _scale));
        BindTarget();

        _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        _dcomp.CreateTargetForHwnd(_hwnd, true, out _target!).CheckError();
        _visual = _dcomp.CreateVisual();
        _visual.SetContent(_swap);
        _target.SetRoot(_visual);
        _dcomp.Commit();

        using var dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _text = dw.CreateTextFormat("Segoe UI Semibold", 24f);
        _white = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); _dot = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, .82f));
        _sepBrush = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, .35f));
        _shadowLayers = Enumerable.Range(0, 8).Select(_ => _ctx.CreateSolidColorBrush(new Color4(0, 0, 0, .035f))).ToArray();
        _barFill = Gradient(new Color4(1, 1, 1, .47f), new Color4(1, 1, 1, .22f));
        _barBorder = Gradient(new Color4(1, 1, 1, .75f), new Color4(1, 1, 1, .15f));

        _squircle = _d2dFactory.CreatePathGeometry();
        using (var sink = _squircle.Open())
        {
            var pts = Squircle.Unit();
            sink.BeginFigure(new Vector2((float)pts[0].X, (float)pts[0].Y), FigureBegin.Filled);
            foreach (var p in pts.Skip(1)) sink.AddLine(new Vector2((float)p.X, (float)p.Y));
            sink.EndFigure(FigureEnd.Closed); sink.Close();
        }
        _iconFill = DockScene.Items.Select(it => Gradient(
            new Color4(Math.Min(255, it.R + 70) / 255f, Math.Min(255, it.G + 70) / 255f, Math.Min(255, it.B + 70) / 255f, 1),
            new Color4(it.R / 255f, it.G / 255f, it.B / 255f, 1))).ToArray();
    }

    private ID2D1LinearGradientBrush Gradient(Color4 top, Color4 bottom)
    {
        using var stops = _ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = top }, new GradientStop { Position = 1, Color = bottom }], Gamma.StandardRgb, ExtendMode.Clamp);
        return _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, 1) }, stops);
    }

    private void BindTarget()
    {
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _targetBmp?.Dispose();
        _targetBmp = _ctx.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied),
            (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _ctx.Target = _targetBmp;
    }

    private void Render(DockPose pose)
    {
        var f = DockScene.Layout(pose.CursorX);
        float W = (float)_winW, H = (float)_winH;
        float off = (W - (float)f.BarWidth) / 2, barBottom = H - (float)DockScene.ShadowMargin, barTop = barBottom - (float)DockScene.BarHeight;
        _ctx.BeginDraw();
        _ctx.Clear(new Color4(0, 0, 0, 0));
        // sombra por capas (aprox.)
        for (int i = 0; i < _shadowLayers.Length; i++)
        {
            float g = 3 + i * 3.2f;
            _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new Vortice.RawRectF(off - g, barTop - g + 10, off + (float)f.BarWidth + g, barBottom + g + 10), RadiusX = (float)DockScene.BarRadius + g, RadiusY = (float)DockScene.BarRadius + g }, _shadowLayers[i]);
        }
        var rr = new RoundedRectangle { Rect = new Vortice.RawRectF(off, barTop, off + (float)f.BarWidth, barBottom), RadiusX = (float)DockScene.BarRadius, RadiusY = (float)DockScene.BarRadius };
        _barFill.StartPoint = new Vector2(0, barTop); _barFill.EndPoint = new Vector2(0, barBottom);
        _barBorder.StartPoint = new Vector2(0, barTop); _barBorder.EndPoint = new Vector2(0, barBottom);
        _ctx.FillRoundedRectangle(rr, _barFill);
        _ctx.DrawRoundedRectangle(rr, _barBorder, 1f);
        float sx = off + (float)f.SeparatorX;
        _ctx.DrawLine(new Vector2(sx, barTop + (float)DockScene.BarHeight * .2f), new Vector2(sx, barTop + (float)DockScene.BarHeight * .8f), _sepBrush, 1f);

        for (int i = 0; i < DockScene.Items.Length; i++)
        {
            float s = (float)f.Size[i], x = off + (float)f.X[i], y = barBottom - (float)DockScene.PadBottom - s;
            _ctx.Transform = Matrix3x2.CreateScale(s, s) * Matrix3x2.CreateTranslation(x, y);
            _iconFill[i].StartPoint = new Vector2(0, 0); _iconFill[i].EndPoint = new Vector2(0, 1);
            _ctx.FillGeometry(_squircle, _iconFill[i]);
            _ctx.DrawGeometry(_squircle, _sepBrush, 1f / s);
            _ctx.Transform = Matrix3x2.CreateScale(s / 56f, s / 56f) * Matrix3x2.CreateTranslation(x + s * .5f - 8 * s / 56f, y + s * .5f - 16 * s / 56f);
            _ctx.DrawText(DockScene.Items[i].Glyph, _text, new Vortice.RawRectF(0, 0, 40, 40), _white);
            _ctx.Transform = Matrix3x2.Identity;
            if (DockScene.Items[i].IsOpen)
                _ctx.FillEllipse(new Ellipse(new Vector2(x + s / 2, barBottom - (float)DockScene.PadBottom + 6), (float)DockScene.DotSize / 2, (float)DockScene.DotSize / 2), _dot);
        }
        _ctx.EndDraw();
        _visual.SetOffsetY((float)(pose.HideT * DockScene.HideDistance * _scale));
        _dcomp.Commit();
        _swap.Present(1, PresentFlags.None);
    }

    public void Dispose() { _targetBmp?.Dispose(); _ctx?.Dispose(); _swap?.Dispose(); _d3d?.Dispose(); }
}

internal static class Win32
{
    public const uint WS_POPUP = 0x80000000; public const uint WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX { public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)] public struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int c);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG m, IntPtr h, uint a, uint b, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, int id);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")] public static extern bool SystemParametersInfo(uint action, uint p, out RECT r, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
}
