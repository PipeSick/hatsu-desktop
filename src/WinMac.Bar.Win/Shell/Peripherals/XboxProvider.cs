using System.Runtime.InteropServices;
using Windows.Gaming.Input;
using WinMac.Bar.Peripherals;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Batería de mandos Xbox (por el adaptador inalámbrico, USB o Bluetooth) y de los GameSir en modo Xbox (docs/66). Primero la API de Windows para mandos (porcentaje real); si no hay, XInput (cuatro niveles).
/// Solo lectura: no se escribe nada a ningún aparato. <b>SIN VERIFICAR con aparatos reales.</b>
/// </summary>
internal sealed class XboxProvider : IPeripheralProvider
{
    public string Id => "vendors-xbox";
    public string DisplayName => "Xbox";
    public SupportLevel Support => SupportLevel.Experimental;

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>(); int i = 0;
        try
        {
            var list = RawGameController.RawGameControllers; if (list.Count == 0) { ct.WaitHandle.WaitOne(300); list = RawGameController.RawGameControllers; }   // la lista se llena sola tras el primer uso
            foreach (var c in list)
            {
                ct.ThrowIfCancellationRequested(); i++;
                if (c.HardwareVendorId is not (XboxBattery.Microsoft or XboxBattery.GameSir)) continue;
                var rep = c.TryGetBatteryReport(); if (rep is null || rep.Status == Windows.System.Power.BatteryStatus.NotPresent) continue;
                if (XboxBattery.WgiPercent(rep.RemainingCapacityInMilliwattHours, rep.FullChargeCapacityInMilliwattHours) is not { } pct) continue;
                string name = c.HardwareVendorId == XboxBattery.GameSir ? "GameSir controller" : string.IsNullOrWhiteSpace(c.DisplayName) ? "Xbox controller" : c.DisplayName;
                res.Add(new RawReading($"xbox:{c.HardwareProductId:X4}:{i}", name, DeviceKind.Gamepad, ConnectionKind.Unknown, true, pct, rep.Status == Windows.System.Power.BatteryStatus.Charging ? ChargeState.Charging : ChargeState.NotCharging, BatteryCapability.Supported,
                    "Lectura por la API de mandos de Windows (sin verificar con tu equipo)."));
            }
        }
        catch (Exception) { /* la API de mandos no está: queda XInput */ }

        if (res.Count == 0) res.AddRange(ReadXInput(ct));
        return res;
    }, ct);

    private static IEnumerable<RawReading> ReadXInput(CancellationToken ct)
    {
        for (uint user = 0; user < 4; user++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (XInputGetState(user, new byte[16]) != 0) continue;   // el adaptador contesta también por ranuras vacías: solo las que tienen un mando
                if (XInputGetBatteryInformation(user, 0 /* mando */, out var bi) != 0 || !XboxBattery.IsBatteryType(bi.Type)) continue;
                var (pct, text) = XboxBattery.XInputLevel(bi.Level);
                yield return new RawReading("xinput:" + user, "Mando Xbox " + (user + 1), DeviceKind.Gamepad, ConnectionKind.Unknown, true, pct, ChargeState.Unknown, BatteryCapability.Supported, $"Nivel aproximado de XInput: {text}.");
            }
            finally { }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct XInputBattery { public byte Type, Level; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint user, byte[] state);
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetBatteryInformation(uint user, byte devType, out XInputBattery info);
}
