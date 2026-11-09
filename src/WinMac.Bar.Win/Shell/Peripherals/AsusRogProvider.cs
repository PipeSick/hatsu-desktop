using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de periféricos ASUS ROG por el receptor ROG OMNI (docs/64). A diferencia de los otros lectores, este ENVÍA una consulta de 64 bytes al receptor (la misma que manda Armoury Crate,
/// observada en una captura del tráfico USB) y lee la respuesta. Por eso solo se crea si el usuario lo activó. Solo toca los canales del fabricante del receptor Omni (VID 0B05 / PID 1ACE),
/// nunca el teclado ni el ratón como tales. Sin respuesta en el plazo, el dispositivo se considera no disponible; nunca se inventa un porcentaje.
/// </summary>
internal sealed class AsusRogProvider : IPeripheralProvider
{
    private readonly string? _kbName, _mouseName;
    public AsusRogProvider(string? keyboardName, string? mouseName) { _kbName = string.IsNullOrWhiteSpace(keyboardName) ? null : keyboardName; _mouseName = string.IsNullOrWhiteSpace(mouseName) ? null : mouseName; }
    public string Id => "asus-rog";
    public string DisplayName => "ASUS ROG (receptor 2,4 GHz)";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var paths = Enumerate(out var others);
        foreach (var q in AsusRogProtocol.OmniQueries)
        {
            ct.ThrowIfCancellationRequested();
            if (!paths.TryGetValue(q.UsagePage, out var path)) continue;             // el receptor no está conectado
            int? pct = Ask(path, q, ct);
            string name = (q.Role == AsusRogProtocol.Role.Keyboard ? _kbName : _mouseName) ?? AsusRogProtocol.DisplayName(q.Role);
            res.Add(new RawReading("asus-rog:" + q.Role, name, AsusRogProtocol.Kind(q.Role), ConnectionKind.Dongle, pct is not null, pct,
                ChargeState.Unknown, pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported, "Lectura por el canal del receptor ROG OMNI; modelo no identificado."));
        }

        // Otros receptores ASUS (por ejemplo, los de auriculares 2,4 GHz): se listan por su nombre para que se vea que Hatsu los reconoció, pero su batería aún no se puede leer.
        foreach (var (pid, prod) in others)
            res.Add(new RawReading("asus-rx:" + pid.ToString("X4"), prod, prod.Contains("HEADSET", StringComparison.OrdinalIgnoreCase) || prod.Contains("DELTA", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Headset : DeviceKind.Other, ConnectionKind.Dongle, true, null,
                ChargeState.Unknown, BatteryCapability.Unsupported, "Receptor ASUS detectado; la batería de este modelo aún no es compatible."));
        return res;
    }, ct);

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
    private static Dictionary<ushort, string> Enumerate(out List<(ushort Pid, string Name)> others)
    {
        var map = new Dictionary<ushort, string>(); others = []; var seen = new HashSet<ushort>();
        foreach (var i in HidInterop.Scan(v => v == AsusRogProtocol.Vendor))
        {
            if (i.Product == AsusRogProtocol.OmniReceiverPid)
            {
                if (i.UsagePage >= 0xFF00 && i.InputLength == AsusRogProtocol.FrameLength && i.OutputLength == AsusRogProtocol.FrameLength) map.TryAdd(i.UsagePage, i.Path);
                continue;
            }

            // otro dispositivo ASUS: solo interesa si es un receptor o un aparato inalámbrico (no la placa, el AIO ni el controlador de luces)
            if (i.ProductName.Length > 0 && seen.Add(i.Product) && (i.ProductName.Contains("2.4", StringComparison.Ordinal) || i.ProductName.Contains("RECEIVER", StringComparison.OrdinalIgnoreCase) || i.ProductName.Contains("WIRELESS", StringComparison.OrdinalIgnoreCase))) others.Add((i.Product, i.ProductName));
        }

        return map;
    }
}
