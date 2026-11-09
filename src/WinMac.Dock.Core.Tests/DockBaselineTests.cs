using WinMac.Dock;
using Xunit;

/// <summary>
/// WM-043 (iteración 6, corrección): el Dock se apoya en el borde FÍSICO del monitor cuando no hay barra de tareas visible, aunque Explorer siga reservando su franja
/// en el área de trabajo (la captura mostraba ~64 px de hueco bajo el Dock). Pruebas puras: DPI, monitores y barra de tareas se pasan como datos.
/// </summary>
public class DockBaselineTests
{
    private static readonly DockMetrics M = DockMetrics.ForIconSize(48);

    private static (int Bar, int Gap) BarBottomAndGap(PxRect mon, PxRect work, int? taskbarTop, double scale, DockConfig? cfg = null)
    {
        cfg ??= new DockConfig(); int winW = (int)Math.Ceiling(1100 * scale), winH = (int)Math.Ceiling((M.BarHeight + 2 * M.ShadowMargin + 80) * scale);
        int baseBottom = DockSettings.Baseline(mon, work, taskbarTop);
        var (_, top) = DockSettings.Place(mon, new PxRect(work.Left, work.Top, work.Right, baseBottom), winW, winH, scale, M, cfg, freeEdge: taskbarTop is null);
        int barBottom = top + winH - (int)Math.Round(M.ShadowMargin * scale);   // la barra termina ShadowMargin por encima del borde inferior de la ventana
        return (barBottom, mon.Bottom - barBottom);
    }

    [Theory]
    [InlineData(0)] [InlineData(48)] [InlineData(64)] [InlineData(96)]
    public void Reserved_but_hidden_taskbar_strip_does_not_separate_the_dock_from_the_physical_edge(int ghostStrip)
    {
        var mon = new PxRect(0, 0, 1920, 1080); var work = new PxRect(0, 0, 1920, 1080 - ghostStrip);
        var (_, gap) = BarBottomAndGap(mon, work, taskbarTop: null, scale: 1.0);
        Assert.Equal((int)Math.Round(DockSettings.FreeEdgeGapDip), gap);   // la misma separación con cualquier franja fantasma
    }

    [Theory]
    [InlineData(1.0)] [InlineData(1.25)] [InlineData(4.0 / 3)] [InlineData(1.5)] [InlineData(1.75)] [InlineData(2.0)] [InlineData(2.5)]
    public void Gap_to_the_physical_edge_scales_with_dpi_and_is_small(double scale)
    {
        int h = (int)Math.Round(1080 * scale); var mon = new PxRect(0, 0, (int)(1920 * scale), h); var work = new PxRect(0, 0, mon.Right, h - (int)(48 * scale));
        var (_, gap) = BarBottomAndGap(mon, work, null, scale);
        Assert.InRange(gap, (int)(DockSettings.FreeEdgeGapDip * scale) - 1, (int)Math.Ceiling(DockSettings.FreeEdgeGapDip * scale) + 1);
        Assert.True(gap < 16 * scale, $"separación {gap} px a {scale:0.##}x");
    }

    [Fact]
    public void Visible_taskbar_keeps_the_dock_above_it_exactly_as_before()
    {
        var mon = new PxRect(0, 0, 1920, 1080); var work = new PxRect(0, 0, 1920, 1032);
        Assert.Equal(1032, DockSettings.Baseline(mon, work, 1032));
        var (_, withTaskbar) = DockSettings.Place(mon, work, 1120, 120, 1, M, new DockConfig());
        var (_, viaBaseline) = DockSettings.Place(mon, new PxRect(0, 0, 1920, DockSettings.Baseline(mon, work, 1032)), 1120, 120, 1, M, new DockConfig());
        Assert.Equal(withTaskbar, viaBaseline);   // con barra visible: la posición de siempre
    }

    [Fact]
    public void Another_appbar_above_a_visible_taskbar_is_still_respected()
    {
        var mon = new PxRect(0, 0, 1920, 1080); var work = new PxRect(0, 0, 1920, 1000);   // barra (48) + otra AppBar (32)
        Assert.Equal(1000, DockSettings.Baseline(mon, work, 1032));
    }

    [Fact]
    public void Baseline_is_clamped_to_the_monitor()
    {
        var mon = new PxRect(0, 0, 1920, 1080);
        Assert.Equal(1080, DockSettings.Baseline(mon, new PxRect(0, 0, 1920, 1200), 1500));
        Assert.Equal(0, DockSettings.Baseline(mon, new PxRect(0, 0, 1920, -5), 0));
    }

    [Theory]
    [InlineData(1032, 1080, true, 1032)]      // barra de 48 px mostrada
    [InlineData(1078, 1080, true, null)]      // autoocultada: asoma 2 px
    [InlineData(1074, 1080, true, null)]      // 6 px: aún franja
    [InlineData(1073, 1080, true, 1073)]      // 7 px: ya es una barra
    [InlineData(1032, 1080, false, null)]     // ventana oculta por WinMac
    public void Taskbar_visibility_rule(int top, int bottomOfMonitor, bool windowVisible, int? expected)
    {
        var mon = new PxRect(0, 0, 1920, bottomOfMonitor);
        Assert.Equal(expected, DockSettings.VisibleTaskbarTop(mon, 0, top, 1920, bottomOfMonitor + 0, windowVisible));
    }

    [Fact]
    public void A_taskbar_of_another_monitor_or_edge_does_not_count()
    {
        var mon = new PxRect(1920, 0, 3840, 1080);
        Assert.Null(DockSettings.VisibleTaskbarTop(mon, 0, 1032, 1920, 1080, true));            // la barra del monitor izquierdo
        Assert.Null(DockSettings.VisibleTaskbarTop(mon, 1920, 0, 1968, 1080, true));            // barra en el borde izquierdo (vertical)
        Assert.Equal(1032, DockSettings.VisibleTaskbarTop(mon, 1920, 1032, 3840, 1080, true));  // la de su propio monitor
    }

    [Fact]
    public void Secondary_monitor_with_other_resolution_and_origin_gets_its_own_edge()
    {
        var mon = new PxRect(-2560, -360, 0, 1080); var work = new PxRect(-2560, -360, 0, 1080 - 60);   // a la izquierda y más alto que el principal
        int b = DockSettings.Baseline(mon, work, null); Assert.Equal(1080, b);
        var (left, top) = DockSettings.Place(mon, new PxRect(mon.Left, mon.Top, mon.Right, b), 1100, 200, 1.0, M, new DockConfig(), freeEdge: true);
        Assert.InRange(left, mon.Left, mon.Right - 1100); Assert.InRange(top, mon.Top, mon.Bottom);
    }

    [Fact]
    public void Monitors_stacked_vertically_each_use_their_own_bottom()
    {
        var upper = new PxRect(0, 0, 1920, 1080); var lower = new PxRect(0, 1080, 1920, 2160);
        Assert.Equal(1080, DockSettings.Baseline(upper, new PxRect(0, 0, 1920, 1032), null));
        Assert.Equal(2160, DockSettings.Baseline(lower, new PxRect(0, 1080, 1920, 2112), null));
    }

    [Fact]
    public void Bottom_offset_still_raises_the_dock_and_the_free_edge_never_leaves_the_screen_at_the_top()
    {
        var mon = new PxRect(0, 0, 1920, 1080);
        var (_, g0) = BarBottomAndGap(mon, mon, null, 1.5); var (_, g1) = BarBottomAndGap(mon, mon, null, 1.5, new DockConfig { BottomOffset = 40 });
        Assert.Equal(60, g1 - g0);   // 40 DIP × 1,5
        var (_, t) = DockSettings.Place(mon, mon, 1100, 5000, 1.0, M, new DockConfig { BottomOffset = 400 }, freeEdge: true); Assert.Equal(0, t);
    }

    [Fact]
    public void The_hot_strip_is_the_physical_edge_when_the_taskbar_is_not_visible_and_the_dock_window_overhangs_it()
    {
        var mon = new PxRect(0, 0, 1920, 1080); double s = 1.5; int winH = 300, winW = 1100;
        var (left, top) = DockSettings.Place(mon, mon, winW, winH, s, M, new DockConfig(), freeEdge: true);
        int winBottom = top + winH;   // sobresale por debajo del monitor
        Assert.True(winBottom > mon.Bottom);
        Assert.True(DockSettings.EdgeHot(left + winW / 2, mon.Bottom - 1, mon.Bottom, left, winW, winBottom, screenEdgeFree: true));
        Assert.False(DockSettings.EdgeHot(left + winW / 2, mon.Bottom - 40, mon.Bottom, left, winW, winBottom, screenEdgeFree: true));   // no se despierta a mitad de pantalla
        Assert.False(DockSettings.EdgeHot(left - 10, mon.Bottom - 1, mon.Bottom, left, winW, winBottom, screenEdgeFree: true));          // fuera de su ancho
    }

    [Fact]
    public void WM044_and_WM045_inputs_are_untouched_by_the_baseline()
    {
        // La base no cambia el área reservada por el Dock ni la política de la barra de Windows: solo dónde se apoya la ventana.
        var m = new DockConfig(); var band = DockSettings.ReservedBand(new PxRect(0, 0, 1920, 1080), 1080, 1.0, M, m);
        Assert.Equal(1080, band.Bottom); Assert.Equal(1080 - (int)Math.Ceiling(M.BarHeight + 6), band.Top);
    }
}
