using System.Diagnostics;

namespace WinMac.SystemControl.Win;

public static class SystemServicesFactory
{
    public static SystemServices CreateReal() => new(new AudioService(), new WifiService(), new BluetoothService(), new BrightnessService(), new PowerService(), new AppearanceService(), new DisplayService(), new SessionService());

    /// <summary>
    /// Mediciones técnicas de lo que ESTE equipo permite, sin cambiar nada del sistema: qué servicios responden, con qué datos y en cuánto tiempo. Lo usan el autodiagnóstico de la barra
    /// (<c>--selftest-system</c>), el CI de Windows (el runner no tiene Wi-Fi/Bluetooth/brillo: se espera «no disponible», nunca un cuelgue ni una excepción) y tu PC (pruebas manuales).
    /// </summary>
    public static IReadOnlyList<string> Probe(SystemServices s)
    {
        var lines = new List<string>();
        void Step<T>(string name, Func<T> f, Func<T, string> show)
        {
            var sw = Stopwatch.StartNew();
            try { var v = f(); lines.Add($"[OK ] {name}: {show(v)} ({sw.ElapsedMilliseconds} ms)"); }
            catch (Exception e) { lines.Add($"[FALLO] {name}: EXCEPCIÓN {e.GetType().Name}: {e.Message}"); }
        }

        Step("Audio: estado", s.Audio.Read, a => a.Available ? $"volumen {a.Volume}%, silencio={a.Muted}, salidas={a.Outputs.Count} ({string.Join(" | ", a.Outputs.Select(o => (o.IsDefault ? "*" : "") + o.Name))})" : "no disponible: " + a.Reason);
        Step("Audio: ¿se puede cambiar la salida? (API no oficial, solo detección)", s.Audio.CanSwitchOutput, r => r.IsOk ? "compatible" : $"{r.Status}: {r.Message}");
        Step("Wi-Fi: estado (escaneo + espera de 4 s + nueva lectura)", () => { s.Wifi.Read(scan: true); System.Threading.Thread.Sleep(4000); return s.Wifi.Read(scan: false); }, w => w.Available ? $"conectado={w.Connected} ssid={w.Ssid} señal={w.Signal} redes={w.Networks.Count} [{string.Join(", ", w.Networks.Take(5).Select(n => n.Ssid + (n.HasProfile ? "*" : "") + (n.RssiDbm is { } r ? $" {r}dBm" : "") + (n.BandText is { } bt ? " " + bt : "")))}] radio={w.RadioOn}{(w.Reason is null ? "" : " MOTIVO: " + w.Reason)}" : "no disponible: " + w.Reason);
        Step("Bluetooth: estado", () => s.Bluetooth.Read(true), b => b.Available ? $"radio={(b.RadioOn ? "encendida" : "apagada")} dispositivos={b.Devices.Count} conectados={b.ConnectedCount}" : "no disponible: " + b.Reason);
        Step("Brillo: pantallas controlables", s.Brightness.Read, b => b.Available ? string.Join(" | ", b.Monitors.Select(m => $"{m.Name} {m.Percent}% ({m.Method})")) : "no disponible: " + b.Reason);
        Step("Energía: modo", s.Power.ReadMode, p => p.Available ? PowerModeState.Label(p.Mode) : "no disponible: " + p.Reason);
        Step("Apariencia: tema", s.Appearance.Read, a => a.Available ? (a.Dark ? "oscuro" : "claro") : "no disponible: " + a.Reason);
        return lines;
    }
}
