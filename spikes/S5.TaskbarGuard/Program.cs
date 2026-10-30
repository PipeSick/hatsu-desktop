// S5 — Reversibilidad de la barra de tareas (requisito de seguridad #9/#18 de la Fase 0).
// Comandos:
//   status           muestra el estado actual (autohide / siempre visible)
//   guarded-hide     guarda el estado original, activa autohide, y lo restaura al salir (Ctrl+C, cierre normal)
//                    y TAMBIÉN si el proceso muere a la fuerza (lo hace un proceso centinela independiente)
//   restore          restaura el estado guardado manualmente (red de seguridad)
// Criterio de aceptación: tras `taskkill /F /IM S5.TaskbarGuard.exe` la barra vuelve a su estado en < 3 s.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

string stateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "spikes", "taskbar-state.json");

switch (args.FirstOrDefault())
{
    case "status": Console.WriteLine(Describe(Taskbar.GetState())); return 0;
    case "restore": return Restore();
    case "--sentinel":
        // Centinela: espera a que muera el padre (por cualquier causa) y restaura.
        try { Process.GetProcessById(int.Parse(args[1])).WaitForExit(); } catch (ArgumentException) { }
        return Restore();
    case "guarded-hide":
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        uint original = Taskbar.GetState();
        if (!File.Exists(stateFile)) File.WriteAllText(stateFile, JsonSerializer.Serialize(new { original }));
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--sentinel {Environment.ProcessId}") { CreateNoWindow = true, UseShellExecute = false });
        Taskbar.SetState(original | Taskbar.ABS_AUTOHIDE);
        Console.WriteLine($"Estado original: {Describe(original)} -> ahora: {Describe(Taskbar.GetState())}");
        var done = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        Console.WriteLine("Ctrl+C para salir y restaurar. Prueba además: taskkill /F /IM S5.TaskbarGuard.exe");
        done.Wait();
        return Restore();
    default: Console.WriteLine("Uso: status | guarded-hide | restore"); return 1;
}

int Restore()
{
    if (!File.Exists(stateFile)) { Console.WriteLine("Nada que restaurar."); return 0; }
    uint original = JsonDocument.Parse(File.ReadAllText(stateFile)).RootElement.GetProperty("original").GetUInt32();
    Taskbar.SetState(original);
    File.Delete(stateFile);
    Console.WriteLine("Restaurado: " + Describe(Taskbar.GetState()));
    return 0;
}
static string Describe(uint s) => ((s & Taskbar.ABS_AUTOHIDE) != 0 ? "autohide" : "siempre visible") + $" (0x{s:X})";

static class Taskbar
{
    public const uint ABS_AUTOHIDE = 1;
    const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public int l, t, r, b; public IntPtr lParam; }
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string? title);

    static APPBARDATA New() => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindowW("Shell_TrayWnd", null) };
    public static uint GetState() { var d = New(); return (uint)SHAppBarMessage(ABM_GETSTATE, ref d); }
    public static void SetState(uint s) { var d = New(); d.lParam = (IntPtr)s; SHAppBarMessage(ABM_SETSTATE, ref d); }
}
