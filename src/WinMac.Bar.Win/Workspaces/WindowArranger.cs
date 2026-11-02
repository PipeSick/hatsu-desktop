using System.Runtime.InteropServices;
using System.Text;
using WinMac.Bar.Capture;
using WinMac.Bar.Workspaces;

namespace WinMac.Bar.Win.Workspaces;

/// <summary>
/// Reads and moves top-level windows with public Win32 APIs only (EnumWindows, Get/SetWindowPlacement). No hooks, no injection: a window that refuses
/// (an app running with more privileges than WinMac — UIPI — or one that is not responding) is reported and left alone. Windows itself applies each
/// app's minimum/maximum size. The bar is Per-Monitor V2, so every coordinate is in physical pixels, whatever the DPI of each monitor.
/// </summary>
internal static class WindowArranger
{
    private static readonly string[] OwnProcesses = ["WinMac", "WinMac.Bar.App", "WinMac.Dock.App", "NotchWin.App"];
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"];

    public static IReadOnlyList<WsMonitor> Monitors()
    {
        var list = new List<WsMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(h, ref mi)) list.Add(new WsMonitor(R(mi.rcWork), (mi.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return list.OrderByDescending(m => m.Primary).ThenBy(m => m.Work.X).ThenBy(m => m.Work.Y).ToList();   // stable order: primary first, then left to right
    }

    /// <summary>Normal application windows, front to back (WinMac's own windows, the desktop and the taskbar excluded).</summary>
    public static IReadOnlyList<LiveWindow> Windows()
    {
        var list = new List<LiveWindow>();
        EnumWindows((h, _) =>
        {
            try
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
                long ex = GetWindowLongPtr(h, -20).ToInt64(); if ((ex & 0x80) != 0) return true;   // tool windows
                if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
                var cls = new StringBuilder(128); GetClassName(h, cls, 128); if (ShellClasses.Contains(cls.ToString())) return true;
                int len = GetWindowTextLength(h); if (len == 0) return true;
                var title = new StringBuilder(Math.Min(len + 1, 512)); GetWindowText(h, title, title.Capacity);
                var proc = ProcessName(h); if (proc is null || OwnProcesses.Contains(proc, StringComparer.OrdinalIgnoreCase)) return true;
                var (rect, show) = Placement(h);
                if (rect.W < 60 || rect.H < 40) return true;
                list.Add(new LiveWindow(h, proc, cls.ToString(), title.ToString(), rect, show));
            }
            catch (Exception) { }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The window's NORMAL rectangle in screen px (also when maximized or minimized) and its state.</summary>
    private static (CaptureRect Rect, WinShow Show) Placement(IntPtr h)
    {
        var wp = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(h, ref wp)) { GetWindowRect(h, out var wr); return (R(wr), WinShow.Normal); }
        var show = wp.showCmd switch { 2 or 6 or 7 => WinShow.Minimized, 3 => WinShow.Maximized, _ => WinShow.Normal };
        if (IsIconic(h)) show = WinShow.Minimized;
        var r = R(wp.rcNormalPosition); var (dx, dy) = WorkspaceOffset(r);   // rcNormalPosition is in workspace coordinates
        return (new CaptureRect(r.X + dx, r.Y + dy, r.W, r.H), show);
    }

    /// <summary>Offset between work area and monitor of the monitor a rectangle is on (taskbar at the top or left).</summary>
    private static (int Dx, int Dy) WorkspaceOffset(CaptureRect r)
    {
        var rc = new RECT { L = r.X, T = r.Y, R = r.Right, B = r.Bottom };
        var mon = MonitorFromRect(ref rc, 2 /* NEAREST */); var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(mon, ref mi) ? (mi.rcWork.L - mi.rcMonitor.L, mi.rcWork.T - mi.rcMonitor.T) : (0, 0);
    }

    /// <summary>Applies one move. Returns null on success or the reason it was not possible.</summary>
    public static string? Apply(WsMove m)
    {
        if (!IsWindow(m.Handle)) return "la ventana ya no existe";
        if (IsHungAppWindow(m.Handle)) return "la aplicación no responde";
        var (dx, dy) = WorkspaceOffset(m.Rect);
        var wp = new WINDOWPLACEMENT
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            showCmd = m.Show switch { WinShow.Maximized => 3u, WinShow.Minimized => 7u, _ => 4u },   // SHOWMAXIMIZED / SHOWMINNOACTIVE / SHOWNOACTIVATE
            rcNormalPosition = new RECT { L = m.Rect.X - dx, T = m.Rect.Y - dy, R = m.Rect.Right - dx, B = m.Rect.Bottom - dy },
            ptMinPosition = new POINT { X = -1, Y = -1 }, ptMaxPosition = new POINT { X = -1, Y = -1 },
        };
        if (m.Show == WinShow.Maximized && IsZoomed(m.Handle)) ShowWindow(m.Handle, 9 /* RESTORE */);   // so it maximizes on the right monitor
        if (SetWindowPlacement(m.Handle, ref wp)) return null;
        int err = Marshal.GetLastWin32Error();
        return err == 5 ? "la aplicación tiene más permisos que WinMac (no se puede mover)" : "Windows no lo permitió (" + err + ")";
    }

    private static string? ProcessName(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid); if (pid == 0) return null;
        var hp = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid); if (hp == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(hp, 0, sb, ref n) ? Path.GetFileNameWithoutExtension(sb.ToString()) : null; }
        finally { CloseHandle(hp); }
    }

    public static string? ProcessPath(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid); var hp = OpenProcess(0x1000, false, pid); if (hp == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(hp, 0, sb, ref n) ? sb.ToString() : null; }
        finally { CloseHandle(hp); }
    }

    private static CaptureRect R(RECT r) => new(r.L, r.T, r.R - r.L, r.B - r.T);

    private delegate bool MonitorProc(IntPtr h, IntPtr dc, IntPtr r, IntPtr l);
    private delegate bool WindowProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT { public uint length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowProc f, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder s, ref int n);
}
