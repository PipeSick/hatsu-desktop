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
        var vendorIfaces = HidInterop.Scan(v => v == AsusRogProtocol.Vendor); var seen = new HashSet<string>();
        // 1) El receptor Omni dice qué aparatos tiene emparejados; cada uno se lee por su canal y sale con su nombre de modelo.
        bool identified = false;
        foreach (var r in ReadOmniPaired(vendorIfaces, ct)) { if (seen.Add(r.Name)) res.Add(r); identified = true; }
        // 2) Teclados y ratones con su propio receptor 2,4 GHz o por cable (informe sin número).
        foreach (var r in ReadDirect(vendorIfaces, ct)) if (seen.Add(r.Name)) res.Add(r);
        // 3) Si el Omni no dio la lista de emparejados, la consulta genérica de antes (sin nombre de modelo).
        foreach (var q in identified ? [] : AsusRogProtocol.OmniQueries)
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
                pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported, asleep ? "Apagado o dormido." : "Lectura por el receptor del audífono (sin verificar)."));
        }

        // Otros receptores ASUS (por ejemplo, los de auriculares 2,4 GHz): se listan por su nombre para que se vea que Hatsu los reconoció, pero su batería aún no se puede leer.
        foreach (var (pid, prod) in others)
            res.Add(new RawReading("asus-rx:" + pid.ToString("X4"), prod, prod.Contains("HEADSET", StringComparison.OrdinalIgnoreCase) || prod.Contains("DELTA", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Headset : DeviceKind.Other, ConnectionKind.Dongle, true, null,
                ChargeState.Unknown, BatteryCapability.Unsupported, "Receptor ASUS detectado; la batería de este modelo aún no es compatible."));
        return res;
    }, ct);

    private static int InterfaceOf(string path) { var m = System.Text.RegularExpressions.Regex.Match(path, @"mi_([0-9a-f]{2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase); return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : -1; }

    private static RawReading? ReadModel(HidIface i, AsusRogModels.Model m, bool omni, CancellationToken ct)
    {
        IntPtr h = HidInterop.OpenReadWrite(i.Path); if (h == IntPtr.Zero) return null;
        try
        {
            var req = AsusRogModels.Request(m); var frame = new byte[Math.Max((int)i.OutputLength, 12)]; Array.Copy(req, frame, req.Length);
            var raw = HidInterop.Transact(h, frame, i.InputLength, 600, b => AsusRogModels.Parse(m, b) is { } p ? new Parsed(p) : null, ct)?.Result; if (raw is null) return null;
            string key = "asus-rog:" + m.Pid.ToString("X4") + (m.Role == AsusRogProtocol.Role.Keyboard ? ":kb" : ":ms");
            if (raw.Answer == AsusRogModels.Answer.Asleep) return new RawReading(key, m.Name, AsusRogProtocol.Kind(m.Role), omni ? ConnectionKind.Dongle : ConnectionKind.Dongle, false, null, ChargeState.Unknown, BatteryCapability.Unknown, "Apagado o dormido.");
            return new RawReading(key, m.Name, AsusRogProtocol.Kind(m.Role), ConnectionKind.Dongle, raw.Percent is not null, raw.Percent, raw.Percent is null ? ChargeState.Unknown : raw.Charging ? ChargeState.Charging : ChargeState.NotCharging,
                raw.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported, omni ? "Lectura por el receptor ROG OMNI." : "Lectura por el receptor o el cable del propio aparato (sin verificar).");
        }
        finally { HidInterop.Close(h); }
    }

    private sealed record Parsed(AsusRogModels.Result Result);

    private static IEnumerable<RawReading> ReadOmniPaired(List<HidIface> all, CancellationToken ct)
    {
        var omni = all.Where(i => i.Product == AsusRogProtocol.OmniReceiverPid && InterfaceOf(i.Path) == 2 && i.OutputLength >= 12).ToList();
        var listIface = omni.FirstOrDefault(i => i.Path.Contains("col01", StringComparison.OrdinalIgnoreCase)); if (listIface is null) yield break;
        IReadOnlyList<ushort>? paired = null; IntPtr h = HidInterop.OpenReadWrite(listIface.Path);
        if (h != IntPtr.Zero)
        {
            try
            {
                var frame = new byte[listIface.OutputLength]; Array.Copy(AsusRogModels.PairingRequest, frame, AsusRogModels.PairingRequest.Length);
                paired = HidInterop.Transact(h, frame, listIface.InputLength, 800, b => AsusRogModels.IsPairingReply(b) ? new Pairs(AsusRogModels.ParsePairing(b)) : null, ct)?.List;
            }
            finally { HidInterop.Close(h); }
        }

        if (paired is null) yield break;
        foreach (var pid in paired)
        {
            ct.ThrowIfCancellationRequested();
            var m = AsusRogModels.Models.FirstOrDefault(x => x.Pid == pid && x.ReportId != 0 && x.Col is not null); if (m is null) continue;   // solo el canal del Omni de ese modelo
            foreach (var i in omni.Where(i => i.Path.Contains(m.Col!, StringComparison.OrdinalIgnoreCase)))
                if (ReadModel(i, m, omni: true, ct) is { } r) { yield return r; break; }
        }
    }

    private sealed record Pairs(IReadOnlyList<ushort> List);

    private static IEnumerable<RawReading> ReadDirect(List<HidIface> all, CancellationToken ct)
    {
        foreach (var m in AsusRogModels.Models.Where(x => x.ReportId == 0))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var i in all.Where(i => i.Product == m.Pid && i.UsagePage >= 0xFF00 && i.OutputLength >= 12 && (m.Interface < 0 || InterfaceOf(i.Path) == m.Interface)))
                if (ReadModel(i, m, omni: false, ct) is { } r) { yield return r; break; }
        }
    }

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
