using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Piezas del prototipo de Ajustes: asa para arrastrar, píldoras azules y «Personalizar iconos…».</summary>
public class SettingsPrototypeParityTests
{
    private static SettingsController Bar()
    {
        var c = new SettingsController(); c.SetPages(SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false))); c.SelectPage("bar"); return c;
    }

    private static (double X, double Y) Center(SettingsController c, (double X, double Y, double W, double H) r) => (SettingsLayout.SidebarW + r.X + r.W / 2, SettingsLayout.TitleH + r.Y + r.H / 2);

    [Fact]
    public void Bar_icon_rows_have_a_grip_and_the_add_rows_have_a_blue_pill()
    {
        var c = Bar(); var rows = c.Frame.Rows;
        var first = rows.First(r => r.Row.Id.StartsWith("baritem:", StringComparison.Ordinal)); Assert.True(SettingsLayout.HasGrip(first.Row));
        if (rows.FirstOrDefault(r => r.Row.Id.StartsWith("baritem.add:", StringComparison.Ordinal)) is { } add) { Assert.Equal(["Agregar"], add.Row.Options); Assert.DoesNotContain("Agregar:", add.Row.Title); }
    }

    [Fact]
    public void Dragging_the_grip_two_rows_down_moves_the_icon_two_places()
    {
        var c = Bar(); var grips = c.Frame.Rows.Where(r => SettingsLayout.HasGrip(r.Row)).ToList(); Assert.True(grips.Count >= 3);
        var from = grips[0]; var (x, y) = Center(c, SettingsLayout.GripRect(from)); Assert.Null(c.PointerDown(x, y));
        var target = grips[2]; c.PointerMove(x, SettingsLayout.TitleH + target.Y + target.H / 2); Assert.Equal((0, 2), (c.Reordering!.Value.From, c.Reordering!.Value.To));
        var e = c.PointerUp(x, SettingsLayout.TitleH + target.Y + target.H / 2); Assert.Equal(new SettingsEdit(from.Row.Id, "move", Index: 2), e);
    }

    [Fact]
    public void Clicking_the_grip_without_dragging_does_nothing()
    {
        var c = Bar(); var from = c.Frame.Rows.First(r => SettingsLayout.HasGrip(r.Row)); var (x, y) = Center(c, SettingsLayout.GripRect(from));
        c.PointerDown(x, y); Assert.Null(c.PointerUp(x, y));
    }

    [Fact]
    public void The_move_edit_reorders_the_saved_bar_order()
    {
        var cfg = new HostConfig(); var pages = SettingsBuilder.Build(new SettingsInput(cfg, new Dictionary<string, ModuleState>(), false));
        var ids = pages.First(p => p.Id == "bar").Groups.SelectMany(g => g.Rows).Where(r => r.Id.StartsWith("baritem:", StringComparison.Ordinal)).Select(r => r.Id).ToList();
        var after = SettingsBuilder.BarIconsInOrder(new WinMac.Bar.BarConfig()).Select(d => d.Id).ToList();
        Assert.Equal(ids.Select(i => i[8..]), after);
    }

    [Fact]
    public void Customize_pill_in_the_preview_strip_is_a_real_button_that_scrolls_to_the_add_list()
    {
        var c = Bar(); var prev = c.Frame.Rows.First(r => r.Row.Kind == RowKind.Preview); var (x, y) = Center(c, SettingsLayout.PreviewPill(prev));
        c.PointerDown(x, y); var e = c.PointerUp(x, y); Assert.Equal(new SettingsEdit("bar.preview", "button"), e);
        c.ScrollToGroup("Disponibles para agregar"); Assert.True(c.ScrollOffset >= 0);
    }
}
