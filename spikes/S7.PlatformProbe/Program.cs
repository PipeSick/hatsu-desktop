// S7 — Sonda de plataforma: resuelve incertidumbres de la Fase 0 sobre el equipo real.
// Reporta (todo OBSERVADO en la máquina donde corre): build exacta, monitores/DPI/Hz, escritorios virtuales
// (API documentada), estado de pantalla completa (SHQueryUserNotificationState) y soporte DWM (esquinas/backdrop).
using System.Runtime.InteropServices;
using Microsoft.Win32;

var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")!;
Console.WriteLine($"Windows: {k.GetValue("ProductName")} {k.GetValue("DisplayVersion")} build {k.GetValue("CurrentBuildNumber")}.{k.GetValue("UBR")}");

Console.WriteLine("\n== Monitores ==");
int i = 0;
P.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hm, IntPtr dc, ref P.RECT r, IntPtr data) =>
{
    P.GetDpiForMonitor(hm, 0, out uint dx, out _);
    Console.WriteLine($"  #{i++} {r.r - r.l}x{r.b - r.t} @({r.l},{r.t}) DPI={dx} ({dx * 100 / 96}%)");
    return true;
}, IntPtr.Zero);

Console.WriteLine("\n== Escritorios virtuales (API documentada IVirtualDesktopManager) ==");
try
{
    var t = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"))!;
    var mgr = (IVirtualDesktopManager)Activator.CreateInstance(t)!;
    IntPtr fg = P.GetForegroundWindow();
    mgr.IsWindowOnCurrentVirtualDesktop(fg, out bool onCur);
    int hr = mgr.GetWindowDesktopId(fg, out Guid id);
    Console.WriteLine($"  OK. Ventana en primer plano: enEscritorioActual={onCur}, desktopId={(hr == 0 ? id.ToString() : "hr=0x" + hr.ToString("X"))}");
}
catch (Exception e) { Console.WriteLine("  FALLÓ: " + e.Message); }
Console.WriteLine("  (Crear/cambiar/mover ventanas ajenas requiere interfaces NO documentadas; se evalúan aparte, ver docs/01-fase1-resultados.md)");

Console.WriteLine("\n== Pantalla completa / no molestar (SHQueryUserNotificationState) ==");
P.SHQueryUserNotificationState(out int qs);
Console.WriteLine($"  estado={qs} (1=ausente 2=pantalla completa exclusiva 3=D3D fullscreen 4=no molestar 5=aceptando 6=presentación 7=app UWP en fullscreen)");

Console.WriteLine("\n== DWM: soporte de esquinas y backdrop (ventana temporal) ==");
IntPtr h = P.CreateWindowExW(0, "STATIC", "probe", 0x80000000, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
int round = 2, backdrop = 3, dark = 1;
Console.WriteLine($"  DWMWA_WINDOW_CORNER_PREFERENCE(33): hr=0x{P.DwmSetWindowAttribute(h, 33, ref round, 4):X}");
Console.WriteLine($"  DWMWA_SYSTEMBACKDROP_TYPE(38)=acrylic: hr=0x{P.DwmSetWindowAttribute(h, 38, ref backdrop, 4):X}");
Console.WriteLine($"  DWMWA_USE_IMMERSIVE_DARK_MODE(20): hr=0x{P.DwmSetWindowAttribute(h, 20, ref dark, 4):X}");
P.DestroyWindow(h);

[ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrent);
    [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
    [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}

static class P
{
#pragma warning disable CS0649
    public struct RECT { public int l, t, r, b; }
#pragma warning restore CS0649
    public delegate bool MonitorEnumProc(IntPtr hm, IntPtr dc, ref RECT r, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hm, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);
}
