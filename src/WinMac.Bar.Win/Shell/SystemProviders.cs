using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using WinMac.Apps.Win;
using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Shell;

/// <summary>Lecturas del sistema para la barra. Cada proveedor está aislado: ante cualquier fallo devuelve «no disponible» y nunca lanza.</summary>
public static class SystemProviders
{
    private static DateTime _devicesAt = DateTime.MinValue; private static bool? _hasInternalBattery;

    /// <summary>¿Hay una batería DEL EQUIPO? (no un SAI). Null = no se pudo saber. Se vuelve a mirar cada 30 s (se enchufa/desenchufa un SAI o una batería extraíble).</summary>
    private static bool? HasInternalBattery()
    {
        if (DateTime.UtcNow - _devicesAt < TimeSpan.FromSeconds(30)) return _hasInternalBattery;
        _devicesAt = DateTime.UtcNow;
        try { _hasInternalBattery = BatteryDetailsProvider.DevicePaths() is { } paths ? BatteryDeviceClass.HasInternal(paths) : null; }
        catch (Exception) { _hasInternalBattery = null; }
        return _hasInternalBattery;
    }

    /// <summary>
    /// It7: energía del PC con la API real de Windows (GetSystemPowerStatus) más la clasificación de dispositivos: un SAI/UPS conectado a un sobremesa
    /// hace que Windows informe «batería» (y un 100 % que no es del PC); aquí solo cuenta una batería del equipo. Sin ella, Present = false (y Plugged = línea de corriente).
    /// Limitación: portátil CON SAI conectado → Windows mezcla ambas; se usa su cifra.
    /// </summary>
    public static BatteryInfo Battery()
    {
        try
        {
            if (!BarNative.GetSystemPowerStatus(out var s)) return BatteryInfo.None;
            bool ac = s.ACLineStatus == 1;   // 0 = batería, 1 = corriente, 255 = desconocido
            bool flagsSayBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255 && s.BatteryLifePercent <= 100;   // 128 = sin batería; 255 = desconocido
            bool present = flagsSayBattery && HasInternalBattery() != false;
            if (!present) return ac ? BatteryInfo.NoBatteryOnAc : BatteryInfo.None;
            return new BatteryInfo(true, s.BatteryLifePercent, (s.BatteryFlag & 8) != 0, ac, s.BatteryLifeTime is > 0 and < 0x7FFFFFFF ? (int)s.BatteryLifeTime : null);   // 0xFFFFFFFF = Windows does not know
        }
        catch (Exception) { return BatteryInfo.None; }
    }

    /// <summary>Red: Wi-Fi o Ethernet con puerta de enlace; se ignoran túneles, virtuales y VPN. Sin ninguna → Offline. La señal del Wi-Fi no se lee todavía (pendiente: API WLAN).</summary>
    public static NetworkInfo Network()
    {
        try
        {
            NetKind best = NetKind.Offline;
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up) continue;
                if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;
                string d = n.Description ?? ""; if (d.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || d.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) || d.Contains("VPN", StringComparison.OrdinalIgnoreCase) || d.Contains("VMware", StringComparison.OrdinalIgnoreCase) || d.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)) continue;
                if (!n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any))) continue;
                var k = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? NetKind.Wifi : NetKind.Ethernet;
                if (best == NetKind.Offline || k == NetKind.Ethernet) best = k;   // el cable prevalece sobre Wi-Fi
            }
            return new NetworkInfo(best);
        }
        catch (Exception) { return NetworkInfo.Unknown; }
    }

    /// <summary>Volumen maestro del dispositivo de salida predeterminado (Core Audio, documentado).</summary>
    public static VolumeInfo Volume()
    {
        object? enumerator = null, device = null, vol = null;
        try
        {
            enumerator = new MMDeviceEnumeratorClass();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var dev) != 0 || dev is null) return VolumeInfo.None;
            device = dev; var iid = typeof(IAudioEndpointVolume).GUID;
            if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out vol) != 0 || vol is not IAudioEndpointVolume ep) return VolumeInfo.None;
            if (ep.GetMasterVolumeLevelScalar(out float level) != 0) return VolumeInfo.None;
            ep.GetMute(out bool muted);
            return new VolumeInfo(true, (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100), muted);
        }
        catch (Exception) { return VolumeInfo.None; }
        finally { foreach (var o in new[] { vol, device, enumerator }) if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorClass { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

/// <summary>
/// Nombre de la app en primer plano. Se alimenta de EVENT_SYSTEM_FOREGROUND (sin sondeo); el nombre se resuelve en un hilo de trabajo (los nombres de apps empaquetadas
/// pasan por el Shell y pueden tardar) y la barra lee siempre el último valor. Debe crearse en el hilo del bucle de mensajes.
/// </summary>
public sealed class ActiveAppProvider : IDisposable
{
    private readonly BarNative.WinEventProc _proc; private IntPtr _hook1, _hook2; private volatile bool _pending = true; private volatile string? _name; private int _changed = 1;
    private readonly uint _self = (uint)Environment.ProcessId; private long _generation;
    public string Desktop { get; init; } = "Escritorio";

    public ActiveAppProvider()
    {
        _proc = (h, ev, hwnd, idObject, idChild, t, time) => { if (idObject == 0 && idChild == 0) _pending = true; };
        _hook1 = BarNative.SetWinEventHook(BarNative.EVENT_SYSTEM_FOREGROUND, BarNative.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, BarNative.WINEVENT_OUTOFCONTEXT);
    }

    public string? Name => _name;
    public bool TakeChanged() => Interlocked.Exchange(ref _changed, 0) != 0;

    /// <summary>Llamar desde el bucle principal: si hubo un cambio de primer plano, resuelve el nombre sin bloquear.</summary>
    public void Pump()
    {
        if (!_pending) return; _pending = false;
        IntPtr h = BarNative.GetForegroundWindow(); if (h == IntPtr.Zero) return;
        BarNative.GetWindowThreadProcessId(h, out uint pid); if (pid == _self) return;   // un clic en la propia barra no cambia la app activa
        var cls = new StringBuilder(128); BarNative.GetClassNameW(h, cls, 128); string c = cls.ToString();
        if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") { Set(Desktop); return; }
        long gen = Interlocked.Increment(ref _generation);
        Task.Run(() =>
        {
            string? n = null;
            try
            {
                var path = BarNative.ProcessPath(pid);
                var id = WindowIdentityResolver.Resolve(h, pid, path);
                n = id is null ? null : DisplayNames.Resolve(id);
            }
            catch (Exception) { }
            if (!string.IsNullOrWhiteSpace(n) && Interlocked.Read(ref _generation) == gen) Set(n!);
        });
    }

    private void Set(string n) { if (_name != n) { _name = n; Interlocked.Exchange(ref _changed, 1); } }
    public void Dispose() { if (_hook1 != IntPtr.Zero) BarNative.UnhookWinEvent(_hook1); _hook1 = IntPtr.Zero; if (_hook2 != IntPtr.Zero) BarNative.UnhookWinEvent(_hook2); _hook2 = IntPtr.Zero; }
}
