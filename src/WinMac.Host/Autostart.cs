using Microsoft.Win32;

namespace WinMac.Host.App;

/// <summary>Inicio automático con UNA sola clave por usuario (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>), totalmente reversible. No toca nada más.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultName = "WinMac";

    public static void Install(string exePath, string name = DefaultName)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        k.SetValue(name, $"\"{exePath}\" --background", RegistryValueKind.String);
    }

    public static bool Remove(string name = DefaultName)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k?.GetValue(name) is null) return false;
        k.DeleteValue(name, throwOnMissingValue: false); return true;
    }

    /// <summary>Hatsu Desktop: repara un inicio automático huérfano (apunta a una carpeta que ya no existe) sin tocar el de otra instalación viva. Ver <see cref="WinMac.Host.AutostartMigration"/>.</summary>
    public static string? Heal(string currentExe, string name = DefaultName)
    {
        try
        {
            var plan = WinMac.Host.AutostartMigration.Plan(Current(name), currentExe, File.Exists);
            if (plan is null) return null;
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true); k.SetValue(name, plan, RegistryValueKind.String); return plan;
        }
        catch (Exception) { return null; }
    }

    public static string? Current(string name = DefaultName)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(name) as string;
    }
}
