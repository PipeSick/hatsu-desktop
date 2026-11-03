using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

public class TrayPanelTests
{
    private static readonly Dictionary<string, ModuleState> States = new() { ["bar"] = ModuleState.Running, ["dock"] = ModuleState.Failed, ["notch"] = ModuleState.Disabled };

    [Fact]
    public void Wm011_status_modules_with_the_action_that_fits_and_safe_exit()
    {
        var items = TrayPanelModel.Build(States, "2.0");
        Assert.Equal("1 módulo necesita atención", items[0].Sub);
        var mods = items.Where(i => i.Kind == TrayPanelModel.Kind.Module).ToList();
        Assert.Equal(["restart:bar", "open:dock", "enable:notch"], mods.Select(m => m.Action));
        Assert.Equal(["Barra superior", "Dock", "Isla"], mods.Select(m => m.Text));
        Assert.Equal("red", TrayPanelModel.StateColor(ModuleState.Failed)); Assert.Equal("green", TrayPanelModel.StateColor(ModuleState.Running));
        Assert.Equal(["restart:bar", "open:dock", "enable:notch", "settings", "restart-all", "quit"], TrayPanelModel.Focusable(items));
        Assert.True(items.Single(i => i.Action == "quit").Danger);
        Assert.Equal("Todo en marcha", TrayPanelModel.Build(new Dictionary<string, ModuleState> { ["bar"] = ModuleState.Running }, "2.0")[0].Sub);
    }

    [Fact]
    public void Wm011_clicks_hit_the_module_button_or_the_command_row_only()
    {
        var items = TrayPanelModel.Build(States, "2.0"); var lay = TrayPanelModel.Layout(items);
        var bar = lay.Single(l => l.Item.Action == "restart:bar"); var (bx, bw) = TrayPanelModel.ButtonX(bar.Item);
        Assert.Equal("restart:bar", TrayPanelModel.Hit(items, bx + bw / 2, bar.Y + bar.H / 2));
        Assert.Null(TrayPanelModel.Hit(items, 30, bar.Y + bar.H / 2));   // the module name is not a button
        var quit = lay.Single(l => l.Item.Action == "quit");
        Assert.Equal("quit", TrayPanelModel.Hit(items, 40, quit.Y + 5));
        Assert.Null(TrayPanelModel.Hit(items, 40, lay[0].Y + 10));   // header
        Assert.True(TrayPanelModel.Height(items) < 420);
    }
}
