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
        Assert.Equal(5, QueryDevices.Specs.Count);
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
        var off = QueryDevices.Lamzu.Parse(0x37B0, 0x0056, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 0)))!; Assert.False(off.Connected); Assert.Null(off.Percent);
        Assert.Null(QueryDevices.Lamzu.Parse(0x37B0, 0x0056, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 150)))); Assert.Null(QueryDevices.Lamzu.Parse(0x37B0, 0x0056, R(65))); Assert.Equal("LAMZU wireless mouse", QueryDevices.Lamzu.Parse(0x373E, 0x7777, R(65, (1, 0xA1), (4, 2), (6, 0x83), (8, 50)))!.Name);
        Assert.True(QueryDevices.LamzuIsCable(0x373E, 0x001C)); Assert.False(QueryDevices.LamzuIsCable(0x373E, 0x001D)); Assert.Equal("LAMZU Maya X V2", QueryDevices.LamzuName(0x373E, 0x0016));
    }
}
