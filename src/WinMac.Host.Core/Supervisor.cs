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
        public IModuleProcess? Proc; public volatile bool ReadySeen, ByeSeen; public DateTime LastPong;
        public readonly CancellationTokenSource Stop = new(); public Task? Loop; public readonly List<DateTime> Crashes = [];
        public readonly TaskCompletionSource FirstLaunch = new(TaskCreationOptions.RunContinuationsAsynchronously);   // el primer intento de lanzamiento ya ocurrió (con éxito o no)

        public void Set(ModuleState s) { if (State == s) return; State = s; owner.Log($"[{Spec.Id}] {s}"); owner.StateChanged?.Invoke(Spec.Id, s); }
    }

    private readonly IProcessLauncher _launcher; private readonly IClock _clock; private readonly SupervisorPolicy _p; private readonly string _pipe, _token;
    private readonly List<Runner> _runners = []; private readonly HashSet<int> _pids = []; private readonly Action<string> _log;

    public Supervisor(IProcessLauncher launcher, IHostBus bus, IClock clock, SupervisorPolicy policy, string pipeName, string token, Action<string>? log = null)
    {
        _launcher = launcher; _clock = clock; _p = policy; _pipe = pipeName; _token = token; _log = log ?? (_ => { });
        bus.Ready += id => With(id, r => { r.ReadySeen = true; r.LastPong = _clock.UtcNow; r.Set(ModuleState.Running); });
        bus.Pong += id => With(id, r => r.LastPong = _clock.UtcNow);
        bus.Bye += id => With(id, r => r.ByeSeen = true);
        _bus = bus;
    }

    private readonly IHostBus _bus;
    public event Action<string, ModuleState>? StateChanged;
    private void Log(string m) => _log(m);
    private void With(string id, Action<Runner> a) { Runner? r; lock (_runners) r = _runners.FirstOrDefault(x => x.Spec.Id == id); if (r is not null) a(r); }

    /// <summary>Alta de un módulo, en el orden de arranque. Un módulo deshabilitado queda en <see cref="ModuleState.Disabled"/> y nunca se lanza.</summary>
    public void Add(ModuleSpec spec, bool enabled)
    {
        var r = new Runner(this, spec); if (!enabled) r.State = ModuleState.Disabled;
        lock (_runners) _runners.Add(r);
    }

    public IReadOnlyDictionary<string, ModuleState> States { get { lock (_runners) return _runners.ToDictionary(r => r.Spec.Id, r => r.State); } }
    /// <summary>¿Es este PID el de un módulo lanzado por nosotros? (validación del cliente del canal en Windows)</summary>
    public bool IsKnownPid(uint pid) { lock (_pids) return _pids.Contains((int)pid); }

    public async Task StartAsync()
    {
        List<Runner> list; lock (_runners) list = [.. _runners];
        foreach (var r in list.Where(r => r.State != ModuleState.Disabled))
        {
            r.Loop = Task.Run(() => RunAsync(r));
            await r.FirstLaunch.Task.ConfigureAwait(false);                                   // el orden de arranque es real: el siguiente no se lanza hasta que este ya existe
            await _clock.Delay(_p.StartStagger, CancellationToken.None).ConfigureAwait(false);   // el orden de aparición importa (la isla debe quedar encima)
        }
    }

    private volatile bool _shuttingDown;

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
        if (r.State is ModuleState.Disabled) return;
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
        int step = 0;
        while (!r.Stop.IsCancellationRequested)
        {
            r.ReadySeen = false; r.ByeSeen = false;
            IModuleProcess proc;
            try { proc = _launcher.Start(r.Spec, _pipe, _token); r.Proc = proc; lock (_pids) _pids.Add(proc.Id); }
            catch (Exception ex) { Log($"[{r.Spec.Id}] no se pudo lanzar: {ex.Message}"); r.Set(ModuleState.Failed); r.FirstLaunch.TrySetResult(); return; }   // sin ejecutable: no se reintenta; los demás siguen
            r.FirstLaunch.TrySetResult();
            r.Set(ModuleState.Starting); var startedAt = _clock.UtcNow; r.LastPong = startedAt;

            var outcome = await WatchAsync(r, proc, startedAt).ConfigureAwait(false);
            if (outcome == Outcome.Stop) return;
            int code = 0;
            if (outcome == Outcome.Exit) { code = await proc.Exited.ConfigureAwait(false); lock (_pids) _pids.Remove(proc.Id); }
            if (outcome == Outcome.Exit && r.ByeSeen && code == 0) { Log($"[{r.Spec.Id}] cierre voluntario: no se reinicia"); r.Set(ModuleState.Stopped); return; }

            if (outcome is Outcome.Hung or Outcome.NeverReady) { Log($"[{r.Spec.Id}] {(outcome == Outcome.Hung ? "sin latido" : "no llegó a estar listo")}: se reinicia"); proc.Kill(); await ExitsWithinAsync(proc, TimeSpan.FromSeconds(2)).ConfigureAwait(false); lock (_pids) _pids.Remove(proc.Id); }
            else Log($"[{r.Spec.Id}] terminó de forma inesperada (código {code})");

            var now = _clock.UtcNow; r.Crashes.Add(now); r.Crashes.RemoveAll(t => now - t > _p.CrashWindow);
            if (r.Crashes.Count >= _p.MaxCrashes) { Log($"[{r.Spec.Id}] {r.Crashes.Count} caídas en {_p.CrashWindow.TotalSeconds:0}s: cortacircuito abierto, no se reintenta"); r.Set(ModuleState.Failed); return; }
            if (now - startedAt >= _p.StableAfter) step = 0;
            var wait = TimeSpan.FromMilliseconds(Math.Min(_p.BackoffMax.TotalMilliseconds, _p.BackoffStart.TotalMilliseconds * Math.Pow(2, step++)));
            r.Set(ModuleState.Crashed); Log($"[{r.Spec.Id}] reinicio en {wait.TotalSeconds:0.##}s");
            try { await _clock.Delay(wait, r.Stop.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task<Outcome> WatchAsync(Runner r, IModuleProcess proc, DateTime startedAt)
    {
        while (true)
        {
            using var tick = CancellationTokenSource.CreateLinkedTokenSource(r.Stop.Token);
            var delay = _clock.Delay(_p.HeartbeatInterval, tick.Token);
            var stopTask = Task.Delay(Timeout.Infinite, r.Stop.Token);
            var done = await Task.WhenAny(proc.Exited, delay, stopTask).ConfigureAwait(false);
            tick.Cancel(); try { await delay.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (done == proc.Exited) return Outcome.Exit;
            if (r.Stop.IsCancellationRequested) return Outcome.Stop;
            var now = _clock.UtcNow;
            if (!r.ReadySeen) { if (now - startedAt > _p.ReadyTimeout) return Outcome.NeverReady; continue; }
            _bus.Send(r.Spec.Id, Ipc.MsgType.Ping);
            if (now - r.LastPong > _p.HeartbeatInterval * _p.MissedBeats) return Outcome.Hung;
        }
    }
}
