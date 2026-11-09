using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinMac.Bar.Win.Shell.Hub;

namespace WinMac.Bar.Win.Shell;

/// <summary>
/// «Acerca de este PC»: lee los datos del equipo LOCALMENTE (registro, API de memoria de Windows y una consulta WMI de las tarjetas gráficas). Sin red, sin telemetría, sin datos de
/// ejemplo: lo que Windows no informa queda en null. Cada lectura es independiente: una que falla no impide las demás. Sin administrador.
/// </summary>
internal static class AboutPcReader
{
    public static AboutRaw Read()
    {
        string? bios(string name) => Reg(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", name) as string;
        string? cpu = Reg(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string;
        string? product = Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") as string;
        string? display = Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion") as string ?? Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId") as string;
        int? build = int.TryParse(Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber") as string, out var b) ? b : null;
        int? ubr = Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR") is int u ? u : null;
        var (installed, visible) = Memory();
        int? cores = null;
        try { int c = 0; foreach (var r in Wmi.Query(@"root\cimv2", "SELECT NumberOfCores FROM Win32_Processor")) c += Wmi.I32(r.GetValueOrDefault("NumberOfCores"), 0); cores = c > 0 ? c : null; } catch (Exception) { }
        return new AboutRaw(bios("SystemManufacturer"), bios("SystemProductName"), bios("SystemFamily"), Safe(() => Environment.MachineName), cpu, cores, Environment.ProcessorCount, installed, visible, Gpus(),
            product, display, build, ubr, Safe(() => RuntimeInformation.OSArchitecture.ToString()), bios("BIOSVersion"), WinMacVersion());
    }

    private static object? Reg(string key, string name) { try { return Registry.GetValue(key, name, null); } catch (Exception) { return null; } }
    private static T? Safe<T>(Func<T> f) { try { return f(); } catch (Exception) { return default; } }

    private static IReadOnlyList<GpuInfo> Gpus()
    {
        try { return Wmi.Query(@"root\cimv2", "SELECT Name FROM Win32_VideoController").Select(r => Wmi.Str(r.GetValueOrDefault("Name"))).Where(n => n.Length > 0).Select(n => new GpuInfo(n)).ToList(); }
        catch (Exception) { return []; }   // sin WMI: «No disponible», no una tarjeta inventada
    }

    private static (ulong? Installed, ulong? Visible) Memory()
    {
        ulong? installed = null, visible = null;
        try { if (GetPhysicallyInstalledSystemMemory(out ulong kb) && kb > 0) installed = kb * 1024; } catch (Exception) { }
        try { var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() }; if (GlobalMemoryStatusEx(ref m) && m.ullTotalPhys > 0) visible = m.ullTotalPhys; } catch (Exception) { }
        return (installed, visible);
    }

    /// <summary>Versión real de WinMac: la del ensamblado en ejecución (sin el sufijo de commit).</summary>
    public static string WinMacVersion()
    {
        try
        {
            var a = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AboutPcReader).Assembly;
            var v = (a.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion ?? a.GetName().Version?.ToString() ?? "";
            int plus = v.IndexOf('+'); if (plus > 0) v = v[..plus];
            return string.IsNullOrWhiteSpace(v) ? "" : v;
        }
        catch (Exception) { return ""; }
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalKb);
    [StructLayout(LayoutKind.Sequential)] private struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
