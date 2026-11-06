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

    public string? Connect(VpnProfile p) => Run(p, disconnect: false);
    public string? Disconnect(VpnProfile p) => Run(p, disconnect: true);

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
