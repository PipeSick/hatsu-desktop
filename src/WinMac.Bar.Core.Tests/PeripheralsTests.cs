using WinMac.Bar.Peripherals;
using Xunit;

namespace WinMac.Bar.Tests;

public class PeripheralsTests
{
    private static readonly DateTime T0 = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static RawReading R(string id = "mouse", int? pct = 80, bool connected = true, ChargeState ch = ChargeState.Unknown, BatteryCapability cap = BatteryCapability.Unknown, string name = "Ratón de prueba") =>
        new(id, name, DeviceKind.Mouse, ConnectionKind.Bluetooth, connected, pct, ch, cap);
    private static ProviderResult Ok(params RawReading[] r) => new("bt", ProviderStatus.Ok, r);

    // ── batería: válida, desconocida, caducada, desconectada
    [Fact] public void Valid_fresh_reading_is_current()
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: 80)), T0); var d = s.View(T0.AddMinutes(1)).Single(); Assert.Equal(BatteryStatus.Current, d.Battery); Assert.Equal(80, d.Percent); }
    [Fact] public void Missing_percent_is_unknown_never_100()
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: null)), T0); var d = s.View(T0).Single(); Assert.Equal(BatteryStatus.Unknown, d.Battery); Assert.Null(d.Percent); }
    [Theory] [InlineData(-1)] [InlineData(101)] [InlineData(255)] [InlineData(1000)]
    public void Out_of_range_percent_is_discarded(int bad)
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: bad)), T0); var d = s.View(T0).Single(); Assert.Null(d.Percent); Assert.Equal(BatteryStatus.Unknown, d.Battery); }
    [Fact] public void Old_reading_is_stale_not_current()
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: 60)), T0); var d = s.View(T0.AddMinutes(16)).Single(); Assert.Equal(BatteryStatus.Stale, d.Battery); Assert.Null(d.ChargeText); }
    [Fact] public void Reading_from_the_future_is_not_current()
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: 60)), T0.AddMinutes(10)); Assert.Equal(BatteryStatus.Stale, s.View(T0).Single().Battery); }
    [Fact] public void Disconnected_keeps_last_reading_as_stored_and_hides_charging()
    {
        var s = new PeripheralStore(); s.Apply(Ok(R(pct: 70, ch: ChargeState.Charging)), T0);
        s.Apply(Ok(R(pct: 70, connected: false)), T0.AddMinutes(2)); var d = s.View(T0.AddMinutes(2)).Single();
        Assert.Equal(BatteryStatus.Disconnected, d.Battery); Assert.Equal(70, d.Percent); Assert.Null(d.ChargeText); Assert.Equal(ChargeState.Unknown, d.Charge);
    }
    [Fact] public void Device_that_vanishes_from_its_provider_is_disconnected()
    { var s = new PeripheralStore(); s.Apply(Ok(R("a"), R("b")), T0); s.Apply(Ok(R("a")), T0.AddMinutes(1)); var v = s.View(T0.AddMinutes(1)); Assert.True(v.Single(x => x.Id.EndsWith("|a")).Connected); Assert.False(v.Single(x => x.Id.EndsWith("|b")).Connected); }
    [Fact] public void Device_without_battery_support_is_unsupported_not_unknown()
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: null, cap: BatteryCapability.Unsupported)), T0); Assert.Equal(BatteryStatus.Unsupported, s.View(T0).Single().Battery); }

    // ── cargando solo si el dispositivo lo informa
    [Fact] public void Charging_only_when_reported_and_current()
    {
        var s = new PeripheralStore(); s.Apply(Ok(R(ch: ChargeState.Unknown)), T0); Assert.Null(s.View(T0).Single().ChargeText);
        s.Apply(Ok(R(ch: ChargeState.Charging)), T0); Assert.Equal("Cargando", s.View(T0).Single().ChargeText);
        Assert.Null(s.View(T0.AddMinutes(30)).Single().ChargeText);   // lectura caducada: ya no se afirma
    }

    // ── proveedor que falla o se cuelga
    private sealed class Prov(string id, Func<CancellationToken, Task<IReadOnlyList<RawReading>>> f) : IPeripheralProvider
    { public string Id => id; public string DisplayName => id; public SupportLevel Support => SupportLevel.Experimental; public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => f(ct); }

    [Fact] public async Task Provider_that_hangs_times_out_without_blocking()
    {
        var p = new Prov("slow", async ct => { await Task.Delay(Timeout.Infinite, ct); return []; });
        var sw = System.Diagnostics.Stopwatch.StartNew(); var r = await ProviderRunner.RunAsync(p, TimeSpan.FromMilliseconds(150));
        Assert.Equal(ProviderStatus.TimedOut, r.Status); Assert.True(sw.ElapsedMilliseconds < 2000);
    }
    [Fact] public async Task Provider_that_throws_is_isolated()
    {
        var r = await ProviderRunner.RunAsync(new Prov("bad", _ => throw new InvalidOperationException("x")), TimeSpan.FromSeconds(2));
        Assert.Equal(ProviderStatus.Failed, r.Status); Assert.Empty(r.Readings);
    }
    [Fact] public async Task A_failing_provider_does_not_erase_what_was_known_but_is_reported()
    {
        var s = new PeripheralStore(); s.Apply(Ok(R(pct: 55)), T0);
        s.Apply(await ProviderRunner.RunAsync(new Prov("bt", _ => throw new Exception("boom")), TimeSpan.FromSeconds(1)), T0.AddMinutes(1));
        var d = s.View(T0.AddMinutes(1)).Single(); Assert.Equal(55, d.Percent); Assert.True(d.ProviderTrouble);
    }
    [Fact] public async Task Healthy_provider_returns_its_readings()
    { var r = await ProviderRunner.RunAsync(new Prov("ok", _ => Task.FromResult<IReadOnlyList<RawReading>>([R()])), TimeSpan.FromSeconds(1)); Assert.Equal(ProviderStatus.Ok, r.Status); Assert.Single(r.Readings); }

    [Fact] public void Percent_of_a_device_never_seen_connected_is_not_trusted()
    { var s = new PeripheralStore(); s.Apply(Ok(R(pct: 60, connected: false)), T0); Assert.Null(s.View(T0).Single().Percent); }
    [Fact] public void Clear_leaves_nothing_in_memory() { var s = new PeripheralStore(); s.Apply(Ok(R()), T0); s.Clear(); Assert.Empty(s.View(T0)); }

    // ── alertas: umbral, histéresis, deduplicación
    private static DeviceView Dv(string id = "d", int pct = 15, BatteryStatus st = BatteryStatus.Current, ChargeState ch = ChargeState.Unknown, bool hidden = false) =>
        new(id, "Ratón", DeviceKind.Mouse, ConnectionKind.Bluetooth, st != BatteryStatus.Disconnected, st, pct, T0, ch, null, hidden, false);

    [Fact] public void Alert_fires_once_when_crossing_the_threshold()
    {
        var e = new PeripheralAlertEngine();
        Assert.Single(e.Evaluate([Dv(pct: 20)], true, 20, T0));
        Assert.Empty(e.Evaluate([Dv(pct: 19)], true, 20, T0.AddMinutes(10)));   // sigue baja: no se repite
        Assert.Empty(e.Evaluate([Dv(pct: 18)], true, 20, T0.AddHours(10)));
    }
    [Fact] public void Alert_rearms_only_after_rising_above_threshold_plus_hysteresis()
    {
        var e = new PeripheralAlertEngine(); e.Evaluate([Dv(pct: 15)], true, 20, T0);
        Assert.Empty(e.Evaluate([Dv(pct: 22)], true, 20, T0.AddHours(5)));            // dentro de la histéresis: sigue desarmada
        e.Evaluate([Dv(pct: 25)], true, 20, T0.AddHours(6));                           // ≥ umbral+5: se rearma
        Assert.Single(e.Evaluate([Dv(pct: 19)], true, 20, T0.AddHours(12)));
    }
    [Fact] public void Alert_has_a_minimum_gap_even_if_it_oscillates()
    {
        var e = new PeripheralAlertEngine(); e.Evaluate([Dv(pct: 15)], true, 20, T0); e.Evaluate([Dv(pct: 40)], true, 20, T0.AddMinutes(30));
        Assert.Empty(e.Evaluate([Dv(pct: 15)], true, 20, T0.AddHours(1)));            // rearmada pero dentro del intervalo mínimo
        Assert.Single(e.Evaluate([Dv(pct: 15)], true, 20, T0.AddHours(5)));
    }
    [Fact] public void Reconnecting_with_low_battery_does_not_repeat_the_alert()
    {
        var e = new PeripheralAlertEngine(); e.Evaluate([Dv(pct: 12)], true, 20, T0);
        e.Evaluate([Dv(pct: 12, st: BatteryStatus.Disconnected)], true, 20, T0.AddHours(1));
        Assert.Empty(e.Evaluate([Dv(pct: 12)], true, 20, T0.AddHours(6)));
    }
    [Theory] [InlineData(BatteryStatus.Stale)] [InlineData(BatteryStatus.Unknown)] [InlineData(BatteryStatus.Unsupported)] [InlineData(BatteryStatus.Disconnected)]
    public void No_alert_without_a_current_reading(BatteryStatus st) => Assert.Empty(new PeripheralAlertEngine().Evaluate([Dv(pct: 5, st: st)], true, 20, T0));
    [Fact] public void No_alert_while_charging_or_hidden_or_disabled()
    {
        var e = new PeripheralAlertEngine();
        Assert.Empty(e.Evaluate([Dv(pct: 5, ch: ChargeState.Charging)], true, 20, T0));
        Assert.Empty(e.Evaluate([Dv(id: "h", pct: 5, hidden: true)], true, 20, T0));
        Assert.Empty(e.Evaluate([Dv(id: "x", pct: 5)], false, 20, T0));
    }
    [Fact] public void Each_device_alerts_independently()
    { var e = new PeripheralAlertEngine(); Assert.Equal(2, e.Evaluate([Dv("a", 10), Dv("b", 12)], true, 20, T0).Count); }
    [Fact] public void Threshold_is_clamped() { Assert.Equal(5, PeripheralSettings.ClampThreshold(0)); Assert.Equal(50, PeripheralSettings.ClampThreshold(99)); Assert.Equal(20, PeripheralSettings.ClampThreshold(20)); }

    // ── ocultos y persistencia
    [Fact] public void Hidden_devices_roundtrip_with_ids_containing_pipes()
    {
        var s = PeripheralSettings.Hide(PeripheralSettings.Hide("", "bt|AA:BB"), "hid|\\\\?\\x"); var set = PeripheralSettings.ParseHidden(s);
        Assert.Contains("bt|AA:BB", set); Assert.Equal(2, set.Count); Assert.DoesNotContain("bt|AA:BB", PeripheralSettings.ParseHidden(PeripheralSettings.Unhide(s, "bt|AA:BB")));
    }
    [Fact] public void Hidden_devices_are_flagged_and_can_be_filtered()
    {
        var st = new PeripheralStore(); st.Apply(Ok(R("a"), R("b")), T0); var hid = new HashSet<string> { "bt|a" };
        Assert.True(st.View(T0, hid).Single(x => x.Id == "bt|a").Hidden);
        Assert.Single(st.View(T0, hid, includeHidden: false));
    }
    [Fact] public void Defaults_are_conservative_and_persist()
    {
        var c = new BarConfig(); Assert.False(c.ShowPeripherals); Assert.False(c.PeripheralAlerts); Assert.False(c.ShowCleanup); Assert.Equal(20, c.PeripheralAlertThreshold);
        var n = c.ApplyPatch(System.Text.Json.JsonDocument.Parse("{\"showPeripherals\":true,\"peripheralAlerts\":true,\"peripheralAlertThreshold\":99,\"showCleanup\":true}").RootElement);
        Assert.True(n.ShowPeripherals && n.PeripheralAlerts && n.ShowCleanup); Assert.Equal(50, n.PeripheralAlertThreshold);
        var back = BarConfig.FromJson(n.ToJson()); Assert.True(back.ShowPeripherals); Assert.Equal(50, back.PeripheralAlertThreshold);
        Assert.Equal(c.ShowBattery, BarConfig.FromJson("{}").ShowBattery);   // los ajustes de antes no cambian
    }

    // ── modelo de barra y panel
    [Fact] public void Bar_items_exist_only_when_enabled_and_are_orderable()
    {
        var s = BarState.Empty(new DateTime(2026, 11, 4, 12, 0, 0));
        Assert.DoesNotContain(BarModel.Build(s, new BarConfig()).Right, i => i.Kind is BarItemKind.Peripherals or BarItemKind.Cleanup);
        var r = BarModel.Build(s, new BarConfig { ShowPeripherals = true, ShowCleanup = true, BarOrder = "Cleanup,Peripherals" }).Right;
        Assert.True(r.ToList().FindIndex(i => i.Id == "Cleanup") < r.ToList().FindIndex(i => i.Id == "Peripherals")); Assert.Equal(PanelKind.Peripherals, BarModel.ClickAction(r.Single(i => i.Kind == BarItemKind.Peripherals)).Panel);
        Assert.Equal(PanelKind.Cleanup, BarModel.ClickAction(r.Single(i => i.Kind == BarItemKind.Cleanup)).Panel);
    }
    [Fact] public void Panel_shows_each_state_without_inventing_data()
    {
        var st = new PeripheralStore(); st.Apply(Ok(R("a", 80, ch: ChargeState.Charging, name: "A"), R("b", null, name: "B"), R("c", 60, name: "C"), R("d", null, cap: BatteryCapability.Unsupported, name: "D")), T0);
        st.Apply(Ok(R("a", 80, ch: ChargeState.Charging, name: "A"), R("b", null, name: "B"), R("c", 60, connected: false, name: "C"), R("d", null, cap: BatteryCapability.Unsupported, name: "D")), T0);
        var view = new PeripheralsView(st.View(T0), 0, false, true, 20);
        var m = PanelBuilder.PeripheralsPanel(PanelContext.Initial(new BarConfig(), T0) with { Periph = view });
        var cards = m.Widgets.Where(w => w.Kind == WidgetKind.DeviceCard).ToDictionary(w => w.Text, w => (DeviceCardData)w.Data!);
        // Solo se muestran los conectados con lectura: A (80 %). B (conectado, sin lectura) va en una línea; C (desconectado) y D (sin datos) no ocupan una tarjeta.
        Assert.Equal("80 %", cards["A"].BatteryText); Assert.Equal("Cargando", cards["A"].Charge); Assert.Equal(0.8, cards["A"].Fraction); Assert.Equal("", cards["A"].Status);
        Assert.Single(cards); Assert.DoesNotContain("C", cards.Keys); Assert.DoesNotContain("B", cards.Keys);
        var nodata = Assert.Single(m.Widgets, w => w.Id == "periph-nodata"); Assert.Contains("B", nodata.Text); Assert.Contains("D", nodata.Text); Assert.DoesNotMatch(@"\bC\b", nodata.Text);
        var st2 = new PeripheralStore(); st2.Apply(Ok(R("c", 60, name: "C")), T0); st2.Apply(Ok(R("c", 60, connected: false, name: "C")), T0);
        var none = PanelBuilder.PeripheralsPanel(PanelContext.Initial(new BarConfig(), T0) with { Periph = new PeripheralsView(st2.View(T0), 0, false, false, 20) });
        Assert.Contains(none.Widgets, w => w.Id == "periph-none");
    }
    [Fact] public void Panel_empty_and_hidden_states_and_hide_event()
    {
        var empty = PanelBuilder.PeripheralsPanel(PanelContext.Initial(new BarConfig(), T0) with { Periph = new PeripheralsView([], 0, false, false, 20) });
        Assert.Contains(empty.Widgets, w => w.Id == "periph-empty" && w.Text.Contains("No se encontró"));
        var st = new PeripheralStore(); st.Apply(Ok(R("a")), T0);
        var m = PanelBuilder.PeripheralsPanel(PanelContext.Initial(new BarConfig(), T0) with { Periph = new PeripheralsView(st.View(T0), 2, false, false, 20) });
        Assert.Contains(m.Widgets, w => w.Id == PanelBuilder.PeriphUnhide && w.Text.Contains("2"));
        var c = new PanelController(); c.SetModel(m); Assert.Contains(c.Targets(), t => t.Id == PanelBuilder.PeriphHidePrefix + "bt|a");
        var f = PanelLayout.Compute(m, 900, 0).Items.First(i => i.W.Kind == WidgetKind.DeviceCard);
        Assert.True(PanelLayout.DeviceHideHit(f, f.X + f.Width - 10, f.Y + 10)); Assert.False(PanelLayout.DeviceHideHit(f, f.X + 10, f.Y + 10));
    }
}

public class AsusRogProtocolTests
{
    private static readonly DateTime T0 = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static ProviderResult Ok(params RawReading[] r) => new("t", ProviderStatus.Ok, r);
    private static byte[] Hex(string h) { var b = new byte[64]; var s = Convert.FromHexString(h); Array.Copy(s, b, s.Length); return b; }
    private static AsusRogProtocol.Query Kb => AsusRogProtocol.OmniQueries.Single(q => q.Role == AsusRogProtocol.Role.Keyboard);
    private static AsusRogProtocol.Query Mouse => AsusRogProtocol.OmniQueries.Single(q => q.Role == AsusRogProtocol.Role.Mouse);

    [Fact] public void Requests_are_exactly_what_armoury_crate_sent_in_the_capture()
    {
        Assert.Equal(Hex("0212010000"), Kb.Request()); Assert.Equal(Hex("0312070000"), Mouse.Request());
        Assert.Equal(64, Kb.Request().Length); Assert.Equal(0xFF00, Kb.UsagePage); Assert.Equal(0xFF01, Mouse.UsagePage);
    }

    [Fact] public void Real_responses_from_the_capture_give_the_batteries_shown_by_armoury_crate()
    {
        // Teclado Strix Scope II 96: 60 %; ratón Keris II Origin: 97 % (valores de Armoury Crate durante la captura)
        Assert.Equal(60, AsusRogProtocol.ParsePercent(Kb, Hex("0212010000003C020100143C9F0F")));
        Assert.Equal(60, AsusRogProtocol.ParsePercent(Kb, Hex("0212010000003C020100143CAA0F")));
        Assert.Equal(97, AsusRogProtocol.ParsePercent(Mouse, Hex("0312070000610214251000000100")));
        Assert.Equal(97, AsusRogProtocol.ParsePercent(Mouse, Hex("03120700006102141A1000000100")));
    }

    [Fact] public void Anything_else_is_rejected_instead_of_guessed()
    {
        Assert.Null(AsusRogProtocol.ParsePercent(Kb, null)); Assert.Null(AsusRogProtocol.ParsePercent(Kb, new byte[4]));
        Assert.Null(AsusRogProtocol.ParsePercent(Kb, Hex("0312070000610214251000000100")));            // respuesta de otra consulta (ratón) recibida por el teclado
        Assert.Null(AsusRogProtocol.ParsePercent(Mouse, Hex("0212010000003C020100143C9F0F")));
        Assert.Null(AsusRogProtocol.ParsePercent(Kb, Hex("021201000000" + "00" + "020100143C9F0F")));            // 0 %: no es creíble (dormido o sin dato)
        Assert.Null(AsusRogProtocol.ParsePercent(Kb, Hex("021201000000" + "FF" + "020100143C9F0F")));            // 255: fuera de 0–100
        Assert.Null(AsusRogProtocol.ParsePercent(Kb, Hex("0212030000003C020100143C9F0F")));            // otra subconsulta (03 en vez de 01)
    }

    [Fact] public void Names_and_kinds_do_not_claim_a_model_the_receiver_does_not_report()
    {
        Assert.Contains("Teclado ROG", AsusRogProtocol.DisplayName(AsusRogProtocol.Role.Keyboard)); Assert.Equal(DeviceKind.Mouse, AsusRogProtocol.Kind(AsusRogProtocol.Role.Mouse));
        Assert.False(new BarConfig().PeripheralAsusRog);   // opt-in: manda una consulta al receptor
    }

    [Fact] public void A_recognized_receiver_without_battery_support_is_listed_by_name_apart_from_connected_devices_without_data()
    {
        var st = new PeripheralStore();
        var rx = new RawReading("asus-rx:1AFA", "ROG DELTA II (2.4GHz)", DeviceKind.Headset, ConnectionKind.Dongle, true, null, ChargeState.Unknown, BatteryCapability.Unsupported);
        var bt = new RawReading("bt:x", "Mando de prueba", DeviceKind.Gamepad, ConnectionKind.Bluetooth, true, null, ChargeState.Unknown, BatteryCapability.Unknown);
        st.Apply(Ok(rx, bt), T0);
        var m = PanelBuilder.PeripheralsPanel(PanelContext.Initial(new BarConfig(), T0) with { Periph = new PeripheralsView(st.View(T0), 0, false, false, 20) });
        Assert.Contains("ROG DELTA II", Assert.Single(m.Widgets, w => w.Id == "periph-receivers").Text);
        var nodata = Assert.Single(m.Widgets, w => w.Id == "periph-nodata"); Assert.Contains("Mando de prueba", nodata.Text); Assert.DoesNotContain("DELTA", nodata.Text);
    }

    [Fact] public void User_given_names_are_cleaned_and_limited()
    {
        var c = new BarConfig { PeripheralAsusKeyboardName = "  Scope II 96\u0007  ", PeripheralAsusMouseName = new string('x', 80) }.Sanitize();
        Assert.Equal("Scope II 96", c.PeripheralAsusKeyboardName); Assert.Equal(40, c.PeripheralAsusMouseName.Length); Assert.Equal("", new BarConfig().PeripheralAsusKeyboardName);
    }
}
