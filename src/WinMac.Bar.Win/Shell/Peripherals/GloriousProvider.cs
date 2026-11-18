using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de ratones inalámbricos Glorious, Hitscan, G-Wolves y WLMouse por su protocolo de informe de característica (docs/66). <b>SIN VERIFICAR con aparatos reales.</b>
/// Envía una consulta de 65 bytes y lee la respuesta; solo si el usuario activó «más marcas». Sin respuesta, el aparato no se muestra; nunca se inventa un porcentaje.
/// </summary>
internal sealed class GloriousProvider : IPeripheralProvider
{
    public string Id => "vendors-glorious";
    public string DisplayName => "Glorious, Hitscan, G-Wolves, WLMouse";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var done = new HashSet<(ushort, ushort)>();
        var ids = GloriousProtocol.Vendors.Select(v => v.Id).ToHashSet();
        foreach (var i in HidInterop.Scan(v => ids.Contains(v)).Where(i => i.FeatureLength == GloriousProtocol.ReportLength && (i.Vendor != 0x36A7 || (i.Product == 0xA874 && i.UsagePage == 0xFFFF && i.Usage == 0))))
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains((i.Vendor, i.Product))) continue;
            var v = GloriousProtocol.Vendors.First(x => x.Id == i.Vendor);
            var reply = Ask(i.Path, v.ReplyDelayMs, ct); if (reply is null) continue;
            done.Add((i.Vendor, i.Product));
            var known = GloriousProtocol.Identify(i.Vendor, i.Product);
            string name = known?.Name ?? (i.ProductName.Length > 0 ? i.ProductName : "Dispositivo inalámbrico");
            var kind = known is not null || !name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Mouse : DeviceKind.Keyboard;
            bool cable = known?.Cable ?? false, awake = reply.State == GloriousProtocol.State.Awake;
            res.Add(new RawReading(v.Key + i.Product.ToString("X4"), name, kind, cable ? ConnectionKind.Usb : ConnectionKind.Dongle, awake, reply.Percent,
                cable || reply.Charging ? ChargeState.Charging : awake ? ChargeState.NotCharging : ChargeState.Unknown, reply.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported,
                awake ? "Lectura por el protocolo del fabricante (sin verificar con tu equipo)." : "Dormido o despertando."));
        }

        return res;
    }, ct);

    private static GloriousProtocol.Reply? Ask(string path, int delayMs, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenFeature(path); if (h == IntPtr.Zero) return null;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!HidInterop.SetFeature(h, GloriousProtocol.Request())) return null;
                ct.WaitHandle.WaitOne(delayMs); ct.ThrowIfCancellationRequested();
                var r = GloriousProtocol.Parse(HidInterop.GetFeature(h, GloriousProtocol.ReportLength));
                if (r.State == GloriousProtocol.State.NotThisProtocol) return null;
                if (r.State == GloriousProtocol.State.WakingUp && attempt == 0) { ct.WaitHandle.WaitOne(400); continue; }   // despertando: se pregunta otra vez
                return r;
            }

            return null;
        }
        finally { HidInterop.Close(h); }
    }
}
