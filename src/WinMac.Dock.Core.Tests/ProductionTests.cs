using WinMac.Apps;
using WinMac.Dock;
using Xunit;

public class PinnedConfigTests
{
    [Fact]
    public void Legacy_config_with_only_paths_loads_and_gets_stable_ids()
    {
        var c = DockConfig.FromJson("""{ "Pinned": [ { "Path": "C:\\Windows\\notepad.exe", "Title": "Bloc" } ], "AutoHide": true }""");
        var id = c.Pinned[0].ToIdentity()!; Assert.Equal(AppKind.Win32, id.Kind); Assert.Equal("exe:C:\\WINDOWS\\NOTEPAD.EXE", c.Pinned[0].StableId); Assert.True(c.AutoHide);
    }

    [Fact]
    public void Packaged_and_icon_override_roundtrip_through_json()
    {
        var p = AppIdentity.FromAumid("Vendor.App_1!App", "Mi app", @"C:\pkg\app.exe");
        var c = DockPinning.SetIconOverride(DockPinning.Pin(new DockConfig(), p), p.Id, @"C:\iconos\mi.png");
        var d = DockConfig.FromJson(c.ToJson()); var q = d.Pinned.Single();
        Assert.Equal("Vendor.App_1!App", q.Aumid); Assert.Equal(@"C:\iconos\mi.png", q.IconOverride); Assert.Equal(AppKind.Packaged, q.ToIdentity()!.Kind); Assert.Equal(4, d.Version);
    }

    [Fact]
    public void Entries_without_path_or_aumid_are_skipped_by_the_builder_instead_of_crashing()
    {
        var cfg = new DockConfig { Pinned = [new PinnedApp(), new PinnedApp { Title = "roto" }, new PinnedApp(@"C:\ok.exe")] };
        var items = new DockModelBuilder(cfg).Build([]); Assert.Single(items, i => i.Kind == DockItemKind.App);
    }
}

public class PinningTests
{
    private static AppIdentity A(string n) => AppIdentity.FromExe($@"C:\{n}.exe");
    private static IReadOnlyList<string> Names(DockConfig c) => c.Pinned.Select(p => p.Title!).ToList();

    [Fact]
    public void Pin_adds_once_and_never_duplicates_the_same_app()
    {
        var c = DockPinning.Pin(new DockConfig(), A("a"), "A"); var c2 = DockPinning.Pin(c, AppIdentity.FromAumid("X_1!App", "X", @"c:\A.EXE"), "otra forma de A");   // mismo ejecutable
        Assert.Single(c2.Pinned); Assert.Same(c, c2);
    }

    [Fact]
    public void Operations_are_pure_the_original_config_is_untouched()
    {
        var c = DockPinning.Pin(DockPinning.Pin(new DockConfig(), A("a"), "A"), A("b"), "B");
        var u = DockPinning.Unpin(c, A("a").Id); Assert.Equal(2, c.Pinned.Count); Assert.Single(u.Pinned);
    }

    [Fact]
    public void Unpin_works_by_stable_id_by_exe_path_and_ignores_unknown()
    {
        var c = DockPinning.Pin(DockPinning.Pin(new DockConfig(), A("a"), "A"), A("b"), "B");
        Assert.Equal(["B"], Names(DockPinning.Unpin(c, A("a").Id))); Assert.Equal(["A"], Names(DockPinning.Unpin(c, @"c:\b.exe"))); Assert.Same(c, DockPinning.Unpin(c, "exe:NOPE"));
    }

    [Fact]
    public void Move_and_MoveBy_reorder_and_clamp()
    {
        var c = new DockConfig(); foreach (var n in new[] { "a", "b", "c", "d" }) c = DockPinning.Pin(c, A(n), n);
        Assert.Equal(["d", "a", "b", "c"], Names(DockPinning.Move(c, A("d").Id, 0))); Assert.Equal(["b", "c", "d", "a"], Names(DockPinning.Move(c, A("a").Id, 99)));
        Assert.Equal(["a", "c", "b", "d"], Names(DockPinning.MoveBy(c, A("b").Id, +1))); Assert.Equal(["a", "b", "c", "d"], Names(DockPinning.MoveBy(c, A("a").Id, -5)));
    }

    [Fact]
    public void ReorderTo_follows_the_given_order_ignores_unknown_ids_and_keeps_missing_at_the_end()
    {
        var c = new DockConfig(); foreach (var n in new[] { "a", "b", "c" }) c = DockPinning.Pin(c, A(n), n);
        Assert.Equal(["c", "a", "b"], Names(DockPinning.ReorderTo(c, [A("c").Id, "exe:FANTASMA", A("a").Id])));
    }

    [Fact]
    public void Pin_can_insert_at_an_index_and_titles_default_to_the_display_name()
    {
        var c = DockPinning.Pin(DockPinning.Pin(new DockConfig(), A("a"), "A"), A("zz"), null, index: 0);
        Assert.Equal("Zz", c.Pinned[0].Title);
    }
}

public class BuilderIdentityTests
{
    private static RunningApp Run(AppIdentity id, bool active = false, bool min = false, int windows = 1) =>
        new(id, 1, Enumerable.Range(0, windows).Select(i => new WindowInfo(100 + i, $"w{i}", min, active && i == 0)).ToList());

    [Fact]
    public void Pinned_packaged_app_matches_its_running_instance_by_aumid_without_duplicates()
    {
        var calc = AppIdentity.FromAumid("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculadora");
        var cfg = DockPinning.Pin(new DockConfig(), calc);
        var items = new DockModelBuilder(cfg).Build([Run(calc, active: true, windows: 2)]);
        var apps = items.Where(i => i.Kind == DockItemKind.App).ToList();
        Assert.Single(apps); Assert.True(apps[0].Pinned && apps[0].Running && apps[0].Active); Assert.Equal(2, apps[0].WindowCount); Assert.Equal(2, apps[0].Windows!.Count);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", apps[0].Path);
    }

    [Fact]
    public void Pinned_by_path_matches_a_running_packaged_instance_with_the_same_executable()
    {
        var cfg = DockPinning.Pin(new DockConfig(), AppIdentity.FromExe(@"C:\Apps\x.exe", "X"));
        var running = AppIdentity.FromAumid("V.X_1!App", "X", @"c:\apps\X.EXE");
        Assert.Single(new DockModelBuilder(cfg).Build([Run(running)]), i => i.Kind == DockItemKind.App);
    }

    [Fact]
    public void Running_but_unpinned_apps_are_temporary_recents_and_pinned_ones_never_appear_twice()
    {
        var a = AppIdentity.FromExe(@"C:\a.exe"); var b = AppIdentity.FromExe(@"C:\b.exe"); var cfg = DockPinning.Pin(new DockConfig { MaxRecents = 2 }, a);
        var items = new DockModelBuilder(cfg).Build([Run(a), Run(b), Run(b)]);
        Assert.Equal(2, items.Count(i => i.Kind == DockItemKind.App)); var rec = items.Single(i => i.Kind == DockItemKind.App && !i.Pinned); Assert.Equal(2, rec.WindowCount);   // dos procesos de B = un ítem
        Assert.Equal(b.Id, rec.Identity!.Id);
    }

    [Fact]
    public void By_default_every_open_unpinned_app_is_shown()
    {
        var apps = Enumerable.Range(0, 8).Select(i => AppIdentity.FromExe($@"C:\apps\app{i}.exe")).ToList();
        var items = new DockModelBuilder(new DockConfig()).Build(apps.Select(a => Run(a)).ToList());
        Assert.Equal(8, items.Count(i => i.Kind == DockItemKind.App && !i.Pinned));
    }

    [Fact]
    public void Old_config_with_the_former_default_cap_is_migrated_but_custom_caps_are_kept()
    {
        Assert.Equal(0, DockConfig.FromJson("{\"Version\":2,\"MaxRecents\":3}").MaxRecents);
        Assert.Equal(5, DockConfig.FromJson("{\"Version\":2,\"MaxRecents\":5}").MaxRecents);
        Assert.Equal(3, DockConfig.FromJson("{\"Version\":3,\"MaxRecents\":3}").MaxRecents);   // v3: el 3 ya es una elección explícita
        Assert.Equal(0, DockConfig.FromJson("{\"Pinned\":[]}").MaxRecents);                     // sin versión = antigua v1
    }

    [Fact]
    public void Icon_override_flows_to_the_item_and_identity_is_present()
    {
        var a = AppIdentity.FromExe(@"C:\a.exe"); var cfg = DockPinning.SetIconOverride(DockPinning.Pin(new DockConfig(), a), a.Id, @"C:\i.png");
        var it = new DockModelBuilder(cfg).Build([]).Single(i => i.Kind == DockItemKind.App); Assert.Equal(@"C:\i.png", it.IconOverride); Assert.Equal(a.Id, it.Identity!.Id); Assert.False(it.Running);
    }

    [Fact]
    public void Item_ids_are_stable_so_animations_survive_refreshes()
    {
        var a = AppIdentity.FromExe(@"C:\a.exe"); var cfg = DockPinning.Pin(new DockConfig(), a); var b = new DockModelBuilder(cfg);
        Assert.Equal(b.Build([]).First(i => i.Kind == DockItemKind.App).Id, b.Build([Run(a)]).First(i => i.Kind == DockItemKind.App).Id);
    }
}

public class MenuTests
{
    private static DockItemModel App(bool pinned, int windows, bool minimized = false, bool active = false) =>
        new("app:x", DockItemKind.App, "X", @"C:\x.exe", pinned, windows > 0, windows, active, minimized && windows > 0, AppIdentity.FromExe(@"C:\x.exe"),
            Enumerable.Range(0, windows).Select(i => new WindowInfo(10 + i, $"Ventana {i}", minimized, false)).ToList());
    private static IEnumerable<DockActionKind> Kinds(IEnumerable<MenuEntry> m) => m.Where(e => e.Action is not null).Select(e => e.Action!.Kind);

    [Fact]
    public void Not_running_app_offers_open_and_pin_but_nothing_about_windows()
    {
        var k = Kinds(DockMenu.ForItem(App(false, 0))).ToList(); Assert.Equal([DockActionKind.Launch, DockActionKind.Pin], k);
    }

    [Fact]
    public void Running_pinned_app_with_two_windows_lists_them_and_offers_unpin_close_and_quit()
    {
        var m = DockMenu.ForItem(App(true, 2), 1, 3); var k = Kinds(m).ToList();
        Assert.Equal(2, k.Count(x => x == DockActionKind.ActivateWindow)); Assert.Contains(DockActionKind.Activate, k); Assert.Contains(DockActionKind.Unpin, k); Assert.DoesNotContain(DockActionKind.Pin, k);
        Assert.Equal(2, k.Count(x => x == DockActionKind.CloseWindow)); Assert.Contains(DockActionKind.CloseApp, k); Assert.Contains(m, e => e.IsHeader);
    }

    [Fact]
    public void Single_window_app_has_one_plain_close_window_entry() =>
        Assert.Single(DockMenu.ForItem(App(false, 1)), e => e.Action?.Kind == DockActionKind.CloseWindow && e.Text == "Cerrar ventana");

    [Fact]
    public void Minimized_app_open_means_restore()
    {
        var open = DockMenu.ForItem(App(true, 1, minimized: true)).First(e => e.Text == "Abrir"); Assert.Equal(DockActionKind.Restore, open.Action!.Kind);
    }

    [Fact]
    public void Move_entries_are_disabled_at_the_ends()
    {
        var first = DockMenu.ForItem(App(true, 0), 0, 3); var last = DockMenu.ForItem(App(true, 0), 2, 3);
        Assert.False(first.First(e => e.Text.StartsWith("Mover a la izq")).Enabled); Assert.True(first.First(e => e.Text.StartsWith("Mover a la der")).Enabled);
        Assert.False(last.First(e => e.Text.StartsWith("Mover a la der")).Enabled);
    }

    [Fact]
    public void Window_list_is_capped_and_long_titles_truncated()
    {
        var it = App(false, 14) with { Windows = Enumerable.Range(0, 14).Select(i => new WindowInfo(i, new string('t', 200), false, false)).ToList() };
        var m = DockMenu.ForItem(it); Assert.Equal(DockMenu.MaxWindows, m.Count(e => e.Action?.Kind == DockActionKind.ActivateWindow)); Assert.Contains(m, e => e.Text.StartsWith("… y 4 más"));
        Assert.All(m, e => Assert.True(e.Text.Length <= DockMenu.MaxTitle + 30));
    }

    [Fact]
    public void Folder_trash_and_background_menus_are_minimal_and_safe()
    {
        Assert.Equal([DockActionKind.Open], Kinds(DockMenu.ForItem(new DockItemModel("trash", DockItemKind.Trash, "Papelera", "shell:RecycleBinFolder"))));
        Assert.Equal([DockActionKind.Quit], Kinds(DockMenu.ForBackground()));
    }

    [Fact] public void No_menu_action_can_terminate_a_process() => Assert.DoesNotContain(Enum.GetNames<DockActionKind>(), n => n.Contains("Kill") || n.Contains("Terminate"));
}

public class LaunchGuardTests
{
    [Fact]
    public void Repeated_launches_within_the_window_are_ignored_until_cleared_or_expired()
    {
        var now = DateTime.UtcNow; var g = new LaunchGuard(TimeSpan.FromSeconds(4), () => now);
        Assert.True(g.TryBegin("a")); Assert.False(g.TryBegin("a")); Assert.True(g.TryBegin("b"));
        now += TimeSpan.FromSeconds(5); Assert.True(g.TryBegin("a")); Assert.False(g.TryBegin("a")); g.Clear("a"); Assert.True(g.TryBegin("a"));
    }
}

public class DragTests
{
    private static DockController Make(out List<DockItemModel> items)
    {
        var l = new List<DockItemModel>();
        foreach (var n in new[] { "a", "b", "c" }) l.Add(new DockItemModel("app:" + n, DockItemKind.App, n, null, true, false, 0, false, false, AppIdentity.FromExe($@"C:\{n}.exe")) with { Id = "app:" + AppIdentity.FromExe($@"C:\{n}.exe").Id });
        l.Add(new DockItemModel("sep", DockItemKind.Separator, "", null)); l.Add(new DockItemModel("trash", DockItemKind.Trash, "T", null));
        var c = new DockController(new DockMetrics(), new DockConfig()); c.SetItems(l); for (int i = 0; i < 90; i++) c.Step(1.0 / 60); items = l; return c;
    }

    [Fact]
    public void Only_pinned_apps_can_be_dragged()
    {
        var c = Make(out _); Assert.True(c.CanDrag(0)); Assert.False(c.CanDrag(3)); Assert.False(c.CanDrag(4)); Assert.False(c.BeginDrag(3)); Assert.False(c.IsDragging);
    }

    [Fact]
    public void Dragging_past_a_neighbour_reorders_and_commit_returns_the_new_order()
    {
        var c = Make(out _); var v = c.Step(0); Assert.True(c.BeginDrag(0));
        c.UpdateDrag(v.Items[2].X + v.Items[2].Size + 4);   // pasada la tercera ancla
        var v2 = c.Step(1.0 / 60); Assert.Equal(0, 0); Assert.Equal(2, v2.DragIndex);
        var order = c.EndDrag(true); Assert.NotNull(order); Assert.Equal(["exe:C:\\B.EXE", "exe:C:\\C.EXE", "exe:C:\\A.EXE"], order);
        Assert.False(c.IsDragging);
    }

    [Fact]
    public void Cancel_restores_the_original_order_and_no_move_returns_null()
    {
        var c = Make(out var items); var v = c.Step(0); c.BeginDrag(0); c.UpdateDrag(v.Items[2].X + 200); Assert.Null(c.EndDrag(false));
        Assert.Equal(items.Select(i => i.Id), c.Items.Select(i => i.Id));
        c.BeginDrag(1); c.UpdateDrag(v.Items[1].X + 5); Assert.Null(c.EndDrag(true));
    }

    [Fact]
    public void The_dragged_item_follows_the_pointer_without_magnification_and_never_crosses_into_the_system_section()
    {
        var c = Make(out _); var v = c.Step(0); c.BeginDrag(1); c.SetPointer(new PointerSample(v.Items[1].X, c.WindowHeight - 40)); c.UpdateDrag(v.WindowWidth - 5);
        for (int i = 0; i < 30; i++) c.Step(1.0 / 60); var d = c.Step(1.0 / 60);
        Assert.All(d.Items.Where(i => i.Model.Kind == DockItemKind.App), i => Assert.Equal(1.0, i.Scale, 2));   // sin ampliación
        Assert.True(d.Items[d.DragIndex].Lift > 0); Assert.Equal(DockItemKind.Separator, c.Items[3].Kind); Assert.Equal(DockItemKind.Trash, c.Items[4].Kind);
        c.EndDrag(false);
    }

    [Fact]
    public void Model_refreshes_during_a_drag_are_deferred_until_it_ends()
    {
        var c = Make(out var items); c.BeginDrag(0); c.SetItems(items.Take(2).ToList()); Assert.Equal(items.Count, c.Items.Count);
        c.EndDrag(false); Assert.Equal(2, c.Items.Count);
    }

    [Fact]
    public void Controller_menu_and_click_carry_the_app_identity()
    {
        var c = Make(out _); Assert.Equal(AppKind.Win32, c.OnClick(0).App!.Kind);
        var m = c.MenuFor(0); Assert.Contains(m, e => e.Action?.Kind == DockActionKind.Unpin && e.Action.App is not null); Assert.Contains(m, e => e.Action?.Kind == DockActionKind.MoveRight && e.Enabled);
        Assert.Equal([DockActionKind.Quit], c.MenuFor(3).Where(e => e.Action is not null).Select(e => e.Action!.Kind));   // sobre el separador: menú del fondo
    }
}
