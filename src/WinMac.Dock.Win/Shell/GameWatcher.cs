using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>Modo juego: ¿la ventana en primer plano es un juego de ESTE monitor? Consulta barata; llamar ~1 Hz.</summary>
public static class GameWatcher
{
    /// <returns>true = hay un juego en primer plano aquí; false = no lo hay (otra app, minimizada, otro monitor); null = la ventana en primer plano es el escritorio, la barra de tareas o este mismo proceso: no cambia nada.</returns>
    public static bool? ForegroundIsGame((int Left, int Top, int Right, int Bottom) thisMonitor, DockConfig cfg)
    {
        try
        {
            var fg = Native.GetForegroundWindow(); if (fg == IntPtr.Zero) return null;
            var sb = new System.Text.StringBuilder(64); Native.GetClassNameW(fg, sb, sb.Capacity);
            if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
            Native.GetWindowThreadProcessId(fg, out uint pid); if (pid == (uint)Environment.ProcessId) return null;
            if (Native.IsIconic(fg)) return false;
            var mon = Native.MonitorFromWindow(fg, 2 /* NEAREST */);
            var mi = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (mon == IntPtr.Zero || !Native.GetMonitorInfo(mon, ref mi)) return false;
            var m = mi.rcMonitor; if (m.Left != thisMonitor.Left || m.Top != thisMonitor.Top || m.Right != thisMonitor.Right || m.Bottom != thisMonitor.Bottom) return false;
            return GameModeRules.IsGame(Native.ProcessPath(pid), cfg.GameApps);
        }
        catch (Exception) { return null; }
    }
}
