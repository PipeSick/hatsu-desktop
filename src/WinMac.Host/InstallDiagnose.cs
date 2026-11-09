using System.Diagnostics;
using WinMac.Host.App;

namespace WinMac.Host.App;

/// <summary>
/// <c>HatsuDesktop.exe --diagnose [--out archivo] [--open]</c> y el botón «Generar informe de diagnóstico» de Ajustes. SOLO LECTURA y LOCAL: reúne hechos (versión, ruta, componentes, procesos, firma, estado del
/// asistente, inicio automático, barra de tareas, registro del host) y los compone con <see cref="InstallDiagnostics"/>. No cambia nada, no abre conexiones, no envía nada.
/// </summary>
internal static class InstallDiagnose
{
    public static string DefaultFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "diagnostico.txt");

    public static string Collect(HostOptions o)
    {
        string dir = AppContext.BaseDirectory.TrimEnd('\\'), exe = Environment.ProcessPath ?? "HatsuDesktop.exe";
        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        var cfg = HostConfig.Load(o.ConfigPath); string appData = Path.GetDirectoryName(Path.GetFullPath(o.ConfigPath)) ?? "";
        var legacy = new List<string>(); foreach (var f in new[] { "bar.json", "dock.json" }) if (File.Exists(Path.Combine(appData, f))) legacy.Add(f);
        if (File.Exists(o.NotchWinSettingsPath)) legacy.Add(@"NotchWin\settings.json");
        if (Directory.Exists(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\WinMac")) legacy.Add(@"carpeta de datos %APPDATA%\WinMac");
        string auto; try { var a = Autostart.Assess(exe); auto = a.State switch { WinMac.Host.AutostartState.NotConfigured => "no está activado", WinMac.Host.AutostartState.Current => "activado: apunta a esta instalación", WinMac.Host.AutostartState.OtherInstall => "activado: apunta a OTRA copia (" + a.StoredExe + ")", WinMac.Host.AutostartState.Orphan => "activado: apunta a una carpeta que ya no existe", _ => "activado: valor ilegible" }; } catch (Exception e) { auto = "no legible (" + e.GetType().Name + ")"; }
        var sigs = new Dictionary<string, string>(); foreach (var f in InstallDiagnostics.ModuleExes) { var p = Path.Combine(dir, f); sigs[f] = File.Exists(p) ? (SecurityDiagnostics.Signed(p) ? "firma válida" : "sin firma válida") : "no está"; }
        var procs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses()) using (p) { try { if (p.ProcessName.StartsWith("HatsuDesktop", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("NotchWin.App", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("WinMac", StringComparison.OrdinalIgnoreCase)) procs.Add(p.ProcessName); } catch (Exception) { } }
        List<string> tail = []; try { if (File.Exists(o.LogPath)) tail = File.ReadLines(o.LogPath).TakeLast(600).ToList(); } catch (Exception) { }
        string sec; try { sec = SecurityDiagnostics.BuildReport(out _); } catch (Exception e) { sec = "no disponible: " + e.GetType().Name; }
        List<string> barTail = []; try { var bl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "bar.log"); if (File.Exists(bl)) barTail = File.ReadLines(bl).TakeLast(400).ToList(); } catch (Exception) { }
        var input = new DiagnosticsInput(version, dir, Environment.OSVersion.VersionString, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), cfg, File.Exists(o.ConfigPath), legacy, auto, File.Exists(TaskbarLease.DefaultPath), sigs, procs.ToList(), tail, sec,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, ReadMonitors(), barTail);
        return InstallDiagnostics.Build(input);
    }

    // ── monitores y área de trabajo (solo lectura: EnumDisplayMonitors + GetMonitorInfo)
    private delegate bool MonitorEnumProc(IntPtr h, IntPtr hdc, IntPtr rc, IntPtr data);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct Rc { public int L, T, R, B; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private struct Mi { public uint Size; public Rc Monitor, Work; public uint Flags; [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr h, ref Mi mi);
    [System.Runtime.InteropServices.DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr h, int type, out uint x, out uint y);

    private static List<MonitorArea> ReadMonitors()
    {
        var list = new List<MonitorArea>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
            {
                var mi = new Mi { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Mi>() }; if (!GetMonitorInfoW(h, ref mi)) return true;
                double scale = 1; try { if (GetDpiForMonitor(h, 0, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0; } catch (Exception) { }
                list.Add(new MonitorArea(mi.Device.Replace(@"\\.\", ""), (mi.Flags & 1) != 0, mi.Monitor.L, mi.Monitor.T, mi.Monitor.R, mi.Monitor.B, mi.Work.L, mi.Work.T, mi.Work.R, mi.Work.B, scale)); return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }
        return list;
    }

    /// <summary>Genera el informe, lo guarda y (si se pide) lo abre en el Bloc de notas para que el usuario lo revise. Devuelve la ruta o null.</summary>
    public static string? Run(HostOptions o, string? outFile, bool open, bool print)
    {
        string report = Collect(o); string file = outFile ?? DefaultFile;
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!); File.WriteAllText(file, report, System.Text.Encoding.UTF8); } catch (Exception) { file = ""; }
        if (print) { try { Console.WriteLine(report); } catch (Exception) { } if (file.Length > 0) { try { Console.WriteLine("Informe guardado en: " + file); } catch (Exception) { } } }
        if (open && file.Length > 0) { try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = false }); } catch (Exception) { } }
        return file.Length > 0 ? file : null;
    }
}
