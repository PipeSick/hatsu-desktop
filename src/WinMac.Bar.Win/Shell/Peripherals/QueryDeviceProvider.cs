using System.Text.RegularExpressions;
using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Lector único de los aparatos que se leen con una consulta y una respuesta (Cooler Master, CHERRY, NZXT, Endgame Gear, LAMZU; docs/66). <b>SIN VERIFICAR con aparatos reales.</b>
/// Solo consultas de lectura y solo si el usuario activó «más marcas». Sin respuesta, el aparato no se muestra; nunca se inventa un porcentaje.
/// </summary>
internal sealed class QueryDeviceProvider : IPeripheralProvider
{
    private readonly IReadOnlyList<QueryDevices.Spec> _specs;
    public QueryDeviceProvider(IReadOnlyList<QueryDevices.Spec>? specs = null) { _specs = specs ?? QueryDevices.Specs; }
    public string Id => "vendors-query";
    public string DisplayName => string.Join(", ", _specs.Select(s => s.Brand));
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var found = new Dictionary<string, QueryDevices.Reading>(); var order = new List<string>();
        var all = HidInterop.Scan(v => _specs.Any(s => s.Vendors.Contains(v)));
        foreach (var spec in _specs)
            foreach (var vendor in spec.Vendors)
            {
                var pids = spec.Pids ?? all.Where(i => i.Vendor == vendor).Select(i => i.Product).Distinct().ToArray();
                foreach (var pid in pids)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var i in all.Where(i => i.Vendor == vendor && i.Product == pid && spec.Collection.Matches(i.UsagePage, i.Usage, InterfaceOf(i.Path), i.OutputLength, i.InputLength, i.FeatureLength)))
                    {
                        var r = spec.Transport == QueryDevices.Transport.Report ? AskReport(i, spec, ct) : AskFeature(i, spec, ct); if (r is null) continue;
                        if (!found.TryGetValue(r.Key, out var prev)) { found[r.Key] = r; order.Add(r.Key); }
                        else if (r.Cable && !prev.Cable) found[r.Key] = r;   // el mismo aparato por cable y por receptor: gana el cable (es el que carga)
                        break;
                    }
                }
            }

        return order.Select(k => found[k]).Select(r => new RawReading(r.Key, r.Name, r.Kind, r.Cable ? ConnectionKind.Usb : ConnectionKind.Dongle, r.Connected, r.Percent,
            r.Percent is null ? ChargeState.Unknown : r.Charging ? ChargeState.Charging : ChargeState.NotCharging, r.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported,
            r.Connected ? "Lectura por el protocolo del fabricante (sin verificar con tu equipo)." : "Apagado, dormido o fuera de alcance.")).ToList();
    }, ct);

    private static int InterfaceOf(string path) { var m = Regex.Match(path, @"mi_([0-9a-f]{2})", RegexOptions.IgnoreCase); return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : -1; }
    private sealed record Box(QueryDevices.Reading R);

    private static QueryDevices.Reading? AskReport(HidIface i, QueryDevices.Spec spec, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            var req = spec.Request(i.Product); var frame = new byte[Math.Max((int)i.OutputLength, 1)]; Array.Copy(req, frame, Math.Min(req.Length, frame.Length));
            return HidInterop.Transact(h, frame, i.InputLength, spec.TimeoutMs, b => spec.Parse(i.Vendor, i.Product, SteelSeriesProtocol.Payload(b)) is { } r ? new Box(r) : null, ct)?.R;
        }
        finally { HidInterop.Close(h); }
    }

    private static QueryDevices.Reading? AskFeature(HidIface i, QueryDevices.Spec spec, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenFeature(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            var req = spec.Request(i.Product); var frame = new byte[i.FeatureLength]; Array.Copy(req, frame, Math.Min(req.Length, frame.Length));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!HidInterop.SetFeature(h, frame)) return null;
                ct.WaitHandle.WaitOne(spec.DelayMs + attempt * 100); ct.ThrowIfCancellationRequested();
                var buf = HidInterop.GetFeature(h, i.FeatureLength, frame[0]); if (buf is null) return null;
                if (spec.Parse(i.Vendor, i.Product, buf) is { } r) return r;
            }

            return null;
        }
        finally { HidInterop.Close(h); }
    }
}
