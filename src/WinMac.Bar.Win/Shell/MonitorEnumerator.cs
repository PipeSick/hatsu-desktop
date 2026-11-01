using System.Runtime.InteropServices;
using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Shell;

/// <summary>Un monitor tal como lo reporta Windows ahora: el modelo puro (<see cref="Info"/>) más lo que solo existe en Windows (el HMONITOR y su rectángulo).</summary>
internal sealed record MonitorSnapshot(MonitorInfo Info, IntPtr Handle, BarNative.RECT Rect);

/// <summary>
/// Lee los monitores activos (EnumDisplayMonitors). El identificador sale de la RUTA DEL DISPOSITIVO del monitor (EDID/PnP), no del número de pantalla, así sobrevive a que Windows renumere
/// las salidas o a que se cambie el modo (duplicar/extender). Cualquier fallo de una lectura de detalle (DPI, ruta) degrada a un valor razonable en vez de perder el monitor.
/// </summary>
internal static class MonitorEnumerator
{
    /// <summary>Todos los monitores que Windows reporta (en modo duplicar Windows suele presentar uno solo; si presentara varios con los mismos límites, <see cref="MonitorPlan.Unique"/> los reduce).</summary>
    public static IReadOnlyList<MonitorSnapshot> Enumerate()
    {
        var raw = new List<(IntPtr H, BarNative.RECT Rect, BarNative.RECT Work, string Device, bool Primary)>();
        BarNative.MonitorEnumProc proc = (IntPtr h, IntPtr dc, ref BarNative.RECT r, IntPtr data) =>
        {
            var mi = new BarNative.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<BarNative.MONITORINFOEX>(), szDevice = "" };
            if (BarNative.GetMonitorInfoEx(h, ref mi)) raw.Add((h, mi.rcMonitor, mi.rcWork, mi.szDevice ?? "", (mi.dwFlags & 1) != 0));
            return true;
        };
        try { BarNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero); } catch (Exception) { /* sin monitores legibles: se usa el principal más abajo */ }
        GC.KeepAlive(proc);

        var ids = MonitorPlan.Disambiguate(raw.Select(m => MonitorPlan.MakeId(DevicePath(m.Device), m.Device)));
        var list = new List<MonitorSnapshot>();
        for (int i = 0; i < raw.Count; i++)
        {
            var m = raw[i];
            var info = new MonitorInfo(ids[i], new PxRect(m.Rect.Left, m.Rect.Top, m.Rect.Width, m.Rect.Height), new PxRect(m.Work.Left, m.Work.Top, m.Work.Width, m.Work.Height), ScaleOf(m.H), m.Primary);
            list.Add(new MonitorSnapshot(info, m.H, m.Rect));
        }

        return list;
    }

    /// <summary>Los monitores que llevan barra (sin espejos). Si Windows no entrega ninguno, el principal: la barra nunca se queda sin ventana.</summary>
    public static IReadOnlyList<MonitorSnapshot> ForBars()
    {
        var all = Enumerate();
        var wanted = MonitorPlan.Unique(all.Select(s => s.Info)).Select(i => all.First(s => s.Info.Id == i.Id)).ToList();
        return wanted.Count > 0 ? wanted : [Fallback()];
    }

    private static MonitorSnapshot Fallback()
    {
        var mi = new BarNative.MONITORINFO { cbSize = (uint)Marshal.SizeOf<BarNative.MONITORINFO>() };
        var h = BarNative.MonitorFromPoint(new BarNative.POINT(), BarNative.MONITOR_DEFAULTTOPRIMARY); BarNative.GetMonitorInfo(h, ref mi);
        var info = new MonitorInfo("primary", new PxRect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height), new PxRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Width, mi.rcWork.Height), ScaleOf(h), true);
        return new MonitorSnapshot(info, h, mi.rcMonitor);
    }

    /// <summary>Ruta del dispositivo del monitor conectado a esa salida (p. ej. MONITOR\GSM5B09\{guid}\0001), o null.</summary>
    private static string? DevicePath(string output)
    {
        try
        {
            if (string.IsNullOrEmpty(output)) return null;
            var dd = new BarNative.DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<BarNative.DISPLAY_DEVICE>(), DeviceName = "", DeviceString = "", DeviceID = "", DeviceKey = "" };
            return BarNative.EnumDisplayDevicesW(output, 0, ref dd, 0) && !string.IsNullOrWhiteSpace(dd.DeviceID) ? dd.DeviceID : null;
        }
        catch (Exception) { return null; }
    }

    private static double ScaleOf(IntPtr monitor)
    {
        try { return BarNative.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out _) == 0 && dx >= 48 ? dx / 96.0 : 1.0; }
        catch (Exception) { return 1.0; }
    }
}
