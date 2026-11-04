using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Bar.Win.Shell.Tray;

/// <summary>It7: ¿hay ahora algún menú contextual de Windows (clase «#32768») visible de OTRO proceso? Solo lectura; no toca ni cierra nada.</summary>
internal static class TrayMenuProbe
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    public static bool AnyMenuVisible()
    {
        bool found = false; uint self = (uint)Environment.ProcessId; var sb = new StringBuilder(32);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            sb.Clear(); if (GetClassNameW(h, sb, 32) == 0 || sb.ToString() != "#32768") return true;
            GetWindowThreadProcessId(h, out uint pid); if (pid == self) return true;
            found = true; return false;
        }, IntPtr.Zero);
        return found;
    }
}
