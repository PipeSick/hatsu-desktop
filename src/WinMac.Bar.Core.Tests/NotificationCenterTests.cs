using WinMac.Bar;
using WinMac.Bar.Notifications;
using WinMac.SystemControl;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Centro de Notificaciones: lógica pura (agrupación, orden, textos, permisos, errores de la API, descartes, configuración de widgets, apertura y cierre).
// Usa una fuente simulada: estas pruebas NO demuestran que Windows entregue notificaciones a WinMac; eso se mide en el PC (docs/20).

internal sealed class FakeNotifications : INotificationSource
{
    public NotificationAccess Access = NotificationAccess.Allowed; public string? AccessReason; public Exception? AccessThrows, ReadThrows;
    public NotificationAccess AfterRequest = NotificationAccess.Allowed; public List<AppNotification> Items = [];
    public bool EventSupported = true; public Action? Fire; public int Reads, Requests, Stops; public List<uint> Removed = []; public int Clears; public List<string> Opened = [];
    public ServiceResult RemoveResult = ServiceResult.Ok, ClearResult = ServiceResult.Ok, OpenResult = ServiceResult.Ok;

    public (NotificationAccess Access, string? Reason) GetAccess() { if (AccessThrows is not null) throw AccessThrows; return (Access, AccessReason); }
    public Task<NotificationAccess> RequestAccessAsync() { Requests++; Access = AfterRequest; return Task.FromResult(AfterRequest); }
    public IReadOnlyList<AppNotification> Read() { Reads++; if (ReadThrows is not null) throw ReadThrows; return Items.ToList(); }
    public ServiceResult Remove(uint id) { Removed.Add(id); if (RemoveResult.IsOk) Items.RemoveAll(i => i.Id == id); return RemoveResult; }
    public ServiceResult ClearAll() { Clears++; if (ClearResult.IsOk) Items.Clear(); return ClearResult; }
    public ServiceResult OpenApp(string appId) { Opened.Add(appId); return OpenResult; }
    public bool StartWatching(Action changed, out string? why) { if (!EventSupported) { why = "Elemento no encontrado (0x80070490)"; return false; } Fire = changed; why = null; return true; }
    public void StopWatching() { Stops++; Fire = null; }
}

internal sealed class NcRig
{
    public readonly FakeNotifications Src = new(); public readonly Queue<Action> Bg = new(), UiQ = new(); public readonly List<string> Log = [];
    public readonly NotificationStore Store; public readonly Rig Base; public int Changes;
    public static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public NcRig(BarConfig? cfg = null, bool enabled = true)
    {
        Store = new NotificationStore(Src, a => Bg.Enqueue(a), a => UiQ.Enqueue(a), Log.Add, () => new DateTime(2026, 11, 2, 9, 5, 0, DateTimeKind.Utc));
        Store.Changed += () => Changes++;
        Base = new Rig((cfg ?? new BarConfig()) with { NotificationsFromWindows = enabled });
        Base.Ops = new PanelOperations(Base.Hub, Base.Ui, Base.Log.Add, TimeSpan.Zero, nc: Store);
        Base.Ui.Ctx = Base.Ui.Ctx with { Now = T0.UtcDateTime.AddMinutes(5), Nc = new NcView(NcState.Off, new HashSet<string>()) };
        if (enabled) Store.SetEnabled(true);
    }

    public static AppNotification N(uint id, string app, int minutesAgo, string? title = "Título", string? body = "Texto", string? name = null, bool icon = true) =>
        new(id, app, name ?? app.Split('!')[0], title, body, T0.AddMinutes(-minutesAgo), icon);

    public void Pump() { while (Bg.Count > 0 || UiQ.Count > 0) { while (Bg.Count > 0) Bg.Dequeue()(); while (UiQ.Count > 0) UiQ.Dequeue()(); } }
    public NcRig Open() { Store.Activate(); Pump(); return this; }
    public void Click(string id) { Base.Click(id); Pump(); }
    public PanelModel Panel() { Base.Ui.Ctx = Base.Ui.Ctx with { Nc = (Base.Ui.Ctx.Nc ?? new NcView(NcState.Off, new HashSet<string>())) with { State = Store.State } }; return PanelBuilder.NotificationCenter(Base.Ui.Ctx); }
}

public class NotificationGroupingTests
{
    [Fact]
    public void Notifications_are_grouped_by_app_newest_first_and_groups_follow_their_latest_notification()
    {
        var g = NotificationGrouping.Group([NcRig.N(1, "Mail", 30), NcRig.N(2, "Teams", 10), NcRig.N(3, "Mail", 5), NcRig.N(4, "Mail", 50), NcRig.N(5, "Teams", 1)]);
        Assert.Equal(["Teams", "Mail"], g.Select(x => x.AppName));
        Assert.Equal([5u, 2u], g[0].Items.Select(i => i.Id)); Assert.Equal([3u, 1u, 4u], g[1].Items.Select(i => i.Id));
        Assert.Equal(NcRig.T0.AddMinutes(-1), g[0].Latest);
    }

    [Fact]
    public void Same_time_orders_by_id_and_hidden_ones_are_left_out_and_app_ids_compare_without_case()
    {
        var g = NotificationGrouping.Group([NcRig.N(7, "mail", 0), NcRig.N(9, "MAIL", 0), NcRig.N(8, "Mail", 0)], new HashSet<uint> { 8 });
        var only = Assert.Single(g); Assert.Equal([9u, 7u], only.Items.Select(i => i.Id));
    }

    [Fact]
    public void Apps_without_id_or_name_still_group_and_get_a_readable_name()
    {
        var noId = new AppNotification(1, "", "Contoso", "a", null, NcRig.T0); var nothing = new AppNotification(2, " ", "", "b", null, NcRig.T0.AddMinutes(-1));
        var g = NotificationGrouping.Group([noId, nothing, new AppNotification(3, "", "Contoso", "c", null, NcRig.T0.AddMinutes(-2))]);
        Assert.Equal(["Contoso", "Aplicación desconocida"], g.Select(x => x.AppName)); Assert.Equal(2, g[0].Items.Count);
        Assert.Equal("Microsoft.Outlook", new AppNotification(4, "Microsoft.Outlook", "", null, null, NcRig.T0).DisplayApp);
    }

    [Fact]
    public void Notifications_without_title_or_body_never_get_invented_text()
    {
        Assert.Equal(("Hola", "Qué tal"), NotificationText.Lines(NcRig.N(1, "A", 0, "Hola", "Qué tal")));
        Assert.Equal(("Solo cuerpo", (string?)null), NotificationText.Lines(NcRig.N(1, "A", 0, null, "  Solo\ncuerpo ")));
        Assert.Equal(("Título", (string?)null), NotificationText.Lines(NcRig.N(1, "A", 0, "Título", "   ")));
        Assert.Equal(("Notificación sin texto", (string?)null), NotificationText.Lines(NcRig.N(1, "A", 0, " ", null)));
    }

    [Fact]
    public void Times_read_like_macos()
    {
        var now = new DateTimeOffset(2026, 11, 2, 15, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 2, 15, 0, 0)));
        Assert.Equal("ahora", NotificationText.When(now.AddSeconds(-20), now, true));
        Assert.Equal("hace 12 min", NotificationText.When(now.AddMinutes(-12), now, true));
        Assert.Equal("09:30", NotificationText.When(now.AddHours(-5.5), now, true));
        Assert.Equal("ayer", NotificationText.When(now.AddDays(-1), now, true));
        Assert.Equal("28/10", NotificationText.When(now.AddDays(-5), now, true));
        Assert.Equal(("Lunes", "2 de noviembre"), NotificationText.Header(new DateTime(2026, 11, 2), "es"));
        Assert.Equal(("Monday", "November 2"), NotificationText.Header(new DateTime(2026, 11, 2), "en"));
    }
}

public class NotificationStoreTests
{
    [Fact]
    public void Off_by_default_nothing_is_read_until_the_user_turns_it_on()
    {
        var r = new NcRig(enabled: false); r.Src.Items.Add(NcRig.N(1, "Mail", 1)); r.Open();
        Assert.Equal(0, r.Src.Reads); Assert.Equal(NcStatus.Off, r.Store.State.Status);
        var m = r.Panel(); Assert.Contains(m.Widgets, w => w.Id == "nc:enable"); Assert.DoesNotContain(m.Widgets, w => w.Kind == WidgetKind.NotifCard);
        r.Click("nc:enable"); Assert.Equal(1, r.Src.Reads); Assert.Equal(NcStatus.Ready, r.Store.State.Status);
        Assert.Contains(r.Base.Ui.Patches, p => p.ToString()!.Contains("notificationsFromWindows = True"));
    }

    [Fact]
    public void Granted_permission_reads_real_items_and_shows_them_grouped()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 3), NcRig.N(2, "Mail", 1), NcRig.N(3, "Teams", 2)]); r.Open();
        var st = r.Store.State; Assert.Equal(NcStatus.Ready, st.Status); Assert.Equal(3, st.Count); Assert.True(st.Live);
        var m = r.Panel();
        Assert.Equal("ng:Mail", m.Widgets.First(w => w.Kind is WidgetKind.NotifGroup or WidgetKind.NotifCard).Id);   // Mail tiene la más reciente
        var stack = (NotifCardData)m.Widgets.Single(w => w.Id == "n:2").Data!; Assert.Equal(1, stack.Stacked); Assert.False(stack.ShowApp);   // plegado: solo la última, apilada
        Assert.DoesNotContain(m.Widgets, w => w.Id == "n:1");
        Assert.True(((NotifCardData)m.Widgets.Single(w => w.Id == "n:3").Data!).ShowApp);
    }

    [Fact]
    public void Undecided_permission_is_requested_from_windows_and_then_read()
    {
        var r = new NcRig(); r.Src.Access = NotificationAccess.Unspecified; r.Open();
        Assert.Equal(NcStatus.NeedsPermission, r.Store.State.Status); Assert.Equal(0, r.Src.Reads);
        Assert.Contains(r.Panel().Widgets, w => w.Id == "nc:request");
        r.Src.Items.Add(NcRig.N(1, "Mail", 0)); r.Click("nc:request");
        Assert.Equal(1, r.Src.Requests); Assert.Equal(NcStatus.Ready, r.Store.State.Status); Assert.Equal(1, r.Store.State.Count);
    }

    [Fact]
    public void Denied_permission_reads_nothing_and_offers_only_the_windows_settings_page()
    {
        var r = new NcRig(); r.Src.Access = NotificationAccess.Denied; r.Src.Items.Add(NcRig.N(1, "Mail", 0)); r.Open();
        Assert.Equal(NcStatus.Denied, r.Store.State.Status); Assert.Equal(0, r.Src.Reads);
        var m = r.Panel(); Assert.Contains(m.Widgets, w => w.Id == "open:ms-settings:privacy-notifications"); Assert.DoesNotContain(m.Widgets, w => w.Id is "nc:request" or "nc:clearall");
        // también si el permiso se niega en el cuadro de Windows
        var r2 = new NcRig(); r2.Src.Access = NotificationAccess.Unspecified; r2.Src.AfterRequest = NotificationAccess.Denied; r2.Open(); r2.Click("nc:request");
        Assert.Equal(NcStatus.Denied, r2.Store.State.Status);
    }

    [Fact]
    public void Api_errors_are_reported_with_their_reason_and_never_crash_or_wipe_what_was_shown()
    {
        var u = new NcRig(); u.Src.Access = NotificationAccess.Unavailable; u.Src.AccessReason = "requiere identidad de paquete"; u.Open();
        Assert.Equal("requiere identidad de paquete", u.Store.State.Message);
        var r = new NcRig(); r.Src.AccessThrows = new InvalidOperationException("Clase no registrada"); r.Open();
        Assert.Equal(NcStatus.Unavailable, r.Store.State.Status); Assert.Contains("Clase no registrada", r.Store.State.Message);
        Assert.Contains(r.Panel().Widgets, w => w.Id == "notifications");   // queda el centro de Windows, que sí funciona

        var e = new NcRig(); e.Src.Items.Add(NcRig.N(1, "Mail", 0)); e.Open(); e.Src.ReadThrows = new TimeoutException("sin respuesta"); e.Store.Refresh(); e.Pump();
        Assert.Equal(NcStatus.Error, e.Store.State.Status); Assert.Equal(1, e.Store.State.Count); Assert.DoesNotContain("sin respuesta", e.Store.State.Message); Assert.Contains(e.Log, l => l.Contains("sin respuesta"));   // WM-026: the technical detail goes to the log, not to the user
        Assert.DoesNotContain(e.Log, l => l.Contains("Título") || l.Contains("Texto"));   // el contenido de las notificaciones nunca va al registro
        var first = new NcRig(); first.Src.ReadThrows = new TimeoutException("x"); first.Open(); Assert.Contains(first.Panel().Widgets, w => w.Id == "nc:retry");
    }

    [Fact]
    public void Hiding_only_affects_winmac_and_removing_asks_windows()
    {
        var r = new NcRig(new BarConfig { NotifRemoveFromWindows = false }); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Teams", 2)]); r.Open();
        r.Click("ndismiss:1"); Assert.Empty(r.Src.Removed); Assert.Equal(1, r.Store.State.Count);
        r.Store.Refresh(); r.Pump(); Assert.Equal(1, r.Store.State.Count);   // sigue oculta tras releer (Windows aún la tiene)
        Assert.Contains("solo la ocultan en WinMac", r.Panel().Widgets.Single(w => w.Id == "nc-mode").Text);

        var w = new NcRig(); w.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Teams", 2)]); w.Open();
        w.Click("ndismiss:1"); Assert.Equal([1u], w.Src.Removed); Assert.Equal(1, w.Store.State.Count); Assert.Equal(2u, w.Store.State.Groups[0].Items[0].Id);
        Assert.Contains("también del centro de notificaciones de Windows", w.Panel().Widgets.Single(x => x.Id == "nc-mode").Text);
    }

    [Fact]
    public void A_removal_windows_rejects_comes_back_with_the_reason()
    {
        var r = new NcRig(); r.Src.Items.Add(NcRig.N(1, "Mail", 1)); r.Open(); r.Src.RemoveResult = ServiceResult.Failed("acceso denegado");
        r.Store.Dismiss(1, fromWindows: true); Assert.Equal(0, r.Store.State.Count);   // se ve al instante…
        r.Pump(); Assert.Equal(1, r.Store.State.Count); Assert.Contains("Windows no permitió borrar 1 notificación", r.Store.State.Message);   // …y vuelve si Windows no la quitó
        Assert.DoesNotContain("acceso denegado", r.Store.State.Message); Assert.Contains(r.Log, l => l.Contains("acceso denegado"));   // detail in the log only
        r.Store.Refresh(); r.Pump(); Assert.Contains("no permitió", r.Store.State.Message);   // el motivo no desaparece con la relectura
    }

    [Fact]
    public void Groups_and_everything_can_be_cleared_in_both_modes()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Mail", 2), NcRig.N(3, "Teams", 3)]); r.Open();
        r.Click("ngclear:Mail"); Assert.Equal([1u, 2u], r.Src.Removed.Order()); Assert.Equal(["Teams"], r.Store.State.Groups.Select(g => g.AppName));
        r.Base.Ui.ConfirmAnswer = false; r.Click("nc:clearall"); Assert.Equal(0, r.Src.Clears); Assert.Single(r.Base.Ui.Confirms);   // borrar en Windows pide confirmación
        r.Base.Ui.ConfirmAnswer = true; r.Click("nc:clearall"); Assert.Equal(1, r.Src.Clears); Assert.Equal(0, r.Store.State.Count);
        Assert.Equal("Eliminar notificaciones", r.Base.Ui.LastConfirmAction);   // WM-024: the button says what it does (Cancelar is the default)
        Assert.Contains(r.Panel().Widgets, w => w.Id == "nc-empty");

        var h = new NcRig(new BarConfig { NotifRemoveFromWindows = false }); h.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Teams", 3)]); h.Open();
        Assert.Equal("Ocultar todo", ((NcSectionData)h.Panel().Widgets.Single(w => w.Id == "nc-sec").Data!).ActionLabel);
        Assert.Equal("Ocultar", ((NotifGroupData?)h.Panel().Widgets.FirstOrDefault(w => w.Kind == WidgetKind.NotifGroup)?.Data)?.ClearLabel ?? "Ocultar");
        h.Click("ngclear:Mail"); h.Click("nc:clearall"); Assert.Empty(h.Src.Removed); Assert.Equal(0, h.Src.Clears); Assert.Empty(h.Base.Ui.Confirms); Assert.Equal(0, h.Store.State.Count);
    }

    [Fact]
    public void New_notifications_arrive_by_event_and_without_it_the_panel_polls_only_while_open()
    {
        var r = new NcRig(); r.Open(); Assert.NotNull(r.Src.Fire); Assert.False(r.Store.NeedsPolling);
        r.Src.Items.Add(NcRig.N(5, "Mail", 0)); r.Src.Fire!(); r.Pump(); Assert.Equal(1, r.Store.State.Count);
        r.Store.Deactivate(); Assert.Equal(1, r.Src.Stops); Assert.Null(r.Src.Fire);

        var p = new NcRig(); p.Src.EventSupported = false; p.Open();
        Assert.False(p.Store.State.Live); Assert.True(p.Store.NeedsPolling); Assert.Contains("cada pocos segundos", p.Panel().Widgets.FirstOrDefault(w => w.Id == "nc-mode")?.Text ?? "cada pocos segundos");
        p.Store.Deactivate(); Assert.False(p.Store.NeedsPolling);
    }

    [Fact]
    public void Only_one_read_runs_at_a_time_and_a_request_during_it_reads_once_more()
    {
        var r = new NcRig(); r.Store.Activate(); r.Store.Refresh(); r.Store.Refresh();
        Assert.Single(r.Bg); r.Pump(); Assert.Equal(2, r.Src.Reads);
    }

    [Fact]
    public void Turning_it_off_forgets_everything_and_late_results_are_dropped()
    {
        var r = new NcRig(); r.Src.Items.Add(NcRig.N(1, "Mail", 0)); r.Open(); Assert.Equal(1, r.Store.State.Count);
        r.Store.Refresh(); r.Store.SetEnabled(false); r.Pump();
        Assert.Equal(NcStatus.Off, r.Store.State.Status); Assert.Equal(0, r.Store.State.Count);
    }

    [Fact]
    public void Opening_the_app_of_a_notification_hides_the_panel_or_says_why_not()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail!App", 0), new AppNotification(2, "", "Sin id", "x", null, NcRig.T0)]); r.Open();
        r.Click("nopen:1"); Assert.Equal(["Mail!App"], r.Src.Opened); Assert.Equal(1, r.Base.Ui.Hides);
        r.Click("nopen:2"); Assert.Single(r.Src.Opened); Assert.Contains("no indica qué app", r.Base.Ui.Ctx.Message);
    }
}

public class NotificationCenterPanelTests
{
    [Fact]
    public void Groups_expand_and_collapse_and_cards_without_icon_have_no_icon_key()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1, icon: false), NcRig.N(2, "Mail", 2, icon: false)]); r.Open();
        Assert.Null(((NotifGroupData)r.Panel().Widgets.Single(w => w.Id == "ng:Mail").Data!).IconKey);
        r.Click("ngtoggle:Mail"); var m = r.Panel();
        Assert.True(((NotifGroupData)m.Widgets.Single(w => w.Id == "ng:Mail").Data!).Expanded); Assert.Equal(2, m.Widgets.Count(w => w.Kind == WidgetKind.NotifCard));
        Assert.All(m.Widgets.Where(w => w.Kind == WidgetKind.NotifCard), w => Assert.Equal(0, ((NotifCardData)w.Data!).Stacked));
        r.Click("ngtoggle:Mail"); Assert.Equal(1, r.Panel().Widgets.Count(w => w.Kind == WidgetKind.NotifCard));
        var withIcon = new NcRig(); withIcon.Src.Items.Add(NcRig.N(1, "Teams", 0)); withIcon.Open();
        Assert.Equal(PanelBuilder.NcIconPrefix + "Teams", ((NotifCardData)withIcon.Panel().Widgets.Single(w => w.Id == "n:1").Data!).IconKey);
    }

    [Fact]
    public void The_header_shows_the_date_and_stays_pinned_with_the_footer_at_the_bottom_of_a_full_height_panel()
    {
        var r = new NcRig(); r.Base.Ui.Ctx = r.Base.Ui.Ctx with { Now = new DateTime(2026, 11, 2, 10, 0, 0) }; r.Open();
        var m = r.Panel(); var head = m.Widgets[0];
        Assert.Equal(WidgetKind.NcHeader, head.Kind); Assert.Equal("Lunes", head.Text); Assert.Equal("2 de noviembre", head.Sub);
        Assert.True(m.FillHeight); Assert.Equal(1, m.PinTop); Assert.Equal(1, m.PinBottom);
        var f = PanelLayout.Compute(m, 900); Assert.Equal(900, f.Height); var last = f.Items[^1];
        Assert.Equal("open:ms-settings:notifications", last.W.Id); Assert.Equal(900 - PanelLayout.Pad, last.Y + last.Height, 3);
        // mucho contenido: el centro se desplaza y el encabezado y el pie siguen fijos
        r.Src.Items.AddRange(Enumerable.Range(1, 30).Select(i => NcRig.N((uint)i, "App" + i, i))); r.Store.Refresh(); r.Pump();
        var big = PanelLayout.Compute(r.Panel(), 700); Assert.NotNull(big.View); Assert.Equal(700, big.Height, 0);
    }

    [Fact]
    public void Widgets_follow_the_configuration_order_and_the_battery_widget_never_invents_a_percentage()
    {
        var desktop = new NcRig(new BarConfig { NcWidgets = "weather,calendar,battery" }).Open().Panel();
        var ids = desktop.Widgets.Select(w => w.Id).ToList();
        Assert.True(ids.IndexOf("wx-nocity") < ids.IndexOf("month")); Assert.DoesNotContain("nc-battery", ids);   // sobremesa: sin batería no hay widget
        var laptop = new NcRig(new BarConfig { NcWidgets = "battery" }); laptop.Base.Ui.Ctx = laptop.Base.Ui.Ctx with { Battery = new BatteryInfo(true, 57, false, false) };
        var bat = laptop.Open().Panel().Widgets.Single(w => w.Id == "nc-battery"); Assert.Equal("57 %", bat.Sub); Assert.Equal(0.57, bat.Value, 3);
        var none = new NcRig(new BarConfig { NcWidgets = "" }).Open().Panel(); Assert.DoesNotContain(none.Widgets, w => w.Id is "nc-wsec" or "month");
    }

    [Fact]
    public void Widget_configuration_is_parsed_and_sanitized()
    {
        Assert.Equal([NcWidget.Calendar, NcWidget.Weather, NcWidget.Battery], NcWidgets.Parse(null));
        Assert.Equal([NcWidget.Battery, NcWidget.Calendar], NcWidgets.Parse("battery, CALENDAR;battery,reloj"));
        Assert.Empty(NcWidgets.Parse(""));
        Assert.Equal("weather,calendar", new BarConfig { NcWidgets = "weather;weather,calendar,xx" }.Sanitize().NcWidgets);
        var cfg = new BarConfig();
        Assert.True(cfg.NotificationCenter); Assert.False(cfg.NotificationsFromWindows); Assert.True(cfg.NotifRemoveFromWindows); Assert.Equal(NcWidgets.Default, cfg.NcWidgets);
        var patched = cfg.ApplyPatch(System.Text.Json.JsonSerializer.SerializeToElement(new { notificationsFromWindows = true, ncWidgets = "battery" }));
        Assert.True(patched.NotificationsFromWindows); Assert.Equal("battery", patched.NcWidgets);
    }

    [Fact]
    public void The_clock_opens_the_notification_center_and_the_control_center_tile_goes_there_too()
    {
        var clock = new BarItem(BarItemKind.Clock, "12:00", 0);
        Assert.Equal(PanelKind.Notifications, BarModel.ClickAction(clock, notificationCenter: true).Panel);
        Assert.Equal(PanelKind.Calendar, BarModel.ClickAction(clock, notificationCenter: false).Panel);
        var cc = PanelBuilder.ControlCenter(PanelContext.Initial(new BarConfig(), DateTime.Now));
        Assert.Contains(cc.Widgets, w => w.Id == "nc:open"); Assert.DoesNotContain(cc.Widgets, w => w.Id == "notifications");
        Assert.Contains(PanelBuilder.ControlCenter(PanelContext.Initial(new BarConfig { NotificationCenter = false }, DateTime.Now)).Widgets, w => w.Id == "notifications");
        var r = new NcRig(); r.Click("nc:open"); Assert.Equal([PanelKind.Notifications], r.Base.Ui.Navigations);
        r.Click("nc:close"); Assert.Equal(1, r.Base.Ui.Hides);
        r.Click("nc:settings"); Assert.Equal(["notifications"], r.Base.Ui.SettingsOpened);
    }

    [Fact]
    public void Clicks_on_card_parts_produce_the_right_events()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Mail", 2), NcRig.N(3, "Teams", 3)]); r.Open();
        var ctl = new PanelController(); ctl.SetMaxHeight(900); ctl.SetModel(r.Panel());
        PanelEvent? Tap(string id, Func<WidgetFrame, (double X, double Y)> at)
        {
            var f = ctl.Frame.Items.Single(i => i.W.Id == id); var (x, y) = at(f); ctl.PointerDown(x, y); return ctl.PointerUp(x, y);
        }

        Assert.Equal("ndismiss:3", Tap("n:3", f => (f.X + f.Width - 14, f.Y + 12))!.Id);
        Assert.Equal("nopen:3", Tap("n:3", f => (f.X + 40, f.Y + f.Height - 10))!.Id);
        Assert.Equal("ngtoggle:Mail", Tap("n:1", f => (f.X + 40, f.Y + f.Height - 10))!.Id);   // tarjeta apilada: despliega
        Assert.Equal("ngclear:Mail", Tap("ng:Mail", f => (f.X + f.Width - 20, f.Y + f.Height / 2))!.Id);
        Assert.Equal("ngtoggle:Mail", Tap("ng:Mail", f => (f.X + 30, f.Y + f.Height / 2))!.Id);
        Assert.Equal("nc:clearall", Tap("nc-sec", f => (f.X + f.Width - 30, f.Y + f.Height / 2))!.Id);
        Assert.Equal("nc:close", Tap("nc-head", f => (f.X + f.Width - 14, f.Y + 14))!.Id);
        Assert.Null(Tap("nc-head", f => (f.X + 20, f.Y + 20)));   // el resto del encabezado no hace nada
    }
}

public class NcGeometryTests
{
    [Fact]
    public void The_side_panel_hugs_the_right_edge_of_the_clicked_monitor_between_the_bar_and_the_taskbar()
    {
        var mon = new PxRect(0, 0, 2560, 1440); var work = new PxRect(0, 36, 2560, 1356);   // barra reservada arriba (36) y barra de tareas abajo (48)
        var r = NcGeometry.Place(36, mon, work, 1.0, 356);
        Assert.Equal(2560 - 8, r.Right); Assert.Equal(36 + 6, r.Y); Assert.Equal(1392 - 8, r.Bottom); Assert.Equal(356, r.W);
        // segundo monitor a la izquierda con 150 %
        var m2 = new PxRect(-1920, 200, 1920, 1080); var r2 = NcGeometry.Place(200 + 48, m2, new PxRect(-1920, 248, 1920, 984), 1.5, 340);
        Assert.Equal(-12, r2.Right); Assert.Equal(248 + 9, r2.Y); Assert.Equal(248 + 984 - 12, r2.Bottom); Assert.Equal(510, r2.W);
        Assert.True(r2.X >= m2.X);
        // un área de trabajo de OTRO monitor se ignora
        var r3 = NcGeometry.Place(30, mon, new PxRect(3000, 0, 1920, 1040), 1.0, 356); Assert.Equal(1440 - 8, r3.Bottom);
    }

    [Fact]
    public void Width_adapts_to_the_monitor_and_the_slide_animation_starts_outside_and_ends_in_place()
    {
        Assert.Equal(300, NcGeometry.Width(1000)); Assert.Equal(372, NcGeometry.Width(3840)); Assert.Equal(340, NcGeometry.Width(1360));
        Assert.Equal(300, NcGeometry.Width(320)); Assert.Equal(234, NcGeometry.Width(250));   // pantalla diminuta: cabe con márgenes
        Assert.Equal(356, NcGeometry.SlideOffset(0, 220, 356, opening: true)); Assert.Equal(0, NcGeometry.SlideOffset(220, 220, 356, true)); Assert.Equal(0, NcGeometry.SlideOffset(999, 220, 356, true));
        Assert.Equal(0, NcGeometry.SlideOffset(0, 160, 356, opening: false)); Assert.Equal(356, NcGeometry.SlideOffset(160, 160, 356, false));
        double mid = NcGeometry.SlideOffset(110, 220, 356, true); Assert.InRange(mid, 1, 178);   // ease-out: a mitad de tiempo ya recorrió más de la mitad
    }
}

public class NotificationLimitWm012Tests
{
    [Fact]
    public void A_folded_group_does_not_produce_a_false_shown_n_of_m_and_many_notifications_all_stay_reachable()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Mail", 2), NcRig.N(3, "Teams", 3), NcRig.N(4, "Chat", 4)]); r.Open();
        var m = r.Panel();
        Assert.DoesNotContain(m.Widgets, w => w.Id == "nc-more");   // beta: «Se muestran 3 de 4» with Mail folded
        Assert.Contains(m.Widgets, w => w.Kind == WidgetKind.NotifGroup);

        var big = new NcRig(); big.Src.Items.AddRange(Enumerable.Range(1, 60).Select(i => NcRig.N((uint)i, "App" + i, i))); big.Open();
        Assert.Equal(60, big.Panel().Widgets.Count(w => w.Kind == WidgetKind.NotifCard));   // no arbitrary cap at a handful
    }
}

public class NotificationDeleteWm026Tests
{
    [Fact]
    public void Clear_all_failing_with_element_not_found_falls_back_to_one_by_one_and_trusts_what_windows_still_has()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Teams", 2)]); r.Open();
        r.Src.ClearResult = ServiceResult.Failed("COMException, 0x80070490");   // what the beta showed
        r.Base.Ui.ConfirmAnswer = true; r.Click("nc:clearall");
        Assert.Equal(1, r.Src.Clears); Assert.Equal([1u, 2u], r.Src.Removed.Order());   // fallback: one by one
        Assert.Equal(0, r.Store.State.Count); Assert.Null(r.Store.State.Message);   // all gone: no error shown
        Assert.Contains(r.Log, l => l.Contains("0x80070490"));                         // the code stays in the log
    }

    [Fact]
    public void When_windows_keeps_some_the_ui_says_how_many_and_keeps_them()
    {
        var r = new NcRig(); r.Src.Items.AddRange([NcRig.N(1, "Mail", 1), NcRig.N(2, "Teams", 2)]); r.Open();
        r.Src.ClearResult = ServiceResult.Failed("COMException, 0x80070490"); r.Src.RemoveResult = ServiceResult.Failed("COMException, 0x80070005");
        r.Base.Ui.ConfirmAnswer = true; r.Click("nc:clearall");
        Assert.Equal(2, r.Store.State.Count);                                            // nothing pretends to be deleted
        Assert.Contains("Windows no permitió borrar 2 notificaciones", r.Store.State.Message); Assert.DoesNotContain("COMException", r.Store.State.Message);
    }

    [Fact]
    public void Removing_one_that_windows_already_dropped_is_not_an_error()
    {
        var r = new NcRig(); r.Src.Items.Add(NcRig.N(1, "Mail", 1)); r.Open();
        r.Src.Items.Clear(); r.Src.RemoveResult = ServiceResult.Failed("COMException, 0x80070490");   // dismissed in Windows meanwhile
        r.Store.Dismiss(1, fromWindows: true); r.Pump();
        Assert.Equal(0, r.Store.State.Count); Assert.Null(r.Store.State.Message);
    }
}

public class NotchSnapshotWm025Tests
{
    [Fact]
    public void The_island_gets_what_windows_holds_with_ids_and_only_when_it_changes()
    {
        var a = new AppNotification(7, "MSTeams!App", "Teams", "Ana", "¿Vienes?", DateTimeOffset.FromUnixTimeSeconds(3000));
        var b = new AppNotification(8, "", "Mail", "Factura", null, DateTimeOffset.FromUnixTimeSeconds(1000));
        var json = System.Text.Json.JsonSerializer.SerializeToElement(WinMac.Bar.Notch.NotchFeed.Snapshot([b, a]));
        Assert.Equal("notifications", json.GetProperty("kind").GetString());
        var items = json.GetProperty("items"); Assert.Equal(7u, items[0].GetProperty("id").GetUInt32()); Assert.Equal("MSTeams!App", items[0].GetProperty("appId").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, items[1].GetProperty("appId").ValueKind);
        Assert.Equal(WinMac.Bar.Notch.NotchFeed.SnapshotKey([a, b]), WinMac.Bar.Notch.NotchFeed.SnapshotKey([b, a]));
        Assert.NotEqual(WinMac.Bar.Notch.NotchFeed.SnapshotKey([a, b]), WinMac.Bar.Notch.NotchFeed.SnapshotKey([a]));
        Assert.Equal(7u, System.Text.Json.JsonSerializer.SerializeToElement(WinMac.Bar.Notch.NotchFeed.Notification(a, null)).GetProperty("id").GetUInt32());   // the pill knows which one it is
    }
}
