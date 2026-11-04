// WinMac Barra superior — primera iteración (PROVISIONAL). Reserva el borde superior con un AppBar real (opcional); NO toca la barra de tareas ni el Explorador.
//
// Uso:
//   WinMac.Bar.App                          ejecuta la barra superior (reloj, batería, volumen, red, app activa)
//   WinMac.Bar.App --no-reserve             sin AppBar: la barra se superpone y no mueve las ventanas
//   WinMac.Bar.App --quit                   pide a la barra en ejecución que se cierre normalmente (libera el espacio reservado)
//   WinMac.Bar.App --status                 imprime lo que leen los proveedores del sistema (batería, volumen, red) — informativo
//   WinMac.Bar.App --render-png f.png [--theme light|dark] [--bg glass|transparent] [--scale 2] [--hover N]   renderiza FUERA de pantalla (para CI)
//   WinMac.Bar.App --render-panel hub|weather --out f.png [--tab sound|system|network|disks] [--theme light|dark] [--scale 2] [--max-height DIP] [--scroll DIP] [--bands]   panel con DATOS DE MUESTRA, fuera de pantalla (para comparar con las referencias)
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
        // Un cierre inesperado deja su causa en bar.log (solo la excepción y su pila; nada del usuario).
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { try { Log("CIERRE INESPERADO: " + e.ExceptionObject); } catch { } };
        // El canal con el host (latido) corre en el grupo de hilos: un mínimo holgado evita que trabajos que bloquean hilos (iconos del Shell, WMI) lo dejen sin hilo.
        ThreadPool.GetMinThreads(out int wt, out int io); ThreadPool.SetMinThreads(Math.Max(wt, 16), Math.Max(io, 8));
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
            if (Arg(args, "--render-dialog") is string dkind)   // It7 (CI): diálogos de confirmación con datos reales de cada acción, en ambos temas
            {
                bool dark = Arg(args, "--theme")?.ToLowerInvariant() != "light"; double ds = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var dsc) ? dsc : 2.0;
                Log("PNG: " + DialogOffscreen.Render(Arg(args, "--out") ?? "dialog.png", dkind.ToLowerInvariant(), dark, ds)); return 0;
            }
            if (Arg(args, "--render-panel") is string kind)
            {
                var theme = Arg(args, "--theme")?.ToLowerInvariant() == "light" ? BarTheme.Light : BarTheme.Dark; double scale = double.TryParse(Arg(args, "--scale"), System.Globalization.CultureInfo.InvariantCulture, out var sc2) ? sc2 : 2.0;
                if (!Enum.TryParse<WinMac.Bar.Hub.HubTab>(Arg(args, "--tab"), true, out var tab)) tab = WinMac.Bar.Hub.HubTab.Sound;
                double maxH = double.TryParse(Arg(args, "--max-height"), System.Globalization.CultureInfo.InvariantCulture, out var mh) ? mh : double.PositiveInfinity, scrollTo = double.TryParse(Arg(args, "--scroll"), System.Globalization.CultureInfo.InvariantCulture, out var sy) ? sy : 0;
                Log("PNG: " + PanelOffscreen.Render(Arg(args, "--out") ?? "panel.png", kind.ToLowerInvariant(), tab, theme, scale, maxH, scrollTo, bands: args.Contains("--bands"))); return 0;
            }
            if (args.Contains("--status")) return Status();
            if (args.Contains("--audio-route")) return AudioRoute(args);   // WM-035: proceso auxiliar (experimental), lo lanza la barra
            if (args.Contains("--quit")) return Quit();
            if (args.Contains("--launchpad")) return Signal(WinMac.Bar.Win.Rendering.BarHost.LaunchpadEventName, "Launchpad");
            if (args.Contains("--spotlight")) return Signal(WinMac.Bar.Win.Rendering.BarHost.SpotlightEventName, "Spotlight");
            if (Arg(args, "--edit") is string ep)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BarHost.EditRequestFile)!); File.WriteAllText(BarHost.EditRequestFile, Path.GetFullPath(ep));
                return Signal(BarHost.EditEventName, "Editar captura");
            }
            if (Arg(args, "--workspace") is string wm) return Signal(WinMac.Bar.Win.Rendering.BarHost.CaptureEventName("ws-" + wm.ToLowerInvariant()), "Espacio de trabajo " + wm);
            if (Arg(args, "--diag") is "gc") return Signal(WinMac.Bar.Win.Rendering.BarHost.CaptureEventName("diag-gc"), "Diagnóstico GC");
            if (Arg(args, "--record") is string rm) return Signal(WinMac.Bar.Win.Rendering.BarHost.CaptureEventName("record-" + rm.ToLowerInvariant()), "Grabación " + rm);
            if (Arg(args, "--capture") is string cm) return Signal(WinMac.Bar.Win.Rendering.BarHost.CaptureEventName(cm.ToLowerInvariant()), "Captura " + cm);
            if (args.Contains("--selftest-system")) return SystemProbe();
            if (args.Contains("--selftest-monitors")) return MonitorProbe();
            if (args.Contains("--selftest")) return SelfTest(appBar: false);
            if (args.Contains("--selftest-appbar")) return SelfTest(appBar: true);
            return RunBar(args);
        }
        catch (Exception e) { Log("ERROR: " + e); return 1; }
    }

    /// <summary>
    /// WM-035: salida de audio por aplicación en un proceso aparte. Una línea JSON por la salida estándar. «probe» no cambia nada; «get» solo lee; «set» aplica y comprueba.
    /// </summary>
    private static int AudioRoute(string[] args)
    {
        int i = Array.IndexOf(args, "--audio-route"); string op = i + 1 < args.Length ? args[i + 1] : "probe";
        static void Out(object o) { Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(o)); Console.Out.Flush(); }
        var f = WinMac.SystemControl.Win.AudioPolicyRouting.Factory(out var why);
        if (f == IntPtr.Zero) { Out(new { ok = false, why }); return 0; }
        try
        {
            switch (op)
            {
                case "probe": Out(new { ok = true }); return 0;
                case "get":
                {
                    var map = new Dictionary<string, string?>();
                    foreach (var part in (i + 2 < args.Length ? args[i + 2] : "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (int.TryParse(part, out int pid)) { try { map[pid.ToString()] = WinMac.SystemControl.Win.AudioPolicyRouting.Get(f, pid); } catch (Exception) { } }
                    Out(new { ok = true, outputs = map }); return 0;
                }
                case "set":
                {
                    if (i + 3 >= args.Length || !int.TryParse(args[i + 2], out int pid)) { Out(new { ok = false, why = "faltan datos" }); return 0; }
                    string? dev = args[i + 3] == "default" ? null : args[i + 3];
                    var err = WinMac.SystemControl.Win.AudioPolicyRouting.Set(f, pid, dev);
                    Out(new { ok = err is null, why = err }); return 0;
                }
                default: Out(new { ok = false, why = "orden desconocida" }); return 0;
            }
        }
        catch (Exception e) { Out(new { ok = false, why = e.GetType().Name + (e is System.Runtime.InteropServices.COMException c ? $" 0x{c.HResult:X8}" : "") }); return 0; }
        finally { WinMac.SystemControl.Win.AudioPolicyRouting.Release(f); }
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
            // La Isla (NotchWin): la barra le cuenta los cambios de volumen y brillo y las notificaciones nuevas, según lo que pida en sus ajustes centrales.
            link.Subscribe("capture.edit", d => { if (d is { ValueKind: System.Text.Json.JsonValueKind.Object } e && e.TryGetProperty("path", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String) host.RequestEdit(p.GetString()); });
            link.Subscribe("config.notch", d => host.ApplyNotchOptions(WinMac.Bar.Notch.NotchFeedOptions.Parse(d)));
            host.NotchEvent = payload => link.Event(WinMac.Bar.Notch.NotchFeed.Topic, payload);
            // WinMac Connect (opcional): estado, lista y avisos del iPhone. Sin el módulo no llega nada.
            link.Subscribe(WinMac.Connect.ConnectTopics.Config, d => host.ApplyConnectConfig(WinMac.Connect.ConnectSettings.Parse(d)));
            link.Subscribe(WinMac.Connect.ConnectTopics.State, d => host.ApplyPhoneState(WinMac.Connect.ConnectWire.ParseState(d)));
            link.Subscribe(WinMac.Connect.ConnectTopics.Notifications, d => host.ApplyPhoneList(WinMac.Connect.ConnectWire.ParseList(d)));
            link.Subscribe(WinMac.Connect.ConnectTopics.Alert, d => { if (WinMac.Connect.ConnectWire.ParseItem(d) is { } it) host.OnPhoneAlert(it); });
            link.Subscribe(WinMac.Connect.ConnectTopics.Call, d => host.ApplyPhoneCall(WinMac.Connect.ConnectWire.ParseCall(d)));
            host.NotchActivity = payload => link.Event("notch.activity", payload);
            host.NotchCommand = payload => link.Event("notch.cmd", payload);
            host.ModuleEvent = (topic, payload) => link.Event(topic, payload);
            link.Subscribe("clip.results", d => { if (d is { } e) host.OnClipResults(e); });   // p. ej. cerrar la Isla abierta antes de capturar   // API interna de actividades de la Isla (docs/25)
            host.HostCommand = (cmd, section) => link.Event("host.cmd", new { cmd, section });   // «Ajustes» y «Salir» del panel: las ejecuta el host (cierre ordenado)
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
        HubProbe();
        NotificationProbe();
        TrayProbe();
        int bad = lines.Count(l => l.StartsWith("[FALLO]"));
        Log(bad == 0 ? "Ningún servicio lanzó excepciones (un servicio «no disponible» es un resultado válido)." : $"{bad} servicio(s) lanzaron excepciones: es un error.");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>Bandeja: ¿encuentra Windows la ventana de bandeja de WinMac (y no la de Explorer)? ¿cuántos iconos llegan al pedir a las apps que los registren? Explorer sigue recibiéndolo todo.</summary>
    private static void TrayProbe()
    {
        Log("Iconos de apps en segundo plano (bandeja; se reenvía todo a Explorer, no se quita nada):");
        var store = new WinMac.Bar.Tray.TrayIconStore(); var gate = new object();
        using var tray = new WinMac.Bar.Win.Shell.Tray.TrayHost(_ => { });
        tray.Message += m => { lock (gate) store.Apply(m); };
        tray.Start(); Thread.Sleep(3000);
        lock (gate) Log($"  ventana de bandeja: {(tray.IsPrimary ? "las apps la encuentran" : "las apps encuentran antes la de Explorer")} · Explorer {(tray.ExplorerFound ? "encontrado" : "NO encontrado")} · {tray.MessagesSeen} mensaje(s) · {store.Visible.Count} icono(s) visibles de {store.Count}");
    }

    /// <summary>
    /// Centro de Notificaciones en ESTE equipo: permiso, si Windows envía el evento de cambios a WinMac y CUÁNTAS notificaciones y apps hay (solo con el permiso ya concedido).
    /// No pide permiso, no muestra títulos ni textos y no elimina nada.
    /// </summary>
    private static void NotificationProbe()
    {
        Log("Centro de Notificaciones (UserNotificationListener; no se pide permiso, no se muestra contenido y no se elimina nada):");
        using var src = new WinMac.Bar.Win.Shell.Notifications.WinNotificationSource(_ => { });
        var (access, why) = src.GetAccess(); Log("  permiso: " + access + (why is null ? "" : " · " + why));
        if (access != WinMac.Bar.Notifications.NotificationAccess.Allowed) { Log("  (sin permiso concedido no se lee nada; el panel ofrece pedirlo)"); return; }
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); var list = src.Read();
            Log($"  lectura: {list.Count} notificación(es) de {list.Select(n => n.GroupKey).Distinct().Count()} app(s) · {list.Count(n => n.HasIcon)} con icono · {list.Count(n => n.Title is null && n.Body is null)} sin texto · {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception e) { Log($"  [FALLO] lectura: {e.GetType().Name} (0x{e.HResult:X8}): {e.Message}"); }
        bool live = src.StartWatching(() => { }, out var whyNot); src.StopWatching();
        Log("  evento de cambios: " + (live ? "disponible (el panel se actualiza al llegar una notificación)" : "NO disponible: " + whyNot + " (el panel relee cada 4 s solo mientras está abierto)"));
    }

    /// <summary>Qué leen de verdad los monitores del panel en ESTE equipo (discos, memoria, temperaturas): para diagnosticar con datos reales, sin cambiar nada.</summary>
    private static void HubProbe()
    {
        Log("Panel de WinMac (lecturas reales de este equipo):");
        var (svc, owner) = WinMac.Bar.Win.Shell.Hub.HubServicesFactory.CreateReal();
        try
        {
            try
            {
                var s = svc.System.Read();
                Log($"  CPU {Fmt(s.CpuPercent)} · GPU {Fmt(s.GpuPercent)} · temp. CPU {Fmt(s.CpuTempC)} · temp. GPU {Fmt(s.GpuTempC)}");
                Log($"  Memoria: {(s.Memory is { } m ? $"{m.UsedBytes >> 20} MB usados de {m.TotalBytes >> 20} MB; paginación {(m.PageFileUsedBytes is { } pu ? (pu >> 20) + " MB" : "no disponible")}" : s.MemoryReason ?? "no disponible")} · USB {Fmt(s.ConnectedDevices)}");
            }
            catch (Exception e) { Log("  [FALLO] sistema: " + e.GetType().Name + ": " + e.Message); }
            try
            {
                var d = svc.Disks.ReadCatalog();
                Log($"  Discos físicos: {d.Disks.Count} · volúmenes: {d.Volumes.Count}" + (d.Reason is null ? "" : " · motivo: " + d.Reason));
                foreach (var v in d.Volumes) { var disk = d.DiskOf(v); Log($"    {v.Letter} «{v.Label}» → " + (disk is null ? "SIN disco físico asociado" : $"disco {disk.Number} {disk.Model} ({disk.Media}) estado {disk.Smart.Health ?? "n/d"}, temp {(disk.Smart.TemperatureC is { } t ? t.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " °C" : "n/d")}, desgaste {(disk.Smart.WearPercent is { } w ? w + " %" : "n/d")}, horas {disk.Smart.PowerOnHours?.ToString() ?? "n/d"}" + (disk.Smart.Incidents is { } inc ? ", errores registrados por Windows (7 días): " + inc.Summary() : ", sin errores registrados por Windows (7 días)") + (disk.Smart.Reason is null ? "" : " [" + disk.Smart.Reason + "]"))); }
            }
            catch (Exception e) { Log("  [FALLO] discos: " + e.GetType().Name + ": " + e.Message); }
            Log("  Lo que Windows dice de los discos:"); foreach (var l in WinMac.Bar.Win.Shell.Hub.HubServicesFactory.DiskDiagnostics()) Log("    " + l);
            foreach (var l in WinMac.Bar.Win.Shell.Hub.HubServicesFactory.StorageReport()) Log("  " + l);   // diagnóstico SMART/NVMe por disco (solo lectura)
        }
        finally { owner.Dispose(); }
    }

    private static string Fmt<T>(WinMac.Bar.Hub.Reading<T> r) where T : struct => r.Available ? Convert.ToString(r.Value, System.Globalization.CultureInfo.InvariantCulture)! : "no disponible (" + r.Reason + ")";

    private static int MonitorProbe()
    {
        Log("Monitores (no se cambia nada):");
        foreach (var l in BarHost.DescribeMonitors()) Log(l);
        return 0;
    }

    /// <summary>Pide a la barra en ejecución que abra (o cierre) Launchpad, como la tecla Windows. Sirve para atajos propios, el Dock o las pruebas.</summary>
    private static int Signal(string name, string what)
    {
        try { AllowSetForegroundWindow(-1); using var ev = EventWaitHandle.OpenExisting(name); ev.Set(); Log(what + ": señal enviada."); return 0; }   // quien lo pide (un atajo, el Dock) cede el primer plano a la barra
        catch (WaitHandleCannotBeOpenedException) { Log("No hay ninguna barra en ejecución."); return 2; }
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
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
