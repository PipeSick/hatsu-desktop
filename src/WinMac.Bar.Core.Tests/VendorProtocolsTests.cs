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

public class CorsairProtocolTests
{
    [Fact] public void Void_answers_give_percent_without_the_mic_bit_and_charging_states()
    {
        var ok = CorsairProtocol.ParseVoid([0, 0, 0x80 | 55, 0, 1])!; Assert.True(ok.Connected); Assert.Equal(55, ok.Percent); Assert.False(ok.Charging);
        var ch = CorsairProtocol.ParseVoid([0, 0, 90, 0, 4])!; Assert.True(ch.Charging); Assert.Equal(90, ch.Percent);
        var off = CorsairProtocol.ParseVoid([0, 0, 70, 0, 0])!; Assert.False(off.Connected); Assert.Null(off.Percent); Assert.Null(CorsairProtocol.ParseVoid([1, 2])); Assert.Null(CorsairProtocol.ParseVoid(null));
        Assert.Equal([0xC9, 0x64], CorsairProtocol.VoidRequest);
    }

    [Fact] public void Bragi_requests_come_in_three_framings()
    {
        Assert.Equal(new byte[] { 0x02, 0x09, 0x02, 0x0F }, CorsairProtocol.BragiRequest(false, true, 0x09, 0x0F)); Assert.Equal(new byte[] { 0x00, 0x02, 0x09, 0x02, 0x0F }, CorsairProtocol.BragiRequest(false, false, 0x09, 0x0F));
        Assert.Equal(new byte[] { 0x00, 0x08, 0x02, 0x10 }, CorsairProtocol.BragiRequest(true, true, 0x08, 0x10));
    }

    [Fact] public void Bragi_answers_are_matched_by_source_and_carry_a_little_endian_value()
    {
        var a = CorsairProtocol.ParseBragi(false, 0x09, [0x02, 0x01, 0x02, 0x00, 0xE8, 0x03, 0, 0])!; Assert.Equal(CorsairProtocol.BragiStatus.Value, a.Status); Assert.Equal(1000u, a.Value);
        Assert.Equal(1000u, CorsairProtocol.ParseBragi(true, 0x09, [0x01, 0x02, 0x00, 0xE8, 0x03, 0, 0])!.Value); Assert.Equal(300u, CorsairProtocol.ParseBragi(true, 0x08, [0x00, 0x02, 0x00, 0x2C, 0x01, 0, 0])!.Value);
        Assert.Null(CorsairProtocol.ParseBragi(false, 0x09, [0x02, 0x00, 0x02, 0x00, 1, 0, 0, 0]));   // viene de otro origen: una notificación
        Assert.Equal(CorsairProtocol.BragiStatus.Error, CorsairProtocol.ParseBragi(false, 0x09, [0x02, 0x01, 0x02, 0x05, 0, 0, 0, 0])!.Status); Assert.Null(CorsairProtocol.ParseBragi(false, 0x09, [1, 2]));
    }

    [Fact] public void Bragi_levels_map_0_to_1000_to_percent_and_zero_is_not_a_reading()
    {
        Assert.Equal(100, CorsairProtocol.BragiPercent(1000)); Assert.Equal(50, CorsairProtocol.BragiPercent(500)); Assert.Equal(0, CorsairProtocol.BragiPercent(1)); Assert.Null(CorsairProtocol.BragiPercent(0)); Assert.Null(CorsairProtocol.BragiPercent(1001));
        Assert.True(CorsairProtocol.BragiCharging(1)); Assert.True(CorsairProtocol.BragiCharging(3)); Assert.False(CorsairProtocol.BragiCharging(2));
    }

    [Fact] public void Compx_frames_have_a_checksum_and_answers_are_validated_against_it()
    {
        var q = CorsairProtocol.CompxRequest(0x04); Assert.Equal(17, q.Length); Assert.Equal((byte)0x08, q[0]); Assert.Equal(CorsairProtocol.CompxChecksum(q), q[16]); Assert.True(CorsairProtocol.IsCompxReply(0x04, q));
        var r = new byte[17]; r[0] = 8; r[1] = 4; r[6] = 87; r[7] = 1; r[8] = 0x0F; r[9] = 0xA0; r[16] = CorsairProtocol.CompxChecksum(r);
        Assert.True(CorsairProtocol.IsCompxReply(4, r)); var b = CorsairProtocol.CompxParseBattery(r); Assert.Equal(87, b.Percent); Assert.True(b.Charging); Assert.Equal(0x0FA0, b.Millivolts);
        r[16] ^= 1; Assert.False(CorsairProtocol.IsCompxReply(4, r)); Assert.False(CorsairProtocol.IsCompxReply(3, q)); Assert.False(CorsairProtocol.IsCompxReply(4, null));
    }

    [Fact] public void Kinds_come_from_the_name_the_paired_id_and_the_0Axx_family()
    {
        Assert.Equal(DeviceKind.Headset, CorsairProtocol.GuessKind("CORSAIR HS80 RGB WIRELESS Gaming Receiver", 0x0A6B)); Assert.Equal(DeviceKind.Mouse, CorsairProtocol.GuessKind("Corsair Slipstream Receiver", 0x1BDC, 0x1B7E));
        Assert.Equal(DeviceKind.Headset, CorsairProtocol.GuessKind("Receiver", 0x1BDC, 0x0A3E)); Assert.Equal(DeviceKind.Keyboard, CorsairProtocol.GuessKind("K70 RGB", 0x2B00)); Assert.Equal(DeviceKind.Other, CorsairProtocol.GuessKind("Algo", 0x2B00));
    }
}

public class HyperXProtocolTests
{
    private static byte[] R(int len, params (int I, byte V)[] set) { var b = new byte[len]; foreach (var (i, v) in set) b[i] = v; return b; }

    [Fact] public void Collections_are_picked_by_page_usage_and_report_sizes()
    {
        var m = HyperXProtocol.Models.First(x => x.Pids.Contains((ushort)0x0C9D)); Assert.True(m.Collection.Matches(0xFF13, 1, 62, 62, 0)); Assert.False(m.Collection.Matches(0xFF13, 1, 64, 64, 0));
        var s = HyperXProtocol.Models.First(x => x.Family == HyperXProtocol.Family.StingerCore); Assert.True(s.Collection.Matches(0xFF00, 1, 0, 0, 32)); Assert.False(s.Collection.Matches(0xFF00, 2, 0, 0, 32)); Assert.False(s.Collection.Matches(0xFF00, 1, 0, 0, 8));
        var a2 = HyperXProtocol.Models.First(x => x.Family == HyperXProtocol.Family.CloudAlpha2); Assert.True(a2.Collection.Matches(0xFF13, 0xFF00, 0, 0, 0));
    }

    [Fact] public void Cloud_III_asks_66_cmd_and_reads_connection_charging_and_percent()
    {
        Assert.Equal(new byte[] { 0x66, 0x82 }, HyperXProtocol.Cloud3(0x82).Request); Assert.True(HyperXProtocol.Cloud3(0x82).Accept(R(8, (0, 0x66), (1, 0x82)))); Assert.False(HyperXProtocol.Cloud3(0x82).Accept(R(8, (0, 0x66), (1, 0x8A))));
        var link = R(8, (0, 0x66), (1, 0x82), (2, 1)); var ch = R(8, (2, 1)); var bat = R(8, (2, 0x0F), (3, 0xA0), (4, 77));
        var r = HyperXProtocol.Cloud3Reading(0x0C9D, link, ch, bat)!; Assert.True(r.Connected); Assert.Equal(77, r.Percent); Assert.True(r.Charging); Assert.Equal(0x0FA0, r.Millivolts); Assert.Equal("HyperX Cloud III Wireless", r.Name);
        Assert.Equal("HyperX Cloud II Core Wireless", HyperXProtocol.Cloud3Reading(0x069F, link, null, null)!.Name); Assert.False(HyperXProtocol.Cloud3Reading(0x0C9D, R(8), null, null)!.Connected);
        Assert.Null(HyperXProtocol.Cloud3Reading(0x0C9D, link, null, R(8, (4, 200)))!.Percent);
    }

    [Fact] public void Cloud_II_and_Alpha_read_their_documented_offsets()
    {
        Assert.Equal(60, HyperXProtocol.Cloud2Reading("x", R(10, (7, 60), (5, 0x0F), (6, 0xA0)), R(8, (4, 1)), true)!.Percent); Assert.Null(HyperXProtocol.Cloud2Reading("x", R(10, (7, 150)), null, true));
        Assert.Equal((byte)0xBB, HyperXProtocol.Cloud2Kingston(0x02).Request[14]); Assert.Equal((byte)0x02, HyperXProtocol.Cloud2Kingston(0x02).Request[15]);
        var off = HyperXProtocol.CloudAlphaReading(R(8, (3, 1)), null, null); Assert.False(off.Connected);
        var on = HyperXProtocol.CloudAlphaReading(R(8, (3, 0)), R(8, (3, 1)), R(8, (3, 45))); Assert.True(on.Connected); Assert.Equal(45, on.Percent); Assert.True(on.Charging);
        var a2 = HyperXProtocol.CloudAlpha2Reading(R(8, (2, 1)), R(8, (2, 88), (3, 1), (6, 0x34), (7, 0x12)))!; Assert.Equal(88, a2.Percent); Assert.True(a2.Charging); Assert.Equal(0x1234, a2.Millivolts); Assert.False(HyperXProtocol.CloudAlpha2Reading(R(8), null).Connected);
        Assert.True(HyperXProtocol.Pair(0x52, 1).Accept(R(8, (0, 0x53), (1, 1)))); Assert.False(HyperXProtocol.Pair(0x52, 1).Accept(R(8, (0, 0x52), (1, 1))));
    }

    [Fact] public void Haste_discards_the_reading_while_the_mouse_is_settling()
    {
        var bat = R(8, (2, 40), (3, 2)); Assert.Null(HyperXProtocol.HasteReading(0x0AB5, true, bat, settled: false).Percent); var ok = HyperXProtocol.HasteReading(0x0AB5, true, bat, settled: true); Assert.Equal(40, ok.Percent); Assert.True(ok.Charging);
        Assert.False(HyperXProtocol.HasteReading(0x0AB5, false, null, true).Connected); Assert.Equal("HyperX Pulsefire Haste 2 Core Wireless", HyperXProtocol.HasteName(0x0AB5)); Assert.Equal("HyperX Pulsefire Haste 2 Wireless", HyperXProtocol.HasteName(0x0D97));
    }

    [Fact] public void Flight_converts_millivolts_to_percent_and_high_voltage_means_charging()
    {
        Assert.True(HyperXProtocol.FlightReading(R(5, (3, 0x11), (4, 0x00))).Charging); Assert.Equal(100, HyperXProtocol.FlightReading(R(5, (3, 0x0F), (4, 0xC0))).Percent); Assert.Equal(4, HyperXProtocol.FlightReading(R(5, (3, 0x0E), (4, 0x40))).Percent);
        var mid = HyperXProtocol.FlightReading(R(5, (3, 0x0F), (4, 0x00))).Percent!.Value; Assert.InRange(mid, 0, 100);
    }

    [Fact] public void Cloud_III_S_and_Stinger_Core_decode_their_frames()
    {
        var link = R(8, (0, 0x0C), (5, 2), (6, 2)); Assert.True(HyperXProtocol.Cloud3S(2).Accept(link)); Assert.False(HyperXProtocol.Cloud3S(2).Accept(R(8, (0, 0x0C), (5, 2), (6, 0xFF))));
        var r = HyperXProtocol.Cloud3SReading(link, R(8, (6, 64)), R(8, (6, 2)))!; Assert.Equal(64, r.Percent); Assert.True(r.Charging); Assert.False(HyperXProtocol.Cloud3SReading(R(8, (6, 1)), null, null)!.Connected);
        var conn = R(20, (11, 1)); var bat = R(20, (0, 0xFF), (1, 0x12), (2, 5), (9, 3), (11, 0x0E), (12, 0x0F));   // 0x0F0E = 3854 mV
        var s = HyperXProtocol.StingerReading(0x173F, conn, bat); Assert.Equal("HyperX Cloud Core Wireless", s.Name); Assert.False(s.Charging); Assert.InRange(s.Percent!.Value, 60, 70);   // 3854 mV está entre el 70 % (3905) y el 60 % (3838)
        Assert.Equal(100, HyperXProtocol.StingerReading(0x0A93, conn, R(20, (9, 6), (11, 0), (12, 0))).Percent); Assert.False(HyperXProtocol.StingerReading(0x0A93, R(20, (11, 3)), null).Connected); Assert.Null(HyperXProtocol.StingerReading(0x0A93, conn, R(20, (9, 9))).Percent);
        Assert.True(HyperXProtocol.IsStingerBattery(bat)); Assert.False(HyperXProtocol.IsFreshConnection(R(20, (1, 1)))); Assert.True(HyperXProtocol.NeedsRereads(0x1713)); Assert.False(HyperXProtocol.NeedsRereads(0x170B));
        Assert.Equal(100, HyperXProtocol.Interpolate(5000, [4121, 4042, 3517], [100, 50, 0])); Assert.Equal(0, HyperXProtocol.Interpolate(3000, [4121, 4042, 3517], [100, 50, 0])); Assert.Equal(75, HyperXProtocol.Interpolate(4081, [4121, 4041, 3517], [100, 50, 0]) is var v && v is >= 74 and <= 76 ? 75 : -1);
    }
}

public class QueryDevicesTests
{
    private static byte[] R(int len, params (int I, byte V)[] set) { var b = new byte[len]; foreach (var (i, v) in set) b[i] = v; return b; }

    [Fact] public void Selectors_demand_only_what_they_name()
    {
        var s = QueryDevices.CoolerMaster.Collection; Assert.True(s.Matches(0xFF0A, 0, 3, 32, 32, 0)); Assert.False(s.Matches(0xFF0A, 0, 2, 32, 32, 0)); Assert.False(s.Matches(0xFF00, 0, 3, 32, 32, 0));
        Assert.True(QueryDevices.Lamzu.Collection.Matches(0, 0, -1, 0, 0, 65)); Assert.False(QueryDevices.Lamzu.Collection.Matches(0, 0, -1, 0, 0, 64));
        Assert.Equal(19, QueryDevices.Specs.Count);
    }

    [Fact] public void Cooler_Master_reads_percent_charging_and_the_link_and_names_the_model_on_the_shared_dongle()
    {
        var q = QueryDevices.CoolerMaster.Request(0x01B7); Assert.Equal([0x00, 0x46, 0x81, 0x00], q);
        var r = R(20, (0, 0x47), (1, 0x81), (2, 0x50), (10, 64), (11, 1), (12, 1)); var m = QueryDevices.CoolerMaster.Parse(0x2516, 0x01B7, r)!;
        Assert.Equal("Cooler Master MM712", m.Name); Assert.Equal(64, m.Percent); Assert.True(m.Charging); Assert.False(m.Cable);
        Assert.Equal("Cooler Master MM731", QueryDevices.CoolerMaster.Parse(0x2516, 0x01B7, R(20, (0, 0x47), (1, 0x81), (2, 0x70), (10, 50)))!.Name); Assert.True(QueryDevices.CoolerMaster.Parse(0x2516, 0x0169, R(20, (0, 0x47), (1, 0x81), (10, 50), (12, 0)))!.Cable);
        Assert.Null(QueryDevices.CoolerMaster.Parse(0x2516, 0x01B7, R(20, (0, 0x44), (1, 0x01)))); Assert.Null(QueryDevices.CoolerMaster.Parse(0x2516, 0x01B7, R(8, (0, 0x47), (1, 0x81)))); Assert.Null(QueryDevices.CoolerMaster.Parse(0x2516, 0x01B7, R(20, (0, 0x47), (1, 0x81), (10, 0)))!.Percent);
    }

    [Fact] public void CHERRY_sends_one_checked_frame_and_reads_percent_and_usb_power()
    {
        var q = QueryDevices.CherryQuery(); Assert.Equal(64, q.Length); Assert.Equal((byte)0x04, q[0]); Assert.Equal((byte)0x1A, q[3]); int sum = 0; for (int i = 3; i < 64; i++) sum += q[i]; Assert.Equal(sum & 0xFFFF, q[1] | (q[2] << 8));
        var ok = QueryDevices.Cherry.Parse(0x046A, 0x00EA, R(64, (0, 4), (3, 0x1A), (8, 73), (9, 1)))!; Assert.Equal(73, ok.Percent); Assert.True(ok.Charging); Assert.False(ok.Cable); Assert.Equal("cherry:mx30s", ok.Key);
        Assert.True(QueryDevices.Cherry.Parse(0x046A, 0x00E3, R(64, (0, 4), (3, 0x1A), (8, 50)))!.Cable); Assert.Null(QueryDevices.Cherry.Parse(0x046A, 0x00EA, R(64, (0, 4), (3, 0x1A), (8, 200)))); Assert.Null(QueryDevices.Cherry.Parse(0x046A, 0x00EA, R(64, (0, 4), (3, 0x1B), (8, 50))));
    }

    [Fact] public void NZXT_skips_the_ack_and_refuses_values_that_are_not_a_percentage()
    {
        var good = R(30, (0, 0x4E), (1, 0x02), (2, 0x97), (17, 88), (22, 1)); var r = QueryDevices.Nzxt.Parse(0x1E71, 0x2101, good)!; Assert.Equal(88, r.Percent); Assert.True(r.Charging); Assert.Equal("nzxt:2101", r.Key);
        Assert.Null(QueryDevices.Nzxt.Parse(0x1E71, 0x2101, R(30, (0, 0x4E), (1, 0xE5)))); Assert.Null(QueryDevices.Nzxt.Parse(0x1E71, 0x2101, R(30, (0, 0x4E), (1, 0x02), (2, 0x97), (17, 0x2C), (18, 1))));
        Assert.True(QueryDevices.Nzxt.Parse(0x1E71, 0x2129, good)!.Cable);
    }

    [Fact] public void Endgame_reads_percent_when_awake_asleep_when_08_or_11_and_retries_when_busy()
    {
        Assert.Equal([0xA1, 0xB4, 0x00], QueryDevices.Endgame.Request(0x1970));
        Assert.Equal(55, QueryDevices.Endgame.Parse(0x3367, 0x1970, R(64, (0, 0xA1), (1, 1), (16, 55)))!.Percent); var asleep = QueryDevices.Endgame.Parse(0x3367, 0x1970, R(64, (0, 0xA1), (1, 0x08)))!; Assert.False(asleep.Connected); Assert.Null(asleep.Percent);
        Assert.Null(QueryDevices.Endgame.Parse(0x3367, 0x1970, R(64, (0, 0xA1), (1, 0x03)))); Assert.Null(QueryDevices.Endgame.Parse(0x3367, 0x1970, R(64, (0, 0xA1), (1, 0x01), (16, 150))));
    }

    [Fact] public void LAMZU_accepts_the_answer_with_or_without_the_report_id_and_names_models_and_cables()
    {
        Assert.Equal([0, 0, 0, 2, 2, 0, 0x83], QueryDevices.Lamzu.Request(0x0042));
        var withId = QueryDevices.Lamzu.Parse(0x37B0, 0x0042, R(65, (1, 0xA1), (4, 2), (6, 0x83), (7, 1), (8, 70)))!; Assert.Equal(70, withId.Percent); Assert.True(withId.Charging); Assert.Equal("LAMZU Orcus", withId.Name); Assert.True(withId.Cable);
        var without = QueryDevices.Lamzu.Parse(0x37B0, 0x0043, R(65, (0, 0xA1), (3, 2), (5, 0x83), (6, 0), (7, 41)))!; Assert.Equal(41, without.Percent); Assert.False(without.Cable);
        var off = QueryDevices.Lamzu.Parse(0x37B0, 0x0043, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 0)))!; Assert.False(off.Connected); Assert.Null(off.Percent);
        Assert.Null(QueryDevices.Lamzu.Parse(0x37B0, 0x0056, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 150)))); Assert.Null(QueryDevices.Lamzu.Parse(0x37B0, 0x0056, R(65))); Assert.Equal("LAMZU wireless mouse", QueryDevices.Lamzu.Parse(0x373E, 0x7777, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 50)))!.Name);
        Assert.True(QueryDevices.LamzuIsCable(0x373E, 0x001C)); Assert.False(QueryDevices.LamzuIsCable(0x373E, 0x001D)); Assert.Equal("LAMZU Maya X V2", QueryDevices.LamzuName(0x373E, 0x0016));
    }
}

public class QueryDevicesBatch2bTests
{
    private static byte[] R(int len, params (int I, byte V)[] set) { var b = new byte[len]; foreach (var (i, v) in set) b[i] = v; return b; }

    [Fact] public void Arbiter_shares_a_key_between_cable_and_receiver_and_tells_asleep_from_off()
    {
        var awake = QueryDevices.Arbiter.Parse(0x373E, 0x000A, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 66)))!; Assert.Equal("Arbiter Studio Akitsu", awake.Name); Assert.Equal(66, awake.Percent); Assert.False(awake.Cable); Assert.Equal("arbiter:373E:000A", awake.Key);
        var cable = QueryDevices.Arbiter.Parse(0x373E, 0x0009, R(65, (1, 0xA2), (4, 2), (6, 0x83), (7, 1), (8, 66)))!; Assert.True(cable.Cable); Assert.True(cable.Charging); Assert.Equal(awake.Key, cable.Key);
        Assert.False(QueryDevices.Arbiter.Parse(0x373E, 0x0039, R(65, (1, 0xA0), (4, 2), (6, 0x83)))!.Connected); Assert.False(QueryDevices.Arbiter.Parse(0x373E, 0x0067, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 0)))!.Connected);
        Assert.Equal("Arbiter Studio Akitsu Medium", QueryDevices.Arbiter.Parse(0x373E, 0x0067, R(65, (0, 0xA1), (3, 2), (5, 0x83), (7, 40)))!.Name);   // sin el id de informe
        Assert.Null(QueryDevices.Arbiter.Parse(0x373E, 0x0067, R(65, (1, 0xA5), (4, 2), (6, 0x83), (8, 40)))); Assert.Null(QueryDevices.Arbiter.Parse(0x373E, 0x7777, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 40))));   // modelo que no es Arbiter: lo lee LAMZU
        Assert.Contains((ushort)0x0066, QueryDevices.Arbiter.Pids!); Assert.True(QueryDevices.Specs.ToList().IndexOf(QueryDevices.Arbiter) < QueryDevices.Specs.ToList().IndexOf(QueryDevices.Lamzu));
    }

    [Fact] public void The_17_byte_frame_has_the_55_minus_sum_checksum_and_replies_are_verified_with_it()
    {
        var q = QueryDevices.Frame17(0x04); Assert.Equal(17, q.Length); Assert.Equal((byte)0x08, q[0]); Assert.Equal((byte)0x49, q[16]); Assert.True(QueryDevices.IsFrame17Reply(0x04, q));
        var r = R(17, (0, 8), (1, 4), (6, 72), (7, 1)); int sum = 0; for (int i = 0; i < 16; i++) sum += r[i]; r[16] = (byte)(0x55 - sum);
        var m = QueryDevices.KysonaDedicated.Parse(0x3554, 0xF58A, r)!; Assert.Equal(72, m.Percent); Assert.True(m.Charging); Assert.Equal("VXE Dragonfly R1", m.Name); Assert.False(m.Cable); Assert.True(QueryDevices.KysonaDedicated.Parse(0x3554, 0xF58C, r)!.Cable);
        r[16] ^= 1; Assert.Null(QueryDevices.KysonaDedicated.Parse(0x3554, 0xF58A, r)); Assert.False(QueryDevices.IsFrame17Reply(0x03, q)); Assert.Equal("Redragon wireless mouse", QueryDevices.Frame17Name(0x3554, 0xF55D)); Assert.Equal("Hitscan Hyperlight", QueryDevices.Frame17Name(0x3770, 0x0200));
        Assert.Contains((ushort)0x11CA, QueryDevices.KysonaDedicated.Exclude!);   // los auriculares ATK no reciben tramas de ratón
        Assert.True(QueryDevices.KysonaVendor.Collection.Matches(0xFF02, 2, -1, 64, 64, 0)); Assert.False(QueryDevices.KysonaVendor.Collection.Matches(0x0001, 2, -1, 64, 64, 0)); Assert.False(QueryDevices.KysonaVendor.Collection.Matches(0xFF02, 2, -1, 8, 8, 0));
    }

    [Fact] public void Pulsar_sends_08_81_01_with_a_16_bit_sum_and_waits_for_the_answered_state()
    {
        var q = QueryDevices.PulsarRequest(); Assert.Equal(65, q.Length); Assert.Equal([0x08, 0x81, 0x01], [q[2], q[3], q[4]]); Assert.Equal(0x08 + 0x81 + 0x01, q[63] | (q[64] << 8));
        var ok = QueryDevices.Pulsar.Parse(0x3710, 0x3402, R(65, (1, 1), (2, 8), (3, 0x81), (4, 1), (7, 55)))!; Assert.Equal(55, ok.Percent); Assert.True(ok.Cable); Assert.True(ok.Charging); Assert.Equal("Pulsar X2 V3", ok.Name);
        var dongle = QueryDevices.Pulsar.Parse(0x3710, 0x5402, R(65, (1, 1), (2, 8), (3, 0x81), (4, 1), (7, 0)))!; Assert.False(dongle.Connected); Assert.Null(dongle.Percent); Assert.Equal("Pulsar wireless mouse", dongle.Name);
        Assert.Null(QueryDevices.Pulsar.Parse(0x3710, 0x5402, R(65, (1, 2), (2, 8), (3, 0x81), (4, 1), (7, 55)))); Assert.Null(QueryDevices.Pulsar.Parse(0x3710, 0x5402, R(65, (1, 5), (2, 8), (3, 0x81), (4, 1), (7, 55))));   // pendiente
        Assert.Null(QueryDevices.Pulsar.Parse(0x3710, 0x5402, R(65, (1, 1), (2, 8), (3, 0x81), (4, 1), (7, 0xFF)))); Assert.True(QueryDevices.Pulsar.HighInterfaceFirst);
    }

    [Fact] public void ATK_NK_asks_for_the_battery_with_a_sum_over_bytes_2_to_63_and_reads_one_byte()
    {
        var q = QueryDevices.AtkNkQuery(0x07); Assert.Equal(64, q.Length); Assert.Equal([0x08, 0x72, 0x02, 0, 0x01, 0x07, 0x01], [q[0], q[2], q[3], q[4], q[5], q[6], q[7]]); int sum = 0; for (int i = 2; i < 64; i++) sum += q[i]; Assert.Equal((byte)sum, q[1]);
        Assert.Equal(81, QueryDevices.AtkNk.Parse(0x373B, 0x1216, R(64, (0, 8), (1, 0x72), (5, 7), (7, 81)))!.Percent); Assert.Null(QueryDevices.AtkNk.Parse(0x373B, 0x1216, R(64, (0, 8), (1, 0x72), (2, 0xFF), (5, 7), (7, 81)))); Assert.Null(QueryDevices.AtkNk.Parse(0x373B, 0x1216, R(64, (0, 8), (1, 0x72), (5, 0x0F), (7, 1))));
        Assert.Null(QueryDevices.AtkNk.Parse(0x373B, 0x1216, R(64, (0, 8), (1, 0x72), (5, 7), (7, 0))));   // 0 = dormido: sin lectura
    }
}

public class AsusRogModelsTests
{
    private static byte[] R(int len, params (int I, byte V)[] set) { var b = new byte[len]; foreach (var (i, v) in set) b[i] = v; return b; }
    private static AsusRogModels.Model M(ushort pid, byte rid = 0) => AsusRogModels.Models.First(m => m.Pid == pid && m.ReportId == rid);

    [Fact] public void The_table_has_every_known_model_with_its_own_offsets_and_the_two_measured_ones_are_there()
    {
        Assert.True(AsusRogModels.Models.Count >= 60); Assert.Equal("ROG Strix Scope II 96", M(0x1AB0, 2).Name); Assert.Equal("ROG Keris II Origin", M(0x1C0E, 3).Name);
        Assert.Equal(11, M(0x193E).Offset); Assert.Equal(25, M(0x18E5).Scale); Assert.Equal(7, M(0x18B4).Offset); Assert.Equal(new byte[] { 2, 0x12, 0x01 }, AsusRogModels.Request(M(0x1AB0, 2))); Assert.Equal(new byte[] { 3, 0x12, 0x07 }, AsusRogModels.Request(M(0x1C0E, 3)));
        Assert.All(AsusRogModels.Models.Where(m => m.ReportId != 0), m => Assert.NotNull(m.Col));   // los del Omni se reconocen por su colección
    }

    [Fact] public void A_keyboard_answer_gives_percent_and_charging_and_Falchion_reads_byte_11()
    {
        var r = AsusRogModels.Parse(M(0x1AB0, 2), R(64, (0, 2), (1, 0x12), (2, 0x01), (6, 60), (9, 1)))!; Assert.Equal(60, r.Percent); Assert.True(r.Charging);
        Assert.Equal(44, AsusRogModels.Parse(M(0x193E), R(64, (1, 0x12), (2, 0x01), (6, 99), (11, 44)))!.Percent); Assert.Null(AsusRogModels.Parse(M(0x1AB0, 2), R(64, (1, 0x12), (2, 0x01), (6, 200)))!.Percent);
    }

    [Fact] public void A_mouse_answer_scales_old_gauges_and_treats_zero_as_no_data()
    {
        Assert.Equal(97, AsusRogModels.Parse(M(0x1C0E, 3), R(64, (0, 3), (1, 0x12), (2, 0x07), (5, 97), (10, 0)))!.Percent);
        var old = AsusRogModels.Parse(M(0x18E5), R(64, (1, 0x12), (2, 0x07), (5, 3), (10, 1)))!; Assert.Equal(75, old.Percent); Assert.True(old.Charging);   // medidor antiguo de 0 a 4: ×25
        Assert.Equal(AsusRogModels.Answer.Asleep, AsusRogModels.Parse(M(0x1C0E, 3), R(64, (1, 0x12), (2, 0x07), (5, 0)))!.Answer); Assert.Null(AsusRogModels.Parse(M(0x1C0E, 3), R(64, (1, 0x12), (2, 0x07), (5, 200)))!.Percent);
    }

    [Fact] public void FF_AA_means_asleep_and_foreign_or_short_answers_are_ignored()
    {
        Assert.Equal(AsusRogModels.Answer.Asleep, AsusRogModels.Parse(M(0x1AB0, 2), R(64, (1, 0xFF), (2, 0xAA)))!.Answer);
        Assert.Null(AsusRogModels.Parse(M(0x1AB0, 2), R(64, (1, 0x12), (2, 0x07), (6, 60)))); Assert.Null(AsusRogModels.Parse(M(0x1AB0, 2), R(4))); Assert.Null(AsusRogModels.Parse(M(0x1AB0, 2), null));
    }

    [Fact] public void The_omni_pairing_list_is_read_in_four_byte_slots_until_a_zero()
    {
        var r = R(64, (0, 1), (1, 0xA0), (2, 0x00), (5, 0xB0), (6, 0x1A), (9, 0x0E), (10, 0x1C)); Assert.True(AsusRogModels.IsPairingReply(r));
        Assert.Equal(new ushort[] { 0x1AB0, 0x1C0E }, AsusRogModels.ParsePairing(r)); Assert.Empty(AsusRogModels.ParsePairing(R(64, (1, 0xA0)))); Assert.False(AsusRogModels.IsPairingReply(R(64, (1, 0x12))));
        Assert.Equal(new byte[] { 1, 0xA0, 0 }, AsusRogModels.PairingRequest);
    }
}

public class QueryDevicesBatch3Tests
{
    private static byte[] R(int len, params (int I, byte V)[] set) { var b = new byte[len]; foreach (var (i, v) in set) b[i] = v; return b; }

    [Fact] public void WLMouse_cable_and_receiver_share_one_key_and_asleep_is_told_apart()
    {
        var rx = QueryDevices.WlMouse.Parse(0x36A7, 0xA878, R(65, (1, 0xA1), (3, 2), (4, 2), (6, 0x83), (7, 1), (8, 57)))!; Assert.Equal("WLMouse Sword X 8K", rx.Name); Assert.Equal(57, rx.Percent); Assert.True(rx.Charging); Assert.False(rx.Cable);
        var cable = QueryDevices.WlMouse.Parse(0x36A7, 0xA879, R(65, (1, 0xA2), (3, 2), (4, 2), (6, 0x83), (8, 57)))!; Assert.True(cable.Cable); Assert.Equal(rx.Key, cable.Key);
        Assert.False(QueryDevices.WlMouse.Parse(0x36A7, 0xA883, R(65, (1, 0xA0), (3, 2), (4, 2), (6, 0x83)))!.Connected);
        Assert.Null(QueryDevices.WlMouse.Parse(0x36A7, 0xA883, R(65, (1, 0xA1), (3, 0), (4, 2), (6, 0x83), (8, 50)))); Assert.Null(QueryDevices.WlMouse.Parse(0x36A7, 0xA883, R(65, (1, 0xA1), (3, 2), (4, 2), (6, 0x83), (8, 150)))); Assert.Null(QueryDevices.WlMouse.Parse(0x36A7, 0x9999, R(65, (1, 0xA1), (3, 2), (4, 2), (6, 0x83), (8, 50))));
    }

    [Fact] public void VAXEE_reads_steps_of_five_percent_and_refuses_values_above_twenty()
    {
        Assert.Equal([0x0E, 0xA5, 0x0B, 0x01, 0x01], QueryDevices.Vaxee.Request(0x1002));
        var r = QueryDevices.Vaxee.Parse(0x3057, 0x1002, R(64, (0, 0x0E), (1, 0xA5), (2, 0x0B), (4, 1), (5, 13)))!; Assert.Equal(65, r.Percent); Assert.Equal("VAXEE wireless mouse", r.Name); Assert.True(QueryDevices.Vaxee.Parse(0x3057, 0x1011, R(64, (0, 0x0E), (1, 0xA5), (2, 0x0B), (4, 1), (5, 20)))!.Cable);
        Assert.Null(QueryDevices.Vaxee.Parse(0x3057, 0x1002, R(64, (0, 0x0E), (1, 0xA5), (2, 0x0B), (4, 1), (5, 21)))); Assert.Null(QueryDevices.Vaxee.Parse(0x3057, 0x1002, R(64, (0, 0x0E), (1, 0xA5), (2, 0x10), (4, 1), (5, 1)))); Assert.Equal("VAXEE 8K Wireless", QueryDevices.Vaxee.Parse(0x3057, 0x2002, R(64, (0, 0x0E), (1, 0xA5), (2, 0x0B), (4, 1), (5, 4)))!.Name);
    }

    [Fact] public void Areson_checks_the_whole_frame_sums_to_55_and_scales_the_level_from_0_10()
    {
        var r = R(17, (0, 9), (1, 4), (6, 8), (7, 1)); r[16] = (byte)(0x55 - (9 + 4 + 8 + 1)); var m = QueryDevices.Areson.Parse(0x25A7, 0xFA07, r)!; Assert.Equal(80, m.Percent); Assert.True(m.Charging);
        r[16] ^= 1; Assert.Null(QueryDevices.Areson.Parse(0x25A7, 0xFA07, r)); Assert.Equal(QueryDevices.Transport.FeatureThenInput, QueryDevices.Areson.Transport); Assert.NotNull(QueryDevices.Areson.ReplyCollection);
        var bad = R(17, (0, 9), (1, 4), (6, 11)); bad[16] = (byte)(0x55 - (9 + 4 + 11)); Assert.Null(QueryDevices.Areson.Parse(0x25A7, 0xFA07, bad));
    }

    [Fact] public void DAREU_reads_the_percent_directly_and_the_cable_is_told_apart()
    {
        Assert.Equal((byte)0x49, QueryDevices.Frame17(0x04)[16]);   // la misma trama que manda el fabricante: 0x55 − (08 + 04)
        var r = R(17, (0, 9), (1, 4), (6, 91), (7, 1)); var d = QueryDevices.Dareu.Parse(0x260D, 0x1074, r)!; Assert.Equal(91, d.Percent); Assert.True(d.Charging); Assert.False(d.Cable); Assert.True(QueryDevices.Dareu.Parse(0x260D, 0x1090, r)!.Cable);
        Assert.Null(QueryDevices.Dareu.Parse(0x260D, 0x1074, R(17, (0, 9), (1, 4), (6, 120)))); Assert.Null(QueryDevices.Dareu.Parse(0x260D, 0x1074, R(16, (0, 9), (1, 4), (6, 50)))); Assert.Equal(19, QueryDevices.Specs.Count);
    }
}

public class GamepadTests
{
    private static byte[] R(int len, params (int I, byte V)[] set) { var b = new byte[len]; foreach (var (i, v) in set) b[i] = v; return b; }

    [Fact] public void DualShock_4_reads_level_cable_and_charging_and_skips_the_reduced_bluetooth_report()
    {
        var usb = QueryDevices.PlayStationParse(0x054C, 0x09CC, R(64, (0, 0x01), (30, 0x08)))!; Assert.Equal(85, usb.Percent); Assert.False(usb.Charging); Assert.True(usb.Cable); Assert.Equal("DualShock 4", usb.Name);
        var cable = QueryDevices.PlayStationParse(0x054C, 0x09CC, R(64, (0, 0x01), (30, 0x10 | 0x06)))!; Assert.Equal(60, cable.Percent); Assert.True(cable.Charging);
        Assert.Equal(100, QueryDevices.PlayStationParse(0x054C, 0x09CC, R(64, (0, 0x01), (30, 0x10 | 0x0B)))!.Percent); Assert.False(QueryDevices.PlayStationParse(0x054C, 0x09CC, R(64, (0, 0x01), (30, 0x10 | 0x0B)))!.Charging);
        var bt = QueryDevices.PlayStationParse(0x054C, 0x09CC, R(78, (0, 0x11), (32, 0x04)))!; Assert.Equal(45, bt.Percent); Assert.False(bt.Cable);
        Assert.Null(QueryDevices.PlayStationParse(0x054C, 0x09CC, R(78, (0, 0x01), (30, 0x08)))); Assert.Null(QueryDevices.PlayStationParse(0x054C, 0x0BA0, R(64, (0, 0x01), (30, 0)))); Assert.Null(QueryDevices.PlayStationParse(0x054C, 0x09CC, R(10, (0, 0x01))));
    }

    [Fact] public void DualSense_reads_level_state_and_full_charge()
    {
        var d = QueryDevices.PlayStationParse(0x054C, 0x0CE6, R(64, (0, 0x01), (53, 0x07)))!; Assert.Equal(75, d.Percent); Assert.False(d.Charging); Assert.Equal("DualSense", d.Name);
        var ch = QueryDevices.PlayStationParse(0x054C, 0x0CE6, R(64, (0, 0x01), (53, 0x15)))!; Assert.True(ch.Charging); Assert.Equal(55, ch.Percent);
        var full = QueryDevices.PlayStationParse(0x054C, 0x0DF2, R(64, (0, 0x01), (53, 0x2A)))!; Assert.Equal(100, full.Percent); Assert.True(full.Charging); Assert.Equal("DualSense Edge", full.Name);
        Assert.Equal(25, QueryDevices.PlayStationParse(0x054C, 0x0CE6, R(78, (0, 0x31), (54, 0x02)))!.Percent); Assert.Null(QueryDevices.PlayStationParse(0x054C, 0x0CE6, R(78, (0, 0x01), (53, 0x07))));   // informe 01 largo = Bluetooth reducido
        Assert.Equal(QueryDevices.Transport.Listen, QueryDevices.PlayStation.Transport); Assert.Equal((byte?)0x05, QueryDevices.PlayStation.PrimeFeatureReport);
    }

    [Fact] public void Switch_Pro_reads_five_levels_from_the_full_report_30()
    {
        var r = QueryDevices.SwitchPro.Parse(0x057E, 0x2009, R(13, (0, 0x30), (2, 0x80)))!; Assert.Equal(100, r.Percent); Assert.False(r.Charging);
        var ch = QueryDevices.SwitchPro.Parse(0x057E, 0x2009, R(13, (0, 0x30), (2, 0x50)))!; Assert.Equal(50, ch.Percent); Assert.True(ch.Charging);
        Assert.Null(QueryDevices.SwitchPro.Parse(0x057E, 0x2009, R(13, (0, 0x21), (2, 0x80)))); Assert.Null(QueryDevices.SwitchPro.Parse(0x057E, 0x2009, R(13, (0, 0x30), (2, 0xE0)))); Assert.Null(QueryDevices.SwitchPro.Parse(0x057E, 0x2009, R(8, (0, 0x30))));
    }

    [Fact] public void GameSir_Nova_and_Cyclone_2_read_their_vendor_replies()
    {
        Assert.Equal([0x00, 0x01, 0x01], QueryDevices.GameSirNova.Request(0x1098)); var nova = QueryDevices.GameSirNova.Parse(0x3537, 0x1098, R(64, (0, 1), (18, 77)))!; Assert.Equal(77, nova.Percent); Assert.False(nova.Charging); Assert.False(nova.Cable);
        var ch = QueryDevices.GameSirNova.Parse(0x3537, 0x100F, R(64, (0, 1), (18, 0xFF)))!; Assert.Null(ch.Percent); Assert.True(ch.Charging); Assert.True(ch.Cable);
        Assert.Null(QueryDevices.GameSirNova.Parse(0x3537, 0x1098, R(64, (0, 0xF4), (18, 50)))); Assert.Null(QueryDevices.GameSirNova.Parse(0x3537, 0x1098, R(64, (0, 1), (18, 150))));
        var cy = QueryDevices.GameSirCyclone2.Parse(0x3537, 0x100B, R(40, (0, 0x12), (35, 1), (36, 63)))!; Assert.Equal(63, cy.Percent); Assert.True(cy.Charging); Assert.Null(QueryDevices.GameSirCyclone2.Parse(0x3537, 0x100B, R(40, (0, 0x10), (36, 63)))); Assert.Equal([0x0F, 0x03], QueryDevices.GameSirCyclone2.Request(0x100B));
    }

    [Fact] public void BEITONG_nibble_cipher_round_trips_and_the_reply_is_decoded()
    {
        for (int x = 0; x < 256; x++) Assert.Equal((byte)x, QueryDevices.BeitongDecode(QueryDevices.BeitongEncode((byte)x)));
        Assert.Equal((byte)0xAD, QueryDevices.BeitongEncode(0x15)); Assert.Equal([0x02, 0xAD], QueryDevices.Beitong.Request(0x5128));
        var r = R(64, (0, 2), (1, QueryDevices.BeitongEncode(0x15)), (2, QueryDevices.BeitongEncode(88)), (4, QueryDevices.BeitongEncode(1))); var b = QueryDevices.Beitong.Parse(0x20BC, 0x5128, r)!; Assert.Equal(88, b.Percent); Assert.True(b.Charging);
        Assert.Null(QueryDevices.Beitong.Parse(0x20BC, 0x5128, R(64, (0, 2), (1, QueryDevices.BeitongEncode(0x25)), (2, QueryDevices.BeitongEncode(88))))); Assert.Null(QueryDevices.Beitong.Parse(0x20BC, 0x5128, R(64, (0, 2), (1, QueryDevices.BeitongEncode(0x15)), (2, QueryDevices.BeitongEncode(150)))));
    }

    [Fact] public void Xbox_battery_uses_real_capacity_or_the_four_coarse_xinput_levels()
    {
        Assert.Equal(50, XboxBattery.WgiPercent(2000, 4000)); Assert.Equal(100, XboxBattery.WgiPercent(5000, 4000)); Assert.Null(XboxBattery.WgiPercent(2000, 0)); Assert.Null(XboxBattery.WgiPercent(null, 4000)); Assert.Null(XboxBattery.WgiPercent(2000, null));
        Assert.Equal((5, "Vacía"), XboxBattery.XInputLevel(0)); Assert.Equal((25, "Baja"), XboxBattery.XInputLevel(1)); Assert.Equal((60, "Media"), XboxBattery.XInputLevel(2)); Assert.Equal((100, "Llena"), XboxBattery.XInputLevel(3));
        Assert.False(XboxBattery.IsBatteryType(0)); Assert.False(XboxBattery.IsBatteryType(1)); Assert.True(XboxBattery.IsBatteryType(2)); Assert.True(XboxBattery.IsBatteryType(3));
    }
}
