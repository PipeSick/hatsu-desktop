using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de teclados y ratones inalámbricos Razer por su protocolo de informes de característica de 90 bytes (docs/66). <b>SIN VERIFICAR con aparatos reales.</b>
/// Envía una consulta de solo lectura y lee la respuesta; solo si el usuario activó «más marcas». Synapse comparte el buzón de respuesta, así que se reintenta y se descartan las respuestas ajenas.
/// </summary>
internal sealed class RazerProvider : IPeripheralProvider
{
    public string Id => "vendors-razer";
    public string DisplayName => "Razer";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); var done = new HashSet<ushort>(); var seen = new HashSet<string>();
        foreach (var i in HidInterop.Scan(v => v == RazerProtocol.Vendor).Where(i => i.FeatureLength == RazerProtocol.ReportLength))
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(i.Product)) continue;
            IntPtr h = HidInterop.OpenFeature(i.Path); if (h == IntPtr.Zero) continue;
            try
            {
                byte kb = RazerProtocol.KeyboardTransaction(i.Product); var tids = kb != 0 ? new[] { kb } : RazerProtocol.ProbeTransactions;
                RazerProtocol.Answer? ok = null; byte tid = 0; bool offline = false;
                foreach (var t in tids)
                {
                    var a = Command(h, t, RazerProtocol.CmdBattery, kb != 0, ct);
                    if (a.Status == RazerProtocol.Status.Ok) { ok = a; tid = t; break; }
                    offline |= a.Status == RazerProtocol.Status.Offline;   // también puede significar «transacción equivocada»: se sigue probando
                }

                if (ok is null && !offline) continue;
                done.Add(i.Product);
                string name = RazerProtocol.CleanName(i.ProductName); var kind = RazerProtocol.Kind(i.Product) ?? (name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) || name.Contains("blackwidow", StringComparison.OrdinalIgnoreCase) ? DeviceKind.Keyboard : DeviceKind.Mouse);
                int? pct = ok is null ? null : RazerProtocol.Percent(ok.Args); bool charging = false;
                if (ok is not null && Command(h, tid, RazerProtocol.CmdCharging, kb != 0, ct) is { Status: RazerProtocol.Status.Ok } c) charging = RazerProtocol.Charging(c.Args);
                if (!seen.Add(name)) { if (charging) for (int k = 0; k < res.Count; k++) if (res[k].Name == name) res[k] = res[k] with { Charge = ChargeState.Charging }; continue; }   // el ratón enchufado a cargar sale dos veces (cable y receptor): una sola
                res.Add(new RawReading("razer:" + i.Product.ToString("X4"), name, kind, ConnectionKind.Dongle, pct is not null, pct, ok is null ? ChargeState.Unknown : charging ? ChargeState.Charging : ChargeState.NotCharging,
                    pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported, ok is null ? "Apagado o fuera de alcance." : "Lectura por el protocolo de Razer (sin verificar con tu equipo)."));
            }
            finally { HidInterop.Close(h); }
        }

        return res;
    }, ct);

    /// <summary>Una consulta con reintentos: ocupado = esperar sin reenviar; respuesta ajena = reenviar (Synapse usa el mismo buzón).</summary>
    private static RazerProtocol.Answer Command(IntPtr h, byte tid, byte cmd, bool keyboard, CancellationToken ct)
    {
        var req = RazerProtocol.Request(tid, RazerProtocol.ClassPower, cmd, RazerProtocol.ArgSize); bool send = true; var last = new RazerProtocol.Answer(RazerProtocol.Status.NotAnswer, []);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (send && !HidInterop.SetFeature(h, req)) return last;
            ct.WaitHandle.WaitOne(keyboard ? 5 : attempt == 0 ? 15 : 40); ct.ThrowIfCancellationRequested();   // los teclados inalámbricos contestan en ~5 ms; esperar más deja que Synapse pise la respuesta
            send = true;
            var a = RazerProtocol.Parse(HidInterop.GetFeature(h, RazerProtocol.ReportLength), tid, RazerProtocol.ClassPower, cmd, RazerProtocol.ArgSize);
            switch (a.Status)
            {
                case RazerProtocol.Status.Ok or RazerProtocol.Status.Unsupported: return a;
                case RazerProtocol.Status.Busy: send = false; break;
                case RazerProtocol.Status.Offline: last = a; break;
            }
        }

        return last;
    }
}
