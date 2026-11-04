using WinMac.Connect;
using Xunit;

namespace WinMac.Bar.Tests;

public class BarItemsTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static PhoneState Phone(PhoneLinkStatus st, int? bat = null, DateTime? at = null, PhoneFeatureStatus bs = PhoneFeatureStatus.Active) =>
        new() { Status = st, DeviceName = "iPhone de Ana", Battery = bat, BatteryAtUtc = at, BatteryStatus = bs };

    // ── iPhone
    [Fact] public void Phone_connected_with_fresh_reading_shows_percent()
    { var i = PhoneBar.Evaluate(Phone(PhoneLinkStatus.Connected, 82, Now.AddMinutes(-1)), Now, true)!; Assert.Equal("82%", i.Text); Assert.Equal(BarItemLook.Normal, i.Look); }
    [Fact] public void Phone_percent_can_be_hidden_but_icon_stays()
    { var i = PhoneBar.Evaluate(Phone(PhoneLinkStatus.Connected, 82, Now), Now, false)!; Assert.Equal("", i.Text); Assert.Contains("82", i.Tip); }
    [Fact] public void Phone_stale_reading_is_never_shown_as_current()
    { var i = PhoneBar.Evaluate(Phone(PhoneLinkStatus.Connected, 82, Now.AddHours(-2)), Now, true)!; Assert.Equal("—", i.Text); Assert.Contains("desconocida", i.Tip); }
    [Fact] public void Phone_connected_without_reading_is_unknown()
    { var i = PhoneBar.Evaluate(Phone(PhoneLinkStatus.Connected), Now, true)!; Assert.Equal("—", i.Text); }
    [Fact] public void Phone_disconnected_is_slashed_without_percent()
    { var i = PhoneBar.Evaluate(Phone(PhoneLinkStatus.Disconnected, 82, Now), Now, true)!; Assert.Equal("", i.Text); Assert.Equal(BarItemLook.Slashed, i.Look); }
    [Fact] public void Phone_connecting_is_dimmed() => Assert.Equal(BarItemLook.Dim, PhoneBar.Evaluate(Phone(PhoneLinkStatus.Connecting), Now, true)!.Look);
    [Theory] [InlineData(PhoneLinkStatus.NoDevice)] [InlineData(PhoneLinkStatus.NoBluetooth)]
    public void Phone_hidden_without_device_or_bluetooth(PhoneLinkStatus st) => Assert.Null(PhoneBar.Evaluate(Phone(st), Now, true));
    [Fact] public void Phone_hidden_when_battery_feature_off() => Assert.Null(PhoneBar.Evaluate(Phone(PhoneLinkStatus.Connected, 50, Now, PhoneFeatureStatus.Off), Now, true));
    [Fact] public void Phone_null_state_hidden() => Assert.Null(PhoneBar.Evaluate(null, Now, true));

    // ── Batería del PC
    [Fact] public void Pc_with_battery_shows_percent()
    { var i = PcPowerBar.Evaluate(new BatteryInfo(true, 64, false, false), new BarConfig())!; Assert.Equal(BarItemKind.Battery, i.Kind); Assert.Equal("64%", i.Text); }
    [Fact] public void Pc_without_battery_hides_module_by_default() => Assert.Null(PcPowerBar.Evaluate(new BatteryInfo(false, 0, false, true), new BarConfig()));
    [Fact] public void Pc_without_battery_never_invents_100() => Assert.Null(PcPowerBar.Evaluate(BatteryInfo.None, new BarConfig { ShowPowerWithoutBattery = true }));
    [Fact] public void Pc_without_battery_on_ac_can_show_plug_without_percent()
    { var i = PcPowerBar.Evaluate(new BatteryInfo(false, 0, false, true), new BarConfig { ShowPowerWithoutBattery = true })!; Assert.Equal(BarItemKind.PowerPlug, i.Kind); Assert.Equal("", i.Text); Assert.Equal("Battery", i.Id); }
    [Fact] public void Battery_percent_option_off_keeps_icon() => Assert.Equal("", PcPowerBar.Evaluate(new BatteryInfo(true, 64, false, false), new BarConfig { ShowBatteryPercent = false })!.Text);
    [Theory]
    [InlineData(@"\\?\acpi#pnp0c0a#1#{72631e54-78a4-11d0-bcf7-00aa00b7b32a}", BatteryDeviceKind.Internal)]
    [InlineData(@"\\?\hid#vid_051d&pid_0002#7&2a#{72631e54-78a4-11d0-bcf7-00aa00b7b32a}", BatteryDeviceKind.Ups)]
    [InlineData(@"\\?\USB#VID_0764&PID_0501#5&1#{72631e54}", BatteryDeviceKind.Ups)]
    [InlineData(null, BatteryDeviceKind.Internal)]
    public void Battery_device_classification(string? path, BatteryDeviceKind k) => Assert.Equal(k, BatteryDeviceClass.Classify(path));
    [Fact] public void Only_ups_means_no_pc_battery() => Assert.False(BatteryDeviceClass.HasInternal([@"\\?\hid#vid_051d&pid_0002#x"]));

    // ── Orden
    private static List<BarItem> Right(params BarItemKind[] ks) => ks.Select(k => new BarItem(k, "", 16)).ToList();
    [Fact] public void Default_order_when_nothing_saved()
    { var r = BarOrdering.Apply(Right(BarItemKind.Hub, BarItemKind.Network, BarItemKind.Volume, BarItemKind.ControlCenter, BarItemKind.Clock), ""); Assert.Equal(["Hub", "Network", "Volume", "ControlCenter", "Clock"], r.Select(i => i.Id)); }
    [Fact] public void Saved_order_applies_and_fixed_items_stay_last()
    { var r = BarOrdering.Apply(Right(BarItemKind.Hub, BarItemKind.Network, BarItemKind.Volume, BarItemKind.ControlCenter, BarItemKind.Clock), "Volume,Network,Hub"); Assert.Equal(["Volume", "Network", "Hub", "ControlCenter", "Clock"], r.Select(i => i.Id)); }
    [Fact] public void Unknown_and_duplicate_ids_are_ignored()
    { var r = BarOrdering.Apply(Right(BarItemKind.Hub, BarItemKind.Network, BarItemKind.Clock), "Zzz,Network,Network,Hub"); Assert.Equal(["Network", "Hub", "Clock"], r.Select(i => i.Id)); }
    [Fact] public void New_module_enters_after_its_default_predecessor()
    { var r = BarOrdering.Apply(Right(BarItemKind.Hub, BarItemKind.Network, BarItemKind.Volume, BarItemKind.Clock), "Volume,Hub"); Assert.Equal(["Volume", "Hub", "Network", "Clock"], r.Select(i => i.Id)); }
    [Fact] public void Move_forward_and_backward()
    {
        Assert.Equal(["B", "C", "A"], BarOrdering.Move(["A", "B", "C"], "A", 3));
        Assert.Equal(["C", "A", "B"], BarOrdering.Move(["A", "B", "C"], "C", 0));
        Assert.Equal(["A", "B", "C"], BarOrdering.Move(["A", "B", "C"], "B", 1));   // soltar en su propio hueco no cambia nada
        Assert.Equal(["A", "B", "C"], BarOrdering.Move(["A", "B", "C"], "B", 2));
    }
    [Fact] public void Merge_keeps_position_of_hidden_modules()
    {
        // guardado: Phone,Network,Volume ; hoy el iPhone está oculto y se arrastra Volume delante de Network
        Assert.Equal("Phone,Volume,Network", BarOrdering.Merge("Phone,Network,Volume", ["Volume", "Network"]));
    }
    [Fact] public void Merge_appends_modules_without_saved_position() => Assert.Equal("Network,Hub", BarOrdering.Merge("", ["Network", "Hub"]));
    [Fact] public void Reset_order_is_empty_string_and_gives_default()
    { var r = BarOrdering.Apply(Right(BarItemKind.Hub, BarItemKind.Network, BarItemKind.Clock), BarOrdering.Serialize([])); Assert.Equal(["Hub", "Network", "Clock"], r.Select(i => i.Id)); }
    [Fact] public void Sanitize_cleans_order() => Assert.Equal("A,B", new BarConfig { BarOrder = " A ; B,,A" }.Sanitize().BarOrder);
    [Fact] public void Order_persists_through_patch_and_json()
    {
        var c = new BarConfig().ApplyPatch(System.Text.Json.JsonDocument.Parse("{\"barOrder\":\"Volume,Hub\",\"showTaskManager\":true}").RootElement);
        Assert.Equal("Volume,Hub", c.BarOrder); Assert.True(c.ShowTaskManager);
        Assert.Equal("Volume,Hub", BarConfig.FromJson(c.ToJson()).BarOrder);
    }

    // ── Administrador de tareas y modelo
    [Fact] public void Task_manager_item_off_by_default_and_on_by_option()
    {
        var s = BarState.Empty(new DateTime(2026, 11, 4, 12, 0, 0));
        Assert.DoesNotContain(BarModel.Build(s, new BarConfig()).Right, i => i.Kind == BarItemKind.TaskManager);
        var tm = BarModel.Build(s, new BarConfig { ShowTaskManager = true }).Right.Single(i => i.Kind == BarItemKind.TaskManager);
        Assert.Equal("Administrador de tareas", tm.Tip); Assert.Equal(BarActionKind.OpenTaskManager, BarModel.ClickAction(tm).Kind);
    }
    [Fact] public void Task_manager_participates_in_order_but_clock_stays_last()
    {
        var s = BarState.Empty(new DateTime(2026, 11, 4, 12, 0, 0));
        var r = BarModel.Build(s, new BarConfig { ShowTaskManager = true, ShowHub = true, BarOrder = "TaskManager,Hub" }).Right;
        Assert.Equal("TaskManager", r[0].Id); Assert.Equal("Clock", r[^1].Id);
    }

    // ── Arrastrar
    private static BarController Ctl(string order = "")
    {
        var c = new BarController(new BarMetrics(), new BarConfig { ShowTaskManager = true, ShowHub = true, ShowWeather = false, ShowTrayIcons = false, BarOrder = order }, BarState.Empty(new DateTime(2026, 11, 4, 12, 0, 0)));
        c.SetWidth(1200); return c;
    }
    private static double CenterOf(BarController c, string id) { var f = c.Frame.Items.First(i => i.Item.Id == id); return f.X + f.W / 2; }
    [Fact] public void Click_without_movement_is_not_a_drag()
    {
        var c = Ctl(); double x = CenterOf(c, "Hub"); c.PressAt(x); Assert.False(c.DragTo(x + 2)); Assert.Null(c.Release(x + 2, out bool drag)); Assert.False(drag);
    }
    [Fact] public void Drag_hub_after_task_manager_returns_new_order()
    {
        var c = Ctl(); double hub = CenterOf(c, "Hub"), tm = CenterOf(c, "TaskManager");
        c.PressAt(hub); Assert.True(c.DragTo(tm + 6)); Assert.NotNull(c.Step(0.016).DropX);
        var order = c.Release(tm + 6, out bool drag); Assert.True(drag); Assert.Equal("TaskManager,Hub", order);
    }
    [Fact] public void Drag_to_same_place_changes_nothing()
    {
        var c = Ctl(); double hub = CenterOf(c, "Hub"); c.PressAt(hub); c.DragTo(hub + 6); Assert.Null(c.Release(hub + 6, out bool drag)); Assert.True(drag);
    }
    [Fact] public void Clock_and_control_center_cannot_be_dragged()
    {
        var c = Ctl(); c.PressAt(CenterOf(c, "Clock")); Assert.False(c.DragTo(CenterOf(c, "Clock") - 80));
        c.PressAt(CenterOf(c, "ControlCenter")); Assert.False(c.DragTo(CenterOf(c, "ControlCenter") - 80));
    }
    [Fact] public void Dropping_past_the_clock_clamps_inside_movable_range()
    {
        var c = Ctl(); double hub = CenterOf(c, "Hub"); c.PressAt(hub); c.DragTo(1190);
        Assert.Equal("TaskManager,Hub", c.Release(1190, out _));
    }
    [Fact] public void Cancel_drag_discards() { var c = Ctl(); c.PressAt(CenterOf(c, "Hub")); c.DragTo(CenterOf(c, "Hub") + 40); c.CancelDrag(); Assert.False(c.Dragging); }
}
