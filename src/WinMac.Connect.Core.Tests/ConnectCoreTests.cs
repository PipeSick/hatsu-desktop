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
        _t = _t + CallTracker.MissedGrace; Assert.True(c.Tick()); Assert.Equal(CallPhase.Ended, c.State.Phase);
    }

    [Fact]
    public void Wm038_safety_timeout_and_disconnection_never_leave_a_phantom_call()
    {
        var c = T(); c.OnEvent(E(AncsEventId.Added, 1, AncsCategory.IncomingCall), 1);
        _t = _t + CallTracker.RingTimeout; Assert.True(c.Tick()); Assert.Equal(CallPhase.Ended, c.State.Phase);   // sin noticias: se reconcilia
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
