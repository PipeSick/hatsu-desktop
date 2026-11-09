using System.Text;
using System.Text.Json;
using WinMac.Connect;
using Xunit;

namespace WinMac.Connect.Tests;

public class AncsProtocolTests
{
    private static byte[] Src(byte ev, byte flags, byte cat, byte count, uint uid) => [ev, flags, cat, count, .. BitConverter.GetBytes(uid)];

    private static byte[] Attrs(uint uid, params (byte Id, string V)[] attrs)
    {
        var b = new List<byte> { 0 }; b.AddRange(BitConverter.GetBytes(uid));
        foreach (var (id, v) in attrs) { var s = Encoding.UTF8.GetBytes(v); b.Add(id); b.Add((byte)(s.Length & 0xFF)); b.Add((byte)(s.Length >> 8)); b.AddRange(s); }
        return b.ToArray();
    }

    [Fact]
    public void Notification_source_events_are_parsed_as_in_apples_specification()
    {
        var e = AncsEvent.Parse(Src(0, 0x04 | 0x10, 4, 178, 323))!.Value;   // datos de la prueba real: WhatsApp, ya existente, acción negativa
        Assert.Equal(AncsEventId.Added, e.Event); Assert.Equal(AncsCategory.Social, e.Category); Assert.Equal(178, e.CategoryCount); Assert.Equal(323u, e.Uid);
        Assert.True(e.PreExisting); Assert.False(e.Silent); Assert.True(e.Flags.HasFlag(AncsFlags.NegativeAction));
        Assert.Null(AncsEvent.Parse(new byte[5])); Assert.Null(AncsEvent.Parse(Src(7, 0, 0, 0, 1)));   // corto o evento desconocido: se ignora
    }

    [Fact]
    public void Commands_follow_the_byte_layout_of_the_specification()
    {
        Assert.Equal(new byte[] { 0, 0x44, 0x01, 0, 0, 0, 1, 128, 0, 5 }, Ancs.GetNotificationAttributes(0x144, Ancs.AttributesFor(ContentLevel.TitleOnly)));
        Assert.Equal(new byte[] { 0, 1, 0, 0, 0, 0, 5 }, Ancs.GetNotificationAttributes(1, Ancs.AttributesFor(ContentLevel.AppOnly)));   // sin título ni texto: no salen del iPhone
        var full = Ancs.AttributesFor(ContentLevel.Full).Select(a => a.Id).ToList(); Assert.Equal(new byte[] { 0, 1, 2, 3, 5 }, full);
        Assert.Equal([1, .. Encoding.UTF8.GetBytes("net.whatsapp.WhatsApp"), 0, 0], Ancs.GetAppAttributes("net.whatsapp.WhatsApp"));
        Assert.Equal(new byte[] { 2, 7, 0, 0, 0, 1 }, Ancs.PerformAction(7, positive: false));
    }

    [Fact]
    public void Data_source_responses_are_reassembled_from_chunks_in_request_order()
    {
        var a = new AncsDataAssembler();
        a.Expect(Ancs.CmdGetNotificationAttributes, 3); a.Expect(Ancs.CmdGetAppAttributes, 1);
        var r1 = Attrs(323, (0, "net.whatsapp.WhatsApp"), (1, "Ana ñandú 👋"), (5, "20261103T172031"));
        var app = new List<byte> { 1 }; app.AddRange(Encoding.UTF8.GetBytes("net.whatsapp.WhatsApp")); app.Add(0); app.AddRange(new byte[] { 0, 8, 0 }); app.AddRange(Encoding.UTF8.GetBytes("WhatsApp"));
        var all = r1.Concat(app).ToArray();
        var got = new List<AncsResponse>();
        for (int i = 0; i < all.Length; i += 7) got.AddRange(a.Feed(all.AsSpan(i, Math.Min(7, all.Length - i))));   // trozos de 7 bytes (MTU pequeña)
        Assert.Equal(2, got.Count);
        var n = Assert.IsType<AncsNotificationAttributes>(got[0]); Assert.Equal(323u, n.Uid); Assert.Equal("Ana ñandú 👋", n.Values[1]);
        var ap = Assert.IsType<AncsAppAttributes>(got[1]); Assert.Equal("WhatsApp", ap.DisplayName); Assert.Equal(0, a.Pending);
    }

    [Fact]
    public void A_rejected_request_or_garbage_never_blocks_the_following_responses()
    {
        var a = new AncsDataAssembler();
        a.Expect(0, 1); a.Expect(0, 1); a.Rejected(0);   // 0xA2: la primera ya no existía en el iPhone
        Assert.Single(a.Feed(Attrs(9, (0, "com.apple.mobilephone"))));
        Assert.Empty(a.Feed(new byte[] { 0xEE, 1, 2 }));   // basura sin petición: se descarta
        a.Expect(0, 1); Assert.Single(a.Feed(Attrs(10, (0, "x"))));
    }

    [Fact]
    public void Ancs_dates_are_read_as_local_time()
    {
        var d = Ancs.ParseDate("20261103T172031")!.Value; Assert.Equal(new DateTime(2026, 11, 3, 17, 20, 31), d.DateTime);
        Assert.Null(Ancs.ParseDate("ayer")); Assert.Null(Ancs.ParseDate(null));
    }
}

public class PhoneNotificationBookTests
{
    private DateTimeOffset _t = new(2026, 11, 3, 17, 20, 0, TimeSpan.FromHours(-3));
    private PhoneNotificationBook Book() { var b = new PhoneNotificationBook(() => _t); b.NewSession(); return b; }
    private static AncsEvent E(AncsEventId ev, uint uid, AncsCategory cat = AncsCategory.Social, AncsFlags f = AncsFlags.None) => new(ev, f, cat, 1, uid);
    private static AncsNotificationAttributes A(uint uid, string bundle, string? title = null) =>
        new(uid, new Dictionary<byte, string> { [Ancs.AttrAppIdentifier] = bundle, [Ancs.AttrTitle] = title ?? "" });

    [Fact]
    public void Existing_notifications_are_imported_silently_even_hundreds()
    {
        var b = Book();
        for (uint i = 0; i < 322; i++) { b.Apply(E(AncsEventId.Added, i, f: AncsFlags.PreExisting)); b.SetAttributes(A(i, "net.whatsapp.WhatsApp", "x")); }
        Assert.Equal(322, b.Count); Assert.Empty(b.TakeAlerts());   // ninguna píldora por lo que ya estaba
    }

    [Fact]
    public void A_new_one_is_announced_once_when_its_data_arrive_or_after_a_short_wait()
    {
        var b = Book();
        Assert.Equal(5u, b.Apply(E(AncsEventId.Added, 5)));
        Assert.Empty(b.TakeAlerts());   // aún sin datos
        Assert.Equal("net.whatsapp.WhatsApp", b.SetAttributes(A(5, "net.whatsapp.WhatsApp", "Ana")));   // se pide el nombre de la app una vez
        var alert = Assert.Single(b.TakeAlerts()); Assert.Equal("Ana", alert.Title); Assert.Equal("WhatsApp", alert.DisplayApp);
        Assert.Empty(b.TakeAlerts());
        b.Apply(E(AncsEventId.Added, 6)); _t = _t.AddSeconds(4);
        Assert.Single(b.TakeAlerts());   // el iPhone no respondió: se avisa con lo que hay
        b.Apply(E(AncsEventId.Added, 7, f: AncsFlags.Silent)); b.SetAttributes(A(7, "a"));
        Assert.Empty(b.TakeAlerts());   // silenciosa: solo en la lista
    }

    [Fact]
    public void Removals_on_the_iphone_are_synchronised_and_unknown_events_are_harmless()
    {
        var b = Book(); b.Apply(E(AncsEventId.Added, 1)); b.Apply(E(AncsEventId.Added, 2)); int v = b.Version;
        b.Apply(E(AncsEventId.Removed, 1));
        Assert.Equal([2u], b.Snapshot().Select(n => n.Uid)); Assert.True(b.Version > v);
        b.Apply(E(AncsEventId.Removed, 99)); b.SetAttributes(A(99, "x")); b.AttributesFailed(99);   // de otra sesión o ya borradas: nada
        Assert.Single(b.Snapshot());
        b.NewSession(); Assert.Empty(b.Snapshot());   // al reconectar los UID cambian: el iPhone reenvía las existentes
    }

    [Fact]
    public void Incoming_and_missed_calls_are_notices_and_a_call_that_ended_first_is_not_announced()
    {
        var b = Book();
        b.Apply(E(AncsEventId.Added, 326, AncsCategory.IncomingCall, AncsFlags.PositiveAction | AncsFlags.NegativeAction));
        b.SetAttributes(A(326, "com.apple.mobilephone", "Mamá"));
        Assert.Equal(PhoneCallKind.Incoming, b.IncomingCall!.Call); Assert.Equal("Mamá", b.IncomingCall.Title);
        b.Apply(E(AncsEventId.Removed, 326)); b.Apply(E(AncsEventId.Added, 327, AncsCategory.MissedCall)); b.SetAttributes(A(327, "com.apple.mobilephone", "Mamá"));
        Assert.Null(b.IncomingCall);
        var alerts = b.TakeAlerts(); Assert.Equal([327u], alerts.Select(a => a.Uid));   // la entrante colgó antes de mostrarse: solo la perdida
        Assert.Equal(PhoneCallKind.Missed, alerts[0].Call);
        var noCalls = ConnectWire.List(b.Snapshot(), calls: false); Assert.Empty(noCalls.Items);
    }

    [Fact]
    public void Memory_is_bounded_and_clearing_forgets_everything()
    {
        var b = Book();
        for (uint i = 0; i < PhoneNotificationBook.MaxItems + 50; i++) { _t = _t.AddSeconds(1); b.Apply(E(AncsEventId.Added, i, f: AncsFlags.PreExisting)); }
        Assert.Equal(PhoneNotificationBook.MaxItems, b.Count); Assert.DoesNotContain(b.Snapshot(), n => n.Uid < 50);
        b.SetAppName(new AncsAppAttributes("x", "X")); b.Clear(); Assert.Equal(0, b.Count); Assert.False(b.KnowsApp("x"));
    }
}

public class PhoneStateTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Battery_is_shown_only_when_connected_and_recent()
    {
        var s = new PhoneState { Status = PhoneLinkStatus.Connected, Battery = 75, BatteryAtUtc = Now.AddMinutes(-2) };
        Assert.Equal(75, PhoneStatusText.CurrentBattery(s, Now));
        Assert.Null(PhoneStatusText.CurrentBattery(s with { BatteryAtUtc = Now.AddMinutes(-20) }, Now));   // vieja
        Assert.Null(PhoneStatusText.CurrentBattery(s with { Status = PhoneLinkStatus.Disconnected }, Now));   // desconectado: nunca un % viejo como actual
        Assert.Null(PhoneStatusText.CurrentBattery(s with { Battery = null }, Now));
        Assert.Equal("hace 2 min", PhoneStatusText.Ago(Now.AddMinutes(-2), Now)); Assert.Equal("nunca", PhoneStatusText.Ago(null, Now));
    }

    [Fact]
    public void Settings_and_wire_round_trip_and_tolerate_garbage()
    {
        var cfg = new ConnectSettings { DeviceName = "  iPhone de PipeSick ", Content = ContentLevel.TitleOnly }.Sanitize();
        Assert.Equal("iPhone de PipeSick", cfg.DeviceName);
        var back = ConnectSettings.Parse(JsonSerializer.SerializeToElement(cfg, ConnectSettings.Wire)); Assert.Equal(cfg, back);
        Assert.Equal(new ConnectSettings(), ConnectSettings.Parse(JsonSerializer.SerializeToElement(new { content = 99 })));   // valor imposible → por defecto
        var st = new PhoneState { Status = PhoneLinkStatus.Connected, DeviceName = "iPhone", Battery = 75, Candidates = ["iPhone"] };
        var parsed = ConnectWire.ParseState(JsonSerializer.SerializeToElement(st, ConnectSettings.Wire))!;
        Assert.Equal(75, parsed.Battery); Assert.Equal(PhoneLinkStatus.Connected, parsed.Status); Assert.Equal(["iPhone"], parsed.Candidates);
        Assert.Null(ConnectWire.ParseState(JsonSerializer.SerializeToElement("x"))); Assert.Empty(ConnectWire.ParseList(null));
        Assert.False(new ConnectSettings().Sanitize() != new ConnectSettings());
    }

    [Fact]
    public void Reconnect_waits_grow_and_reset()
    {
        var b = new Backoff(2, 60);
        Assert.Equal([2, 4, 8, 16, 32, 60, 60], Enumerable.Range(0, 7).Select(_ => b.Next().TotalSeconds));
        b.Reset(); Assert.Equal(2, b.Next().TotalSeconds);
    }
}

public class CallTrackerTests
{
    private DateTimeOffset _t = new(2026, 11, 3, 17, 20, 46, TimeSpan.FromHours(-3));
    private CallTracker T() => new(() => _t);
    private static AncsEvent E(AncsEventId ev, uint uid, AncsCategory cat, AncsFlags f = AncsFlags.PositiveAction | AncsFlags.NegativeAction) => new(ev, f, cat, 1, uid);

    [Fact]
    public void Wm038_a_ringing_call_stays_until_it_is_resolved_and_a_missed_call_is_named()
    {
        var c = T();
        Assert.True(c.OnEvent(E(AncsEventId.Added, 326, AncsCategory.IncomingCall), 1)); Assert.Equal(CallPhase.Ringing, c.State.Phase);
        Assert.True(c.OnCaller(326, 1, "Mamá")); Assert.Equal("Mamá", c.State.Caller);
        _t = _t.AddSeconds(30); Assert.False(c.Tick()); Assert.Equal(CallPhase.Ringing, c.State.Phase);   // sigue sonando: no caduca como una notificación
        c.OnEvent(E(AncsEventId.Removed, 326, AncsCategory.IncomingCall), 1);   // secuencia real de la prueba: retirada y enseguida perdida
        Assert.True(c.OnEvent(E(AncsEventId.Added, 327, AncsCategory.MissedCall), 1)); Assert.Equal(CallPhase.Missed, c.State.Phase); Assert.Equal("Mamá", c.State.Caller);
        _t = _t + CallTracker.EndedShow; Assert.True(c.Tick()); Assert.Equal(CallPhase.None, c.State.Phase);
    }

    [Fact]
    public void Wm038_answered_or_rejected_ends_without_pretending_to_know_which()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1);
        c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1);
        _t = _t.AddSeconds(1); Assert.False(c.Tick());   // se espera por si llega una perdida
        _t = _t + CallTracker.MissedGrace; Assert.True(c.Tick());
        Assert.Equal(CallPhase.None, c.State.Phase);   // cierre NEUTRO: nunca «terminada» (contestada = sigue activa en el iPhone; evidencia real)
        Assert.NotEqual(CallPhase.Ended, c.State.Phase);
    }

    [Fact]
    public void Answered_call_never_passes_through_Ended()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1); c.OnCaller(9, 1, "Fernando Noc");
        c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1);
        var seen = new List<CallPhase>();
        for (int i = 0; i < 40; i++) { _t = _t.AddSeconds(0.5); c.Tick(); seen.Add(c.State.Phase); }
        Assert.DoesNotContain(CallPhase.Ended, seen);
        Assert.Equal(CallPhase.None, c.State.Phase);
    }

    [Fact]
    public void Duplicate_added_keeps_the_caller_and_old_events_do_not_touch_a_later_call()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1); c.OnCaller(9, 1, "Mamá");
        Assert.False(c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1)); Assert.Equal("Mamá", c.State.Caller);   // duplicado
        c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1); _t = _t + CallTracker.MissedGrace; c.Tick();
        c.OnEvent(E(AncsEventId.Added, 12, AncsCategory.IncomingCall), 1); c.OnCaller(12, 1, "Fernando");
        Assert.False(c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1));   // Removed atrasado de la llamada anterior (fuera de orden)
        Assert.False(c.OnCaller(9, 1, "Mamá")); Assert.Equal(CallPhase.Ringing, c.State.Phase); Assert.Equal("Fernando", c.State.Caller);
        _t = _t.AddSeconds(30); c.Tick(); Assert.Equal(CallPhase.Ringing, c.State.Phase);
    }

    [Fact]
    public void Modified_event_does_not_change_the_phase()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1);
        Assert.False(c.OnEvent(E(AncsEventId.Modified, 9, AncsCategory.IncomingCall), 1)); Assert.Equal(CallPhase.Ringing, c.State.Phase);
    }

    [Fact]
    public void Missed_arriving_just_after_the_neutral_close_is_still_named_but_only_once_and_not_for_other_sessions()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1); c.OnCaller(9, 1, "Mamá");
        c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1); _t = _t + CallTracker.MissedGrace; c.Tick();
        _t = _t.AddSeconds(3);
        Assert.True(c.OnEvent(E(AncsEventId.Added, 10, AncsCategory.MissedCall), 1)); Assert.Equal(CallPhase.Missed, c.State.Phase); Assert.Equal("Mamá", c.State.Caller);
        var d = T(); d.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1); d.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1); _t = _t + CallTracker.MissedGrace; d.Tick();
        _t = _t + CallTracker.LateMissed + TimeSpan.FromSeconds(1);
        Assert.False(d.OnEvent(E(AncsEventId.Added, 10, AncsCategory.MissedCall), 1));   // demasiado tarde: no se asocia
        var e = T(); e.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1); e.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1); _t = _t + CallTracker.MissedGrace; e.Tick();
        Assert.False(e.OnEvent(E(AncsEventId.Added, 10, AncsCategory.MissedCall), 2));   // otra sesión
    }

    [Fact]
    public void Trusted_telephony_marks_active_with_caller_and_duration_and_only_then_ended()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1); c.OnCaller(9, 1, "Fernando Noc");
        c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1);   // contestada: ANCS la retira
        Assert.True(c.OnTelephony(true)); Assert.Equal(CallPhase.Active, c.State.Phase); Assert.Equal("Fernando Noc", c.State.Caller);
        _t = _t.AddSeconds(125); c.Tick(); Assert.Equal(CallPhase.Active, c.State.Phase);   // la caducidad de timbre no la toca
        Assert.Equal(TimeSpan.FromSeconds(125), c.ActiveDuration); Assert.False(c.NeedsTicks);
        Assert.False(c.OnEvent(E(AncsEventId.Added, 20, AncsCategory.IncomingCall), 1)); Assert.Equal(CallPhase.Active, c.State.Phase);   // un aviso ANCS no sustituye la confirmada
        Assert.True(c.OnTelephony(false)); Assert.Equal(CallPhase.Ended, c.State.Phase);
        _t = _t + CallTracker.EndedShow; c.Tick(); Assert.Equal(CallPhase.None, c.State.Phase);
    }

    [Fact]
    public void Telephony_inactive_without_an_active_call_says_nothing_and_reset_drops_active()
    {
        var c = T(); Assert.False(c.OnTelephony(false)); Assert.Equal(CallPhase.None, c.State.Phase);
        Assert.False(c.OnTelephony(true)); Assert.Equal(CallPhase.None, c.State.Phase);   // sin correlación (p. ej. una llamada de Teams): no es del iPhone
        c.OnEvent(E(AncsEventId.Added, 1, AncsCategory.IncomingCall), 1); c.OnTelephony(true); Assert.True(c.Reset()); Assert.Equal(CallPhase.None, c.State.Phase); Assert.Null(c.ActiveDuration);   // desconexión BT: nada fijado
    }

    [Fact]
    public void Reconnect_clears_everything_including_the_late_missed_window()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 9, AncsCategory.IncomingCall), 1);
        c.OnEvent(E(AncsEventId.Removed, 9, AncsCategory.IncomingCall), 1); _t = _t + CallTracker.MissedGrace; c.Tick();
        c.Reset(); Assert.False(c.OnEvent(E(AncsEventId.Added, 10, AncsCategory.MissedCall), 1)); Assert.False(c.NeedsTicks);
    }

    [Fact]
    public void Wm038_safety_timeout_and_disconnection_never_leave_a_phantom_call()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 1, AncsCategory.IncomingCall), 1);
        _t = _t + CallTracker.RingTimeout; Assert.True(c.Tick()); Assert.Equal(CallPhase.None, c.State.Phase);   // sin noticias: cierre neutro, no se afirma nada
        var d = T(); d.OnEvent(E(AncsEventId.Added, 1, AncsCategory.IncomingCall), 1);
        Assert.True(d.Reset()); Assert.Equal(CallPhase.None, d.State.Phase);   // se perdió el Bluetooth
        var other = T(); other.OnEvent(E(AncsEventId.Added, 1, AncsCategory.IncomingCall), 1);
        other.OnEvent(E(AncsEventId.Removed, 1, AncsCategory.IncomingCall), 2);   // UID de otra sesión: no es esta llamada
        _t = _t.AddSeconds(10); other.Tick(); Assert.Equal(CallPhase.Ringing, other.State.Phase);
        Assert.False(other.OnCaller(1, 2, "X"));
        var pre = T(); Assert.True(pre.OnEvent(E(AncsEventId.Added, 4, AncsCategory.IncomingCall, AncsFlags.PreExisting), 1));   // ya sonaba al conectar
        Assert.False(pre.OnEvent(E(AncsEventId.Added, 5, AncsCategory.MissedCall, AncsFlags.PreExisting), 1));   // perdidas antiguas no cambian nada
    }
}

public class PhoneCapabilitiesTests
{
    [Fact] public void No_call_buttons_without_a_verified_mechanism()
    {
        var t = WinMac.Connect.CallControlCapabilities.Today;
        Assert.False(t.AnyButton); Assert.Equal(WinMac.Connect.ControlSupport.Unavailable, t.PcAudio);
        Assert.Equal(WinMac.Connect.ControlSupport.ExperimentalReadOnly, t.ActiveState);
    }

    [Fact] public void Sig_uuid_uses_bluetooth_base() =>
        Assert.Equal(new Guid("0000111f-0000-1000-8000-00805f9b34fb"), WinMac.Connect.PhoneCapabilities.Sig(0x111F));

    [Fact] public void Not_enumerated_is_unknown_not_absent()
    {
        var c = WinMac.Connect.PhoneCapabilities.From([], servicesEnumerated: false);
        Assert.Equal(WinMac.Connect.CapabilityState.Unknown, c.HandsFreeAg);
        Assert.False(c.CallControlRouteAdvertised);
    }

    [Fact] public void Android_like_set_has_hfp_pbap_but_no_ancs()
    {
        var c = WinMac.Connect.PhoneCapabilities.From([WinMac.Connect.PhoneCapabilities.Sig(0x111F), WinMac.Connect.PhoneCapabilities.Sig(0x112F)], true);
        Assert.True(c.CallControlRouteAdvertised);
        Assert.False(c.CanShowIncomingCallNotice);
        Assert.Equal(WinMac.Connect.CapabilityState.Absent, c.Ancs);
        Assert.Equal(WinMac.Connect.CapabilityState.Absent, c.BatteryGatt);
    }

    [Fact] public void Iphone_like_set_has_ancs_and_battery()
    {
        var c = WinMac.Connect.PhoneCapabilities.From([WinMac.Connect.PhoneCapabilities.AncsService, WinMac.Connect.PhoneCapabilities.Sig(0x180F)], true);
        Assert.True(c.CanShowIncomingCallNotice); Assert.True(c.CanReadBattery);
    }
}

public class CallRoutePlannerTests
{
    private static CallProbeReport R(bool access = false, bool audio = false) => new(true, true, 1, access, access ? "Allowed" : "DeniedBySystem", 2, audio);

    [Fact] public void Current_real_state_offers_no_buttons() =>
        Assert.False(CallRoutePlanner.Evaluate(R(), CallControlRoute.PhoneLineTransport, controlVerifiedOnDevice: false).AnyButton);

    [Fact] public void Answer_without_verified_pc_audio_is_never_offered_even_if_control_works()
    {
        var c = CallRoutePlanner.Evaluate(R(audio: false), CallControlRoute.AncsAction, controlVerifiedOnDevice: true);
        Assert.False(c.AnyButton); Assert.Equal(ControlSupport.Unavailable, c.Answer);
        Assert.Contains("audio", CallRoutePlanner.Why(R(audio: false), CallControlRoute.AncsAction, true));
    }

    [Fact] public void Audio_alone_without_control_is_not_enough()
    {
        Assert.False(CallRoutePlanner.Evaluate(R(audio: true), CallControlRoute.None, true).AnyButton);
        Assert.False(CallRoutePlanner.Evaluate(R(audio: true), CallControlRoute.AncsAction, controlVerifiedOnDevice: false).AnyButton);
    }

    [Fact] public void PhoneLine_route_needs_granted_access()
    {
        Assert.False(CallRoutePlanner.Evaluate(R(access: false, audio: true), CallControlRoute.PhoneLineTransport, true).AnyButton);
        Assert.Contains("DeniedBySystem", CallRoutePlanner.Why(R(access: false, audio: true), CallControlRoute.PhoneLineTransport, true));
        var ok = CallRoutePlanner.Evaluate(R(access: true, audio: true), CallControlRoute.PhoneLineTransport, true);
        Assert.True(ok.AnyButton); Assert.Equal(ControlSupport.Verified, ok.PcAudio); Assert.Equal(ControlSupport.Unavailable, ok.Dial);   // marcar: fuera de alcance
    }
}

public class CallDiagnosticsTests
{
    [Fact] public void Redacts_mac_addresses_keeping_only_the_last_byte()
    {
        var r = DiagRedactor.Redact("Dispositivo AA:BB:CC:DD:EE:0f y bb-cc-dd-ee-ff-01; id 112233445566");
        Assert.DoesNotContain("AA:BB", r); Assert.DoesNotContain("bb-cc", r); Assert.DoesNotContain("1122334455", r);
        Assert.Contains("**:**:**:**:**:0F", r); Assert.Contains("**:**:**:**:**:01", r);
    }

    [Fact] public void Redacts_phone_numbers_and_profile_paths_but_not_short_numbers()
    {
        var r = DiagRedactor.Redact(@"Llamada de +56 9 1234 5678 en C:\Users\felipe\AppData código 0x8000 v2.0.1 HRESULT 80070005", @"C:\Users\felipe", "felipe");
        Assert.DoesNotContain("1234 5678", r); Assert.DoesNotContain("felipe", r); Assert.Contains("%USERPROFILE%", r); Assert.Contains("78", r);
        Assert.Contains("0x8000", r); Assert.Contains("v2.0.1", r);
    }

    [Fact] public void Dates_and_windows_versions_survive_but_ips_do_not()
    {
        var r = DiagRedactor.Redact("Fecha: 2026-11-05 16:29 · Windows NT 10.0.26200.0 · ip 192.168.1.20 · 14:05:33");
        Assert.Contains("2026-11-05 16:29", r); Assert.Contains("10.0.26200.0", r); Assert.DoesNotContain("192.168.1.20", r); Assert.Contains("14:05:33", r);
    }

    [Fact] public void Report_has_summary_on_top_and_distinguishes_all_states()
    {
        var f = new[]
        {
            new DiagFinding("Servicios HFP", DiagState.Detected, "1 teléfono"), new DiagFinding("Observador", DiagState.Available, ""),
            new DiagFinding("Transporte telefónico", DiagState.AccessDenied, "DeniedBySystem"), new DiagFinding("Audio manos libres", DiagState.NotChecked, ""),
            new DiagFinding("Línea", DiagState.Error, "COMException"), new DiagFinding("Dial", DiagState.NotSupported, "fuera de alcance"),
        };
        var t = DiagReport.Compose("Prueba", ["Windows 11"], f, ["detalle"]);
        var sum = DiagReport.SummaryOf(t);
        Assert.Equal(6, sum.Count); Assert.Contains("Acceso denegado — DeniedBySystem", sum[2]); Assert.Contains("No comprobado", sum[3]); Assert.Contains("Error de ejecución", sum[4]);
        Assert.True(t.IndexOf("RESUMEN|", StringComparison.Ordinal) < t.IndexOf("detalle", StringComparison.Ordinal));
        Assert.Contains("NO prueba que el teléfono sea incompatible", t);   // la ausencia de un transporte no se interpreta como incompatibilidad
    }
}

public class DiagIdsTests
{
    [Fact] public void Tag_is_stable_case_insensitive_short_and_not_the_id()
    {
        var a = DiagIds.Tag("{ABC-123}"); Assert.Equal(a, DiagIds.Tag(" {abc-123} ")); Assert.NotEqual(a, DiagIds.Tag("{abc-124}"));
        Assert.Equal(7, a.Length); Assert.DoesNotContain("abc", a, StringComparison.OrdinalIgnoreCase); Assert.Equal("-", DiagIds.Tag(null));
    }

    [Fact] public void Masks_numbers_and_names()
    {
        Assert.Equal("<número>…78", DiagIds.MaskNumber("+56 9 1234 5678")); Assert.Equal("(sin número)", DiagIds.MaskNumber(""));
        Assert.Equal("(12 caracteres)", DiagIds.MaskName("Fernando Noc ")); Assert.DoesNotContain("Fernando", DiagIds.MaskName("Fernando Noc"));
    }

    [Fact] public void Names_the_hresults_seen_in_real_diagnostics()
    {
        Assert.Contains("ERROR_INVALID_STATE", DiagIds.HResultName(unchecked((int)0x8007139F)));
        Assert.Contains("EPT_S_NOT_REGISTERED", DiagIds.HResultName(unchecked((int)0x800706D9)));
        Assert.Contains("sin nombre", DiagIds.HResultName(0x12345));
    }
}
