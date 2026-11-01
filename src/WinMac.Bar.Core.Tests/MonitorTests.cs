using Xunit;
using WinMac.Bar;

namespace WinMac.Bar.Core.Tests;

public class MonitorPlanTests
{
    private static MonitorInfo M(string id, int x, int y, int w, int h, double scale = 1, bool primary = false) => new(id, new PxRect(x, y, w, h), new PxRect(x, y, w, h - 40), scale, primary);

    private static readonly MonitorInfo Left = M("LEFT", 0, 0, 2560, 1440, 1, true), Right = M("RIGHT", 2560, 0, 2560, 1440);

    [Fact]
    public void Extended_mode_gives_one_bar_per_monitor_with_the_primary_first()
    {
        var plan = MonitorPlan.Unique([Right, Left]);
        Assert.Equal(["LEFT", "RIGHT"], plan.Select(m => m.Id));
        // el principal puede estar a la derecha: sigue yendo primero, y el resto de izquierda a derecha
        var plan2 = MonitorPlan.Unique([M("A", -1920, 0, 1920, 1080), M("B", 0, 0, 2560, 1440, 1, true), M("C", 2560, 0, 1920, 1080)]);
        Assert.Equal(["B", "A", "C"], plan2.Select(m => m.Id));
    }

    [Fact]
    public void Mirror_mode_never_duplicates_the_bar()
    {
        var mirrored = M("RIGHT", 0, 0, 2560, 1440);   // mismos límites que el principal: la misma imagen en otra salida
        var plan = MonitorPlan.Unique([mirrored, Left]);
        Assert.Single(plan); Assert.Equal("LEFT", plan[0].Id);   // se queda el principal aunque llegue después
        Assert.Single(MonitorPlan.Unique([M("X", 0, 0, 1920, 1080), M("Y", 0, 0, 1920, 1080)]));   // sin principal marcado: queda uno igualmente
        Assert.Empty(MonitorPlan.Unique([]));
        Assert.Empty(MonitorPlan.Unique([M("VACIO", 0, 0, 0, 0)]));   // un monitor sin área no lleva barra
    }

    [Fact]
    public void Ids_that_collide_are_made_distinct_and_the_monitor_path_wins_over_the_output_name()
    {
        Assert.Equal(["a", "a#2", "b", "a#3"], MonitorPlan.Disambiguate(["a", "a", "b", "a"]));
        Assert.Equal(["x"], MonitorPlan.Disambiguate(["x"]));
        Assert.Equal(@"MONITOR\GSM5B09\{guid}\0001", MonitorPlan.MakeId(@"MONITOR\GSM5B09\{guid}\0001", @"\\.\DISPLAY2"));   // sobrevive a que Windows renumere las salidas
        Assert.Equal(@"\\.\DISPLAY2", MonitorPlan.MakeId(null, @"\\.\DISPLAY2")); Assert.Equal(@"\\.\DISPLAY2", MonitorPlan.MakeId("  ", @"\\.\DISPLAY2"));
    }

    [Fact]
    public void The_signature_changes_with_resolution_position_scale_and_primary_but_not_with_the_work_area_or_listing_order()
    {
        string s = MonitorPlan.Signature([Left, Right]);
        Assert.Equal(s, MonitorPlan.Signature([Right, Left]));
        Assert.Equal(s, MonitorPlan.Signature([Left with { Work = new PxRect(0, 28, 2560, 1380) }, Right]));   // la barra reserva su espacio y cambia el área de trabajo: no puede provocar un bucle
        Assert.NotEqual(s, MonitorPlan.Signature([Left with { Bounds = new PxRect(0, 0, 1920, 1080) }, Right]));
        Assert.NotEqual(s, MonitorPlan.Signature([Left, Right with { Bounds = new PxRect(2560, 100, 2560, 1440) }]));
        Assert.NotEqual(s, MonitorPlan.Signature([Left, Right with { Scale = 1.5 }]));
        Assert.NotEqual(s, MonitorPlan.Signature([Left with { Primary = false }, Right with { Primary = true }]));
    }

    private static Dictionary<string, MonitorInfo> Cur(params MonitorInfo[] ms) => ms.ToDictionary(m => m.Id);

    [Fact]
    public void Switching_between_extend_and_duplicate_adds_and_releases_only_the_second_bar()
    {
        var toExtend = MonitorPlan.Diff(MonitorPlan.Unique([Left, Right]), Cur(Left));
        Assert.Equal(["RIGHT"], toExtend.Add.Select(m => m.Id)); Assert.Empty(toExtend.Remove); Assert.Empty(toExtend.Update);
        var toMirror = MonitorPlan.Diff(MonitorPlan.Unique([Left, Right with { Bounds = Left.Bounds }]), Cur(Left, Right));
        Assert.Equal(["RIGHT"], toMirror.Remove); Assert.Empty(toMirror.Add); Assert.Empty(toMirror.Update);   // la barra del principal NO se toca
    }

    [Fact]
    public void Unplugging_a_monitor_releases_its_bar_and_a_dpi_or_resolution_change_relocates_without_recreating()
    {
        var unplug = MonitorPlan.Diff(MonitorPlan.Unique([Left]), Cur(Left, Right)); Assert.Equal(["RIGHT"], unplug.Remove);
        var dpi = MonitorPlan.Diff([Left, Right with { Scale = 1.5 }], Cur(Left, Right)); Assert.Equal(["RIGHT"], dpi.Update.Select(m => m.Id)); Assert.Empty(dpi.Add); Assert.Empty(dpi.Remove);
        var res = MonitorPlan.Diff([Left with { Bounds = new PxRect(0, 0, 1920, 1080) }, Right], Cur(Left, Right)); Assert.Equal(["LEFT"], res.Update.Select(m => m.Id));
        var swap = MonitorPlan.Diff([Left with { Primary = false }, Right with { Primary = true }], Cur(Left, Right)); Assert.Equal(2, swap.Update.Count);
        Assert.True(MonitorPlan.Diff([Left, Right], Cur(Left, Right)).IsEmpty);
        Assert.True(MonitorPlan.Diff([Left with { Work = new PxRect(0, 28, 2560, 1380) }, Right], Cur(Left, Right)).IsEmpty);   // el área de trabajo no cuenta
    }

    [Fact]
    public void Replacing_a_monitor_by_another_model_releases_one_bar_and_creates_the_other()
    {
        var d = MonitorPlan.Diff([Left, M("NUEVO", 2560, 0, 1920, 1080)], Cur(Left, Right));
        Assert.Equal(["NUEVO"], d.Add.Select(m => m.Id)); Assert.Equal(["RIGHT"], d.Remove);
    }
}

public class TopologyWatcherTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(double ms) => T0.AddMilliseconds(ms);
    private static TopologyWatcher Make() => new(TimeSpan.FromMilliseconds(500));

    [Fact]
    public void A_change_is_applied_only_after_windows_has_settled()
    {
        var w = Make(); w.MarkApplied("1 monitor", T0);
        w.Notify(At(100));
        Assert.False(w.ShouldReconcile("2 monitores", At(150)));    // Windows avisa antes de terminar
        Assert.False(w.ShouldReconcile("2 monitores", At(400)));    // aún no pasó el tiempo de asentamiento desde el aviso
        Assert.True(w.ShouldReconcile("2 monitores", At(700)));
    }

    [Fact]
    public void A_flapping_layout_restarts_the_wait()
    {
        var w = Make(); w.MarkApplied("A", T0); w.Notify(At(0));
        Assert.False(w.ShouldReconcile("B", At(100)));
        Assert.False(w.ShouldReconcile("C", At(550)));   // cambió otra vez: hay que esperar de nuevo
        Assert.False(w.ShouldReconcile("C", At(900)));
        Assert.True(w.ShouldReconcile("C", At(1100)));
    }

    [Fact]
    public void A_missed_notification_is_still_detected_by_polling()
    {
        var w = Make(); w.MarkApplied("A", T0);
        Assert.False(w.ShouldReconcile("A", At(1000)));
        Assert.False(w.ShouldReconcile("B", At(1500)));   // aparece una huella distinta sin aviso
        Assert.True(w.ShouldReconcile("B", At(2100)));
    }

    [Fact]
    public void A_notification_that_changes_nothing_relevant_does_not_reconcile_and_after_applying_it_stays_quiet()
    {
        var w = Make(); w.MarkApplied("A", T0); w.Notify(At(10));
        Assert.False(w.ShouldReconcile("A", At(100))); Assert.False(w.ShouldReconcile("A", At(900)));
        Assert.True(w.ShouldReconcile("B", At(1500)) || w.ShouldReconcile("B", At(2200)));
        w.MarkApplied("B", At(2300)); Assert.False(w.ShouldReconcile("B", At(3000)));
    }

    [Fact]
    public void The_first_reconciliation_happens_without_a_previous_applied_signature()
    {
        var w = Make(); Assert.False(w.ShouldReconcile("A", At(0))); Assert.True(w.ShouldReconcile("A", At(600)));
    }
}

public class PopoverPlacementTests
{
    private static readonly PxRect Main = new(0, 0, 2560, 1440), Second = new(2560, 0, 1920, 1080), LeftOfMain = new(-1920, 0, 1920, 1080);

    [Fact]
    public void The_control_center_sticks_to_the_right_edge_of_the_monitor_where_the_click_happened()
    {
        var (x1, _) = PopoverPlacement.Place(2400, 28, 320, 400, Main, 1, true); Assert.Equal(2560 - 320 - 8, x1);
        var (x2, _) = PopoverPlacement.Place(4300, 28, 320, 400, Second, 1, true); Assert.Equal(4480 - 320 - 8, x2);   // en el segundo monitor, no en el principal
        Assert.InRange(x2, Second.X, Second.Right);
    }

    [Fact]
    public void A_regular_panel_is_centered_under_its_icon_but_never_leaves_the_monitor()
    {
        Assert.Equal(1000 - 160, PopoverPlacement.Place(1000, 28, 320, 300, Main, 1, false).X);
        Assert.Equal(Main.X + 8, PopoverPlacement.Place(5, 28, 320, 300, Main, 1, false).X);                  // icono pegado a la izquierda
        Assert.Equal(Second.X + 8, PopoverPlacement.Place(2570, 28, 320, 300, Second, 1, false).X);           // no invade el monitor vecino
        Assert.Equal(Second.Right - 320 - 8, PopoverPlacement.Place(4479, 28, 320, 300, Second, 1, false).X);
        Assert.Equal(LeftOfMain.X + 8, PopoverPlacement.Place(-1915, 28, 320, 300, LeftOfMain, 1, false).X);  // monitores a la izquierda del principal tienen coordenadas negativas
    }

    [Fact]
    public void The_panel_always_fits_inside_the_monitor_for_any_anchor_scale_and_size()
    {
        foreach (var mon in new[] { Main, Second, LeftOfMain })
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                foreach (int anchor in new[] { mon.X - 500, mon.X, mon.X + 3, mon.X + mon.W / 2, mon.Right - 3, mon.Right, mon.Right + 500 })
                    foreach (bool right in new[] { false, true })
                    {
                        int w = (int)(320 * scale), h = (int)(500 * scale), barBottom = mon.Y + (int)(28 * scale);
                        var (x, y) = PopoverPlacement.Place(anchor, barBottom, w, h, mon, scale, right);
                        Assert.True(x >= mon.X && x + w <= mon.Right, $"x fuera: {x} (monitor {mon}, ancla {anchor}, escala {scale})");
                        Assert.True(y >= mon.Y && y + h <= mon.Bottom, $"y fuera: {y} (monitor {mon})");
                    }
    }

    [Fact]
    public void A_panel_taller_than_the_free_space_moves_up_but_not_above_the_monitor_and_the_margin_scales_with_dpi()
    {
        var (_, y) = PopoverPlacement.Place(1000, 28, 320, 1200, new PxRect(0, 0, 1920, 1080), 1, false);
        Assert.Equal(0, y);   // no cabe: queda pegado arriba (nunca por encima del monitor)
        Assert.Equal(28 + 4, PopoverPlacement.Place(1000, 28, 320, 400, Main, 1, false).Y);
        Assert.Equal(42 + 6, PopoverPlacement.Place(1000, 42, 480, 400, Main, 1.5, false).Y);   // separación de 4 DIP a 150 % = 6 px
        Assert.Equal(2560 - 640 - 16, PopoverPlacement.Place(2400, 56, 640, 800, Main, 2, true).X);
    }

    [Fact]
    public void A_monitor_narrower_than_the_panel_does_not_produce_an_invalid_position()
    {
        var (x, _) = PopoverPlacement.Place(100, 28, 400, 300, new PxRect(0, 0, 300, 600), 1, false);
        Assert.Equal(8, x);   // el panel no cabe a lo ancho: se pega al borde, sin lanzar
    }
}

public class PerMonitorYieldTests
{
    // Dos monitores en extendido; la Isla está en el segundo (como cuando se mueve entre pantallas).
    private static readonly PxRect BarMain = new(0, 0, 2560, 28), BarSecond = new(2560, 0, 1920, 28);

    [Fact]
    public void A_notch_footprint_on_one_monitor_never_cuts_the_bar_of_the_other()
    {
        var notchOnSecond = new PxRect(2560 + 960 - 150, 0, 300, 40);
        Assert.False(YieldRegions.Touches(BarMain, [notchOnSecond]));
        Assert.True(YieldRegions.Touches(BarSecond, [notchOnSecond]));
        Assert.Equal([BarMain], YieldRegions.Visible(BarMain, [notchOnSecond]));   // intacta: ni un recorte prestado
        var cut = YieldRegions.Visible(BarSecond, [notchOnSecond]);
        Assert.Equal(BarSecond.W * BarSecond.H - 300 * 28, cut.Sum(r => (long)r.W * r.H));
        Assert.All(cut, r => Assert.True(r.X >= BarSecond.X && r.Right <= BarSecond.Right));
    }

    [Fact]
    public void Each_monitor_with_its_own_notch_gets_only_its_own_cut()
    {
        var holes = new[] { new PxRect(1130, 0, 300, 40), new PxRect(2560 + 810, 0, 300, 40) };
        Assert.Equal(BarMain.W * BarMain.H - 300 * 28, YieldRegions.Visible(BarMain, holes).Sum(r => (long)r.W * r.H));
        Assert.Equal(BarSecond.W * BarSecond.H - 300 * 28, YieldRegions.Visible(BarSecond, holes).Sum(r => (long)r.W * r.H));
    }

    [Fact]
    public void A_footprint_that_only_touches_the_shared_edge_between_monitors_cuts_nothing()
    {
        Assert.False(YieldRegions.Touches(BarSecond, [new PxRect(2260, 0, 300, 40)]));   // termina justo donde empieza el segundo monitor
        Assert.False(YieldRegions.Touches(BarMain, [new PxRect(2560, 0, 300, 40)]));       // empieza justo donde termina el principal
    }

    [Fact]
    public void Losing_the_notch_gives_every_bar_its_full_window_back()
    {
        var holes = new[] { new PxRect(2560 + 810, 0, 300, 40) };
        Assert.True(YieldRegions.Touches(BarSecond, holes));
        IReadOnlyList<PxRect> none = YieldRegions.Parse(System.Text.Json.JsonDocument.Parse("{\"rects\":[]}").RootElement);   // lo que publica el host cuando la Isla se cierra o falla
        Assert.False(YieldRegions.Touches(BarSecond, none)); Assert.False(YieldRegions.Touches(BarMain, none));
        Assert.Equal([BarSecond], YieldRegions.Visible(BarSecond, none));
    }

    [Fact]
    public void The_cut_follows_the_notch_when_the_monitor_layout_changes()
    {
        var wasSecond = new[] { new PxRect(2560 + 810, 0, 300, 40) };
        var nowMirrored = new PxRect(0, 0, 2560, 28);   // tras duplicar, queda una sola barra en el monitor principal
        Assert.False(YieldRegions.Touches(nowMirrored, wasSecond));   // un hueco viejo de otro monitor no deja un recorte fantasma
    }
}
