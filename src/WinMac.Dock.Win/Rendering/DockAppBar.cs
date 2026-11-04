using System.Runtime.InteropServices;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// WM-001 (docs/36): the Dock reserves its strip at the bottom of the work area as a Windows AppBar (documented <c>SHAppBarMessage</c>), so maximized windows end
/// ABOVE it and their bottom controls stay usable. Windows positions it after the other AppBars (the taskbar, if visible). It is removed when the Dock closes,
/// when the user turns it off or chooses auto-hide, and registered again if Explorer restarts. If WinMac dies, Windows drops an AppBar whose window no longer
/// exists; the host restarts the Dock, which registers it again. Nothing in Windows' settings is changed.
/// </summary>
internal sealed class DockAppBar(IntPtr hwnd, uint callbackMessage, Action<string> log)
{
    private bool _registered;
    private RECT _last;

    public bool Registered => _registered;

    /// <summary>Reserves a strip of <paramref name="heightPx"/> at the bottom of <paramref name="monitor"/>; returns the bottom edge the Dock must sit on.</summary>
    public int Reserve(Interop.Native.RECT monitor, int heightPx, bool flushWithEdge = false)
    {
        var abd = New();
        if (!_registered)
        {
            abd.uCallbackMessage = callbackMessage;
            _registered = SHAppBarMessage(ABM_NEW, ref abd) != 0;
            if (!_registered) { log("[dock] ABM_NEW falló: el Dock no puede reservar su espacio (se superpone)"); return monitor.Bottom; }
        }

        abd = New(); abd.uEdge = ABE_BOTTOM;
        abd.rc = new RECT { L = monitor.Left, T = monitor.Bottom - heightPx, R = monitor.Right, B = monitor.Bottom };
        // flushWithEdge (WM-043): sin barra de tareas visible NO se consulta la posición: ABM_QUERYPOS empujaría el Dock por encima de la franja que Explorer pueda seguir reservando
        // aunque la barra esté oculta, y el Dock quedaría separado del borde. Con barra visible, Windows lo sube sobre ella como siempre.
        if (!flushWithEdge) SHAppBarMessage(ABM_QUERYPOS, ref abd);   // Windows moves it above other AppBars (e.g. a visible taskbar)
        abd.rc.T = abd.rc.B - heightPx;
        SHAppBarMessage(ABM_SETPOS, ref abd);
        if (abd.rc.L != _last.L || abd.rc.T != _last.T || abd.rc.R != _last.R || abd.rc.B != _last.B)
            log($"[dock] espacio reservado (AppBar): ({abd.rc.L},{abd.rc.T})-({abd.rc.R},{abd.rc.B}) px");
        _last = abd.rc;
        return abd.rc.B;
    }

    public void Remove()
    {
        if (!_registered) return;
        var abd = New(); SHAppBarMessage(ABM_REMOVE, ref abd); _registered = false; _last = default;
        log("[dock] espacio reservado liberado");
    }

    /// <summary>Explorer restarted (TaskbarCreated): its AppBar list is new.</summary>
    public void Forget() { _registered = false; _last = default; }

    private APPBARDATA New() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };

    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABE_BOTTOM = 3;
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public RECT rc; public IntPtr lParam; }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
}
