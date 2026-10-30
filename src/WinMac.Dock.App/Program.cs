// WinMac Dock — primera iteración (PROVISIONAL). Overlay propio sobre la barra de tareas; NO reemplaza ni modifica la barra de tareas de Windows.
//
// Uso:
//   WinMac.Dock.App                         ejecuta el dock con las apps reales
//   WinMac.Dock.App --demo                  ejecuta el dock con datos de demostración (sin seguir ventanas reales)
//   WinMac.Dock.App --render-png f.png [--pose rest|hover|hidden|open|edge] [--theme light|dark] [--tier tint|wallpaper] [--scale 1.5]
//                                           renderiza FUERA de pantalla (no abre ventana) con el renderizador real y guarda un PNG (para CI)
//   WinMac.Dock.App --bench-offscreen f.json [--theme ..] [--tier ..] [--scale ..]
//                                           mide el coste de dibujar (WARP en CI; NO es FPS real) y escribe un JSON con sus limitaciones
//   WinMac.Dock.App --selftest              comprobaciones no interactivas con diagnóstico; código de salida 0 si todo funciona
//   Opciones: --tier tint|wallpaper · --theme light|dark (por defecto: tema de Windows) · --config ruta.json · --no-config (ignora el archivo guardado)
using System.Diagnostics;
using WinMac.Dock;
using WinMac.Dock.Win.Offscreen;
using WinMac.Dock.Win.Rendering;
using WinMac.Dock.Win.Shell;

internal static class Program
{
    private static string? Arg(string[] a, string n) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    private static StreamWriter? _log;
    private static void Log(string m)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {m}";
        try { Console.WriteLine(line); } catch { }
        try { _log?.WriteLine(line); _log?.Flush(); } catch { }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }
        try { Directory.CreateDirectory(Path.GetDirectoryName(DockDefaults.LogPath)!); _log = new StreamWriter(DockDefaults.LogPath, true); } catch { }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("EXCEPCIÓN NO CONTROLADA: " + e.ExceptionObject);
        var tier = (Arg(args, "--tier")?.ToLowerInvariant()) switch { "tint" => GlassTier.Tint, "wallpaper" => GlassTier.WallpaperBlur, _ => (GlassTier?)null };
        ThemeMode? themeArg = Arg(args, "--theme")?.ToLowerInvariant() switch { "dark" => ThemeMode.Dark, "light" => ThemeMode.Light, _ => null };
        try
        {
            if (Arg(args, "--render-png") is string png)
            {
                var pose = Arg(args, "--pose")?.ToLowerInvariant() switch { "hover" => DemoPose.Hover, "hidden" => DemoPose.Hidden, "open" => DemoPose.Open, "edge" => DemoPose.Edge, _ => DemoPose.Rest };
                double scale = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1.0;
                string info = OffscreenRenderer.Render(png, pose, tier ?? GlassTier.WallpaperBlur, SystemTheme.Resolve(themeArg ?? ThemeMode.Light), scale);
                Log($"PNG escrito: {png} ({info})"); return 0;
            }
            if (Arg(args, "--bench-offscreen") is string json)
            {
                double scale = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 1.0;
                Log("BENCH: " + OffscreenBench.Run(json, tier ?? GlassTier.WallpaperBlur, SystemTheme.Resolve(themeArg ?? ThemeMode.Light), scale)); return 0;
            }
            if (args.Contains("--selftest")) return SelfTest(tier ?? GlassTier.WallpaperBlur);
            return RunDock(args, tier, themeArg);
        }
        catch (Exception e) { Log("ERROR: " + e); return 1; }
    }

    private static int RunDock(string[] args, GlassTier? tier, ThemeMode? themeArg)
    {
        using var mutex = new Mutex(true, "WinMac.Dock.SingleInstance", out bool first);
        if (!first) { Log("Ya hay un dock en ejecución."); return 0; }
        string cfgPath = Arg(args, "--config") ?? DockDefaults.ConfigPath;
        var cfg = args.Contains("--no-config") ? DockDefaults.Create() : DockConfig.LoadOrDefault(cfgPath, DockDefaults.Create);
        if (tier is GlassTier t) cfg = cfg with { GlassTier = t };
        if (themeArg is ThemeMode tm) cfg = cfg with { Theme = tm };
        if (!File.Exists(cfgPath) && !args.Contains("--no-config")) { try { cfg.Save(cfgPath); } catch (Exception e) { Log("No se pudo guardar la configuración: " + e.Message); } }
        using var host = new DockHost(cfg, new DockMetrics(), GlassStyle.For(SystemTheme.Resolve(cfg.Theme)), args.Contains("--demo"), Log);
        Log("Iniciando dock…");
        int rc = host.Run();
        Log("Dock terminado. " + host.Report());
        return rc;
    }

    /// <summary>Cada sección se ejecuta aislada para que un fallo no oculte los demás. Pensado para el runner de CI (sin monitor ni GPU reales).</summary>
    private static int SelfTest(GlassTier tier)
    {
        int failures = 0;
        void Step(string name, Func<string> body)
        {
            var sw = Stopwatch.StartNew();
            try { Log($"[OK ] {name}: {body()} ({sw.ElapsedMilliseconds} ms)"); }
            catch (Exception e) { failures++; Log($"[FALLO] {name}: {e.GetType().Name}: {e.Message}"); }
        }
        Log($"Windows: {Environment.OSVersion.VersionString}; 64 bits: {Environment.Is64BitProcess}");
        Step("Configuración por defecto", () => $"{DockDefaults.Create().Pinned.Count} apps ancladas; Descargas={DockDefaults.DownloadsPath()}");
        Step("Seguimiento de ventanas (SetWinEventHook + EnumWindows)", () =>
        {
            using var tr = new WindowTracker(); var snap = tr.Snapshot();
            return $"hooks={tr.HookCount}; apps con ventana={snap.Count} [{string.Join(", ", snap.Take(5).Select(a => Path.GetFileName(a.ExePath)))}]";
        });
        Step("Íconos del Shell (IShellItemImageFactory)", () =>
        {
            string[] probes = [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe")];
            var results = probes.Select(p => (p, px: IconProvider.Load(p))).ToList();
            if (results.All(r => r.px is null)) throw new InvalidOperationException("ningún ícono cargado");
            return string.Join("; ", results.Select(r => $"{Path.GetFileName(r.p)}={(r.px is null ? "null" : $"{r.px.Width}x{r.px.Height}")}"));
        });
        Step("Detector de pantalla completa", () => $"suprimir={FullscreenWatcher.ShouldSuppress()}");
        Step("Render fuera de pantalla (D2D) → PNG, 5 escenas × 2 temas", () =>
        {
            string dir = Arg(Environment.GetCommandLineArgs(), "--out") ?? "selftest-out"; Directory.CreateDirectory(dir);
            int n = 0; string last = "";
            foreach (var theme in new[] { DockTheme.Light, DockTheme.Dark })
                foreach (var pose in new[] { DemoPose.Rest, DemoPose.Hover, DemoPose.Open, DemoPose.Edge, DemoPose.Hidden })
                { last = OffscreenRenderer.Render(Path.Combine(dir, $"selftest-{theme}-{pose}.png"), pose, tier, theme); n++; }
            return $"{n} PNG; último: {last}";
        });
        Step("Ventana + DirectComposition (30 frames con datos demo)", () =>
        {
            using var host = new DockHost(new DockConfig { GlassTier = tier }, new DockMetrics(), new GlassStyle(), demo: true, Log);
            int rc = host.Run(maxFrames: 30, selfTest: true);
            return $"código {rc}; {host.Report()}";
        });
        Log(failures == 0 ? "SELFTEST: todo OK" : $"SELFTEST: {failures} sección(es) fallaron");
        return failures == 0 ? 0 : 2;
    }
}
