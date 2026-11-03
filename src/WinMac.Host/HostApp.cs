using System.Diagnostics;
using WinMac.Ipc;

namespace WinMac.Host.App;

/// <summary>Opciones de línea de comandos del host (las de prueba están marcadas).</summary>
internal sealed record HostOptions(string ConfigPath, string LogPath, bool NoTray, bool Fast, string? PipeName, string NotchWinSettingsPath, string BarJsonPath)
{
    public static string Arg(string[] a, string n, string def) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }
    public static HostOptions Parse(string[] a) => new(
        Arg(a, "--config", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "winmac.json")),
        Arg(a, "--log", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "host.log")),
        a.Contains("--no-tray"),          // PRUEBAS: sin icono de bandeja (runners sin escritorio)
        a.Contains("--fast"),             // PRUEBAS: tiempos de supervisión reducidos
        a.Contains("--pipe-name") ? Arg(a, "--pipe-name", "") : null,
        Arg(a, "--notchwin-settings", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin", "settings.json")),
        Arg(a, "--bar-json", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "bar.json")));   // origen de la migración única (PRUEBAS: se puede redirigir)
}

internal sealed class FileLog(string path)
{
    private readonly object _l = new();
    public void Write(string m)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {m}";
        try { Console.WriteLine(line); } catch (Exception) { }
        try { lock (_l) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.AppendAllText(path, line + Environment.NewLine); } } catch (Exception) { }
    }
}

/// <summary>
/// Host de WinMac: instancia única → canal local → supervisor → bucle de mensajes (bandeja) → cierre ordenado.
/// Sin privilegios elevados, sin puertos de red, sin tocar Explorer. Orden de cierre: quitar icono → cerrar módulos en orden inverso → liberar Job Object y canal.
/// </summary>
internal static class HostApp
{
    public const string MutexName = @"Local\WinMac.Host", QuitEventName = @"Local\WinMac.Host.Quit", ReopenEventName = @"Local\WinMac.Host.Reopen";

    public static int Run(HostOptions o)
    {
        var log = new FileLog(o.LogPath).Write;
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {   // Segunda instancia: no duplica nada, pero le pide al host vivo que reabra los módulos que estén cerrados o caídos.
            try { using var ev = EventWaitHandle.OpenExisting(ReopenEventName); ev.Set(); log("[host] ya hay una instancia de WinMac: se le pidió reabrir los módulos cerrados; esta se cierra."); }
            catch (Exception) { log("[host] ya hay una instancia de WinMac en ejecución: esta se cierra sin hacer nada."); }
            return 0;
        }
        using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
        quit.Reset();
        using var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, ReopenEventName);

        var cfg = HostConfig.Load(o.ConfigPath);
        cfg = MigrateNotchOnce(cfg, o, log);
        cfg = WinMac.Host.BarMigration.ImportOnce(cfg, o.BarJsonPath, o.ConfigPath, log);
        { var (m, changed) = BetaMigration.Apply(cfg); if (changed) { try { HostConfig.Save(o.ConfigPath, m); } catch (Exception e) { log("[host] no se pudo guardar la migración: " + e.Message); } log("[host] configuración actualizada a la versión 3 (menús de app duplicados desactivados)"); } cfg = m; }
        string baseDir = AppContext.BaseDirectory, pipe = o.PipeName ?? PipeNames.ForCurrentUser(), token = PipeNames.NewToken();
        log($"[host] iniciado (pid {Environment.ProcessId}); configuración: {o.ConfigPath}; canal: {pipe}");

        JobObject? job = null;
        try { job = new JobObject(); } catch (Exception e) { log("[host] AVISO: sin Job Object (" + e.Message + "): si el host muriera de golpe, los módulos podrían quedar abiertos."); }

        Supervisor? sup = null;
        var server = new IpcServer(new ServerOptions(pipe, token) { PeerValidator = pid => sup?.IsKnownPid(pid) ?? false });
        server.Start(); var bus = new IpcHostBus(server);
        var launcher = new SystemProcessLauncher
        {
            OnStarted = p => { try { job?.Assign(p); } catch (Exception e) { log($"[host] AVISO: no se pudo asignar {p.ProcessName} al Job Object: {e.Message}"); } },
            CloseHook = p => PostCloseToWindows(p.Id),
        };
        var policy = o.Fast
            ? new SupervisorPolicy { ReadyTimeout = TimeSpan.FromSeconds(8), HeartbeatInterval = TimeSpan.FromMilliseconds(500), BackoffStart = TimeSpan.FromMilliseconds(100), BackoffMax = TimeSpan.FromMilliseconds(500), StopGrace = TimeSpan.FromSeconds(5), StartStagger = TimeSpan.FromMilliseconds(50) }
            : new SupervisorPolicy();
        sup = new Supervisor(launcher, bus, new SystemClock(), policy, pipe, token, log);
        foreach (var (spec, enabled, available) in cfg.Resolve(baseDir)) sup.Add(spec, enabled, available);
        var config = new ConfigService(o.ConfigPath, cfg, log); var controller = new HostController(config, sup, bus, log); controller.Start();   // la Isla recibe sus ajustes al conectar

        bool quitting = false; void RequestQuit() => quitting = true;
        var uiActions = new System.Collections.Concurrent.ConcurrentQueue<Action>();   // lo que llega por el canal (otro hilo) se ejecuta en el hilo de interfaz: las ventanas de Windows no admiten otro
        controller.QuitRequested = () => uiActions.Enqueue(RequestQuit);
        Tray? tray = null; SettingsWindow? settings = null;
        string exePath = Environment.ProcessPath ?? "WinMac.exe";
        try
        {
            settings = new SettingsWindow(config, () => sup.States, controller.OpenNotchLocalSettings, exePath, log);
            var win = settings; controller.SettingsRequested = section => uiActions.Enqueue(() => win.Show(section));
            tray = new Tray(() => sup.States, RequestQuit, id => { if (sup.States.TryGetValue(id, out var st) && st == ModuleState.Disabled) config.SetModuleEnabled(id, true); else sup.Restart(id); }, settings.Show, log, showIcon: !o.NoTray);
        }
        catch (Exception e) { log("[host] sin ventana/bandeja: " + e.Message); }

        // Extensiones (docs/30): canal propio, un proceso por extensión, solo lo que permite su manifiesto
        var extCatalog = new WinMac.Host.Extensions.ExtensionCatalog(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "extensions"));
        var extHost = new WinMac.Host.Extensions.ExtensionHost(extCatalog, launcher, (topic, payload) => bus.Emit(topic, payload), log, pipe + "-ext");
        try { extHost.Start(); } catch (Exception e) { log("[extensiones] no disponibles: " + e.Message); }
        if (settings is not null)
        {
            settings.ExtensionList = () => extCatalog.List().Select(e => (e, e.Manifest is { } m ? extHost.StatusOf(m.Id) : "")).ToList();
            settings.ExtensionOps = new ExtensionOps(extCatalog, extHost, settings.Refresh);
            controller.HotkeysChanged = settings.Refresh;
            settings.ClearClipboard = () => { bus.Emit(NotchSettings.CommandTopic, new { cmd = "clear-clipboard" }); log("[host] historial del portapapeles borrado desde Ajustes"); };
        }

        sup.StartAsync().GetAwaiter().GetResult();
        log("[host] módulos lanzados: " + string.Join(", ", sup.States.Select(kv => $"{kv.Key}={kv.Value}")));

        // Bucle de mensajes (la bandeja y el cierre de sesión lo necesitan) + vigilancia de la señal de cierre (--quit).
        while (!quitting)
        {
            while (uiActions.TryDequeue(out var act)) { try { act(); } catch (Exception e) { log("[host] acción de interfaz falló: " + e.Message); } }
            while (Native.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1))
            {
                Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg);
            }
            if (reopen.WaitOne(0)) { var ids = sup.RestartStopped(); log("[host] reapertura pedida: " + (ids.Count == 0 ? "nada que reabrir" : string.Join(", ", ids))); }
            if (quit.WaitOne(0)) { log("[host] señal de cierre recibida (--quit)"); break; }
            Native.MsgWaitForMultipleObjectsEx(0, null, 100, Native.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);
        }

        log("[host] cerrando…");
        tray?.RemoveIcon();
        sup.StopAllAsync().GetAwaiter().GetResult();
        log("[host] módulos cerrados: " + string.Join(", ", sup.States.Select(kv => $"{kv.Key}={kv.Value}")));
        try { extHost.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        settings?.Dispose(); tray?.Dispose(); job?.Dispose(); server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        log("[host] fin."); return 0;
    }

    /// <summary>
    /// Migración ÚNICA y NO destructiva: si <c>winmac.json</c> aún no tiene ajustes de la Isla y NotchWin ya tenía los suyos, se copian los campos centrales (nunca claves ni rutas locales).
    /// El <c>settings.json</c> original no se toca ni se borra. Una vez guardada la sección, no se vuelve a importar.
    /// </summary>
    private static HostConfig MigrateNotchOnce(HostConfig cfg, HostOptions o, Action<string> log)
    {
        if (cfg.Notch is not null || !File.Exists(o.NotchWinSettingsPath)) return cfg;
        try
        {
            var imported = NotchSettings.ImportFromNotchWin(File.ReadAllText(o.NotchWinSettingsPath));
            if (imported is null) { log("[host] los ajustes anteriores de NotchWin no se pudieron leer: se usan los de fábrica"); return cfg; }
            var mods = new Dictionary<string, ModuleConfig>(cfg.Modules);
            var next = cfg with { Notch = imported, Modules = mods };
            HostConfig.Save(o.ConfigPath, next); log("[host] ajustes de la Isla importados de " + o.NotchWinSettingsPath + " (el archivo original no se modificó)"); return next;
        }
        catch (Exception e) { log("[host] no se pudo importar los ajustes de NotchWin: " + e.Message); return cfg; }
    }

    /// <summary>
    /// Migración ÚNICA y NO destructiva de los ajustes de la Barra: si <c>winmac.json</c> aún no tiene sección <c>bar</c> y existe <c>bar.json</c>, se copian. El archivo original no se
    /// toca ni se borra. Desde entonces la fuente de verdad es <c>winmac.json</c> (la Barra recibe sus ajustes del host; sin host sigue usando bar.json).
    /// </summary>
    /// <summary>Pide el cierre a las ventanas de nivel superior de un proceso (WM_CLOSE): cooperativo, no fuerza nada.</summary>
    private static void PostCloseToWindows(int pid)
    {
        Native.EnumWindows((h, _) => { Native.GetWindowThreadProcessId(h, out uint p); if (p == (uint)pid) Native.PostMessage(h, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero); return true; }, IntPtr.Zero);
    }

    /// <summary>Pide al host en ejecución que se cierre normalmente.</summary>
    public static int SignalQuit()
    {
        try { using var ev = EventWaitHandle.OpenExisting(QuitEventName); ev.Set(); Console.WriteLine("Señal de cierre enviada a WinMac."); return 0; }
        catch (WaitHandleCannotBeOpenedException) { Console.WriteLine("WinMac no está en ejecución."); return 0; }
    }
}

/// <summary>Settings → Extensions: every change is applied at once (start/stop the process) and the page redraws with the new state.</summary>
internal sealed class ExtensionOps(WinMac.Host.Extensions.ExtensionCatalog catalog, WinMac.Host.Extensions.ExtensionHost host, Action refresh) : WinMac.Host.IExtensionOps
{
    public void SetEnabled(string id, bool on) { catalog.SetEnabled(id, on); host.Sync(); refresh(); _ = Task.Delay(1500).ContinueWith(_ => refresh()); }
    public void Uninstall(string id) { catalog.SetEnabled(id, false); host.Sync(); Thread.Sleep(400); catalog.Uninstall(id); refresh(); }
    public string? Install(string folder) { var (_, err) = catalog.Install(folder); refresh(); return err; }
}

