using WinMac.Bar;
using Xunit;

namespace WinMac.Host.Core.Tests;

public class SettingsModelTests
{
    private static IReadOnlyList<SettingsPage> Pages(HostConfig? cfg = null, bool notchInstalled = true, string status = "") =>
        SettingsBuilder.Build(new SettingsInput(cfg ?? new HostConfig(), notchInstalled ? new Dictionary<string, ModuleState>() : new Dictionary<string, ModuleState> { ["notch"] = ModuleState.NotInstalled }, false, status));

    [Fact]
    public void There_are_twelve_pages_row_ids_are_unique_and_every_bar_option_appears_exactly_once()
    {
        var pages = Pages(); Assert.Equal(["general", "bar", "dock", "notch", "modules", "gestures", "spot", "nc", "capture", "weather", "connect", "ext"], pages.Select(p => p.Id));   // propuesta A: 12 secciones en 5 grupos
        Assert.Equal([null, "Escritorio", "Escritorio", "Escritorio", "Funciones", "Funciones", "Funciones", "Contenido", "Contenido", "Contenido", "Dispositivos", "Avanzado"], pages.Select(p => p.Section));
        var ids = pages.SelectMany(p => p.Groups).SelectMany(g => g.Rows).Select(r => r.Id).ToList(); Assert.Equal(ids.Count, ids.Distinct().Count());
        // los iconos que se agregan, quitan y ordenan viven en «Personalizar los iconos de la barra» (baritem:); el resto sigue en su sitio
        var moved = new HashSet<string> { "showNetwork", "showVolume", "showBattery", "showBluetooth", "showTrayIcons", "showTaskManager", "showHubSystem", "showHubNetwork", "showHubDisks" };
        foreach (var t in SettingsBuilder.BarToggles.Where(t => t.Field != "weatherAutoLocation"))
        {
            if (moved.Contains(t.Field)) { var def = SettingsBuilder.BarIcons.Single(d => d.Field == t.Field); Assert.Equal(1, ids.Count(i => i == "baritem:" + def.Id || i == "baritem.add:" + def.Id)); Assert.DoesNotContain("bar:" + t.Field, ids); }
            else Assert.Single(ids, i => i == "bar:" + t.Field);   // ningún ajuste de la Barra se pierde en el rediseño
        }
        Assert.Single(ids, i => i == "wx.src");   // WM-015: «Usar mi ubicación» es ahora el selector «Origen» (Automática / Ciudad elegida)
        Assert.Equal(SettingsBuilder.BarToggles.Length - 1 - moved.Count, ids.Count(i => i.StartsWith("bar:", StringComparison.Ordinal)));
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
        var hub = Pages(status: "Ciudad: Molina, Maule, Chile").Single(p => p.Id == "weather"); Assert.Equal("Ciudad: Molina, Maule, Chile", hub.Groups.Single(g => g.Header == "Ubicación principal").Footer);
        Assert.Contains("Molina, Chile", Pages().Single(p => p.Id == "weather").Groups.Single(g => g.Header == "Ubicación principal").Footer);
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
            var f = SettingsLayout.Compute(page); Assert.True(f.ContentHeight > 50, page.Id);
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
        Assert.Equal(0, SettingsLayout.Compute(SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).OrderBy(p => p.Groups.Sum(g => g.Rows.Count)).First()).MaxScroll);   // la página más corta no se desplaza (General ya puede, con la fila del asistente)
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
        var c = Make(); var r = SettingsLayout.SideEntries(c.Pages)[3].Rect; c.PointerDown(r.X + 10, r.Y + 10); c.PointerUp(r.X + 10, r.Y + 10); Assert.Equal("notch", c.Page!.Id);
        Assert.True(c.Scroll(120)); Assert.Equal(120, c.ScrollOffset, 3);
        c.SetPages(SettingsBuilder.Build(new SettingsInput(new HostConfig { Bar = new BarConfig { ShowDate = false } }, new Dictionary<string, ModuleState>(), false))); Assert.Equal("notch", c.Page!.Id); Assert.Equal(120, c.ScrollOffset, 3);   // un refresco no te saca de la página ni del punto donde estabas
        var r0 = SettingsLayout.SideEntries(c.Pages)[0].Rect; c.PointerDown(r0.X + 5, r0.Y + 5); c.PointerUp(r0.X + 5, r0.Y + 5); Assert.Equal("general", c.Page!.Id); Assert.Equal(0, c.ScrollOffset);
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
        var c = Make(); c.SelectPage("weather"); var city = c.RowOf("hub.city")!; c.SetSuggestions("hub.city", ["Molina, Maule, Chile", "Molina de Segura, España"]);
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
    public void Wm019_the_dock_page_edits_dock_json_presets_fine_size_and_reservation()
    {
        var (a, cfg, _, f) = Make();
        try
        {
            var dock = new WinMac.Dock.DockConfig(); a.LoadDock = () => dock; a.SaveDock = d => dock = d;
            Assert.True(a.Apply(new SettingsEdit("dock.size", "segment", Index: 0))); Assert.Equal(36, dock.IconSize);
            Assert.True(a.Apply(new SettingsEdit("dock.fine", "slider", Number: 100))); Assert.Equal(SettingsBuilder.DockFineMax, dock.IconSize);
            Assert.True(a.Apply(new SettingsEdit("dock.fine", "slider", Number: 0))); Assert.Equal(WinMac.Dock.DockSettings.MinIconSize, dock.IconSize);
            Assert.True(a.Apply(new SettingsEdit("dock.reserve", "toggle", On: false))); Assert.False(dock.ReserveSpace);
            Assert.True(a.Apply(new SettingsEdit("dock.autohide", "toggle", On: true))); Assert.True(dock.AutoHide);
            var page = SettingsBuilder.Build(new SettingsInput(cfg.Current, new Dictionary<string, ModuleState>(), false) { Dock = dock }).Single(p => p.Id == "dock");
            var rows = page.Groups.SelectMany(g => g.Rows).ToDictionary(r => r.Id);
            Assert.False(rows["dock.reserve"].Enabled); Assert.False(string.IsNullOrEmpty(rows["dock.reserve"].Reason));   // con autoocultación no hay espacio que reservar: se dice por qué
            Assert.Equal("Otro", rows["dock.size"].Options![rows["dock.size"].Selected]);   // 28 no es un preajuste
            var none = SettingsBuilder.Build(new SettingsInput(cfg.Current, new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "dock");
            Assert.All(none.Groups.SelectMany(g => g.Rows).Where(r => !r.Id.StartsWith("host.")), r => Assert.Equal(RowKind.Info, r.Kind));   // sin dock.json legible: ningún control falso del Dock (la barra de tareas no depende de dock.json)
            a.LoadDock = null; Assert.False(a.Apply(new SettingsEdit("dock.size", "segment", Index: 1)));
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Wm015_weather_source_and_additional_cities_can_be_removed_and_reordered()
    {
        var (a, cfg, _, f) = Make();
        try
        {
            cfg.ApplyBarPatch(System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["weatherFavorites"] = "Santiago|-33.45|-70.67;Valparaíso|-33.05|-71.62;Talca|-35.43|-71.66" }));
            Assert.True(a.Apply(new SettingsEdit("wx.src", "segment", Index: 0))); Assert.True(cfg.Current.Bar!.WeatherAutoLocation);
            Assert.True(a.Apply(new SettingsEdit("wx.fav:2", "up"))); Assert.Equal(["Santiago", "Talca", "Valparaíso"], WinMac.Bar.Hub.GeoPoint.ParseList(cfg.Current.Bar!.WeatherFavorites).Select(g => g.Name));
            Assert.True(a.Apply(new SettingsEdit("wx.fav:0", "toggle", On: false))); Assert.Equal(["Talca", "Valparaíso"], WinMac.Bar.Hub.GeoPoint.ParseList(cfg.Current.Bar!.WeatherFavorites).Select(g => g.Name));
            Assert.True(a.Apply(new SettingsEdit("wx.src", "segment", Index: 1))); Assert.False(cfg.Current.Bar!.WeatherAutoLocation);
            var page = SettingsBuilder.Build(new SettingsInput(cfg.Current, new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "weather");
            var favs = page.Groups.Single(g => g.Header == "Ciudades adicionales").Rows;
            Assert.Equal(["wx.fav:0", "wx.fav:1", "hub.favadd"], favs.Select(r => r.Id)); Assert.False(favs[0].CanUp); Assert.False(favs[1].CanDown);
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Wm002_capture_delay_only_takes_the_offered_values()
    {
        var (a, cfg, _, f) = Make();
        try
        {
            Assert.True(a.Apply(new SettingsEdit("bar.capdelay", "segment", Index: 2))); Assert.Equal(5, cfg.Current.Bar!.CaptureDelaySeconds);
            Assert.Equal(10, new BarConfig { CaptureDelaySeconds = 99 }.Sanitize().CaptureDelaySeconds); Assert.Equal(0, new BarConfig { CaptureDelaySeconds = -2 }.Sanitize().CaptureDelaySeconds);
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Wm013_long_subtitles_wrap_and_grow_the_row_instead_of_being_cut()
    {
        var shortRow = new SettingsRow(RowKind.Toggle, "x", "Título", "Corto");
        var longRow = shortRow with { Subtitle = string.Join(" ", Enumerable.Repeat("texto largo que no cabe", 8)) };
        Assert.True(SettingsLayout.RowHeight(longRow) > SettingsLayout.RowHeight(shortRow));
        Assert.Equal(0, (SettingsLayout.RowHeight(longRow) - SettingsLayout.RowHeight(shortRow)) % SettingsLayout.SubLineH, 3);
        var side = SettingsLayout.SideEntries(SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false))); Assert.True(side[^1].Rect.Y + SettingsLayout.SideItemH <= SettingsLayout.WinH - 8);   // todas las secciones y los nombres de grupo caben en la barra lateral
        var seg = new SettingsRow(RowKind.Segmented, "wx.src", "Origen", "Subtítulo", Options: ["Automática", "Ciudad elegida"]);
        Assert.True(SettingsLayout.SegmentWidth(seg, SettingsLayout.ColW) / 2 >= "Ciudad elegida".Length * 7.4);   // CI: «Ciudad eleg…» salía recortado
        Assert.True(SettingsLayout.ControlSpace(seg) >= SettingsLayout.SegmentWidth(seg, SettingsLayout.ColW));      // el subtítulo no se mete bajo el control
        Assert.Equal("48 pt", new SettingsRow(RowKind.Slider, "dock.fine", "x", SliderMin: 28, SliderMax: 96, Unit: "pt").SliderLabel(SettingsBuilder.DockFineValue(48)));
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
            Assert.False(a.Apply(new SettingsEdit("bar.theme", "segment", Index: 2))); Assert.Equal(BarThemeMode.System, cfg.Bar.Theme);   // It7: ya no hay selector manual de tema
            Assert.True(a.Apply(new SettingsEdit("bar:showTaskManager", "toggle", On: true))); Assert.True(cfg.Bar.ShowTaskManager);
            Assert.True(a.Apply(new SettingsEdit("bar:showPhonePercent", "toggle", On: false))); Assert.False(cfg.Bar.ShowPhonePercent);
            Assert.True(a.Apply(new SettingsEdit("bar:showPowerWithoutBattery", "toggle", On: true))); Assert.True(cfg.Bar.ShowPowerWithoutBattery);
            cfg.ApplyBarPatch(System.Text.Json.JsonSerializer.SerializeToElement(new { barOrder = "Volume,Network" })); Assert.True(a.Apply(new SettingsEdit("bar.orderreset", "button"))); Assert.Equal("", cfg.Bar.BarOrder);
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

public class It8SettingsTests
{
    [Fact]
    public void Peripherals_and_cleanup_are_off_by_default_and_every_edit_persists()
    {
        var def = new BarConfig(); Assert.False(def.ShowPeripherals); Assert.False(def.PeripheralAlerts); Assert.False(def.ShowCleanup); Assert.True(def.CleanupIsland);
        Assert.Contains(SettingsBuilder.BarToggles, t => t.Field == "showPeripherals"); Assert.Contains(SettingsBuilder.BarToggles, t => t.Field == "showCleanup");
        // los ajustes de antes siguen en su sitio (se añadieron al final de la tabla)
        Assert.Equal("showNetwork", SettingsBuilder.BarToggles[0].Field);
    }
}

public class SettingsSearchTests
{
    private static SettingsController Make() { var c = new SettingsController(); c.SetPages(SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false))); return c; }

    [Fact] public void Searching_shows_a_results_page_with_the_matching_rows_and_their_real_controls()
    {
        var c = Make(); Assert.Equal("general", c.Page!.Id);
        c.SetSearch("vpn"); Assert.Equal("search", c.Page!.Id);
        var rows = c.Page.Groups.SelectMany(g => g.Rows).ToList(); Assert.Contains(rows, r => r.Id == "bar:showVpn" || r.Id.StartsWith("baritem", StringComparison.Ordinal)); Assert.DoesNotContain(rows, r => r.Kind == RowKind.Info && r.Id != "search.none");
        Assert.All(c.Page.Groups, g => Assert.False(string.IsNullOrEmpty(g.Header)));   // cada grupo dice de qué sección viene
    }

    [Fact] public void The_search_ignores_accents_and_case_and_says_so_when_nothing_matches()
    {
        var c = Make(); c.SetSearch("BATERIA"); Assert.Contains(c.Page!.Groups.SelectMany(g => g.Rows), r => r.Id.Contains("Battery", StringComparison.OrdinalIgnoreCase) || r.Title.Contains("atería"));
        c.SetSearch("zzzzzz"); Assert.Equal("search.none", Assert.Single(c.Page!.Groups.SelectMany(g => g.Rows)).Id);
    }

    [Fact] public void Clearing_the_search_or_choosing_a_page_returns_to_the_normal_view_on_the_same_page()
    {
        var c = Make(); c.SelectPage("dock"); c.SetSearch("sonido"); Assert.Equal("search", c.Page!.Id);
        c.SetSearch(""); Assert.Equal("dock", c.Page!.Id);   // volver no cambia de sección
        c.SetSearch("sonido"); c.SelectPage("notch"); Assert.Equal("", c.SearchText); Assert.Equal("notch", c.Page!.Id);
        var f = c.SearchFrame; var r = SettingsLayout.FieldRect(f); Assert.True(r.X >= 8 && r.X + r.W <= SettingsLayout.SidebarW - 8 && r.Y >= 40 && r.Y + r.H < SettingsLayout.SideTop);   // la caja cabe en la barra lateral, sobre las secciones
    }
}

