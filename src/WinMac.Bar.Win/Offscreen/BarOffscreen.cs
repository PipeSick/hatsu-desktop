using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using WinMac.Bar.Win.Rendering;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Offscreen;

/// <summary>Renderiza la barra FUERA de pantalla con el pintor real (D2D, GPU o WARP) sobre un fondo procedural y la guarda como PNG. Datos ficticios. No valida AppBar, ventana, ratón ni el fondo real.</summary>
public static class BarOffscreen
{
    public static BarState DemoState(DateTime? now = null) => new(now ?? new DateTime(2026, 10, 30, 8, 5, 0), "Bloc de notas", new BatteryInfo(true, 82, false, true), new VolumeInfo(true, 60, false), new NetworkInfo(NetKind.Wifi));

    public static string Render(string pngPath, BarTheme theme, BarBackground bg, double scale = 2.0, double widthDip = 1280, BarState? state = null, int hoverItem = -1, bool software = true)
    {
        var m = new BarMetrics(); var cfg = new BarConfig { Background = bg }; var st = state ?? DemoState();
        using var dev = new D2DDevice(software); dev.SetDpi(scale);
        using var painter = new BarPainter(dev, m) { Theme = theme, Background = bg };
        var ctl = new BarController(m, cfg, st); ctl.Measure = (t, b) => painter.Measure(t, b); ctl.SetWidth(widthDip);
        var v = ctl.Step(0);
        if (hoverItem >= 0) { var f = v.Frame.Items[Math.Min(hoverItem, v.Frame.Items.Count - 1)]; ctl.SetPointer(f.X + f.W / 2, m.Height / 2); for (int i = 0; i < 30; i++) v = ctl.Step(1.0 / 60); }
        int pw = (int)Math.Ceiling(widthDip * scale), ph = (int)Math.Ceiling(m.Height * scale);
        var dockTheme = theme == BarTheme.Dark ? DockTheme.Dark : DockTheme.Light;
        // El fondo procedural es alto (como una pantalla) para que el recorte de la barra se vea en contexto; se dibuja la barra sobre su franja superior.
        double bgH = 140;
        using var under = SyntheticBackdrop.Create(dev, widthDip, bgH, scale, dockTheme);
        using var blurred = bg == BarBackground.Glass ? GlassBackdrop.Build(dev, under, new RectD(0, 0, widthDip, m.Height), widthDip, m.Height, scale, GlassStyle.For(dockTheme) with { BlurSigma = 16 }) : null;
        painter.SetBackdrop(blurred);
        int totalH = (int)Math.Ceiling(bgH * scale);
        using var target = GlassBackdrop.CreateTarget(dev.Context, pw, totalH, scale); var ctx = dev.Context; ctx.Target = target;
        ctx.BeginDraw(); ctx.DrawBitmap(under, (Vortice.RawRectF?)new Vortice.RawRectF(0, 0, (float)widthDip, (float)bgH), 1f, BitmapInterpolationMode.Linear, (Vortice.RawRectF?)null); ctx.EndDraw();
        painter.PaintOver(v);   // dibuja sin borrar lo que hay debajo
        var rgba = Readback(dev, target, pw, totalH, scale);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, PngWriter.EncodeRgba(rgba, pw, totalH)); ctx.Target = null;
        return $"{pw}x{totalH}px, {dev.Description}, tema {theme}, fondo {bg}, hover={hoverItem}";
    }

    internal static byte[] Readback(D2DDevice dev, ID2D1Bitmap1 target, int pw, int ph, double scale)
    {
        using var cpu = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale, readable: true); cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read); var bgra = new byte[map.Pitch * ph];
        try { Marshal.Copy(map.Bits, bgra, 0, bgra.Length); } finally { cpu.Unmap(); }
        return PngWriter.BgraPremulToRgba(bgra, (int)map.Pitch, pw, ph);
    }
}
