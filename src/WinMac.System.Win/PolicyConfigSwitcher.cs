using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// ÚNICO punto donde WinMac usa una interfaz COM NO documentada de Windows (<c>IPolicyConfig</c>), porque Windows no ofrece API pública para cambiar la salida de audio predeterminada.
/// Encapsulación: (1) <see cref="Probe"/> detecta compatibilidad sin cambiar nada; (2) si la interfaz no existe o falla, devuelve «no disponible» y la interfaz ofrece abrir Configuración;
/// (3) quien la usa (<see cref="AudioService"/>) verifica el resultado y revierte si Windows no aplicó el cambio.
/// </summary>
internal sealed class PolicyConfigSwitcher
{
    private static readonly Guid ClsidPolicyConfigClient = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
    private const string Unsupported = "Esta versión de Windows no permite cambiar la salida desde WinMac; usa Configuración de sonido";

    private static IPolicyConfig? Create()
    {
        var t = Type.GetTypeFromCLSID(ClsidPolicyConfigClient, false);
        return t is null ? null : Activator.CreateInstance(t) as IPolicyConfig;   // el cast hace el QueryInterface: null/InvalidCastException si la IID no existe
    }

    public ServiceResult Probe() => ServiceResult.Guard(() =>
    {
        object? pc = null;
        try { pc = Create(); return pc is null ? ServiceResult.Unavailable(Unsupported) : ServiceResult.Ok; }
        catch (Exception e) when (e is InvalidCastException or COMException) { return ServiceResult.Unavailable(Unsupported); }
        finally { if (pc is not null && Marshal.IsComObject(pc)) Marshal.ReleaseComObject(pc); }
    }, "Salida de audio");

    public ServiceResult SetDefault(string deviceId) => ServiceResult.Guard(() =>
    {
        IPolicyConfig? pc;
        try { pc = Create(); }
        catch (Exception e) when (e is InvalidCastException or COMException) { return ServiceResult.Unavailable(Unsupported); }
        if (pc is null) return ServiceResult.Unavailable(Unsupported);
        try
        {
            foreach (int role in new[] { 0 /* consola */, 1 /* multimedia */, 2 /* comunicaciones */ })
            {
                int hr = pc.SetDefaultEndpoint(deviceId, role);
                if (hr != 0) return ServiceResult.Failed($"Windows rechazó el cambio (0x{hr:X8})");
            }

            return ServiceResult.Ok;
        }
        finally { if (Marshal.IsComObject(pc)) Marshal.ReleaseComObject(pc); }
    }, "Salida de audio");

    /// <summary>Tabla virtual de IPolicyConfig (Windows 7 en adelante). Solo importa el ORDEN; los métodos que no usamos van con firma genérica.</summary>
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr deviceName, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr deviceName, int def, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr deviceName);
        [PreserveSig] int SetDeviceFormat(IntPtr deviceName, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(IntPtr deviceName, int def, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod(IntPtr deviceName, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr deviceName, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr deviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr deviceName, int bFx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr deviceName, int bFx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr deviceName, int visible);
    }
}
