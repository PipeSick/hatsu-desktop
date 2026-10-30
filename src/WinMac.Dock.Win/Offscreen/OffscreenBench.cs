using System.Diagnostics;
using System.Text.Json;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Dock.Win.Offscreen;

/// <summary>
/// Mide el coste de DIBUJAR el Dock fuera de pantalla con el renderizador real, sin escritorio interactivo.
/// LIMITACIONES (se imprimen en el JSON): en el runner de CI se usa WARP (CPU), no una GPU; el tiempo incluye una lectura completa del fotograma
/// para sincronizar con la GPU/WARP; NO mide presentación, DirectComposition, latencia de entrada ni consumo real. Sirve para comparar versiones del painter entre sí
/// y detectar regresiones gruesas, no para decidir si se cumplen los umbrales de FPS.
/// </summary>
public static class OffscreenBench
{
    public static string Run(string jsonPath, GlassTier tier, DockTheme theme, double scale, int frames = 360, bool software = true)
    {
        var metrics = new DockMetrics(); var style = GlassStyle.For(theme); var cfg = new DockConfig { GlassTier = tier };
        var ctl = new DockController(metrics, cfg); ctl.SetItems(DemoData.AllOpen());
        for (int i = 0; i < 60; i++) ctl.Step(1.0 / 60);
        var v0 = ctl.Step(0);

        using var dev = new D2DDevice(software); dev.SetDpi(scale); var ctx = dev.Context;
        int pw = (int)Math.Ceiling(v0.WindowWidth * scale), ph = (int)Math.Ceiling(v0.WindowHeight * scale);
        using var under = SyntheticBackdrop.Create(dev, v0.WindowWidth, v0.WindowHeight, scale, theme);
        var swBuild = Stopwatch.StartNew();
        using var blurred = tier == GlassTier.Tint ? null : GlassBackdrop.Build(dev, under, new RectD(0, 0, v0.WindowWidth, v0.WindowHeight), v0.WindowWidth, v0.WindowHeight, scale, style);
        if (blurred is not null) OffscreenRenderer.Readback(dev, blurred, pw, ph, scale); // fuerza la ejecución del efecto
        double buildMs = swBuild.Elapsed.TotalMilliseconds;

        using var painter = new DockPainter(dev, metrics, style, new NoIconsBench()); painter.SetBackdrop(blurred);
        using var target = GlassBackdrop.CreateTarget(ctx, pw, ph, scale); ctx.Target = target;

        var paint = new List<double>(frames); var total = new List<double>(frames);
        double rest = DockLayout.RestWidth(metrics, ctl.Items), restLeft = (v0.WindowWidth - rest) / 2;
        for (int f = 0; f < frames + 20; f++)
        {
            double t = f / 60.0;
            double px = restLeft + rest * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 2.5 - Math.PI / 2));
            ctl.SetPointer(new PointerSample(px, v0.WindowHeight - metrics.ShadowMargin - 30));
            var view = ctl.Step(1.0 / 60);
            var sw = Stopwatch.StartNew();
            painter.Paint(view, tier, null);
            double a = sw.Elapsed.TotalMilliseconds;
            OffscreenRenderer.Readback(dev, target, pw, ph, scale);
            double b = sw.Elapsed.TotalMilliseconds;
            if (f >= 20) { paint.Add(a); total.Add(b); } // los 20 primeros son calentamiento
        }
        ctx.Target = null;
        double P(List<double> l, double p) { var s = l.OrderBy(x => x).ToArray(); return s[Math.Min(s.Length - 1, (int)(p * s.Length))]; }
        var report = new
        {
            Kind = "offscreen-bench", Device = dev.Description, IsSoftware = dev.IsSoftware, Tier = tier.ToString(), Theme = theme.ToString(), Scale = scale,
            SurfacePx = $"{pw}x{ph}", Frames = paint.Count, BackdropBuildMs = Math.Round(buildMs, 2),
            PaintMs = new { P50 = Math.Round(P(paint, .5), 3), P95 = Math.Round(P(paint, .95), 3), P99 = Math.Round(P(paint, .99), 3), Max = Math.Round(paint.Max(), 3) },
            PaintPlusReadbackMs = new { P50 = Math.Round(P(total, .5), 3), P95 = Math.Round(P(total, .95), 3), P99 = Math.Round(P(total, .99), 3), Max = Math.Round(total.Max(), 3) },
            WorkingSetMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1048576.0, 1),
            Limitations = new[] { "Renderizado por WARP (CPU) en el runner, no GPU real.", "Incluye lectura completa del fotograma para sincronizar.", "No mide presentación, DirectComposition, latencia de entrada ni consumo.", "No sirve para verificar umbrales de FPS: solo para comparar versiones y detectar regresiones gruesas." },
        };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(jsonPath))!);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return $"paint p50={report.PaintMs.P50} ms p95={report.PaintMs.P95} ms; con lectura p50={report.PaintPlusReadbackMs.P50} ms; build fondo={report.BackdropBuildMs} ms; {dev.Description}";
    }

    private sealed class NoIconsBench : IIconSource { public Shell.IconPixels? Get(DockItemModel item) => null; }
}
