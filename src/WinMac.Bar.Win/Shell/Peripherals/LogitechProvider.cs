using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de periféricos Logitech con receptor (Unifying, Lightspeed, Bolt) por HID++ 2.0. <b>PROBABLE: escrito con la documentación pública y sin ningún dispositivo Logitech para probarlo.</b>
/// Solo hace consultas de lectura (qué funciones tiene el dispositivo, cómo se llama, cuánta batería tiene) y solo si el usuario lo activó. El nombre sale del propio dispositivo.
/// Sin respuesta en el plazo, la ranura se salta; nunca se inventa un porcentaje.
/// </summary>
internal sealed class LogitechProvider : IPeripheralProvider
{
    public string Id => "logitech";
    public string DisplayName => "Logitech (receptor, HID++)";
    public SupportLevel Support => SupportLevel.Experimental;

    private const int Wait = 300;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>();
        // Interfaz de HID++ de trama larga del receptor: página de uso del fabricante 0xFF00, uso 0x0002, informes de 20 bytes.
        foreach (var rx in HidInterop.Scan(v => v == LogitechHidpp.Vendor).Where(i => i.UsagePage == 0xFF00 && i.Usage == 0x0002 && i.InputLength == LogitechHidpp.LongLength && i.OutputLength == LogitechHidpp.LongLength))
        {
            IntPtr h = HidInterop.OpenReadWrite(rx.Path); if (h == IntPtr.Zero) continue;
            try
            {
                for (byte dev = 1; dev <= 6; dev++)       // las ranuras del receptor
                {
                    ct.ThrowIfCancellationRequested();
                    if (ReadDevice(h, dev, rx.Product, ct) is { } r) res.Add(r);
                }
            }
            finally { HidInterop.Close(h); }
        }

        return res;
    }, ct);

    private static byte[]? Ask(IntPtr h, byte[] req, CancellationToken ct) =>
        HidInterop.Transact(h, req, LogitechHidpp.LongLength, Wait, b => LogitechHidpp.IsReplyTo(req, b) || LogitechHidpp.IsErrorFor(req, b) ? new Frame(b) : null, ct)?.Bytes;

    private sealed record Frame(byte[] Bytes);

    private static byte? FindFeature(IntPtr h, byte dev, ushort feature, CancellationToken ct)
    {
        var req = LogitechHidpp.GetFeature(dev, feature); return LogitechHidpp.ParseFeatureIndex(req, Ask(h, req, ct));
    }

    private static RawReading? ReadDevice(IntPtr h, byte dev, ushort receiverPid, CancellationToken ct)
    {
        // 1) ¿Hay un dispositivo despierto en esta ranura? Se pregunta por su función de nombre; sin respuesta o con error, se salta.
        if (FindFeature(h, dev, LogitechHidpp.FeatureDeviceName, ct) is not { } nameIdx) return null;

        // 2) Batería: primero la función moderna (0x1004), si no, la clásica (0x1000).
        LogitechHidpp.Battery? bat = null;
        if (FindFeature(h, dev, LogitechHidpp.FeatureUnifiedBattery, ct) is { } uIdx) { var r = LogitechHidpp.Request(dev, uIdx, 1); bat = LogitechHidpp.ParseUnifiedBattery(r, Ask(h, r, ct)); }
        if (bat is null && FindFeature(h, dev, LogitechHidpp.FeatureBatteryStatus, ct) is { } sIdx) { var r = LogitechHidpp.Request(dev, sIdx, 0); bat = LogitechHidpp.ParseBatteryStatus(r, Ask(h, r, ct)); }

        // 3) Nombre y tipo: del propio dispositivo.
        string name = ""; var kind = DeviceKind.Other;
        var cr = LogitechHidpp.Request(dev, nameIdx, 0); var cresp = Ask(h, cr, ct);
        if (LogitechHidpp.IsReplyTo(cr, cresp))
        {
            int count = Math.Min(cresp![4], (byte)40);
            for (int guard = 0; name.Length < count && guard < 6; guard++)
            {
                var nr = LogitechHidpp.Request(dev, nameIdx, 1, (byte)name.Length); var chunk = LogitechHidpp.NameChunk(nr, Ask(h, nr, ct)); if (chunk.Length == 0) break; name += chunk;
            }

            var tr = LogitechHidpp.Request(dev, nameIdx, 2); var tresp = Ask(h, tr, ct); if (LogitechHidpp.IsReplyTo(tr, tresp)) kind = LogitechHidpp.KindFrom(tresp![4]);
        }

        if (name.Length == 0) name = "Dispositivo Logitech (ranura " + dev + ")";
        return new RawReading($"logi:{receiverPid:X4}:{dev}", name, kind, ConnectionKind.Dongle, true, bat?.Percent, bat?.Charge ?? ChargeState.Unknown, bat is null ? BatteryCapability.Unknown : BatteryCapability.Supported,
            "Lectura por HID++ (Logitech). Función en prueba: no se ha verificado con un dispositivo real.");
    }
}
