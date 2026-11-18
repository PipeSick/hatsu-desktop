using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Mezclador: micrófono (entrada) y volumen por aplicación (sesiones de Core Audio). Comparte con el resto del servicio los mismos COM y el mismo cambiador de dispositivo predeterminado.
/// NO hay salida por aplicación: Windows solo la ofrece por una interfaz interna sin contrato estable (<c>AudioPolicyConfig</c>) que cambia entre compilaciones; el panel ofrece abrir
/// «Volumen de aplicaciones» de Windows, donde sí se elige.
/// </summary>
public sealed partial class AudioService
{
    // ───────── Entrada
    public AudioInputState ReadInput()
    {
        object? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumeratorClass(); var en = (IMMDeviceEnumerator)enumerator;
            if (en.GetDefaultAudioEndpoint(1, 1, out var def) != 0 || def is null) return AudioInputState.None("No hay micrófono");
            string? id = null; def.GetId(out id); var devices = ListDevices(en, 1, id);
            if (!TryVolume(def, out var ep)) return new AudioInputState(true, 0, false, false, devices, "Este micrófono no permite ajustar el volumen desde Windows");
            ep.GetMasterVolumeLevelScalar(out float level); ep.GetMute(out bool muted);
            return new AudioInputState(true, (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100), muted, true, devices);
        }
        catch (Exception e) { return AudioInputState.None("Core Audio falló: " + e.GetType().Name); }
        finally { if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator); }
    }

    public ServiceResult SetInputVolume(int percent) { var g = Guid.Empty; return WithEndpoint(ep => ep.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref g), "Volumen del micrófono", flow: 1); }
    public ServiceResult SetInputMuted(bool muted) { var g = Guid.Empty; return WithEndpoint(ep => ep.SetMute(muted, ref g), "Silencio del micrófono", flow: 1); }

    public ServiceResult SetDefaultInput(string deviceId) => ServiceResult.Guard(() =>
    {
        var before = ReadInput(); var prev = before.Devices.FirstOrDefault(d => d.IsDefault)?.Id;
        if (!before.Devices.Any(d => d.Id == deviceId)) return ServiceResult.Failed("Ese micrófono ya no está disponible");
        var r = _switcher.SetDefault(deviceId); if (!r.IsOk) return r;
        if (ReadInput().Devices.FirstOrDefault(d => d.IsDefault)?.Id == deviceId) return ServiceResult.Ok;
        if (prev is not null) _switcher.SetDefault(prev);   // como con la salida: si Windows no lo aplicó, se deja el anterior y se avisa
        return ServiceResult.Failed("Windows no aplicó el cambio de micrófono; se dejó el anterior");
    }, "Micrófono");

    // ───────── Sesiones por aplicación
    /// <summary>
    /// Las aplicaciones con audio de TODAS las salidas activas, no solo de la predeterminada: así se ve y se controla una app aunque suene por otro dispositivo (auriculares, monitor, TV…).
    /// Una app que suena en varias salidas es una sola fila; su volumen es el de la salida predeterminada (o el de la primera) y el cambio se aplica a todas sus sesiones.
    /// </summary>
    public AudioSessionsState ReadSessions()
    {
        object? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumeratorClass(); var en = (IMMDeviceEnumerator)enumerator;
            string? defId = null; if (en.GetDefaultAudioEndpoint(0, 1, out var def) == 0 && def is not null) { def.GetId(out defId); if (Marshal.IsComObject(def)) Marshal.ReleaseComObject(def); }
            var groups = new Dictionary<string, SessionAcc>(); int devices = 0;
            ForEachRenderDevice(en, (dev, id, name) =>
            {
                devices++; bool isDefault = id == defId;
                ForEachSession(dev, (ctl, vol, pid, system) =>
                {
                    ctl.GetState(out int state); if (state == 2 /* AudioSessionStateExpired */) return;
                    vol.GetMasterVolume(out float v); vol.GetMute(out bool m);
                    string key = system ? "system" : "pid:" + pid;
                    if (!groups.TryGetValue(key, out var g)) groups[key] = g = new SessionAcc(pid, system);
                    g.Add(v, m, state == 1, isDefault, name);
                });
            });
            if (devices == 0) return AudioSessionsState.None("No hay dispositivo de salida de audio");
            var list = groups.Select(kv => Describe(kv.Key, kv.Value)).OrderByDescending(s => s.Active).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return new AudioSessionsState(true, list);
        }
        catch (Exception e) { return AudioSessionsState.None("No se pudieron leer las sesiones de audio: " + e.GetType().Name); }
        finally { if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>Acumula las sesiones de un mismo proceso a través de las distintas salidas.</summary>
    private sealed class SessionAcc(int pid, bool system)
    {
        public int Pid { get; } = pid; public bool System { get; } = system;
        public float Volume; public bool HasVolume, VolumeFromDefault, Muted = true, Active;
        private readonly List<(string Name, bool Active)> _devices = [];
        public void Add(float v, bool muted, bool active, bool isDefault, string device)
        {
            if (!HasVolume || (isDefault && !VolumeFromDefault)) { Volume = v; HasVolume = true; VolumeFromDefault = isDefault; }
            Muted &= muted; Active |= active;
            int at = _devices.FindIndex(d => d.Name == device);
            if (at < 0) _devices.Add((device, active)); else if (active && !_devices[at].Active) _devices[at] = (device, true);
        }

        /// <summary>Nombres de las salidas: primero donde suena ahora.</summary>
        public string DeviceText => string.Join(" · ", _devices.OrderByDescending(d => d.Active).Select(d => d.Name));
    }

    /// <summary>Cada salida activa (render), con su identificador y nombre. Libera los objetos COM de cada dispositivo.</summary>
    private static void ForEachRenderDevice(IMMDeviceEnumerator en, Action<IMMDevice, string, string> visit)
    {
        if (en.EnumAudioEndpoints(0, 1 /* DEVICE_STATE_ACTIVE */, out var col) != 0 || col is null) return;
        col.GetCount(out uint n);
        for (uint i = 0; i < n && i < 32; i++)
        {
            if (col.Item(i, out var dev) != 0 || dev is null) continue;
            try
            {
                dev.GetId(out var id); if (id is null) continue;
                try { visit(dev, id, FriendlyName(dev) ?? "Dispositivo de audio"); } catch (Exception) { /* una salida sin gestor de sesiones no impide ver las demás */ }
            }
            finally { if (Marshal.IsComObject(dev)) Marshal.ReleaseComObject(dev); }
        }
    }

    private static AudioSession Describe(string id, SessionAcc g)
    {
        int pid = g.Pid; bool system = g.System; float vol = g.Volume; bool muted = g.Muted, active = g.Active;
        string name = system ? "Sonidos del sistema" : $"Proceso {pid}"; string? path = null;
        if (!system)
        {
            path = Shell.ProcessPath(pid);
            try
            {
                using var p = Process.GetProcessById(pid);
                var desc = path is null ? null : FileVersionInfo.GetVersionInfo(path).FileDescription;
                name = !string.IsNullOrWhiteSpace(desc) ? desc! : (path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : p.ProcessName);
            }
            catch (Exception) { if (path is not null) name = System.IO.Path.GetFileNameWithoutExtension(path); }
        }

        return new AudioSession(id, pid, name, path, (int)Math.Round(Math.Clamp(vol, 0f, 1f) * 100), muted, system, active, g.DeviceText);
    }

    public ServiceResult SetSessionVolume(string sessionId, int percent) => OnSessions(sessionId, (vol, _) => { var g = Guid.Empty; return vol.SetMasterVolume(Math.Clamp(percent, 0, 100) / 100f, ref g); }, "Volumen de la aplicación");
    public ServiceResult SetSessionMuted(string sessionId, bool muted) => OnSessions(sessionId, (vol, _) => { var g = Guid.Empty; return vol.SetMute(muted, ref g); }, "Silencio de la aplicación");

    /// <summary>Aplica la operación a TODAS las sesiones del proceso (una app puede tener varias), reenumerando: el identificador no guarda punteros COM.</summary>
    private ServiceResult OnSessions(string sessionId, Func<ISimpleAudioVolume, IAudioSessionControl2, int> op, string what) => ServiceResult.Guard(() =>
    {
        object? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumeratorClass(); int applied = 0, failed = 0, devices = 0;
            ForEachRenderDevice((IMMDeviceEnumerator)enumerator, (dev, _, _) =>
            {
                devices++;
                ForEachSession(dev, (ctl, vol, pid, system) =>
                {
                    if ((system ? "system" : "pid:" + pid) != sessionId) return;
                    if (op(vol, ctl) == 0) applied++; else failed++;
                });
            });
            if (devices == 0) return ServiceResult.Unavailable("No hay dispositivo de salida de audio");
            if (applied == 0 && failed == 0) return ServiceResult.Failed("Esa aplicación ya no tiene sesión de audio");
            return failed == 0 ? ServiceResult.Ok : ServiceResult.Failed($"{what}: Windows rechazó el cambio en {failed} sesión(es)");
        }
        finally { if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator); }
    }, what);

    private static void ForEachSession(IMMDevice device, Action<IAudioSessionControl2, ISimpleAudioVolume, int, bool> visit)
    {
        var iid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref iid, 23, IntPtr.Zero, out var mgrObj) != 0 || mgrObj is not IAudioSessionManager2 mgr) throw new InvalidOperationException("sin gestor de sesiones");
        object? enumObj = null;
        try
        {
            if (mgr.GetSessionEnumerator(out var sessions) != 0 || sessions is null) return; enumObj = sessions;
            sessions.GetCount(out int n);
            for (int i = 0; i < n && i < 128; i++)
            {
                object? ctlObj = null;
                try
                {
                    if (sessions.GetSession(i, out var ctl) != 0 || ctl is null) continue; ctlObj = ctl;
                    if (ctl is not IAudioSessionControl2 c2 || ctl is not ISimpleAudioVolume vol) continue;
                    c2.GetProcessId(out uint pid); bool system = c2.IsSystemSoundsSession() == 0;
                    visit(c2, vol, (int)pid, system);
                }
                finally { if (ctlObj is not null && Marshal.IsComObject(ctlObj)) Marshal.ReleaseComObject(ctlObj); }
            }
        }
        finally { if (enumObj is not null && Marshal.IsComObject(enumObj)) Marshal.ReleaseComObject(enumObj); if (Marshal.IsComObject(mgrObj)) Marshal.ReleaseComObject(mgrObj); }
    }

    // ───────── COM (documentado). El orden de los métodos ES la tabla virtual; las interfaces derivadas repiten los métodos de la base.
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr groupId, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object ctl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr groupId, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object vol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator? sessions);
        [PreserveSig] int RegisterSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notify);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notify);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notify);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object? session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
        // IAudioSessionControl2
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static class Shell
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, System.Text.StringBuilder name, ref uint size);

        public static string? ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid); if (h == IntPtr.Zero) return null;
            try { var sb = new System.Text.StringBuilder(1024); uint n = (uint)sb.Capacity; return QueryFullProcessImageNameW(h, 0, sb, ref n) ? sb.ToString() : null; } finally { CloseHandle(h); }
        }
    }
}
