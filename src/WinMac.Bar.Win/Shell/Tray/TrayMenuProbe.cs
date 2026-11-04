using System.Runtime.InteropServices;
using System.Text;
using WinMac.Bar.Tray;

namespace WinMac.Bar.Win.Shell.Tray;

/// <summary>
/// Observa (solo lectura) si la app a la que se reenvió un clic derecho está mostrando un menú: ventana «#32768» de otro proceso, modo menú del hilo (GetGUIThreadInfo) o una
/// ventana emergente nueva de esa app. No toca, enfoca ni cierra nada, y no necesita permisos elevados. Lo que Windows no deja observar queda como «sin detectar» (ver <see cref="TrayMenuWatch"/>).
/// </summary>
internal sealed class TrayMenuProbe
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct GUITHREADINFO { public int cbSize, flags; public IntPtr active, focus, capture, menuOwner, moveSize, caret; public RECT rc; }
    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20, WS_CAPTION = 0xC00000, WS_POPUP = unchecked((int)0x80000000), WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, GUI_INMENUMODE = 0x4, GUI_POPUPMENUMODE = 0x10;

    private readonly uint _pid, _self = (uint)Environment.ProcessId, _targetThread;
    private readonly HashSet<IntPtr> _before;

    public TrayMenuProbe(IntPtr targetHwnd)
    {
        _targetThread = GetWindowThreadProcessId(targetHwnd, out _pid);
        _before = TopLevelOf(_pid);   // lo que la app ya tenía abierto: no cuenta como «menú nuevo»
    }

    private static HashSet<IntPtr> TopLevelOf(uint pid)
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((h, _) => { GetWindowThreadProcessId(h, out uint p); if (p == pid && IsWindowVisible(h)) set.Add(h); return true; }, IntPtr.Zero);
        return set;
    }

    public TrayMenuSignals Read()
    {
        var fg = GetForegroundWindow(); uint fgThread = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, out uint fgPid2);
        uint fgPid = 0; if (fg != IntPtr.Zero) GetWindowThreadProcessId(fg, out fgPid);
        var owner = fgPid == _self ? ForegroundOwner.Panel : fgPid == _pid ? ForegroundOwner.Target : ForegroundOwner.Other;

        bool menuWindow = false, newPopup = false; var sb = new StringBuilder(64);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint p); if (p == _self) return true;
            sb.Clear(); GetClassNameW(h, sb, 64);
            if (sb.ToString() == "#32768") { menuWindow = true; return true; }
            if (p == _pid && !_before.Contains(h) && owner != ForegroundOwner.Other && LooksLikePopup(h)) newPopup = true;
            return true;
        }, IntPtr.Zero);

        bool menuMode = InMenuMode(_targetThread) || (fgThread != 0 && fgThread != _targetThread && owner == ForegroundOwner.Target && InMenuMode(fgThread));
        return new TrayMenuSignals(menuWindow, menuMode, newPopup, owner);
    }

    private static bool InMenuMode(uint thread)
    {
        if (thread == 0) return false;
        var gi = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(thread, ref gi) && (gi.flags & (GUI_INMENUMODE | GUI_POPUPMENUMODE)) != 0;
    }

    /// <summary>Ventana emergente: sin barra de título y (de herramientas o tipo popup), de tamaño de menú. Una ventana normal de la app no cuenta (no se mantiene el panel por ella).</summary>
    private static bool LooksLikePopup(IntPtr h)
    {
        int style = GetWindowLong(h, GWL_STYLE), ex = GetWindowLong(h, GWL_EXSTYLE);
        if ((style & WS_CAPTION) == WS_CAPTION || (ex & WS_EX_APPWINDOW) != 0) return false;
        if ((style & WS_POPUP) == 0 && (ex & WS_EX_TOOLWINDOW) == 0) return false;
        return GetWindowRect(h, out var r) && r.R - r.L is > 20 and < 900 && r.B - r.T is > 10 and < 1200;
    }
}
