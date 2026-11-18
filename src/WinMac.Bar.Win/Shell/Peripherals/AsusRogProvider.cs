using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de periféricos ASUS ROG por el receptor ROG OMNI (docs/64). A diferencia de los otros lectores, este ENVÍA una consulta de 64 bytes al receptor (la misma que manda Armoury Crate,
/// observada en una captura del tráfico USB) y lee la respuesta. Por eso solo se crea si el usuario lo activó. Solo toca los canales del fabricante del receptor Omni (VID 0B05 / PID 1ACE),
/// nunca el teclado ni el ratón como tales. Sin respuesta en el plazo, el dispositivo se considera no disponible; nunca se inventa un porcentaje.
/// </summary>
internal sealed class AsusRogProvider : IPeripheralProvider
{
    private readonly string? _kbName, _mouseName, _hsName;
    public AsusRogProvider(string? keyboardName, string? mouseName, string? headsetName = null) { _hsName = string.IsNullOrWhiteSpace(headsetName) ? null : headsetName; _kbName = string.IsNullOrWhiteSpace(keyboardName) ? null : keyboardName; _mouseName = string.IsNullOrWhiteSpace(mouseName) ? null : mouseName; }
    public string Id => "asus-rog";
    public string DisplayName => "ASUS ROG (receptor 2,4 GHz)";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var paths = Enumerate(out var others, out var headsets);
        foreach (var q in AsusRogProtocol.OmniQueries)
        {
            ct.ThrowIfCancellationRequested();
            if (!paths.TryGetValue(q.UsagePage, out var path)) continue;             // el receptor no está conectado
            int? pct = Ask(path, q, ct);
            string name = (q.Role == AsusRogProtocol.Role.Keyboard ? _kbName : _mouseName) ?? AsusRogProtocol.DisplayName(q.Role);
            res.Add(new RawReading("asus-rog:" + q.Role, name, AsusRogProtocol.Kind(q.Role), ConnectionKind.Dongle, pct is not null, pct,
                ChargeState.Unknown, pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported, "Lectura por el canal del receptor ROG OMNI; modelo no identificado."));
        }

        // Auriculares ROG Pelta / Delta II: otro receptor, otro canal (FF00:0001, informe CC). Mismo principio: una consulta de solo lectura y la respuesta.
        foreach (var (pid, path) in headsets)
        {
            ct.ThrowIfCancellationRequested();
            string hsName = AsusRogProtocol.Headsets[pid]; var (pct, asleep, charging) = AskHeadset(path, ct);
            res.Add(new RawReading("asus-rog:hs:" + pid.ToString("X4"), _hsName ?? hsName, DeviceKind.Headset, ConnectionKind.Dongle, pct is not null, pct, charging is null ? ChargeState.Unknown : charging.Value ? ChargeState.Charging : ChargeState.NotCharging,
                pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported, asleep ? "Apagado o dormido." : "Lectura por el receptor del audífono (sin verificar con tu equipo)."));
        }

        // Otros receptores ASUS (por ejemplo, los de auriculares 2,4 GHz): se listan por su nombre para que se vea que Hatsu los reconoció, pero su batería aún no se puede leer.
        foreach (var (pid, prod) in others)
            res.Add(new RawReading("asus-rx:" + pid.ToString("X4"), prod, prod.Contains("HEADSET", StringComparison.OrdinalIgnoreCase) || prod.Contains("DELTA", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Headset : DeviceKind.Other, ConnectionKind.Dongle, true, null,
                ChargeState.Unknown, BatteryCapability.Unsupported, "Receptor ASUS detectado; la batería de este modelo aún no es compatible."));
        return res;
    }, ct);

    private static (int? Percent, bool Asleep, bool? Charging) AskHeadset(string path, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenReadWrite(path); if (h == IntPtr.Zero) return (null, false, null);
        try
        {
            // el primer aviso tras reconectar puede tardar ~1 s
            var b = HidInterop.Transact(h, AsusRogProtocol.HeadsetRequest(AsusRogProtocol.HeadsetSubBattery), AsusRogProtocol.FrameLength, 1500, buf => AsusRogProtocol.ParseHeadsetBattery(buf), ct);
            if (b is null) return (null, false, null);
            bool? charging = null;
            if (b.Percent is not null) charging = HidInterop.Transact(h, AsusRogProtocol.HeadsetRequest(AsusRogProtocol.HeadsetSubCharging), AsusRogProtocol.FrameLength, 800, buf => AsusRogProtocol.ParseHeadsetCharging(buf) is { } c ? new Box(c ? 1 : 0) : null, ct) is { } cb ? cb.Value == 1 : null;
            return (b.Percent, b.Asleep, charging);
        }
        finally { HidInterop.Close(h); }
    }

    private static int? Ask(string path, AsusRogProtocol.Query q, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenReadWrite(path); if (h == IntPtr.Zero) return null;
        try
        {
            var r = HidInterop.Transact(h, q.Request(), AsusRogProtocol.FrameLength, 800, buf => AsusRogProtocol.ParsePercent(q, buf) is { } p ? new Box(p) : null, ct);   // los informes ajenos se ignoran
            return r?.Value;   // sin respuesta: el dispositivo está apagado o dormido
        }
        finally { HidInterop.Close(h); }
    }

    private sealed record Box(int Value);

    /// <summary>Rutas HID del receptor Omni por página de uso (solo canales del fabricante de 64 bytes) y otros receptores ASUS inalámbricos por su nombre.</summary>
    private static Dictionary<ushort, string> Enumerate(out List<(ushort Pid, string Name)> others, out Dictionary<ushort, string> headsets)
    {
        var map = new Dictionary<ushort, string>(); others = []; var seen = new HashSet<ushort>(); headsets = [];
        foreach (var i in HidInterop.Scan(v => v == AsusRogProtocol.Vendor))
        {
            if (i.Product == AsusRogProtocol.OmniReceiverPid)
            {
                if (i.UsagePage >= 0xFF00 && i.InputLength == AsusRogProtocol.FrameLength && i.OutputLength == AsusRogProtocol.FrameLength) map.TryAdd(i.UsagePage, i.Path);
                continue;
            }

            if (AsusRogProtocol.Headsets.ContainsKey(i.Product))   // receptor de auriculares conocido: su canal FF00:0001 de 64 bytes
            {
                if (i.UsagePage == AsusRogProtocol.HeadsetUsagePage && i.Usage == AsusRogProtocol.HeadsetUsage && i.InputLength == AsusRogProtocol.FrameLength && i.OutputLength == AsusRogProtocol.FrameLength) headsets.TryAdd(i.Product, i.Path);
                continue;
            }

            // otro dispositivo ASUS: solo interesa si es un receptor o un aparato inalámbrico (no la placa, el AIO ni el controlador de luces)
            if (i.ProductName.Length > 0 && seen.Add(i.Product) && (i.ProductName.Contains("2.4", StringComparison.Ordinal) || i.ProductName.Contains("RECEIVER", StringComparison.OrdinalIgnoreCase) || i.ProductName.Contains("WIRELESS", StringComparison.OrdinalIgnoreCase))) others.Add((i.Product, i.ProductName));
        }

        return map;
    }
}
