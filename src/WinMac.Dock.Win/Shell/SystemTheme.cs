using Microsoft.Win32;

namespace WinMac.Dock.Win.Shell;

public static class SystemTheme
{
    /// <summary>Tema de aplicaciones de Windows (HKCU\...\Themes\Personalize\AppsUseLightTheme: 0 = oscuro). Ante cualquier error, claro.</summary>
    public static DockTheme Current()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0 ? DockTheme.Dark : DockTheme.Light;
        }
        catch (Exception) { return DockTheme.Light; }
    }

    public static DockTheme Resolve(ThemeMode mode) => mode switch { ThemeMode.Dark => DockTheme.Dark, ThemeMode.Light => DockTheme.Light, _ => Current() };
}
