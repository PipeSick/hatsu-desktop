// WinMac Dock — primera iteración (PROVISIONAL). Overlay propio sobre la barra de tareas; NO reemplaza ni modifica la barra de tareas de Windows.
//
// Uso:
//   WinMac.Dock.App                         ejecuta el dock con las apps reales (íconos originales, apps ancladas persistentes, menú con clic derecho)
//   WinMac.Dock.App --demo                  SOLO PRUEBAS: dock con datos de demostración (íconos de letra, sin seguir ventanas reales)
//   WinMac.Dock.App --quit                  pide al dock en ejecución que se cierre normalmente
//   WinMac.Dock.App --list-pinned           muestra las apps ancladas (id estable, tipo, título)
//   WinMac.Dock.App --pin <ruta.exe|AUMID> [--title T]   ancla una app (Win32 por ruta; empaquetada por AUMID, p. ej. Microsoft.WindowsCalculator_8wekyb3d8bbwe!App)
//   WinMac.Dock.App --unpin <id|ruta|AUMID> desancla una app
//   WinMac.Dock.App --icon-test <ruta.exe|AUMID> [--out icono.png]   extrae el icono original con el mismo servicio que usa el dock y lo guarda como PNG
//   WinMac.Dock.App --set-size 32..128 | --set-align left|center|right | --set-raise 0..400   cambia el tamaño y la posición (edita dock.json; un dock en marcha lo aplica en ≤ 2 s) · --show-layout muestra los valores
//   WinMac.Dock.App --render-png f.png [--pose rest|hover|hidden|open|edge|preview] [--theme light|dark] [--tier tint|wallpaper] [--scale 1.5] [--size 60] [--align left|center|right]
//                                           renderiza FUERA de pantalla (no abre ventana) con el renderizador real y guarda un PNG (para CI)
//   WinMac.Dock.App --bench-offscreen f.json [--theme ..] [--tier ..] [--scale ..]
//                                           mide el coste de dibujar (WARP en CI; NO es FPS real) y escribe un JSON con sus limitaciones
//   WinMac.Dock.App --selftest              comprobaciones no interactivas con diagnóstico; código de salida 0 si todo funciona
//   Opciones: --tier tint|wallpaper · --theme light|dark (por defecto: tema de Windows) · --config ruta.json · --no-config (ignora el archivo guardado)
using System.Diagnostics;
using WinMac.Apps;
using WinMac.Apps.Win;
using WinMac.Dock;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Offscreen;
using WinMac.Dock.Win.Rendering;
using WinMac.Dock.Win.Shell;
using WinMac.Ipc;

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
                var pose = Arg(args, "--pose")?.ToLowerInvariant() switch { "hover" => DemoPose.Hover, "hidden" => DemoPose.Hidden, "open" => DemoPose.Open, "edge" => DemoPose.Edge, "preview" => DemoPose.Preview, _ => DemoPose.Rest };
                double scale = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 1.0;
                double size = double.TryParse(Arg(args, "--size"), System.Globalization.CultureInfo.InvariantCulture, out var isz) ? isz : 60;
                var align = Arg(args, "--align")?.ToLowerInvariant() switch { "left" => DockAlignment.Left, "right" => DockAlignment.Right, _ => DockAlignment.Center };
                string info = OffscreenRenderer.Render(png, pose, tier ?? GlassTier.WallpaperBlur, SystemTheme.Resolve(themeArg ?? ThemeMode.Light), scale, iconSize: size, alignment: align);
                Log($"PNG escrito: {png} ({info})"); return 0;
            }
            if (Arg(args, "--bench-offscreen") is string json)
            {
                double scale = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 1.0;
                Log("BENCH: " + OffscreenBench.Run(json, tier ?? GlassTier.WallpaperBlur, SystemTheme.Resolve(themeArg ?? ThemeMode.Light), scale)); return 0;
            }
            if (args.Contains("--selftest")) return SelfTest(tier ?? GlassTier.WallpaperBlur);
            if (args.Contains("--quit")) return Quit();
            if (args.Contains("--list-pinned")) return ConfigCommand(args, null);
            if (Arg(args, "--pin") is string toPin) return ConfigCommand(args, ("pin", toPin));
            if (Arg(args, "--unpin") is string toUnpin) return ConfigCommand(args, ("unpin", toUnpin));
            if (Arg(args, "--set-size") is not null || Arg(args, "--set-align") is not null || Arg(args, "--set-raise") is not null || args.Contains("--show-layout")) return LayoutCommand(args);
            if (Arg(args, "--icon-test") is string iconTarget) return IconTest(iconTarget, Arg(args, "--out"));
            if (args.Contains("--thumb-test")) return ThumbTest(Arg(args, "--out"));
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
        cfg = DockSettings.Sanitize(cfg);
        if (!File.Exists(cfgPath) && !args.Contains("--no-config")) { try { cfg.Save(cfgPath); } catch (Exception e) { Log("No se pudo guardar la configuración: " + e.Message); } }
        bool demo = args.Contains("--demo"); string? persist = args.Contains("--no-config") ? null : cfgPath;
        if (demo || args.Contains("--single"))   // demostración o un solo Dock (en el monitor principal), como antes
        {
            using var host = new DockHost(cfg, DockMetrics.ForIconSize(cfg.IconSize), GlassStyle.For(SystemTheme.Resolve(cfg.Theme)), demo, Log, persist);
            var link1 = ModuleHostLink.FromArgs(args, "dock", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0");
            if (link1 is not null) { link1.ShutdownRequested += host.RequestQuit; link1.HealthCheck = () => host.Responsive; link1.Start(); Log("Modo hosted: enlazado con HatsuDesktop.exe."); }
            Log("Iniciando dock…");
            int rc1 = host.Run();
            Log("Dock terminado. " + host.Report());
            if (link1 is not null) { if (rc1 == 0) link1.ByeAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); link1.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            return rc1;
        }

        // Un Dock en cada pantalla, cada uno en su hilo (ventana, bucle de mensajes y seguimiento de ventanas propios). Las apps ancladas y los ajustes se comparten por dock.json:
        // cada Dock guarda lo que cambia en él y los demás lo releen al ver el archivo cambiado.
        var fleet = new List<(string Key, DockHost Host, Thread Thread)>(); var gate = new object(); bool quitting = false;
        void StartOn((int Left, int Top, int Right, int Bottom) mon)
        {
            string key = DockFleet.Key(mon); using var ready = new ManualResetEventSlim();
            var th = new Thread(() =>
            {
                DockHost? h = null;
                try
                {
                    var c = persist is null ? cfg : DockSettings.Sanitize(DockConfig.LoadOrDefault(persist, DockDefaults.Create));   // lo último guardado (otro Dock puede haberlo cambiado)
                    if (tier is GlassTier tt) c = c with { GlassTier = tt };
                    if (themeArg is ThemeMode tmm) c = c with { Theme = tmm };
                    h = new DockHost(c, DockMetrics.ForIconSize(c.IconSize), GlassStyle.For(SystemTheme.Resolve(c.Theme)), false, Log, persist, mon);
                    lock (gate) fleet.Add((key, h, Thread.CurrentThread));
                    ready.Set();
                    Log($"Iniciando dock en el monitor {key}…");
                    h.Run();
                    Log($"Dock del monitor {key} terminado{(h.MonitorGone ? " (el monitor ya no existe)" : "")}. " + h.Report());
                    if (!h.MonitorGone) lock (gate) quitting = true;   // cerrado a propósito (señal de cierre): se cierran todos
                }
                catch (Exception e) { Log($"Dock del monitor {key} falló: " + e); }
                finally { lock (gate) fleet.RemoveAll(x => x.Key == key); h?.Dispose(); try { ready.Set(); } catch (ObjectDisposedException) { } }
            }) { Name = "Dock " + key, IsBackground = false };
            th.SetApartmentState(ApartmentState.STA); th.Start();
            ready.Wait(TimeSpan.FromSeconds(10));
        }

        List<DockHost> Hosts() { lock (gate) return fleet.Select(x => x.Host).ToList(); }
        var link = ModuleHostLink.FromArgs(args, "dock", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0");
        if (link is not null) { link.ShutdownRequested += () => { lock (gate) quitting = true; foreach (var h in Hosts()) h.RequestQuit(); }; link.HealthCheck = () => Hosts().All(h => h.Responsive); link.Start(); Log("Modo hosted: enlazado con HatsuDesktop.exe."); }

        using var quitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, DockDefaults.QuitEventName);
        while (true)
        {
            List<string> running; lock (gate) running = fleet.Select(x => x.Key).ToList();
            foreach (var mon in DockFleet.ToStart(DockHost.Monitors(), running)) StartOn(mon);   // al arrancar y cuando aparece una pantalla nueva
            bool q; lock (gate) q = quitting;
            if (q || quitEvent.WaitOne(500)) break;   // cierre en ≤ 0,5 s; las pantallas nuevas se miran en cada vuelta (EnumDisplayMonitors es barato)
        }

        foreach (var h in Hosts()) h.RequestQuit();
        List<Thread> threads; lock (gate) threads = fleet.Select(x => x.Thread).ToList();
        foreach (var th in threads) th.Join(TimeSpan.FromSeconds(5));
        Log("Docks terminados.");
        if (link is not null) { link.ByeAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); link.DisposeAsync().AsTask().GetAwaiter().GetResult(); }   // «bye» = cierre voluntario: el host no nos reinicia
        return 0;
    }

    private static int Quit()
    {
        try { using var ev = EventWaitHandle.OpenExisting(DockDefaults.QuitEventName); ev.Set(); Log("Señal de cierre enviada al dock."); return 0; }
        catch (WaitHandleCannotBeOpenedException) { Log("No hay un dock en ejecución."); return 0; }
    }

    /// <summary>Identidad desde un argumento: «aumid:XXX», un AUMID (contiene «!») o la ruta de un ejecutable.</summary>
    private static AppIdentity ParseTarget(string t, string? title)
    {
        if (t.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase)) return AppIdentity.FromAumid(t[6..], title);
        if (t.Contains('!') && !t.Contains('\\') && !t.Contains('/')) return AppIdentity.FromAumid(t, title);
        return AppIdentity.FromExe(Path.GetFullPath(Environment.ExpandEnvironmentVariables(t)), title);
    }

    private static int ConfigCommand(string[] args, (string Op, string Target)? op)
    {
        string path = Arg(args, "--config") ?? DockDefaults.ConfigPath; var cfg = DockConfig.LoadOrDefault(path, DockDefaults.Create);
        if (op is { } o)
        {
            var id = ParseTarget(o.Target, Arg(args, "--title") ?? null);
            if (o.Op == "pin") { var n = DockPinning.Pin(cfg, id, Arg(args, "--title") ?? DockDefaults.Title(id)); Log(ReferenceEquals(n, cfg) ? "Ya estaba anclada." : $"Anclada: {id.Id}"); cfg = n; }
            else { var n = DockPinning.Unpin(cfg, id.Id); Log(ReferenceEquals(n, cfg) ? "No estaba anclada." : $"Desanclada: {id.Id}"); cfg = n; }
            if (!cfg.TrySave(path)) { Log("No se pudo guardar la configuración."); return 1; }
            Log("Si el dock está en ejecución, recarga el archivo en unos segundos.");
        }
        foreach (var p in cfg.Pinned) Log($"  {p.StableId}  [{p.ToIdentity()?.Kind}]  {p.Title}");
        return 0;
    }

    /// <summary>Cambia el tamaño o la posición del Dock editando dock.json (un Dock en marcha lo aplica en ≤ 2 s). Sin argumentos de cambio, solo muestra los valores.</summary>
    private static int LayoutCommand(string[] args)
    {
        string path = Arg(args, "--config") ?? DockDefaults.ConfigPath; var cfg = DockSettings.Sanitize(DockConfig.LoadOrDefault(path, DockDefaults.Create)); var before = cfg;
        if (Arg(args, "--set-size") is string sz) { if (double.TryParse(sz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) cfg = DockSettings.WithIconSize(cfg, v); else { Log("--set-size espera un número (32 a 128)."); return 2; } }
        if (Arg(args, "--set-align") is string al)
        {
            switch (al.ToLowerInvariant()) { case "left" or "izquierda": cfg = DockSettings.WithAlignment(cfg, DockAlignment.Left); break; case "center" or "centro": cfg = DockSettings.WithAlignment(cfg, DockAlignment.Center); break; case "right" or "derecha": cfg = DockSettings.WithAlignment(cfg, DockAlignment.Right); break; default: Log("--set-align espera left, center o right."); return 2; }
        }
        if (Arg(args, "--set-raise") is string rs) { if (double.TryParse(rs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) cfg = cfg with { BottomOffset = DockSettings.ClampBottom(v) }; else { Log("--set-raise espera un número (0 a 400)."); return 2; } }
        if (!ReferenceEquals(cfg, before) && cfg != before) { if (!cfg.TrySave(path)) { Log("No se pudo guardar la configuración."); return 1; } Log("Guardado. Si el dock está en ejecución, lo aplica en unos segundos."); }
        Log($"Tamaño del icono: {cfg.IconSize:0} (32–128, normal 60) · alineación: {cfg.Alignment} · margen lateral: {cfg.EdgeMargin:0} · altura extra: {cfg.BottomOffset:0} (0–400)");
        return 0;
    }

    private static int IconTest(string target, string? outPng)
    {
        var id = ParseTarget(target, null);
        using var svc = new IconService(new ShellIconProvider(), new IconCache(8, null));
        var b = svc.GetAsync(new IconRequest(id)).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        if (b is null) { Log($"Sin icono válido para {id.Id}: el Dock usaría el mosaico de reemplazo. Estadísticas: {svc.Stats}"); return 1; }
        Log($"Icono de {id.Id}: {b.Width}x{b.Height}px (proporción {(double)b.Width / b.Height:0.00}); válido");
        if (outPng is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPng))!); File.WriteAllBytes(outPng, PngWriter.EncodeRgba(PngWriter.BgraPremulToRgba(b.Bgra, b.Width * 4, b.Width, b.Height), b.Width, b.Height)); Log("PNG: " + outPng); }
        return 0;
    }

    /// <summary>Captura las miniaturas de todas las ventanas de app abiertas (las mismas que usa la vista previa) y las guarda como PNG. Informativo: depende de las ventanas que haya.</summary>
    private static int ThumbTest(string? outDir)
    {
        using var tracker = new WinMac.Dock.Win.Shell.WindowTracker(); var apps = tracker.Snapshot(); int ok = 0, fail = 0, skipped = 0, n = 0;
        if (outDir is not null) Directory.CreateDirectory(outDir);
        foreach (var app in apps)
            foreach (var w in app.Windows)
            {
                n++;
                if (w.Minimized) { skipped++; Log($"  (minimizada, se omite) {app.Identity.DisplayName}: {w.Title}"); continue; }
                var sw = Stopwatch.StartNew(); var b = WinMac.Dock.Win.Shell.WindowThumbnails.Capture(new IntPtr(w.Hwnd));
                if (b is null) { fail++; Log($"  FALLÓ {app.Identity.DisplayName}: {w.Title} ({sw.ElapsedMilliseconds} ms; negra, protegida o sin respuesta)"); continue; }
                ok++; Log($"  OK {app.Identity.DisplayName}: {w.Title} → {b.Width}x{b.Height}px ({sw.ElapsedMilliseconds} ms)");
                if (outDir is not null) File.WriteAllBytes(Path.Combine(outDir, $"thumb-{n:00}-{new string(app.Identity.DisplayName.Where(char.IsLetterOrDigit).ToArray())}.png"), PngWriter.EncodeRgba(PngWriter.BgraPremulToRgba(b.Bgra, b.Width * 4, b.Width, b.Height), b.Width, b.Height));
            }
        Log($"Miniaturas: {ok} correctas, {fail} fallidas, {skipped} minimizadas omitidas, de {n} ventanas en {apps.Count} apps.");
        return 0;
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
        Step("Configuración por defecto", () => $"{DockDefaults.Create().Pinned.Count} apps ancladas; Descargas={DockDefaults.DownloadsPath()}; iconos en caché: {DockDefaults.IconCachePath}");
        Step("Seguimiento de ventanas (SetWinEventHook + EnumWindows)", () =>
        {
            using var tr = new WindowTracker(); var snap = tr.Snapshot();
            return $"hooks={tr.HookCount}; apps con ventana={snap.Count}";
        });
        Step("Identidad de las ventanas abiertas (AUMID / ejecutable)", () =>
        {
            using var tr = new WindowTracker(); var snap = tr.Snapshot();
            if (snap.Select(x => x.Identity.Id).Distinct().Count() != snap.Count) throw new InvalidOperationException("identidades duplicadas en la instantánea");
            return $"{snap.Count} apps; empaquetadas={snap.Count(x => x.Identity.Kind == AppKind.Packaged)}; [{string.Join(", ", snap.Take(6).Select(a => a.Identity.Id.Length > 48 ? a.Identity.Id[..48] + "…" : a.Identity.Id))}]";
        });
        Step("Servicio de iconos Win32 (original, sin deformar) + caché de disco", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "winmac-selftest-icons-" + Guid.NewGuid().ToString("N")); var report = new List<string>();
            try
            {
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows), sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var ids = new[] { Path.Combine(win, "explorer.exe"), Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "cmd.exe") }.Where(File.Exists).Select(p => AppIdentity.FromExe(p)).ToList();
                int ok = 0;
                using (var svc = new IconService(new ShellIconProvider(), new IconCache(16, dir)))
                    foreach (var id in ids) { var b = svc.GetAsync(new IconRequest(id)).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); if (b is not null) { ok++; report.Add($"{Path.GetFileName(id.ExePath)}={b.Width}x{b.Height}"); } else report.Add($"{Path.GetFileName(id.ExePath)}=sin icono"); }
                if (ok == 0) throw new InvalidOperationException("ningún icono Win32 cargado");
                using var svc2 = new IconService(new ShellIconProvider(), new IconCache(16, dir));
                foreach (var id in ids) svc2.GetAsync(new IconRequest(id)).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
                report.Add($"caché de disco: {svc2.Stats.DiskHits} aciertos de {ids.Count}");
                if (svc2.Stats.DiskHits == 0) throw new InvalidOperationException("la caché de disco no sirvió ningún icono en la segunda ejecución");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            return string.Join("; ", report);
        });
        Step("Apps empaquetadas MSIX/UWP (informativo: depende de lo instalado en el equipo)", () =>
        {
            string[] aumids = ["Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", "MicrosoftWindows.Client.CBS_cw5n1h2txyewy!CortanaUI"];
            var found = new List<string>(); using var svc = new IconService(new ShellIconProvider(), new IconCache(8, null));
            foreach (var a in aumids) { var id = AppIdentity.FromAumid(a); var b = svc.GetAsync(new IconRequest(id)).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); found.Add($"{a.Split('_')[0]}={(b is null ? "sin icono/no instalada" : $"{b.Width}x{b.Height}")} nombre='{DisplayNames.Resolve(id)}'"); }
            return string.Join("; ", found);
        });
        Step("Ancladas: anclar, reordenar, desanclar, guardar y recuperar (configuración temporal)", () =>
        {
            string f = Path.Combine(Path.GetTempPath(), "winmac-selftest-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var a = AppIdentity.FromExe(Path.Combine(sys, "notepad.exe")); var b = AppIdentity.FromExe(Path.Combine(sys, "cmd.exe")); var c = AppIdentity.FromAumid("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculadora");
                var cfg = DockPinning.Pin(DockPinning.Pin(DockPinning.Pin(new DockConfig(), a, "A"), b, "B"), c, "C");
                cfg = DockPinning.Move(cfg, c.Id, 0); cfg = DockPinning.Unpin(cfg, b.Id);
                if (!cfg.TrySave(f)) throw new InvalidOperationException("no se pudo guardar");
                var back = DockConfig.LoadOrDefault(f, () => new DockConfig()); var order = string.Join(",", back.Pinned.Select(p => p.Title));
                if (order != "C,A") throw new InvalidOperationException("orden inesperado tras recuperar: " + order);
                return $"orden recuperado = {order}; tipos = {string.Join(",", back.Pinned.Select(p => p.ToIdentity()!.Kind))}";
            }
            finally { try { File.Delete(f); } catch { } }
        });
        Step("Modelo del Dock con apps REALES (ancladas por defecto + abiertas, sin duplicados) y menú", () =>
        {
            using var tr = new WindowTracker(); var cfg = DockDefaults.Create(); var items = new DockModelBuilder(cfg, DockDefaults.Title).Build(tr.Snapshot(), cfg.DownloadsPath);
            var apps = items.Where(i => i.Kind == DockItemKind.App).ToList();
            if (apps.Select(i => i.Id).Distinct().Count() != apps.Count) throw new InvalidOperationException("ítems de app duplicados");
            var first = apps.FirstOrDefault(); int menu = first is null ? 0 : DockMenu.ForItem(first, 0, apps.Count(a => a.Pinned)).Count;
            return $"{apps.Count} apps ({apps.Count(a => a.Pinned)} ancladas, {apps.Count(a => a.Running)} abiertas); entradas del menú del primero = {menu}";
        });
        Step("Miniaturas de ventanas (reducción + captura de las ventanas abiertas)", () =>
        {
            var src = new byte[200 * 100 * 4]; for (int i = 0; i < src.Length; i += 4) { src[i] = 200; src[i + 1] = 120; src[i + 2] = 40; src[i + 3] = 255; }
            var small = WinMac.Dock.Win.Shell.WindowThumbnails.Downscale(src, 200, 0, 0, 200, 100, 50, 50) ?? throw new Exception("la reducción devolvió null");
            if (small.Width != 50 || small.Height != 25) throw new Exception($"tamaño inesperado {small.Width}x{small.Height}");
            if (WinMac.Dock.Win.Shell.WindowThumbnails.Downscale(new byte[200 * 100 * 4], 200, 0, 0, 200, 100, 50, 50) is not null) throw new Exception("una imagen negra debía descartarse");
            using var tr = new WinMac.Dock.Win.Shell.WindowTracker(); int ok = 0, fail = 0, total = 0;
            foreach (var app in tr.Snapshot()) foreach (var w in app.Windows) { total++; if (w.Minimized) continue; if (WinMac.Dock.Win.Shell.WindowThumbnails.Capture(new IntPtr(w.Hwnd)) is null) fail++; else ok++; }
            return $"reducción OK (200x100→50x25; negro descartado); capturas de ventanas reales: {ok} correctas, {fail} fallidas de {total} (informativo)";
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
