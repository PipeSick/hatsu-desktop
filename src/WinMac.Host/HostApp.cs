using System.Diagnostics;
using WinMac.Ipc;

namespace WinMac.Host.App;

/// <summary>Opciones de línea de comandos del host (las de prueba están marcadas).</summary>
internal sealed record HostOptions(string ConfigPath, string LogPath, bool NoTray, bool Fast, string? PipeName, string NotchWinSettingsPath, string BarJsonPath, bool Replace = false, bool Background = false, bool NoOnboarding = false, string? OnboardingAuto = null)
{
    public static string Arg(string[] a, string n, string def) { int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }
    public static HostOptions Parse(string[] a) => new(
        Arg(a, "--config", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "winmac.json")),
        Arg(a, "--log", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "host.log")),
        a.Contains("--no-tray"),          // PRUEBAS: sin icono de bandeja (runners sin escritorio)
        a.Contains("--fast"),             // PRUEBAS: tiempos de supervisión reducidos
        a.Contains("--pipe-name") ? Arg(a, "--pipe-name", "") : null,
        Arg(a, "--notchwin-settings", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchWin", "settings.json")),
        Arg(a, "--bar-json", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "bar.json")),
        a.Contains("--replace-running"), a.Contains("--background"),
        a.Contains("--no-onboarding"),                                                          // PRUEBAS: sin asistente de primera ejecución
        a.Contains("--onboarding-auto") ? Arg(a, "--onboarding-auto", "skip") : null);          // PRUEBAS: aplica el asistente SIN ventana (skip = valores seguros)   // Hatsu Desktop: cierra la instancia en ejecución (también una de WinMac anterior: mismos nombres de instancia única) y continúa con esta   // origen de la migración única (PRUEBAS: se puede redirigir)
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
        bool cfgExisted = File.Exists(o.ConfigPath);   // ANTES de cualquier migración que lo cree: distingue instalación nueva de usuario existente
        bool legacyData = File.Exists(o.BarJsonPath) || File.Exists(o.NotchWinSettingsPath) || File.Exists(WinMac.Dock.Win.Shell.DockDefaults.ConfigPath);
        using var mutex = new Mutex(true, MutexName, out bool first);
        bool replace = o.Replace;
        if (!first && !replace)
        {   // Convivencia (MSI / portable / WinMac antiguo): si el host vivo es de OTRA carpeta, nunca se toca por cuenta propia. En uso interactivo se PREGUNTA; en segundo plano (inicio automático) esta copia se cierra.
            string self = Environment.ProcessPath ?? "";
            var others = OtherHostPaths(); var kind = HostCoexistence.Classify(self, others);
            if (HostCoexistence.ShouldAsk(kind, o.Background, o.NoTray))
            {
                string other = HostCoexistence.OtherPath(self, others) ?? "otra carpeta";
                replace = WinMac.Dock.Win.Rendering.DarkDialog.Ask("Ya hay otra copia en ejecución", $"Hatsu Desktop (o WinMac) ya está abierto desde:\n{other}\n\nSolo puede haber una copia a la vez. ¿Cerrarla y usar esta?\n\nNo se borra nada ni se cambia el inicio automático.", "Cerrar la otra y usar esta", false, log);
                log(replace ? "[host] el usuario aceptó reemplazar la copia en ejecución (" + other + ")" : "[host] el usuario prefirió dejar la otra copia en ejecución: esta se cierra.");
            }
            else if (kind == CoexistenceKind.OtherInstall) log("[host] hay otra copia de Hatsu Desktop/WinMac en ejecución (" + HostCoexistence.OtherPath(self, others) + "): esta se cierra sin tocarla.");
        }
        if (!first && replace)
        {   // Adopción explícita de esta instalación (--replace-running): se pide el cierre ordenado a la instancia viva —los nombres de instancia única y de cierre son los mismos que usaba WinMac,
            // así que también sirve para una versión anterior— y se espera a que libere la exclusión mutua. Si no la libera, esta se cierra sin tocar nada (nunca se mata un proceso).
            log("[host] --replace-running: se pide el cierre de la instancia en ejecución…");
            try { using var q = EventWaitHandle.OpenExisting(QuitEventName); q.Set(); } catch (Exception e) { log("[host] no se pudo avisar a la instancia en ejecución: " + e.Message); }
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(20)); } catch (AbandonedMutexException) { first = true; }
            log(first ? "[host] la instancia anterior se cerró: esta continúa." : "[host] la instancia anterior no se cerró a tiempo: esta se cierra sin cambios.");
        }

        if (!first)
        {   // Segunda instancia: no duplica nada, pero le pide al host vivo que reabra los módulos que estén cerrados o caídos.
            try { using var ev = EventWaitHandle.OpenExisting(ReopenEventName); ev.Set(); log("[host] ya hay una instancia de Hatsu Desktop: se le pidió reabrir los módulos cerrados; esta se cierra."); }
            catch (Exception) { log("[host] ya hay una instancia de Hatsu Desktop en ejecución: esta se cierra sin hacer nada."); }
            return 0;
        }
        using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, QuitEventName);
        quit.Reset();
        using var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, ReopenEventName);

        var cfg = HostConfig.Load(o.ConfigPath);
        cfg = MigrateNotchOnce(cfg, o, log);
        cfg = WinMac.Host.BarMigration.ImportOnce(cfg, o.BarJsonPath, o.ConfigPath, log);
        { var (m, changed) = BetaMigration.Apply(cfg); if (changed) { try { HostConfig.Save(o.ConfigPath, m); } catch (Exception e) { log("[host] no se pudo guardar la migración: " + e.Message); } log("[host] configuración actualizada a la versión 3 (menús de app duplicados desactivados)"); } cfg = m; }
        var (onbAction, onbRecord) = o.NoOnboarding ? (OnboardingAction.None, cfg.Onboarding ?? new OnboardingRecord { Status = OnboardingStatus.Existing }) : OnboardingDecision.Decide(cfg, cfgExisted, legacyData);
        if (!o.NoOnboarding && cfg.Onboarding is null)
        {   // primera vez que se decide: usuario existente → se anota y no se toca nada más; instalación nueva → ningún módulo arranca hasta que el usuario decida (el escritorio no cambia antes)
            cfg = onbAction is OnboardingAction.None or OnboardingAction.ShowMigrationWelcome ? cfg with { Onboarding = onbRecord } : OnboardingDecision.GateFresh(cfg, onbRecord);
            try { HostConfig.Save(o.ConfigPath, cfg); } catch (Exception e) { log("[host] no se pudo guardar el estado del asistente: " + e.Message); }
            log(onbAction == OnboardingAction.ShowMigrationWelcome ? $"[host] configuración existente ({onbRecord.Origin}): se conserva y los módulos arrancan como siempre; se ofrece una bienvenida de migración (una vez)." : "[host] instalación nueva: se muestra el asistente; los módulos esperan a que elijas.");
        }
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
        // ── Asistente de primera ejecución (Fase 3): ventana propia, aplicación transaccional por el dueño único de la configuración
        OnboardingWindow? wizard = null;
        string exePath = Environment.ProcessPath ?? "HatsuDesktop.exe";
        var ports = new HostOnboardingPorts(config, sup, exePath, log); var applier = new OnboardingApplier(ports, log);
        void OpenWizard(OnboardingMode mode)
        {
            if (wizard is { IsOpen: true }) { wizard.Show(); return; }
            var initial = mode is OnboardingMode.Rerun or OnboardingMode.Migration ? OnboardingRules.FromCurrent(config.Current, ports.LoadDock(), ports.AutostartOn) : config.Current.Onboarding?.Pending ?? OnboardingChoices.Safe;
            var ctl = new OnboardingController(mode, initial); ApplyResult? last = null; bool skipped = false;
            var w = new OnboardingWindow(ctl, log); wizard = w;
            void RunApply(bool skip)
            {
                skipped = skip; if (skip) { ctl.Reset(); } ctl.BeginApply(); w.Invalidate();
                var choices = skip ? OnboardingChoices.Safe : ctl.Choices;
                _ = Task.Run(async () =>
                {
                    ApplyResult r; try { r = await applier.ApplyAsync(choices, skip).ConfigureAwait(false); }
                    catch (Exception e) { log("[asistente] fallo inesperado: " + e); r = new ApplyResult([new("unexpected", "Aplicar", ApplyOutcome.Failed, e.Message)], OnboardingStatus.InProgress, false); }
                    last = r; log($"[asistente] resultado: {(r.AllOk ? "todo aplicado" : r.EssentialFailed ? "fallo esencial" + (r.RolledBack ? " (estado anterior restaurado)" : "") : "con avisos")}; " + string.Join("; ", r.Items.Where(i => i.Outcome != ApplyOutcome.Ok).Select(i => i.Label + ": " + i.Detail)));
                    w.Post(() => ctl.ShowResult(r));
                });
            }
            w.OnCommand = cmd =>
            {
                switch (cmd)
                {
                    case OnboardingCommand.Apply: case OnboardingCommand.Retry: RunApply(false); break;
                    case OnboardingCommand.Skip: RunApply(true); break;
                    case OnboardingCommand.Accept: if (last is not null) { var err = applier.AcceptWarnings(last, skipped); if (err is not null) log("[asistente] no se pudo anotar la aceptación: " + err); } w.Close(); break;
                    case OnboardingCommand.Finish: w.Close(); break;
                    case OnboardingCommand.Cancel: w.Close(); break;
                    case OnboardingCommand.Keep: log("[asistente] el usuario conserva su configuración anterior"); w.Close(); break;   // Closed marca la bienvenida como mostrada   // cerrar sin aplicar: lo ya aplicado se queda; si no se aplicó nada, el asistente vuelve en el próximo arranque (instalación nueva)
                }
            };
            w.Closed = () =>
            {
                log("[asistente] ventana cerrada"); wizard = null;
                if (mode == OnboardingMode.Migration)   // «conservar», cerrar o completar la revisión: la bienvenida no se repite (nunca se insiste)
                    config.Update(c => c with { Onboarding = (c.Onboarding ?? new OnboardingRecord { Status = OnboardingStatus.Existing }) with { WelcomeShownUtc = c.Onboarding?.WelcomeShownUtc ?? DateTime.UtcNow } });
            };
            w.Show();
        }
        Tray? tray = null; SettingsWindow? settings = null;
        if (Autostart.Heal(exePath) is { } healed) log("[host] el inicio automático apuntaba a una carpeta que ya no existe: ahora apunta a esta instalación.");
        var taskbar = new TaskbarHider(exePath, log);   // WM-034
        try { taskbar.RecoverStale(); } catch (Exception e) { log("[barra de tareas] no se pudo revisar la sesión anterior: " + e.Message); }
        config.Changed += (old, now) => { if (old.HideTaskbar && !now.HideTaskbar) uiActions.Enqueue(taskbar.RestoreNow); };   // al desactivar: al instante, en el hilo de interfaz
        try
        {
            settings = new SettingsWindow(config, () => sup.States, controller.OpenNotchLocalSettings, exePath, log);
            settings.RunOnboarding = () => uiActions.Enqueue(() => OpenWizard(OnboardingMode.Rerun));
            settings.ModuleFailures = () => sup.Failures;
            settings.RetryModule = id => { bool ok = sup.Restart(id); log(ok ? $"[host] reintento manual de {id} pedido desde Ajustes" : $"[host] reintento de {id}: nada que reintentar ahora"); return ok; };
            settings.RunDiagnostics = () => _ = Task.Run(() => { try { var f = InstallDiagnose.Run(o, null, open: true, print: false); log("[host] informe de diagnóstico: " + (f ?? "no se pudo guardar")); } catch (Exception e) { log("[host] informe de diagnóstico falló: " + e.Message); } });
            var win = settings; controller.SettingsRequested = section => uiActions.Enqueue(() => win.Show(section));
            tray = new Tray(() => sup.States, RequestQuit, id => { if (sup.States.TryGetValue(id, out var st) && st == ModuleState.Disabled) config.SetModuleEnabled(id, true); else sup.Restart(id); }, settings.Show, log, showIcon: !o.NoTray);
            tray.TaskbarCreated = () => taskbar.OnTaskbarCreated(config.Current.HideTaskbar);
            tray.Failures = () => sup.Failures;
            settings.TaskbarStatus = () => taskbar.Status;
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

        if (onbAction is OnboardingAction.ShowWizard or OnboardingAction.ResumeWizard && o.OnboardingAuto is null && !o.NoTray) uiActions.Enqueue(() => OpenWizard(onbAction == OnboardingAction.ResumeWizard ? OnboardingMode.Resume : OnboardingMode.Fresh));
        if (onbAction == OnboardingAction.ShowMigrationWelcome && o.OnboardingAuto is null && !o.NoTray && !o.Background) uiActions.Enqueue(() => OpenWizard(OnboardingMode.Migration));   // no en el inicio automático: no se abre una ventana al iniciar sesión
        if (onbAction is OnboardingAction.ShowWizard or OnboardingAction.ResumeWizard && o.OnboardingAuto is { } auto)
        {   // PRUEBAS: el mismo camino de aplicación, sin ventana (valores seguros). Verifica de verdad que el arranque queda gobernado por el asistente.
            _ = Task.Run(async () =>
            {
                var r = await applier.ApplyAsync(OnboardingChoices.Safe, skipped: true).ConfigureAwait(false);
                log($"[asistente] (automático: {auto}) " + (r.AllOk ? "completado con valores seguros" : "con avisos/fallos: " + string.Join("; ", r.Items.Where(i => i.Outcome != ApplyOutcome.Ok).Select(i => i.Label + ": " + i.Detail))));
            });
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
            taskbar.Tick(config.Current.HideTaskbar, DateTime.UtcNow);
            Native.MsgWaitForMultipleObjectsEx(0, null, 100, Native.QS_ALLINPUT, 0x4 /* MWMO_INPUTAVAILABLE */);
        }

        log("[host] cerrando…");
        try { taskbar.RestoreNow(); } catch (Exception e) { log("[barra de tareas] no se pudo restaurar al salir: " + e.Message + " (el guardián o «HatsuDesktop.exe --restore-taskbar» la devuelven)"); }   // lo primero: la barra vuelve al instante
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

    /// <summary>Rutas de los ejecutables de OTROS procesos HatsuDesktop/WinMac de este usuario (null si no se pudo leer). Solo lectura.</summary>
    private static List<string?> OtherHostPaths()
    {
        var list = new List<string?>(); int me = Environment.ProcessId;
        foreach (var name in new[] { "HatsuDesktop", "WinMac" })
            foreach (var p in Process.GetProcessesByName(name))
                using (p) { if (p.Id == me) continue; try { list.Add(p.MainModule?.FileName); } catch (Exception) { list.Add(null); } }
        return list;
    }

    /// <summary>Pide al host en ejecución que se cierre normalmente.</summary>
    public static int SignalQuit()
    {
        try { using var ev = EventWaitHandle.OpenExisting(QuitEventName); ev.Set(); Console.WriteLine("Señal de cierre enviada a Hatsu Desktop."); return 0; }
        catch (WaitHandleCannotBeOpenedException) { Console.WriteLine("Hatsu Desktop no está en ejecución."); return 0; }
    }
}

/// <summary>Settings → Extensions: every change is applied at once (start/stop the process) and the page redraws with the new state.</summary>
internal sealed class ExtensionOps(WinMac.Host.Extensions.ExtensionCatalog catalog, WinMac.Host.Extensions.ExtensionHost host, Action refresh) : WinMac.Host.IExtensionOps
{
    public void SetEnabled(string id, bool on) { catalog.SetEnabled(id, on); host.Sync(); refresh(); _ = Task.Delay(1500).ContinueWith(_ => refresh()); }
    public void Uninstall(string id) { catalog.SetEnabled(id, false); host.Sync(); Thread.Sleep(400); catalog.Uninstall(id); refresh(); }
    public string? Install(string folder) { var (_, err) = catalog.Install(folder); refresh(); return err; }
}


/// <summary>El asistente habla con el host solo a través de estas piezas ya existentes: ConfigService (dueño único de winmac.json), Supervisor, Autostart y dock.json. No hay un segundo gestor de módulos.</summary>
internal sealed class HostOnboardingPorts(ConfigService config, Supervisor sup, string exePath, Action<string> log) : IOnboardingPorts
{
    public HostConfig Config => config.Current;
    public string? Update(Func<HostConfig, HostConfig> change) { config.Update(change); return config.LastSaveError; }
    public WinMac.Dock.DockConfig? LoadDock()
    {
        try { return WinMac.Dock.DockSettings.Sanitize(WinMac.Dock.DockConfig.LoadOrDefault(WinMac.Dock.Win.Shell.DockDefaults.ConfigPath, WinMac.Dock.Win.Shell.DockDefaults.Create)); }
        catch (Exception e) { log("[asistente] dock.json no se pudo leer: " + e.Message); return null; }
    }
    public bool SaveDock(WinMac.Dock.DockConfig d) => d.TrySave(WinMac.Dock.Win.Shell.DockDefaults.ConfigPath);
    public bool AutostartOn => Autostart.Current() is not null;
    public void SetAutostart(bool on) { if (on) Autostart.Install(exePath); else Autostart.Remove(); }
    public IReadOnlyDictionary<string, ModuleState> ModuleStates => sup.States;
    public IReadOnlyDictionary<string, ModuleFailure> ModuleFailures => sup.Failures;
    public Task DelayAsync(TimeSpan t) => Task.Delay(t);
}
