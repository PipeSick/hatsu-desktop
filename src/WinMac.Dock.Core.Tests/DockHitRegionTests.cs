using WinMac.Dock;
using Xunit;

// Región de la ventana del Dock: fuera de ella el clic llega a la app de debajo (antes se perdía en toda la ventana transparente).
public class DockHitRegionTests
{
    private static readonly DockMetrics M = new();
    private static DockView View(double hideT = 0, double size = 60, double lift = 0) =>
        new([new ItemView(0, new DockItemModel("a", DockItemKind.App, "App", null), 200, size, size / 60, lift)], 1200, M.WindowHeight, 150, 300, M.BarHeight, hideT, -1, false, false);

    [Fact]
    public void At_rest_only_the_bar_and_its_shadow_take_clicks_and_the_rest_of_the_window_lets_them_through()
    {
        var r = Assert.Single(DockHitRegion.Compute(View(), M, hovering: false, dragging: false));
        Assert.Equal(150 - M.ShadowMargin, r.X); Assert.Equal(300 + 2 * M.ShadowMargin, r.W, 6); Assert.Equal(M.WindowHeight, r.Bottom, 6);
        Assert.Equal(M.BarHeight + M.ShadowMargin + M.BounceHeight, r.H, 6);   // barra, sombra y sitio del salto: nada más
        Assert.False(r.Contains(10, M.WindowHeight - 5));            // fuera de la barra, a la altura de la barra (p. ej. la barra de descargas de Steam)
        Assert.False(r.Contains(300, 5));                            // arriba del todo de la ventana (sitio de la vista previa)
    }

    [Fact]
    public void Hovering_reserves_the_magnification_and_label_room_and_a_drag_takes_the_whole_window()
    {
        var idle = Assert.Single(DockHitRegion.Compute(View(), M, false, false));
        var hover = Assert.Single(DockHitRegion.Compute(View(size: 102), M, true, false));
        Assert.True(hover.Y < idle.Y);
        Assert.True(M.WindowHeight - M.ShadowMargin - M.BarHeight - hover.Y >= M.BaseSize * (M.MaxScale - 1) + M.LabelHeight);
        Assert.Equal(new PRect(0, 0, 1200, M.WindowHeight), Assert.Single(DockHitRegion.Compute(View(), M, false, true)));
    }

    [Fact]
    public void Hiding_moves_the_region_down_with_the_dock_and_a_hidden_dock_takes_nothing()
    {
        var half = Assert.Single(DockHitRegion.Compute(View(hideT: 0.5), M, false, false)); var shown = Assert.Single(DockHitRegion.Compute(View(), M, false, false));
        Assert.Equal(shown.Y + 0.5 * M.HideDistance, half.Y, 6);
        Assert.Empty(DockHitRegion.Compute(View(hideT: 1), M, false, false));
    }
}

// Un Dock por pantalla.
public class DockFleetTests
{
    [Fact]
    public void Each_monitor_gets_one_dock_mirrors_and_running_ones_do_not_get_another()
    {
        (int, int, int, int) a = (0, 0, 2560, 1440), b = (2560, 0, 5120, 1440);
        Assert.Equal([a, b], DockFleet.ToStart([a, b, a], []));                         // el espejo (mismos límites) no lleva otro
        Assert.Equal([b], DockFleet.ToStart([a, b], [DockFleet.Key(a)]));                // ya hay uno en a
        Assert.Empty(DockFleet.ToStart([a, b], [DockFleet.Key(a), DockFleet.Key(b)]));
        Assert.Empty(DockFleet.ToStart([(0, 0, 0, 0)], []));                              // monitor sin tamaño (en mitad de un cambio)
        Assert.Equal("2560,0,5120,1440", DockFleet.Key(b));
    }
}
