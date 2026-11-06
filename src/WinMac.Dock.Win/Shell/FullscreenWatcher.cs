using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>Detecta juegos/vídeo a pantalla completa con SHQueryUserNotificationState (documentada) para que el dock se aparte. Consulta barata; llamar ~1 Hz.</summary>
public static class FullscreenWatcher
{
    // QUNS_BUSY=2 (pantalla completa/ocupado), QUNS_RUNNING_D3D_FULL_SCREEN=3, QUNS_PRESENTATION_MODE=4, QUNS_APP=7 (UWP a pantalla completa)
    public static bool ShouldSuppress()
    {
        try
        {
            if (Native.SHQueryUserNotificationState(out int s) != 0) return false;
            if (s is 3 or 4) return true;                  // Direct3D a pantalla completa / modo presentación: inequívocos
            if (s is 2 or 7) return ForegroundCoversMonitor();   // «ocupado»/app: Windows lo marca también con apps de marco propio (p. ej. ChatGPT); solo cuenta si la ventana cubre el monitor entero
            return false;
        }
        catch (Exception) { return false; }
    }

    /// <summary>La ventana en primer plano cubre el monitor completo (no solo el área de trabajo): una ventana maximizada normal deja la barra/dock a la vista.</summary>
    public static bool ForegroundCoversMonitor()
    {
        try
        {
            var fg = Native.GetForegroundWindow(); if (fg == IntPtr.Zero) return false;
            if (!Native.GetWindowRect(fg, out var r)) return false;
            var mon = Native.MonitorFromWindow(fg, 2 /* NEAREST */); if (mon == IntPtr.Zero) return false;
            var mi = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;
            var m = mi.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }
        catch (Exception) { return false; }
    }
}
