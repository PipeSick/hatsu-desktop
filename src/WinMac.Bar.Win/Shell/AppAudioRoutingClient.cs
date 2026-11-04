using System.Diagnostics;
using System.Text.Json;
using WinMac.SystemControl;

namespace WinMac.Bar.Win.Shell;

/// <summary>
/// WM-035 (experimental): la barra pide la salida por aplicación a un proceso auxiliar (<c>WinMac.Bar.App --audio-route …</c>) que usa la interfaz interna de Windows.
/// Así, si esa interfaz no encaja con una versión de Windows, falla el auxiliar (y se informa), nunca la barra. Cada llamada tiene un tiempo máximo de 5 s.
/// </summary>
public sealed class AppAudioRoutingClient(Action<string> log) : IAppAudioRouting
{
    private AppRoutingState? _probe;   // la disponibilidad no cambia durante la sesión: se pregunta una vez

    public AppRoutingState Probe()
    {
        if (_probe is not null) return _probe;
        var r = Run("probe");
        _probe = r is { } d && d.TryGetProperty("ok", out var ok) && ok.GetBoolean() ? new AppRoutingState(true, null, new Dictionary<int, string?>()) : AppRoutingState.Off(Why(r) ?? "no se pudo comprobar");
        log("[sonido] salida por aplicación: " + (_probe.Available ? "disponible (experimental)" : "no disponible: " + _probe.Reason));
        return _probe;
    }

    public IReadOnlyDictionary<int, string?> Read(IReadOnlyList<int> pids)
    {
        var map = new Dictionary<int, string?>();
        if (pids.Count == 0) return map;
        var r = Run("get", string.Join(",", pids));
        if (r is { } d && d.TryGetProperty("outputs", out var o) && o.ValueKind == JsonValueKind.Object)
            foreach (var p in o.EnumerateObject()) if (int.TryParse(p.Name, out int pid)) map[pid] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        return map;
    }

    public ServiceResult Set(int pid, string? deviceId)
    {
        var r = Run("set", pid.ToString(), deviceId ?? "default");
        if (r is { } d && d.TryGetProperty("ok", out var ok) && ok.GetBoolean()) return ServiceResult.Ok;
        string why = Why(r) ?? "el proceso auxiliar no respondió";
        log("[sonido] salida por aplicación no aplicada: " + why);
        return ServiceResult.Failed("No se cambió la salida: " + why);
    }

    private static string? Why(JsonElement? r) => r is { } d && d.TryGetProperty("why", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;

    private JsonElement? Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath ?? "HatsuDesktop.Bar.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            psi.ArgumentList.Add("--audio-route"); foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var read = p.StandardOutput.ReadLineAsync();
            if (!read.Wait(5000)) { try { p.Kill(); } catch (Exception) { } log("[sonido] el proceso auxiliar de salida por aplicación no respondió en 5 s"); return null; }
            p.WaitForExit(1000);
            if (p.HasExited && p.ExitCode != 0 && string.IsNullOrEmpty(read.Result)) { log($"[sonido] el proceso auxiliar terminó con el código 0x{p.ExitCode:X8} (interfaz de Windows incompatible)"); return null; }
            return string.IsNullOrWhiteSpace(read.Result) ? null : JsonDocument.Parse(read.Result).RootElement.Clone();
        }
        catch (Exception e) { log("[sonido] proceso auxiliar: " + e.GetType().Name); return null; }
    }
}
