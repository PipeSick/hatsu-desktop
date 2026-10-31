using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Dock.Win.Offscreen;

/// <summary>Escenas de demostración que usa el renderizador REAL del proyecto (DockController + DockPainter + GlassBackdrop).</summary>
public enum DemoPose { Rest, Hover, Hidden, Open, Edge, Preview }

/// <summary>
/// Renderiza el Dock fuera de pantalla (Direct2D, GPU o WARP) y lo guarda como PNG. Sirve para CI y para revisar el aspecto sin una ventana real.
/// Usa exactamente el mismo controlador y painter que el dock en pantalla. NO valida: composición con DirectComposition, ventana, entrada del ratón,
/// ni el fondo real del escritorio (el fondo es procedural, distinto para tema claro y oscuro).
/// </summary>
public static class OffscreenRenderer
{
    private sealed class NoIcons : IIconSource { public WinMac.Apps.IconBitmap? Get(DockItemModel item) => null; }

    /// <summary>Ventanas ficticias para la escena de la vista previa: cada HWND genera una miniatura distinta (barra de título, líneas de texto y un bloque), sin imágenes de terceros.</summary>
    private sealed class SyntheticThumbs : IThumbnailSource
    {
        public static (int W, int H) SizeOf(long hwnd) => (hwnd % 3) switch { 0 => (640, 400), 1 => (480, 360), _ => (720, 400) };
        public static double Aspect(long hwnd) { var (w, h) = SizeOf(hwnd); return (double)w / h; }
        public WinMac.Apps.IconBitmap? Get(long hwnd)
        {
            var (w, h) = SizeOf(hwnd); var px = new byte[w * h * 4]; double hue = (hwnd * 37 % 360) / 360.0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double t = y / (double)h; byte r = (byte)(235 - 25 * t), g = (byte)(238 - 20 * t), b = (byte)(245 - 10 * t); int o = (y * w + x) * 4;
                    if (y < h * 0.07) { r = (byte)(60 + 80 * hue); g = 70; b = (byte)(110 + 100 * (1 - hue)); }                                    // barra de título
                    else if (y > h * 0.20 && y < h * 0.80 && x > w * 0.08 && x < w * 0.92 && (int)((y - h * 0.20) / (h * 0.06)) % 2 == 0) { r = g = b = 190; }   // líneas de texto
                    else if (x > w * 0.60 && y > h * 0.55 && x < w * 0.92 && y < h * 0.90) { r = (byte)(40 + 150 * hue); g = (byte)(120 + 60 * hue); b = 200; }  // bloque de imagen
                    px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = 255;
                }
            return new WinMac.Apps.IconBitmap(w, h, px);
        }
    }

    internal static DockView BuildView(DemoPose pose, DockMetrics metrics, DockConfig cfg, out DockController ctl)
    {
        var items = pose == DemoPose.Preview ? DemoData.AllOpenWithWindows() : pose == DemoPose.Open ? DemoData.AllOpen() : DemoData.Items();
        ctl = new DockController(metrics, cfg); ctl.SetItems(items);
        if (pose == DemoPose.Preview)
        {
            ctl.ThumbAspect = SyntheticThumbs.Aspect;
            for (int i = 0; i < 60; i++) ctl.Step(1.0 / 60);
            var v0 = ctl.Step(0); var it = v0.Items[2]; ctl.SetPointer(new PointerSample(it.X + it.Size / 2, v0.WindowHeight - metrics.ShadowMargin - 30));
            for (int i = 0; i < 90; i++) ctl.Step(1.0 / 60);                                   // la vista previa aparece tras el retardo
            var pv = ctl.Step(0).Preview; if (pv is not null) { var card = pv.Frame.Cards[1].Thumb; ctl.SetPointer(new PointerSample(card.X + card.W / 2, card.Y + card.H / 2)); }   // puntero sobre la 2.ª tarjeta
            for (int i = 0; i < 40; i++) ctl.Step(1.0 / 60);
            return ctl.Step(1.0 / 60);
        }
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

    public static string Render(string pngPath, DemoPose pose, GlassTier tier, DockTheme theme = DockTheme.Light, double scale = 1.0, bool software = true, double iconSize = 60, DockAlignment alignment = DockAlignment.Center)
    {
        var plain = DockMetrics.ForIconSize(iconSize);   // la vista previa agranda la ventana; el resto de escenas conservan su tamaño de siempre
        var metrics = pose == DemoPose.Preview ? plain with { PreviewHeadroom = PreviewSpec.Headroom(plain), PreviewMaxWidth = PreviewSpec.MaxPanelWidth } : plain;
        var cfg = new DockConfig { GlassTier = tier, IconSize = iconSize, Alignment = alignment }; var style = GlassStyle.For(theme);
        var view = BuildView(pose, metrics, cfg, out _);

        using var dev = new D2DDevice(software); dev.SetDpi(scale);
        var ctx = dev.Context; int pw = (int)Math.Ceiling(view.WindowWidth * scale), ph = (int)Math.Ceiling(view.WindowHeight * scale);
        using var under = SyntheticBackdrop.Create(dev, view.WindowWidth, view.WindowHeight, scale, theme);
        using var blurred = tier == GlassTier.Tint ? null : GlassBackdrop.Build(dev, under, new RectD(0, 0, view.WindowWidth, view.WindowHeight), view.WindowWidth, view.WindowHeight, scale, style);
        using var painter = new DockPainter(dev, metrics, style, new NoIcons(), pose == DemoPose.Preview ? new SyntheticThumbs() : null);
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
