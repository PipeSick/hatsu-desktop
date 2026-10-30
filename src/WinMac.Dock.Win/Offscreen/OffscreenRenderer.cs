using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Dock.Win.Offscreen;

public enum DemoPose { Rest, Hover, Hidden }

/// <summary>
/// Renderiza el Dock fuera de pantalla (Direct2D, GPU o WARP) y lo guarda como PNG. Sirve para CI y para revisar el aspecto sin una ventana real.
/// NO valida: composición con DirectComposition, ventana, entrada del ratón, ni el fondo real del escritorio (usa un fondo procedural).
/// </summary>
public static class OffscreenRenderer
{
    private sealed class NoIcons : IIconSource { public Shell.IconPixels? Get(DockItemModel item) => null; }

    public static string Render(string pngPath, DemoPose pose, GlassTier tier, double scale = 1.0, bool software = true)
    {
        var metrics = new DockMetrics(); var cfg = new DockConfig { GlassTier = tier };
        var items = DemoData.Items();
        var ctl = new DockController(metrics, cfg); ctl.SetItems(items);
        for (int i = 0; i < 60; i++) ctl.Step(1.0 / 60);
        if (pose == DemoPose.Hover)
        {
            var v0 = ctl.Step(0); var it = v0.Items[3];
            ctl.SetPointer(new PointerSample(it.X + it.Size / 2, v0.WindowHeight - metrics.ShadowMargin - 30));
        }
        if (pose == DemoPose.Hidden) ctl.SetSuppressed(true);
        for (int i = 0; i < 180; i++) ctl.Step(1.0 / 60);
        var view = ctl.Step(1.0 / 60);

        using var dev = new D2DDevice(software); dev.SetDpi(scale);
        var ctx = dev.Context; int pw = (int)Math.Ceiling(view.WindowWidth * scale), ph = (int)Math.Ceiling(view.WindowHeight * scale);
        using var under = SyntheticBackdrop.Create(dev, pw, ph);
        using var blurred = tier == GlassTier.Tint ? null : GlassBackdrop.Build(dev, under, new RectD(0, 0, pw, ph), view.WindowWidth, view.WindowHeight, scale, new GlassStyle());
        using var painter = new DockPainter(dev, metrics, new GlassStyle(), new NoIcons());
        painter.SetBackdrop(blurred);

        using var target = GlassBackdrop.CreateTarget(ctx, pw, ph, scale);
        ctx.Target = target;
        painter.Paint(view, tier, under);

        using var cpu = GlassBackdrop.CreateTarget(ctx, pw, ph, scale, readable: true);
        cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read);
        var bgra = new byte[map.Pitch * ph];
        try { Marshal.Copy(map.Bits, bgra, 0, bgra.Length); } finally { cpu.Unmap(); }
        var rgba = PngWriter.BgraPremulToRgba(bgra, (int)map.Pitch, pw, ph);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, PngWriter.EncodeRgba(rgba, pw, ph));
        ctx.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, nivel {tier}";
    }
}
