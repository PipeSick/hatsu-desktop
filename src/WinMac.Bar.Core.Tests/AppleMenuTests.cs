using WinMac.SystemControl;
using WinMac.Bar;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Menú de la manzana con aspecto de macOS (antes, el menú nativo de Windows).
public class AppleMenuTests
{
    private static PanelContext Ctx(string? app = "LeagueClientUx", bool reserve = true) => PanelContext.Initial(new BarConfig { ReserveSpace = reserve }, new DateTime(2026, 11, 2)) with { ForegroundApp = app };

    [Fact]
    public void Wm009_it_is_a_control_center_with_the_user_real_tiles_session_actions_and_the_old_options()
    {
        var m = PanelBuilder.AppleMenu(Ctx() with { User = new SessionUser("Ana María Pérez", "PORTATIL") });
        var head = m.Widgets[0]; Assert.Equal(WidgetKind.UserHeader, head.Kind); Assert.Equal("Ana María Pérez", head.Text); Assert.Equal("AM", ((UserHeaderData)head.Data!).Initials);
        Assert.Equal(["open:wifi", "open:bluetooth", "open:ms-settings:notifications", "open:display", "open:battery", "open:ms-settings:"], m.Widgets.Where(w => w.Kind == WidgetKind.Tile).Select(w => w.Id));
        Assert.Equal(["volume", "brightness"], m.Widgets.Where(w => w.Kind == WidgetKind.Slider).Select(w => w.Id));
        var strip = (ActionStripData)m.Widgets.Single(w => w.Kind == WidgetKind.ActionStrip).Data!;
        Assert.Equal(["apple:Sleep", "apple:Restart", "apple:Shutdown", "apple:LogOff", "apple:Lock"], strip.Actions.Select(a => a.Id));   // the same actions (and confirmations) as before
        Assert.Equal(["apple:About", "apple:ForceQuit", "apple:reserve", "apple:quitbar"], m.Widgets.Where(w => w.Kind == WidgetKind.MenuItem).Select(w => w.Id));
        Assert.Equal("Forzar salida de «LeagueClientUx»…", m.Widgets.Single(w => w.Id == "apple:ForceQuit").Text);
        Assert.True(m.Widgets.Single(w => w.Id == "apple:reserve").On);
        Assert.False(m.Widgets.Single(w => w.Id == "open:wifi").Enabled);   // no state yet: no fake «connected»
        Assert.Equal(PanelKind.AppleMenu, PanelBuilder.Build(PanelKind.AppleMenu, Ctx()).Kind);
    }

    [Fact]
    public void Force_quit_is_dimmed_without_an_app_that_can_be_closed_and_the_check_follows_the_setting()
    {
        var m = PanelBuilder.AppleMenu(Ctx(app: null, reserve: false));
        var fq = m.Widgets.Single(w => w.Id == "apple:ForceQuit"); Assert.False(fq.Enabled); Assert.Equal("Forzar salida…", fq.Text);
        Assert.False(m.Widgets.Single(w => w.Id == "apple:reserve").On);
    }

    [Fact]
    public void Wm009_session_buttons_and_the_power_button_are_real_clicks_and_dimmed_items_do_nothing()
    {
        var ctl = new PanelController(); ctl.SetModel(PanelBuilder.AppleMenu(Ctx()));
        var strip = ctl.Frame.Items.Single(i => i.W.Kind == WidgetKind.ActionStrip); double cw = strip.Width / 5;
        ctl.PointerDown(strip.X + cw * 4.5, strip.Y + 20); Assert.Equal("apple:Lock", ctl.PointerUp(strip.X + cw * 4.5, strip.Y + 20)!.Id);
        ctl.PointerDown(strip.X + cw * 0.5, strip.Y + 20); Assert.Equal("apple:Sleep", ctl.PointerUp(strip.X + cw * 0.5, strip.Y + 20)!.Id);
        var head = ctl.Frame.Items[0];
        ctl.PointerDown(head.X + head.Width - 20, head.Y + 25); Assert.Equal("apple:Shutdown", ctl.PointerUp(head.X + head.Width - 20, head.Y + 25)!.Id);
        ctl.PointerDown(head.X + 30, head.Y + 25); Assert.Null(ctl.PointerUp(head.X + 30, head.Y + 25));   // the name is not a button
        var off = PanelBuilder.AppleMenu(Ctx(app: null)); ctl.SetModel(off); var d = ctl.Frame.Items.Single(i => i.W.Id == "apple:ForceQuit");
        ctl.PointerDown(d.X + 20, d.Y + 10); Assert.Null(ctl.PointerUp(d.X + 20, d.Y + 10));
        var hgt = PanelLayout.Compute(PanelBuilder.AppleMenu(Ctx())).Height; Assert.True(hgt < 640, "alto " + hgt);   // worst case (every control disabled with its reason); 150 % on 1080p still fits under the bar
    }
}

public class BatteryPanelWm010Tests
{
    private static PanelContext Ctx(BatteryInfo b, BatteryDetails? d) => PanelContext.Initial(new BarConfig(), new DateTime(2026, 11, 3)) with { Battery = b, BatteryDetail = d };

    [Fact]
    public void Shows_only_what_the_battery_reports_and_says_what_it_does_not()
    {
        var full = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 64, false, false, 12_000), new BatteryDetails(57_000, 52_440, 33_560, -9_800, 212)));
        var kv = full.Widgets.Where(w => w.Kind == WidgetKind.KeyValue).ToDictionary(w => w.Id, w => w.Sub);
        Assert.Equal("3 h 20 min", kv["bat-left"]); Assert.Equal("92 %", kv["bat-health"]); Assert.Equal("212", kv["bat-cycles"]); Assert.Equal("57,0 Wh", kv["bat-design"]);
        Assert.DoesNotContain(full.Widgets, w => w.Id == "bat-missing");

        var partial = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 64, false, false), new BatteryDetails(null, 52_440, null, null, null)));
        Assert.DoesNotContain(partial.Widgets, w => w.Id is "bat-health" or "bat-cycles" or "bat-design");   // never invented
        Assert.Contains(partial.Widgets, w => w.Id == "bat-missing");
        Assert.Equal("Windows lo está calculando", partial.Widgets.Single(w => w.Id == "bat-left").Sub);

        var charging = PanelBuilder.BatteryPanel(Ctx(new BatteryInfo(true, 80, true, true, null), null));
        Assert.DoesNotContain(charging.Widgets, w => w.Id == "bat-left");   // plugged in: no «time left»
        Assert.Equal("Cargando", charging.Widgets.Single(w => w.Id == "battery").Sub);
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
