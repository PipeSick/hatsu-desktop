using WinMac.Bar.Peripherals;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class AsusHeadsetProtocolTests
{
    private static byte[] Reply(byte sub, int b5 = 0, int b6 = 0) { var r = new byte[64]; r[0] = 0xCC; r[1] = 0x12; r[2] = sub; r[5] = (byte)b5; r[6] = (byte)b6; return r; }

    [Fact] public void The_request_is_cc_12_sub_padded_to_64_bytes()
    {
        var q = AsusRogProtocol.HeadsetRequest(0x07); Assert.Equal(64, q.Length); Assert.Equal([0xCC, 0x12, 0x07], q[..3]); Assert.All(q[3..], b => Assert.Equal(0, b));
        Assert.Equal(0x08, AsusRogProtocol.HeadsetRequest(0x08)[2]);
    }

    [Fact] public void A_battery_answer_gives_the_percent_and_unknown_or_asleep_never_a_number()
    {
        Assert.Equal(new AsusRogProtocol.HeadsetBattery(72, false), AsusRogProtocol.ParseHeadsetBattery(Reply(0x07, 0, 72)));
        Assert.Equal(new AsusRogProtocol.HeadsetBattery(null, false), AsusRogProtocol.ParseHeadsetBattery(Reply(0x07, 0, 0xFE)));   // 0xFE = aún no lo sabe
        Assert.Equal(new AsusRogProtocol.HeadsetBattery(null, true), AsusRogProtocol.ParseHeadsetBattery(Reply(0x07, 0xFF, 0xAA)));   // FF AA en [5..6]
        var off = new byte[64]; off[0] = 0xCC; off[1] = 0xFF; off[2] = 0xAA; Assert.True(AsusRogProtocol.ParseHeadsetBattery(off)!.Asleep);                  // FF AA en [1..2]
        Assert.Null(AsusRogProtocol.ParseHeadsetBattery(Reply(0x08, 1, 50)));   // respuesta de otra consulta
        Assert.Null(AsusRogProtocol.ParseHeadsetBattery(null)); Assert.Null(AsusRogProtocol.ParseHeadsetBattery(new byte[4]));
        var other = Reply(0x07, 0, 50); other[0] = 0x02; Assert.Null(AsusRogProtocol.ParseHeadsetBattery(other));   // informe de otro canal
    }

    [Fact] public void The_charging_answer_is_read_from_byte_5()
    {
        Assert.True(AsusRogProtocol.ParseHeadsetCharging(Reply(0x08, 1))); Assert.False(AsusRogProtocol.ParseHeadsetCharging(Reply(0x08, 0)));
        Assert.Null(AsusRogProtocol.ParseHeadsetCharging(Reply(0x07, 1, 60)));
    }

    [Fact] public void Only_the_known_headset_receivers_are_listed()
    {
        Assert.Equal("ROG Delta II", AsusRogProtocol.Headsets[0x1AFA]); Assert.Equal("ROG Pelta", AsusRogProtocol.Headsets[0x1B84]); Assert.False(AsusRogProtocol.Headsets.ContainsKey(AsusRogProtocol.OmniReceiverPid));
    }
}
