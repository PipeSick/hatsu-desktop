using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Dock.Win.Offscreen;

/// <summary>Escenas de demostración que usa el renderizador REAL del proyecto (DockController + DockPainter + GlassBackdrop).</summary>
public enum DemoPose { Rest, Hover, Hidden, Open, Edge }

/// <summary>
/// Renderiza el Dock fuera de pantalla (Direct2D, GPU o WARP) y lo guarda como PNG. Sirve para CI y para revisar el aspecto sin una ventana real.
/// Usa exactamente el mismo controlador y painter que el dock en pantalla. NO valida: composición con DirectComposition, ventana, entrada del ratón,
/// ni el fondo real del escritorio (el fondo es procedural, distinto para tema claro y oscuro).
/// </summary>
public static class OffscreenRenderer
{
    private sealed class NoIcons : IIconSource { public WinMac.Apps.IconBitmap? Get(DockItemModel item) => null; }

    internal static DockView BuildView(DemoPose pose, DockMetrics metrics, DockConfig cfg, out DockController ctl)
    {
        var items = pose == DemoPose.Open ? DemoData.AllOpen() : DemoData.Items();
        ctl = new DockController(metrics, cfg); ctl.SetItems(items);
        for (int i = 0; i < 60; i++) ctl.Step(1.0 / 60);
        double pointerY(DockView v) => v.WindowHeight - metrics.ShadowMargin - 30;
        if (pose == DemoPose.Hover || pose == DemoPose.Open)
        {
            var v0 = ctl.Step(0); var it = v0.Items[pose == DemoPose.Open ? 4 : 3];
            ctl.SetPointer(new PointerSample(it.X + it.Size / 2, pointerY(v0)));
        }
        if (pose == DemoPose.Edge) { var v0 = ctl.Step(0); var it = v0.Items[0]; ctl.SetPointer(new PointerSample(it.X + it.Size / 2, pointerY(v0))); }
        if (pose == DemoPose.Hidden) ctl.SetSuppressed(true);
        for (int i = 0; i < 180; i++) ctl.Step(1.0 / 60);
        return ctl.Step(1.0 / 60);
    }

    public static string Render(string pngPath, DemoPose pose, GlassTier tier, DockTheme theme = DockTheme.Light, double scale = 1.0, bool software = true)
    {
        var metrics = new DockMetrics(); var cfg = new DockConfig { GlassTier = tier }; var style = GlassStyle.For(theme);
        var view = BuildView(pose, metrics, cfg, out _);

        using var dev = new D2DDevice(software); dev.SetDpi(scale);
        var ctx = dev.Context; int pw = (int)Math.Ceiling(view.WindowWidth * scale), ph = (int)Math.Ceiling(view.WindowHeight * scale);
        using var under = SyntheticBackdrop.Create(dev, view.WindowWidth, view.WindowHeight, scale, theme);
        using var blurred = tier == GlassTier.Tint ? null : GlassBackdrop.Build(dev, under, new RectD(0, 0, view.WindowWidth, view.WindowHeight), view.WindowWidth, view.WindowHeight, scale, style);
        using var painter = new DockPainter(dev, metrics, style, new NoIcons());
        painter.SetBackdrop(blurred);

        using var target = GlassBackdrop.CreateTarget(ctx, pw, ph, scale);
        ctx.Target = target;
        painter.Paint(view, tier, under);
        var rgba = Readback(dev, target, pw, ph, scale);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pngPath))!);
        File.WriteAllBytes(pngPath, PngWriter.EncodeRgba(rgba, pw, ph));
        ctx.Target = null;
        return $"{pw}x{ph}px, {dev.Description}, nivel {tier}, tema {theme}, escena {pose}";
    }

    internal static byte[] Readback(D2DDevice dev, ID2D1Bitmap1 target, int pw, int ph, double scale)
    {
        using var cpu = GlassBackdrop.CreateTarget(dev.Context, pw, ph, scale, readable: true);
        cpu.CopyFromBitmap(target);
        var map = cpu.Map(MapOptions.Read);
        var bgra = new byte[map.Pitch * ph];
        try { Marshal.Copy(map.Bits, bgra, 0, bgra.Length); } finally { cpu.Unmap(); }
        return PngWriter.BgraPremulToRgba(bgra, (int)map.Pitch, pw, ph);
    }
}
