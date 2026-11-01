using System.Runtime.InteropServices;
using Windows.Devices.Radios;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Wi-Fi por la API WLAN nativa (<c>wlanapi</c>, documentada): estado, SSID y señal, redes visibles y conexión a redes con perfil guardado. Sin administrador.
/// No introduce contraseñas ni crea perfiles: una red nueva se conecta desde Configuración (el panel lo indica). Las estructuras se leen por desplazamientos fijos del SDK
/// (<c>WLAN_AVAILABLE_NETWORK</c> = 628 bytes) y cualquier error de la API se traduce a «no disponible» con su motivo.
/// </summary>
public sealed class WifiService : IWifiService
{
    private const int AvailableNetworkSize = 628, ListHeader = 8, InterfaceInfoSize = 532, BssEntrySize = 360;
    private const uint FlagConnected = 1, FlagHasProfile = 2;

    private static ServiceResult Code(uint rc, string what) => rc switch
    {
        0 => ServiceResult.Ok,
        5 => ServiceResult.Denied($"{what}: Windows denegó el acceso"),
        1062 => ServiceResult.Unavailable($"{what}: el servicio Wi-Fi de Windows no está en ejecución"),
        _ => ServiceResult.Failed($"{what}: error {rc}"),
    };

    private static bool Open(out IntPtr handle, out Guid iface, out ServiceResult problem)
    {
        handle = IntPtr.Zero; iface = Guid.Empty; problem = ServiceResult.Ok;
        uint rc = Native.WlanOpenHandle(2, IntPtr.Zero, out _, out handle);
        if (rc != 0) { problem = Code(rc, "Wi-Fi"); return false; }
        rc = Native.WlanEnumInterfaces(handle, IntPtr.Zero, out var list);
        if (rc != 0) { Native.WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; problem = Code(rc, "Wi-Fi"); return false; }
        try
        {
            int n = Marshal.ReadInt32(list);
            if (n <= 0) { Native.WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; problem = ServiceResult.Unavailable("No hay adaptador Wi-Fi"); return false; }
            var bytes = new byte[16]; Marshal.Copy(list + ListHeader, bytes, 0, 16); iface = new Guid(bytes);   // la primera interfaz
            return true;
        }
        finally { Native.WlanFreeMemory(list); }
    }

    public WifiState Read(bool scan)
    {
        if (!Open(out var h, out var iface, out var problem)) return WifiState.None(problem.Message ?? "Wi-Fi no disponible");
        try
        {
            uint scanRc = scan ? Native.WlanScan(h, ref iface, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) : 0;   // asíncrono: los resultados aparecen en la lectura siguiente
            bool? radio = ReadRadioOn(h, iface);
            var nets = ReadNetworks(h, iface, out uint listRc);
            if (listRc == 0) nets = AddBssInfo(h, iface, nets);
            // Windows 11 exige el permiso de ubicación para ver redes (WLAN devuelve «acceso denegado»): se dice, no se finge una lista vacía.
            if (listRc == 5 || (scan && scanRc == 5)) return new WifiState(true, false, null, 0, [], LocationReason, radio);
            if (listRc != 0) return new WifiState(true, false, null, 0, [], $"Wi-Fi: no se pudo leer la lista de redes (error {listRc})", radio);
            var cur = nets.FirstOrDefault(n => n.Connected);
            return new WifiState(true, cur is not null, cur?.Ssid, cur?.Signal ?? 0, nets, RadioOn: radio);
        }
        catch (Exception e) { return WifiState.None("Wi-Fi: " + e.GetType().Name); }
        finally { Native.WlanCloseHandle(h, IntPtr.Zero); }
    }

    private static bool? ReadRadioOn(IntPtr h, Guid iface)
    {
        if (Native.WlanQueryInterface(h, ref iface, 4 /* wlan_intf_opcode_radio_state */, IntPtr.Zero, out _, out var data, IntPtr.Zero) != 0) return null;
        try
        {
            if (Marshal.ReadInt32(data) < 1) return null;                                      // dwNumberOfPhys
            int software = Marshal.ReadInt32(data, 8), hardware = Marshal.ReadInt32(data, 12);  // dot11_radio_state_on = 1
            return software == 1 && hardware == 1;
        }
        finally { Native.WlanFreeMemory(data); }
    }

    /// <summary>Motivo mostrado cuando Windows niega las redes por falta del permiso de ubicación (el panel ofrece abrir esa página).</summary>
    public const string LocationReason = "Windows necesita el permiso de ubicación para mostrar las redes Wi-Fi";

    private static IReadOnlyList<WifiNetwork> ReadNetworks(IntPtr h, Guid iface, out uint rc)
    {
        var result = new List<WifiNetwork>();
        rc = Native.WlanGetAvailableNetworkList(h, ref iface, 0, IntPtr.Zero, out var list);
        if (rc != 0) return result;
        try
        {
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < count && i < 128; i++)
            {
                var p = list + ListHeader + i * AvailableNetworkSize;
                string profile = Marshal.PtrToStringUni(p, 256).Split('\0')[0];
                int len = Math.Clamp(Marshal.ReadInt32(p, 512), 0, 32); var raw = new byte[len]; Marshal.Copy(p + 516, raw, 0, len);
                string ssid = System.Text.Encoding.UTF8.GetString(raw);
                int signal = Marshal.ReadInt32(p, 604); bool secured = Marshal.ReadInt32(p, 608) != 0; uint flags = (uint)Marshal.ReadInt32(p, 620);
                if (string.IsNullOrWhiteSpace(ssid)) continue;   // redes ocultas
                result.Add(new WifiNetwork(ssid, signal, secured, (flags & FlagConnected) != 0, (flags & FlagHasProfile) != 0, profile.Length > 0 ? profile : null));
            }
        }
        finally { Native.WlanFreeMemory(list); }
        // una fila por SSID: la conectada o, si no, la de mejor señal
        return result.GroupBy(n => n.Ssid).Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).First())
            .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ToList();
    }

    /// <summary>
    /// Potencia (RSSI, dBm) y frecuencia de cada red, de <c>WlanGetNetworkBssList</c> (WLAN_BSS_ENTRY = 360 bytes; RSSI en 56, frecuencia central en kHz en 92). Por SSID se toma el punto de acceso
    /// más fuerte. Si la lectura falla la lista queda como estaba: son datos de detalle y nunca deben quitar las redes.
    /// </summary>
    private static IReadOnlyList<WifiNetwork> AddBssInfo(IntPtr h, Guid iface, IReadOnlyList<WifiNetwork> nets)
    {
        try
        {
            if (Native.WlanGetNetworkBssList(h, ref iface, IntPtr.Zero, 3 /* dot11_BSS_type_any */, false, IntPtr.Zero, out var list) != 0) return nets;
            try
            {
                int count = Marshal.ReadInt32(list, 4); var best = new Dictionary<string, (int Rssi, int Khz)>();
                for (int i = 0; i < count && i < 512; i++)
                {
                    var p = list + 8 + i * BssEntrySize;
                    int len = Math.Clamp(Marshal.ReadInt32(p, 0), 0, 32); var raw = new byte[len]; Marshal.Copy(p + 4, raw, 0, len);
                    string ssid = System.Text.Encoding.UTF8.GetString(raw); if (string.IsNullOrWhiteSpace(ssid)) continue;
                    int rssi = Marshal.ReadInt32(p, 56), khz = Marshal.ReadInt32(p, 92);
                    if (!best.TryGetValue(ssid, out var cur) || rssi > cur.Rssi) best[ssid] = (rssi, khz);
                }
                return nets.Select(n => best.TryGetValue(n.Ssid, out var b) ? n with { RssiDbm = b.Rssi is < 0 and > -120 ? b.Rssi : null, FrequencyMhz = b.Khz > 0 ? b.Khz / 1000 : null } : n).ToList();
            }
            finally { Native.WlanFreeMemory(list); }
        }
        catch (Exception) { return nets; }
    }

    public ServiceResult Connect(string profileName, string ssid) => ServiceResult.Guard(() =>
    {
        if (string.IsNullOrWhiteSpace(profileName)) return ServiceResult.Unavailable("Esta red no tiene un perfil guardado: conéctala desde Configuración");
        if (!Open(out var h, out var iface, out var problem)) return problem;
        try
        {
            var p = new Native.WLAN_CONNECTION_PARAMETERS { Mode = 0 /* wlan_connection_mode_profile */, Profile = profileName, BssType = 1 /* infraestructura */, Flags = 0 };
            return Code(Native.WlanConnect(h, ref iface, ref p, IntPtr.Zero), $"Conectar a «{ssid}»");
        }
        finally { Native.WlanCloseHandle(h, IntPtr.Zero); }
    }, "Wi-Fi");

    public ServiceResult Disconnect() => ServiceResult.Guard(() =>
    {
        if (!Open(out var h, out var iface, out var problem)) return problem;
        try { return Code(Native.WlanDisconnect(h, ref iface, IntPtr.Zero), "Desconectar"); }
        finally { Native.WlanCloseHandle(h, IntPtr.Zero); }
    }, "Wi-Fi");

    public ServiceResult SetRadio(bool on) => RadioHelper.SetState(RadioKind.WiFi, on, "Radio Wi-Fi");
}
