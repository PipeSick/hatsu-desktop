using WinMac.SystemControl;
using WinMac.Bar;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Menú de la manzana con aspecto de macOS (antes, el menú nativo de Windows).
public class AppleMenuTests
{
    private static PanelContext Ctx(string? app = "LeagueClientUx", bool reserve = true) => PanelContext.Initial(new BarConfig { ReserveSpace = reserve }, new DateTime(2026, 11, 2)) with { ForegroundApp = app };

    [Fact]
    public void Wm022_one_control_center_without_the_old_menu_underneath()
    {
        var m = PanelBuilder.AppleMenu(Ctx() with { User = new SessionUser("Ana María Pérez", "PORTATIL") });
        var head = m.Widgets[0]; Assert.Equal(WidgetKind.UserHeader, head.Kind); Assert.Equal("Ana María Pérez", head.Text); Assert.Equal("AM", ((UserHeaderData)head.Data!).Initials);
        Assert.Equal("open:ms-settings:yourinfo", ((UserHeaderData)head.Data!).AccountId);   // account options
        Assert.Null(((UserHeaderData)head.Data!).PictureKey);                                 // WM-029: no picture read → initials
        var withPic = PanelBuilder.AppleMenu(Ctx() with { User = new SessionUser("Ana", "PC", "user-picture:42") });
        Assert.Equal("user-picture:42", ((UserHeaderData)withPic.Widgets[0].Data!).PictureKey);
        Assert.Equal(["open:wifi", "open:bluetooth", "open:ms-settings:notifications", "appearance", "open:display", "open:battery", "open:ms-settings:"], m.Widgets.Where(w => w.Kind == WidgetKind.Tile).Select(w => w.Id));
        Assert.Equal(["volume", "brightness"], m.Widgets.Where(w => w.Kind == WidgetKind.Slider).Select(w => w.Id));
        Assert.NotNull(m.Card);   // the sliders share a card
        var strip = (ActionStripData)m.Widgets.Single(w => w.Kind == WidgetKind.ActionStrip).Data!;
        Assert.Equal(["apple:Sleep", "apple:Restart", "apple:Shutdown", "apple:LogOff", "apple:Lock"], strip.Actions.Select(a => a.Id));
        Assert.DoesNotContain(m.Widgets, w => w.Kind is WidgetKind.MenuItem or WidgetKind.Separator);   // the old menu does not appear underneath
        var links = ((LinkRowData)m.Widgets.Single(w => w.Kind == WidgetKind.LinkRow).Data!).Links;
        Assert.Equal(["apple:winmac-settings", "apple:winmac-quit", "apple:About", "apple:ForceQuit"], links.Select(l => l.Id));
        Assert.DoesNotContain(m.Widgets, w => w.Id is "apple:reserve" or "apple:quitbar");   // they are settings: Ajustes › Barra superior / General
        Assert.Equal(PanelKind.AppleMenu, PanelBuilder.Build(PanelKind.AppleMenu, Ctx()).Kind);
    }

    [Fact]
    public void Force_quit_is_dimmed_without_an_app_that_can_be_closed()
    {
        var links = ((LinkRowData)PanelBuilder.AppleMenu(Ctx(app: null)).Widgets.Single(w => w.Kind == WidgetKind.LinkRow).Data!).Links;
        Assert.False(links.Single(l => l.Id == "apple:ForceQuit").Enabled);
    }

    [Fact]
    public void Wm022_session_buttons_account_and_links_are_real_clicks_and_the_keyboard_reaches_everything()
    {
        var ctl = new PanelController(); ctl.SetModel(PanelBuilder.AppleMenu(Ctx() with { Sys = SystemSnapshot.Empty with { Audio = new WinMac.SystemControl.AudioState(true, 40, false, []) } }));
        var strip = ctl.Frame.Items.Single(i => i.W.Kind == WidgetKind.ActionStrip); double cw = strip.Width / 5;
        ctl.PointerDown(strip.X + cw * 4.5, strip.Y + 20); Assert.Equal("apple:Lock", ctl.PointerUp(strip.X + cw * 4.5, strip.Y + 20)!.Id);
        ctl.PointerDown(strip.X + cw * 2.5, strip.Y + 20); Assert.Equal("apple:Shutdown", ctl.PointerUp(strip.X + cw * 2.5, strip.Y + 20)!.Id);
        var head = ctl.Frame.Items[0];
        ctl.PointerDown(head.X + head.Width - 20, head.Y + 30); Assert.Equal("open:ms-settings:yourinfo", ctl.PointerUp(head.X + head.Width - 20, head.Y + 30)!.Id);
        ctl.PointerDown(head.X + 30, head.Y + 30); Assert.Null(ctl.PointerUp(head.X + 30, head.Y + 30));   // the name is not a button
        var lr = ctl.Frame.Items.Single(i => i.W.Kind == WidgetKind.LinkRow);
        ctl.PointerDown(lr.X + 10, lr.Y + 10); Assert.Equal("apple:winmac-settings", ctl.PointerUp(lr.X + 10, lr.Y + 10)!.Id);
        // keyboard: every control in order, Intro activates, ← → move a slider
        var ids = new List<string>(); for (int i = 0; i < ctl.Targets().Count; i++) { ctl.FocusKey(0x09, false); ids.Add(ctl.Focus!.Id); }
        Assert.Equal("open:ms-settings:yourinfo", ids[0]); Assert.Contains("apple:Restart", ids); Assert.Contains("volume", ids); Assert.Contains("apple:About", ids);
        while (ctl.Focus!.Id != "apple:Restart") ctl.FocusKey(0x09, false);
        Assert.Equal("apple:Restart", ctl.FocusKey(0x0D, false)!.Id);
        while (ctl.Focus!.Id != "volume") ctl.FocusKey(0x28, false);
        var ev = ctl.FocusKey(0x27, false)!; Assert.Equal(PanelEventKind.SliderCommit, ev.Kind); Assert.Equal("volume", ev.Id);
        var hgt = PanelLayout.Compute(PanelBuilder.AppleMenu(Ctx())).Height; Assert.True(hgt < 680, "alto " + hgt);   // fits a 1080p screen at 150 % (720 DIP) with the dark-mode tile row (3.2)
    }
}

public class BatteryPanelWm010Tests
{
    private static PanelContext Ctx(BatteryInfo b, BatteryDetails? d, bool open = false) => PanelContext.Initial(new BarConfig(), new DateTime(2026, 11, 3)) with { Battery = b, BatteryDetail = d, BatteryDetailsOpen = open };

    [Fact]
    public void Wm027_hero_health_cards_and_folded_details_with_only_what_the_battery_reports()
    {
        // the beta's notebook: 81 %, charging at 56 W, 80.7 of 90.0 Wh (sample values for the test, not fixed anywhere in the code)
        var m = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 81, true, true), new BatteryDetails(90_000, 80_700, 65_400, 56_000, null)));
        var hero = m.Widgets.Single(w => w.Id == "battery"); Assert.Equal(WidgetKind.BatteryHero, hero.Kind); Assert.Equal("81 %", hero.Text); Assert.Equal("Cargando", hero.Sub); Assert.Equal(81, hero.Value);
        Assert.Equal("Cargando a 56,0 W", ((BatteryHeroData)hero.Data!).Detail);
        var cards = ((StatCardsData)m.Widgets.Single(w => w.Kind == WidgetKind.StatCards).Data!).Cards;
        Assert.Equal(["Salud", "Plena carga", "Diseño"], cards.Select(c => c.Label)); Assert.Equal("90 %", cards[0].Value); Assert.Equal("good", cards[0].Tone);
        Assert.DoesNotContain(m.Widgets, w => w.Id == "bat-cycles");                       // folded
        var open = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 81, true, true), new BatteryDetails(90_000, 80_700, 65_400, 56_000, null), open: true));
        Assert.Contains(open.Widgets, w => w.Id == "bat-missing");                         // «la batería no informa de sus ciclos»
        Assert.DoesNotContain(open.Widgets, w => w.Id == "bat-cycles");                    // never invented
        Assert.Equal("Potencia de carga", open.Widgets.Single(w => w.Id == "bat-rate").Text);
        Assert.Equal(4, m.Widgets.Count(w => w.Kind == WidgetKind.ModeCard));               // modes are cards with a description
        Assert.All(m.Widgets.Where(w => w.Kind == WidgetKind.ModeCard), w => Assert.False(string.IsNullOrEmpty(w.Sub)));
    }

    [Fact]
    public void Without_capacities_there_are_no_cards_and_time_left_shows_only_on_battery()
    {
        var partial = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 64, false, false, 12_000), new BatteryDetails(null, null, null, null, null), open: true));
        Assert.DoesNotContain(partial.Widgets, w => w.Kind == WidgetKind.StatCards);
        Assert.Equal("3 h 20 min", partial.Widgets.Single(w => w.Id == "bat-left").Sub);
        Assert.Equal("Quedan 3 h 20 min", ((BatteryHeroData)partial.Widgets.Single(w => w.Id == "battery").Data!).Detail);
        var plugged = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 80, true, true, null), null));
        Assert.DoesNotContain(plugged.Widgets, w => w.Id == "bat-left"); Assert.Contains(plugged.Widgets, w => w.Id == "bat-reading");
        Assert.Null(new BatteryDetails(0, 52_000, null, null, null).HealthPercent);   // a driver reporting 0 as design capacity: no health
    }
}

public class BrightnessLabelWm016Tests
{
    [Fact]
    public void The_slider_says_whether_it_moves_the_internal_panel_or_external_monitors()
    {
        Assert.Equal("Brillo · pantalla integrada", PanelBuilder.BrightnessLabel(new BrightnessState(true, [new MonitorBrightness("wmi:0", "Pantalla integrada", 50, "WMI")])));
        Assert.Equal("Brillo · monitor externo (DDC/CI)", PanelBuilder.BrightnessLabel(new BrightnessState(true, [new MonitorBrightness("ddc:1.0", "Pantalla 1", 50, "DDC/CI")])));
        Assert.Equal("Brillo · integrada y 1 externa(s)", PanelBuilder.BrightnessLabel(new BrightnessState(true, [new MonitorBrightness("wmi:0", "Pantalla integrada", 50, "WMI"), new MonitorBrightness("ddc:2.0", "Pantalla 2", 40, "DDC/CI")])));
        Assert.Equal("Brillo", PanelBuilder.BrightnessLabel(BrightnessState.None("x")));
    }
}
