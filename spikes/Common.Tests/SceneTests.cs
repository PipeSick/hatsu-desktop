using WinMac.Spikes.Common;
using Xunit;

public class SceneTests
{
    [Fact]
    public void Rest_layout_has_base_sizes_and_no_overlap()
    {
        var f = DockScene.Layout(null);
        Assert.All(f.Size, s => Assert.Equal(DockScene.BaseSize, s, 6));
        for (int i = 1; i < f.X.Length; i++) Assert.True(f.X[i] >= f.X[i - 1] + f.Size[i - 1], $"solape en {i}");
    }

    [Theory]
    [InlineData(0)] [InlineData(150)] [InlineData(400)] [InlineData(700)]
    public void Magnified_layout_never_overlaps_and_stays_inside_bar(double cursor)
    {
        var f = DockScene.Layout(cursor);
        for (int i = 1; i < f.X.Length; i++) Assert.True(f.X[i] >= f.X[i - 1] + f.Size[i - 1] - 1e-9);
        Assert.True(f.X[^1] + f.Size[^1] <= f.BarWidth - DockScene.PadX + 1e-9);
        Assert.All(f.Size, s => Assert.InRange(s, DockScene.BaseSize - 1e-9, DockScene.BaseSize * DockScene.MaxScale + 1e-9));
    }

    [Fact]
    public void Magnification_grows_bar_and_peaks_near_cursor()
    {
        var rest = DockScene.Layout(null); var hover = DockScene.Layout(DockScene.RestWidth / 2);
        Assert.True(hover.BarWidth > rest.BarWidth);
        int peak = Array.IndexOf(hover.Size, hover.Size.Max());
        Assert.InRange(peak, 5, 8);
    }

    [Fact]
    public void Window_is_wide_and_tall_enough_for_maximum_magnification()
    {
        double worst = new[] { 0.0, DockScene.RestWidth / 2, DockScene.RestWidth }.Max(c => DockScene.Layout(c).BarWidth);
        Assert.True(DockScene.WindowWidth >= worst + 2 * DockScene.ShadowMargin - 1e-6, $"{DockScene.WindowWidth} < {worst}");
        Assert.True(DockScene.WindowHeight >= DockScene.BarHeight + DockScene.BaseSize * (DockScene.MaxScale - 1));
    }

    [Fact]
    public void Separator_sits_between_apps_and_folder()
    {
        var f = DockScene.Layout(null);
        Assert.True(f.SeparatorX > f.X[DockScene.SeparatorBefore - 1] + f.Size[DockScene.SeparatorBefore - 1]);
        Assert.True(f.SeparatorX < f.X[DockScene.SeparatorBefore]);
    }

    [Fact]
    public void Sweep_stays_within_rest_width() =>
        Assert.All(Enumerable.Range(0, 500).Select(i => DockScene.SweepX(i * 0.01, DockScene.RestWidth)), x => Assert.InRange(x, -1e-9, DockScene.RestWidth + 1e-9));

    [Fact]
    public void Squircle_points_are_inside_unit_square_and_closed_loop()
    {
        var p = Squircle.Unit();
        Assert.All(p, q => { Assert.InRange(q.X, -1e-9, 1 + 1e-9); Assert.InRange(q.Y, -1e-9, 1 + 1e-9); });
        Assert.Equal(1.0, p.Max(q => q.X), 3); Assert.Equal(0.0, p.Min(q => q.Y), 3);
    }

    [Fact]
    public void FrameRecorder_summarizes_percentiles()
    {
        var r = new FrameRecorder(); r.Tick(); Thread.Sleep(5); r.Tick(); Thread.Sleep(5); r.Tick();
        var s = r.Summarize(60);
        Assert.Equal(2, s.Frames); Assert.True(s.MaxMs >= 4);
    }

    [Fact]
    public async Task BenchRun_quick_mode_finishes_with_sweep_then_hide_then_null()
    {
        var run = new BenchRun("T", new CliOptions("v", Pose.None, false, "x", true));
        bool started = false;
        await run.RunIdleAsync(a => a(), () => started = true);
        Assert.True(started);
        bool sawHide = false; int guard = 0;
        while (guard++ < 100000)
        {
            var p = run.Frame();
            if (p is null) break;
            if (p.Value.HideT > 0) sawHide = true;
            Thread.Sleep(2);
        }
        Assert.True(sawHide); Assert.Null(run.Frame());
    }
}
