using System.Runtime.InteropServices;
using WinMac.Bar;

namespace WinMac.Bar.Win.Shell;

/// <summary>
/// WM-010: details of the battery from documented sources, read on demand (when the Energy panel opens), off the UI thread:
/// <list type="bullet">
/// <item>capacities and rate: <c>Windows.Devices.Power.Battery.AggregateBattery.GetReport()</c>;</item>
/// <item>cycle count and chemistry: <c>IOCTL_BATTERY_QUERY_INFORMATION</c> (BatteryInformation) on each battery device of GUID_DEVCLASS_BATTERY, as Windows' own
/// battery report does. No administrator rights are needed; a value the driver does not report stays null.</item>
/// </list>
/// </summary>
internal static class BatteryDetailsProvider
{
    public static BatteryDetails Read()
    {
        int? design = null, full = null, remaining = null, rate = null, cycles = null; string? chem = null;
        try
        {
            var r = global::Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            design = Positive(r.DesignCapacityInMilliwattHours); full = Positive(r.FullChargeCapacityInMilliwattHours); remaining = Positive(r.RemainingCapacityInMilliwattHours);
            rate = r.ChargeRateInMilliwatts is { } cr && cr != 0 ? cr : null;
        }
        catch (Exception) { /* no battery report on this machine */ }

        try { (cycles, chem) = CyclesFromDriver(); } catch (Exception) { }
        return new BatteryDetails(design, full, remaining, rate, cycles, chem);
    }

    /// <summary>
    /// It7: rutas de interfaz de los dispositivos de la clase Batería presentes (batería del equipo y, si los hay, SAI/UPS conectados por USB/HID).
    /// Null si Windows no deja enumerar (el llamante usa entonces solo GetSystemPowerStatus). Solo lectura; no necesita derechos de administrador.
    /// </summary>
    internal static List<string>? DevicePaths()
    {
        var cls = new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");
        var set = SetupDiGetClassDevsW(ref cls, IntPtr.Zero, IntPtr.Zero, 0x2 | 0x10);
        if (set == new IntPtr(-1)) return null;
        try
        {
            var res = new List<string>();
            for (uint i = 0; i < 16; i++)
            {
                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref cls, i, ref did)) break;
                SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                if (need <= 0) continue;
                var buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, need, out _, IntPtr.Zero) && Marshal.PtrToStringUni(buf + 4) is { } p) res.Add(p);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return res;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private static int? Positive(int? v) => v is > 0 ? v : null;

    private static (int? Cycles, string? Chemistry) CyclesFromDriver()
    {
        var cls = new Guid("72631e54-78a4-11d0-bcf7-00aa00b7b32a");   // GUID_DEVCLASS_BATTERY
        var set = SetupDiGetClassDevsW(ref cls, IntPtr.Zero, IntPtr.Zero, 0x2 | 0x10 /* DIGCF_PRESENT | DIGCF_DEVICEINTERFACE */);
        if (set == new IntPtr(-1)) return (null, null);
        try
        {
            int total = 0; bool any = false; string? chem = null;
            for (uint i = 0; i < 4; i++)
            {
                var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref cls, i, ref did)) break;
                SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, out int need, IntPtr.Zero);
                var buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);   // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, need, out _, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringUni(buf + 4)!;
                    if (BatteryDeviceClass.Classify(path) == BatteryDeviceKind.Ups) continue;   // un SAI/UPS no es la batería del equipo: sus ciclos y química no se mezclan
                    using var h = CreateFileW(path, 0xC0000000 /* GENERIC_READ|WRITE */, 3, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0x80, IntPtr.Zero);
                    if (h.IsInvalid) continue;
                    uint wait = 0, tag;
                    if (!DeviceIoControl(h, 0x294040 /* IOCTL_BATTERY_QUERY_TAG */, ref wait, 4, out tag, 4, out _, IntPtr.Zero) || tag == 0) continue;
                    var q = new BATTERY_QUERY_INFORMATION { BatteryTag = tag, InformationLevel = 0 /* BatteryInformation */ };
                    if (!DeviceIoControl(h, 0x294044 /* IOCTL_BATTERY_QUERY_INFORMATION */, ref q, Marshal.SizeOf<BATTERY_QUERY_INFORMATION>(), out BATTERY_INFORMATION bi, Marshal.SizeOf<BATTERY_INFORMATION>(), out _, IntPtr.Zero)) continue;
                    if (bi.CycleCount > 0) { total += (int)bi.CycleCount; any = true; }
                    chem ??= System.Text.Encoding.ASCII.GetString(BitConverter.GetBytes(bi.Chemistry)).Trim('\0', ' ') is { Length: > 0 } c ? c : null;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }

            return (any ? total : null, chem);
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct BATTERY_QUERY_INFORMATION { public uint BatteryTag; public int InformationLevel; public int AtRate; }
    [StructLayout(LayoutKind.Sequential)] private struct BATTERY_INFORMATION { public uint Capabilities; public byte Technology; public byte R1, R2, R3; public uint Chemistry; public uint DesignedCapacity, FullChargedCapacity, DefaultAlert1, DefaultAlert2, CriticalBias, CycleCount; }
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid cls, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid cls, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref uint inBuf, int inSize, out uint outBuf, int outSize, out int returned, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref BATTERY_QUERY_INFORMATION inBuf, int inSize, out BATTERY_INFORMATION outBuf, int outSize, out int returned, IntPtr ov);
}
