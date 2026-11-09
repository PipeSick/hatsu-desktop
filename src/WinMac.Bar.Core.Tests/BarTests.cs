using WinMac.Bar;
using Xunit;

public class ClockTests
{
    private static readonly DateTime Fri = new(2026, 10, 31, 8, 5, 9);   // 2026-10-31 es sábado
    [Fact]
    public void Spanish_date_and_24h_time_are_deterministic()
    {
        var t = new DateTime(2026, 10, 30, 8, 5, 9);   // viernes
        Assert.Equal("vie 30 oct", ClockFormat.Date(t, "es")); Assert.Equal("08:05", ClockFormat.Time(t, new BarConfig()));
        Assert.Equal("vie 30 oct  08:05", ClockFormat.Format(t, new BarConfig()));
        Assert.Equal("sáb 31 oct", ClockFormat.Date(Fri, "es")); Assert.Equal("mié 1 ene", ClockFormat.Date(new DateTime(2025, 1, 1), "es"));
    }

    [Fact]
    public void English_and_12_hour_with_seconds()
    {
        var c = new BarConfig { Language = "en", Use24Hour = false, ShowSeconds = true };
        Assert.Equal("Sat Oct 31", ClockFormat.Date(Fri, "en")); Assert.Equal("8:05:09 AM", ClockFormat.Time(Fri, c));
        Assert.Equal("12:00:00 PM", ClockFormat.Time(new DateTime(2026, 1, 1, 12, 0, 0), c)); Assert.Equal("12:30:00 AM", ClockFormat.Time(new DateTime(2026, 1, 1, 0, 30, 0), c));
        Assert.Equal("1:00 p. m.", ClockFormat.Time(new DateTime(2026, 1, 1, 13, 0, 0), new BarConfig { Use24Hour = false }));
    }

    [Fact]
    public void Date_can_be_hidden()
    {
        Assert.Equal("08:05", ClockFormat.Format(Fri, new BarConfig { ShowDate = false }));
    }

    [Fact]
    public void Next_change_is_the_next_minute_or_second_and_never_zero()
    {
        Assert.Equal(TimeSpan.FromSeconds(51), ClockFormat.UntilNextChange(Fri, new BarConfig()));
        Assert.Equal(TimeSpan.FromSeconds(1) - TimeSpan.FromMilliseconds(250), ClockFormat.UntilNextChange(new DateTime(2026, 1, 1, 0, 0, 0, 250), new BarConfig { ShowSeconds = true }));
        Assert.True(ClockFormat.UntilNextChange(new DateTime(2026, 1, 1, 0, 0, 59).AddTicks(TimeSpan.TicksPerSecond - 1), new BarConfig()) >= TimeSpan.FromMilliseconds(20));
    }
}

public class StateTests
{
    [Theory]
    [InlineData(0, false, 0)] [InlineData(10, false, 1)] [InlineData(33, false, 1)] [InlineData(34, false, 2)] [InlineData(66, false, 2)] [InlineData(67, false, 3)] [InlineData(100, false, 3)] [InlineData(80, true, 0)]
    public void Volume_waves(int percent, bool muted, int waves) => Assert.Equal(waves, new VolumeInfo(true, percent, muted).Waves);

    [Fact]
    public void Unavailable_volume_has_no_waves_and_battery_flags_are_consistent()
    {
        Assert.Equal(0, VolumeInfo.None.Waves);
        Assert.True(new BatteryInfo(true, 15, false, false).Low); Assert.False(new BatteryInfo(true, 15, true, true).Low); Assert.False(new BatteryInfo(false, 5, false, false).Low);
        Assert.Equal(0.5, new BatteryInfo(true, 50, false, false).Fill); Assert.Equal(1, new BatteryInfo(true, 250, false, false).Fill);
    }
}

public class ModelTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 5, 0);
    private static BarState S(string? app = "Bloc de notas", BatteryInfo? b = null, VolumeInfo? v = null, NetworkInfo? n = null) =>
        new(Now, app, b ?? new BatteryInfo(true, 80, false, true), v ?? new VolumeInfo(true, 50, false), n ?? new NetworkInfo(NetKind.Wifi));

    [Fact]
    public void Items_follow_the_expected_order_and_hide_what_is_not_available()
    {
        var (l, r) = BarModel.Build(S(), new BarConfig());
        Assert.Equal([BarItemKind.WinMacMenu, BarItemKind.AppName], l.Select(i => i.Kind)); Assert.Equal("Bloc de notas", l[1].Text); Assert.True(l[1].Bold);
        Assert.Equal([BarItemKind.HubSystem, BarItemKind.HubNetwork, BarItemKind.HubDisks, BarItemKind.Weather, BarItemKind.Network, BarItemKind.Volume, BarItemKind.Battery, BarItemKind.ControlCenter, BarItemKind.Clock], r.Select(i => i.Kind)); Assert.Equal("80%", r[6].Text);   // + Centro de control (siempre que no se apague en los ajustes)
        var (l2, r2) = BarModel.Build(S(app: null, b: BatteryInfo.None, v: VolumeInfo.None, n: NetworkInfo.Unknown), new BarConfig());
        Assert.Single(l2); Assert.Equal([BarItemKind.HubSystem, BarItemKind.HubNetwork, BarItemKind.HubDisks, BarItemKind.Weather, BarItemKind.ControlCenter, BarItemKind.Clock], r2.Select(i => i.Kind));   // sin red/volumen/batería solo queda lo que tiene respaldo
        var (_, r3) = BarModel.Build(S(), new BarConfig { ShowNetwork = false, ShowVolume = false, ShowBatteryPercent = false });
        Assert.Equal([BarItemKind.HubSystem, BarItemKind.HubNetwork, BarItemKind.HubDisks, BarItemKind.Weather, BarItemKind.Battery, BarItemKind.ControlCenter, BarItemKind.Clock], r3.Select(i => i.Kind)); Assert.Equal("", r3[4].Text);
    }

    [Fact]
    public void Clicks_open_the_matching_settings_page_and_the_app_name_is_inert()
    {
        Assert.Equal("ms-settings:network", BarModel.ActionFor(BarItemKind.Network).Uri); Assert.Equal("ms-settings:sound", BarModel.ActionFor(BarItemKind.Volume).Uri);
        Assert.Equal("ms-settings:batterysaver", BarModel.ActionFor(BarItemKind.Battery).Uri); Assert.Equal("ms-settings:dateandtime", BarModel.ActionFor(BarItemKind.Clock).Uri);
        Assert.Equal(BarActionKind.ShowMenu, BarModel.ActionFor(BarItemKind.WinMacMenu).Kind); Assert.False(BarModel.IsClickable(BarItemKind.AppName));
    }
}

public class LayoutTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 8, 5, 0);
    private static BarFrame Frame(double width, string app = "Bloc de notas", BarConfig? c = null)
    {
        c ??= new BarConfig(); var (l, r) = BarModel.Build(new BarState(Now, app, new BatteryInfo(true, 80, false, true), new VolumeInfo(true, 50, false), new NetworkInfo(NetKind.Wifi)), c);
        return BarLayout.Compute(new BarMetrics(), c, width, l, r, (t, bold) => t.Length * (bold ? 8 : 7));
    }

    [Fact]
    public void Left_items_start_at_the_margin_and_right_items_end_at_the_margin_without_overlap()
    {
        var f = Frame(1920); var m = new BarMetrics();
        Assert.Equal(m.PadX, f.Items[0].X, 6); var last = f.Items[^1]; Assert.Equal(1920 - m.PadX, last.X + last.W, 6);
        for (int i = 1; i < f.Items.Count; i++) Assert.True(f.Items[i].X >= f.Items[i - 1].X + f.Items[i - 1].W - 1e-9, $"se solapan {i - 1} y {i}");
        Assert.Equal([BarItemKind.WinMacMenu, BarItemKind.AppName, BarItemKind.HubSystem, BarItemKind.HubNetwork, BarItemKind.HubDisks, BarItemKind.Weather, BarItemKind.Network, BarItemKind.Volume, BarItemKind.Battery, BarItemKind.ControlCenter, BarItemKind.Clock], f.Items.Select(i => i.Item.Kind));
    }

    [Fact]
    public void A_long_app_name_is_truncated_to_stay_out_of_the_center_gap()
    {
        var f = Frame(1000, new string('X', 200)); var app = f.Items.Single(i => i.Item.Kind == BarItemKind.AppName);
        Assert.True(app.X + app.W <= 1000 * (1 - 0.30) / 2 + 1e-6, $"el nombre invade el hueco central: {app.X + app.W}"); Assert.True(app.ContentMaxW < 200 * 8);
    }

    [Fact]
    public void On_a_narrow_bar_low_priority_items_are_dropped_but_never_the_clock()
    {
        var f = Frame(420); var kinds = f.Items.Select(i => i.Item.Kind).ToList();
        Assert.Contains(BarItemKind.Clock, kinds); Assert.DoesNotContain(BarItemKind.Volume, kinds);
        var last = f.Items[^1]; Assert.True(last.X + last.W <= 420 + 1e-6);
    }

    [Fact]
    public void Hit_testing_finds_the_item_under_x_and_nothing_in_the_gaps_or_center()
    {
        var f = Frame(1920); var clock = f.Items[^1];
        Assert.Equal(f.Items.Count - 1, BarLayout.HitTest(f, clock.X + clock.W / 2)); Assert.Equal(-1, BarLayout.HitTest(f, 960)); Assert.Equal(-1, BarLayout.HitTest(f, 1));
    }
}

public class ControllerTests
{
    private static BarController Make(out BarState st)
    {
        st = new BarState(new DateTime(2026, 10, 30, 8, 5, 0), "Notas", new BatteryInfo(true, 60, false, true), new VolumeInfo(true, 40, false), new NetworkInfo(NetKind.Ethernet));
        var c = new BarController(new BarMetrics(), new BarConfig(), st); c.Measure = (t, b) => t.Length * 7; c.SetWidth(1600); return c;
    }

    [Fact]
    public void Hover_highlights_only_clickable_items_and_fades_in_and_out()
    {
        var c = Make(out _); var v = c.Step(0); var clock = v.Frame.Items[^1]; var app = v.Frame.Items.First(i => i.Item.Kind == BarItemKind.AppName);
        c.SetPointer(app.X + 5, 10); Assert.Equal(-1, c.Step(0.016).HoverIndex);                       // el nombre no es clicable
        c.SetPointer(clock.X + 5, 10); var a = c.Step(0.016); Assert.Equal(v.Frame.Items.Count - 1, a.HoverIndex); Assert.InRange(a.HoverAlpha[^1], 0.01, 0.99); Assert.True(a.NeedsFrames);
        BarView b = a; for (int i = 0; i < 60; i++) b = c.Step(0.016); Assert.Equal(1, b.HoverAlpha[^1], 3); Assert.False(b.NeedsFrames);
        c.SetPointer(null, null); for (int i = 0; i < 60; i++) b = c.Step(0.016); Assert.Equal(0, b.HoverAlpha[^1], 3); Assert.Equal(-1, b.HoverIndex);
        c.SetPointer(clock.X + 5, 80); Assert.Equal(-1, c.Step(0.016).HoverIndex);                       // por debajo de la barra
    }

    [Fact]
    public void Click_returns_the_action_of_the_item_under_the_pointer()
    {
        var c = Make(out _); var v = c.Step(0); var net = v.Frame.Items.First(i => i.Item.Kind == BarItemKind.Network);
        Assert.Equal("ms-settings:network", c.OnClick(net.X + net.W / 2).Uri); Assert.Equal(BarActionKind.None, c.OnClick(800).Kind);
        Assert.Equal(BarActionKind.ShowMenu, c.OnClick(v.Frame.Items[0].X + 3).Kind); Assert.Equal(BarItemKind.Network, c.ItemAt(net.X + 1)!.Item.Kind);
    }

    [Fact]
    public void State_changes_relayout_and_keep_the_hover_on_the_same_item()
    {
        var c = Make(out var st); var v = c.Step(0); var clock = v.Frame.Items[^1]; c.SetPointer(clock.X + 5, 10); for (int i = 0; i < 40; i++) c.Step(0.016);
        c.State = st with { ActiveApp = "Un nombre de aplicación mucho más largo", Battery = BatteryInfo.None };
        var v2 = c.Step(0.016); Assert.Equal(BarItemKind.Clock, v2.Frame.Items[v2.HoverIndex].Item.Kind); Assert.DoesNotContain(v2.Frame.Items, i => i.Item.Kind == BarItemKind.Battery);
        Assert.Equal(1, v2.HoverAlpha[v2.HoverIndex], 2);
    }
}

public class ConfigTests
{
    [Fact]
    public void Config_roundtrips_and_a_broken_or_missing_file_gives_defaults()
    {
        var f = Path.Combine(Path.GetTempPath(), "winmac-bar-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(new BarConfig(), BarConfig.LoadOrDefault(f));
            var c = new BarConfig { Background = BarBackground.Transparent, ReserveSpace = false, Use24Hour = false, Language = "en", CenterGap = 0.4 };
            Assert.True(c.TrySave(f)); Assert.Equal(c, BarConfig.LoadOrDefault(f)); Assert.False(File.Exists(f + ".tmp"));
            File.WriteAllText(f, "{ esto no es json"); Assert.Equal(new BarConfig(), BarConfig.LoadOrDefault(f));
            Assert.Equal(BarBackground.Glass, BarConfig.FromJson("{}").Background);
        }
        finally { try { File.Delete(f); } catch (Exception) { } }
    }
}

public class YieldRegionsTests
{
    private static readonly PxRect Bar = new(0, 0, 2560, 30);
    private static long Area(IEnumerable<PxRect> r) => r.Sum(x => x.Area);
    private static System.Text.Json.JsonElement? J(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Without_holes_the_whole_bar_stays()
    {
        Assert.Equal([Bar], YieldRegions.Visible(Bar, [])); Assert.False(YieldRegions.Touches(Bar, []));
    }

    [Fact]
    public void A_hole_in_the_middle_leaves_exactly_the_rest_and_the_pieces_never_overlap()
    {
        var island = new PxRect(1020, 0, 520, 30);   // la Isla expandida cubre toda la altura de la barra
        var v = YieldRegions.Visible(Bar, [island]);
        Assert.Equal(Bar.Area - island.Area, Area(v));
        Assert.All(v, p => Assert.False(Overlaps(p, island)));
        for (int i = 0; i < v.Count; i++) for (int j = i + 1; j < v.Count; j++) Assert.False(Overlaps(v[i], v[j]));
        Assert.True(YieldRegions.Touches(Bar, [island]));
    }

    [Fact]
    public void A_pill_shorter_than_the_bar_leaves_the_strip_below_it_and_both_sides()
    {
        var pill = new PxRect(1100, 0, 360, 20);
        var v = YieldRegions.Visible(Bar, [pill]);
        Assert.Equal(Bar.Area - pill.Area, Area(v));
        Assert.Contains(v, p => p.X <= 1100 && p.Right >= 1460 && p.Y <= 20 && p.Bottom >= 30);   // la franja bajo la píldora sigue siendo barra
    }

    [Fact]
    public void A_hole_taller_than_the_bar_only_removes_the_part_that_overlaps_it()
    {
        var island = new PxRect(1000, 0, 560, 220);   // expandida: muy por debajo de la barra
        Assert.Equal(Bar.Area - 560L * 30, Area(YieldRegions.Visible(Bar, [island])));
    }

    [Fact]
    public void Holes_outside_the_bar_or_empty_change_nothing_and_a_covering_hole_removes_everything()
    {
        Assert.Equal([Bar], YieldRegions.Visible(Bar, [new PxRect(100, 50, 200, 100), new PxRect(0, 0, 0, 0)]));
        Assert.False(YieldRegions.Touches(Bar, [new PxRect(100, 50, 200, 100)]));
        Assert.Empty(YieldRegions.Visible(Bar, [new PxRect(-10, -10, 3000, 100)]));
    }

    [Fact]
    public void Two_holes_are_both_removed()
    {
        var a = new PxRect(200, 0, 100, 30); var b = new PxRect(1000, 0, 520, 30);
        Assert.Equal(Bar.Area - a.Area - b.Area, Area(YieldRegions.Visible(Bar, [a, b])));
    }

    [Fact]
    public void Overlapping_holes_are_not_subtracted_twice()
    {
        var a = new PxRect(1000, 0, 400, 30); var b = new PxRect(1200, 0, 400, 30);   // se solapan 200
        Assert.Equal(Bar.Area - 600L * 30, Area(YieldRegions.Visible(Bar, [a, b])));
    }

    [Fact]
    public void Parse_reads_valid_rects_and_ignores_garbage()
    {
        var r = YieldRegions.Parse(J("{\"rects\":[{\"x\":10,\"y\":0,\"w\":300,\"h\":40},{\"x\":1,\"y\":2,\"w\":0,\"h\":5},{\"x\":\"a\"},7]}"));
        Assert.Equal([new PxRect(10, 0, 300, 40)], r);
        Assert.Empty(YieldRegions.Parse(J("{\"rects\":[]}"))); Assert.Empty(YieldRegions.Parse(J("[1]"))); Assert.Empty(YieldRegions.Parse(J("{\"otra\":1}"))); Assert.Empty(YieldRegions.Parse(null));
    }

    [Fact]
    public void Parse_caps_the_number_of_rects()
    {
        var many = string.Join(",", Enumerable.Range(0, 100).Select(i => $"{{\"x\":{i},\"y\":0,\"w\":5,\"h\":5}}"));
        Assert.Equal(YieldRegions.MaxRects, YieldRegions.Parse(J("{\"rects\":[" + many + "]}")).Count);
    }

    private static bool Overlaps(PxRect a, PxRect b) => Math.Max(a.X, b.X) < Math.Min(a.Right, b.Right) && Math.Max(a.Y, b.Y) < Math.Min(a.Bottom, b.Bottom);
}
