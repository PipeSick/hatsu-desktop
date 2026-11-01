using WinMac.Bar;
using WinMac.Bar.Hub;
using WinMac.SystemControl;
using Xunit;

namespace WinMac.Bar.Core.Tests;

/// <summary>Un equipo simulado completo: servicios del Control Center, monitores del hub, prueba de velocidad y tiempo, todo con planificador determinista.</summary>
internal sealed class HubRig
{
    public readonly Rig Base = new(); public readonly FakeSys Sys = new(); public readonly FakeNet Net = new(); public readonly FakeDisks Disks = new(); public readonly HubMonitor Monitor; public readonly SpeedTestRunner Speed; public readonly WeatherService Weather;
    public readonly Queue<Action> Bg = new(), UiQ = new(); public DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc); public readonly WeatherFakeSource Source = new();

    public sealed class WeatherFakeSource : IWeatherSource
    {
        public int Calls; public Exception? Throw;
        public Task<WeatherSnapshot> FetchAsync(GeoPoint place, IReadOnlyList<GeoPoint> others, CancellationToken ct)
        {
            Calls++; if (Throw is not null) throw Throw;
            var city = OpenMeteoParser.ParseForecast(WeatherTests.Sample, place); return Task.FromResult(new WeatherSnapshot(city, others.Select(o => new CityBrief(o, 15, 0, true)).ToList(), DateTime.UtcNow));
        }

        public Task<IReadOnlyList<GeoPoint>> SearchAsync(string name, CancellationToken ct) => Task.FromResult<IReadOnlyList<GeoPoint>>([]);
    }

    public HubRig(Func<HttpRequestMessage, HttpResponseMessage>? speedHandler = null)
    {
        var services = new HubServices(Sys, Net, Disks, Source, null, new NoIcons(), speedHandler is null ? null : new SpeedTestEngine(new HttpClient(new WeatherTests.StubHandler(speedHandler)), System.Diagnostics.Stopwatch.GetTimestamp, System.Diagnostics.Stopwatch.Frequency));
        Monitor = new HubMonitor(services, a => Bg.Enqueue(a), a => UiQ.Enqueue(a), Base.Log.Add, () => Now);
        Speed = new SpeedTestRunner(services.SpeedTest, a => Bg.Enqueue(a), a => UiQ.Enqueue(a));
        Weather = new WeatherService(Source, new WeatherCache(null), () => Now, a => Bg.Enqueue(a), a => UiQ.Enqueue(a), Base.Log.Add);
        Base.Ops = new PanelOperations(Base.Hub, Base.Ui, Base.Log.Add, TimeSpan.Zero, new HubRuntime(Monitor, Speed, Weather));
    }

    public void Pump() { while (Bg.Count > 0 || UiQ.Count > 0 || Base.Background.Count > 0 || Base.UiThread.Count > 0) { while (Bg.Count > 0) Bg.Dequeue()(); while (UiQ.Count > 0) UiQ.Dequeue()(); Base.Pump(); } }

    /// <summary>Lee todo (como al abrir el panel en cada pestaña) y deja el contexto del panel con datos reales.</summary>
    public HubRig LoadAll(HubTab tab = HubTab.Sound)
    {
        Base.LoadAll(); Base.Hub.RefreshInput(); Base.Hub.RefreshSessions(); Monitor.RefreshSystem(); Monitor.RefreshNetwork(true); Monitor.RefreshDisks(true); Pump();
        Now = Now.AddSeconds(1); Net.Next = Net.Next with { BytesReceived = Net.Next.BytesReceived + 222_000, BytesSent = Net.Next.BytesSent + 1000 }; Monitor.RefreshNetwork(true); Monitor.RefreshSystem(); Monitor.RefreshDisks(); Pump();
        Base.Ui.Ctx = Base.Ui.Ctx with { Tab = tab }; Sync(); return this;
    }

    public void Sync() { Base.Ui.Ctx = Base.Ui.Ctx with { Sys = Base.Hub.Snapshot, Hub = new HubData(Monitor, Speed.State, Weather.Current(), Base.Ui.Ctx.Hub?.SelectedVolume, Base.Ui.Ctx.Cfg.Use24Hour) }; }
    public PanelModel Hub() { Sync(); return PanelBuilder.HubPanel(Base.Ui.Ctx); }
    public PanelModel WeatherPanel() { Sync(); return PanelBuilder.WeatherPanel(Base.Ui.Ctx); }
    public void Click(string id) { Base.Click(id); Pump(); Sync(); }
    public void Tab(HubTab t) { Base.Ui.Ctx = Base.Ui.Ctx with { Tab = t }; Sync(); }
}

public class HubPanelModelTests
{
    internal static Widget W(PanelModel m, string id) => m.Widgets.Single(w => w.Id == id);
    internal static bool Has(PanelModel m, string id) => m.Widgets.Any(w => w.Id == id);

    [Fact]
    public void There_is_ONE_panel_with_a_horizontal_tab_strip_header_logo_and_permanent_bottom_buttons_on_every_tab()
    {
        var r = new HubRig().LoadAll();
        foreach (var tab in Enum.GetValues<HubTab>())
        {
            r.Tab(tab); var m = r.Hub();
            Assert.Equal(PanelKind.Hub, m.Kind); Assert.Equal(PanelLayout.HubWidth, m.Width);
            Assert.Equal(["logo", "tabs", "settings:" + tab], m.Widgets.Take(3).Select(w => w.Id));
            var tabs = (TabsData)W(m, "tabs").Data!; Assert.Equal(4, tabs.Tabs.Count); Assert.Equal((int)tab, tabs.Selected);
            Assert.Equal(["hub:settings", "hub:quit"], m.Widgets.TakeLast(2).Select(w => w.Id)); Assert.Equal([0, 1], m.Widgets.TakeLast(2).Select(w => w.Col));   // Ajustes y Salir, siempre, lado a lado
            Assert.Equal(3, m.Card!.Value.From); Assert.Equal(m.Widgets.Count - 3, m.Card.Value.To);   // la tarjeta envuelve el contenido y no los botones
        }
    }

    [Fact]
    public void The_four_tabs_are_sound_system_network_and_disks_and_the_app_identity_is_decoupled_from_the_panel_logic()
    {
        Assert.Equal(["Sound", "System", "Network", "Disks"], HubTabs.All.Select(t => t.Id)); Assert.Equal(["sound", "cpu", "globe", "disk"], HubTabs.All.Select(t => t.Glyph));
        var logo = W(new HubRig().LoadAll().Hub(), "logo"); Assert.Equal(AppIdentity.Current.Name, logo.Text); Assert.Equal(AppIdentity.Current.LogoKey, logo.Data);   // nombre y logotipo salen de UN registro: cambiarlos no toca el panel
        Assert.Equal(HubTab.Network, Enum.Parse<HubTab>("Network")); Assert.True(HubTabs.TryParse("Disks", out _)); Assert.False(HubTabs.TryParse("Notifications", out _)); Assert.False(HubTabs.TryParse("99", out _));
    }

    [Fact]
    public void The_card_wraps_only_the_content_with_inset_and_the_bottom_buttons_sit_below_it_in_the_layout()
    {
        var r = new HubRig().LoadAll(HubTab.Disks); var m = r.Hub(); var f = PanelLayout.Compute(m);
        Assert.Equal(PanelLayout.HubWidth, f.Width); Assert.NotNull(f.Card);
        var content = f.Items.Skip(3).Take(m.Widgets.Count - 5).ToList(); var card = f.Card!;
        Assert.All(content, i => { Assert.True(i.X >= card.X + PanelLayout.CardInset - 0.01 && i.X + i.Width <= card.X + card.W - PanelLayout.CardInset + 0.01); Assert.True(i.Y >= card.Y && i.Y + i.Height <= card.Y + card.H); });
        var buttons = f.Items.TakeLast(2).ToList(); Assert.All(buttons, b => Assert.True(b.Y >= card.Y + card.H)); Assert.True(f.Height > buttons[0].Y + buttons[0].Height);
        Assert.Equal(f.Items.OrderBy(i => i.Y).Select(i => i.W.Id), f.Items.Select(i => i.W.Id));   // el orden vertical sigue el orden del modelo
    }

    [Fact]
    public void The_hub_and_weather_are_two_independent_bar_items_and_there_are_no_separate_icons_for_the_tabs()
    {
        var (_, right) = BarModel.Build(BarState.Empty(DateTime.Now) with { Volume = new VolumeInfo(true, 50, false) }, new BarConfig());
        Assert.Equal(BarItemKind.Hub, right[0].Kind); Assert.Equal(BarItemKind.Weather, right[1].Kind); Assert.Equal(1, right.Count(i => i.Kind == BarItemKind.Hub)); Assert.Equal(1, right.Count(i => i.Kind == BarItemKind.Weather));
        Assert.Equal(PanelKind.Hub, BarModel.ClickAction(right[0]).Panel); Assert.Equal(PanelKind.Weather, BarModel.ClickAction(right[1]).Panel);
        var (_, off) = BarModel.Build(BarState.Empty(DateTime.Now), new BarConfig { ShowHub = false, ShowWeather = false }); Assert.DoesNotContain(off, i => i.Kind is BarItemKind.Hub or BarItemKind.Weather);
        var (_, txt) = BarModel.Build(BarState.Empty(DateTime.Now) with { WeatherInfo = new WeatherBarInfo("rain", "12°C") }, new BarConfig()); Assert.Equal("12°C", txt.Single(i => i.Kind == BarItemKind.Weather).Text);
        Assert.True(BarModel.IsClickable(BarItemKind.Hub)); Assert.True(BarModel.IsClickable(BarItemKind.Weather));
    }

    // ───────── Sonido (FOTO 2)
    [Fact]
    public void Sound_shows_output_volume_microphone_and_one_row_per_app_with_real_values()
    {
        var r = new HubRig().LoadAll(HubTab.Sound); var m = r.Hub();
        var outSel = (SelectData)W(m, "pick:out").Data!; Assert.Equal("Altavoces", outSel.Current); Assert.Equal("Salida", outSel.Label);
        var vol = W(m, "volume"); Assert.Equal("40%", vol.Sub); Assert.Equal(40, vol.Value); Assert.Equal("mute", vol.Data); Assert.Equal("sound", vol.Glyph);
        Assert.Equal("Micrófono", ((SelectData)W(m, "pick:in").Data!).Current); Assert.Equal("50%", W(m, "mic").Sub); Assert.Equal("mic-mute", W(m, "mic").Data);
        var apps = m.Widgets.Where(w => w.Kind == WidgetKind.AppRow).ToList(); Assert.Equal(["pid:10", "system"], apps.Select(a => a.Id)); var spotify = (AppRowData)apps[0].Data!;
        Assert.Equal("Spotify", spotify.Name); Assert.Equal(80, spotify.Volume); Assert.True(spotify.Active); Assert.Equal(@"C:\Spotify.exe", spotify.ExePath); Assert.False(((AppRowData)apps[1].Data!).Active);
        Assert.True(Has(m, "open:ms-settings:sound")); Assert.True(Has(m, "open:ms-settings:apps-volume"));
    }

    [Fact]
    public void Muted_output_and_microphone_change_their_icons_and_unavailable_controls_are_disabled_with_the_reason_not_faked()
    {
        var r = new HubRig(); r.Base.Audio.State = new AudioState(true, 40, true, r.Base.Audio.State.Outputs); r.Base.Audio.Input = new AudioInputState(true, 0, true, false, [new("mic", "Interno", true)], "Este micrófono no permite ajustar el volumen desde Windows");
        r.Base.Audio.Sessions = AudioSessionsState.None("No se pudieron leer las sesiones de audio"); r.LoadAll(); var m = r.Hub();
        Assert.Equal("sound-muted", W(m, "volume").Glyph); Assert.Equal("mic-muted", W(m, "mic").Glyph);
        Assert.False(W(m, "mic").Enabled); Assert.Contains("no permite ajustar", W(m, "mic").Reason); Assert.Null(W(m, "mic").Sub);
        Assert.DoesNotContain(m.Widgets, w => w.Kind == WidgetKind.AppRow); Assert.Contains("No se pudieron leer", W(m, "apps-none").Text);
        var none = new HubRig(); none.Base.Audio.State = AudioState.None("No hay dispositivo de salida de audio"); none.LoadAll(); var mn = none.Hub();
        Assert.False(W(mn, "pick:out").Enabled); Assert.False(W(mn, "volume").Enabled); Assert.Contains("No hay dispositivo", W(mn, "volume").Reason);
    }

    [Fact]
    public void An_empty_session_list_says_so_and_only_the_first_eight_apps_are_listed()
    {
        var r = new HubRig(); r.Base.Audio.Sessions = new AudioSessionsState(true, []); r.LoadAll(); Assert.Contains("Ninguna aplicación", W(r.Hub(), "apps-none").Text);
        var many = new HubRig(); many.Base.Audio.Sessions = new AudioSessionsState(true, Enumerable.Range(1, 12).Select(i => new AudioSession("pid:" + i, i, "App" + i, null, 50, false, false, true)).ToList()); many.LoadAll();
        Assert.Equal(8, many.Hub().Widgets.Count(w => w.Kind == WidgetKind.AppRow));
    }

    // ───────── Sistema (FOTO 3)
    [Fact]
    public void System_shows_temperatures_usage_with_history_charts_memory_uptime_and_devices()
    {
        var r = new HubRig().LoadAll(HubTab.System); var m = r.Hub();
        Assert.Equal("57 °C", W(m, "temp-cpu").Text); Assert.Equal("CPU", W(m, "temp-cpu").Sub); Assert.Equal("54 °C", W(m, "temp-gpu").Text); Assert.Equal([0, 1], [W(m, "temp-cpu").Col, W(m, "temp-gpu").Col]);
        var cpu = W(m, "use-cpu"); Assert.Equal("25%", cpu.Sub); Assert.Equal(0.25, cpu.Value, 3); Assert.Equal("blue", ((MeterData)cpu.Data!).Color); Assert.Equal("yellow", ((MeterData)W(m, "use-gpu").Data!).Color);   // 72 %: amarillo como en la referencia
        var chart = (ChartData)W(m, "chart-cpu").Data!; Assert.Equal(HubMonitor.HistoryLength, chart.Series[0].Values.Count); Assert.Equal("100%", chart.MaxLabel); Assert.Equal(100, chart.Max);
        Assert.Equal("Presión: Normal", W(m, "mem-pressure").Text); Assert.Equal("dot-green", W(m, "mem-pressure").Glyph); Assert.Equal("12 GB / 16 GB", W(m, "mem-pressure").Sub); Assert.Equal("4 GB", W(m, "mem-avail").Sub);
        Assert.Equal("4 GB", W(m, "mem-comp").Sub); Assert.Equal("1d 19h", W(m, "uptime").Sub); Assert.Equal("0", W(m, "devices").Sub);
    }

    [Fact]
    public void Sensors_that_cannot_be_read_say_so_with_the_reason_and_draw_no_invented_chart()
    {
        var r = new HubRig(); r.Sys.Next = FakeSys.Sample(40, null, null, null); r.LoadAll(HubTab.System); var m = r.Hub();
        Assert.False(W(m, "temp-cpu").Enabled); Assert.Equal("No disponible", W(m, "temp-cpu").Text); Assert.Contains("sin sensor", W(m, "temp-cpu").Sub); Assert.Contains("sin sensor", W(m, "temp-gpu").Sub);
        Assert.Equal("No disponible", W(m, "use-gpu").Sub); Assert.False(Has(m, "chart-gpu")); Assert.True(Has(m, "chart-cpu")); Assert.Contains("sin GPU", W(m, "use-gpu-why").Text);
    }

    [Fact]
    public void Memory_pressure_levels_unavailable_extras_and_the_acpi_caveat_are_all_reported_honestly()
    {
        var r = new HubRig(); var low = new MemoryInfo(16UL << 30, 1UL << 30, null, 10UL << 30, 32UL << 30, null, null);
        r.Sys.Next = FakeSys.Sample(10, 10, 40, 40) with { Memory = low, CpuTempNote = "zona térmica ACPI (aproximada)" }; r.LoadAll(HubTab.System); var m = r.Hub();
        Assert.Equal("Presión: Crítica", W(m, "mem-pressure").Text); Assert.Equal("dot-red", W(m, "mem-pressure").Glyph);
        Assert.Equal("No disponible", W(m, "mem-comp").Sub); Assert.False(W(m, "mem-comp").Enabled); Assert.Equal("No disponible", W(m, "mem-page").Sub);
        Assert.Contains("zona térmica ACPI", W(m, "temp-note").Text); Assert.Contains("sin un controlador", W(m, "temp-note").Text);
        var noMem = new HubRig(); noMem.Sys.Next = FakeSys.Sample(10, 10, 40, 40) with { Memory = null, MemoryReason = "Windows no entregó el uso de memoria" }; noMem.LoadAll(HubTab.System); Assert.Contains("no entregó", W(noMem.Hub(), "mem-none").Text);
    }

    // ───────── Red (FOTO 4)
    [Fact]
    public void Network_shows_rates_in_the_chosen_unit_history_session_adapter_ip_and_apps()
    {
        var r = new HubRig().LoadAll(HubTab.Network); var m = r.Hub(); var st = (StatsData)W(m, "stats-net").Data!;
        Assert.Equal("222 KB/s", st.Left.Value); Assert.Equal("Descarga", st.Left.Label); Assert.Equal("1,0 KB/s", st.Right.Value); Assert.Equal("Subida", st.Right.Label); Assert.Equal("Auto", st.Pill);
        var ch = (ChartData)W(m, "chart-net").Data!; Assert.Equal(2, ch.Series.Count); Assert.Equal(["blue", "green"], ch.Series.Select(s => s.Color)); Assert.Equal("500 KB/s", ch.MaxLabel);
        Assert.Equal("192.168.20.175 (Wi-Fi)", W(m, "ip").Sub); Assert.Equal("Wi-Fi", W(m, "kind").Sub); Assert.Equal("Wi-Fi", W(m, "adapter").Sub); Assert.Equal("1200 Mb/s", W(m, "link").Sub);
        Assert.Equal("↓217 KB  ↑1000 B", W(m, "session").Sub);   // 222 000 B y 1 000 B acumulados entre las dos lecturas (base 1024)
        Assert.Equal("12 conexiones", W(m, "app-10").Sub); Assert.Equal("chrome", W(m, "app-10").Text); Assert.Contains("no permite medir", W(m, "apps-note").Text);
    }

    [Fact]
    public void The_rate_unit_selection_changes_every_network_figure_and_cycles_through_all_four_units()
    {
        var r = new HubRig().LoadAll(HubTab.Network);
        foreach (var (unit, left, pill) in new[] { (RateUnit.KBps, "222 KB/s", "KB/s"), (RateUnit.MBps, "0,2 MB/s", "MB/s"), (RateUnit.Mbps, "1,8 Mb/s", "Mb/s"), (RateUnit.Auto, "222 KB/s", "Auto") })
        {
            r.Base.Ui.Ctx = r.Base.Ui.Ctx with { Cfg = r.Base.Ui.Ctx.Cfg with { RateUnit = unit } }; var st = (StatsData)W(r.Hub(), "stats-net").Data!; Assert.Equal(left, st.Left.Value); Assert.Equal(pill, st.Pill);
        }

        r.Base.Ui.Ctx = r.Base.Ui.Ctx with { Cfg = new BarConfig() }; var seen = new List<RateUnit>();
        for (int i = 0; i < 4; i++) { r.Click("unit"); seen.Add(r.Base.Ui.Ctx.Cfg.RateUnit); }
        Assert.Equal([RateUnit.KBps, RateUnit.MBps, RateUnit.Mbps, RateUnit.Auto], seen); Assert.Equal(4, r.Base.Ui.Patches.Count);
    }

    [Fact]
    public void Without_apps_or_adapter_the_panel_says_so_and_never_invents_traffic()
    {
        var r = new HubRig(); r.Net.Apps = []; r.LoadAll(HubTab.Network); Assert.Contains("Ninguna app usa la red", W(r.Hub(), "apps-none").Text);
        var off = new HubRig(); off.Net.Next = new NetCounters(0, 0, null, "Sin conexión de red"); off.LoadAll(HubTab.Network); var m = off.Hub(); Assert.Contains("Sin conexión", W(m, "net-none").Text); Assert.False(Has(m, "adapter")); Assert.Equal("0 B/s", ((StatsData)W(m, "stats-net").Data!).Left.Value);
        var noApps = new HubRig(); noApps.Net.AppsThrow = new UnauthorizedAccessException(); noApps.LoadAll(HubTab.Network); Assert.Contains("aplicaciones", W(noApps.Hub(), "apps-why").Text);
    }

    [Fact]
    public void The_speed_test_section_warns_about_data_use_and_shows_each_state_progress_result_cancel_and_failure()
    {
        var r = new HubRig(req => req.Method == HttpMethod.Post ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2000]) }).LoadAll(HubTab.Network); var m = r.Hub();
        Assert.Equal("Prueba de velocidad", W(m, "speedtest").Text); Assert.Contains("Consume datos", W(m, "speed-warn").Text); Assert.Contains("Solo se ejecuta cuando la pulsas", W(m, "speed-warn").Text);
        r.Base.Ui.ConfirmAnswer = true; r.Click("speedtest"); Assert.Equal(SpeedTestStatus.Done, r.Speed.State.Status); var done = r.Hub(); Assert.True(Has(done, "speed-down")); Assert.True(Has(done, "speed-lat")); Assert.Equal("Repetir la prueba de velocidad", W(done, "speedtest").Text);
        var running = new HubRig(req => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([]) }).LoadAll(HubTab.Network); running.Speed.Start(); running.Sync(); var pm = running.Hub();
        Assert.Equal(SpeedTestStatus.Running, running.Speed.State.Status); Assert.StartsWith("Midiendo", W(pm, "speed-progress").Text); Assert.True(Has(pm, "speedtest-cancel")); Assert.False(Has(pm, "speedtest"));
        var failed = new HubRig(_ => throw new HttpRequestException("dns")).LoadAll(HubTab.Network); failed.Click("speedtest"); Assert.Contains("sin conexión", W(failed.Hub(), "speed-msg").Text); Assert.Equal("Volver a intentar", W(failed.Hub(), "speedtest").Text);
    }

    // ───────── Discos (FOTO 5)
    [Fact]
    public void Disks_show_volumes_usage_activity_smart_external_protection_and_tools()
    {
        var r = new HubRig().LoadAll(HubTab.Disks); var m = r.Hub();
        var tile = W(m, "disk:C:"); Assert.True(tile.On); Assert.Equal("75% usado", tile.Sub); Assert.Equal("disk", tile.Glyph);
        Assert.Equal("NTFS · SSD · Interno", W(m, "vol-name").Sub); var use = W(m, "vol-usage"); Assert.Equal(0.75, use.Value, 3); Assert.Equal("yellow", ((MeterData)use.Data!).Color); Assert.Equal("75% usado", use.Sub);
        Assert.Equal("250 B", W(m, "vol-free").Sub); Assert.Equal("750 B / 1000 B", W(m, "vol-cap").Sub);
        Assert.True(Has(m, "stats-io")); var io = (ChartData)W(m, "chart-io").Data!; Assert.Equal(["blue", "red"], io.Series.Select(s => s.Color));
        Assert.Equal("Saludable".Replace("Saludable", "Healthy"), W(m, "smart-state").Sub); Assert.Equal("37 °C", W(m, "smart-temp").Sub); Assert.Equal("1% usado", W(m, "smart-wear").Sub); Assert.Equal("639", W(m, "smart-hours").Sub);
        Assert.False(W(m, "eject:C:").Enabled); Assert.False(W(m, "eject-all").Enabled); Assert.Contains("No hay discos externos", W(m, "ext-note").Text);
        Assert.Equal("explorer:C:", m.Widgets.Single(w => w.Id.StartsWith("explorer:")).Id); Assert.True(Has(m, "open:ms-settings:storagesense"));
    }

    [Fact]
    public void Smart_data_is_shown_for_the_selected_volumes_own_disk_only_and_missing_fields_say_not_available()
    {
        var r = new HubRig(); r.Disks.Catalog = DiskCatalog.Build(
            [new PhysicalRec(0, "NVMe", "SSD", "NVMe", 1000, new SmartInfo("Healthy", 37, 639, 1, null, null)), new PhysicalRec(1, "USB Elements", "HDD", "USB", 1000, null)],
            [new PartitionRec(0, "C"), new PartitionRec(1, "E")], [new VolumeRec("C:\\", "", "NTFS", 1000, 250, false), new VolumeRec("E:\\", "Respaldo", "NTFS", 900, 100, true)]);
        r.Disks.Io = [new DiskIo(0, 1000, 500), new DiskIo(1, 100, 100)]; r.LoadAll(HubTab.Disks);
        r.Click("disk:E:"); var m = r.Hub();
        Assert.True(W(m, "disk:E:").On); Assert.False(W(m, "disk:C:").On); Assert.Equal("USB Elements", W(m, "smart-model").Text);
        Assert.Equal("No disponible", W(m, "smart-state").Sub); Assert.Equal("No disponible", W(m, "smart-temp").Sub); Assert.Equal("No disponible", W(m, "smart-hours").Sub); Assert.Contains("no entrega", W(m, "smart-why").Text);   // el disco USB NO hereda los 37 °C del NVMe
        Assert.Equal("HDD · Externo".Replace("HDD · Externo", "NTFS · HDD · Externo"), W(m, "vol-name").Sub);
        Assert.True(W(m, "eject:E:").Enabled); Assert.True(W(m, "eject-all").Enabled); Assert.Contains("mecanismo seguro", W(m, "ext-note").Text);
    }

    [Fact]
    public void A_volume_without_a_physical_disk_shows_neither_activity_nor_smart_and_says_why()
    {
        var r = new HubRig(); r.Disks.Catalog = DiskCatalog.Build([new PhysicalRec(0, "NVMe", "SSD", "NVMe", 1000, null)], [new PartitionRec(0, "C")], [new VolumeRec("C:\\", "", "NTFS", 1000, 250, false), new VolumeRec("Y:\\", "Red", "NTFS", 500, 500, false)]); r.LoadAll(HubTab.Disks);
        r.Click("disk:Y:"); var m = r.Hub(); Assert.Contains("No se pudo asociar", W(m, "io-none").Text); Assert.False(Has(m, "stats-io")); Assert.Contains("no está asociado", W(m, "smart-none").Text); Assert.False(Has(m, "smart-temp"));
        var empty = new HubRig(); empty.Disks.Catalog = DisksSnapshot.None("Windows no expone la información de discos físicos"); empty.LoadAll(HubTab.Disks); Assert.Contains("no expone", W(empty.Hub(), "disks-none").Text);
    }

    [Fact]
    public void The_selected_volume_falls_back_to_c_or_the_first_and_external_volumes_are_found_by_bus_or_removable_flag()
    {
        var snap = DiskCatalog.Build([new PhysicalRec(0, "A", "SSD", "NVMe", 1, null), new PhysicalRec(1, "B", "HDD", "USB", 1, null)], [new PartitionRec(0, "D"), new PartitionRec(1, "E")],
            [new VolumeRec("D:\\", "", "NTFS", 1, 1, false), new VolumeRec("E:\\", "", "NTFS", 1, 1, false), new VolumeRec("F:\\", "", "FAT32", 1, 1, true)]);
        Assert.Equal("D:", HubSelection.Volume(snap, null)); Assert.Equal("E:", HubSelection.Volume(snap, "E:")); Assert.Equal("D:", HubSelection.Volume(snap, "Z:"));
        Assert.Equal(["E:", "F:"], HubSelection.External(snap).Select(v => v.Letter)); Assert.Null(HubSelection.Volume(DisksSnapshot.None("x"), "C:"));
    }

    // ───────── Tiempo (FOTO 1)
    [Fact]
    public void Weather_composes_city_big_temperature_condition_high_low_hourly_strip_extra_cities_and_the_open_app_action()
    {
        var r = new HubRig(); r.Base.Ui.Ctx = r.Base.Ui.Ctx with { Cfg = new BarConfig { Use24Hour = false } }; r.Weather.Configure(new GeoPoint("Molina", null, -35.1167, -71.2833), [new GeoPoint("Santiago", null, -33.45, -70.67), new GeoPoint("Antofagasta", null, -23.65, -70.4)]); r.Weather.Refresh(); r.Pump();
        var m = r.WeatherPanel(); Assert.Equal(PanelKind.Weather, m.Kind); Assert.Equal(PanelLayout.WeatherWidth, m.Width);
        var head = (WeatherHeadData)W(m, "head").Data!; Assert.Equal("Molina", head.Place); Assert.Equal("12°", head.Temp); Assert.Equal("Mayormente nublado", head.Description); Assert.Equal("Máx. 21° Mín. 12°", head.HighLow); Assert.Equal("cloudy", head.IconKey);
        var strip = (StripData)W(m, "strip").Data!; Assert.Equal(5, strip.Items.Count); Assert.Equal(["6 a. m.", "6:42 a. m.", "7 a. m.", "8 a. m.", "9 a. m."], strip.Items.Select(i => i.Label)); Assert.Equal(10, strip.Items[3].PrecipProb);
        Assert.Equal(["Santiago", "Antofagasta"], m.Widgets.Where(w => w.Kind == WidgetKind.CityRow).Select(w => w.Text)); Assert.Equal("15°", m.Widgets.First(w => w.Kind == WidgetKind.CityRow).Sub);
        Assert.Equal("Abrir la app Tiempo", W(m, "weather:open").Text);
    }

    [Fact]
    public void Weather_states_no_city_loading_offline_with_stale_data_and_fahrenheit_are_each_explicit()
    {
        var r = new HubRig(); var none = r.WeatherPanel(); Assert.True(Has(none, "nocity")); Assert.Equal("hub:settings-weather", none.Widgets.Single(w => w.Kind == WidgetKind.Button).Id);
        r.Weather.Configure(new GeoPoint("Molina", null, -35.1, -71.2), []); var loading = r.WeatherPanel(); Assert.Contains("Cargando", W(loading, "wx-msg").Text);
        r.Weather.Refresh(); r.Pump(); r.Now = r.Now.AddMinutes(20); r.Source.Throw = new HttpRequestException("sin red"); r.Weather.Refresh(); r.Pump();
        var off = r.WeatherPanel(); Assert.True(Has(off, "head")); Assert.StartsWith("Sin conexión", W(off, "wx-stale").Text); Assert.Contains("última actualización", W(off, "wx-stale").Text); Assert.True(Has(off, "weather:refresh"));   // sigue mostrando los datos, marcados
        r.Base.Ui.Ctx = r.Base.Ui.Ctx with { Cfg = new BarConfig { WeatherFahrenheit = true } }; r.Source.Throw = null; Assert.Equal("54°", ((WeatherHeadData)W(r.WeatherPanel(), "head").Data!).Temp);
        var bad = new HubRig(); bad.Weather.Configure(new GeoPoint("X", null, 1, 1), []); bad.Source.Throw = new HttpRequestException("503", null, System.Net.HttpStatusCode.ServiceUnavailable); bad.Weather.Refresh(); bad.Pump(); var e = bad.WeatherPanel(); Assert.Contains("503", W(e, "wx-msg").Text); Assert.True(Has(e, "weather:refresh"));
    }
}
