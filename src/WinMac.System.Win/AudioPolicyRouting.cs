using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// WM-035 (EXPERIMENTAL): salida de audio por aplicación con la interfaz INTERNA de Windows «Windows.Media.Internal.AudioPolicyConfig» (IAudioPolicyConfigFactory),
/// la que usa la página «Volumen de la aplicación y preferencias de dispositivo» de Configuración (y proyectos abiertos como EarTrumpet, MIT). No es una API pública:
/// <list type="bullet">
/// <item>Se usa SOLO en un proceso auxiliar (<c>WinMac.Bar.App --audio-route</c>): si en una versión de Windows la interfaz cambiara, falla ese proceso, no la barra.</item>
/// <item>Se prueban las dos variantes conocidas (Windows 10 1803–21H1 y Windows 11 / 21H2+). Si ninguna existe, «no disponible» y no se ofrece nada.</item>
/// <item>Cada cambio se comprueba leyéndolo de vuelta; si Windows no lo guardó, se informa del fallo (nunca se da por aplicado).</item>
/// <item>La preferencia la guarda Windows por aplicación (como al elegirla en Configuración): sobrevive a que la app reinicie su sesión de audio. «Predeterminada» la borra.</item>
/// </list>
/// No modifica archivos del sistema ni inyecta nada en otros procesos.
/// </summary>
public static unsafe class AudioPolicyRouting
{
    private static readonly Guid Iid21H2 = new("ab3d4648-e242-459f-b02f-541c70306324"), IidDownlevel = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
    private const string ClassName = "Windows.Media.Internal.AudioPolicyConfig", MmdevToken = @"\\?\SWD#MMDEVAPI#", RenderInterface = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const int SlotSet = 25, SlotGet = 26;   // tras los 6 de IInspectable y 19 métodos sin uso (mismo orden en las dos variantes)
    private const int ERender = 0, EConsole = 0, EMultimedia = 1;

    /// <summary>La fábrica, o null con el motivo. Solo lectura: no cambia nada.</summary>
    public static IntPtr Factory(out string? why)
    {
        why = null; IntPtr cls = IntPtr.Zero;
        try
        {
            if (Environment.OSVersion.Version.Build < 17134) { why = "necesita Windows 10 1803 o posterior"; return IntPtr.Zero; }
            if (WindowsCreateString(ClassName, ClassName.Length, out cls) != 0) { why = "no se pudo crear la cadena de clase"; return IntPtr.Zero; }
            foreach (var iid in Environment.OSVersion.Version.Build >= 21390 ? new[] { Iid21H2, IidDownlevel } : new[] { IidDownlevel, Iid21H2 })
            {
                var g = iid;
                if (RoGetActivationFactory(cls, ref g, out var f) == 0 && f != IntPtr.Zero) return f;
            }
            why = "esta versión de Windows no ofrece la interfaz de salida por aplicación"; return IntPtr.Zero;
        }
        catch (Exception e) { why = e.GetType().Name; return IntPtr.Zero; }
        finally { if (cls != IntPtr.Zero) WindowsDeleteString(cls); }
    }

    /// <summary>Salida guardada para el proceso (id de MMDevice) o null si sigue la predeterminada del sistema.</summary>
    public static string? Get(IntPtr factory, int pid)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr*, int>)(*(IntPtr**)factory)[SlotGet];
        IntPtr h = IntPtr.Zero;
        int hr = fn(factory, (uint)pid, ERender, EMultimedia, &h);
        if (hr != 0) throw new COMException("GetPersistedDefaultAudioEndpoint", hr);
        try { return h == IntPtr.Zero ? null : Unpack(Read(h)); }
        finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
    }

    /// <summary>Aplica (multimedia y consola, como Configuración) y comprueba leyendo de vuelta. Devuelve null si quedó aplicado, o el motivo.</summary>
    public static string? Set(IntPtr factory, int pid, string? mmdeviceId)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)(*(IntPtr**)factory)[SlotSet];
        IntPtr h = IntPtr.Zero;
        try
        {
            if (mmdeviceId is not null) { string full = MmdevToken + mmdeviceId + RenderInterface; if (WindowsCreateString(full, full.Length, out h) != 0) return "no se pudo preparar el dispositivo"; }
            foreach (var role in new[] { EMultimedia, EConsole })
            {
                int hr = fn(factory, (uint)pid, ERender, role, h);
                if (hr != 0) return $"Windows rechazó el cambio (0x{hr:X8})";
            }
        }
        finally { if (h != IntPtr.Zero) WindowsDeleteString(h); }
        var now = Get(factory, pid);
        return string.Equals(now, mmdeviceId, StringComparison.OrdinalIgnoreCase) ? null : "Windows no guardó el cambio (al leerlo de vuelta sigue " + (now is null ? "la predeterminada" : "otra salida") + ")";
    }

    public static void Release(IntPtr factory) { if (factory != IntPtr.Zero) Marshal.Release(factory); }

    /// <summary>«\\?\SWD#MMDEVAPI#{id}#{interfaz}» → «{id}» (el id que dan las listas de dispositivos).</summary>
    public static string Unpack(string id)
    {
        if (id.StartsWith(MmdevToken, StringComparison.OrdinalIgnoreCase)) id = id[MmdevToken.Length..];
        int k = id.IndexOf("#{", StringComparison.Ordinal); return k > 0 ? id[..k] : id;
    }

    private static string Read(IntPtr h) { var p = WindowsGetStringRawBuffer(h, out uint len); return p == IntPtr.Zero ? "" : new string((char*)p, 0, (int)len); }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string s, int len, out IntPtr h);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr h);
    [DllImport("combase.dll")] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr h, out uint len);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr cls, ref Guid iid, out IntPtr factory);
}
