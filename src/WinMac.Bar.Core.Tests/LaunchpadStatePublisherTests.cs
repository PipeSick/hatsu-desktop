using WinMac.Bar;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class LaunchpadStatePublisherTests
{
    private static (LaunchpadStatePublisher P, List<object> Sent, Func<long> Clock, Action<long> Advance) Make()
    {
        long now = 0; var sent = new List<object>();
        return (new LaunchpadStatePublisher(sent.Add, () => now), sent, () => now, ms => now += ms);
    }
    private static readonly PxRect Mon = new(1920, 0, 1920, 1080);

    [Fact] public void Closed_launchpad_sends_nothing_at_all()
    {
        var (p, sent, _, adv) = Make(); for (int i = 0; i < 50; i++) { p.Tick(false, Mon); adv(100); } Assert.Empty(sent);
    }

    [Fact] public void Opening_sends_once_then_one_heartbeat_per_interval_then_a_close_message()
    {
        var (p, sent, _, adv) = Make();
        p.Tick(true, Mon); Assert.Single(sent);
        for (int i = 0; i < 10; i++) { adv(100); p.Tick(true, Mon); } Assert.Single(sent);                     // 1 s: aún no toca el latido
        adv(600); p.Tick(true, Mon); Assert.Equal(2, sent.Count);                                               // ≥ 1,5 s: latido
        p.Tick(false, Mon); Assert.Equal(3, sent.Count);                                                        // cierre: mensaje inmediato
        adv(5000); p.Tick(false, Mon); Assert.Equal(3, sent.Count);                                             // cerrado: silencio
        var last = sent[^1]; Assert.Equal(false, last.GetType().GetProperty("open")!.GetValue(last));
        var first = sent[0]; Assert.Equal(1920, first.GetType().GetProperty("x")!.GetValue(first)); Assert.Equal(1080, first.GetType().GetProperty("h")!.GetValue(first));
    }

    [Fact] public void Reopening_after_closing_sends_again()
    {
        var (p, sent, _, adv) = Make(); p.Tick(true, Mon); p.Tick(false, Mon); adv(10); p.Tick(true, Mon); Assert.Equal(3, sent.Count);
    }
}
