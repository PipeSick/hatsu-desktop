using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using WinMac.Bar.Hub;
using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Tráfico del ADAPTADOR activo (contadores del sistema, sin administrador) y aplicaciones con conexiones TCP establecidas. Son cosas distintas: Windows no entrega bytes por proceso sin
/// privilegios de administrador (ETW), así que la lista de aplicaciones dice «usa la red» (conexiones), nunca «consume X KB/s».
/// </summary>
internal sealed class NetworkMonitor : INetworkMonitor
{
    public NetCounters ReadCounters()
    {
        try
        {
            var nic = ActiveInterface();
            if (nic is null) return new NetCounters(0, 0, null, "Sin conexión de red");
            var st = nic.GetIPv4Statistics(); var ip = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            return new NetCounters((ulong)Math.Max(0, st.BytesReceived), (ulong)Math.Max(0, st.BytesSent), new AdapterInfo(nic.Name, nic.Description, KindOf(nic.NetworkInterfaceType), ip, nic.Speed > 0 ? nic.Speed : null));
        }
        catch (Exception e) when (e is NetworkInformationException or InvalidOperationException or PlatformNotSupportedException) { return new NetCounters(0, 0, null, "No se pudieron leer los adaptadores de red"); }
    }

    /// <summary>El adaptador por el que sale el tráfico a internet: activo, con puerta de enlace IPv4, sin bucle local ni túneles; si hay varios, el de mayor velocidad.</summary>
    private static NetworkInterface? ActiveInterface() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Where(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address)))
            .OrderByDescending(n => n.Speed).FirstOrDefault();

    private static string KindOf(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi", NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Ppp or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Banda ancha móvil", _ => "Otro",
    };

    public IReadOnlyList<NetApp> ReadApps()
    {
        int size = 0; HubNative.GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /* AF_INET */, 4 /* TCP_TABLE_OWNER_PID_CONNECTIONS */, 0);
        if (size <= 0) return [];
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (HubNative.GetExtendedTcpTable(buf, ref size, false, 2, 4, 0) != 0) throw new InvalidOperationException("GetExtendedTcpTable falló");
            int n = Marshal.ReadInt32(buf); var counts = new Dictionary<int, int>();
            for (int i = 0; i < n && i < 4096; i++)
            {
                IntPtr row = buf + 4 + i * 24;   // MIB_TCPROW_OWNER_PID: estado, dir. local, puerto local, dir. remota, puerto remoto, PID (6 × 4 bytes)
                uint state = (uint)Marshal.ReadInt32(row), remote = (uint)Marshal.ReadInt32(row, 12); int pid = Marshal.ReadInt32(row, 20);
                if (state != 5 /* ESTABLISHED */ || pid <= 4 || (remote & 0xFF) == 127) continue;   // sin el sistema ni el bucle local
                counts[pid] = counts.GetValueOrDefault(pid) + 1;
            }

            return counts.Select(kv => Describe(kv.Key, kv.Value)).GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new NetApp(g.Key, g.First().Pid, g.Sum(a => a.Connections), g.First().ExePath)).OrderByDescending(a => a.Connections).Take(8).ToList();
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static NetApp Describe(int pid, int connections)
    {
        string? path = BarNative.ProcessPath((uint)pid); string name = path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : $"Proceso {pid}";
        return new NetApp(name, pid, connections, path);
    }
}
