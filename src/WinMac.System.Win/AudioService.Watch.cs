using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Aviso del sistema cuando cambia el volumen o el silencio de la salida predeterminada (Core Audio, documentado): <c>IAudioEndpointVolume::RegisterControlChangeNotify</c>
/// para el nivel y <c>IMMDeviceEnumerator::RegisterEndpointNotificationCallback</c> para seguir a la salida cuando cambia la predeterminada. Sin sondeo: Windows llama
/// cuando cambia (teclas multimedia, el control de volumen de Windows, otras apps o la propia barra). Las llamadas llegan en hilos de Windows; el que escucha debe
/// pasarlas a su hilo. Se deja de escuchar con <see cref="IDisposable.Dispose"/> (también al cerrar el proceso: Windows lo limpia solo).
/// </summary>
public sealed partial class AudioService
{
    /// <summary>Empieza a escuchar. <paramref name="changed"/> recibe (nivel 0–100, silenciado, nombre del dispositivo) solo cuando cambia el nivel o el silencio.
    /// Devuelve null con el motivo si este equipo no ofrece el aviso (sin salida de audio, servicio de audio parado…).</summary>
    public static IDisposable? WatchVolume(Action<int, bool, string?> changed, Action<string> log, out string? why)
    {
        var w = new VolumeWatcher(changed, log);
        try
        {
            if (!w.Start(out why)) { w.Dispose(); return null; }
            return w;
        }
        catch (Exception e) { w.Dispose(); why = "Core Audio: " + e.GetType().Name + $" 0x{e.HResult:X8}"; return null; }
    }

    private sealed class VolumeWatcher : IDisposable
    {
        private readonly Action<int, bool, string?> _changed; private readonly Action<string> _log;
        private readonly object _gate = new();
        private IMMDeviceEnumerator? _en; private IAudioEndpointVolume? _ep; private IntPtr _cbPtr; private VolumeCallback? _cb; private DeviceCallback? _dev;
        private string? _name; private int _lastLevel = -1; private bool _lastMuted; private bool _disposed;

        public VolumeWatcher(Action<int, bool, string?> changed, Action<string> log) { _changed = changed; _log = log; }

        public bool Start(out string? why)
        {
            _en = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            _dev = new DeviceCallback(this);
            int hr = _en.RegisterEndpointNotificationCallback(_dev);
            if (hr != 0) _log($"[audio] sin aviso de cambio de salida (0x{hr:X8}): se seguirá la salida actual");
            return Arm(out why);
        }

        /// <summary>Se engancha a la salida predeterminada actual (y se suelta de la anterior).</summary>
        private bool Arm(out string? why)
        {
            lock (_gate)
            {
                Disarm();
                why = null;
                if (_disposed || _en is null) { why = "cerrado"; return false; }
                if (_en.GetDefaultAudioEndpoint(0, 1, out var def) != 0 || def is null) { why = "No hay dispositivo de salida de audio"; return false; }
                if (!TryVolume(def, out var ep)) { why = "No se pudo abrir el volumen del dispositivo"; return false; }
                _name = FriendlyName(def);
                _cb = new VolumeCallback(this);
                _cbPtr = Marshal.GetComInterfaceForObject(_cb, typeof(IAudioEndpointVolumeCallback));
                int hr = ep.RegisterControlChangeNotify(_cbPtr);
                if (hr != 0) { Marshal.Release(_cbPtr); _cbPtr = IntPtr.Zero; _cb = null; why = $"Windows rechazó el aviso de volumen (0x{hr:X8})"; return false; }
                _ep = ep;
                ep.GetMasterVolumeLevelScalar(out float level); ep.GetMute(out bool muted);
                _lastLevel = (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100); _lastMuted = muted;   // el estado al empezar NO se anuncia: solo los cambios
                return true;
            }
        }

        private void Disarm()
        {
            if (_ep is not null && _cbPtr != IntPtr.Zero) { try { _ep.UnregisterControlChangeNotify(_cbPtr); } catch (Exception) { /* el dispositivo ya no existe */ } }
            if (_cbPtr != IntPtr.Zero) { Marshal.Release(_cbPtr); _cbPtr = IntPtr.Zero; }
            if (_ep is not null && Marshal.IsComObject(_ep)) Marshal.ReleaseComObject(_ep);
            _ep = null; _cb = null;
        }

        internal void OnNotify(IntPtr data)
        {
            if (data == IntPtr.Zero) return;
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext (16 bytes), BOOL bMuted, float fMasterVolume, …
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            int pct = (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100);
            string? name;
            lock (_gate)
            {
                if (_disposed || (pct == _lastLevel && muted == _lastMuted)) return;   // cambios solo de canales (balance) no se anuncian
                _lastLevel = pct; _lastMuted = muted; name = _name;
            }

            try { _changed(pct, muted, name); } catch (Exception e) { _log("[audio] aviso de volumen: " + e.GetType().Name + ": " + e.Message); }
        }

        internal void OnDefaultChanged()
        {
            // No se toca Core Audio dentro de su propio aviso: se rehace en otro hilo.
            Task.Run(() =>
            {
                try { if (!Arm(out var why)) _log("[audio] salida nueva sin aviso de volumen: " + why); }
                catch (Exception e) { _log("[audio] al seguir la salida nueva: " + e.GetType().Name + ": " + e.Message); }
            });
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return; _disposed = true;
                Disarm();
                if (_en is not null)
                {
                    if (_dev is not null) { try { _en.UnregisterEndpointNotificationCallback(_dev); } catch (Exception) { /* proceso cerrando */ } }
                    if (Marshal.IsComObject(_en)) Marshal.ReleaseComObject(_en);
                    _en = null;
                }
            }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class VolumeCallback(VolumeWatcher owner) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr data) { owner.OnNotify(data); return 0; }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class DeviceCallback(VolumeWatcher owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string id, int state) => 0;
        public int OnDeviceAdded(string id) => 0;
        public int OnDeviceRemoved(string id) => 0;
        public int OnDefaultDeviceChanged(int flow, int role, string? id) { if (flow == 0 && role == 1) owner.OnDefaultChanged(); return 0; }
        public int OnPropertyValueChanged(string id, Native.PROPERTYKEY key) => 0;
    }

    [ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notifyData);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, Native.PROPERTYKEY key);
    }
}
