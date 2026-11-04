namespace WinMac.Host;

/// <summary>
/// Supervisa módulos como procesos hijos: arranque en orden, latido, reinicio con espera creciente, cortacircuito y cierre ordenado.
/// Reglas aprobadas: un módulo que cae NO afecta a los demás; un cierre voluntario (<c>bye</c> + código 0) no se reinicia; un módulo sin ejecutable queda en
/// <see cref="ModuleState.Failed"/> y los demás siguen; al cerrar WinMac se cierran los módulos en orden inverso.
/// </summary>
public sealed class Supervisor
{
    private sealed class Runner(Supervisor owner, ModuleSpec spec)
    {
        public readonly ModuleSpec Spec = spec; public ModuleState State = ModuleState.Stopped;
        public IModuleProcess? Proc; public ModuleFailure? Failure; public volatile bool ReadySeen, ByeSeen; public DateTime LastPong;
        public CancellationTokenSource Stop = new(); public Task? Loop; public readonly List<DateTime> Crashes = [];
        public readonly TaskCompletionSource FirstLaunch = new(TaskCreationOptions.RunContinuationsAsynchronously);   // el primer intento de lanzamiento ya ocurrió (con éxito o no)

        public void Set(ModuleState s) { if (State == s) return; State = s; owner.Log($"[{Spec.Id}] {s}"); owner.StateChanged?.Invoke(Spec.Id, s); }
    }

    private readonly IProcessLauncher _launcher; private readonly IClock _clock; private readonly SupervisorPolicy _p; private readonly string _pipe, _token;
    private readonly List<Runner> _runners = []; private readonly HashSet<int> _pids = []; private readonly Action<string> _log;

    public Supervisor(IProcessLauncher launcher, IHostBus bus, IClock clock, SupervisorPolicy policy, string pipeName, string token, Action<string>? log = null)
    {
        _launcher = launcher; _clock = clock; _p = policy; _pipe = pipeName; _token = token; _log = log ?? (_ => { });
        bus.Ready += id => With(id, r => { r.ReadySeen = true; r.LastPong = _clock.UtcNow; r.Failure = null; r.Set(ModuleState.Running); });
        bus.Pong += id => With(id, r => r.LastPong = _clock.UtcNow);
        bus.Bye += id => With(id, r => r.ByeSeen = true);
        _bus = bus;
    }

    private readonly IHostBus _bus;
    public event Action<string, ModuleState>? StateChanged;
    private void Log(string m) => _log(m);
    private void With(string id, Action<Runner> a) { Runner? r; lock (_runners) r = _runners.FirstOrDefault(x => x.Spec.Id == id); if (r is not null) a(r); }

    /// <summary>Alta de un módulo, en el orden de arranque. Un módulo deshabilitado queda en <see cref="ModuleState.Disabled"/> y nunca se lanza.</summary>
    public void Add(ModuleSpec spec, bool enabled, bool available = true)
    {
        var r = new Runner(this, spec); if (!available) r.State = ModuleState.NotInstalled; else if (!enabled) r.State = ModuleState.Disabled;
        lock (_runners) _runners.Add(r);
    }

    /// <summary>Última causa conocida de que un módulo no esté en marcha (solo los que fallaron; un módulo desactivado NO aparece aquí).</summary>
    public IReadOnlyDictionary<string, ModuleFailure> Failures { get { lock (_runners) return _runners.Where(r => r.Failure is not null && r.State is not (ModuleState.Disabled or ModuleState.Running)).ToDictionary(r => r.Spec.Id, r => r.Failure!); } }

    public IReadOnlyDictionary<string, ModuleState> States { get { lock (_runners) return _runners.ToDictionary(r => r.Spec.Id, r => r.State); } }
    /// <summary>¿Es este PID el de un módulo lanzado por nosotros? (validación del cliente del canal en Windows)</summary>
    public bool IsKnownPid(uint pid) { lock (_pids) return _pids.Contains((int)pid); }

    public async Task StartAsync()
    {
        List<Runner> list; lock (_runners) list = [.. _runners];
        foreach (var r in list.Where(r => r.State is not (ModuleState.Disabled or ModuleState.NotInstalled)))
        {
            if (_shuttingDown) break;   // si el host se cierra durante el arranque escalonado, no se lanza lo que falta
            r.Loop = Task.Run(() => RunAsync(r));
            await r.FirstLaunch.Task.ConfigureAwait(false);                                   // el orden de arranque es real: el siguiente no se lanza hasta que este ya existe
            await _clock.Delay(_p.StartStagger, CancellationToken.None).ConfigureAwait(false);   // el orden de aparición importa (la isla debe quedar encima)
        }
    }

    private volatile bool _shuttingDown;
    private readonly SemaphoreSlim _toggle = new(1, 1);   // activar/desactivar en vivo de uno en uno

    /// <summary>
    /// Activa o desactiva UN módulo en vivo, sin tocar a los demás ni al host. Desactivar lo cierra por su camino normal (shutdown → cierre cortés → último recurso) y lo deja en
    /// <see cref="ModuleState.Disabled"/>; activar lo lanza de inmediato con el historial de caídas limpio. No hace nada con módulos no instalados ni durante el cierre del host.
    /// </summary>
    public async Task<bool> SetEnabledAsync(string id, bool enabled)
    {
        Runner? r; lock (_runners) r = _runners.FirstOrDefault(x => x.Spec.Id == id);
        if (r is null || _shuttingDown || r.State == ModuleState.NotInstalled) return false;
        await _toggle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_shuttingDown) return false;
            if (!enabled)
            {
                if (r.State == ModuleState.Disabled) return false;
                Log($"[{id}] desactivado por el usuario");
                await StopAsync(r).ConfigureAwait(false); r.Set(ModuleState.Disabled); return true;
            }
            if (r.State != ModuleState.Disabled) return false;
            Log($"[{id}] activado por el usuario");
            r.Stop = new CancellationTokenSource(); r.Crashes.Clear(); r.Proc = null; r.Set(ModuleState.Stopped);
            r.Loop = Task.Run(() => RunAsync(r)); return true;
        }
        finally { _toggle.Release(); }
    }

    /// <summary>Vuelve a lanzar un módulo que está detenido (cierre voluntario) o en <see cref="ModuleState.Failed"/> (cortacircuito o sin ejecutable), a petición del usuario.
    /// Reinicia el historial de caídas. No hace nada si el módulo está desactivado, ya está en marcha, o el host se está cerrando.</summary>
    public bool Restart(string id)
    {
        Runner? r; lock (_runners) r = _runners.FirstOrDefault(x => x.Spec.Id == id);
        if (r is null || _shuttingDown || r.State is not (ModuleState.Stopped or ModuleState.Failed)) return false;
        if (r.Loop is { IsCompleted: false }) return false;   // el bucle anterior aún no terminó: se ignora (el usuario puede repetir)
        r.Crashes.Clear(); r.Proc = null; Log($"[{id}] apertura manual");
        r.Loop = Task.Run(() => RunAsync(r));
        return true;
    }

    /// <summary>Reabre todos los módulos detenidos o caídos (en el orden de arranque). Devuelve los ids lanzados.</summary>
    public IReadOnlyList<string> RestartStopped()
    {
        List<string> ids; lock (_runners) ids = _runners.Select(r => r.Spec.Id).ToList();
        return ids.Where(Restart).ToList();
    }

    /// <summary>Cierre ordenado en orden inverso al arranque. Termina cuando todos los módulos han salido (o se han forzado como último recurso).</summary>
    public async Task StopAllAsync()
    {
        _shuttingDown = true;
        List<Runner> list; lock (_runners) list = [.. _runners]; list.Reverse();
        foreach (var r in list) await StopAsync(r).ConfigureAwait(false);
    }

    private async Task StopAsync(Runner r)
    {
        if (r.State is ModuleState.Disabled or ModuleState.NotInstalled) return;
        r.Stop.Cancel();
        var proc = r.Proc;
        if (proc is not null && !proc.Exited.IsCompleted)
        {
            r.Set(ModuleState.Stopping); _bus.Send(r.Spec.Id, Ipc.MsgType.Shutdown);
            if (!await ExitsWithinAsync(proc, _p.StopGrace).ConfigureAwait(false))
            {
                Log($"[{r.Spec.Id}] no cerró tras «shutdown»: se pide el cierre a sus ventanas");
                proc.RequestClose();
                if (!await ExitsWithinAsync(proc, _p.CloseGrace).ConfigureAwait(false)) { Log($"[{r.Spec.Id}] sigue vivo: se termina (último recurso, es nuestro propio proceso)"); proc.Kill(); await ExitsWithinAsync(proc, TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            }
        }
        if (r.Loop is not null) { try { await r.Loop.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch (Exception) { } }
        if (r.State != ModuleState.Failed) r.Set(ModuleState.Stopped);
    }

    private async Task<bool> ExitsWithinAsync(IModuleProcess p, TimeSpan t)
    {
        using var cts = new CancellationTokenSource();
        var delay = _clock.Delay(t, cts.Token);
        var done = await Task.WhenAny(p.Exited, delay).ConfigureAwait(false);
        cts.Cancel(); try { await delay.ConfigureAwait(false); } catch (OperationCanceledException) { }
        return done == p.Exited;
    }

    private enum Outcome { Exit, Hung, NeverReady, Stop }

    private async Task RunAsync(Runner r)
    {
        int step = 0; var stop = r.Stop;   // este bucle responde SOLO a su propia señal de parada (activar de nuevo crea otra)
        while (!stop.IsCancellationRequested)
        {
            r.ReadySeen = false; r.ByeSeen = false;
            IModuleProcess? proc = null; Exception? launchError = null;
            try { proc = _launcher.Start(r.Spec, _pipe, _token); r.Proc = proc; lock (_pids) _pids.Add(proc.Id); }
            catch (Exception ex) { launchError = ex; }
            if (launchError is not null)
            {   // WM-036: el motivo real (código de Windows); un bloqueo de seguridad o un fallo pasajero se reintenta como una caída, un ejecutable inexistente no
                Log($"[{r.Spec.Id}] no se pudo lanzar: {LaunchDiagnostics.Explain(launchError)}");
                r.Failure = LaunchDiagnostics.Classify(launchError, r.Crashes.Count + 1);
                r.FirstLaunch.TrySetResult();
                if (!LaunchDiagnostics.Retryable(launchError)) { r.Set(ModuleState.Failed); return; }   // sin ejecutable: no se reintenta; los demás siguen
                var t0 = _clock.UtcNow; r.Crashes.Add(t0); r.Crashes.RemoveAll(t => t0 - t > _p.CrashWindow);
                if (r.Crashes.Count >= _p.MaxCrashes) { Log($"[{r.Spec.Id}] {r.Crashes.Count} intentos fallidos en {_p.CrashWindow.TotalSeconds:0}s: no se reintenta (ábrelo desde la bandeja de Hatsu Desktop)"); r.Set(ModuleState.Failed); return; }
                var w0 = TimeSpan.FromMilliseconds(Math.Min(_p.BackoffMax.TotalMilliseconds, _p.BackoffStart.TotalMilliseconds * Math.Pow(2, step++)));
                r.Set(ModuleState.Crashed); Log($"[{r.Spec.Id}] nuevo intento en {w0.TotalSeconds:0.##}s");
                try { await _clock.Delay(w0, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }
            r.FirstLaunch.TrySetResult();
            var started = proc!;
            r.Set(ModuleState.Starting); var startedAt = _clock.UtcNow; r.LastPong = startedAt;

            var outcome = await WatchAsync(r, started, startedAt, stop).ConfigureAwait(false);
            if (outcome == Outcome.Stop) return;
            int code = 0;
            if (outcome == Outcome.Exit) { code = await started.Exited.ConfigureAwait(false); lock (_pids) _pids.Remove(started.Id); }
            if (outcome == Outcome.Exit && r.ByeSeen && code == 0) { Log($"[{r.Spec.Id}] cierre voluntario: no se reinicia"); r.Set(ModuleState.Stopped); return; }

            r.Failure = outcome == Outcome.Exit ? LaunchDiagnostics.ClassifyExit(code, r.Crashes.Count + 1) : new ModuleFailure(FailureKind.NotResponding, outcome == Outcome.Hung ? "sin latido" : "no llegó a estar listo", null, null, r.Crashes.Count + 1);
            if (outcome is Outcome.Hung or Outcome.NeverReady) { Log($"[{r.Spec.Id}] {(outcome == Outcome.Hung ? "sin latido" : "no llegó a estar listo")}: se reinicia"); started.Kill(); await ExitsWithinAsync(started, TimeSpan.FromSeconds(2)).ConfigureAwait(false); lock (_pids) _pids.Remove(started.Id); }
            else Log($"[{r.Spec.Id}] terminó de forma inesperada ({LaunchDiagnostics.ExitText(code)})");

            var now = _clock.UtcNow; r.Crashes.Add(now); r.Crashes.RemoveAll(t => now - t > _p.CrashWindow);
            if (r.Crashes.Count >= _p.MaxCrashes) { Log($"[{r.Spec.Id}] {r.Crashes.Count} caídas en {_p.CrashWindow.TotalSeconds:0}s: cortacircuito abierto, no se reintenta"); r.Set(ModuleState.Failed); return; }
            if (now - startedAt >= _p.StableAfter) step = 0;
            var wait = TimeSpan.FromMilliseconds(Math.Min(_p.BackoffMax.TotalMilliseconds, _p.BackoffStart.TotalMilliseconds * Math.Pow(2, step++)));
            r.Set(ModuleState.Crashed); Log($"[{r.Spec.Id}] reinicio en {wait.TotalSeconds:0.##}s");
            try { await _clock.Delay(wait, stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task<Outcome> WatchAsync(Runner r, IModuleProcess proc, DateTime startedAt, CancellationTokenSource stop)
    {
        while (true)
        {
            using var tick = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            var delay = _clock.Delay(_p.HeartbeatInterval, tick.Token);
            var stopTask = Task.Delay(Timeout.Infinite, stop.Token);
            var done = await Task.WhenAny(proc.Exited, delay, stopTask).ConfigureAwait(false);
            tick.Cancel(); try { await delay.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (done == proc.Exited) return Outcome.Exit;
            if (stop.IsCancellationRequested) return Outcome.Stop;
            var now = _clock.UtcNow;
            if (!r.ReadySeen) { if (now - startedAt > _p.ReadyTimeout) return Outcome.NeverReady; continue; }
            _bus.Send(r.Spec.Id, Ipc.MsgType.Ping);
            if (now - r.LastPong > _p.HeartbeatInterval * _p.MissedBeats) return Outcome.Hung;
        }
    }
}
