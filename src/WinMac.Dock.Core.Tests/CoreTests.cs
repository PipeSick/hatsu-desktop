using System.IO.Compression;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using Xunit;

public class SpringTests
{
    [Fact]
    public void Converges_to_target_and_settles()
    {
        var s = new Spring(0, 1);
        for (int i = 0; i < 600 && !s.IsSettled; i++) s.Step(1.0 / 60);
        Assert.True(s.IsSettled); Assert.Equal(1, s.Value);
    }

    [Fact]
    public void Is_stable_with_large_time_steps()
    {
        var s = new Spring(0, 1);
        for (int i = 0; i < 200; i++) { s.Step(0.5); Assert.True(double.IsFinite(s.Value)); Assert.InRange(s.Value, -1, 3); }
    }

    [Fact]
    public void Underdamped_spring_overshoots_slightly()
    {
        var s = new Spring(0, 1); double max = 0;
        for (int i = 0; i < 300; i++) { s.Step(1.0 / 120); max = Math.Max(max, s.Value); }
        Assert.InRange(max, 1.0005, 1.25);
    }
}

public class LayoutTests
{
    private static readonly DockMetrics M = new();
    private static IReadOnlyList<DockItemModel> Items(int apps, bool recents = false)
    {
        var l = new List<DockItemModel>();
        for (int i = 0; i < apps; i++) l.Add(new DockItemModel($"app:{i}", DockItemKind.App, $"A{i}", $"C:\\a{i}.exe", Pinned: true));
        if (recents) { l.Add(new DockItemModel("sep:r", DockItemKind.Separator, "", null)); l.Add(new DockItemModel("app:r", DockItemKind.App, "R", "C:\\r.exe")); }
        l.Add(new DockItemModel("sep:s", DockItemKind.Separator, "", null));
        l.Add(new DockItemModel("folder", DockItemKind.Folder, "F", "C:\\f")); l.Add(new DockItemModel("trash", DockItemKind.Trash, "T", null));
        return l;
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(8)] [InlineData(20)]
    public void Magnified_layout_never_overlaps(int apps)
    {
        var items = Items(apps, apps % 2 == 0); double rest = DockLayout.RestWidth(M, items);
        for (double p = -50; p <= rest + 50; p += 13)
        {
            var f = DockLayout.Compute(M, items, DockLayout.TargetScales(M, items, p));
            for (int i = 1; i < f.Items.Length; i++)
            {
                if (items[i].Kind == DockItemKind.Separator || items[i - 1].Kind == DockItemKind.Separator) continue;
                Assert.True(f.Items[i].X >= f.Items[i - 1].X + f.Items[i - 1].Size - 1e-9);
            }
        }
    }

    [Fact]
    public void Scales_are_bounded_and_peak_at_pointer()
    {
        var items = Items(10); var c = DockLayout.RestCenters(M, items);
        var s = DockLayout.TargetScales(M, items, c[4]);
        Assert.All(s, v => Assert.InRange(v, 1 - 1e-9, M.MaxScale + 1e-9));
        Assert.Equal(M.MaxScale, s[4], 6); Assert.Equal(4, Array.IndexOf(s, s.Max()));
    }

    [Fact]
    public void Window_is_always_wide_enough()
    {
        var items = Items(14, true); double w = DockLayout.WindowWidth(M, items), rest = DockLayout.RestWidth(M, items);
        for (double p = 0; p <= rest; p += 3)
            Assert.True(DockLayout.Compute(M, items, DockLayout.TargetScales(M, items, p)).BarWidth + 2 * M.ShadowMargin <= w);
    }

    [Fact]
    public void Rest_layout_without_cursor_is_unscaled()
    {
        var items = Items(5); var s = DockLayout.TargetScales(M, items, null);
        Assert.All(s, v => Assert.Equal(1.0, v));
    }

    [Fact]
    public void Hit_test_finds_item_and_ignores_separators_and_gaps_outside()
    {
        var items = Items(3); var f = DockLayout.Compute(M, items, null);
        Assert.Equal(1, DockLayout.HitTestX(f, items, f.Items[1].X + 5));
        Assert.Equal(-1, DockLayout.HitTestX(f, items, -100));
        int sep = items.ToList().FindIndex(i => i.Kind == DockItemKind.Separator);
        Assert.NotEqual(sep, DockLayout.HitTestX(f, items, f.Items[sep].X));
    }
}

public class ModelBuilderTests
{
    private static DockConfig Cfg() => new() { Pinned = [new PinnedApp(@"C:\Windows\notepad.exe"), new PinnedApp(@"C:\Windows\calc.exe")], MaxRecents = 2 };
    private static RunningApp App(string p, bool active = false, bool min = false) => new(p, 1, [new WindowInfo(1, "w", min, active)]);

    [Fact]
    public void Pinned_come_first_in_config_order_and_flag_running()
    {
        var b = new DockModelBuilder(Cfg());
        var items = b.Build([App(@"c:\windows\CALC.exe", active: true)]);
        Assert.Equal("Notepad", items[0].Title); Assert.False(items[0].Running);
        Assert.True(items[1].Running); Assert.True(items[1].Active);
    }

    [Fact]
    public void Unpinned_running_apps_become_recents_limited_and_stable()
    {
        var b = new DockModelBuilder(Cfg());
        var first = b.Build([App(@"C:\x\one.exe"), App(@"C:\x\two.exe"), App(@"C:\x\three.exe")]);
        var apps = first.Where(i => i.Kind == DockItemKind.App && !i.Pinned).Select(i => i.Title).ToList();
        Assert.Equal(["One", "Two"], apps);
        var second = b.Build([App(@"C:\x\two.exe"), App(@"C:\x\one.exe"), App(@"C:\x\three.exe")]);
        Assert.Equal(["One", "Two"], second.Where(i => i.Kind == DockItemKind.App && !i.Pinned).Select(i => i.Title).ToList()); // orden de primera aparición
    }

    [Fact]
    public void Always_ends_with_folder_and_trash_after_separator()
    {
        var items = new DockModelBuilder(Cfg()).Build([]);
        Assert.Equal(DockItemKind.Trash, items[^1].Kind); Assert.Equal(DockItemKind.Folder, items[^2].Kind); Assert.Equal(DockItemKind.Separator, items[^3].Kind);
    }

    [Fact]
    public void No_recents_separator_when_none()
    {
        var items = new DockModelBuilder(Cfg()).Build([]);
        Assert.Single(items, i => i.Kind == DockItemKind.Separator);
    }

    [Fact]
    public void Minimized_state_is_derived_from_all_windows()
    {
        var b = new DockModelBuilder(Cfg());
        var r = new RunningApp(@"C:\Windows\notepad.exe", 5, [new WindowInfo(1, "a", true, false), new WindowInfo(2, "b", false, false)]);
        Assert.False(b.Build([r])[0].AllMinimized);
        var r2 = new RunningApp(@"C:\Windows\notepad.exe", 5, [new WindowInfo(1, "a", true, false)]);
        Assert.True(b.Build([r2])[0].AllMinimized);
    }
}

public class ControllerTests
{
    private static DockController Make(DockConfig? cfg = null, int apps = 6)
    {
        var c = new DockController(new DockMetrics(), cfg ?? new DockConfig());
        var l = new List<DockItemModel>();
        for (int i = 0; i < apps; i++) l.Add(new DockItemModel($"app:{i}", DockItemKind.App, $"A{i}", $"C:\\a{i}.exe", true, i == 1, i == 1 ? 1 : 0, i == 1));
        l.Add(new DockItemModel("sep", DockItemKind.Separator, "", null)); l.Add(new DockItemModel("trash", DockItemKind.Trash, "T", "shell:RecycleBinFolder"));
        c.SetItems(l); return c;
    }

    private static void Run(DockController c, double seconds, Action<DockController>? each = null)
    { for (double t = 0; t < seconds; t += 1.0 / 60) { c.Step(1.0 / 60); each?.Invoke(c); } }

    [Fact]
    public void Idle_dock_reports_no_frames_needed_once_settled()
    {
        var c = Make(); Run(c, 3); Assert.False(c.Step(1.0 / 60).NeedsFrames);
    }

    [Fact]
    public void Hover_magnifies_and_leaving_relaxes_and_stops_frames()
    {
        var c = Make(); Run(c, 1);
        var v0 = c.Step(0); double y = c.WindowHeight - 40;
        var mid = v0.Items[2]; c.SetPointer(new PointerSample(mid.X + mid.Size / 2, y));
        Run(c, 1.5); var hot = c.Step(1.0 / 60);
        Assert.True(hot.NeedsFrames); Assert.True(hot.Items[2].Scale > 1.5); Assert.True(hot.HoverIndex >= 0);
        c.SetPointer(null); Run(c, 3); var cold = c.Step(1.0 / 60);
        Assert.False(cold.NeedsFrames); Assert.Equal(1.0, cold.Items[2].Scale, 3);
    }

    [Fact]
    public void Magnification_can_be_disabled()
    {
        var c = Make(new DockConfig { MagnificationEnabled = false }); Run(c, 1);
        c.SetPointer(new PointerSample(c.WindowWidth / 2, c.WindowHeight - 40)); Run(c, 1);
        Assert.All(c.Step(1.0 / 60).Items.Where(i => i.Model.Kind == DockItemKind.App), i => Assert.Equal(1.0, i.Scale, 3));
    }

    [Fact]
    public void Autohide_hides_after_delay_and_shows_on_edge()
    {
        var c = Make(new DockConfig { AutoHide = true, HideDelaySeconds = 0.5, ShowDelaySeconds = 0.1 }); Run(c, 0.2);
        Assert.True(c.Step(0).HideT < 0.1);
        Run(c, 2); Assert.True(c.Step(0).HideT > 0.95);
        c.SetEdgeHot(true); Run(c, 1.5); var shown = c.Step(1.0 / 60);
        Assert.True(shown.HideT < 0.05);
    }

    [Fact]
    public void Autohide_does_not_hide_while_pointer_is_over_dock()
    {
        var c = Make(new DockConfig { AutoHide = true, HideDelaySeconds = 0.3 }); Run(c, 0.1);
        c.SetPointer(new PointerSample(c.WindowWidth / 2, c.WindowHeight - 40)); Run(c, 3);
        Assert.True(c.Step(0).HideT < 0.05);
    }

    [Fact]
    public void Suppression_hides_even_without_autohide_and_blocks_reveal()
    {
        var c = Make(); Run(c, 0.2); c.SetSuppressed(true); c.SetEdgeHot(true); Run(c, 2);
        var v = c.Step(0); Assert.True(v.Suppressed); Assert.True(v.HideT > 0.95);
        c.SetSuppressed(false); Run(c, 2); Assert.True(c.Step(0).HideT < 0.05);
    }

    [Fact]
    public void Click_rules_follow_macos_style_and_option_for_minimize()
    {
        var c = Make();
        Assert.Equal(DockActionKind.Launch, c.OnClick(0).Kind);          // no corre -> lanzar
        Assert.Equal(DockActionKind.None, c.OnClick(1).Kind);            // corre y activa -> nada (estilo macOS)
        Assert.Equal(DockActionKind.Open, c.OnClick(c.Items.Count - 1).Kind);
        Assert.Equal(DockActionKind.None, c.OnClick(99).Kind);
        var m = Make(new DockConfig { ClickActiveMinimizes = true });
        Assert.Equal(DockActionKind.Minimize, m.OnClick(1).Kind);
    }

    [Fact]
    public void Click_restores_minimized_and_activates_background_apps()
    {
        var c = new DockController(new DockMetrics(), new DockConfig());
        c.SetItems([new("app:a", DockItemKind.App, "A", "C:\\a.exe", true, true, 1, false, true), new("app:b", DockItemKind.App, "B", "C:\\b.exe", true, true, 1, false, false)]);
        Assert.Equal(DockActionKind.Restore, c.OnClick(0).Kind); Assert.Equal(DockActionKind.Activate, c.OnClick(1).Kind);
    }

    [Fact]
    public void Launch_triggers_a_bounce_that_decays()
    {
        var c = Make(); Run(c, 1); c.OnClick(0);
        double peak = 0; Run(c, 1.5, k => peak = Math.Max(peak, k.Step(0).Items[0].Lift));
        Assert.InRange(peak, 3, c.Metrics.BounceHeight + 1e-9);
        Run(c, 4); Assert.Equal(0, c.Step(0).Items[0].Lift, 3);
    }

    [Fact]
    public void New_items_grow_in_instead_of_popping()
    {
        var c = Make(); Run(c, 1);
        var more = c.Items.ToList(); more.Insert(0, new DockItemModel("app:new", DockItemKind.App, "N", "C:\\n.exe"));
        c.SetItems(more); var v = c.Step(1.0 / 60);
        Assert.True(v.Items[0].Scale < 0.2); Run(c, 2); Assert.Equal(1.0, c.Step(0).Items[0].Scale, 3);
    }
}

public class ConfigTests
{
    [Fact]
    public void Roundtrip_preserves_values()
    {
        var c = new DockConfig { AutoHide = true, MaxRecents = 5, GlassTier = GlassTier.Tint, Pinned = [new PinnedApp(@"C:\a.exe", "A")] };
        var d = DockConfig.FromJson(c.ToJson());
        Assert.True(d.AutoHide); Assert.Equal(5, d.MaxRecents); Assert.Equal(GlassTier.Tint, d.GlassTier); Assert.Equal("A", d.Pinned[0].Title);
    }

    [Fact]
    public void Corrupt_or_missing_file_yields_fallback_and_save_is_atomic()
    {
        string dir = Path.Combine(Path.GetTempPath(), "winmac-" + Guid.NewGuid().ToString("N")); string f = Path.Combine(dir, "dock.json");
        try
        {
            Assert.Equal(7, DockConfig.LoadOrDefault(f, () => new DockConfig { MaxRecents = 7 }).MaxRecents);
            new DockConfig { MaxRecents = 2 }.Save(f); Assert.Equal(2, DockConfig.LoadOrDefault(f, () => new()).MaxRecents); Assert.False(File.Exists(f + ".tmp"));
            File.WriteAllText(f, "{ esto no es json"); Assert.Equal(9, DockConfig.LoadOrDefault(f, () => new DockConfig { MaxRecents = 9 }).MaxRecents);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

public class ImagingTests
{
    [Fact]
    public void Png_has_valid_signature_chunks_and_decodable_pixels()
    {
        int w = 3, h = 2; var rgba = new byte[w * h * 4]; for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 7);
        var png = PngWriter.EncodeRgba(rgba, w, h);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        int pos = 8; var idat = new MemoryStream(); string last = "";
        while (pos < png.Length)
        {
            int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3]; string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (type == "IDAT") idat.Write(png, pos + 8, len); last = type; pos += 12 + len;
        }
        Assert.Equal("IEND", last); Assert.Equal(png.Length, pos);
        idat.Position = 0; using var z = new ZLibStream(idat, CompressionMode.Decompress); var raw = new MemoryStream(); z.CopyTo(raw);
        Assert.Equal(h * (w * 4 + 1), raw.Length);
        Assert.Equal(rgba[..(w * 4)], raw.ToArray()[1..(w * 4 + 1)]);
    }

    [Fact]
    public void Premultiplied_bgra_is_unpremultiplied_to_rgba()
    {
        var o = PngWriter.BgraPremulToRgba([50, 100, 150, 200, 0, 0, 0, 0], 8, 2, 1);
        Assert.Equal((byte)200, o[3]); Assert.Equal((byte)Math.Min(255, 150 * 255 / 200), o[0]); Assert.Equal((byte)0, o[7]);
    }

    [Theory]
    [InlineData(WallpaperStyle.Fill)] [InlineData(WallpaperStyle.Fit)] [InlineData(WallpaperStyle.Stretch)] [InlineData(WallpaperStyle.Center)]
    public void Wallpaper_source_rect_has_positive_area_and_scales_with_region(WallpaperStyle s)
    {
        var r = WallpaperMapping.SourceRectFor(s, 3840, 2160, 1920, 1080, new RectD(500, 900, 800, 150));
        Assert.True(r.W > 0 && r.H > 0);
        var r2 = WallpaperMapping.SourceRectFor(s, 3840, 2160, 1920, 1080, new RectD(500, 900, 400, 75));
        Assert.Equal(r.W / 2, r2.W, 6);
    }

    [Fact]
    public void Fill_maps_a_full_monitor_region_into_the_image_without_exceeding_it()
    {
        var r = WallpaperMapping.SourceRectFor(WallpaperStyle.Fill, 4000, 2000, 1920, 1080, new RectD(0, 0, 1920, 1080));
        Assert.InRange(r.X, -1e-6, 4000); Assert.True(r.X + r.W <= 4000 + 1e-6); Assert.True(r.Y >= -1e-6 && r.Y + r.H <= 2000 + 1e-6);
    }

    [Theory]
    [InlineData("10", "0", WallpaperStyle.Fill)] [InlineData("6", "0", WallpaperStyle.Fit)] [InlineData("2", "0", WallpaperStyle.Stretch)]
    [InlineData("0", "1", WallpaperStyle.Tile)] [InlineData(null, null, WallpaperStyle.Fill)]
    public void Registry_values_map_to_style(string? ws, string? tile, WallpaperStyle expected) => Assert.Equal(expected, WallpaperMapping.FromRegistry(ws, tile));
}

public class DemoAndMetricsTests
{
    [Fact]
    public void Window_height_reserves_room_for_magnification_bounce_and_label()
    {
        var m = new DockMetrics();
        Assert.True(m.WindowHeight >= m.BarHeight + m.BaseSize * (m.MaxScale - 1) + m.BounceHeight + m.LabelHeight + m.LabelGap + 2 * m.ShadowMargin - 1e-9);
    }

    [Fact]
    public void Demo_items_are_valid_and_drive_the_controller_without_error()
    {
        var items = DemoData.Items();
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        var c = new DockController(new DockMetrics(), new DockConfig()); c.SetItems(items);
        for (int i = 0; i < 120; i++) c.Step(1.0 / 60);
        var v = c.Step(0);
        Assert.Equal(items.Count, v.Items.Length); Assert.True(v.WindowWidth > v.BarWidth);
        Assert.Contains(items, i => i.Running && i.Kind == DockItemKind.App); Assert.Contains(items, i => i.Kind == DockItemKind.Trash);
    }

    [Fact]
    public void Bar_always_fits_inside_window_for_every_pointer_position()
    {
        var items = DemoData.Items(); var c = new DockController(new DockMetrics(), new DockConfig()); c.SetItems(items);
        for (double x = 0; x <= c.WindowWidth; x += 25)
        {
            c.SetPointer(new PointerSample(x, c.WindowHeight - 40));
            for (int i = 0; i < 90; i++) c.Step(1.0 / 60);
            var v = c.Step(0);
            Assert.True(v.BarLeft >= c.Metrics.ShadowMargin - 1e-6, $"x={x}"); Assert.True(v.BarLeft + v.BarWidth <= v.WindowWidth - c.Metrics.ShadowMargin + 1e-6, $"x={x}");
        }
    }
}

public class ThemeTests
{
    [Fact]
    public void Dark_preset_is_darker_denser_and_has_its_own_marks()
    {
        var l = GlassStyle.For(DockTheme.Light); var d = GlassStyle.For(DockTheme.Dark);
        Assert.Equal(DockTheme.Light, l.Theme); Assert.Equal(DockTheme.Dark, d.Theme);
        Assert.True(d.TintR + d.TintG + d.TintB < l.TintR + l.TintG + l.TintB);
        Assert.True(d.TintAlpha > l.TintAlpha); Assert.True(d.ShadowAlpha > l.ShadowAlpha); Assert.True(d.RimTopAlpha < l.RimTopAlpha);
    }

    [Fact]
    public void Config_theme_roundtrips_and_defaults_to_system()
    {
        Assert.Equal(ThemeMode.System, new DockConfig().Theme);
        Assert.Equal(ThemeMode.Dark, DockConfig.FromJson(new DockConfig { Theme = ThemeMode.Dark }.ToJson()).Theme);
    }

    [Fact]
    public void AllOpen_scene_has_every_pinned_app_running_and_a_minimized_one()
    {
        var items = DemoData.AllOpen();
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        var apps = items.Where(i => i.Kind == DockItemKind.App).ToList();
        Assert.All(apps, a => Assert.True(a.Running)); Assert.Contains(apps, a => a.AllMinimized); Assert.Contains(apps, a => a.WindowCount > 1); Assert.Single(apps, a => a.Active);
        var c = new DockController(new DockMetrics(), new DockConfig()); c.SetItems(items);
        for (int i = 0; i < 120; i++) c.Step(1.0 / 60);
        Assert.True(c.Step(0).BarWidth + 2 * c.Metrics.ShadowMargin <= c.WindowWidth);
    }
}
