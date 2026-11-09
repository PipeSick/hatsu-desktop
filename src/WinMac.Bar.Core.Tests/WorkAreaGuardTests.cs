using WinMac.Bar;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class WorkAreaGuardTests
{
    [Fact]
    public void Wm003_reserved_only_when_work_area_starts_below_the_bar()
    {
        Assert.True(WorkAreaGuard.Reserved(workTop: 36, workBottom: 1032, barTop: 0, barBottom: 36));
        Assert.False(WorkAreaGuard.Reserved(workTop: 0, workBottom: 1032, barTop: 0, barBottom: 36));   // primer arranque sin reserva: tapa los menús
        Assert.True(WorkAreaGuard.Reserved(workTop: 84, workBottom: 1080, barTop: 48, barBottom: 84));  // barra de tareas arriba: la barra va debajo
    }

    [Fact]
    public void Wm003_retries_with_growing_wait_and_stops_when_confirmed()
    {
        var g = new WorkAreaGuard(); g.Placed(0);
        Assert.False(g.Due(1, reserved: false));    // Windows aún recoloca: no se mide antes de 2 s
        Assert.True(g.Due(2, reserved: false));     // intento 1
        Assert.False(g.Due(3, reserved: false));    // espera 2 s
        Assert.True(g.Due(4, reserved: false));     // intento 2 → espera 4 s
        Assert.False(g.Due(7, reserved: false));
        Assert.True(g.Due(8, reserved: false));
        Assert.False(g.Due(9, reserved: true));     // confirmado: se reinicia
        Assert.Equal(0, g.Failures);
        Assert.Equal(30, WorkAreaGuard.Backoff(20));
    }
}

public class CaptureCountdownTests
{
    [Fact]
    public void Wm002_counts_down_whole_seconds_and_fires_once_due()
    {
        var c = new WinMac.Bar.Capture.CaptureCountdown(WinMac.Bar.Capture.CaptureMode.Region, 5, now: 100);
        Assert.Equal(5, c.Remaining(100)); Assert.Equal(5, c.Remaining(100.2)); Assert.Equal(4, c.Remaining(101)); Assert.Equal(1, c.Remaining(104.5));
        Assert.False(c.Due(104.99)); Assert.True(c.Due(105)); Assert.Equal("Captura en 3 s", c.Text(102));
    }

    [Fact]
    public void Wm002_only_3_5_or_10_seconds_and_cancel_stops_it()
    {
        Assert.Equal(3, new WinMac.Bar.Capture.CaptureCountdown(WinMac.Bar.Capture.CaptureMode.Region, 1, 0).Seconds);
        Assert.Equal(10, new WinMac.Bar.Capture.CaptureCountdown(WinMac.Bar.Capture.CaptureMode.Region, 60, 0).Seconds);
        var c = new WinMac.Bar.Capture.CaptureCountdown(WinMac.Bar.Capture.CaptureMode.Monitor, 3, 0); c.Cancel();
        Assert.False(c.Due(10)); Assert.Equal(0, c.Remaining(1));
    }
}

public class QuickLookTests
{
    private static readonly WinMac.Bar.Capture.CaptureMonitor Mon = new(new(0, 0, 1920, 1080), 1.5, true, new(0, 54, 1920, 930));

    [Fact]
    public void Thumbnail_sits_bottom_right_of_the_work_area_and_above_the_dock()
    {
        var t = WinMac.Bar.Capture.QuickLook.ThumbRect(Mon, null, 1920, 1080);
        Assert.Equal(1920 - 24, t.X + t.W); Assert.Equal(54 + 930 - 24, t.Y + t.H); Assert.True(t.W <= 330 && t.H <= 225);
        var dock = new WinMac.Bar.Capture.CaptureRect(210, 412, 1500, 668);   // Dock with auto-hide: its WINDOW is tall (magnification room), the visible Dock is the bottom ~110 DIP
        var full = Mon with { Work = new(0, 54, 1920, 1026) };
        var u = WinMac.Bar.Capture.QuickLook.ThumbRect(full, dock, 1920, 1080);
        Assert.Equal(1080 - 165 - 24, u.Y + u.H);   // above the visible Dock, still at the bottom (CI: it went to y=109 using the window's top)
        var side = WinMac.Bar.Capture.QuickLook.ThumbRect(full, new(18, 412, 1200, 668), 1920, 1080);
        Assert.Equal(1080 - 24, side.Y + side.H);   // a centred Dock that does not reach the right corner: the thumbnail stays in the corner
    }

    [Fact]
    public void Visible_five_seconds_then_fades_and_hover_keeps_it()
    {
        Assert.Equal(1, WinMac.Bar.Capture.QuickLook.Alpha(4.9, false));
        Assert.InRange(WinMac.Bar.Capture.QuickLook.Alpha(5.2, false), 0.4, 0.6);
        Assert.True(WinMac.Bar.Capture.QuickLook.Expired(5.5, false)); Assert.False(WinMac.Bar.Capture.QuickLook.Expired(60, true));
    }

    [Fact]
    public void Preview_is_centred_in_the_work_area_and_keys_map_to_actions()
    {
        var (w, img) = WinMac.Bar.Capture.QuickLook.PreviewRect(Mon, 3840, 2160);
        Assert.InRange(w.X + w.W / 2 - (Mon.WorkArea.X + Mon.WorkArea.W / 2), -1, 1); Assert.True(w.Y >= 54 && w.Y + w.H <= 984);
        Assert.True(img.X >= 0 && img.X + img.W <= w.W && img.Y + img.H <= w.H);
        Assert.Equal("close", WinMac.Bar.Capture.QuickLook.KeyAction(0x1B, false)); Assert.Equal("close", WinMac.Bar.Capture.QuickLook.KeyAction(0x20, false));
        Assert.Equal("edit", WinMac.Bar.Capture.QuickLook.KeyAction(0x0D, false)); Assert.Equal("copy", WinMac.Bar.Capture.QuickLook.KeyAction(0x43, true)); Assert.Null(WinMac.Bar.Capture.QuickLook.KeyAction(0x43, false));
        Assert.Equal(8, WinMac.Bar.Capture.QuickLook.Actions.Length);
    }

    [Fact]
    public void Scale_averages_pixels()
    {
        var px = new byte[] { 0, 0, 0, 255, 200, 200, 200, 255 };   // 2×1: black and light grey
        var o = WinMac.Bar.Capture.QuickLook.Scale(px, 2, 1, 1, 1);
        Assert.Equal(100, o[0]); Assert.Equal(255, o[3]);
    }
}
