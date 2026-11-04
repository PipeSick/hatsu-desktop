using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.DXGI;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Host.App;

/// <summary>
/// Ventana del asistente de primera ejecución: dibujada con Direct2D (sin web ni navegador), tamaño fijo, claro/oscuro según Windows y DPI del sistema. Solo recoge ratón y teclado y los pasa al
/// <see cref="OnboardingController"/> (probado sin Windows); lo que hay que APLICAR lo decide el host a través de <see cref="OnCommand"/>.
/// </summary>
internal sealed class OnboardingWindow : IDisposable
{
    private const uint WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WM_CLOSE = 0x10, WM_DESTROY = 2, WM_KEYDOWN = 0x100, WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_MOUSELEAVE = 0x2A3, WM_DPICHANGED = 0x2E0, WM_RUN = Native.WM_APP + 21;
    private const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;
    private const string ClassName = "HatsuDesktopOnboarding";

    private readonly Action<string> _log; private readonly Native.WndProc _proc; private readonly ConcurrentQueue<Action> _queue = new();
    private IntPtr _hwnd; private D2DDevice? _dev; private OnboardingPainter? _painter; private IDXGISwapChain1? _swap; private ID2D1Bitmap1? _bmp; private double _scale = 1; private bool _dark, _tracking;

    public OnboardingController Controller { get; }
    /// <summary>El host decide qué hacer con cada orden (aplicar, omitir, reintentar…). Se llama en el hilo de la ventana.</summary>
    public Action<OnboardingCommand>? OnCommand { get; set; }
    /// <summary>Se llama al destruirse la ventana (por cualquier vía).</summary>
    public Action? Closed { get; set; }
    public bool IsOpen => _hwnd != IntPtr.Zero;

    public OnboardingWindow(OnboardingController controller, Action<string> log) { Controller = controller; _log = log; _proc = WndProcImpl; }

    /// <summary>Ejecuta algo en el hilo de la ventana (desde una tarea en segundo plano).</summary>
    public void Post(Action a) { _queue.Enqueue(a); if (_hwnd != IntPtr.Zero) Native.PostMessage(_hwnd, WM_RUN, IntPtr.Zero, IntPtr.Zero); }
    public void Invalidate() { if (_hwnd != IntPtr.Zero) Native.InvalidateRect(_hwnd, IntPtr.Zero, false); }
    public void Close() { if (_hwnd != IntPtr.Zero) Native.DestroyWindow(_hwnd); }

    private static bool SystemDark() { try { return Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0; } catch (Exception) { return false; } }

    public void Show()
    {
        try
        {
            if (_hwnd != IntPtr.Zero) { Native.ShowWindow(_hwnd, 9); Native.SetForegroundWindow(_hwnd); return; }
            uint dpi = Native.GetDpiForSystem(); if (dpi < 96) dpi = 96; _scale = dpi / 96.0; _dark = SystemDark();
            var inst = Native.GetModuleHandle(null);
            var cls = new Native.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = ClassName, hCursor = Native.LoadCursorW(IntPtr.Zero, (IntPtr)32512) };
            Native.RegisterClassEx(ref cls);
            const uint style = 0x00CA0000;   // caption + sysmenu + minimizar; tamaño fijo
            var r = new Native.RECT { Right = (int)Math.Round(OnboardingController.WinW * _scale), Bottom = (int)Math.Round(OnboardingController.WinH * _scale) }; Native.AdjustWindowRectExForDpi(ref r, style, false, 0, dpi);
            int sw = Native.GetSystemMetrics(0), sh = Native.GetSystemMetrics(1), ww = r.Right - r.Left, wh = r.Bottom - r.Top;
            _hwnd = Native.CreateWindowEx(0, ClassName, "Hatsu Desktop", style, Math.Max(0, (sw - ww) / 2), Math.Max(0, (sh - wh) / 2), ww, wh, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falló: " + Marshal.GetLastWin32Error());
            int dk = _dark ? 1 : 0; Native.DwmSetWindowAttribute(_hwnd, 20, ref dk, 4);
            _dev = new D2DDevice(); _dev.SetDpi(_scale); _painter = new OnboardingPainter(_dev, _dark); CreateSurface();
            Native.ShowWindow(_hwnd, 5); Native.SetForegroundWindow(_hwnd);
        }
        catch (Exception e) { _log("[asistente] no se pudo abrir la ventana: " + e.GetType().Name + ": " + e.Message); DestroyAll(); }
    }

    private void CreateSurface()
    {
        Native.GetClientRect(_hwnd, out var rc); int w = Math.Max(1, rc.Right - rc.Left), h = Math.Max(1, rc.Bottom - rc.Top); DisposeSurface();
        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        _swap = factory.CreateSwapChainForHwnd(_dev!.D3D, _hwnd, new SwapChainDescription1
        {
            Width = (uint)w, Height = (uint)h, Format = Format.B8G8R8A8_UNorm, BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipSequential, AlphaMode = Vortice.DXGI.AlphaMode.Ignore, Scaling = Scaling.None,
        });
        using var surface = _swap.GetBuffer<IDXGISurface>(0);
        _bmp = _dev.Context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore), (float)(96 * _scale), (float)(96 * _scale), BitmapOptions.Target | BitmapOptions.CannotDraw));
        _dev.Context.Target = _bmp;
    }
    private void DisposeSurface() { if (_dev is not null) _dev.Context.Target = null; _bmp?.Dispose(); _bmp = null; _swap?.Dispose(); _swap = null; }

    private void Render()
    {
        if (_painter is null || _swap is null || _hwnd == IntPtr.Zero) return;
        try { _painter.Paint(Controller); _swap.Present(1, PresentFlags.None); } catch (Exception e) { _log("[asistente] error al dibujar: " + e.GetType().Name + ": " + e.Message); }
    }

    private double X(IntPtr l) => (short)((long)l & 0xFFFF) / _scale;
    private double Y(IntPtr l) => (short)(((long)l >> 16) & 0xFFFF) / _scale;
    private void Run(OnboardingCommand c) { if (c != OnboardingCommand.None) { try { OnCommand?.Invoke(c); } catch (Exception e) { _log("[asistente] error al ejecutar la orden " + c + ": " + e.Message); } } Invalidate(); }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch (msg)
            {
                case WM_PAINT: Render(); Native.ValidateRect(h, IntPtr.Zero); return IntPtr.Zero;
                case WM_ERASEBKGND: return (IntPtr)1;
                case WM_MOUSEMOVE:
                {
                    if (!_tracking) { var t = new Native.TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<Native.TRACKMOUSEEVENT>(), dwFlags = 2, hwndTrack = _hwnd }; _tracking = Native.TrackMouseEvent(ref t); }
                    string? id = Controller.HitTest(X(l), Y(l)); if (id != Controller.Hover) { Controller.Hover = id; Invalidate(); }
                    return IntPtr.Zero;
                }
                case WM_MOUSELEAVE: _tracking = false; Controller.Hover = null; Invalidate(); return IntPtr.Zero;
                case WM_LBUTTONDOWN: if (Controller.HitTest(X(l), Y(l)) is { } hit) Run(Controller.Click(hit)); return IntPtr.Zero;
                case WM_KEYDOWN:
                {
                    int vk = (int)w;
                    if (vk == VK_RETURN) Run(Controller.Primary()); else if (vk == VK_ESCAPE) Run(Controller.Escape());
                    return IntPtr.Zero;
                }
                case WM_RUN: while (_queue.TryDequeue(out var a)) { try { a(); } catch (Exception e) { _log("[asistente] " + e.Message); } } Invalidate(); return IntPtr.Zero;
                case WM_DPICHANGED:
                {
                    uint dpi = (uint)((long)w & 0xFFFF); _scale = dpi / 96.0; _dev!.SetDpi(_scale); var rc = Marshal.PtrToStructure<Native.RECT>(l);
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, 0x0004 | 0x0010); CreateSurface(); Invalidate(); return IntPtr.Zero;
                }
                case WM_CLOSE: if (Controller.Busy) return IntPtr.Zero; Native.DestroyWindow(h); return IntPtr.Zero;   // mientras se aplica no se puede cerrar: nada a medias
                case WM_DESTROY: DestroyAll(); try { Closed?.Invoke(); } catch (Exception) { } return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[asistente] error en la ventana: " + e.GetType().Name + ": " + e.Message); }
        return Native.DefWindowProc(h, msg, w, l);
    }

    private void DestroyAll() { DisposeSurface(); _painter?.Dispose(); _painter = null; _dev?.Dispose(); _dev = null; _hwnd = IntPtr.Zero; }
    public void Dispose() { var h = _hwnd; if (h != IntPtr.Zero) Native.DestroyWindow(h); DestroyAll(); }

    // ───────────────────────────── CI: dibujo fuera de pantalla con el pintor real
    /// <summary>Renderiza una pantalla del asistente a PNG con elecciones de MUESTRA. No abre ventanas ni lee el sistema: valida dibujo y composición, NO el ratón ni el foco.</summary>
    public static string RenderOffscreen(string png, OnboardingStep step, bool dark, double scale)
    {
        var choices = OnboardingChoices.Safe with { Connect = false, Peripherals = true, Theme = WinMac.Dock.ThemeMode.System, DockSize = 2, DockAlignment = WinMac.Dock.DockAlignment.Center, Autostart = false, HideTaskbar = false };
        var ctl = new OnboardingController(OnboardingMode.Fresh, choices);
        switch (step)
        {
            case OnboardingStep.Components: ctl.Click("next"); break;
            case OnboardingStep.Appearance: ctl.Click("next"); ctl.Click("next"); break;
            case OnboardingStep.Windows: ctl.Click("next"); ctl.Click("next"); ctl.Click("next"); ctl.Click("t:autostart"); break;
            case OnboardingStep.Review: for (int i = 0; i < 4; i++) ctl.Click("next"); ctl.Click("t:autostart"); break;
            case OnboardingStep.Result:
                for (int i = 0; i < 4; i++) ctl.Click("next");
                ctl.ShowResult(new ApplyResult([new("config", "Guardar la configuración", ApplyOutcome.Ok), new("mod:bar", "Barra superior", ApplyOutcome.Ok, "En ejecución"), new("mod:dock", "Dock", ApplyOutcome.Ok, "En ejecución"),
                    new("mod:notch", "Dynamic Island", ApplyOutcome.Warning, "No arrancó (estado: Failed)"), new("mod:connect", "Hatsu Connect", ApplyOutcome.Ok, "Desactivado: no se inicia")], OnboardingStatus.InProgress, false)); break;
        }
        int pw = (int)Math.Ceiling(OnboardingController.WinW * scale), ph = (int)Math.Ceiling(OnboardingController.WinH * scale);
        using var dev = new D2DDevice(true); dev.SetDpi(scale); using var painter = new OnboardingPainter(dev, dark);
        using var target = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale); dev.Context.Target = target;
        painter.Paint(ctl);
        using var cpu = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale, readable: true); cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read); var bgra = new byte[map.Pitch * ph];
        try { Marshal.Copy(map.Bits, bgra, 0, bgra.Length); } finally { cpu.Unmap(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png))!); File.WriteAllBytes(png, PngWriter.EncodeRgba(PngWriter.BgraPremulToRgba(bgra, (int)map.Pitch, pw, ph), pw, ph)); dev.Context.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, paso {step}, tema {(dark ? "oscuro" : "claro")}";
    }
}
