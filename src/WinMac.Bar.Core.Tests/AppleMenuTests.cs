using WinMac.Bar;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Menú de la manzana con aspecto de macOS (antes, el menú nativo de Windows).
public class AppleMenuTests
{
    private static PanelContext Ctx(string? app = "LeagueClientUx", bool reserve = true) => PanelContext.Initial(new BarConfig { ReserveSpace = reserve }, new DateTime(2026, 11, 2)) with { ForegroundApp = app };

    [Fact]
    public void It_has_the_same_real_actions_in_macos_order_with_thin_separators()
    {
        var m = PanelBuilder.AppleMenu(Ctx());
        var items = m.Widgets.Where(w => w.Kind == WidgetKind.MenuItem).Select(w => w.Id).ToList();
        Assert.Equal(["apple:About", "apple:Settings", "apple:Store", "apple:ForceQuit", "apple:Sleep", "apple:Restart", "apple:Shutdown", "apple:Lock", "apple:LogOff", "apple:reserve", "apple:quitbar"], items);
        Assert.Equal(5, m.Widgets.Count(w => w.Kind == WidgetKind.Separator));
        Assert.Equal("Forzar salida de «LeagueClientUx»…", m.Widgets.Single(w => w.Id == "apple:ForceQuit").Text);
        Assert.True(m.Widgets.Single(w => w.Id == "apple:reserve").On);
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
    public void Rows_are_compact_and_contiguous_and_other_panels_keep_their_spacing()
    {
        var f = PanelLayout.Compute(PanelBuilder.AppleMenu(Ctx()));
        var rows = f.Items.Where(i => i.W.Kind == WidgetKind.MenuItem).ToList();
        Assert.Equal(5, f.Items[0].X); Assert.Equal(5, f.Items[0].Y); Assert.Equal(PanelBuilder.MenuWidth - 10, f.Items[0].Width);
        Assert.Equal(rows[0].Y + 24, rows[1].Y - 8, 6);   // About → separador (8) → Configuración: sin huecos extra
        Assert.Equal(rows[1].Y + 24, rows[2].Y, 6);
        Assert.True(f.Height < 340);
        var cc = PanelLayout.Compute(PanelBuilder.ControlCenter(PanelContext.Initial(new BarConfig(), DateTime.Now)));
        Assert.Equal(PanelLayout.Pad, cc.Items[0].X);   // el Control Center no cambia
        var ctl = new PanelController(); ctl.SetModel(PanelBuilder.AppleMenu(Ctx())); var r = ctl.Frame.Items.Single(i => i.W.Id == "apple:Lock");
        ctl.PointerDown(r.X + 20, r.Y + 10); Assert.Equal("apple:Lock", ctl.PointerUp(r.X + 20, r.Y + 10)!.Id);
        var off = PanelBuilder.AppleMenu(Ctx(app: null)); ctl.SetModel(off); var d = ctl.Frame.Items.Single(i => i.W.Id == "apple:ForceQuit");
        ctl.PointerDown(d.X + 20, d.Y + 10); Assert.Null(ctl.PointerUp(d.X + 20, d.Y + 10));   // atenuada: no hace nada
    }
}
