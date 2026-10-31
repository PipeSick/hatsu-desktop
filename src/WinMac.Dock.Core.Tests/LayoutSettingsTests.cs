using WinMac.Dock;
using Xunit;

public class IconSizeTests
{
    [Fact]
    public void The_default_size_is_exactly_the_approved_metrics()
    {
        Assert.Equal(new DockMetrics(), DockMetrics.ForIconSize(60)); Assert.Equal(60, new DockConfig().IconSize); Assert.Equal(DockAlignment.Center, new DockConfig().Alignment); Assert.Equal(0, new DockConfig().BottomOffset);
    }

    [Theory]
    [InlineData(32)] [InlineData(48)] [InlineData(80)] [InlineData(96)] [InlineData(128)]
    public void Every_proportion_scales_with_the_icon_so_the_dock_looks_the_same_at_any_size(double size)
    {
        var d = new DockMetrics(); var m = DockMetrics.ForIconSize(size); double k = size / 60;
        Assert.Equal(size, m.BaseSize);
        foreach (var (a, b) in new[] { (m.BarHeight, d.BarHeight), (m.Gap, d.Gap), (m.PadX, d.PadX), (m.PadBottom, d.PadBottom), (m.BarRadius, d.BarRadius), (m.SeparatorLine, Math.Max(1, d.BaseSize * d.SeparatorLineRatio * k)),
                                      (m.DotSize, d.DotSize), (m.SeparatorHeight, d.SeparatorHeight), (m.Radius, d.Radius), (m.BounceHeight, d.BounceHeight) })
            if (a != m.SeparatorLine) Assert.Equal(b * k, a, 6);
        Assert.Equal(d.BarHeight / d.BaseSize, m.BarHeight / m.BaseSize, 9);
    }

    [Fact]
    public void Sizes_are_clamped_and_rounded_and_garbage_falls_back_to_the_default()
    {
        Assert.Equal(32, DockSettings.ClampIconSize(5)); Assert.Equal(128, DockSettings.ClampIconSize(900)); Assert.Equal(73, DockSettings.ClampIconSize(72.6)); Assert.Equal(60, DockSettings.ClampIconSize(double.NaN));
        Assert.Equal(32, DockMetrics.ForIconSize(1).BaseSize);
        var c = new DockConfig(); Assert.Same(c, DockSettings.WithIconSize(c, 60)); Assert.Equal(96, DockSettings.WithIconSize(c, 96).IconSize); Assert.Equal(128, DockSettings.WithIconSize(c, 1000).IconSize);
    }

    [Fact]
    public void Window_width_at_any_size_still_holds_the_bar_symmetrically()
    {
        foreach (double s in new[] { 40.0, 60, 90 })
        {
            var m = DockMetrics.ForIconSize(s) with { PreviewMaxWidth = 0 }; var items = DemoData.Items();
            Assert.Equal(DockLayout.MaxBarWidth(m, items) + 2 * m.BarInset, DockLayout.WindowWidth(m, items), 6);
        }
    }
}

public class SettingsOperationsTests
{
    [Fact]
    public void Height_nudges_within_limits_and_returns_the_same_config_when_nothing_changes()
    {
        var c = new DockConfig(); Assert.Same(c, DockSettings.NudgeBottom(c, -8)); Assert.Same(c, DockSettings.ResetBottom(c));
        var up = DockSettings.NudgeBottom(c, 8); Assert.Equal(8, up.BottomOffset); Assert.Equal(16, DockSettings.NudgeBottom(up, 8).BottomOffset);
        Assert.Equal(DockSettings.MaxBottomOffset, DockSettings.NudgeBottom(new DockConfig { BottomOffset = 396 }, 8).BottomOffset); Assert.Equal(0, DockSettings.ResetBottom(up).BottomOffset);
    }

    [Fact]
    public void Alignment_changes_only_when_different()
    {
        var c = new DockConfig(); Assert.Same(c, DockSettings.WithAlignment(c, DockAlignment.Center)); Assert.Equal(DockAlignment.Right, DockSettings.WithAlignment(c, DockAlignment.Right).Alignment);
    }

    [Fact]
    public void Hand_edited_nonsense_is_sanitized()
    {
        var c = DockSettings.Sanitize(new DockConfig { IconSize = -5, BottomOffset = 99999, EdgeMargin = double.NaN }); Assert.Equal(32, c.IconSize); Assert.Equal(400, c.BottomOffset); Assert.Equal(16, c.EdgeMargin);
    }

    [Fact]
    public void Config_roundtrips_and_old_files_without_the_new_fields_get_the_defaults()
    {
        var c = new DockConfig { IconSize = 84, Alignment = DockAlignment.Right, EdgeMargin = 24, BottomOffset = 40 }; var back = DockConfig.FromJson(c.ToJson());
        Assert.Equal(84, back.IconSize); Assert.Equal(DockAlignment.Right, back.Alignment); Assert.Equal(24, back.EdgeMargin); Assert.Equal(40, back.BottomOffset); Assert.Contains("\"Right\"", c.ToJson());
        var old = DockConfig.FromJson("{\"Version\":3,\"AutoHide\":true}"); Assert.Equal(60, old.IconSize); Assert.Equal(DockAlignment.Center, old.Alignment); Assert.Equal(0, old.BottomOffset);
    }
}

public class PlacementTests
{
    private static readonly PxRect Monitor = new(0, 0, 1920, 1080), Work = new(0, 0, 1920, 1040);   // barra de tareas de 40 px abajo
    private static (int, int) Old(PxRect mon, PxRect work, int w, int h, double scale) => (mon.Left + (mon.Width - w) / 2, work.Bottom - h + (int)(4 * scale));

    [Fact]
    public void Center_with_no_offset_is_exactly_the_position_the_dock_always_had()
    {
        var m = DockMetrics.ForIconSize(60);
        foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Assert.Equal(Old(Monitor, Work, 1330, 300, scale), DockSettings.Place(Monitor, Work, 1330, 300, scale, m, new DockConfig()));
    }

    [Fact]
    public void Left_and_right_align_the_BAR_to_the_edge_margin_not_the_wider_window()
    {
        var m = DockMetrics.ForIconSize(60); int winW = 1330; double inset = m.BarInset;
        var (l, _) = DockSettings.Place(Monitor, Work, winW, 300, 1.0, m, new DockConfig { Alignment = DockAlignment.Left, EdgeMargin = 16 });
        Assert.Equal(16, l + (int)inset);                                  // el borde izquierdo de la barra queda a 16 px del borde
        var (r, _) = DockSettings.Place(Monitor, Work, winW, 300, 1.0, m, new DockConfig { Alignment = DockAlignment.Right, EdgeMargin = 16 });
        Assert.Equal(1920 - 16, r + winW - (int)inset);                    // el borde derecho de la barra queda a 16 px del borde
    }

    [Fact]
    public void Scale_applies_to_margins_and_the_offset_raises_the_dock_but_never_off_screen()
    {
        var m = DockMetrics.ForIconSize(60); var (_, t0) = DockSettings.Place(Monitor, Work, 1330, 300, 1.5, m, new DockConfig());
        var (_, t1) = DockSettings.Place(Monitor, Work, 1330, 300, 1.5, m, new DockConfig { BottomOffset = 40 }); Assert.Equal(60, t0 - t1);   // 40 DIP × 1,5
        var (_, t2) = DockSettings.Place(Monitor, Work, 1330, 2000, 1.0, m, new DockConfig { BottomOffset = 400 }); Assert.Equal(0, t2);          // ventana más alta que la pantalla: nunca por encima del borde superior
    }

    [Fact]
    public void A_second_monitor_with_an_offset_origin_is_respected()
    {
        var mon = new PxRect(1920, 0, 3840, 1080); var work = new PxRect(1920, 0, 3840, 1040); var m = DockMetrics.ForIconSize(60);
        var (l, _) = DockSettings.Place(mon, work, 1330, 300, 1.0, m, new DockConfig { Alignment = DockAlignment.Left }); Assert.Equal(1920 + 16 - (int)m.BarInset, l);
    }
}

public class AlignmentInTheControllerTests
{
    private static DockController Make(DockConfig cfg, DockMetrics? m = null)
    {
        m ??= new DockMetrics(); var c = new DockController(m, cfg); c.SetItems(DemoData.Items()); for (int i = 0; i < 60; i++) c.Step(1.0 / 60); return c;
    }

    [Fact]
    public void Center_is_unchanged_left_pins_the_bar_to_the_left_and_right_to_the_right()
    {
        var m = new DockMetrics(); var center = Make(new DockConfig(), m).Step(0);
        Assert.Equal((center.WindowWidth - center.BarWidth) / 2, center.BarLeft, 6);
        var left = Make(new DockConfig { Alignment = DockAlignment.Left }, m).Step(0); Assert.Equal(m.BarInset, left.BarLeft, 6); Assert.Equal(center.BarWidth, left.BarWidth, 6);
        var right = Make(new DockConfig { Alignment = DockAlignment.Right }, m).Step(0); Assert.Equal(right.WindowWidth - m.BarInset, right.BarLeft + right.BarWidth, 6);
    }

    [Fact]
    public void Changing_the_alignment_live_moves_the_bar_without_a_new_controller()
    {
        var c = Make(new DockConfig()); var before = c.Step(0).BarLeft; c.Config = new DockConfig { Alignment = DockAlignment.Right }; var after = c.Step(0);
        Assert.True(after.BarLeft > before); Assert.Equal(after.WindowWidth - new DockMetrics().BarInset, after.BarLeft + after.BarWidth, 6);
    }

    [Fact]
    public void Hover_and_magnification_still_find_the_icon_under_the_pointer_when_aligned_to_a_side()
    {
        foreach (var a in new[] { DockAlignment.Left, DockAlignment.Right })
        {
            var m = new DockMetrics(); var c = Make(new DockConfig { Alignment = a }, m); var v = c.Step(0); var it = v.Items[3];
            c.SetPointer(new PointerSample(it.X + it.Size / 2, v.WindowHeight - m.ShadowMargin - 30)); for (int i = 0; i < 90; i++) c.Step(1.0 / 60);
            var h = c.Step(0); Assert.Equal(3, h.HoverIndex); Assert.True(h.Items[3].Scale > 1.3, $"sin magnificación con alineación {a}");
        }
    }

    [Fact]
    public void Dragging_works_when_aligned_to_a_side()
    {
        var items = new List<DockItemModel> { Pin("A"), Pin("B"), Pin("C"), new("sep:system", DockItemKind.Separator, "", null), new("trash", DockItemKind.Trash, "", null) };
        var c = new DockController(new DockMetrics(), new DockConfig { Alignment = DockAlignment.Right }); c.SetItems(items); for (int i = 0; i < 60; i++) c.Step(1.0 / 60);
        var v = c.Step(0); Assert.True(c.BeginDrag(0)); c.UpdateDrag(v.Items[2].X + v.Items[2].Size + 4); var order = c.EndDrag(true);
        Assert.Equal(["exe:C:\\B.EXE", "exe:C:\\C.EXE", "exe:C:\\A.EXE"], order);
        static DockItemModel Pin(string n) => new("app:" + n, DockItemKind.App, n, null, true, false, 0, false, false, WinMac.Apps.AppIdentity.FromExe($@"C:\{n}.exe"));
    }

    [Theory]
    [InlineData(DockAlignment.Left)] [InlineData(DockAlignment.Right)]
    public void The_preview_panel_stays_inside_the_screen_when_the_dock_hugs_a_side(DockAlignment a)
    {
        var m0 = new DockMetrics(); var m = m0 with { PreviewHeadroom = PreviewSpec.Headroom(m0), PreviewMaxWidth = PreviewSpec.MaxPanelWidth }; var cfg = new DockConfig { Alignment = a, EdgeMargin = 16 };
        var items = DemoData.AllOpenWithWindows(); var c = new DockController(m, cfg); c.SetItems(items); for (int i = 0; i < 90; i++) c.Step(1.0 / 60);
        int idx = a == DockAlignment.Left ? 2 : items.ToList().FindLastIndex(i => i.Kind == DockItemKind.App && i.WindowCount > 0);
        var v = c.Step(0); var it = v.Items[idx]; c.SetPointer(new PointerSample(it.X + it.Size / 2, v.WindowHeight - m.ShadowMargin - 30));
        for (int i = 0; i < 120; i++) c.Step(1.0 / 60);
        var pv = c.Step(0).Preview; Assert.NotNull(pv);
        double screenEdge = m.BarInset - cfg.EdgeMargin, w = v.WindowWidth;
        if (a == DockAlignment.Left) Assert.True(pv!.Frame.Panel.X >= screenEdge + PreviewSpec.Margin - 1e-6, $"el panel se sale por la izquierda: {pv.Frame.Panel.X}");
        else Assert.True(pv!.Frame.Panel.Right <= w - screenEdge - PreviewSpec.Margin + 1e-6, $"el panel se sale por la derecha: {pv.Frame.Panel.Right}");
    }
}

public class SettingsMenuTests
{
    private static IEnumerable<MenuEntry> Flat(IEnumerable<MenuEntry> l) => l.SelectMany(e => new[] { e }.Concat(e.Children is null ? [] : Flat(e.Children)));

    [Fact]
    public void The_background_menu_offers_settings_and_quit_and_marks_the_current_values()
    {
        var m = DockMenu.ForBackground(new DockConfig { IconSize = 80, Alignment = DockAlignment.Left, BottomOffset = 16 }); var all = Flat(m).ToList();
        Assert.Contains(all, e => e.Text == "Ajustes del Dock"); Assert.Equal(DockActionKind.Quit, m[^1].Action!.Kind);
        Assert.True(all.Single(e => e.Text.StartsWith("Grande")).Checked); Assert.False(all.Single(e => e.Text.StartsWith("Mediano")).Checked);
        Assert.True(all.Single(e => e.Text == "Izquierda").Checked); Assert.False(all.Single(e => e.Text == "Centro").Checked);
        Assert.Equal(DockActionKind.SetIconSize, all.Single(e => e.Text.StartsWith("Muy grande")).Action!.Kind); Assert.Equal(96, all.Single(e => e.Text.StartsWith("Muy grande")).Action!.Number);
        Assert.Equal((double)DockAlignment.Right, all.Single(e => e.Text == "Derecha").Action!.Number);
    }

    [Fact]
    public void A_custom_size_is_shown_as_checked_custom_and_height_entries_respect_the_limits()
    {
        var all = Flat(DockMenu.ForBackground(new DockConfig { IconSize = 72 })).ToList();
        Assert.True(all.Single(e => e.Text.StartsWith("Personalizado")).Checked); Assert.DoesNotContain(all, e => e.Checked && e.Text.StartsWith("Mediano"));
        Assert.False(all.Single(e => e.Text.StartsWith("Bajar")).Enabled); Assert.False(all.Single(e => e.Text == "Altura normal").Enabled); Assert.True(all.Single(e => e.Text.StartsWith("Subir")).Enabled);
        var top = Flat(DockMenu.ForBackground(new DockConfig { BottomOffset = 400 })).ToList(); Assert.False(top.Single(e => e.Text.StartsWith("Subir")).Enabled); Assert.True(top.Single(e => e.Text.StartsWith("Bajar")).Enabled);
    }

    [Fact]
    public void Item_menus_also_carry_the_settings_and_every_actionable_entry_is_a_leaf()
    {
        var c = new DockController(new DockMetrics(), new DockConfig { Alignment = DockAlignment.Right }); c.SetItems(DemoData.Items());
        var menu = c.MenuFor(0); Assert.Contains(Flat(menu), e => e.Text == "Derecha" && e.Checked); Assert.Contains(menu, e => e.Text == "Abrir");
        Assert.All(Flat(menu).Where(e => e.Children is not null), e => Assert.Null(e.Action));   // los submenús no ejecutan nada por sí mismos
    }
}
