using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Audio por Core Audio (API documentada): volumen maestro, silencio y lista de salidas activas del dispositivo predeterminado.
/// El cambio de salida predeterminada usa <see cref="PolicyConfigSwitcher"/> (COM NO oficial, aislado, verificado y reversible).
/// </summary>
public sealed class AudioService : IAudioService
{
    private readonly PolicyConfigSwitcher _switcher = new();
    /// <summary>Salida predeterminada anterior al último cambio hecho por WinMac (para «volver a la anterior»).</summary>
    public string? PreviousDefaultId { get; private set; }

    public AudioState Read()
    {
        object? enumerator = null;
        try
        {
            enumerator = new MMDeviceEnumeratorClass();
            var en = (IMMDeviceEnumerator)enumerator;
            if (en.GetDefaultAudioEndpoint(0, 1, out var def) != 0 || def is null) return AudioState.None("No hay dispositivo de salida de audio");
            string? defId = null; def.GetId(out defId);
            if (!TryVolume(def, out var ep)) return AudioState.None("No se pudo leer el volumen del dispositivo");
            ep.GetMasterVolumeLevelScalar(out float level); ep.GetMute(out bool muted);
            return new AudioState(true, (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100), muted, ListOutputs(en, defId));
        }
        catch (Exception e) { return AudioState.None("Core Audio falló: " + e.GetType().Name); }
        finally { if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator); }
    }

    private static IReadOnlyList<AudioDevice> ListOutputs(IMMDeviceEnumerator en, string? defaultId)
    {
        var list = new List<AudioDevice>();
        try
        {
            if (en.EnumAudioEndpoints(0, 1 /* DEVICE_STATE_ACTIVE */, out var col) != 0 || col is null) return list;
            col.GetCount(out uint n);
            for (uint i = 0; i < n && i < 32; i++)
            {
                if (col.Item(i, out var dev) != 0 || dev is null) continue;
                dev.GetId(out var id); if (id is null) continue;
                list.Add(new AudioDevice(id, FriendlyName(dev) ?? "Dispositivo de audio", id == defaultId));
            }
        }
        catch (Exception) { /* la lista es opcional: el volumen sigue funcionando */ }
        return list;
    }

    private static string? FriendlyName(IMMDevice dev)
    {
        try
        {
            if (dev.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store is null) return null;
            var key = new Native.PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };   // PKEY_Device_FriendlyName
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.vt == 31 /* VT_LPWSTR */ && pv.p1 != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p1) : null; }
            finally { Native.PropVariantClear(ref pv); }
        }
        catch (Exception) { return null; }
    }

    private static bool TryVolume(IMMDevice dev, out IAudioEndpointVolume ep)
    {
        ep = null!; var iid = typeof(IAudioEndpointVolume).GUID;
        if (dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var o) != 0 || o is not IAudioEndpointVolume v) return false;
        ep = v; return true;
    }

    private ServiceResult WithEndpoint(Func<IAudioEndpointVolume, int> op, string what) => ServiceResult.Guard(() =>
    {
        var enumerator = new MMDeviceEnumeratorClass();
        try
        {
            if (((IMMDeviceEnumerator)(object)enumerator).GetDefaultAudioEndpoint(0, 1, out var def) != 0 || def is null) return ServiceResult.Unavailable("No hay dispositivo de salida de audio");
            if (!TryVolume(def, out var ep)) return ServiceResult.Unavailable("El dispositivo no permite controlar el volumen");
            int hr = op(ep);
            return hr == 0 ? ServiceResult.Ok : ServiceResult.Failed($"{what}: error 0x{hr:X8}");
        }
        finally { if (Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator); }
    }, what);

    public ServiceResult SetVolume(int percent) { var g = Guid.Empty; return WithEndpoint(ep => ep.SetMasterVolumeLevelScalar(Math.Clamp(percent, 0, 100) / 100f, ref g), "Volumen"); }
    public ServiceResult SetMuted(bool muted) { var g = Guid.Empty; return WithEndpoint(ep => ep.SetMute(muted, ref g), "Silencio"); }

    public ServiceResult CanSwitchOutput() => _switcher.Probe();

    public ServiceResult SetDefaultOutput(string deviceId) => ServiceResult.Guard(() =>
    {
        var before = Read(); var prev = before.Outputs.FirstOrDefault(o => o.IsDefault)?.Id;
        if (!before.Outputs.Any(o => o.Id == deviceId)) return ServiceResult.Failed("Esa salida ya no está disponible");
        var r = _switcher.SetDefault(deviceId); if (!r.IsOk) return r;
        var after = Read();
        if (after.Outputs.FirstOrDefault(o => o.IsDefault)?.Id == deviceId) { PreviousDefaultId = prev; return ServiceResult.Ok; }
        // Windows aceptó la llamada pero la salida no cambió: se deja como estaba y se avisa (nunca se da por hecho).
        if (prev is not null) _switcher.SetDefault(prev);
        return ServiceResult.Failed("Windows no aplicó el cambio de salida; se dejó la anterior");
    }, "Salida de audio");

    // ───────── COM de Core Audio (documentado). El orden de los métodos ES la tabla virtual: no reordenar ni quitar.
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection? devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore? store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out Native.PROPERTYKEY key);
        [PreserveSig] int GetValue(ref Native.PROPERTYKEY key, out Native.PROPVARIANT value);
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
