using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de auriculares, ratones y teclados inalámbricos Corsair por sus tres protocolos (docs/66). <b>SIN VERIFICAR con aparatos reales.</b>
/// Solo consultas de lectura y solo si el usuario activó «más marcas». Sin respuesta, el aparato no se muestra; nunca se inventa un porcentaje.
/// </summary>
internal sealed class CorsairProvider : IPeripheralProvider
{
    public string Id => "vendors-corsair";
    public string DisplayName => "Corsair";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var all = HidInterop.Scan(v => v == CorsairProtocol.Vendor);
        foreach (var pid in all.Select(i => i.Product).Distinct())
        {
            ct.ThrowIfCancellationRequested(); var mine = all.Where(i => i.Product == pid).ToList();
            if (CorsairProtocol.VoidPids.Contains(pid)) { foreach (var i in mine.Where(i => (i.UsagePage == 0xFFC5 || i.UsagePage == 0xFF00) && i.OutputLength > 0)) if (ReadVoid(i, ct) is { } v) { res.Add(v); break; } continue; }
            if (CorsairProtocol.CompxMice.TryGetValue(pid, out var cname)) { foreach (var i in mine.Where(i => i.UsagePage == 0xFF02 && i.Usage == 0x0002 && i.OutputLength >= 17 && i.InputLength >= 17)) if (ReadCompx(i, cname, ct, out var done) is { } c) { res.Add(c); break; } else if (done) break; continue; }
            bool known = CorsairProtocol.BragiPids.Contains(pid);
            foreach (var i in mine.Where(i => (i.OutputLength is 64 or 65) && (i.InputLength is 64 or 65) && (i.UsagePage == 0xFF42 || (known && i.UsagePage >= 0xFF00))))
            {
                var found = ReadBragi(i, ct); if (found.Count == 0) continue; res.AddRange(found); break;
            }
        }

        return res;
    }, ct);

    private static byte[] Frame(HidIface i, byte[] request) { var f = new byte[i.OutputLength]; Array.Copy(request, f, Math.Min(request.Length, f.Length)); return f; }
    private sealed record Box<T>(T Value);

    private static RawReading? ReadVoid(HidIface i, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            var r = HidInterop.Transact(h, Frame(i, CorsairProtocol.VoidRequest), i.InputLength, 1000, b => CorsairProtocol.ParseVoid(SteelSeriesProtocol.Payload(b)) is { } v ? new Box<CorsairProtocol.VoidReading>(v) : null, ct)?.Value; if (r is null) return null;
            return new RawReading("corsair:" + i.Product.ToString("X4"), i.ProductName.Length > 0 ? i.ProductName : "Corsair wireless headset", DeviceKind.Headset, ConnectionKind.Dongle, r.Connected, r.Percent,
                r.Percent is null ? ChargeState.Unknown : r.Charging ? ChargeState.Charging : ChargeState.NotCharging, r.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported, "Lectura por el protocolo de Corsair (sin verificar).");
        }
        finally { HidInterop.Close(h); }
    }

    private static RawReading? ReadCompx(HidIface i, string name, CancellationToken ct, out bool done)
    {
        done = false; IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            byte[]? Ask(byte cmd) => HidInterop.Transact(h, Frame(i, CorsairProtocol.CompxRequest(cmd)), i.InputLength, 500, b => CorsairProtocol.IsCompxReply(cmd, b) ? new Box<byte[]>(b) : null, ct)?.Value;
            bool answered = false, online = true; var st = Ask(0x03); if (st is not null) { answered = true; online = CorsairProtocol.CompxOnline(st); }
            if (!answered || online)
            {
                var b = Ask(0x04);
                if (b is not null) { done = true; var bat = CorsairProtocol.CompxParseBattery(b); return new RawReading("corsair:" + i.Product.ToString("X4"), name, DeviceKind.Mouse, ConnectionKind.Dongle, true, bat.Percent, bat.Percent is null ? ChargeState.Unknown : bat.Charging ? ChargeState.Charging : ChargeState.NotCharging, bat.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported, "Lectura por el protocolo de Corsair (sin verificar)."); }
                if (answered) { done = true; return new RawReading("corsair:" + i.Product.ToString("X4"), name, DeviceKind.Mouse, ConnectionKind.Dongle, true, null, ChargeState.Unknown, BatteryCapability.Unknown, "Conectado, pero no contestó la batería."); }
            }
            else done = true;   // el receptor dice que el ratón no está conectado: no se muestra
            return null;
        }
        finally { HidInterop.Close(h); }
    }

    private static List<RawReading> ReadBragi(HidIface i, CancellationToken ct)
    {
        var res = new List<RawReading>(); IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return res;
        try
        {
            bool numbered = i.OutputLength == 64; bool? plainMode = null;   // la primera forma de trama que contesta se recuerda para este aparato
            uint? Get(byte target, byte prop)
            {
                foreach (bool plain in plainMode is { } m ? new[] { m } : new[] { false, true })
                {
                    var a = HidInterop.Transact(h, Frame(i, CorsairProtocol.BragiRequest(plain, numbered, target, prop)), i.InputLength, 300, b => CorsairProtocol.ParseBragi(plain, target, SteelSeriesProtocol.Payload(b)), ct);
                    if (a is null) continue;
                    plainMode = plain; return a.Status == CorsairProtocol.BragiStatus.Value ? a.Value : null;
                }

                return null;
            }

            bool any = false;
            foreach (byte target in CorsairProtocol.BragiTargets)
            {
                if (target == 0x08 && any) break;
                uint? level = Get(target, CorsairProtocol.PropBattery);
                for (int retry = 0; retry < 2 && (level is null or 0 or > 1000) && level is not null; retry++) level = Get(target, CorsairProtocol.PropBattery);   // 0 = «aún no lo sabe»
                if (level is null || CorsairProtocol.BragiPercent(level.Value) is not { } pct) continue;
                uint? paired = target != 0x08 ? Get(target, CorsairProtocol.PropProductId) : null;
                string product = i.ProductName.Length > 0 ? i.ProductName : "Corsair wireless device"; var kind = CorsairProtocol.GuessKind(product, i.Product, paired ?? 0);
                if (paired is { } pp && CorsairProtocol.Paired.TryGetValue((ushort)pp, out var known)) product = known.Name; else if (kind == DeviceKind.Headset && product.Contains("Receiver", StringComparison.OrdinalIgnoreCase)) product = "Corsair wireless headset";
                bool charging = Get(target, CorsairProtocol.PropChargeState) is { } cs && CorsairProtocol.BragiCharging(cs);
                res.Add(new RawReading("corsair:" + i.Product.ToString("X4") + ":" + target, product, kind, target == 0x08 ? ConnectionKind.Usb : ConnectionKind.Dongle, true, pct, charging ? ChargeState.Charging : ChargeState.NotCharging, BatteryCapability.Supported, "Lectura por el protocolo de Corsair (sin verificar)."));
                any = true;
            }

            return res;
        }
        finally { HidInterop.Close(h); }
    }
}
