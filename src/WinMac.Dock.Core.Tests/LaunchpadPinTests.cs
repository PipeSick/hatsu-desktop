using System.Text.Json;
using WinMac.Dock;
using Xunit;

namespace WinMac.Dock.Core.Tests;

/// <summary>Launchpad + Dock: el Dock del monitor de Launchpad se ve mientras Launchpad está abierto, sin tocar su configuración.</summary>
public class LaunchpadPinTests
{
    private static DockController Make(DockConfig cfg)
    {
        var c = new DockController(new DockMetrics(), cfg); var l = new List<DockItemModel>();
        for (int i = 0; i < 6; i++) l.Add(new DockItemModel($"app:{i}", DockItemKind.App, $"A{i}", $"C:\\a{i}.exe", true, i == 1, i == 1 ? 1 : 0, i == 1));
        l.Add(new DockItemModel("sep", DockItemKind.Separator, "", null)); l.Add(new DockItemModel("trash", DockItemKind.Trash, "T", "shell:RecycleBinFolder"));
        c.SetItems(l); return c;
    }
    private static void Run(DockController c, double s) { for (double t = 0; t < s; t += 1.0 / 60) c.Step(1.0 / 60); }

    [Fact] public void With_autohide_the_dock_hides_but_appears_while_launchpad_is_open_and_hides_again_after_closing()
    {
        var c = Make(new DockConfig { AutoHide = true, HideDelaySeconds = 0.4, ShowDelaySeconds = 0.1 }); Run(c, 3); Assert.True(c.Step(0).HideT > 0.95);   // oculto
        c.SetLaunchpadOpen(true); Run(c, 1.5); Assert.True(c.Step(0).HideT < 0.05); Assert.True(c.LaunchpadPinned);                                       // visible
        c.SetLaunchpadOpen(true); Run(c, 3); Assert.True(c.Step(0).HideT < 0.05);                                                                          // el latido lo mantiene
        c.SetLaunchpadOpen(false); Assert.False(c.LaunchpadPinned); Run(c, 0.2); Assert.True(c.Step(0).HideT < 0.5);                                      // al cerrar espera el retardo normal…
        Run(c, 3); Assert.True(c.Step(0).HideT > 0.95);                                                                                                    // …y recupera su comportamiento
        Assert.True(c.Config.AutoHide);                                                                                                                    // la configuración no se tocó
    }

    [Fact] public void Without_autohide_nothing_changes_and_the_setting_stays_off()
    {
        var c = Make(new DockConfig { AutoHide = false }); Run(c, 1); Assert.True(c.Step(0).HideT < 0.01);
        c.SetLaunchpadOpen(true); Run(c, 1); Assert.True(c.Step(0).HideT < 0.01); c.SetLaunchpadOpen(false); Run(c, 3); Assert.True(c.Step(0).HideT < 0.01); Assert.False(c.Config.AutoHide);
    }

    [Fact] public void If_the_bar_dies_with_launchpad_open_the_pin_expires_by_itself()
    {
        var c = Make(new DockConfig { AutoHide = true, HideDelaySeconds = 0.3 }); Run(c, 3);
        c.SetLaunchpadOpen(true); Run(c, 1); Assert.True(c.LaunchpadPinned);
        Run(c, LaunchpadPin.TtlSeconds + 0.5); Assert.False(c.LaunchpadPinned);          // sin latidos: caduca
        Run(c, 3); Assert.True(c.Step(0).HideT > 0.95);                                  // y el Dock vuelve a su comportamiento normal
    }

    [Fact] public void Launchpad_overrides_the_fullscreen_suppression_because_launchpad_itself_is_a_fullscreen_window()
    {
        var c = Make(new DockConfig()); Run(c, 1); c.SetSuppressed(true); Run(c, 2); Assert.True(c.Step(0).HideT > 0.95);
        c.SetLaunchpadOpen(true); Run(c, 1.5); Assert.True(c.Step(0).HideT < 0.05);
        c.SetLaunchpadOpen(false); Run(c, 2); Assert.True(c.Step(0).HideT > 0.95);       // sigue suprimido por la pantalla completa real
    }

    [Fact] public void Pin_asks_for_frames_so_the_dock_is_not_left_half_drawn()
    {
        var c = Make(new DockConfig { AutoHide = true }); Run(c, 3); Assert.False(c.Step(1.0 / 60).NeedsFrames);
        c.SetLaunchpadOpen(true); Assert.True(c.Step(1.0 / 60).NeedsFrames);
    }

    [Fact] public void Pin_ttl_counts_down_and_close_releases_immediately()
    {
        var p = new LaunchpadPin(); Assert.False(p.Active); p.Apply(true); Assert.True(p.Active); p.Step(LaunchpadPin.TtlSeconds - 0.1); Assert.True(p.Active); p.Step(0.2); Assert.False(p.Active);
        p.Apply(true); p.Apply(false); Assert.False(p.Active);
    }

    [Fact] public void Routing_picks_only_the_dock_of_the_monitor_where_launchpad_is_including_negative_coordinates()
    {
        string[] keys = ["0,0,1920,1080", "1920,0,3840,1080", "-1280,0,0,1024"];
        Assert.Equal("0,0,1920,1080", LaunchpadRouting.MonitorKeyFor(new LaunchpadState(true, 0, 0, 1920, 1080), keys));
        Assert.Equal("1920,0,3840,1080", LaunchpadRouting.MonitorKeyFor(new LaunchpadState(true, 1920, 0, 1920, 1080), keys));
        Assert.Equal("-1280,0,0,1024", LaunchpadRouting.MonitorKeyFor(new LaunchpadState(true, -1280, 0, 1280, 1024), keys));
        Assert.Null(LaunchpadRouting.MonitorKeyFor(new LaunchpadState(true, 5000, 0, 800, 600), keys));   // monitor sin Dock (desactivado o aún no creado): ningún Dock cambia
        Assert.Null(LaunchpadRouting.MonitorKeyFor(new LaunchpadState(true, 0, 0, 0, 0), keys));          // mensaje sin tamaño: se ignora
        Assert.Null(LaunchpadRouting.MonitorKeyFor(new LaunchpadState(true, 0, 0, 100, 100), ["basura", "1,2,3"]));
    }

    [Fact] public void Message_parsing_is_tolerant()
    {
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement;
        Assert.Equal(new LaunchpadState(true, 10, 20, 300, 400), LaunchpadState.Parse(J("{\"open\":true,\"x\":10,\"y\":20,\"w\":300,\"h\":400}")));
        Assert.False(LaunchpadState.Parse(J("{\"open\":false}"))!.Value.Open);
        Assert.Null(LaunchpadState.Parse(null)); Assert.Null(LaunchpadState.Parse(J("[1,2]"))); Assert.False(LaunchpadState.Parse(J("{\"open\":\"yes\"}"))!.Value.Open);
    }
}
