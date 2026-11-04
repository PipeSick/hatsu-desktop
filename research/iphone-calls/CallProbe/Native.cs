using System.Runtime.InteropServices;

namespace CallProbe;

internal static class Native
{
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern int GetCurrentPackageFullName(ref uint length, IntPtr buffer);
    [StructLayout(LayoutKind.Sequential)] private struct OSVERSIONINFOEX { public uint Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string CSD; }
    [DllImport("ntdll.dll")] private static extern int RtlGetVersion(ref OSVERSIONINFOEX v);

    /// <summary>15700 = APPMODEL_ERROR_NO_PACKAGE (sin identidad); 122 = hay paquete (el búfer nulo es pequeño). Se mide sin lanzar excepciones.</summary>
    public static bool HasPackageIdentity() { uint len = 0; return GetCurrentPackageFullName(ref len, IntPtr.Zero) != 15700; }

    public static string PackageFullName()
    {
        uint len = 0; int rc = GetCurrentPackageFullName(ref len, IntPtr.Zero); if (rc == 15700) return "(sin paquete)";
        var buf = Marshal.AllocHGlobal((int)len * 2); try { return GetCurrentPackageFullName(ref len, buf) == 0 ? Marshal.PtrToStringUni(buf)! : "(error)"; } finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>Tipo de firma del paquete (Developer = carga lateral/desarrollo, Store, System…). null si no hay paquete.</summary>
    public static string? SignatureKind() { try { return HasPackageIdentity() ? Windows.ApplicationModel.Package.Current.SignatureKind.ToString() : null; } catch (Exception) { return null; } }

    public static bool PhoneLinkRunning() => new[] { "PhoneExperienceHost", "YourPhone", "PhoneLink" }.Any(n => System.Diagnostics.Process.GetProcessesByName(n).Length > 0);

    /// <summary>Resumen corto e irreversible de un texto (para comparar estados sin guardar identificadores).</summary>
    public static string Hash(string? s) => string.IsNullOrEmpty(s) ? "(ninguno)" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s)))[..8];

    public static string WindowsBuild()
    {
        var v = new OSVERSIONINFOEX { Size = (uint)Marshal.SizeOf<OSVERSIONINFOEX>() }; RtlGetVersion(ref v);
        string ubr = ""; try { ubr = "." + Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", "?"); } catch (Exception) { }
        string disp = ""; try { disp = " " + Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", ""); } catch (Exception) { }
        return $"{v.Major}.{v.Minor}.{v.Build}{ubr}{disp}";
    }
}
