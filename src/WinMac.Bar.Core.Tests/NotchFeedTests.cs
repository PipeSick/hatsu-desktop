using System.Text.Json;
using WinMac.Bar.Notch;
using WinMac.Bar.Notifications;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Barra → Isla: qué se envía y cuándo. Lógica pura; la recepción real de avisos de Windows (Core Audio, WMI, notificaciones) solo se ve en un escritorio físico.
public class NotchFeedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static AppNotification N(uint id, double minutesAgo, string title = "t") => new(id, "App.Id", "App", title, "cuerpo", T0.AddMinutes(-minutesAgo));
    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
    private static JsonElement Wire(object o) => JsonDocument.Parse(JsonSerializer.Serialize(o)).RootElement.Clone();

    [Fact]
    public void FirstReadIsTheBaseline_OnlyLaterArrivalsAreNew()
    {
        var t = new NewNotificationTracker();
        Assert.Empty(t.Observe([N(1, 0.5), N(2, 0.2)], T0));
        var fresh = t.Observe([N(1, 0.5), N(2, 0.2), N(3, 0)], T0);
        Assert.Equal([3u], fresh.Select(n => n.Id));
        Assert.Empty(t.Observe([N(1, 0.5), N(2, 0.2), N(3, 0)], T0));   // the same list again: nothing new
    }

    [Fact]
    public void OldOnesAndBurstsAreNotAnnouncedOneByOne()
    {
        var t = new NewNotificationTracker(); t.Observe([], T0);
        var fresh = t.Observe([N(10, 30), N(11, 0.4), N(12, 0.3), N(13, 0.2), N(14, 0.1)], T0);
        Assert.Equal([12u, 13u, 14u], fresh.Select(n => n.Id));   // the newest three, oldest first; the one from 30 min ago never
        t.Reset();
        Assert.Empty(t.Observe([N(20, 0)], T0));                  // after a reset the next read is a baseline again
    }

    [Fact]
    public void OptionsFollowTheNotchSettingsAndDefaultToOn()
    {
        Assert.Equal(new NotchFeedOptions(), NotchFeedOptions.Parse(null));
        var o = NotchFeedOptions.Parse(J("""{"pillVolume":false,"pillBrightness":true,"pillNotifications":false,"glassLevel":3}"""));
        Assert.False(o.Volume); Assert.True(o.Brightness); Assert.False(o.Notifications);
        Assert.Equal(new NotchFeedOptions(), NotchFeedOptions.Parse(J("""{"pillVolume":"no"}""")));
    }

    [Fact]
    public void Messages_HaveTheShapeTheNotchReads()
    {
        var v = Wire(NotchFeed.Volume(140, true, "Altavoces"));
        Assert.Equal("volume", v.GetProperty("kind").GetString()); Assert.Equal(100, v.GetProperty("level").GetInt32()); Assert.True(v.GetProperty("muted").GetBoolean());
        var b = Wire(NotchFeed.Brightness(80, "Pantalla integrada"));
        Assert.Equal("brightness", b.GetProperty("kind").GetString()); Assert.Equal("Pantalla integrada", b.GetProperty("display").GetString());
        var n = Wire(NotchFeed.Notification(N(1, 0, "Ana"), new byte[32 * 32 * 4]));
        Assert.Equal("App", n.GetProperty("app").GetString()); Assert.Equal("Ana", n.GetProperty("title").GetString()); Assert.Equal(32, n.GetProperty("iconW").GetInt32());
        Assert.Equal(4096, Convert.FromBase64String(n.GetProperty("icon").GetString()!).Length);
        var bad = Wire(NotchFeed.Notification(N(1, 0), new byte[10]));
        Assert.Equal(JsonValueKind.Null, bad.GetProperty("icon").ValueKind); Assert.Equal(0, bad.GetProperty("iconW").GetInt32());
    }

    [Fact]
    public void DoNotDisturbAndPresentationsSilenceTheNotch()
    {
        Assert.False(NotchFeed.Quiet(5));     // QUNS_ACCEPTS_NOTIFICATIONS
        Assert.True(NotchFeed.Quiet(2));      // QUNS_BUSY (full screen)
        Assert.True(NotchFeed.Quiet(4));      // QUNS_PRESENTATION_MODE
        Assert.True(NotchFeed.Quiet(6));      // QUNS_QUIET_TIME
        Assert.False(NotchFeed.Quiet(null));  // unknown: same as Windows, notifications allowed
    }
}
