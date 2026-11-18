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

public class SteelSeriesProtocolTests
{
    private static SteelSeriesProtocol.Reading P(SteelSeriesProtocol.Family f, ushort pid, params byte[] r) => SteelSeriesProtocol.Parse(f, pid, r)!;

    [Fact] public void Every_known_product_belongs_to_one_family_and_the_requests_are_the_documented_ones()
    {
        Assert.Equal(SteelSeriesProtocol.Family.Aerox, SteelSeriesProtocol.Find(0x1838)!.Family); Assert.Equal(SteelSeriesProtocol.Family.Nova, SteelSeriesProtocol.Find(0x2202)!.Family); Assert.Null(SteelSeriesProtocol.Find(0xFFFF));
        Assert.Equal(new byte[] { 0x06, 0x12 }, SteelSeriesProtocol.StatusRequests(SteelSeriesProtocol.Family.Arctis1, 0x12B3)[0]); Assert.Equal(new byte[] { 0x00, 0xD2 }, SteelSeriesProtocol.StatusRequests(SteelSeriesProtocol.Family.Aerox, 0x1838)[0]);
        Assert.Equal(new byte[] { 0x00, 0x92 }, SteelSeriesProtocol.StatusRequests(SteelSeriesProtocol.Family.Aerox, 0x183A)[0]); Assert.Equal(2, SteelSeriesProtocol.StatusRequests(SteelSeriesProtocol.Family.ArctisProWireless, 0x1290).Count);
        Assert.Equal(0, SteelSeriesProtocol.CollectionFor(0x172B).Interface); Assert.Equal(3, SteelSeriesProtocol.CollectionFor(0x1872).Interface);
    }

    [Fact] public void Arctis_headsets_read_their_percent_offline_and_charging()
    {
        var a1 = P(SteelSeriesProtocol.Family.Arctis1, 0x12B3, 0, 0, 0x00, 75); Assert.True(a1.Connected); Assert.Equal(75, a1.Percent);
        Assert.False(P(SteelSeriesProtocol.Family.Arctis1, 0x12B3, 0, 0, 0x01, 75).Connected); Assert.Null(P(SteelSeriesProtocol.Family.Arctis1, 0x12B3, 0, 0, 0x01, 75).Percent);
        Assert.Equal(60, P(SteelSeriesProtocol.Family.Arctis7, 0x1260, 0, 0, 60).Percent); Assert.Equal("SteelSeries Arctis Pro", P(SteelSeriesProtocol.Family.Arctis7, 0x1252, 0, 0, 60).Name);
        var a9 = P(SteelSeriesProtocol.Family.Arctis9, 0x12C2, 0, 0, 0, 0x7F, 1); Assert.Equal(50, a9.Percent); Assert.True(a9.Charging);
    }

    [Fact] public void Pro_Wireless_needs_a_second_question_for_the_level_and_Nova_Pro_reads_eight_steps()
    {
        var pro = P(SteelSeriesProtocol.Family.ArctisProWireless, 0x1290, 0x04); Assert.True(pro.Connected && pro.NeedsLevel); Assert.False(P(SteelSeriesProtocol.Family.ArctisProWireless, 0x1290, 0x02).Connected);
        Assert.Equal(50, SteelSeriesProtocol.ParseLevel([2])); Assert.Null(SteelSeriesProtocol.ParseLevel([9])); Assert.Null(SteelSeriesProtocol.ParseLevel([]));
        var r = new byte[20]; r[6] = 4; r[15] = 2; var np = SteelSeriesProtocol.Parse(SteelSeriesProtocol.Family.NovaProWireless, 0x12E0, r)!; Assert.Equal(50, np.Percent); Assert.True(np.Charging); Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Family.NovaProWireless, 0x12E0, new byte[8]));
    }

    [Fact] public void Nova_family_layouts_depend_on_the_model()
    {
        Assert.Equal(75, P(SteelSeriesProtocol.Family.Nova, 0x220E, 0, 0x00, 3, 0x01, 0, 0, 0).Percent);   // Arctis 7+: 0…4
        var n5 = P(SteelSeriesProtocol.Family.Nova, 0x2232, 0, 0x00, 0, 88, 0x01, 0, 0); Assert.Equal(88, n5.Percent); Assert.True(n5.Charging);
        Assert.Equal(30, P(SteelSeriesProtocol.Family.Nova, 0x230A, 0, 0, 0, 0x03, 0x03, 30, 55).Percent);   // GameBuds: el menor
        Assert.False(P(SteelSeriesProtocol.Family.Nova, 0x230A, 0, 0, 0, 0, 0, 30, 55).Connected);
        Assert.Equal(50, P(SteelSeriesProtocol.Family.Nova, 0x2202, 0, 0, 2, 0x04, 0, 0, 0).Percent);   // Nova 7 con niveles
        Assert.Null(P(SteelSeriesProtocol.Family.Nova, 0x2202, 0, 0, 80, 0x00, 0, 0, 0).Percent);   // desconectado: sin número
    }

    [Fact] public void Mice_decode_their_level_charging_and_off_states()
    {
        var a = P(SteelSeriesProtocol.Family.Aerox, 0x1838, 0, 0x80 | 21); Assert.Equal(100, a.Percent); Assert.True(a.Charging);
        Assert.Equal(0, P(SteelSeriesProtocol.Family.Aerox, 0x1838, 0, 1).Percent); Assert.False(P(SteelSeriesProtocol.Family.Aerox, 0x1838, 0, 0).Connected); Assert.Null(P(SteelSeriesProtocol.Family.Aerox, 0x1838, 0, 60).Percent);
        Assert.Equal("Cable USB", P(SteelSeriesProtocol.Family.Aerox, 0x183A, 0, 11).Via);
        var rv = P(SteelSeriesProtocol.Family.Rival, 0x172B, 70, 0, 1); Assert.Equal(70, rv.Percent); Assert.True(rv.Charging); Assert.Equal("SteelSeries Rival 650 Wireless", rv.Name); Assert.Equal("Cable USB", rv.Via);
        Assert.Equal(55, P(SteelSeriesProtocol.Family.RivalPro, 0x1880, 0x92, 0x00, 55).Percent); Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Family.RivalPro, 0x1880, [0x91, 0, 55])); Assert.Null(SteelSeriesProtocol.Parse(SteelSeriesProtocol.Family.RivalPro, 0x1880, [0x92, 0, 200]));
    }

    [Fact] public void A_leading_zero_is_dropped_from_frames_and_numbered_ones_are_kept()
    {
        Assert.Equal(new byte[] { 5, 6 }, SteelSeriesProtocol.Payload([0, 5, 6])); Assert.Equal(new byte[] { 0x92, 0, 55 }, SteelSeriesProtocol.Payload([0x92, 0, 55]));
        Assert.Equal(100, SteelSeriesProtocol.Map(300, 0, 8)); Assert.Equal(0, SteelSeriesProtocol.Map(-5, 0, 8));
    }
}
