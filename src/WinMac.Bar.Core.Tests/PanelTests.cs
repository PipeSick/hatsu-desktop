using System.Text.Json;
using WinMac.Bar;
using WinMac.Calendar;
using WinMac.SystemControl;
using Xunit;

public class BarConfigPatchTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_patch_changes_only_the_named_fields_and_keeps_the_rest()
    {
        var c = new BarConfig { ShowSeconds = false, CenterGap = 0.4 }.ApplyPatch(J("{\"showSeconds\":true,\"theme\":\"Dark\",\"showBluetooth\":false}"));
        Assert.True(c.ShowSeconds); Assert.Equal(BarThemeMode.Dark, c.Theme); Assert.False(c.ShowBluetooth); Assert.Equal(0.4, c.CenterGap); Assert.True(c.ShowNetwork);
    }

    [Fact]
    public void Wrong_types_unknown_fields_and_the_version_are_ignored_field_by_field()
    {
        var c = new BarConfig().ApplyPatch(J("{\"showSeconds\":\"yes\",\"nope\":1,\"version\":99,\"centerGap\":0.2,\"theme\":\"Neon\"}"));
        Assert.False(c.ShowSeconds); Assert.Equal(1, c.Version); Assert.Equal(0.2, c.CenterGap); Assert.Equal(BarThemeMode.System, c.Theme);   // «Neon» no existe: se descarta el parche completo del campo
    }

    [Fact]
    public void Ranges_and_text_are_sanitized_and_the_calendar_path_can_be_set_and_cleared()
    {
        Assert.Equal(0.6, new BarConfig().ApplyPatch(J("{\"centerGap\":5}")).CenterGap); Assert.Equal(0, new BarConfig().ApplyPatch(J("{\"centerGap\":-1}")).CenterGap);
        Assert.Equal("es", new BarConfig { Language = "fr" }.Sanitize().Language); Assert.Equal("en", new BarConfig { Language = "en" }.Sanitize().Language);
        var c = new BarConfig().ApplyPatch(J("{\"calendarIcsPath\":\"  \\\"C:\\\\c.ics\\\"  \"}")); Assert.Equal("C:\\c.ics", c.CalendarIcsPath);
        Assert.Null(c.ApplyPatch(J("{\"calendarIcsPath\":null}")).CalendarIcsPath);
        var d = new BarConfig(); Assert.Same(d, d.ApplyPatch(J("[1]"))); Assert.Equal(d, d.ApplyPatch(J("{}")));
    }

    [Fact]
    public void The_wire_form_is_camelCase_and_roundtrips_through_a_patch()
    {
        var c = new BarConfig { ShowSeconds = true, AppMenus = true, Theme = BarThemeMode.Light, CcBrightness = false };
        Assert.Contains("\"showSeconds\":true", c.ToWireJson()); Assert.Contains("\"theme\":\"Light\"", c.ToWireJson());
        Assert.Equal(c, new BarConfig().ApplyPatch(J(c.ToWireJson())).Sanitize());
    }

    [Fact]
    public void New_options_default_to_the_safe_choice()
    {
        var c = new BarConfig(); Assert.False(c.AppMenus); Assert.True(c.ConfirmDestructive); Assert.True(c.ShowControlCenter); Assert.Null(c.CalendarIcsPath);
    }
}

public class BarModelPanelTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0);
    private static BarState St(BluetoothInfo? bt = null, IReadOnlyList<string>? menus = null) => new(Now, "Notas", new BatteryInfo(true, 80, false, true), new VolumeInfo(true, 50, false), new NetworkInfo(NetKind.Wifi, "Casa", 3), bt, menus);

    [Fact]
    public void Control_center_and_bluetooth_icons_appear_only_when_there_is_something_behind_them()
    {
        var (_, r) = BarModel.Build(St(new BluetoothInfo(true, true, 1)), new BarConfig());
        Assert.Equal([BarItemKind.Bluetooth, BarItemKind.Network, BarItemKind.Volume, BarItemKind.Battery, BarItemKind.ControlCenter, BarItemKind.Clock], r.Select(i => i.Kind));
        var (_, none) = BarModel.Build(St(BluetoothInfo.None), new BarConfig()); Assert.DoesNotContain(none, i => i.Kind == BarItemKind.Bluetooth);   // sin adaptador: no hay icono que engañe
        var (_, off) = BarModel.Build(St(new BluetoothInfo(true, true, 1)), new BarConfig { ShowBluetooth = false, ShowControlCenter = false }); Assert.DoesNotContain(off, i => i.Kind is BarItemKind.Bluetooth or BarItemKind.ControlCenter);
    }

    [Fact]
    public void App_menus_are_experimental_off_by_default_and_each_has_a_unique_identity()
    {
        var menus = new[] { "Archivo", "Edición", "Ver" };
        Assert.DoesNotContain(BarModel.Build(St(menus: menus), new BarConfig()).Left, i => i.Kind == BarItemKind.AppMenu);
        var (l, _) = BarModel.Build(St(menus: menus), new BarConfig { AppMenus = true });
        var m = l.Where(i => i.Kind == BarItemKind.AppMenu).ToList(); Assert.Equal(["Archivo", "Edición", "Ver"], m.Select(i => i.Text)); Assert.Equal(3, m.Select(i => i.Id).Distinct().Count());
        Assert.Equal(2, BarModel.ClickAction(m[2]).Index); Assert.Equal(BarActionKind.ShowAppMenu, BarModel.ClickAction(m[0]).Kind);
    }

    [Fact]
    public void Clicks_open_their_panel_and_keep_the_settings_page_as_the_fallback()
    {
        BarAction A(BarItemKind k) => BarModel.ClickAction(new BarItem(k, "", 16));
        Assert.Equal(PanelKind.Wifi, A(BarItemKind.Network).Panel); Assert.Equal("ms-settings:network", A(BarItemKind.Network).Uri);
        Assert.Equal(PanelKind.Sound, A(BarItemKind.Volume).Panel); Assert.Equal(PanelKind.Battery, A(BarItemKind.Battery).Panel); Assert.Equal(PanelKind.Calendar, A(BarItemKind.Clock).Panel);
        Assert.Equal(PanelKind.Bluetooth, A(BarItemKind.Bluetooth).Panel); Assert.Equal(PanelKind.ControlCenter, A(BarItemKind.ControlCenter).Panel);
        Assert.Equal(BarActionKind.OpenPanel, A(BarItemKind.Clock).Kind); Assert.Equal(BarActionKind.ShowMenu, A(BarItemKind.WinMacMenu).Kind); Assert.False(BarModel.IsClickable(BarItemKind.AppName));
    }
}

public class PanelLayoutTests
{
    private static Widget T(string id, int col) => new(WidgetKind.Tile, id, id, Col: col);

    [Fact]
    public void Two_half_tiles_share_a_row_and_a_lone_tile_takes_the_full_width()
    {
        var f = PanelLayout.Compute(new PanelModel(PanelKind.ControlCenter, "", [new(WidgetKind.Header, "h"), T("a", 0), T("b", 1), T("c", 0), new(WidgetKind.Slider, "s")]));
        var a = f.Items[1]; var b = f.Items[2]; var c = f.Items[3];
        Assert.Equal(a.Y, b.Y); Assert.True(b.X > a.X + a.Width - 0.01); Assert.Equal(a.Width, b.Width, 3);
        Assert.Equal(PanelLayout.Width - 2 * PanelLayout.Pad, c.Width, 3);   // c queda solo en su fila
        Assert.True(f.Items[4].Y > c.Y + c.Height - 0.01); Assert.True(f.Height > f.Items[4].Y + f.Items[4].Height - 0.01);
    }

    [Fact]
    public void Hit_testing_and_slider_math_map_points_to_widgets_and_values()
    {
        var f = PanelLayout.Compute(new PanelModel(PanelKind.Sound, "", [new(WidgetKind.Slider, "volume", Value: 30)]));
        var s = f.Items[0]; Assert.Equal(0, PanelLayout.HitTest(f, s.X + 5, s.Y + 5)); Assert.Equal(-1, PanelLayout.HitTest(f, s.X + 5, s.Y + s.Height + 50));
        var (l, r) = PanelLayout.SliderTrack(s); Assert.Equal(0, PanelLayout.SliderValueAt(s, l - 100)); Assert.Equal(100, PanelLayout.SliderValueAt(s, r + 100)); Assert.Equal(50, PanelLayout.SliderValueAt(s, (l + r) / 2), 3);
    }

    [Fact]
    public void A_disabled_slider_with_a_reason_is_taller_so_the_reason_fits()
    {
        Assert.True(PanelLayout.HeightOf(new Widget(WidgetKind.Slider, "b", Enabled: false, Reason: "x")) > PanelLayout.HeightOf(new Widget(WidgetKind.Slider, "b", Enabled: true)));
    }
}

public class PanelControllerTests
{
    private static PanelController Make(params Widget[] ws) { var c = new PanelController(); c.SetModel(new PanelModel(PanelKind.ControlCenter, "", ws)); return c; }
    private static (double X, double Y) Mid(PanelController c, int i) { var f = c.Frame.Items[i]; return (f.X + f.Width / 2, f.Y + f.Height / 2); }

    [Fact]
    public void Press_and_release_on_the_same_enabled_widget_is_a_click_and_releasing_elsewhere_cancels_it()
    {
        var c = Make(new Widget(WidgetKind.Row, "r1", "uno"), new Widget(WidgetKind.Row, "r2", "dos"));
        var (x1, y1) = Mid(c, 0); var (x2, y2) = Mid(c, 1);
        Assert.Null(c.PointerDown(x1, y1)); Assert.Equal(new PanelEvent(PanelEventKind.Click, "r1"), c.PointerUp(x1, y1));
        c.PointerDown(x1, y1); Assert.Null(c.PointerUp(x2, y2));
    }

    [Fact]
    public void Disabled_widgets_are_not_interactive_and_info_notes_headers_never_click()
    {
        var c = Make(new Widget(WidgetKind.Tile, "t", Enabled: false), new Widget(WidgetKind.Info, "i"), new Widget(WidgetKind.Note, "n", "texto"), new Widget(WidgetKind.Header, "title", "x"), new Widget(WidgetKind.Header, "back", "y"));
        for (int i = 0; i < 4; i++) { var (x, y) = Mid(c, i); c.PointerMove(x, y); Assert.Equal(-1, c.Hover); c.PointerDown(x, y); Assert.Null(c.PointerUp(x, y)); }
        var (bx, by) = Mid(c, 4); c.PointerMove(bx, by); Assert.Equal(4, c.Hover); c.PointerDown(bx, by); Assert.Equal("back", c.PointerUp(bx, by)!.Id);   // «‹ atrás» sí
    }

    [Fact]
    public void Dragging_a_slider_streams_changes_and_commits_the_final_value_on_release()
    {
        var c = Make(new Widget(WidgetKind.Slider, "volume", Value: 10)); var f = c.Frame.Items[0]; var (l, r) = PanelLayout.SliderTrack(f); double y = f.Y + 10;
        var first = c.PointerDown(l + (r - l) * 0.2, y)!; Assert.Equal(PanelEventKind.SliderChange, first.Kind); Assert.Equal(20, first.Value, 1); Assert.True(c.Dragging);
        Assert.Null(c.PointerMove(l + (r - l) * 0.2, y));                                   // sin cambio real: sin evento
        var mid = c.PointerMove(l + (r - l) * 0.6, y + 500)!; Assert.Equal(60, mid.Value, 1);   // el arrastre sigue aunque el puntero se salga del alto
        var end = c.PointerUp(l + (r - l) * 0.9, y)!; Assert.Equal(PanelEventKind.SliderCommit, end.Kind); Assert.Equal(90, end.Value, 1); Assert.False(c.Dragging);
    }

    [Fact]
    public void A_disabled_slider_cannot_be_dragged()
    {
        var c = Make(new Widget(WidgetKind.Slider, "brightness", Enabled: false, Reason: "sin DDC/CI")); var (x, y) = Mid(c, 0);
        Assert.Null(c.PointerDown(x, y)); Assert.False(c.Dragging); Assert.Null(c.PointerUp(x, y));
    }

    [Fact]
    public void Calendar_clicks_map_to_a_day_and_to_the_previous_or_next_month()
    {
        var ctx = PanelContext.Initial(new BarConfig(), new DateTime(2026, 11, 1)); var c = new PanelController(); c.SetModel(PanelBuilder.CalendarPanel(ctx));
        var nav = c.Frame.Items.First(i => i.W.Kind == WidgetKind.Nav); var month = c.Frame.Items.First(i => i.W.Kind == WidgetKind.Month);
        c.PointerDown(nav.X + 5, nav.Y + 5); Assert.Equal("prev", c.PointerUp(nav.X + 5, nav.Y + 5)!.Id);
        c.PointerDown(nav.X + nav.Width - 5, nav.Y + 5); Assert.Equal("next", c.PointerUp(nav.X + nav.Width - 5, nav.Y + 5)!.Id);
        double cx = month.X + month.Width / 7 * 6.5, cy = month.Y + PanelLayout.MonthHeader + PanelLayout.MonthRow * 0.5;   // primera fila, domingo
        c.PointerDown(cx, cy); Assert.Equal("day:2026-11-01", c.PointerUp(cx, cy)!.Id);
    }

    [Fact]
    public void Replacing_the_model_keeps_the_hover_by_id()
    {
        var c = Make(new Widget(WidgetKind.Row, "a"), new Widget(WidgetKind.Row, "b")); var (x, y) = Mid(c, 1); c.PointerMove(x, y); Assert.Equal(1, c.Hover);
        c.SetModel(new PanelModel(PanelKind.Sound, "", [new Widget(WidgetKind.Note, "n", "x"), new Widget(WidgetKind.Row, "a"), new Widget(WidgetKind.Row, "b")])); Assert.Equal(2, c.Hover);
    }
}

public class PanelBuilderTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0);
    private static PanelContext Ctx(SystemSnapshot? s = null, BarConfig? cfg = null) => PanelContext.Initial(cfg ?? new BarConfig(), Now) with { Sys = s ?? SystemSnapshot.Empty };
    private static readonly SystemSnapshot Nothing = new(AudioState.None("sin audio"), WifiState.None("sin Wi-Fi"), BluetoothState.None("sin Bluetooth"), BrightnessState.None("sin DDC/CI"), PowerModeState.None("sin modos"), AppearanceState.None("sin registro"), false, "no compatible", false);

    [Fact]
    public void When_nothing_is_available_every_control_is_disabled_and_says_why_nothing_pretends_to_work()
    {
        foreach (var kind in Enum.GetValues<PanelKind>())
        {
            foreach (var w in PanelBuilder.Build(kind, Ctx(Nothing)).Widgets.Where(x => x.Kind is WidgetKind.Tile or WidgetKind.Slider or WidgetKind.Row && !x.Id.StartsWith("open:") && !x.Id.StartsWith("topology:") && x.Id is not ("notifications" or "mute")))
            {
                if (w.Enabled) Assert.True(w.Id is "power:BestEfficiency" or "power:Balanced" or "power:Performance" or "power:BestPerformance" ? false : false, $"{kind}/{w.Id} está habilitado sin respaldo");
                else Assert.False(string.IsNullOrWhiteSpace(w.Reason) && !PanelBuilder.Build(kind, Ctx(Nothing)).Widgets.Any(n => n.Kind == WidgetKind.Note), $"{kind}/{w.Id} deshabilitado sin motivo");
            }
        }
    }

    [Fact]
    public void Control_center_tiles_follow_the_real_state_and_the_users_choices()
    {
        var sys = Nothing with { Wifi = new WifiState(true, true, "Casa", 80, [], RadioOn: true), Bluetooth = new BluetoothState(true, true, [new("1", "Audífonos", true)]), Audio = new AudioState(true, 42, false, []), Brightness = new BrightnessState(true, [new("a", "P1", 70, "DDC/CI")]), Appearance = new AppearanceState(true, true) };
        var m = PanelBuilder.ControlCenter(Ctx(sys)); Widget W(string id) => m.Widgets.Single(x => x.Id == id);
        Assert.True(W("open:wifi").On); Assert.Equal("Casa", W("open:wifi").Sub); Assert.True(W("open:bluetooth").On); Assert.Equal("1 conectado(s)", W("open:bluetooth").Sub); Assert.True(W("appearance").On);
        Assert.Equal(42, W("volume").Value); Assert.True(W("volume").Enabled); Assert.Equal(70, W("brightness").Value); Assert.True(W("brightness").Enabled);
        var cfg = new BarConfig { CcWifi = false, CcBrightness = false, CcNotifications = false };
        var hidden = PanelBuilder.ControlCenter(Ctx(sys, cfg)); Assert.DoesNotContain(hidden.Widgets, x => x.Id is "open:wifi" or "brightness" or "notifications");
    }

    [Fact]
    public void An_odd_number_of_tiles_still_closes_the_row_and_the_layout_never_overlaps()
    {
        var m = PanelBuilder.ControlCenter(Ctx(Nothing, new BarConfig { CcNotifications = false })); var f = PanelLayout.Compute(m);
        for (int i = 0; i < f.Items.Count; i++) for (int j = i + 1; j < f.Items.Count; j++)
        {
            var a = f.Items[i]; var b = f.Items[j];
            bool overlap = a.X < b.X + b.Width - 0.01 && b.X < a.X + a.Width - 0.01 && a.Y < b.Y + b.Height - 0.01 && b.Y < a.Y + a.Height - 0.01; Assert.False(overlap, $"{a.W.Id} se solapa con {b.W.Id}");
        }
    }

    [Fact]
    public void Wifi_rows_are_clickable_only_for_known_or_connected_networks_and_new_ones_point_to_settings()
    {
        var nets = new List<WifiNetwork> { new("Casa", 80, true, true, true, "Casa"), new("Vecino", 60, true, false, true, "Vecino"), new("Cafe", 40, false, false, false, null), new("Oficina", 30, true, false, false, null) };
        var m = PanelBuilder.WifiPanel(Ctx(Nothing with { Wifi = new WifiState(true, true, "Casa", 80, nets, RadioOn: true) }));
        Widget N(string s) => m.Widgets.Single(x => x.Id == "net:" + s);
        Assert.True(N("Casa").Enabled); Assert.Contains("desconectar", N("Casa").Sub); Assert.True(N("Vecino").Enabled); Assert.False(N("Cafe").Enabled); Assert.Contains("Configuración", N("Cafe").Sub); Assert.False(N("Oficina").Enabled); Assert.Contains("contraseña", N("Oficina").Sub);
        Assert.Contains(m.Widgets, w => w.Id == "open:ms-settings:network-wifi");
        var off = PanelBuilder.WifiPanel(Ctx(Nothing with { Wifi = new WifiState(true, false, null, 0, nets, RadioOn: false) })); Assert.DoesNotContain(off.Widgets, w => w.Id.StartsWith("net:"));
        var unknown = PanelBuilder.WifiPanel(Ctx(Nothing with { Wifi = new WifiState(true, false, null, 0, [], RadioOn: null) })); Assert.False(unknown.Widgets.Single(w => w.Id == "wifi-radio").Enabled);   // radio de estado desconocido: no se simula un interruptor
    }

    [Fact]
    public void Wifi_denied_by_missing_location_permission_says_so_and_offers_the_privacy_page_instead_of_an_empty_list()
    {
        var sys = Nothing with { Wifi = new WifiState(true, false, null, 0, [], "Windows necesita el permiso de ubicación", RadioOn: true) };
        var m = PanelBuilder.WifiPanel(Ctx(sys));
        Assert.Contains(m.Widgets, w => w.Id == "reason" && w.Text.Contains("ubicación")); Assert.Contains(m.Widgets, w => w.Id == "open:ms-settings:privacy-location"); Assert.DoesNotContain(m.Widgets, w => w.Id == "none");
        Assert.Equal("Requiere ubicación", PanelBuilder.Build(PanelKind.ControlCenter, Ctx(sys)).Widgets.Single(w => w.Id == "open:wifi").Sub);
    }

    [Fact]
    public void Wifi_signal_and_band_appear_only_when_the_option_is_on_and_band_is_derived_from_the_frequency()
    {
        var nets = new List<WifiNetwork> { new("Casa", 89, true, true, true, "Casa", -57, 5260), new("Vecino", 40, true, false, true, "Vecino", -80, 2437), new("Sin", 40, true, false, true, "Sin") };
        Assert.Equal(["5 GHz", "2,4 GHz", null], nets.Select(n => n.BandText)); Assert.Equal("6 GHz", (nets[0] with { FrequencyMhz = 6195 }).BandText);
        var sys = Nothing with { Wifi = new WifiState(true, true, "Casa", 89, nets, RadioOn: true) };
        var off = PanelBuilder.WifiPanel(Ctx(sys)); Assert.DoesNotContain("dBm", off.Widgets.Single(w => w.Id == "net:Casa").Sub);
        var on = PanelBuilder.WifiPanel(Ctx(sys, new BarConfig { WifiDetails = true }));
        var casa = on.Widgets.Single(w => w.Id == "net:Casa").Sub; Assert.Equal("5 GHz · -57 dBm · Conectado", casa); Assert.True(casa!.Length <= 30);   // cabe en la fila de 320 px
        Assert.Equal("2,4 GHz · -80 dBm · Conocida", on.Widgets.Single(w => w.Id == "net:Vecino").Sub); Assert.Equal("Conocida", on.Widgets.Single(w => w.Id == "net:Sin").Sub);   // sin dato de Windows no se inventa
    }

    [Fact]
    public void The_audio_output_list_is_switchable_only_when_the_unofficial_api_is_compatible_and_offers_restore()
    {
        var outs = new List<AudioDevice> { new("a", "Altavoces", true), new("b", "Audífonos", false) };
        var ok = PanelBuilder.SoundPanel(Ctx(Nothing with { Audio = new AudioState(true, 30, false, outs), OutputSwitchOk = true, CanRestoreOutput = true }));
        Assert.True(ok.Widgets.Single(w => w.Id == "out:b").Enabled); Assert.Contains(ok.Widgets, w => w.Id == "audio-restore"); Assert.DoesNotContain(ok.Widgets, w => w.Id == "noswitch");
        var no = PanelBuilder.SoundPanel(Ctx(Nothing with { Audio = new AudioState(true, 30, false, outs), OutputSwitchOk = false, OutputSwitchReason = "usa Configuración" }));
        Assert.False(no.Widgets.Single(w => w.Id == "out:b").Enabled); Assert.Contains(no.Widgets, w => w.Id == "noswitch" && w.Text == "usa Configuración"); Assert.Contains(no.Widgets, w => w.Id == "open:ms-settings:sound"); Assert.DoesNotContain(no.Widgets, w => w.Id == "audio-restore");
    }

    [Fact]
    public void The_battery_panel_tells_desktops_apart_and_marks_the_current_power_mode()
    {
        var desktop = PanelBuilder.BatteryPanel(Ctx(Nothing with { Power = new PowerModeState(true, PowerMode.Performance) }));
        Assert.Equal("Sin batería", desktop.Widgets.Single(w => w.Id == "battery").Text); Assert.True(desktop.Widgets.Single(w => w.Id == "power:Performance").On); Assert.True(desktop.Widgets.All(w => w.Kind != WidgetKind.Row || w.Enabled));
        var laptop = PanelBuilder.BatteryPanel(Ctx(Nothing with { Power = PowerModeState.None("no") }) with { Battery = new BatteryInfo(true, 15, false, false) });
        Assert.Contains("15%", laptop.Widgets.Single(w => w.Id == "battery").Text); Assert.Equal("Batería baja", laptop.Widgets.Single(w => w.Id == "battery").Sub); Assert.All(laptop.Widgets.Where(w => w.Id.StartsWith("power:")), w => Assert.False(w.Enabled));
    }

    [Fact]
    public void The_calendar_panel_shows_the_month_events_of_the_chosen_day_and_asks_for_an_ics_when_there_is_none()
    {
        var events = new List<CalendarEvent> { new("Daily", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 10, 15, 0, TimeSpan.Zero), false) };
        var cfg = new BarConfig { CalendarIcsPath = "C:\\c.ics" };
        var m = PanelBuilder.CalendarPanel(Ctx(cfg: cfg) with { Events = events, SelectedDay = new DateOnly(2026, 11, 3) });
        Assert.Equal("noviembre 2026", m.Widgets.Single(w => w.Kind == WidgetKind.Nav).Text); Assert.Contains(m.Widgets, w => w.Kind == WidgetKind.Info && w.Text == "Daily" && w.Sub == "10:00–10:15");
        var md = (MonthData)m.Widgets.Single(w => w.Kind == WidgetKind.Month).Data!; Assert.Equal(1, md.Days.Single(d => d.Date == new DateOnly(2026, 11, 3)).Events);
        Assert.Contains(PanelBuilder.CalendarPanel(Ctx()).Widgets, w => w.Id == "noics");
        Assert.Contains(PanelBuilder.CalendarPanel(Ctx(cfg: cfg)).Widgets, w => w.Id == "noevents");
    }

    [Fact]
    public void Busy_labels_replace_the_subtitle_while_an_operation_runs_and_messages_appear_as_notes()
    {
        var nets = new List<WifiNetwork> { new("Casa", 80, true, false, true, "Casa") };
        var ctx = Ctx(Nothing with { Wifi = new WifiState(true, false, null, 0, nets, RadioOn: true) }) with { Busy = new Dictionary<string, string> { ["net:Casa"] = "Conectando…" }, Message = "Falló algo" };
        var m = PanelBuilder.WifiPanel(ctx); Assert.Equal("Conectando…", m.Widgets.Single(w => w.Id == "net:Casa").Sub); Assert.Contains(m.Widgets, w => w.Id == "msg" && w.Text == "Falló algo");
    }
}
