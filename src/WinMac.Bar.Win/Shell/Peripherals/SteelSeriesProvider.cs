using System.Text.RegularExpressions;
using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de auriculares y ratones inalámbricos SteelSeries por su protocolo propio (docs/66). <b>SIN VERIFICAR con aparatos reales.</b>
/// Envía una consulta de solo lectura y lee la respuesta; solo si el usuario activó «más marcas». Sin respuesta, el aparato no se muestra; nunca se inventa un porcentaje.
/// </summary>
internal sealed class SteelSeriesProvider : IPeripheralProvider
{
    public string Id => "vendors-steelseries";
    public string DisplayName => "SteelSeries";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var all = HidInterop.Scan(v => v == SteelSeriesProtocol.Vendor); var done = new HashSet<ushort>();
        foreach (var pid in all.Select(i => i.Product).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            if (SteelSeriesProtocol.Find(pid) is not { } model) continue;
            var where = SteelSeriesProtocol.CollectionFor(pid);
            foreach (var i in all.Where(i => i.Product == pid && Matches(i, where)))
            {
                if (Read(i, model.Family, ct) is not { } r) continue;
                done.Add(pid);
                string key = "steelseries:" + (pid is 0x172B or 0x1726 ? "172B" : pid.ToString("X4"));   // el Rival 650 es uno solo, por cable o por receptor
                var raw = new RawReading(key, r.Name, model.Family is SteelSeriesProtocol.Family.Aerox or SteelSeriesProtocol.Family.Rival or SteelSeriesProtocol.Family.RivalPro ? DeviceKind.Mouse : DeviceKind.Headset,
                    r.Via == "Cable USB" ? ConnectionKind.Usb : ConnectionKind.Dongle, r.Connected, r.Percent, r.Percent is null || !r.Connected ? ChargeState.Unknown : r.Charging ? ChargeState.Charging : ChargeState.NotCharging,
                    r.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported, r.Connected ? "Lectura por el protocolo de SteelSeries (sin verificar con tu equipo)." : "Apagado o fuera de alcance.");
                int dup = res.FindIndex(x => x.DeviceId == key);
                if (dup < 0) res.Add(raw); else if (r.Via == "Cable USB") res[dup] = raw;   // cargando por cable, se queda la lectura del cable
                break;
            }
        }

        return res;
    }, ct);

    private static bool Matches(HidIface i, SteelSeriesProtocol.Where w)
    {
        if (i.OutputLength == 0) return false;
        if (w.Interface >= 0) return InterfaceOf(i.Path) == w.Interface;
        return i.UsagePage == w.UsagePage && i.Usage == w.Usage;
    }

    private static int InterfaceOf(string path) { var m = Regex.Match(path, @"mi_([0-9a-f]{2})", RegexOptions.IgnoreCase); return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : -1; }

    private static SteelSeriesProtocol.Reading? Read(HidIface i, SteelSeriesProtocol.Family f, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            foreach (var req in SteelSeriesProtocol.StatusRequests(f, i.Product))
            {
                var r = Ask(h, i, req, b => SteelSeriesProtocol.Parse(f, i.Product, b), ct); if (r is null) continue;
                if (r.NeedsLevel)
                {
                    foreach (var lr in SteelSeriesProtocol.LevelRequests)
                        if (Ask(h, i, lr, b => SteelSeriesProtocol.ParseLevel(b) is { } p ? new Box(p) : null, ct) is { } lvl) return r with { Percent = lvl.Value, NeedsLevel = false };
                }

                return r;
            }

            return null;
        }
        finally { HidInterop.Close(h); }
    }

    private sealed record Box(int Value);

    private static T? Ask<T>(IntPtr h, HidIface i, byte[] request, Func<byte[], T?> parse, CancellationToken ct) where T : class
    {
        var frame = new byte[i.OutputLength]; Array.Copy(request, frame, Math.Min(request.Length, frame.Length));
        return HidInterop.Transact(h, frame, i.InputLength, SteelSeriesProtocol.TimeoutMs(SteelSeriesProtocol.Find(i.Product)!.Family), b => parse(SteelSeriesProtocol.Payload(b)), ct);
    }
}
