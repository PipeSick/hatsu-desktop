using WinMac.Bar.Features;
using Xunit;
using Mon = WinMac.Bar.Features.HotCornerEngine.Mon;

namespace WinMac.Bar.Core.Tests;

public class GesturesTests
{
    private static readonly Mon[] One = [new(0, 0, 1920, 1080)];
    private static readonly Mon[] Two = [new(0, 0, 1920, 1080), new(1920, 0, 1920, 1080)];

    [Fact] public void A_corner_fires_once_after_the_delay_and_not_before()
    {
        var e = new HotCornerEngine(); Func<HotCorner, bool> all = _ => true;
        Assert.Null(e.Update(0, 0, false, One, 1000, 400, all, out int wait)); Assert.Equal(400, wait);
        Assert.Null(e.Update(1, 1, false, One, 1200, 400, all, out wait)); Assert.Equal(200, wait);
        Assert.Equal(HotCorner.TopLeft, e.Update(0, 0, false, One, 1400, 400, all, out _));
        Assert.Null(e.Update(0, 0, false, One, 2400, 400, all, out _));                       // una sola vez mientras siga ahí
    }

    [Fact] public void Moving_away_rearms_but_jittering_next_to_the_corner_does_not()
    {
        var e = new HotCornerEngine(); Func<HotCorner, bool> all = _ => true;
        e.Update(1919, 1079, false, One, 0, 100, all, out _); Assert.Equal(HotCorner.BottomRight, e.Update(1919, 1079, false, One, 150, 100, all, out _));
        e.Update(1900, 1060, false, One, 200, 100, all, out _); Assert.Null(e.Update(1919, 1079, false, One, 400, 100, all, out _));      // cerca: no se rearma
        e.Update(900, 500, false, One, 500, 100, all, out _);                                                                              // lejos: sí
        e.Update(1919, 1079, false, One, 600, 100, all, out _); Assert.Equal(HotCorner.BottomRight, e.Update(1919, 1079, false, One, 720, 100, all, out _));
    }

    [Fact] public void Dragging_with_a_button_down_never_fires_so_snapping_a_window_into_a_corner_is_safe()
    {
        var e = new HotCornerEngine(); e.Update(0, 0, true, One, 0, 100, _ => true, out _); Assert.Null(e.Update(0, 0, true, One, 5000, 100, _ => true, out _));
    }

    [Fact] public void Only_corners_with_an_assigned_action_arm()
    {
        var e = new HotCornerEngine(); Assert.Null(e.Update(0, 0, false, One, 0, 100, c => c == HotCorner.TopRight, out int w)); Assert.Equal(-1, w);
        e.Update(1919, 0, false, One, 0, 100, c => c == HotCorner.TopRight, out w); Assert.Equal(100, w);
    }

    [Fact] public void Corners_between_two_monitors_are_ignored_but_the_outer_corners_work()
    {
        Assert.Null(HotCornerEngine.CornerAt(1919, 0, Two)); Assert.Null(HotCornerEngine.CornerAt(1920, 0, Two)); Assert.Null(HotCornerEngine.CornerAt(1919, 1079, Two));
        Assert.Equal((HotCorner.TopLeft, 0), HotCornerEngine.CornerAt(0, 0, Two)); Assert.Equal((HotCorner.TopRight, 1), HotCornerEngine.CornerAt(3839, 0, Two)); Assert.Equal((HotCorner.BottomLeft, 0), HotCornerEngine.CornerAt(0, 1079, Two));
    }

    [Fact] public void Monitors_with_different_sizes_expose_the_corners_that_are_really_free()
    {
        Mon[] m = [new(0, 0, 1920, 1080), new(1920, 0, 1280, 1024)];                    // el segundo es más bajo: su esquina inferior izquierda toca al primero
        Assert.Null(HotCornerEngine.CornerAt(1920, 1023, m)); Assert.Equal((HotCorner.BottomRight, 1), HotCornerEngine.CornerAt(3199, 1023, m));
        Assert.Equal((HotCorner.BottomRight, 0), HotCornerEngine.CornerAt(1919, 1079, m) is { } c && c.Monitor == 0 ? (HotCorner.BottomRight, 0) : (HotCorner.TopLeft, -1));
    }

    [Fact] public void Actions_of_a_switched_off_module_are_not_available_to_gestures()
    {
        var off = new BarConfig(); var on = new BarConfig { McEnabled = true, SnapEnabled = true, LiveTextEnabled = true };
        Assert.False(GestureRules.IsAvailable("mission", off)); Assert.True(GestureRules.IsAvailable("mission", on)); Assert.False(GestureRules.IsAvailable("snapnext", off)); Assert.False(GestureRules.IsAvailable("livetext", off));
        Assert.True(GestureRules.IsAvailable("spotlight", off)); Assert.False(GestureRules.IsAvailable("dock", off));   // ocultar el Dock ya no es una acción: una asignación antigua queda en «Nada»
        Assert.Equal("none", FeatureConfig.NormalizeAction("dock")); Assert.False(GestureRules.IsAvailable("none", on)); Assert.False(GestureRules.IsAvailable("inventada", on));
    }

    [Fact] public void Corner_assignments_read_the_right_config_field()
    {
        var c = new BarConfig { GestCornerTL = "mission", GestCornerBR = "spotlight" };
        Assert.Equal("mission", GestureRules.ForCorner(c, HotCorner.TopLeft)); Assert.Equal("spotlight", GestureRules.ForCorner(c, HotCorner.BottomRight)); Assert.Equal("none", GestureRules.ForCorner(c, HotCorner.TopRight));
    }
}

public class LauncherTests
{
    private static readonly GestLauncher Spot = new("l1", "Spotify", GestLauncher.App, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");

    [Fact]
    public void Urls_are_normalized_and_only_http_https_are_accepted()
    {
        Assert.Equal("https://youtube.com/", Launchers.NormalizeUrl("youtube.com")); Assert.Equal("http://localhost.local/x", Launchers.NormalizeUrl("http://localhost.local/x"));
        Assert.Null(Launchers.NormalizeUrl("")); Assert.Null(Launchers.NormalizeUrl("dos palabras.com")); Assert.Null(Launchers.NormalizeUrl("localhost"));
        Assert.Null(Launchers.NormalizeUrl("file:///C:/Windows/notepad.exe")); Assert.Null(Launchers.NormalizeUrl("javascript:alert(1)"));
        Assert.False(Launchers.IsSafeUrl("ms-settings:", out _)); Assert.False(Launchers.IsSafeUrl("ftp://x.com", out _));
    }

    [Fact]
    public void Add_remove_roundtrip_respects_the_limit_and_duplicates()
    {
        IReadOnlyList<GestLauncher> list = [];
        var (l1, a1) = Launchers.Add(list, "Spotify", GestLauncher.App, Spot.Target)!.Value; Assert.Equal("launch:l1", a1.ActionId);
        Assert.Null(Launchers.Add(l1, "Otra vez", GestLauncher.App, Spot.Target.ToLowerInvariant()));                       // mismo destino: no se repite
        Assert.Null(Launchers.Add(l1, "Mala", GestLauncher.Url, "file:///x"));                                                  // dirección no válida
        var cur = l1; for (int i = 0; i < Launchers.Max - 1; i++) cur = Launchers.Add(cur, "App" + i, GestLauncher.App, "id" + i)!.Value.List;
        Assert.Equal(Launchers.Max, cur.Count); Assert.Null(Launchers.Add(cur, "Extra", GestLauncher.App, "extra"));
        var back = Launchers.Parse(Launchers.Serialize(cur)); Assert.Equal(cur.Select(x => x.Id), back.Select(x => x.Id));
        Assert.DoesNotContain(Launchers.Remove(cur, "l1"), x => x.Id == "l1");
    }

    [Fact]
    public void Parse_drops_garbage_and_dangerous_entries()
    {
        Assert.Empty(Launchers.Parse("no es json")); Assert.Empty(Launchers.Parse("")); Assert.Empty(Launchers.Parse("{}"));
        var l = Launchers.Parse("[{\"id\":\"l1\",\"name\":\"ok\",\"kind\":\"url\",\"target\":\"https://a.com\"},{\"id\":\"L2\",\"name\":\"mayus\",\"kind\":\"app\",\"target\":\"x\"},{\"id\":\"l3\",\"name\":\"cmd\",\"kind\":\"app\",\"target\":\"a\\\"b\"},{\"id\":\"l4\",\"name\":\"mala\",\"kind\":\"url\",\"target\":\"file:///c\"}]");
        Assert.Equal(["l1"], l.Select(x => x.Id));
    }

    [Fact]
    public void A_launcher_action_is_available_only_while_it_exists_and_is_normalized()
    {
        var cfg = new BarConfig { GestLaunchers = Launchers.Serialize([Spot]) };
        Assert.True(GestureRules.IsAvailable("launch:l1", cfg)); Assert.False(GestureRules.IsAvailable("launch:l2", cfg)); Assert.False(GestureRules.IsAvailable("launch:l1", new BarConfig()));
        Assert.Equal("launch:l1", FeatureConfig.NormalizeAction("launch:l1")); Assert.Equal("none", FeatureConfig.NormalizeAction("launch:")); Assert.Equal("none", FeatureConfig.NormalizeAction("launch:../x"));
        Assert.Equal("Abrir: Spotify", Launchers.DisplayName(Spot)); Assert.Equal("youtube.com", Launchers.SiteName("https://www.youtube.com/watch"));
    }
}

