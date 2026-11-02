using System.Runtime.InteropServices;
using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// Window classes for windows that are created and destroyed many times (capture overlay, editor, recording indicator). A class is registered ONCE per
/// process, so its window procedure must not belong to the first instance: that instance is disposed and its delegate collected, and the next window of the
/// same class would call into it (the bar hung and was restarted on the second capture). One static procedure routes each message to its window's instance.
/// UI thread only.
/// </summary>
internal static class InstanceWindows
{
    private static readonly BarNative.WndProc s_proc = Route;   // rooted for the life of the process
    private static readonly Dictionary<IntPtr, BarNative.WndProc> s_map = [];
    private static readonly HashSet<string> s_registered = [];
    private static BarNative.WndProc? s_creating;

    public static IntPtr Create(string className, int cursor, BarNative.WndProc proc, uint exStyle, string title, uint style, int x, int y, int w, int h)
    {
        var inst = BarNative.GetModuleHandle(null);
        if (s_registered.Add(className))
        {
            var cls = new BarNative.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<BarNative.WNDCLASSEX>(), lpfnWndProc = s_proc, hInstance = inst, lpszClassName = className, hCursor = BarNative.LoadCursor(IntPtr.Zero, cursor) };
            BarNative.RegisterClassEx(ref cls);
        }

        s_creating = proc;
        try
        {
            var hwnd = BarNative.CreateWindowEx(exStyle, className, title, style, x, y, w, h, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hwnd != IntPtr.Zero) s_map[hwnd] = proc;
            return hwnd;
        }
        finally { s_creating = null; }
    }

    /// <summary>Call right after DestroyWindow.</summary>
    public static void Forget(IntPtr hwnd) => s_map.Remove(hwnd);

    private static IntPtr Route(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (s_map.TryGetValue(h, out var p)) return p(h, msg, w, l);
        if (s_creating is { } c) { s_map[h] = c; return c(h, msg, w, l); }   // messages sent during CreateWindowEx
        return BarNative.DefWindowProc(h, msg, w, l);
    }
}
