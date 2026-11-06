using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class SnapTests
{
    private static readonly CaptureRect Work = new(0, 0, 1920, 1040);

    [Fact] public void Presets_cover_the_work_area_without_overlap_and_stay_inside_it()
    {
        foreach (var l in SnapLayouts.Presets)
        {
            var r = SnapMath.Rects(l, Work, 0); long area = 0;
            foreach (var z in r) { Assert.True(z.X >= 0 && z.Right <= 1920 && z.Y >= 0 && z.Bottom <= 1040, l.Name); area += (long)z.W * z.H; }
            Assert.InRange(area, 1920L * 1040 - 4000, 1920L * 1040); for (int i = 0; i < r.Length; i++) for (int j = i + 1; j < r.Length; j++) Assert.False(r[i].X < r[j].Right && r[j].X < r[i].Right && r[i].Y < r[j].Bottom && r[j].Y < r[i].Bottom, l.Name);
        }
    }

    [Fact] public void Gap_splits_between_neighbours_but_screen_edges_keep_their_space()
    {
        var r = SnapMath.Rects(SnapLayouts.Presets[0], Work, 16);
        Assert.Equal(0, r[0].X); Assert.Equal(960 - 8, r[0].Right); Assert.Equal(960 + 8, r[1].X); Assert.Equal(1920, r[1].Right);
    }

    [Fact] public void Work_areas_that_do_not_start_at_zero_and_negative_monitors_are_respected()
    {
        var r = SnapMath.Rects(SnapLayouts.Presets[0], new CaptureRect(-1920, 0, 1920, 1040), 0); Assert.Equal(-1920, r[0].X); Assert.Equal(0, r[1].Right);
        Assert.Equal(0, SnapMath.ZoneAt(r, -1000, 500)); Assert.Equal(1, SnapMath.ZoneAt(r, -10, 500)); Assert.Equal(-1, SnapMath.ZoneAt(r, 100, 100));
    }

    [Fact] public void Overlapping_zones_pick_the_smaller_one_and_next_wraps_around()
    {
        var l = new SnapLayout("x", [new(0, 0, 1, 1), new(0.25, 0.25, 0.5, 0.5)]); var r = SnapMath.Rects(l, Work, 0);
        Assert.Equal(1, SnapMath.ZoneAt(r, 960, 520)); Assert.Equal(0, SnapMath.ZoneAt(r, 10, 10));
        Assert.Equal(1, SnapMath.Next(3, 0)); Assert.Equal(0, SnapMath.Next(3, 2)); Assert.Equal(-1, SnapMath.Next(0, 0));
    }

    [Fact] public void Invisible_borders_are_added_so_the_visible_frame_fills_the_zone()
        => Assert.Equal(new CaptureRect(-7, 0, 974, 1047), SnapMath.Compensate(new CaptureRect(0, 0, 960, 1040), 7, 0, 7, 7));

    [Fact] public void Custom_layouts_round_trip_and_invalid_ones_are_dropped_without_losing_the_rest()
    {
        var (list, name) = SnapLayouts.Add([], "Mi diseño", [new(0, 0, 0.3, 1), new(0.3, 0, 0.7, 1)]);
        var back = SnapLayouts.Parse(SnapLayouts.Serialize(list)); Assert.Single(back); Assert.Equal(name, back[0].Name); Assert.Equal(2, back[0].Zones.Count);
        var dirty = "{\"layouts\":[{\"name\":\"Mitades\",\"zones\":[{\"x\":0,\"y\":0,\"w\":1,\"h\":1}]},{\"name\":\"mala\",\"zones\":[{\"x\":2,\"y\":0,\"w\":1,\"h\":1}]},{\"name\":\"\",\"zones\":[]},{\"zones\":1},{\"name\":\"buena\",\"zones\":[{\"x\":0,\"y\":0,\"w\":0.5,\"h\":1}]}]}";
        var p = SnapLayouts.Parse(dirty); Assert.Equal(2, p.Count); Assert.Equal("Mitades (2)", p[0].Name); Assert.Equal("buena", p[1].Name);
        Assert.Empty(SnapLayouts.Parse("no es json")); Assert.Empty(SnapLayouts.Parse(null));
    }

    [Fact] public void Limits_hold_for_zones_and_layouts_and_find_falls_back_to_halves()
    {
        var many = Enumerable.Range(0, 30).Select(i => $"{{\"x\":0,\"y\":0,\"w\":0.1,\"h\":0.1}}"); var json = "{\"layouts\":[{\"name\":\"a\",\"zones\":[" + string.Join(',', many) + "]}]}";
        Assert.Equal(SnapLayouts.MaxZones, SnapLayouts.Parse(json)[0].Zones.Count);
        Assert.Equal("Mitades", SnapLayouts.Find("no existe", []).Name); Assert.Equal("Tercios", SnapLayouts.Find("tercios", []).Name);
        Assert.Equal("Personalizada 2", SnapLayouts.NextCustomName([new SnapLayout("Personalizada 1", [new(0, 0, 1, 1)])]));
    }

    [Fact] public void Dragging_a_zone_snaps_to_the_grid_ignores_direction_and_rejects_tiny_zones()
    {
        var z = SnapMath.FromDrag(960, 520, 0, 0, 1920, 1040)!.Value; Assert.Equal(0, z.X); Assert.Equal(0.5, z.W, 3); Assert.Equal(0.5, z.H, 3);
        Assert.Null(SnapMath.FromDrag(10, 10, 20, 20, 1920, 1040)); Assert.Null(SnapMath.FromDrag(0, 0, 100, 100, 0, 0));
        var o = SnapMath.FromDrag(-50, -50, 5000, 5000, 1920, 1040)!.Value; Assert.Equal(1, o.W, 3);   // se recorta a la pantalla
    }
}
