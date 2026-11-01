using System.Text.Json;
using WinMac.Bar;
using WinMac.Bar.Hub;
using WinMac.SystemControl;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class HubControllerTests
{
    private static (PanelController C, PanelFrame F, HubRig R) Open(HubTab tab, Action<HubRig>? setup = null)
    {
        var r = new HubRig(); setup?.Invoke(r); r.LoadAll(tab); var c = new PanelController(); var m = r.Hub(); c.SetModel(m); return (c, c.Frame, r);
    }

    private static WidgetFrame F(PanelFrame f, string id) => f.Items.Single(i => i.W.Id == id);
    private static PanelEvent? Tap(PanelController c, double x, double y) { c.PointerMove(x, y); c.PointerDown(x, y); return c.PointerUp(x, y); }

    [Fact]
    public void Tapping_a_tab_icon_selects_that_tab_and_dead_zones_do_nothing()
    {
        var (c, f, _) = Open(HubTab.Sound); var t = F(f, "tabs"); double cw = (t.Width - 12) / 4, y = t.Y + t.Height / 2;
        Assert.Equal("tab:Sound", Tap(c, t.X + 6 + cw * 0.5, y)!.Id); Assert.Equal("tab:System", Tap(c, t.X + 6 + cw * 1.5, y)!.Id); Assert.Equal("tab:Network", Tap(c, t.X + 6 + cw * 2.5, y)!.Id); Assert.Equal("tab:Disks", Tap(c, t.X + 6 + cw * 3.5, y)!.Id);
        Assert.Null(Tap(c, t.X + 2, y)); Assert.Equal(-1, PanelLayout.TabAt(t, t.X + t.Width + 5));   // el margen del contenedor no es una pestaña
    }

    [Fact]
    public void The_tab_title_row_opens_that_tabs_settings()
    {
        var (c, f, _) = Open(HubTab.Disks); var h = F(f, "settings:Disks"); Assert.Equal("settings:Disks", Tap(c, h.X + h.Width - 10, h.Y + h.Height / 2)!.Id);
    }

    [Fact]
    public void The_volume_slider_drags_on_the_track_and_the_speaker_icon_mutes_instead_of_dragging()
    {
        var (c, f, _) = Open(HubTab.Sound); var v = F(f, "volume"); var (l, r) = PanelLayout.SliderTrack(v); double y = v.Y + 23;
        Assert.Equal("mute", Tap(c, v.X + 18, y)!.Id);   // icono = silencio
        var down = c.PointerDown((l + r) / 2, y)!; Assert.Equal((PanelEventKind.SliderChange, "volume"), (down.Kind, down.Id)); Assert.Equal(50, down.Value, 1);
        var move = c.PointerMove(r, y)!; Assert.Equal(100, move.Value, 1); var up = c.PointerUp(r, y)!; Assert.Equal(PanelEventKind.SliderCommit, up.Kind);
        Assert.True(r < v.X + v.Width - 12 - 40);   // la pista deja sitio al porcentaje de la derecha
    }

    [Fact]
    public void An_app_row_drags_its_own_slider_mutes_from_the_right_and_ignores_the_name_and_icon()
    {
        var (c, f, _) = Open(HubTab.Sound); var a = F(f, "pid:10"); var (l, r) = PanelLayout.AppSliderTrack(a); double sy = a.Y + a.Height - 14;
        var down = c.PointerDown((l + r) / 2, sy)!; Assert.Equal((PanelEventKind.SliderChange, "appvol:pid:10"), (down.Kind, down.Id)); Assert.Equal(50, down.Value, 1);
        var commit = c.PointerUp(r, sy)!; Assert.Equal("appvol:pid:10", commit.Id); Assert.Equal(PanelEventKind.SliderCommit, commit.Kind);
        Assert.Equal("appmute:pid:10", Tap(c, a.X + a.Width - 12, a.Y + 16)!.Id);
        Assert.Null(Tap(c, a.X + 20, a.Y + 14)); Assert.Null(Tap(c, a.X + 90, a.Y + 10));   // icono y nombre no son controles
    }

    [Fact]
    public void Selectors_unit_pill_and_disabled_controls_behave()
    {
        var (c, f, _) = Open(HubTab.Sound); var s = F(f, "pick:out"); Assert.Equal("pick:out", Tap(c, s.X + s.Width - 30, s.Y + s.Height / 2)!.Id);
        var (cn, fn, _) = Open(HubTab.Network); var st = F(fn, "stats-net"); Assert.Equal("unit", Tap(cn, st.X + st.Width - 20, st.Y + 12)!.Id); Assert.Null(Tap(cn, st.X + 20, st.Y + 20));
        var (cd, fd, _) = Open(HubTab.Sound, r => r.Base.Audio.State = AudioState.None("sin salida")); var dis = F(fd, "pick:out"); Assert.Null(Tap(cd, dis.X + 20, dis.Y + 10));   // deshabilitado: no responde
        var dv = F(fd, "volume"); Assert.Null(cd.PointerDown(dv.X + 100, dv.Y + 23));
    }

    [Fact]
    public void Content_buttons_inside_the_card_and_the_two_bottom_buttons_are_all_reachable()
    {
        var (c, f, _) = Open(HubTab.Network); var q = F(f, "hub:quit"); var s = F(f, "hub:settings"); Assert.Equal("hub:quit", Tap(c, q.X + 20, q.Y + 10)!.Id); Assert.Equal("hub:settings", Tap(c, s.X + 20, s.Y + 10)!.Id);
        Assert.True(q.X > s.X); Assert.Equal(q.Y, s.Y);
    }
}

public class HubOperationsTests
{
    private static HubRig Rig(HubTab tab = HubTab.Sound, Func<HttpRequestMessage, HttpResponseMessage>? speed = null) => new HubRig(speed).LoadAll(tab);
    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Fact]
    public void Selecting_a_tab_changes_the_view_remembers_it_in_the_central_config_and_starts_reading_that_tab()
    {
        var r = Rig(); r.Click("tab:System"); Assert.Equal(HubTab.System, r.Base.Ui.Ctx.Tab); Assert.Equal([HubTab.System], r.Base.Ui.TabChanges); Assert.Equal("{\"hubTab\":\"System\"}", Json(r.Base.Ui.Patches[^1]));
        r.Click("tab:Disks"); Assert.Equal(HubTab.Disks, r.Base.Ui.Ctx.Tab); Assert.Equal("{\"hubTab\":\"Disks\"}", Json(r.Base.Ui.Patches[^1]));
        int n = r.Base.Ui.Patches.Count; r.Click("tab:Notificaciones"); r.Click("tab:"); Assert.Equal(HubTab.Disks, r.Base.Ui.Ctx.Tab); Assert.Equal(n, r.Base.Ui.Patches.Count);   // una pestaña inexistente no cambia nada
    }

    [Fact]
    public void Settings_open_the_related_section_and_quit_closes_the_whole_app_not_just_the_panel()
    {
        var r = Rig(HubTab.Network); r.Click("settings:Network"); r.Click("hub:settings"); r.Tab(HubTab.Disks); r.Click("hub:settings"); r.Click("hub:settings-weather");
        Assert.Equal(["network", "network", "disks", "weather"], r.Base.Ui.SettingsOpened); Assert.Equal(1, r.Base.Ui.Hides);   // solo la de Tiempo cierra el panel (abre otra ventana)
        r.Click("hub:quit"); Assert.Equal(1, r.Base.Ui.Quits);
    }

    [Fact]
    public void The_output_and_microphone_selectors_list_real_devices_mark_the_default_and_apply_the_choice()
    {
        var r = Rig(); r.Base.Ui.PickNext = null; r.Click("pick:out"); var menu = r.Base.Ui.Menus[^1]; Assert.Equal("Salida", menu.Title);
        Assert.Equal(["Altavoces", "Auriculares"], menu.Choices.Select(c => c.Text)); Assert.Equal([true, false], menu.Choices.Select(c => c.Checked)); Assert.Equal(["out:spk", "out:hp"], menu.Choices.Select(c => c.Id));
        r.Base.Ui.PickNext = "out:hp"; r.Click("pick:out"); Assert.Contains("SetDefaultOutput hp", r.Base.Audio.Calls);
        r.Base.Ui.PickNext = "in:mic"; r.Click("pick:in"); Assert.Contains("SetDefaultInput mic", r.Base.Audio.Calls); Assert.Equal("Micrófono", r.Base.Ui.Menus[^1].Title);
    }

    [Fact]
    public void A_failed_device_switch_shows_why_and_a_cancelled_menu_changes_nothing()
    {
        var r = Rig(); r.Base.Audio.Next = ServiceResult.Unavailable("Este equipo no permite cambiar el micrófono desde WinMac"); r.Base.Ui.PickNext = "in:mic"; r.Click("pick:in"); Assert.Contains("no permite cambiar", r.Base.Ui.Ctx.Message);
        var r2 = Rig(); int calls = r2.Base.Audio.Calls.Count; r2.Click("pick:out"); Assert.Equal(calls, r2.Base.Audio.Calls.Count);   // se cerró el menú sin elegir
    }

    [Fact]
    public void The_microphone_mutes_and_its_slider_coalesces_and_rereads_on_release()
    {
        var r = Rig(); r.Click("mic-mute"); Assert.Contains("SetInputMuted True", r.Base.Audio.Calls);
        r.Base.Audio.Calls.Clear(); r.Base.Click("mic"); // un clic sin valor no es un deslizamiento
        r.Base.Slide("mic", 20); r.Base.Slide("mic", 35); r.Base.Slide("mic", 42, commit: true); Assert.Equal(42, r.Base.Hub.Snapshot.Mic.Volume);   // se ve donde está el puntero
        r.Pump(); Assert.Equal(["SetInputVolume 42", "ReadInput"], r.Base.Audio.Calls.Where(c => c.StartsWith("SetInput") || c == "ReadInput").ToList());
    }

    [Fact]
    public void Each_app_can_be_muted_and_have_its_own_volume_with_immediate_feedback_and_the_real_reading_after()
    {
        var r = Rig(); r.Click("appmute:pid:10"); Assert.Contains("SetSessionMuted pid:10 True", r.Base.Audio.Calls); Assert.Contains("ReadSessions", r.Base.Audio.Calls);
        r.Base.Audio.Calls.Clear(); r.Base.Slide("appvol:pid:10", 10); r.Base.Slide("appvol:pid:10", 25, commit: true); Assert.Equal(25, r.Base.Hub.Snapshot.AppSessions.Sessions.First(a => a.Id == "pid:10").Volume);
        r.Pump(); Assert.Equal(["SetSessionVolume pid:10 25"], r.Base.Audio.Calls.Where(c => c.StartsWith("SetSession")).ToList());
        r.Base.Audio.Calls.Clear(); r.Click("appmute:pid:999"); Assert.DoesNotContain(r.Base.Audio.Calls, c => c.StartsWith("SetSession"));   // una app que ya no está: nada
    }

    [Fact]
    public void An_app_operation_that_fails_reports_the_reason_and_the_list_is_reread()
    {
        var r = Rig(); r.Base.Audio.Next = ServiceResult.Failed("Esa aplicación ya no tiene sesión de audio"); r.Click("appmute:pid:10"); Assert.Contains("ya no tiene sesión", r.Base.Ui.Ctx.Message);
        Assert.False(r.Base.Hub.Snapshot.AppSessions.Sessions.First(a => a.Id == "pid:10").Muted);   // la lectura real revirtió el valor mostrado
    }

    [Fact]
    public void The_speed_test_needs_confirmation_never_starts_by_itself_and_can_be_cancelled()
    {
        var r = Rig(HubTab.Network, req => req.Method == HttpMethod.Post ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[100]) });
        Assert.Equal(SpeedTestStatus.Idle, r.Speed.State.Status);
        r.Base.Ui.ConfirmAnswer = false; r.Click("speedtest"); Assert.Equal(SpeedTestStatus.Idle, r.Speed.State.Status); Assert.Contains("consume datos", r.Base.Ui.Confirms[^1].Text.ToLowerInvariant());
        r.Base.Ui.ConfirmAnswer = true; r.Click("speedtest"); Assert.Equal(SpeedTestStatus.Done, r.Speed.State.Status);
        r.Click("speedtest-cancel"); Assert.Equal(SpeedTestStatus.Done, r.Speed.State.Status);   // cancelar sin prueba en curso no rompe nada
    }

    [Fact]
    public void Without_a_speed_test_engine_the_action_explains_it_instead_of_pretending()
    {
        var r = new HubRig().LoadAll(HubTab.Network); r.Click("speedtest"); Assert.Contains("no está disponible", r.Base.Ui.Ctx.Message); Assert.Empty(r.Base.Ui.Confirms);
    }

    [Fact]
    public void Disk_selection_explorer_and_the_windows_storage_page_work()
    {
        var r = Rig(HubTab.Disks); r.Click("disk:C:"); Assert.Equal("C:", r.Base.Ui.Ctx.Hub!.SelectedVolume); r.Click("explorer:C:"); Assert.Equal("C:\\", r.Base.Ui.Uris[^1]);
        r.Click("open:ms-settings:storagesense"); Assert.Equal("ms-settings:storagesense", r.Base.Ui.Uris[^1]);
    }

    [Fact]
    public void Ejecting_uses_the_safe_mechanism_reports_a_busy_drive_and_never_forces_it()
    {
        var r = Rig(HubTab.Disks); r.Disks.Catalog = DiskCatalog.Build([new PhysicalRec(0, "NVMe", "SSD", "NVMe", 1, null), new PhysicalRec(1, "USB", "HDD", "USB", 1, null)], [new PartitionRec(0, "C"), new PartitionRec(1, "E")],
            [new VolumeRec("C:\\", "", "NTFS", 1000, 250, false), new VolumeRec("E:\\", "Respaldo", "NTFS", 900, 100, true)]); r.Monitor.RefreshDisks(true); r.Pump();
        r.Disks.EjectResult = ServiceResult.Failed("La unidad sigue en uso: cierra lo que la use e inténtalo de nuevo"); r.Click("eject:E:");
        Assert.Equal(["E:"], r.Disks.Ejected); Assert.Contains("sigue en uso", r.Base.Ui.Ctx.Message);
        r.Disks.EjectResult = ServiceResult.Ok; r.Click("eject:E:"); Assert.Null(r.Base.Ui.Ctx.Message);
    }

    [Fact]
    public void Eject_all_goes_only_through_external_volumes()
    {
        var r = Rig(HubTab.Disks); r.Disks.Catalog = DiskCatalog.Build([new PhysicalRec(0, "NVMe", "SSD", "NVMe", 1, null), new PhysicalRec(1, "USB", "HDD", "USB", 1, null)], [new PartitionRec(0, "C"), new PartitionRec(1, "E")],
            [new VolumeRec("C:\\", "", "NTFS", 1, 1, false), new VolumeRec("E:\\", "", "NTFS", 1, 1, true), new VolumeRec("F:\\", "", "FAT32", 1, 1, true)]); r.Monitor.RefreshDisks(true); r.Pump();
        r.Click("eject-all"); Assert.Equal(["E:", "F:"], r.Disks.Ejected);   // el disco interno nunca se toca
    }

    [Fact]
    public void Weather_actions_refresh_on_demand_and_open_the_windows_weather_app()
    {
        var r = Rig(); r.Weather.Configure(new GeoPoint("Molina", null, -35, -71), []); r.Weather.Refresh(); r.Pump(); int calls = r.Source.Calls;
        r.Click("weather:refresh"); Assert.Equal(calls + 1, r.Source.Calls); r.Click("weather:open"); Assert.Equal("bingweather:", r.Base.Ui.Uris[^1]); Assert.True(r.Base.Ui.Hides >= 1);
    }

    [Fact]
    public void The_original_control_center_actions_keep_working_alongside_the_new_ones()
    {
        var r = Rig(); r.Click("mute"); Assert.Contains("SetMuted True", r.Base.Audio.Calls); r.Click("out:hp"); Assert.Contains("SetDefaultOutput hp", r.Base.Audio.Calls); r.Click("topology:Extend"); Assert.Contains("SetTopology Extend", r.Disks.Ejected.Count == 0 ? r.Base.Display.Calls : []);
    }
}

public class HubConfigTests
{
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void The_new_settings_have_safe_defaults_and_nothing_is_read_or_located_until_asked()
    {
        var c = new BarConfig(); Assert.True(c.ShowHub); Assert.True(c.ShowWeather); Assert.Equal(HubTab.Sound, c.HubTab); Assert.Equal(1000, c.HubRefreshMs); Assert.Equal(RateUnit.Auto, c.RateUnit);
        Assert.Null(c.WeatherCity); Assert.Null(c.WeatherFavorites); Assert.False(c.WeatherFahrenheit); Assert.False(c.WeatherAutoLocation);   // sin ciudad ni ubicación automática hasta que el usuario lo pida
    }

    [Fact]
    public void Sanitizing_clamps_the_refresh_interval_and_drops_invalid_cities_keeping_the_valid_ones()
    {
        Assert.Equal(500, (new BarConfig { HubRefreshMs = 10 }).Sanitize().HubRefreshMs); Assert.Equal(10_000, (new BarConfig { HubRefreshMs = 999_999 }).Sanitize().HubRefreshMs); Assert.Equal(2000, (new BarConfig { HubRefreshMs = 2000 }).Sanitize().HubRefreshMs);
        Assert.Null(new BarConfig { WeatherCity = "ciudad sin coordenadas" }.Sanitize().WeatherCity); Assert.Equal("Molina|-35.1167|-71.2833", new BarConfig { WeatherCity = "Molina|-35.1167|-71.2833" }.Sanitize().WeatherCity);
        var favs = new BarConfig { WeatherFavorites = "Santiago|-33.45|-70.67;basura;Antofagasta|-23.65|-70.4" }.Sanitize().WeatherFavorites; Assert.Equal("Santiago|-33.45|-70.67;Antofagasta|-23.65|-70.4", favs);
        Assert.Null(new BarConfig { WeatherFavorites = "solo basura" }.Sanitize().WeatherFavorites);
    }

    [Fact]
    public void Patches_set_and_clear_the_tab_unit_and_cities_and_ignore_invalid_values_field_by_field()
    {
        var c = new BarConfig().ApplyPatch(J("{\"hubTab\":\"Disks\",\"rateUnit\":\"Mbps\",\"weatherCity\":\"Molina|-35.1167|-71.2833\",\"weatherFahrenheit\":true,\"hubRefreshMs\":2500,\"showWeather\":false}"));
        Assert.Equal(HubTab.Disks, c.HubTab); Assert.Equal(RateUnit.Mbps, c.RateUnit); Assert.Equal("Molina|-35.1167|-71.2833", c.WeatherCity); Assert.True(c.WeatherFahrenheit); Assert.Equal(2500, c.HubRefreshMs); Assert.False(c.ShowWeather);
        var bad = c.ApplyPatch(J("{\"hubTab\":\"Notificaciones\",\"rateUnit\":7,\"hubRefreshMs\":\"rápido\",\"weatherCity\":\"basura\"}"));
        Assert.Equal(HubTab.Disks, bad.HubTab); Assert.Equal(RateUnit.Mbps, bad.RateUnit); Assert.Equal(2500, bad.HubRefreshMs); Assert.Null(bad.WeatherCity);   // un enum o número inválido no entra; una ciudad inválida se descarta al sanear
        Assert.Null(c.ApplyPatch(J("{\"weatherCity\":null}")).WeatherCity);   // se puede quitar la ciudad
    }

    [Fact]
    public void The_new_settings_roundtrip_through_the_wire_json_and_the_bar_json_file_format()
    {
        var c = new BarConfig { HubTab = HubTab.Network, RateUnit = RateUnit.MBps, WeatherCity = "Molina|-35.1167|-71.2833", WeatherFavorites = "Santiago|-33.45|-70.67", WeatherFahrenheit = true, HubRefreshMs = 1500 };
        var viaWire = JsonSerializer.Deserialize<BarConfig>(c.ToWireJson(), BarConfig.Wire)!; Assert.Equal(c, viaWire); Assert.Contains("\"hubTab\":\"Network\"", c.ToWireJson());
        Assert.Equal(c, BarConfig.FromJson(c.ToJson()));
        Assert.Equal(new BarConfig(), BarConfig.FromJson("{\"ShowSeconds\":false}"));   // un bar.json anterior a estas opciones sigue cargando con los valores por defecto
    }
}
