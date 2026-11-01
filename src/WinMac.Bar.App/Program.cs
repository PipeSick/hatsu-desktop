// WinMac Barra superior — primera iteración (PROVISIONAL). Reserva el borde superior con un AppBar real (opcional); NO toca la barra de tareas ni el Explorador.
//
// Uso:
//   WinMac.Bar.App                          ejecuta la barra superior (reloj, batería, volumen, red, app activa)
//   WinMac.Bar.App --no-reserve             sin AppBar: la barra se superpone y no mueve las ventanas
//   WinMac.Bar.App --quit                   pide a la barra en ejecución que se cierre normalmente (libera el espacio reservado)
//   WinMac.Bar.App --status                 imprime lo que leen los proveedores del sistema (batería, volumen, red) — informativo
//   WinMac.Bar.App --render-png f.png [--theme light|dark] [--bg glass|transparent] [--scale 2] [--hover N]   renderiza FUERA de pantalla (para CI)
//   WinMac.Bar.App --selftest               comprobaciones no interactivas (NO registra AppBar); código 0 si todo funciona
//   WinMac.Bar.App --selftest-appbar        SOLO CI: registra y libera un AppBar durante unos fotogramas
//   WinMac.Bar.App --selftest-system        mide qué controles del sistema permite ESTE equipo (audio, Wi-Fi, Bluetooth, brillo, energía, apariencia) sin cambiar nada
//   WinMac.Bar.App --selftest-monitors      lista los monitores que ve Windows y cuáles llevarán barra (modo espejo = una sola); no cambia nada
//   Opciones: --config ruta.json · --no-config
using System.Diagnostics;
using WinMac.Bar;
using WinMac.Bar.Win.Offscreen;
using WinMac.Bar.Win.Rendering;
using WinMac.Bar.Win.Shell;
using WinMac.Ipc;

internal static class Program
{
    private static string? Arg(string[] a, string n) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    private static StreamWriter? _log;
    private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac");
    private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "bar.json");
    private static void Log(string m)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {m}";
        try { Console.WriteLine(line); } catch { }
        try { _log?.WriteLine(line); _log?.Flush(); } catch { }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        try { var dir = Path.Combine(DataDir, "logs"); Directory.CreateDirectory(dir); _log = new StreamWriter(Path.Combine(dir, "bar.log"), true); } catch { }
        try
        {
            if (Arg(args, "--render-png") is string png)
            {
                var theme = Arg(args, "--theme")?.ToLowerInvariant() == "dark" ? BarTheme.Dark : BarTheme.Light;
                var bg = Arg(args, "--bg")?.ToLowerInvariant() == "transparent" ? BarBackground.Transparent : BarBackground.Glass;
                double scale = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 2.0;
                int hover = int.TryParse(Arg(args, "--hover"), out var h) ? h : -1;
                Log("PNG: " + BarOffscreen.Render(png, theme, bg, scale, hoverItem: hover)); return 0;
            }
            if (args.Contains("--status")) return Status();
            if (args.Contains("--quit")) return Quit();
            if (args.Contains("--selftest-system")) return SystemProbe();
            if (args.Contains("--selftest-monitors")) return MonitorProbe();
            if (args.Contains("--selftest")) return SelfTest(appBar: false);
            if (args.Contains("--selftest-appbar")) return SelfTest(appBar: true);
            return RunBar(args);
        }
        catch (Exception e) { Log("ERROR: " + e); return 1; }
    }

    private static int RunBar(string[] args)
    {
        using var mutex = new Mutex(true, "WinMac.Bar.SingleInstance", out bool first);   // dos barras reservarían dos veces el borde superior
        if (!first) { Log("Ya hay una barra superior en ejecución."); return 0; }
        string? cfgPath = args.Contains("--no-config") ? null : Arg(args, "--config") ?? ConfigPath;
        var cfg = cfgPath is null ? new BarConfig() : BarConfig.LoadOrDefault(cfgPath);
        if (args.Contains("--no-reserve")) cfg = cfg with { ReserveSpace = false };
        using var host = new BarHost(cfg, cfgPath, Log);
        // --hosted (lo pasa WinMac.exe): enlace con el host. Sin él, la barra funciona exactamente como siempre. Si el host cae, la barra sigue sola.
        var link = ModuleHostLink.FromArgs(args, "bar", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0");
        if (link is not null)
        {
            link.ShutdownRequested += host.RequestQuit; link.HealthCheck = () => host.Responsive; link.Subscribe(YieldRegions.Topic, d => host.SetYield(YieldRegions.Parse(d)));
            // Ajustes centrales: el host es la única fuente de verdad. Llegan retenidos al conectar y tras cada cambio; los cambios hechos desde la barra se piden al host.
            link.Subscribe("config.bar", d => { try { if (d is { } e && System.Text.Json.JsonSerializer.Deserialize<BarConfig>(e.GetRawText(), BarConfig.Wire) is { } c) host.ApplyConfig(c.Sanitize()); } catch (Exception ex) { Log("Ajustes de la barra ilegibles: " + ex.Message); } });
            host.PersistPatch = patch => link.Event("config.bar.set", patch);
            link.Start(); Log("Modo hosted: enlazada con WinMac.exe."); }
        Log("Barra superior iniciada. Cierra con «WinMac.Bar.App --quit» o desde su menú (clic derecho).");
        int rc = host.Run(); Log("Barra superior cerrada. " + host.Report());
        if (link is not null) { if (rc == 0) link.ByeAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); link.DisposeAsync().AsTask().GetAwaiter().GetResult(); }   // «bye» = cierre voluntario: el host no nos reinicia
        return rc;
    }

    private static int SystemProbe()
    {
        Log("Controles del sistema que permite este equipo (no se cambia nada):");
        var lines = WinMac.SystemControl.Win.SystemServicesFactory.Probe(WinMac.SystemControl.Win.SystemServicesFactory.CreateReal());
        foreach (var l in lines) Log("  " + l);
        int bad = lines.Count(l => l.StartsWith("[FALLO]"));
        Log(bad == 0 ? "Ningún servicio lanzó excepciones (un servicio «no disponible» es un resultado válido)." : $"{bad} servicio(s) lanzaron excepciones: es un error.");
        return bad == 0 ? 0 : 1;
    }

    private static int MonitorProbe()
    {
        Log("Monitores (no se cambia nada):");
        foreach (var l in BarHost.DescribeMonitors()) Log(l);
        return 0;
    }

    private static int Quit()
    {
        try { using var ev = EventWaitHandle.OpenExisting(BarHost.QuitEventName); ev.Set(); Log("Señal de cierre enviada."); return 0; }
        catch (WaitHandleCannotBeOpenedException) { Log("La barra superior no está en ejecución."); return 0; }
    }

    private static int Status()
    {
        var b = SystemProviders.Battery(); var v = SystemProviders.Volume(); var n = SystemProviders.Network();
        Log($"Batería: presente={b.Present} {b.Percent}% cargando={b.Charging} enchufada={b.Plugged}");
        Log($"Volumen: disponible={v.Available} {v.Percent}% silenciado={v.Muted} ondas={v.Waves}");
        Log($"Red: {n.Kind}");
        Log($"Reloj: {ClockFormat.Format(DateTime.Now, new BarConfig())}");
        return 0;
    }

    private static int SelfTest(bool appBar)
    {
        int failures = 0;
        void Step(string name, Func<string> body)
        {
            var sw = Stopwatch.StartNew();
            try { Log($"[OK ] {name}: {body()} ({sw.ElapsedMilliseconds} ms)"); } catch (Exception e) { failures++; Log($"[FALLO] {name}: {e.GetType().Name}: {e.Message}"); }
        }
        Log($"Windows: {Environment.OSVersion.VersionString}; 64 bits: {Environment.Is64BitProcess}");
        Step("Proveedores del sistema (informativo: dependen del equipo)", () =>
        {
            var b = SystemProviders.Battery(); var v = SystemProviders.Volume(); var n = SystemProviders.Network();
            return $"batería presente={b.Present} ({b.Percent}%), volumen disponible={v.Available} ({v.Percent}%), red={n.Kind}";
        });
        Step("Configuración por defecto y reloj", () => $"{ClockFormat.Format(DateTime.Now, new BarConfig())}; reservar espacio={new BarConfig().ReserveSpace}");
        Step("Render fuera de pantalla (D2D) → PNG, 2 temas × 2 fondos", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "winmac-bar-selftest"); Directory.CreateDirectory(dir); string last = "";
            foreach (var t in new[] { BarTheme.Light, BarTheme.Dark }) foreach (var bg in new[] { BarBackground.Glass, BarBackground.Transparent }) last = BarOffscreen.Render(Path.Combine(dir, $"bar-{t}-{bg}.png"), t, bg, 1.0, 900, hoverItem: 4);
            return "4 PNG; último: " + last;
        });
        Step(appBar ? "Ventana + DirectComposition + AppBar (30 fotogramas; registra y libera el AppBar)" : "Ventana + DirectComposition (30 fotogramas; SIN AppBar)", () =>
        {
            using var host = new BarHost(new BarConfig { ReserveSpace = appBar }, null, Log, trackActiveApp: true);
            int rc = host.Run(maxFrames: 30, selfTest: true);
            if (rc != 0) throw new Exception($"el bucle devolvió {rc}");
            return host.Report();
        });
        Log(failures == 0 ? "SELFTEST: todo OK" : $"SELFTEST: {failures} fallo(s)");
        return failures == 0 ? 0 : 1;
    }
}
