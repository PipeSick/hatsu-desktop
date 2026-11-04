namespace WinMac.Bar.Peripherals;

public sealed record PeripheralAlert(string DeviceId, string Name, int Percent);

/// <summary>
/// Alertas de batería baja. Evitan el ruido: solo con lectura ACTUAL de un dispositivo CONECTADO y visible, que no esté cargando; una vez avisado, el dispositivo se «desarma» y
/// solo vuelve a avisar tras SUBIR por encima del umbral + histéresis (cargarlo) y bajar otra vez; además hay un intervalo mínimo entre avisos del mismo dispositivo. Desconectar y
/// reconectar con la batería aún baja NO repite el aviso. Puro: el anfitrión envía el resultado por el sistema de notificaciones existente (la Isla).
/// </summary>
public sealed class PeripheralAlertEngine
{
    public const int Hysteresis = 5;
    public static readonly TimeSpan MinGap = TimeSpan.FromHours(4);

    private sealed class State { public bool Armed = true; public DateTime? LastAlertUtc; }
    private readonly Dictionary<string, State> _state = new(StringComparer.Ordinal);

    public IReadOnlyList<PeripheralAlert> Evaluate(IEnumerable<DeviceView> devices, bool enabled, int threshold, DateTime nowUtc)
    {
        var fire = new List<PeripheralAlert>();
        if (!enabled) return fire;
        threshold = Math.Clamp(threshold, 1, 90);
        foreach (var d in devices)
        {
            if (d.Hidden) continue;
            var s = _state.TryGetValue(d.Id, out var x) ? x : _state[d.Id] = new State();
            if (d.Battery != BatteryStatus.Current || d.Percent is not { } p) continue;   // sin lectura actual no se decide nada (ni se rearma)
            if (p >= threshold + Hysteresis) { s.Armed = true; continue; }
            if (d.Charge == ChargeState.Charging) continue;                                // cargando: no se avisa de batería baja
            if (p <= threshold && s.Armed && (s.LastAlertUtc is null || nowUtc - s.LastAlertUtc >= MinGap))
            {
                s.Armed = false; s.LastAlertUtc = nowUtc; fire.Add(new PeripheralAlert(d.Id, d.Name, p));
            }
        }

        return fire;
    }

    public void Reset() => _state.Clear();
}
