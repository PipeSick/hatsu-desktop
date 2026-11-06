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
        Assert.True(GestureRules.IsAvailable("spotlight", off)); Assert.True(GestureRules.IsAvailable("dock", off)); Assert.False(GestureRules.IsAvailable("none", on)); Assert.False(GestureRules.IsAvailable("inventada", on));
    }

    [Fact] public void Corner_assignments_read_the_right_config_field()
    {
        var c = new BarConfig { GestCornerTL = "mission", GestCornerBR = "spotlight" };
        Assert.Equal("mission", GestureRules.ForCorner(c, HotCorner.TopLeft)); Assert.Equal("spotlight", GestureRules.ForCorner(c, HotCorner.BottomRight)); Assert.Equal("none", GestureRules.ForCorner(c, HotCorner.TopRight));
    }
}
