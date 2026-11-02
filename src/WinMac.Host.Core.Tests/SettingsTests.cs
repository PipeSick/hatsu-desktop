using WinMac.Bar;
using Xunit;

namespace WinMac.Host.Core.Tests;

public class SettingsModelTests
{
    private static IReadOnlyList<SettingsPage> Pages(HostConfig? cfg = null, bool notchInstalled = true, string status = "") =>
        SettingsBuilder.Build(new SettingsInput(cfg ?? new HostConfig(), notchInstalled ? new Dictionary<string, ModuleState>() : new Dictionary<string, ModuleState> { ["notch"] = ModuleState.NotInstalled }, false, status));

    [Fact]
    public void There_are_five_pages_row_ids_are_unique_and_every_bar_option_appears_exactly_once()
    {
        var pages = Pages(); Assert.Equal(["general", "bar", "hub", "nc", "notch", "ext"], pages.Select(p => p.Id));
        var ids = pages.SelectMany(p => p.Groups).SelectMany(g => g.Rows).Select(r => r.Id).ToList(); Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var t in SettingsBuilder.BarToggles) Assert.Single(ids, i => i == "bar:" + t.Field);   // ningún ajuste de la Barra se pierde en el rediseño
        Assert.Equal(SettingsBuilder.BarToggles.Length, ids.Count(i => i.StartsWith("bar:", StringComparison.Ordinal)));
        Assert.Equal(NotchSettings.SectionIds.Length, ids.Count(i => i.StartsWith("notch.sec:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Rows_reflect_the_current_configuration()
    {
        var cfg = new HostConfig { Bar = new BarConfig { ShowSeconds = true, HubRefreshMs = 2500 }, Notch = new NotchSettings { GlassLevel = 33, Position = "TopRight", MusicPlayer = "Custom", CustomPlayerPath = "C:\\x.exe" } };
        var rows = Pages(cfg).SelectMany(p => p.Groups).SelectMany(g => g.Rows).ToDictionary(r => r.Id);
        Assert.True(rows["bar:showSeconds"].On); Assert.Equal("2500", rows["hub.ms"].Text); Assert.Equal(33, rows["notch.glass"].Value); Assert.Equal(2, rows["notch.pos"].Selected); Assert.Equal(2, rows["notch.player"].Selected);
        Assert.True(rows["notch.path"].Enabled); Assert.Equal("C:\\x.exe", rows["notch.path"].Text);
    }

    [Fact]
    public void Without_the_notch_installed_its_controls_are_disabled_with_the_reason_and_never_fake()
    {
        var n = Pages(notchInstalled: false).Single(p => p.Id == "notch"); var rows = n.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(rows, r => r.Kind == RowKind.Info); Assert.All(rows.Where(r => r.Kind != RowKind.Info), r => { Assert.False(r.Enabled); if (!r.Id.StartsWith("notch.sec:", StringComparison.Ordinal)) Assert.False(string.IsNullOrEmpty(r.Reason)); });   // las pestañas van atenuadas bajo el aviso de arriba (sin repetir el motivo 10 veces)
        Assert.False(Pages(notchInstalled: false).Single(p => p.Id == "general").Groups[0].Rows.Single(r => r.Id == "mod:notch").Enabled);
    }

    [Fact]
    public void The_player_path_is_only_enabled_with_a_custom_player_and_the_lyrics_pill_only_with_lyrics()
    {
        var def = Pages().SelectMany(p => p.Groups).SelectMany(g => g.Rows).ToDictionary(r => r.Id); Assert.False(def["notch.path"].Enabled); Assert.False(def["notch.pill"].Enabled);
        var on = Pages(new HostConfig { Notch = new NotchSettings { LyricsEnabled = true, MusicPlayer = "Custom" } }).SelectMany(p => p.Groups).SelectMany(g => g.Rows).ToDictionary(r => r.Id); Assert.True(on["notch.path"].Enabled); Assert.True(on["notch.pill"].Enabled);
    }

    [Fact]
    public void The_weather_footer_shows_the_search_status_or_the_hint_and_the_first_and_last_sections_cannot_move_out_of_range()
    {
        var hub = Pages(status: "Ciudad: Molina, Maule, Chile").Single(p => p.Id == "hub"); Assert.Equal("Ciudad: Molina, Maule, Chile", hub.Groups.Single(g => g.Header == "Tiempo").Footer);
        Assert.Contains("Molina, Chile", Pages().Single(p => p.Id == "hub").Groups.Single(g => g.Header == "Tiempo").Footer);
        var secs = Pages().Single(p => p.Id == "notch").Groups.Single(g => g.Header == "Pestañas").Rows; Assert.False(secs[0].CanUp); Assert.True(secs[0].CanDown); Assert.True(secs[^1].CanUp); Assert.False(secs[^1].CanDown);
    }
}

public class SettingsLayoutTests
{
    private static SettingsPage Notch() => SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "notch");

    [Fact]
    public void Rows_stack_without_overlapping_inside_their_cards_and_cards_never_touch_each_other()
    {
        foreach (var page in SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)))
        {
            var f = SettingsLayout.Compute(page); Assert.True(f.ContentHeight > 100);
            foreach (var g in f.Groups) Assert.True(g.H > 0);
            for (int i = 1; i < f.Rows.Count; i++) Assert.True(f.Rows[i].Y >= f.Rows[i - 1].Y + f.Rows[i - 1].H - 0.01, page.Id + " " + f.Rows[i].Row.Id);
            for (int i = 1; i < f.Groups.Count; i++) Assert.True(f.Groups[i].HeaderY >= f.Groups[i - 1].Y + f.Groups[i - 1].H + f.Groups[i - 1].FooterH + SettingsLayout.GroupGap - 0.5, page.Id + " grupo " + i);   // aire entre grupos: nada «pegado»
            Assert.All(f.Rows, r => { Assert.True(r.X >= SettingsLayout.Pad && r.X + r.W <= SettingsLayout.ContentW - SettingsLayout.Pad + 0.01); });
        }
    }

    [Fact]
    public void A_long_page_scrolls_clamped_and_a_short_one_does_not()
    {
        var page = Notch(); var top = SettingsLayout.Compute(page, 0); Assert.True(top.MaxScroll > 0);
        var end = SettingsLayout.Compute(page, 1e9); Assert.Equal(top.MaxScroll, end.Scroll, 3); Assert.True(end.Rows[0].Y < top.Rows[0].Y);
        Assert.Equal(0, SettingsLayout.Compute(page, -50).Scroll);
        Assert.Equal(0, SettingsLayout.Compute(SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "general")).MaxScroll);
    }

    [Fact]
    public void Control_geometry_stays_inside_the_row_and_hit_testing_finds_each_part()
    {
        var f = SettingsLayout.Compute(Notch()); var seg = f.Rows.First(r => r.Row.Kind == RowKind.Segmented); var s = SettingsLayout.SegmentRect(seg);
        Assert.True(s.X >= seg.X && s.X + s.W <= seg.X + seg.W && s.Y >= seg.Y && s.Y + s.H <= seg.Y + seg.H);
        int n = seg.Row.Options!.Count; for (int i = 0; i < n; i++) Assert.Equal("seg:" + i, SettingsLayout.Hit(f, s.X + s.W / n * (i + 0.5), s.Y + s.H / 2)!.Value.Part);
        var tog = f.Rows.First(r => r.Row.Kind == RowKind.Toggle && !r.Row.CanUp && !r.Row.CanDown); var sw = SettingsLayout.SwitchRect(tog); Assert.Equal("switch", SettingsLayout.Hit(f, sw.X + 5, sw.Y + 5)!.Value.Part); Assert.Equal("switch", SettingsLayout.Hit(f, tog.X + 8, tog.Y + 8)!.Value.Part);   // toda la fila alterna
        var sec = f.Rows.First(r => r.Row.Id == "notch.sec:1"); var d = SettingsLayout.DownRect(sec); var u = SettingsLayout.UpRect(sec);
        Assert.Equal("down", SettingsLayout.Hit(f, d.X + 3, d.Y + 3)!.Value.Part); Assert.Equal("up", SettingsLayout.Hit(f, u.X + 3, u.Y + 3)!.Value.Part); Assert.True(u.X + u.W <= d.X && d.X + d.W <= sw.X);   // flechas y interruptor no se pisan
        Assert.Null(SettingsLayout.Hit(f, 2, 2));
    }
}

public class SettingsControllerTests
{
    private static SettingsController Make(HostConfig? cfg = null) { var c = new SettingsController(); c.SetPages(SettingsBuilder.Build(new SettingsInput(cfg ?? new HostConfig(), new Dictionary<string, ModuleState>(), false))); return c; }
    private static (double x, double y) Abs(RowFrame r, double dx, double dy) => (SettingsLayout.SidebarW + r.X + dx, SettingsLayout.TitleH + r.Y + dy);

    [Fact]
    public void Clicking_a_toggle_row_returns_the_flipped_value_on_release_and_releasing_elsewhere_cancels()
    {
        var c = Make(); var row = c.Frame.Rows.First(r => r.Row.Id == "mod:bar"); var (x, y) = Abs(row, 20, 10);
        Assert.Null(c.PointerDown(x, y)); var e = c.PointerUp(x, y)!; Assert.Equal(new SettingsEdit("mod:bar", "toggle", On: false), e);   // estaba activa
        var other = c.Frame.Rows.First(r => r.Row.Id == "mod:dock"); var (ox, oy) = Abs(other, 20, 10);
        c.PointerDown(x, y); Assert.Null(c.PointerUp(ox, oy));
    }

    [Fact]
    public void The_sidebar_changes_page_resets_scroll_and_keeps_the_page_across_refreshes()
    {
        var c = Make(); var r = SettingsLayout.SideItemRect(4); c.PointerDown(r.X + 10, r.Y + 10); c.PointerUp(r.X + 10, r.Y + 10); Assert.Equal("notch", c.Page!.Id);
        Assert.True(c.Scroll(120)); Assert.Equal(120, c.ScrollOffset, 3);
        c.SetPages(SettingsBuilder.Build(new SettingsInput(new HostConfig { Bar = new BarConfig { ShowDate = false } }, new Dictionary<string, ModuleState>(), false))); Assert.Equal("notch", c.Page!.Id); Assert.Equal(120, c.ScrollOffset, 3);   // un refresco no te saca de la página ni del punto donde estabas
        var r0 = SettingsLayout.SideItemRect(0); c.PointerDown(r0.X + 5, r0.Y + 5); c.PointerUp(r0.X + 5, r0.Y + 5); Assert.Equal("general", c.Page!.Id); Assert.Equal(0, c.ScrollOffset);
    }

    [Fact]
    public void Segmented_slider_and_buttons_produce_their_edits_and_disabled_rows_do_nothing()
    {
        var c = Make(); c.SelectPage("notch");
        var seg = c.Frame.Rows.First(r => r.Row.Id == "notch.pos"); var s = SettingsLayout.SegmentRect(seg); double sx = SettingsLayout.SidebarW + s.X + s.W * 0.9, sy = SettingsLayout.TitleH + s.Y + s.H / 2;
        c.PointerDown(sx, sy); Assert.Equal(new SettingsEdit("notch.pos", "segment", Index: 2), c.PointerUp(sx, sy));
        var sl = c.Frame.Rows.First(r => r.Row.Id == "notch.glass"); var (x0, x1, cy) = SettingsLayout.SliderTrack(sl);
        double ax = SettingsLayout.SidebarW + x0 + (x1 - x0) * 0.25, ay = SettingsLayout.TitleH + cy; c.PointerDown(ax, ay); c.PointerMove(SettingsLayout.SidebarW + x0 + (x1 - x0) * 0.8, ay); Assert.Equal(80, c.Dragging!.Value.Value, 0);
        var up = c.PointerUp(SettingsLayout.SidebarW + x0 + (x1 - x0) * 0.8, ay)!; Assert.Equal("slider", up.Kind); Assert.Equal(80, up.Number, 0); Assert.Null(c.Dragging);
        var path = c.Frame.Rows.First(r => r.Row.Id == "notch.path"); var (px, py) = Abs(path, path.W - 60, path.H / 2); Assert.Null(c.PointerDown(px, py)); Assert.Null(c.PointerUp(px, py));   // desactivado (no es «Otro»)
        var cd = Make(new HostConfig { Notch = new NotchSettings { MusicPlayer = "Custom" } }); cd.SelectPage("notch"); var path2 = cd.Frame.Rows.First(r => r.Row.Id == "notch.path"); var f = SettingsLayout.FieldRect(path2);
        Assert.Equal(new SettingsEdit("notch.path", "focus"), cd.PointerDown(SettingsLayout.SidebarW + f.X + 10, SettingsLayout.TitleH + f.Y + 10));
        var local = cd.Frame.Rows.First(r => r.Row.Id == "notch.local"); var (bx, by) = Abs(local, 30, 10); cd.PointerDown(bx, by); Assert.Equal(new SettingsEdit("notch.local", "button"), cd.PointerUp(bx, by));
    }

    [Fact]
    public void Suggestions_are_picked_on_press_before_the_field_loses_focus_and_a_click_elsewhere_closes_them()
    {
        var c = Make(); c.SelectPage("hub"); var city = c.RowOf("hub.city")!; c.SetSuggestions("hub.city", ["Molina, Maule, Chile", "Molina de Segura, España"]);
        var r = SettingsLayout.SuggestRect(city, 2); double x = SettingsLayout.SidebarW + r.X + 20, y = SettingsLayout.TitleH + r.Y + 4 + SettingsLayout.SuggestRowH * 1.5;
        Assert.Equal(new SettingsEdit("hub.city", "suggest", Index: 1), c.PointerDown(x, y));
        c.SetSuggestions("hub.city", ["a"]); c.PointerDown(SettingsLayout.SidebarW + 5, SettingsLayout.TitleH + 5); Assert.Empty(c.Suggestions); Assert.Null(c.SuggestFor);
        c.SetSuggestions("hub.city", ["a", "b", "c"]); c.MoveSuggestion(1); Assert.Equal(0, c.SuggestIndex); c.MoveSuggestion(5); Assert.Equal(2, c.SuggestIndex); c.MoveSuggestion(-9); Assert.Equal(0, c.SuggestIndex);
    }
}

public class SettingsActionsTests
{
    private static (SettingsActions a, ConfigService cfg, List<string> calls, string file) Make()
    {
        string f = Path.Combine(Path.GetTempPath(), "winmac-settings-" + Guid.NewGuid().ToString("N") + ".json"); var cfg = new ConfigService(f, new HostConfig()); var calls = new List<string>();
        return (new SettingsActions(cfg, () => calls.Add("local"), on => calls.Add("autostart:" + on)), cfg, calls, f);
    }

    [Fact]
    public void Notification_center_widgets_can_be_turned_off_on_and_reordered()
    {
        var (a, cfg, _, f) = Make();
        try
        {
            Assert.True(a.Apply(new SettingsEdit("nc.w:weather", "toggle", On: false))); Assert.Equal("calendar,battery", cfg.Current.Bar!.NcWidgets);
            Assert.True(a.Apply(new SettingsEdit("nc.w:battery", "up"))); Assert.Equal("battery,calendar", cfg.Current.Bar!.NcWidgets);
            Assert.True(a.Apply(new SettingsEdit("nc.w:weather", "toggle", On: true))); Assert.Equal("battery,calendar,weather", cfg.Current.Bar!.NcWidgets);
            Assert.True(a.Apply(new SettingsEdit("nc.w:weather", "down"))); Assert.Equal("battery,calendar,weather", cfg.Current.Bar!.NcWidgets);   // ya es el último: no cambia
            Assert.True(a.Apply(new SettingsEdit("bar:notificationsFromWindows", "toggle", On: true))); Assert.True(cfg.Current.Bar!.NotificationsFromWindows);
            var rows = SettingsBuilder.Build(new SettingsInput(cfg.Current, new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "nc").Groups[1].Rows;
            Assert.Equal(["nc.w:battery", "nc.w:calendar", "nc.w:weather"], rows.Select(r => r.Id)); Assert.False(rows[0].CanUp); Assert.True(rows[0].CanDown); Assert.False(rows[2].CanDown);
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Every_kind_of_edit_reaches_the_central_configuration()
    {
        var (a, cfg, calls, f) = Make();
        try
        {
            Assert.True(a.Apply(new SettingsEdit("bar:showSeconds", "toggle", On: true))); Assert.True(cfg.Bar.ShowSeconds);
            Assert.True(a.Apply(new SettingsEdit("bar.theme", "segment", Index: 2))); Assert.Equal(BarThemeMode.Dark, cfg.Bar.Theme);
            Assert.True(a.Apply(new SettingsEdit("bar.bg", "segment", Index: 1))); Assert.Equal(BarBackground.Transparent, cfg.Bar.Background);
            Assert.True(a.Apply(new SettingsEdit("hub.rate", "segment", Index: 2))); Assert.Equal(WinMac.Bar.Hub.RateUnit.MBps, cfg.Bar.RateUnit);
            Assert.True(a.Apply(new SettingsEdit("hub.ms", "text", Text: " 2000 "))); Assert.Equal(2000, cfg.Bar.HubRefreshMs); Assert.True(a.Apply(new SettingsEdit("hub.ms", "text", Text: "abc"))); Assert.Equal(2000, cfg.Bar.HubRefreshMs);   // basura se ignora
            Assert.True(a.Apply(new SettingsEdit("bar.ics", "text", Text: "C:\\c.ics"))); Assert.Equal("C:\\c.ics", cfg.Bar.CalendarIcsPath); a.Apply(new SettingsEdit("bar.ics", "text", Text: "  ")); Assert.Null(cfg.Bar.CalendarIcsPath);
            Assert.True(a.Apply(new SettingsEdit("mod:dock", "toggle", On: false))); Assert.False(cfg.IsEnabled("dock"));
            Assert.True(a.Apply(new SettingsEdit("autostart", "toggle", On: true))); Assert.Equal(["autostart:True"], calls); Assert.True(cfg.Current.Autostart);
            Assert.True(a.Apply(new SettingsEdit("notch.pos", "segment", Index: 1))); Assert.Equal("TopLeft", cfg.Notch.Position);
            Assert.True(a.Apply(new SettingsEdit("notch.glass", "slider", Number: 71.6))); Assert.Equal(72, cfg.Notch.GlassLevel);
            Assert.True(a.Apply(new SettingsEdit("notch.player", "segment", Index: 2))); Assert.Equal("Custom", cfg.Notch.MusicPlayer);
            Assert.True(a.Apply(new SettingsEdit("notch.lyrics", "toggle", On: true))); Assert.True(cfg.Notch.LyricsEnabled); a.Apply(new SettingsEdit("notch.pill", "toggle", On: true)); Assert.True(cfg.Notch.LyricsInPill);
            a.Apply(new SettingsEdit("notch.lyrics", "toggle", On: false)); Assert.False(cfg.Notch.LyricsInPill);   // sin letras no hay letra en el notch
            var first = cfg.Notch.Sections[0].Id; Assert.True(a.Apply(new SettingsEdit("notch.sec:0", "down"))); Assert.Equal(first, cfg.Notch.Sections[1].Id);
            Assert.True(a.Apply(new SettingsEdit("notch.sec:1", "up"))); Assert.Equal(first, cfg.Notch.Sections[0].Id);
            bool before = cfg.Notch.Sections[2].Enabled; a.Apply(new SettingsEdit("notch.sec:2", "toggle", On: !before)); Assert.Equal(!before, cfg.Notch.Sections[2].Enabled);
            Assert.True(a.Apply(new SettingsEdit("notch.local", "button"))); Assert.Contains("local", calls);
            Assert.False(a.Apply(new SettingsEdit("bar:nope", "toggle", On: true))); Assert.False(a.Apply(new SettingsEdit("desconocido", "toggle")));
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }
}
