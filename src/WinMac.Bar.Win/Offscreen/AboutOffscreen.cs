using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;
using WinMac.Bar.Win.Rendering;

namespace WinMac.Bar.Win.Offscreen;

/// <summary>It7 (CI): «Acerca de este PC» con DATOS DE MUESTRA (no son los de ningún equipo real: en producción todo sale de <c>AboutPcReader</c>), en ambos temas.</summary>
public static class AboutOffscreen
{
    public static string Render(string png, bool dark, double scale, bool expanded)
    {
        var raw = new AboutRaw("LENOVO", "82XB", null, "PC-DE-MUESTRA", "AMD Ryzen 7 5800H with Radeon Graphics", 8, 16, 16UL << 30, null,
            [new GpuInfo("AMD Radeon(TM) Graphics"), new GpuInfo("NVIDIA GeForce RTX 3060 Laptop GPU")], "Windows 10 Pro", "25H2", 26300, 9711, "X64", "GKCN55WW", "2.0.0");
        var view = AboutFormatter.Build(raw); var theme = dark ? DockTheme.Dark : DockTheme.Light;
        using var dev = new D2DDevice(true); dev.SetDpi(scale);
        var (pw, ph) = AboutWindow.PreviewSize(view, expanded, scale, dev);
        using var under = SyntheticBackdrop.Create(dev, pw / scale, ph / scale, scale, theme, bands: false); using var target = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale);
        var c = dev.Context; c.Target = target; c.BeginDraw(); c.DrawBitmap(under, (Vortice.RawRectF?)new Vortice.RawRectF(0, 0, (float)(pw / scale), (float)(ph / scale)), 1f, Vortice.Direct2D1.BitmapInterpolationMode.Linear, (Vortice.RawRectF?)null); c.EndDraw();
        AboutWindow.Preview(dev, view, expanded, theme);
        var rgba = BarOffscreen.Readback(dev, target, pw, ph, scale); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(png))!);
        File.WriteAllBytes(png, PngWriter.EncodeRgba(rgba, pw, ph)); c.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, acerca de (DATOS DE MUESTRA), tema {(dark ? "oscuro" : "claro")}, {(expanded ? "con detalles" : "compacto")}";
    }
}
