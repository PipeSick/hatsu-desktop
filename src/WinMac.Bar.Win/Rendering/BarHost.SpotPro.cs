using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using WinMac.Bar.Capture;
using WinMac.Bar.Launcher;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Spotlight Pro actions (docs/27). Each one runs only on Enter/click; slow ones (ping, DNS) run off the UI thread with a timeout and are cancelled
/// by a new search, so a slow provider never blocks typing. Nothing typed is executed, nothing is elevated, and user scripts need a second Enter.
/// </summary>
public sealed partial class BarHost
{
    private CancellationTokenSource? _toolCts;
    private IReadOnlyList<SpotScript> _scripts = [];
    private IReadOnlyList<string> _wsNames = [];

    /// <summary>User scripts: <c>%APPDATA%\WinMac\spotlight-scripts.json</c> (<c>[{"name": "...", "path": "C:\\...", "arguments": "..."}]</c>). Read when Spotlight opens.</summary>
    public static string ScriptsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "spotlight-scripts.json");

    private void LoadScripts()
    {
        try
        {
            if (!File.Exists(ScriptsFile)) { _scripts = []; return; }
            var list = JsonSerializer.Deserialize<List<SpotScript>>(File.ReadAllText(ScriptsFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            _scripts = list.Where(s => !string.IsNullOrWhiteSpace(s.Name) && Path.IsPathFullyQualified(s.Path ?? "")).Take(50).ToList();
        }
        catch (Exception e) { _scripts = []; _log("[spotlight] scripts ilegibles: " + e.GetType().Name); }
        _wsNames = WorkspaceNames();
    }

    /// <summary>Instant, local providers: local IP and adapters (no network traffic).</summary>
    private IReadOnlyList<SpotItem> FillLocalTools(IReadOnlyList<SpotItem> items) => items.Select(i => i.Target switch
    {
        "tool:ip" => i with { Details = LocalAddresses() },
        "tool:net" => i with { Details = Adapters() },
        _ => i,
    }).ToList();

    private static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            var l = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.Address + "  (" + n.Name + ")")).ToList();
            return l.Count == 0 ? ["Sin dirección IPv4 activa"] : l;
        }
        catch (Exception e) { return ["No disponible: " + e.GetType().Name]; }
    }

    private static IReadOnlyList<string> Adapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).Take(6)
                .Select(n => $"{n.Name}: {n.NetworkInterfaceType}{(n.Speed > 0 ? $", {n.Speed / 1_000_000} Mb/s" : "")}{(n.GetIPProperties().GatewayAddresses.FirstOrDefault()?.Address is { } g ? ", puerta de enlace " + g : "")}").ToList();
        }
        catch (Exception e) { return ["No disponible: " + e.GetType().Name]; }
    }

    /// <summary>Opens a Spotlight Pro result. Returns true when Spotlight must stay open (a tool showing its result).</summary>
    private bool OpenPro(SpotItem it)
    {
        switch (it.Kind)
        {
            case SpotKind.Action: RunAction(it.Target); return false;
            case SpotKind.Tool when it.Target is "tool:ip" or "tool:net": CopyText(string.Join(Environment.NewLine, it.Details ?? [])); _log("[spotlight] información de red copiada"); return false;
            case SpotKind.Tool when it.Details is { Count: > 0 } d && d[0] != "Ejecutando…": CopyText(string.Join(Environment.NewLine, d)); _log("[spotlight] resultado de la herramienta copiado"); return false;
            case SpotKind.Tool: RunTool(it); return true;
            case SpotKind.Command when it.Target.StartsWith("ssh:", StringComparison.Ordinal): Ssh(it.Target[4..]); return false;
            case SpotKind.Command when it.Target == "terminal:": Terminal(); return false;
            case SpotKind.Command when it.Target.StartsWith("copy:", StringComparison.Ordinal): CopyText(it.Target[5..]); _log("[spotlight] comando copiado (no se ha ejecutado)"); return false;
            case SpotKind.Script: RunScript(it); return false;
        }

        return false;
    }

    private void RunAction(string target)
    {
        _log("[spotlight] acción " + target);
        switch (target)
        {
            case "winmac:capture:region": BeginCapture(CaptureMode.Region, "Spotlight"); break;
            case "winmac:capture:window": BeginCapture(CaptureMode.Window, "Spotlight"); break;
            case "winmac:capture:screen": BeginCapture(CaptureMode.Monitor, "Spotlight"); break;
            case "winmac:capture:all": BeginCapture(CaptureMode.AllMonitors, "Spotlight"); break;
            case "winmac:record": ToggleRecording(CaptureMode.Region, "Spotlight"); break;
            case "winmac:record:stop": StopRecording("Spotlight"); break;
            case "winmac:launchpad": ToggleLaunchpad("Spotlight"); break;
            case "winmac:settings": if (HostCommand is { } hc) hc("settings", null); break;
            case "winmac:ws:save": SaveWorkspace(null); break;
            case "winmac:ws:side": ArrangeWindows(grid: false); break;
            case "winmac:ws:grid": ArrangeWindows(grid: true); break;
            case var t when t.StartsWith("winmac:ws:restore:", StringComparison.Ordinal): RestoreWorkspace(t["winmac:ws:restore:".Length..]); break;
        }
    }

    /// <summary>Ping (3 echoes, 2 s each) or DNS (4 s), cancelled by a new search; the result replaces the item's details in place.</summary>
    private void RunTool(SpotItem it)
    {
        var parts = it.Target.Split(':', 3); if (parts.Length < 3 || !SpotPro.IsHost(parts[2])) return;
        string kind = parts[1], host = parts[2];
        _toolCts?.Cancel(); var cts = _toolCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        _spot?.Controller.Update(it with { Details = ["Ejecutando…"] }); _spot?.Refresh();
        _log($"[spotlight] herramienta {kind} iniciada");
        Task.Run(async () =>
        {
            IReadOnlyList<string> lines;
            try
            {
                if (kind == "dns")
                {
                    var addrs = await System.Net.Dns.GetHostAddressesAsync(host, cts.Token).ConfigureAwait(false);
                    lines = addrs.Length == 0 ? ["Sin direcciones"] : addrs.Take(8).Select(a => a.ToString()).ToList();
                }
                else
                {
                    var res = new List<string>(); using var ping = new Ping();
                    for (int i = 0; i < 3 && !cts.IsCancellationRequested; i++)
                    {
                        var r = await ping.SendPingAsync(host, TimeSpan.FromSeconds(2), null, null, cts.Token).ConfigureAwait(false);
                        res.Add(r.Status == IPStatus.Success ? $"Respuesta de {r.Address}: {r.RoundtripTime} ms" : "Sin respuesta (" + r.Status + ")");
                    }
                    lines = res;
                }
            }
            catch (OperationCanceledException) { lines = ["Cancelado o sin respuesta a tiempo"]; }
            catch (Exception e) { lines = ["Error: " + (e.InnerException ?? e).Message]; }
            _log($"[spotlight] herramienta {kind} terminada: {lines.Count} línea(s)");
            Post(() => { if (_spot is { IsOpen: true } s) { s.Controller.Update(it with { Details = lines }); s.Refresh(); } });   // ignored if the results changed meanwhile
        });
    }

    private static string? SshClient()
    {
        var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Windows' own OpenSSH client in its own console window; arguments validated and passed one by one (no shell).</summary>
    private void Ssh(string args)
    {
        if (SshClient() is not { } exe) { ShowInfo("SSH", "El cliente SSH de Windows no está instalado (Configuración › Sistema › Características opcionales › Cliente OpenSSH)."); return; }
        var list = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (list.Length == 0 || list.Any(a => a.StartsWith('-') && a != "-p")) return;
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = false };
                foreach (var a in list) psi.ArgumentList.Add(a);
                using var _ = Process.Start(psi); _log("[spotlight] SSH abierto");
            }
            catch (Exception e) { _log("[spotlight] SSH: " + e.Message); }
        });
    }

    /// <summary>A terminal in the user's folder (Windows Terminal if installed, otherwise PowerShell). Nothing typed is passed to it.</summary>
    private void Terminal()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Task.Run(() =>
        {
            try { using var _ = Process.Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true, ArgumentList = { "-d", home } }); }
            catch (Exception)
            {
                try { using var _ = Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = home }); }
                catch (Exception e) { _log("[spotlight] terminal: " + e.Message); }
            }
        });
    }

    /// <summary>A script the user registered, after the second Enter, with the user's own rights (no «runas»). PowerShell honours the execution policy.</summary>
    private void RunScript(SpotItem it)
    {
        var s = _scripts.FirstOrDefault(x => x.Path == it.Target && x.Name == it.Title);
        if (s is null || !File.Exists(s.Path)) { _log("[spotlight] script no registrado o inexistente"); return; }
        _log("[spotlight] script ejecutado: " + s.Name);
        Task.Run(() =>
        {
            try
            {
                ProcessStartInfo psi = Path.GetExtension(s.Path).Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessStartInfo("powershell.exe") { UseShellExecute = false, ArgumentList = { "-NoProfile", "-File", s.Path } }
                    : new ProcessStartInfo(s.Path) { UseShellExecute = true };
                if (!string.IsNullOrWhiteSpace(s.Arguments)) { if (psi.UseShellExecute) psi.Arguments = s.Arguments; else foreach (var a in s.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(a); }
                psi.WorkingDirectory = Path.GetDirectoryName(s.Path) ?? "";
                using var _ = Process.Start(psi);
            }
            catch (Exception e) { _log("[spotlight] script: " + e.Message); }
        });
    }
}
