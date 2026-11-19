using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de auriculares HyperX y ratones Pulsefire Haste 2 por sus protocolos propios (docs/66). <b>SIN VERIFICAR con aparatos reales.</b>
/// Solo consultas de lectura y solo si el usuario activó «más marcas». Sin respuesta, el aparato no se muestra; nunca se inventa un porcentaje.
/// </summary>
internal sealed class HyperXProvider : IPeripheralProvider
{
    public string Id => "vendors-hyperx";
    public string DisplayName => "HyperX";
    public SupportLevel Support => SupportLevel.Experimental;

    // Pulsefire Haste 2: tras encender, durante ~5 s dice 40 % en vez de su nivel. El primer sondeo de la sesión supone que lleva rato encendido.
    private static long _linkUpAt; private static bool _polled;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var all = HidInterop.Scan(v => v is HyperXProtocol.Hp or HyperXProtocol.Kingston); var done = new HashSet<(ushort, ushort)>();
        foreach (var m in HyperXProtocol.Models)
            foreach (var pid in m.Pids)
            {
                ct.ThrowIfCancellationRequested(); if (done.Contains((m.Vendor, pid))) continue;
                foreach (var i in all.Where(i => i.Vendor == m.Vendor && i.Product == pid && m.Collection.Matches(i.UsagePage, i.Usage, i.OutputLength, i.InputLength, i.FeatureLength)))
                {
                    var r = Run(i, m.Family, ct); if (r is null) continue;
                    done.Add((m.Vendor, pid));
                    res.Add(new RawReading("hyperx:" + pid.ToString("X4"), r.Name, r.Kind, ConnectionKind.Dongle, r.Connected, r.Percent, r.Percent is null ? ChargeState.Unknown : r.Charging ? ChargeState.Charging : ChargeState.NotCharging,
                        r.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported, r.Connected ? "Lectura por el protocolo de HyperX (sin verificar)." : "Apagado o fuera de alcance."));
                    break;
                }
            }

        return res;
    }, ct);

    private static HyperXProtocol.Reading? Run(HidIface i, HyperXProtocol.Family f, CancellationToken ct)
    {
        if (f == HyperXProtocol.Family.StingerCore) return RunStinger(i, ct);
        IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            int timeout = f == HyperXProtocol.Family.Cloud3 ? 800 : 1000;
            byte[]? Q(HyperXProtocol.Ask a)
            {
                var frame = new byte[i.OutputLength]; Array.Copy(a.Request, frame, Math.Min(a.Request.Length, frame.Length));
                return HidInterop.Transact(h, frame, i.InputLength, timeout, b => { var p = SteelSeriesProtocol.Payload(b); return a.Accept(p) ? new Box(p) : null; }, ct)?.Bytes;
            }

            switch (f)
            {
                case HyperXProtocol.Family.Cloud3:
                {
                    var link = Q(HyperXProtocol.Cloud3(0x82)); if (link is null) return null;
                    return HyperXProtocol.Cloud3Reading(i.Product, link, link[2] != 0 ? Q(HyperXProtocol.Cloud3(0x8A)) : null, link[2] != 0 ? Q(HyperXProtocol.Cloud3(0x89)) : null);
                }
                case HyperXProtocol.Family.Cloud2:
                case HyperXProtocol.Family.Cloud2Kingston:
                {
                    bool king = f == HyperXProtocol.Family.Cloud2Kingston; var bat = Q(king ? HyperXProtocol.Cloud2Kingston(0x02) : HyperXProtocol.Cloud2(0x02)); if (bat is null) return null;
                    return HyperXProtocol.Cloud2Reading("HyperX Cloud II Wireless", bat, Q(king ? HyperXProtocol.Cloud2Kingston(0x03) : HyperXProtocol.Cloud2(0x03)), !king);
                }
                case HyperXProtocol.Family.CloudAlpha:
                {
                    var link = Q(HyperXProtocol.CloudAlpha(0x03)); if (link is null) return null;
                    return link[3] == 0x01 ? HyperXProtocol.CloudAlphaReading(link, null, null) : HyperXProtocol.CloudAlphaReading(link, Q(HyperXProtocol.CloudAlpha(0x0C)), Q(HyperXProtocol.CloudAlpha(0x0B)));
                }
                case HyperXProtocol.Family.CloudAlpha2:
                {
                    var link = Q(HyperXProtocol.Pair(0x52, 0x01)); if (link is null) return null;
                    return HyperXProtocol.CloudAlpha2Reading(link, link[2] != 0 ? Q(HyperXProtocol.Pair(0x50, 0x02)) : null);
                }
                case HyperXProtocol.Family.Haste:
                {
                    // tras reconectar el receptor inunda el canal con informes ajenos y la respuesta del enlace puede perderse: sin ella, decide la de la batería
                    var link = Q(HyperXProtocol.Pair(0x52, 0x01)); bool linked = link is null || link[2] != 0;
                    var bat = linked ? Q(HyperXProtocol.Pair(0x50, 0x02)) : null; bool got = bat is { Length: >= 8 } && bat[2] <= 100;
                    if (link is null && !got) return null;
                    long now = Environment.TickCount64; if (!linked) _linkUpAt = 0; else if (_linkUpAt == 0) _linkUpAt = _polled ? now : now - HyperXProtocol.HasteSettleMs; _polled = true;
                    return HyperXProtocol.HasteReading(i.Product, linked, bat, now - _linkUpAt >= HyperXProtocol.HasteSettleMs);
                }
                case HyperXProtocol.Family.CloudFlight:
                    return Q(HyperXProtocol.Flight) is { } fl ? HyperXProtocol.FlightReading(fl) : null;
                case HyperXProtocol.Family.Cloud3S:
                {
                    var link = Q(HyperXProtocol.Cloud3S(0x02)); if (link is null) return null;
                    return HyperXProtocol.Cloud3SReading(link, link[6] == 2 ? Q(HyperXProtocol.Cloud3S(0x06)) : null, link[6] == 2 ? Q(HyperXProtocol.Cloud3S(0x48)) : null);
                }
            }

            return null;
        }
        finally { HidInterop.Close(h); }
    }

    private sealed record Box(byte[] Bytes);

    /// <summary>Cloud Stinger Core / Cloud Core DTS: se envía un informe de característica, se espera 20 ms y se lee el informe FF (con relecturas en los receptores de PlayStation).</summary>
    private static HyperXProtocol.Reading? RunStinger(HidIface i, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenFeature(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            bool ps = HyperXProtocol.NeedsRereads(i.Product);
            byte[]? Exchange(byte[] cmd, Func<byte[], bool> good, bool keepLast = false)
            {
                var buf = new byte[i.FeatureLength]; Array.Copy(cmd, buf, Math.Min(cmd.Length, buf.Length)); if (!HidInterop.SetFeature(h, buf)) return null;
                byte[]? last = null;
                for (int n = 0; n < (ps ? 10 : 1); n++)
                {
                    ct.WaitHandle.WaitOne(20); ct.ThrowIfCancellationRequested();
                    last = HidInterop.GetFeature(h, i.FeatureLength, 0xFF); if (last is null || last.Length < 13) return null;
                    if (!ps || good(last)) return last;
                }

                return keepLast ? last : null;   // el encabezado de la respuesta de conexión no está documentado: tras 10 lecturas con pinta de viejas se usa la última
            }

            var conn = Exchange(HyperXProtocol.StingerConnection, HyperXProtocol.IsFreshConnection, keepLast: true);
            if (conn is null) return null;
            var bat = conn.Length >= 12 && conn[11] == 0x01 ? Exchange(HyperXProtocol.StingerBattery, HyperXProtocol.IsStingerBattery) : null;
            return HyperXProtocol.StingerReading(i.Product, conn, bat);
        }
        finally { HidInterop.Close(h); }
    }
}
