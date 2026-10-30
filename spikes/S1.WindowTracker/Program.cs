// S1 — ¿SetWinEventHook alcanza para seguir ventanas reales con costo despreciable?
// Uso: S1.WindowTracker [segundos=60]   (usa el equipo con normalidad mientras corre)
// Mide: ventanas "de app" detectadas, eventos/s por tipo, costo del handler (p50/p95 µs), CPU, y el costo de un
// escaneo completo con EnumWindows (lo que costaría hacer polling).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinMac.Spikes.Common;

int seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 60;
var counts = new Dictionary<uint, int>();
var costUs = new List<double>(65536);
var live = new Dictionary<IntPtr, string>();
var cpu = new CpuSampler();

Native2.WinEventProc proc = (hook, ev, hwnd, idObject, idChild, thread, time) =>
{
    long t0 = Stopwatch.GetTimestamp();
    if (idObject == 0 && idChild == 0 && hwnd != IntPtr.Zero)
    {
        counts[ev] = counts.GetValueOrDefault(ev) + 1;
        if (ev is Native2.EVENT_OBJECT_DESTROY or Native2.EVENT_OBJECT_HIDE or Native2.EVENT_OBJECT_CLOAKED) live.Remove(hwnd);
        else if (Native2.IsAppWindow(hwnd)) live[hwnd] = Native2.Describe(hwnd);
    }
    costUs.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000);
};

// Escaneo completo (referencia de polling)
var scanMs = new List<double>();
for (int i = 0; i < 20; i++)
{
    long t0 = Stopwatch.GetTimestamp(); int n = 0;
    Native2.EnumWindows((h, _) => { if (Native2.IsAppWindow(h)) n++; return true; }, IntPtr.Zero);
    scanMs.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
}
Native2.EnumWindows((h, _) => { if (Native2.IsAppWindow(h)) live[h] = Native2.Describe(h); return true; }, IntPtr.Zero);
Console.WriteLine($"Ventanas de app iniciales: {live.Count}");
foreach (var d in live.Values.Take(15)) Console.WriteLine("  " + d);

cpu.Start();
var hooks = new[]
{
    Native2.SetWinEventHook(Native2.EVENT_SYSTEM_FOREGROUND, Native2.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, Native2.WINEVENT_OUTOFCONTEXT),
    Native2.SetWinEventHook(Native2.EVENT_SYSTEM_MINIMIZESTART, Native2.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, proc, 0, 0, Native2.WINEVENT_OUTOFCONTEXT),
    Native2.SetWinEventHook(Native2.EVENT_OBJECT_CREATE, Native2.EVENT_OBJECT_HIDE, IntPtr.Zero, proc, 0, 0, Native2.WINEVENT_OUTOFCONTEXT),
    Native2.SetWinEventHook(Native2.EVENT_OBJECT_CLOAKED, Native2.EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, proc, 0, 0, Native2.WINEVENT_OUTOFCONTEXT),
};
if (hooks.Any(h => h == IntPtr.Zero)) { Console.Error.WriteLine("SetWinEventHook falló"); return 1; }

Console.WriteLine($"Escuchando {seconds}s… abre/cierra/minimiza ventanas, cambia de escritorio virtual.");
var end = Stopwatch.StartNew();
while (end.Elapsed.TotalSeconds < seconds)
{
    while (Native2.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1)) { Native2.TranslateMessage(ref msg); Native2.DispatchMessage(ref msg); }
    Thread.Sleep(5);
}
double cpuPct = cpu.StopPercent();
foreach (var h in hooks) Native2.UnhookWinEvent(h);

double P(List<double> l, double p) { var a = l.OrderBy(x => x).ToArray(); return a.Length == 0 ? 0 : a[Math.Min(a.Length - 1, (int)(p * a.Length))]; }
var report = new
{
    Spike = "S1", Os = Environment.OSVersion.VersionString, Seconds = seconds,
    EventsTotal = costUs.Count, EventsPerSec = costUs.Count / (double)seconds,
    ByEvent = counts.ToDictionary(k => $"0x{k.Key:X}", v => v.Value),
    HandlerP50Us = P(costUs, .5), HandlerP95Us = P(costUs, .95), HandlerMaxUs = costUs.Count == 0 ? 0 : costUs.Max(),
    ProcessCpuPct = cpuPct,
    FullScanP50Ms = P(scanMs, .5), FullScanP95Ms = P(scanMs, .95),
    FinalAppWindows = live.Count,
};
Directory.CreateDirectory("bench-results");
string path = Path.Combine("bench-results", $"S1-{DateTime.Now:yyyyMMdd-HHmmss}.json");
File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(File.ReadAllText(path)); Console.WriteLine("-> " + path);
return 0;

static class Native2
{
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG msg);

    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }

    const int GWL_EXSTYLE = -20; const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000; const uint GW_OWNER = 4; const int DWMWA_CLOAKED = 14;

    /// <summary>Heurística "ventana de app" (similar a la lista de Alt+Tab). Es una hipótesis a validar con este spike.</summary>
    public static bool IsAppWindow(IntPtr h)
    {
        if (!IsWindowVisible(h)) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        if ((ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_APPWINDOW) == 0) return false;
        if (GetWindow(h, GW_OWNER) != IntPtr.Zero && (ex & WS_EX_APPWINDOW) == 0) return false;
        var t = new StringBuilder(256); if (GetWindowTextW(h, t, 256) == 0) return false;
        var c = new StringBuilder(256); GetClassNameW(h, c, 256);
        return c.ToString() is not ("Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "WorkerW");
    }

    public static string Describe(IntPtr h)
    {
        var t = new StringBuilder(256); GetWindowTextW(h, t, 256);
        var c = new StringBuilder(256); GetClassNameW(h, c, 256);
        GetWindowThreadProcessId(h, out uint pid);
        string proc; try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { proc = "?"; }
        return $"{proc,-18} [{c}] {t}";
    }
}
