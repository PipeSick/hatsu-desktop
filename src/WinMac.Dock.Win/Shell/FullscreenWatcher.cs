using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>Detecta juegos/vídeo a pantalla completa con SHQueryUserNotificationState (documentada) para que el dock se aparte. Consulta barata; llamar ~1 Hz.</summary>
public static class FullscreenWatcher
{
    // QUNS_BUSY=2 (pantalla completa/ocupado), QUNS_RUNNING_D3D_FULL_SCREEN=3, QUNS_PRESENTATION_MODE=4, QUNS_APP=7 (UWP a pantalla completa)
    /// <param name="thisMonitor">Rectángulo del monitor de quien pregunta (px). null = cualquier monitor. Con varios monitores, un juego a pantalla completa en uno no esconde el Dock ni la Barra de los otros.</param>
    public static bool ShouldSuppress((int Left, int Top, int Right, int Bottom)? thisMonitor = null)
    {
        try
        {
            if (Native.SHQueryUserNotificationState(out int s) != 0) return false;
            if (s == FullscreenRules.Presentation) return true;
            if (s is not (FullscreenRules.Busy or FullscreenRules.D3DFullScreen or FullscreenRules.App)) return false;
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            bool minimized = Native.IsIconic(fg), shell = IsShellWindow(fg), covers = false, onThis = thisMonitor is null;
            if (!minimized && !shell && Native.GetWindowRect(fg, out var r))
            {
                var mon = Native.MonitorFromWindow(fg, 2 /* NEAREST */);
                var mi = new Native.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
                if (mon != IntPtr.Zero && Native.GetMonitorInfo(mon, ref mi))
                {
                    var m = mi.rcMonitor; covers = r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
                    if (thisMonitor is { } t) onThis = m.Left == t.Left && m.Top == t.Top && m.Right == t.Right && m.Bottom == t.Bottom;   // por rectángulo: Windows puede renovar el HMONITOR
                }
            }

            return FullscreenRules.ShouldSuppress(s, true, minimized, shell, covers, onThis);
        }
        catch (Exception) { return false; }
    }

    private static bool IsShellWindow(IntPtr h)
    {
        var sb = new System.Text.StringBuilder(64); Native.GetClassNameW(h, sb, sb.Capacity);
        return sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
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
