using System.Text.Json;
using WinMac.Bar;
using WinMac.Bar.Notch;
using WinMac.Bar.Notifications;
using WinMac.Connect;
using Xunit;
using WinMac.Bar.Core.Tests;

/// <summary>WinMac Connect en la barra (fases 1 y 2): indicador, panel del iPhone, sección del Centro de Notificaciones, ocultar y la lista de la Isla.</summary>
public class ConnectBarTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 17, 30, 0, DateTimeKind.Local);
    private static ConnectWire.Item I(uint uid, string app, string bundle, string? title = null, string? body = null, AncsCategory cat = AncsCategory.Social, long session = 1, bool pre = false) =>
        new($"{session}:{uid}", uid, session, app, bundle, cat, title, null, body, new DateTimeOffset(Now).AddMinutes(-uid).ToUnixTimeMilliseconds(), pre);
    private static PhoneState Connected(int? battery = 75, double minutesAgo = 1) => new()
    {
        Status = PhoneLinkStatus.Connected, DeviceName = "iPhone de PipeSick", Battery = battery, BatteryAtUtc = Now.ToUniversalTime().AddMinutes(-minutesAgo),
        BatteryStatus = PhoneFeatureStatus.Active, NotificationsStatus = PhoneFeatureStatus.Active,
    };
    private static BarState State(PhoneState? p) => BarState.Empty(Now) with { Phone = p };

    [Fact]
    public void The_bar_shows_the_iphone_only_with_the_module_and_real_recent_battery()
    {
        BarItem? Phone(PhoneState? p, BarConfig? c = null) => BarModel.Build(State(p), c ?? new BarConfig()).Right.FirstOrDefault(i => i.Kind == BarItemKind.Phone);
        Assert.Null(Phone(null));   // sin módulo: nada
        Assert.Equal("75%", Phone(Connected())!.Text);
        Assert.Equal("—", Phone(Connected(minutesAgo: 30))!.Text);   // lectura vieja: estado desconocido, nunca el número viejo
        Assert.Equal("", Phone(Connected() with { Status = PhoneLinkStatus.Disconnected })!.Text);   // desconectado: nunca un % viejo como actual
        Assert.Null(Phone(new PhoneState { Status = PhoneLinkStatus.NoDevice }));
        Assert.Null(Phone(Connected(), new BarConfig { ShowPhone = false }));
        Assert.Null(Phone(Connected() with { BatteryStatus = PhoneFeatureStatus.Off }));   // batería desactivada en Ajustes
        Assert.Equal(PanelKind.Phone, BarModel.ClickAction(new BarItem(BarItemKind.Phone, "", 16)).Panel);
    }

    [Fact]
    public void The_iphone_panel_tells_the_real_state_and_the_age_of_the_last_reading()
    {
        PanelModel M(PhoneState? p) => PanelBuilder.PhonePanel(PanelContext.Initial(new BarConfig(), Now) with { Phone = p });
        var on = M(Connected());
        Assert.Equal(WidgetKind.PhoneHeader, on.Widgets[0].Kind); Assert.Equal("Conectado", on.Widgets.Single(w => w.Id == "phone-name").Sub);
        var bat = (PhoneBatteryData)on.Widgets.Single(w => w.Id == "phone-battery").Data!;
        Assert.Equal("75 %", bat.Big); Assert.Equal(0.75, bat.Fraction); Assert.Equal("Actualizada hace 1 min", bat.Updated);
        Assert.Null(bat.Charge);   // Bluetooth no indica la carga: no se inventa «cargando»
        var off = M(Connected(minutesAgo: 20) with { Status = PhoneLinkStatus.Disconnected, LastSeenUtc = Now.ToUniversalTime().AddMinutes(-20) });
        var offBat = (PhoneBatteryData)off.Widgets.Single(w => w.Id == "phone-battery").Data!;
        Assert.Equal("—", offBat.Big); Assert.Null(offBat.Fraction); Assert.Equal("Última lectura: 75 % · hace 20 min", offBat.Caption);   // sin conexión: nunca el número viejo como actual
        Assert.Contains(off.Widgets, w => w.Id == "phone-seen");
        Assert.Contains(M(null).Widgets, w => w.Id == "phone-off");
        Assert.Equal("Sin permiso en el iPhone", ((PhoneNotifData)M(Connected() with { NotificationsStatus = PhoneFeatureStatus.NotAuthorized }).Widgets.Single(w => w.Id == "phone-notifs").Data!).Status);
        // «Activas» no es «recibidas»
        PanelModel N(int? count) => PanelBuilder.PhonePanel(PanelContext.Initial(new BarConfig(), Now) with { Phone = Connected() with { NotificationsStatus = PhoneFeatureStatus.Active }, PhoneNotifCount = count });
        Assert.Equal("Todavía no se ha recibido ninguna", ((PhoneNotifData)N(0).Widgets.Single(w => w.Id == "phone-notifs").Data!).Detail);
        Assert.Equal("Recibidas ahora: 3", ((PhoneNotifData)N(3).Widgets.Single(w => w.Id == "phone-notifs").Data!).Detail);
        // acciones inferiores
        Assert.Contains(on.Widgets, w => w.Id == "phone:settings-btn" && w.Text == "Ajustes de Hatsu Connect"); Assert.Contains(on.Widgets, w => w.Text == "Bluetooth de Windows");
    }

    [Fact]
    public void The_phone_header_gear_opens_connect_settings_and_is_a_keyboard_target()
    {
        var m = PanelBuilder.PhonePanel(PanelContext.Initial(new BarConfig(), Now) with { Phone = Connected() });
        var c = new PanelController(); c.SetModel(m);
        Assert.Contains(c.Targets(), t => t.Id == "phone:settings");
        var f = PanelLayout.Compute(m, 900, 0).Items[0];
        Assert.True(PanelLayout.PhoneGearHit(f, f.X + f.Width - 10)); Assert.False(PanelLayout.PhoneGearHit(f, f.X + 20));
    }

    [Fact]
    public void Iphone_notifications_get_their_own_section_identified_and_only_hidden_in_winmac()
    {
        var inbox = new PhoneInbox(); inbox.SetState(Connected());
        inbox.SetList([I(1, "WhatsApp", "net.whatsapp.WhatsApp", "Ana", "Hola"), I(2, "WhatsApp", "net.whatsapp.WhatsApp", "Luis", "¿Vienes?"), I(3, "Teléfono", "com.apple.mobilephone", "Mamá", cat: AncsCategory.MissedCall)]);
        var ctx = PanelContext.Initial(new BarConfig(), Now) with { Phone = inbox.State, Nc = new NcView(NcState.Off, new HashSet<string>(), Phone: inbox.Groups) };
        var m = PanelBuilder.NotificationCenter(ctx);
        Assert.Equal("3", m.Widgets.Single(w => w.Id == "nc-phone").Sub);
        var cards = m.Widgets.Where(w => w.Id.StartsWith("pn:")).Select(w => (NotifCardData)w.Data!).ToList();
        Assert.All(cards, c => { Assert.Equal("iphone", c.Origin); Assert.StartsWith("iPhone · ", c.AppName); });
        Assert.Contains(cards, c => c.Primary == "Llamada perdida");   // la llamada dice qué es

        var r = new Rig(); r.Ops.Phone = inbox;
        r.Click("pdismiss:1"); Assert.DoesNotContain(inbox.Visible, i => i.Uid == 1);
        r.Click("pgclear:" + PhoneInbox.GroupPrefix + "net.whatsapp.WhatsApp"); Assert.Single(inbox.Visible);
        r.Click("pclearall"); Assert.Empty(inbox.Visible);
        inbox.SetList([I(1, "WhatsApp", "net.whatsapp.WhatsApp", "Ana", "Hola"), I(4, "Mail", "com.apple.mobilemail", "Factura")]);   // el iPhone borró la 2 y la 3; la 1 sigue oculta
        Assert.Equal([4u], inbox.Visible.Select(i => i.Uid));
        r.Click("phone:settings"); Assert.Equal("connect", r.Ui.SettingsOpened.Last());
    }

    [Fact]
    public void Card_events_of_the_iphone_never_try_to_open_an_app_or_remove_from_windows()
    {
        var card = new Widget(WidgetKind.NotifCard, "pn:7", "Ana", "Hola", Data: new NotifCardData(7, "iphone:x", null, "iPhone · WhatsApp", "Ana", "Hola", "17:20", true, 0, true, "iphone"));
        var c = new PanelController(); c.SetModel(new PanelModel(PanelKind.Notifications, "", [card], 356));
        var f = c.Frame.Items[0];
        c.PointerDown(f.X + 20, f.Y + f.Height / 2); Assert.Null(c.PointerUp(f.X + 20, f.Y + f.Height / 2));   // clic en la tarjeta: no hay app que abrir en el PC
    }

    [Fact]
    public void Without_the_module_or_with_notifications_off_there_is_no_iphone_section()
    {
        var m = PanelBuilder.NotificationCenter(PanelContext.Initial(new BarConfig(), Now) with { Nc = new NcView(NcState.Off, new HashSet<string>()) });
        Assert.DoesNotContain(m.Widgets, w => w.Id == "nc-phone");
        var empty = PanelBuilder.NotificationCenter(PanelContext.Initial(new BarConfig(), Now) with { Phone = Connected() with { NotificationsStatus = PhoneFeatureStatus.NotAuthorized }, Nc = new NcView(NcState.Off, new HashSet<string>(), Phone: []) });
        Assert.Contains("Compartir notificaciones del sistema", empty.Widgets.Single(w => w.Id == "nc-phone-empty").Text);
        var inbox = new PhoneInbox(); inbox.SetState(Connected()); inbox.SetList([I(1, "A", "a")]); inbox.Clear();
        Assert.False(inbox.Active); Assert.Empty(inbox.Visible);   // módulo apagado: se borra de memoria
        Assert.Null(ConnectWire.ParseState(JsonSerializer.SerializeToElement(ConnectWire.Off)));
    }

    [Fact]
    public void The_island_list_merges_windows_and_iphone_without_id_clashes_and_respects_privacy_fields()
    {
        var win = new List<AppNotification> { new(1, "Teams", "Teams", "Reunión", "a las 18", new DateTimeOffset(Now)) };
        var phone = new List<ConnectWire.Item> { I(1, "WhatsApp", "net.whatsapp.WhatsApp", "Ana", null), I(1, "WhatsApp", "net.whatsapp.WhatsApp", "Ana", null, session: 2) };
        var json = JsonSerializer.SerializeToElement(NotchFeed.Snapshot(win, phone));
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Select(i => i.GetProperty("id").GetInt64()).Distinct().Count());   // uid 1 en Windows, en la sesión 1 y en la 2: tres ids distintos
        var ph = items.First(i => i.GetProperty("app").GetString()!.StartsWith("iPhone · "));
        Assert.Equal(JsonValueKind.Null, ph.GetProperty("appId").ValueKind);   // sin «Abrir»: una app del iPhone no se abre desde el PC
        Assert.True(ph.GetProperty("id").GetInt64() >= PhoneInbox.IslandIdBase);
        Assert.NotEqual(NotchFeed.SnapshotKey(win, phone), NotchFeed.SnapshotKey(win, phone.Take(1).ToList()));
        Assert.Equal(NotchFeed.SnapshotKey(win, []), NotchFeed.SnapshotKey(win, []));
        var pill = JsonSerializer.SerializeToElement(NotchFeed.PhoneNotification(I(9, "Teléfono", "com.apple.mobilephone", "Mamá", cat: AncsCategory.IncomingCall)));
        Assert.Equal("Llamada entrante", pill.GetProperty("title").GetString()); Assert.Equal("Mamá", pill.GetProperty("body").GetString());
        Assert.Equal("notification", pill.GetProperty("kind").GetString());
    }

    [Fact]
    public void Wm038_the_call_state_travels_to_the_island_as_its_own_kind()
    {
        JsonElement J(CallPhase p, string? who) => JsonSerializer.SerializeToElement(NotchFeed.Call(new CallState(p, who, 326, 1, DateTimeOffset.Now)));
        Assert.Equal("call", J(CallPhase.Ringing, "Mamá").GetProperty("kind").GetString());
        Assert.Equal("ringing", J(CallPhase.Ringing, "Mamá").GetProperty("state").GetString()); Assert.Equal("Mamá", J(CallPhase.Ringing, "Mamá").GetProperty("caller").GetString());
        Assert.Equal("missed", J(CallPhase.Missed, null).GetProperty("state").GetString());
        Assert.Equal("none", J(CallPhase.None, null).GetProperty("state").GetString());
        Assert.Equal("active", J(CallPhase.Active, "Mamá").GetProperty("state").GetString());
        Assert.True(J(CallPhase.Active, "Mamá").GetProperty("since").GetInt64() > 0); Assert.Equal(JsonValueKind.Null, J(CallPhase.Ringing, "Mamá").GetProperty("since").ValueKind);
        Assert.False(J(CallPhase.Ringing, "Mamá").GetProperty("controls").GetProperty("answer").GetBoolean());   // sin mecanismo verificado: ningún botón
        Assert.Equal("iPhone", J(CallPhase.Ringing, "Mamá").GetProperty("device").GetString());
        var round = ConnectWire.ParseCall(JsonSerializer.SerializeToElement(new CallState(CallPhase.Ringing, "Mamá", 326, 1, DateTimeOffset.Now), ConnectSettings.Wire))!;
        Assert.Equal(CallPhase.Ringing, round.Phase); Assert.Equal("Mamá", round.Caller);
    }
}
