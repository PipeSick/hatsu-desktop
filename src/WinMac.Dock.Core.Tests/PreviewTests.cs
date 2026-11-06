using WinMac.Apps;
using WinMac.Dock;
using Xunit;

public class PreviewLayoutTests
{
    [Fact]
    public void Panel_sits_above_the_icon_with_the_tail_pointing_at_it()
    {
        var f = PreviewSpec.Compute([1.6], anchorX: 600, iconTopY: 400, windowWidth: 1300, maxPanelWidth: PreviewSpec.MaxPanelWidth);
        Assert.True(f.Panel.Bottom < 400 - PreviewSpec.TailH);
        Assert.Equal(f.Panel.Bottom, f.TailBaseY, 6); Assert.Equal(f.TailBaseY + PreviewSpec.TailH, f.TailTipY, 6);
        Assert.Equal(600, f.TailX, 6); Assert.Equal(600, f.Panel.X + f.Panel.W / 2, 3);   // centrado sobre el ícono
        Assert.Single(f.Cards); Assert.True(f.Panel.Contains(f.Cards[0].Thumb.X, f.Cards[0].Thumb.Y) && f.Panel.Contains(f.Cards[0].Thumb.Right, f.Cards[0].Thumb.Bottom));
    }

    [Theory]
    [InlineData(5.0)] [InlineData(1295.0)]
    public void Panel_is_clamped_inside_the_window_and_the_tail_stays_on_the_panel(double anchor)
    {
        var f = PreviewSpec.Compute([1.6, 1.6, 1.6], anchor, 400, 1300, PreviewSpec.MaxPanelWidth);
        Assert.True(f.Panel.X >= PreviewSpec.Margin - 1e-9 && f.Panel.Right <= 1300 - PreviewSpec.Margin + 1e-9);
        Assert.True(f.TailX >= f.Panel.X && f.TailX <= f.Panel.Right);
    }

    [Fact]
    public void Cards_do_not_overlap_and_thumbnails_keep_their_aspect()
    {
        var f = PreviewSpec.Compute([1.6, 0.6, 3.0], 600, 400, 1300, PreviewSpec.MaxPanelWidth);
        for (int i = 1; i < f.Cards.Length; i++) Assert.True(f.Cards[i].Thumb.X >= f.Cards[i - 1].Thumb.Right + PreviewSpec.CardGap - 1e-9);
        double[] a = [1.6, 0.6, 3.0];
        for (int i = 0; i < 3; i++) { var t = f.Cards[i].Thumb; Assert.True(t.W <= PreviewSpec.ThumbMaxW + 1e-9 && t.H <= PreviewSpec.ThumbMaxH + 1e-9); if (t.W > PreviewSpec.ThumbMinW + 1e-6 && a[i] is > 0.8 and < 1.6) Assert.Equal(a[i], t.W / t.H, 3); }
    }

    [Fact]
    public void Too_many_windows_for_a_narrow_screen_shrink_the_cards_instead_of_overflowing()
    {
        var f = PreviewSpec.Compute([1.6, 1.6, 1.6, 1.6, 1.6], 400, 400, 700, 660);
        Assert.True(f.Panel.W <= 660 + 1e-6); Assert.True(f.Cards[^1].Thumb.Right <= f.Panel.Right - PreviewSpec.Pad + 1e-6);
    }

    [Fact]
    public void Garbage_aspects_fall_back_to_the_default()
    {
        var f = PreviewSpec.Compute([double.NaN, -1, 0, 1000], 600, 400, 1300, PreviewSpec.MaxPanelWidth);
        Assert.All(f.Cards, c => Assert.True(c.Thumb.W > 0 && c.Thumb.H > 0 && !double.IsNaN(c.Thumb.W)));
    }

    [Fact]
    public void Enabling_previews_makes_room_above_even_the_fully_magnified_icon_and_keeps_the_old_size_when_disabled()
    {
        var plain = new DockMetrics(); var m = plain with { PreviewHeadroom = PreviewSpec.Headroom(plain), PreviewMaxWidth = PreviewSpec.MaxPanelWidth };
        Assert.Equal(plain.WindowHeight + m.PreviewHeadroom, m.WindowHeight, 6);
        double barBottom = m.WindowHeight - m.ShadowMargin, tallestIconTop = barBottom - m.PadBottom - m.BaseSize * m.MaxScale - 8;   // + elevación de arrastre/salto
        var f = PreviewSpec.Compute([1.6], 500, tallestIconTop, 1300, PreviewSpec.MaxPanelWidth);
        Assert.True(f.Panel.Y >= 0, $"el panel se sale por arriba: y={f.Panel.Y}");
        var items = DemoData.Items(); Assert.True(DockLayout.WindowWidth(m, items) >= PreviewSpec.MaxPanelWidth);
        Assert.Equal(DockLayout.WindowWidth(plain, items), DockLayout.MaxBarWidth(plain, items) + 2 * plain.ShadowMargin + 8, 6);   // sin vista previa, igual que antes
    }
}

public class PreviewControllerTests
{
    private static AppIdentity A(string n) => AppIdentity.FromExe($@"C:\apps\{n}.exe");
    private static DockItemModel App(string n, int windows, bool pinned = false) => new("app:" + A(n).Id, DockItemKind.App, n, null, pinned, windows > 0, windows, false, false, A(n),
        Enumerable.Range(0, windows).Select(i => new WindowInfo(1000 + i + n[0] * 10, $"{n} {i}", i == 1, false)).ToList());
    private static (DockController c, DockMetrics m) Make(DockConfig? cfg = null, params DockItemModel[] apps)
    {
        var m0 = new DockMetrics(); var m = m0 with { PreviewHeadroom = PreviewSpec.Headroom(m0), PreviewMaxWidth = PreviewSpec.MaxPanelWidth };
        var c = new DockController(m, cfg ?? new DockConfig());
        var items = new List<DockItemModel>(apps) { new("sep:system", DockItemKind.Separator, "", null), new("trash", DockItemKind.Trash, "Papelera", null) };
        c.SetItems(items); for (int i = 0; i < 120; i++) c.Step(1.0 / 60); return (c, m);
    }
    private static void Hover(DockController c, DockMetrics m, int index)
    {
        var v = c.Step(0); var it = v.Items[index]; c.SetPointer(new PointerSample(it.X + it.Size / 2, v.WindowHeight - m.ShadowMargin - 30));
    }
    private static DockView Run(DockController c, double seconds) { DockView v = c.Step(0); for (double t = 0; t < seconds; t += 1.0 / 60) v = c.Step(1.0 / 60); return v; }

    [Fact]
    public void Preview_appears_only_after_the_hover_delay_and_only_for_apps_with_windows()
    {
        var (c, m) = Make(null, App("a", 2), App("b", 0, pinned: true)); Hover(c, m, 0);
        Assert.Null(Run(c, 0.2).Preview); var v = Run(c, 0.4); Assert.NotNull(v.Preview); Assert.Equal(2, v.Preview!.Windows.Count); Assert.Equal(1.0, v.Preview.Alpha, 3);
        var (c2, m2) = Make(null, App("a", 2), App("b", 0, pinned: true)); Hover(c2, m2, 1); Assert.Null(Run(c2, 1).Preview);   // app sin ventanas
    }

    [Fact]
    public void Moving_to_another_app_while_open_switches_immediately_and_leaving_hides_after_the_grace()
    {
        var (c, m) = Make(null, App("a", 1), App("b", 2)); Hover(c, m, 0); Run(c, 0.6);
        Hover(c, m, 1); var v = Run(c, 0.05); Assert.Equal(A("b").Id, v.Preview?.Item.Identity?.Id);
        c.SetPointer(new PointerSample(5, 5)); Assert.NotNull(Run(c, 0.1).Preview);   // dentro del margen de gracia
        Assert.Null(Run(c, 0.6).Preview);
    }

    [Fact]
    public void Pointer_travelling_up_to_the_panel_keeps_it_open_and_hovers_the_card()
    {
        var (c, m) = Make(null, App("a", 2)); Hover(c, m, 0); var v = Run(c, 0.6); var card = v.Preview!.Frame.Cards[1].Thumb;
        c.SetPointer(new PointerSample(card.X + card.W / 2, card.Y + card.H / 2)); v = Run(c, 1.0);
        Assert.NotNull(v.Preview); Assert.Equal(1, v.Preview!.HoverCard);
    }

    [Fact]
    public void Clicking_a_card_activates_that_window_and_the_preview_does_not_reopen_until_the_pointer_leaves()
    {
        var (c, m) = Make(null, App("a", 2)); Hover(c, m, 0); var v = Run(c, 0.6); var card = v.Preview!.Frame.Cards[1].Thumb;
        c.SetPointer(new PointerSample(card.X + 5, card.Y + 5)); Run(c, 0.2);
        var act = c.PreviewClick(); Assert.NotNull(act); Assert.Equal(DockActionKind.ActivateWindow, act!.Kind); Assert.Equal(v.Preview.Windows[1].Hwnd, act.Hwnd);
        Assert.Null(Run(c, 1.0).Preview);                                          // el puntero sigue donde estaba la tarjeta: no reaparece
        Hover(c, m, 0); Assert.Null(Run(c, 0.1).Preview); Assert.NotNull(Run(c, 0.6).Preview);   // al volver al ícono reaparece tras el retardo
    }

    [Fact]
    public void A_click_outside_cards_or_on_a_fading_preview_does_nothing()
    {
        var (c, m) = Make(null, App("a", 1)); Hover(c, m, 0); Run(c, 0.4); Assert.Null(c.PreviewClick());   // el puntero está en la barra, no sobre una tarjeta
    }

    [Fact]
    public void Previews_respect_the_config_switch_the_missing_headroom_and_dragging()
    {
        var (off, mo) = Make(new DockConfig { PreviewsEnabled = false }, App("a", 2)); Hover(off, mo, 0); Assert.Null(Run(off, 1).Preview);
        var (c, m) = Make(null, App("a", 2), App("b", 1)); Hover(c, m, 0); Run(c, 0.6); Assert.NotNull(c.Preview);
        Assert.True(c.BeginDrag(0)); Assert.Null(Run(c, 0.3).Preview);
    }
}

public class TemporaryDragTests
{
    private static DockItemModel Pin(string n, bool running = false) => new("app:" + n, DockItemKind.App, n, null, true, running, running ? 1 : 0, false, false, AppIdentity.FromExe($@"C:\{n}.exe"));
    private static DockItemModel Tmp(string n) => new("app:" + n, DockItemKind.App, n, null, false, true, 1, false, false, AppIdentity.FromExe($@"C:\{n}.exe"));
    private static DockController Make()
    {
        var c = new DockController(new DockMetrics(), new DockConfig()); c.SetItems([Pin("A"), Pin("B"), new("sep:recents", DockItemKind.Separator, "", null), Tmp("X"), Tmp("Y"), Tmp("Z"), new("sep:system", DockItemKind.Separator, "", null), new("trash", DockItemKind.Trash, "", null)]);
        for (int i = 0; i < 60; i++) c.Step(1.0 / 60); return c;
    }

    [Fact]
    public void Temporary_apps_can_be_dragged_but_stopped_or_unpinned_ones_cannot()
    {
        var c = Make(); Assert.True(c.CanDrag(0)); Assert.True(c.CanDrag(3)); Assert.False(c.CanDrag(2)); Assert.False(c.CanDrag(7));
        c.SetItems([Pin("A"), Tmp("X").with_(false)]); Assert.False(c.CanDrag(1));
    }

    [Fact]
    public void Dragging_a_temporary_app_reorders_only_among_temporaries_and_reports_it()
    {
        var c = Make(); var v = c.Step(0); Assert.True(c.BeginDrag(3));
        c.UpdateDrag(v.Items[5].X + v.Items[5].Size + 4);                           // más allá de Z
        var order = c.EndDrag(true); Assert.True(c.LastDropTemporary);
        Assert.Equal(["exe:C:\\Y.EXE", "exe:C:\\Z.EXE", "exe:C:\\X.EXE"], order);
        Assert.Equal(["A", "B"], c.Items.Take(2).Select(i => i.Title));            // las ancladas no se movieron
    }

    [Fact]
    public void A_temporary_app_cannot_be_dropped_among_the_pinned_ones_and_vice_versa()
    {
        var c = Make(); var v = c.Step(0); c.BeginDrag(4); c.UpdateDrag(v.Items[0].X - 50); c.EndDrag(true);
        Assert.Equal(["A", "B"], c.Items.Take(2).Select(i => i.Title)); Assert.Equal("sep:recents", c.Items[2].Id); Assert.Equal("Y", c.Items[3].Title);   // se quedó la primera de las temporales
        var d = Make(); var w = d.Step(0); d.BeginDrag(0); d.UpdateDrag(w.Items[5].X + 300); d.EndDrag(true);
        Assert.False(d.LastDropTemporary); Assert.Equal(["B", "A"], d.Items.Take(2).Select(i => i.Title)); Assert.Equal("sep:recents", d.Items[2].Id);
    }

    [Fact]
    public void Builder_remembers_a_manual_order_of_temporaries_only_while_they_stay_open()
    {
        static RunningApp R(string n) => new(AppIdentity.FromExe($@"C:\{n}.exe"), 1, [new WindowInfo(1, n, false, false)]);
        var b = new DockModelBuilder(new DockConfig()); var all = new[] { R("X"), R("Y"), R("Z") };
        Assert.Equal(["X", "Y", "Z"], b.Build(all).Where(i => i.Kind == DockItemKind.App).Select(i => i.Title));
        b.SetRecentOrder(["exe:C:\\Z.EXE", "exe:C:\\X.EXE", "exe:C:\\Y.EXE", "exe:C:\\NOPE.EXE"]);
        Assert.Equal(["Z", "X", "Y"], b.Build(all).Where(i => i.Kind == DockItemKind.App).Select(i => i.Title));
        b.Build([R("X"), R("Y")]);                                                    // Z se cerró: se olvida
        Assert.Equal(["X", "Y", "Z"], b.Build(all).Where(i => i.Kind == DockItemKind.App).Select(i => i.Title));
    }
}

internal static class TestExt { public static DockItemModel with_(this DockItemModel m, bool running) => m with { Running = running, Windows = null, WindowCount = 0 }; }

public class PreviewCloseButtonTests
{
    [Fact] public void Every_card_has_a_close_button_inside_its_thumbnail_and_inside_the_panel()
    {
        var f = PreviewSpec.Compute([1.6, 1.0, 1.8], 400, 600, 1200, 1000);
        foreach (var c in f.Cards)
        {
            Assert.True(c.Close.W == PreviewSpec.CloseSize && c.Close.X >= c.Thumb.X && c.Close.Right <= c.Thumb.Right && c.Close.Y >= c.Thumb.Y && c.Close.Bottom <= c.Thumb.Bottom);
            Assert.True(c.Close.X >= f.Panel.X && c.Close.Right <= f.Panel.Right);
        }
    }
}
