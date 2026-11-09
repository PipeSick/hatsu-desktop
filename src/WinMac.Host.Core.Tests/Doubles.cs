using WinMac.Host;

internal sealed class ManualClock : IClock
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); private readonly List<(DateTime Due, TaskCompletionSource Tcs)> _w = [];
    public DateTime UtcNow { get { lock (_w) return _now; } }

    public Task Delay(TimeSpan d, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_w) _w.Add((_now + d, tcs));
        if (ct.CanBeCanceled) ct.Register(() => { lock (_w) _w.RemoveAll(x => x.Tcs == tcs); tcs.TrySetCanceled(ct); });
        return tcs.Task;
    }

    /// <summary>Avanza el tiempo saltando de un vencimiento al siguiente (y dejando que cada continuación corra antes de seguir).</summary>
    public async Task AdvanceAsync(TimeSpan total)
    {
        DateTime end; lock (_w) end = _now + total;
        while (true)
        {
            List<TaskCompletionSource> due = [];
            lock (_w)
            {
                var next = _w.Count == 0 ? end : _w.Min(x => x.Due); var target = next < end ? (next < _now ? _now : next) : end; _now = target;
                foreach (var x in _w.Where(x => x.Due <= _now).OrderBy(x => x.Due).ToList()) { due.Add(x.Tcs); _w.Remove(x); }
            }
            foreach (var t in due) t.TrySetResult();
            await Task.Delay(8);
            lock (_w) { if (due.Count == 0 && _now >= end) return; if (_now >= end && !_w.Any(x => x.Due <= _now)) return; }
        }
    }
}

internal sealed class FakeProcess(int id) : IModuleProcess
{
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Id { get; } = id; public Task<int> Exited => _exit.Task;
    public bool CloseRequested, Killed;
    public void RequestClose() => CloseRequested = true;
    public void Kill() { Killed = true; _exit.TrySetResult(137); }
    public void Exit(int code) => _exit.TrySetResult(code);
}

internal sealed class FakeLauncher : IProcessLauncher
{
    public readonly List<(string Id, FakeProcess Proc)> Started = []; private int _pid = 1000; public HashSet<string> Missing = []; public Dictionary<string, Exception> Blocked = [];
    public IModuleProcess Start(ModuleSpec spec, string pipeName, string token)
    {
        if (Missing.Contains(spec.Id)) throw new FileNotFoundException("no existe", spec.ExePath);
        if (Blocked.TryGetValue(spec.Id, out var blocked)) throw blocked;
        var p = new FakeProcess(++_pid); lock (Started) Started.Add((spec.Id, p)); return p;
    }
    public int Count(string id) { lock (Started) return Started.Count(s => s.Id == id); }
    public FakeProcess Last(string id) { lock (Started) return Started.Last(s => s.Id == id).Proc; }
}

internal sealed class FakeBus : IHostBus
{
    public event Action<string>? Ready; public event Action<string>? Pong; public event Action<string>? Bye; public event Action<string>? Disconnected;
    public event Action<string, string, System.Text.Json.JsonElement?>? Event;
    public readonly List<(string Topic, object Data)> Published = []; public readonly List<string> Emitted = [];
    public void Publish(string topic, object data) { lock (Published) Published.Add((topic, data)); }
    public void Emit(string topic, object? data = null) { lock (Emitted) Emitted.Add(topic); }
    public void RaiseEvent(string m, string topic, string json) => Event?.Invoke(m, topic, System.Text.Json.JsonDocument.Parse(json).RootElement.Clone());
    public readonly List<(string Module, string Type)> Sent = []; public Func<string, string, bool>? OnSend;
    public bool Send(string module, string type) { lock (Sent) Sent.Add((module, type)); return OnSend?.Invoke(module, type) ?? true; }
    public void RaiseReady(string m) => Ready?.Invoke(m); public void RaisePong(string m) => Pong?.Invoke(m); public void RaiseBye(string m) => Bye?.Invoke(m); public void RaiseDisconnected(string m) => Disconnected?.Invoke(m);
    public int Count(string m, string t) { lock (Sent) return Sent.Count(s => s.Module == m && s.Type == t); }
}

internal sealed class Fixture
{
    public readonly ManualClock Clock = new(); public readonly FakeLauncher Launcher = new(); public readonly FakeBus Bus = new(); public readonly List<string> Log = [];
    public readonly Supervisor Sup; public readonly SupervisorPolicy Policy;
    public Fixture(SupervisorPolicy? policy = null)
    {
        Policy = policy ?? new SupervisorPolicy(); Sup = new Supervisor(Launcher, Bus, Clock, Policy, "pipe", "tok", m => { lock (Log) Log.Add(m); });
    }
    public Fixture With(params (string Id, bool Enabled)[] mods) { foreach (var (id, en) in mods) Sup.Add(new ModuleSpec(id, "/x/" + id, []), en); return this; }
    public async Task StartAllAsync()
    {
        var t = Sup.StartAsync();   // cada módulo espera a que el anterior ya esté lanzado y luego el escalonado: se avanza el reloj hasta que termine
        for (int i = 0; i < 200 && !t.IsCompleted; i++) { await Clock.AdvanceAsync(TimeSpan.FromMilliseconds(300)); await Task.Delay(2); }
        await t.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public ModuleState State(string id) => Sup.States[id];
    /// <summary>Simula que el módulo se conectó y está listo.</summary>
    public async Task ReadyAsync(string id) { Bus.RaiseReady(id); await Task.Delay(8); }
    /// <summary>Avanza el reloj respondiendo a los latidos como lo haría un módulo sano.</summary>
    public async Task AdvanceHealthyAsync(TimeSpan total, params string[] healthy)
    {
        Bus.OnSend = (m, t) => { if (t == "ping" && healthy.Contains(m)) Bus.RaisePong(m); return true; };
        await Clock.AdvanceAsync(total);
    }
}
