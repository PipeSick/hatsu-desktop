using WinMac.Bar.Peripherals;
using Xunit;

namespace WinMac.Bar.Tests;

public class GloriousProtocolTests
{
    private static byte[] Reply(int status, int charging = 0, int pct = 0, int marker = 0x83) { var r = new byte[65]; r[1] = (byte)status; r[6] = (byte)marker; r[7] = (byte)charging; r[8] = (byte)pct; return r; }

    [Fact] public void The_request_is_65_bytes_with_02_02_83_at_3_4_6()
    {
        var q = GloriousProtocol.Request(); Assert.Equal(65, q.Length); Assert.Equal((byte)0, q[0]); Assert.Equal([2, 2, 0, 0x83], [q[3], q[4], q[5], q[6]]);
    }

    [Fact] public void An_awake_answer_gives_percent_and_charging_and_zero_reads_as_one()
    {
        var r = GloriousProtocol.Parse(Reply(0xA1, 1, 64)); Assert.Equal(GloriousProtocol.State.Awake, r.State); Assert.Equal(64, r.Percent); Assert.True(r.Charging);
        Assert.Equal(1, GloriousProtocol.Parse(Reply(0xA1, 0, 0)).Percent); Assert.Null(GloriousProtocol.Parse(Reply(0xA1, 0, 150)).Percent);
    }

    [Fact] public void Asleep_waking_and_foreign_answers_never_give_a_number()
    {
        Assert.Equal(GloriousProtocol.State.Asleep, GloriousProtocol.Parse(Reply(0xA4, 0, 50)).State); Assert.Null(GloriousProtocol.Parse(Reply(0xA4, 0, 50)).Percent);
        Assert.Equal(GloriousProtocol.State.WakingUp, GloriousProtocol.Parse(Reply(0xA0)).State);
        Assert.Equal(GloriousProtocol.State.NotThisProtocol, GloriousProtocol.Parse(Reply(0xA1, 0, 50, marker: 0x11)).State); Assert.Equal(GloriousProtocol.State.NotThisProtocol, GloriousProtocol.Parse(Reply(0x01, 0, 50)).State);
        Assert.Equal(GloriousProtocol.State.NotThisProtocol, GloriousProtocol.Parse(null).State); Assert.Equal(GloriousProtocol.State.NotThisProtocol, GloriousProtocol.Parse(new byte[4]).State);
    }

    [Fact] public void Models_are_named_by_brand_and_product_and_cables_are_told_apart()
    {
        Assert.Equal(("Glorious Model O Wireless", false), GloriousProtocol.Identify(0x258A, 0x2022)); Assert.Equal(("Glorious Model O Wireless", true), GloriousProtocol.Identify(0x258A, 0x2011)); Assert.Null(GloriousProtocol.Identify(0x258A, 0x9999));
        Assert.Equal("G-Wolves Fenrir", GloriousProtocol.Identify(0x33E4, 0x3517)!.Value.Name); Assert.True(GloriousProtocol.Identify(0x33E4, 0x3508)!.Value.Cable); Assert.Equal("G-Wolves wireless mouse", GloriousProtocol.Identify(0x33E4, 0x3854)!.Value.Name);
        Assert.Equal("WLMouse YING", GloriousProtocol.Identify(0x36A7, 0xA874)!.Value.Name); Assert.Null(GloriousProtocol.Identify(0x36A7, 0x0001)); Assert.Equal("Hitscan Hyperlight X", GloriousProtocol.Identify(0x3770, 0x0136)!.Value.Name);
    }
}

public class RazerProtocolTests
{
    private static byte[] Reply(byte status, byte tid, byte cls, byte cmd, byte size, params byte[] args)
    {
        var f = new byte[91]; f[1] = status; f[2] = tid; f[6] = size; f[7] = cls; f[8] = cmd; for (int i = 0; i < args.Length; i++) f[9 + i] = args[i]; f[89] = RazerProtocol.Crc(f); return f;
    }

    [Fact] public void The_request_has_the_transaction_class_command_size_and_a_valid_xor()
    {
        var q = RazerProtocol.Request(0x1F, 0x07, 0x80, 0x02); Assert.Equal(91, q.Length);
        byte x = 0; for (int i = 3; i < 89; i++) x ^= q[i]; Assert.Equal(x, q[89]);
    }

    [Fact] public void A_good_answer_gives_the_percent_from_a_0_to_255_scale_and_charging()
    {
        var a = RazerProtocol.Parse(Reply(0x02, 0x1F, 0x07, 0x80, 0x02, 0x00, 0xFF), 0x1F, 0x07, 0x80, 0x02); Assert.Equal(RazerProtocol.Status.Ok, a.Status); Assert.Equal(100, RazerProtocol.Percent(a.Args));
        Assert.Equal(50, RazerProtocol.Percent([0, 128])); Assert.Equal(0, RazerProtocol.Percent([0, 0])); Assert.Null(RazerProtocol.Percent([0]));
        Assert.True(RazerProtocol.Charging([0, 1])); Assert.False(RazerProtocol.Charging([0, 0]));
    }

    [Fact] public void Status_codes_and_foreign_answers_are_told_apart()
    {
        Assert.Equal(RazerProtocol.Status.Busy, RazerProtocol.Parse(Reply(0x01, 0x1F, 0x07, 0x80, 0x02), 0x1F, 0x07, 0x80, 0x02).Status);
        Assert.Equal(RazerProtocol.Status.Offline, RazerProtocol.Parse(Reply(0x04, 0x1F, 0x07, 0x80, 0x02), 0x1F, 0x07, 0x80, 0x02).Status);
        Assert.Equal(RazerProtocol.Status.Unsupported, RazerProtocol.Parse(Reply(0x05, 0x1F, 0x07, 0x80, 0x02), 0x1F, 0x07, 0x80, 0x02).Status);
        Assert.Equal(RazerProtocol.Status.NotAnswer, RazerProtocol.Parse(Reply(0x02, 0x3F, 0x07, 0x80, 0x02), 0x1F, 0x07, 0x80, 0x02).Status);   // otra transacción (Synapse)
        Assert.Equal(RazerProtocol.Status.NotAnswer, RazerProtocol.Parse(Reply(0x02, 0x1F, 0x07, 0x84, 0x02), 0x1F, 0x07, 0x80, 0x02).Status);   // otro comando
        var bad = Reply(0x02, 0x1F, 0x07, 0x80, 0x02, 0, 9); bad[89] ^= 0xFF; Assert.Equal(RazerProtocol.Status.NotAnswer, RazerProtocol.Parse(bad, 0x1F, 0x07, 0x80, 0x02).Status);   // XOR roto
        Assert.Equal(RazerProtocol.Status.NotAnswer, RazerProtocol.Parse(null, 0x1F, 0x07, 0x80, 0x02).Status);
    }

    [Fact] public void Keyboards_use_their_own_transaction_and_names_lose_receiver_suffixes()
    {
        Assert.Equal(0x9F, RazerProtocol.KeyboardTransaction(0x02D2)); Assert.Equal(0x1F, RazerProtocol.KeyboardTransaction(0x0258)); Assert.Equal(0x3F, RazerProtocol.KeyboardTransaction(0x025A)); Assert.Equal(0, RazerProtocol.KeyboardTransaction(0x0001));
        Assert.Equal(DeviceKind.Keyboard, RazerProtocol.Kind(0x02D2)); Assert.Equal(DeviceKind.Mouse, RazerProtocol.Kind(0x00B6)); Assert.Null(RazerProtocol.Kind(0xFFFF));
        Assert.Equal("Razer DeathAdder V3 Pro", RazerProtocol.CleanName("Razer DeathAdder V3 Pro HyperSpeed Dongle")); Assert.Equal("Razer device", RazerProtocol.CleanName(""));
    }
}
