using System.Diagnostics;
using System.Net.NetworkInformation;
using WinMac.Bar.Vpn;

namespace WinMac.Bar.Win.Shell;

/// <summary>
/// VPN de Windows con piezas públicas y sin permisos de administrador: los perfiles se leen de las agendas de RAS (<c>rasphone.pbk</c> del usuario y de todos los usuarios), «conectada» se deduce de que exista
/// una interfaz de red activa con el nombre del perfil (así las nombra Windows) y conectar/desconectar usa <c>rasdial.exe</c>, que reutiliza las credenciales guardadas en el perfil.
/// </summary>
public sealed class RasVpnService : IVpnService
{
    private static IEnumerable<string> Phonebooks()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Network", "Connections", "Pbk", "rasphone.pbk");
    }

    public VpnState Read()
    {
        try
        {
            var found = new List<(string Name, string Pbk)>();
            foreach (var pbk in Phonebooks())
            {
                if (!File.Exists(pbk)) continue;
                try { foreach (var v in PhonebookParser.Vpns(File.ReadAllText(pbk), pbk)) if (!found.Any(f => string.Equals(f.Name, v.Name, StringComparison.OrdinalIgnoreCase))) found.Add(v); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            var up = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces()) if (n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback) up.Add(n.Name);
            return new VpnState(true, found.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Select(f => new VpnProfile(f.Name, up.Contains(f.Name), f.Pbk)).ToList());
        }
        catch (Exception e) { return VpnState.None("No se pudieron leer las conexiones VPN (" + e.GetType().Name + ")."); }
    }

    /// <summary>
    /// Conectar: primero con la misma API de VPN que usa Configuración de Windows (<c>VpnManagementAgent</c>), que aprovecha las credenciales que el usuario ya guardó en el perfil, sin pedir nada.
    /// Si esa vía no puede (perfil que no administra, permiso, etc.), <c>rasdial.exe</c>, y como último recurso <c>rasphone -d</c>, que muestra la ventana de credenciales de Windows.
    /// </summary>
    public string? Connect(VpnProfile p)
    {
        var viaAgent = ViaAgent(p, disconnect: false); if (viaAgent.Done) return null;
        var err = Run(p, disconnect: false); if (err is null) return null;
        return RunRasphone(p) is null ? null : (viaAgent.Detail is { Length: > 0 } d ? $"{err} (Windows indicó: {d})" : err);
    }

    public string? Disconnect(VpnProfile p) => ViaAgent(p, disconnect: true).Done ? null : Run(p, disconnect: true);

    /// <summary>(Done, Detail): Done = la API lo hizo; Detail = por qué no, para el mensaje de error.</summary>
    private static (bool Done, string? Detail) ViaAgent(VpnProfile p, bool disconnect)
    {
        try
        {
            var task = Task.Run(() =>
            {
                var agent = new Windows.Networking.Vpn.VpnManagementAgent();
                var profile = agent.GetProfilesAsync().AsTask().GetAwaiter().GetResult().FirstOrDefault(x => string.Equals(x.ProfileName, p.Name, StringComparison.OrdinalIgnoreCase));
                if (profile is null) return "el perfil no está entre los que administra la API de VPN";
                var st = (disconnect ? agent.DisconnectProfileAsync(profile) : agent.ConnectProfileAsync(profile)).AsTask().GetAwaiter().GetResult();
                return st is Windows.Networking.Vpn.VpnManagementErrorStatus.Ok or Windows.Networking.Vpn.VpnManagementErrorStatus.AlreadyConnected or Windows.Networking.Vpn.VpnManagementErrorStatus.AlreadyDisconnecting ? null : st.ToString();
            });
            if (!task.Wait(disconnect ? 20000 : 90000)) return (false, "la API de VPN no respondió a tiempo");
            return task.Result is null ? (true, null) : (false, task.Result);
        }
        catch (Exception e) { return (false, (e.InnerException ?? e).GetType().Name); }
    }

    /// <summary>Último recurso: <c>rasphone -d</c> (puede mostrar la ventana de credenciales de Windows). Null si, al cerrarse, la VPN quedó conectada.</summary>
    private string? RunRasphone(VpnProfile p)
    {
        try
        {
            var psi = new ProcessStartInfo("rasphone.exe") { UseShellExecute = false };
            psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(p.Name);
            if (p.Phonebook is { Length: > 0 }) { psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(p.Phonebook); }
            using var proc = Process.Start(psi); if (proc is null) return "No se pudo iniciar rasphone.exe.";
            if (!proc.WaitForExit(180000)) return "La ventana de conexión de Windows siguió abierta demasiado tiempo.";
            for (int i = 0; i < 10; i++)   // la interfaz puede tardar un instante en aparecer tras cerrarse la ventana
            {
                if (Read().Profiles.Any(x => x.Connected && string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase))) return null;
                Thread.Sleep(500);
            }

            return "La VPN no quedó conectada.";
        }
        catch (Exception e) { return "No se pudo ejecutar rasphone.exe (" + e.GetType().Name + ")."; }
    }

    private static string? Run(VpnProfile p, bool disconnect)
    {
        try
        {
            var psi = new ProcessStartInfo("rasdial.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add(p.Name);
            if (disconnect) psi.ArgumentList.Add("/disconnect");
            if (p.Phonebook is { Length: > 0 }) psi.ArgumentList.Add("/phonebook:" + p.Phonebook);
            using var proc = Process.Start(psi); if (proc is null) return "No se pudo iniciar rasdial.exe.";
            if (!proc.WaitForExit(disconnect ? 15000 : 60000)) { try { proc.Kill(); } catch (Exception) { } return "La VPN tardó demasiado en responder."; }
            return proc.ExitCode == 0 ? null : VpnText.Explain(proc.ExitCode);
        }
        catch (Exception e) { return "No se pudo ejecutar rasdial.exe (" + e.GetType().Name + ")."; }
    }
}
