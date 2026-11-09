using System.Collections.Concurrent;
using WinMac.Ipc;

namespace WinMac.Host.Extensions;

/// <summary>
/// Runs enabled extensions as SEPARATE processes (docs/30) on their OWN channel: a second named pipe (current user only) where each launch has its own token,
/// bound to the PID the host started, and where clients cannot talk to each other (<see cref="ServerOptions.Relay"/> off). Every message goes through
/// <see cref="ExtensionBroker"/>. A crashing extension is restarted at most three times with growing waits, then left stopped with the reason; it can
/// never take down the host, the bar or the Island.
/// Limit (documented): an extension runs with the user's own rights — this isolates WinMac from faults and limits what it can ask WinMac to do,
/// it is not a security sandbox for untrusted code.
/// </summary>
public sealed class ExtensionHost : IAsyncDisposable
{
    private sealed class Run
    {
        public required ExtensionManifest M; public required string Token; public IModuleProcess? Proc; public int Crashes; public bool Stopping; public string Status = "Iniciando…";
    }

    private readonly ExtensionCatalog _catalog; private readonly IProcessLauncher _launcher; private readonly Action<string, object> _emit; private readonly Action<string> _log;
    private readonly string _pipe; private readonly ConcurrentDictionary<string, Run> _runs = new(); private readonly ExtensionBroker _broker = new();
    private readonly ConcurrentDictionary<string, string> _lastStatus = new();
    private IpcServer? _server;

    /// <param name="emit">Forwards a validated message to WinMac's bus (topic, payload).</param>
    public ExtensionHost(ExtensionCatalog catalog, IProcessLauncher launcher, Action<string, object> emit, Action<string> log, string pipeName)
    {
        _catalog = catalog; _launcher = launcher; _emit = emit; _log = log; _pipe = pipeName;
    }

    public static string ModuleId(string extId) => "ext-" + extId;

    public void Start()
    {
        _server = new IpcServer(new ServerOptions(_pipe, ExtensionBroker.NewToken())
        {
            Relay = false,
            HelloValidator = (module, token, pid) => _runs.Values.FirstOrDefault(r => ModuleId(r.M.Id) == module) is { } r && r.Token == token && (pid is null || r.Proc is null || r.Proc.Id == pid),
        });
        _server.MessageReceived += OnMessage;
        _server.ModuleConnected += (m, _) => { var r = _runs.Values.FirstOrDefault(x => ModuleId(x.M.Id) == m); if (r is not null) { r.Status = "En ejecución"; _log($"[extensiones] {r.M.Id} conectada"); } };
        _server.Start();
        Sync();
    }

    /// <summary>Starts what is enabled and stops what is not (after install, uninstall or a toggle in Settings).</summary>
    public void Sync()
    {
        var wanted = _catalog.List().Where(e => e.Enabled && e.Manifest is not null).Select(e => (e.Folder, e.Manifest!)).ToList();
        foreach (var id in _runs.Keys.Where(k => !wanted.Any(w => w.Item2.Id == k)).ToList()) Stop(id);
        foreach (var (folder, m) in wanted.Where(w => !_runs.ContainsKey(w.Item2.Id))) Launch(folder, m);
    }

    public string StatusOf(string id) => _runs.TryGetValue(id, out var r) ? r.Status : _lastStatus.TryGetValue(id, out var s) ? s : "Detenida";

    private void Launch(string folder, ExtensionManifest m)
    {
        var run = _runs.GetOrAdd(m.Id, _ => new Run { M = m, Token = ExtensionBroker.NewToken() });
        run.Token = ExtensionBroker.NewToken();   // a new token for every launch
        try
        {
            run.Proc = _launcher.Start(new ModuleSpec(ModuleId(m.Id), Path.Combine(folder, m.Entry), m.Args), _pipe, run.Token);
            run.Status = "Iniciando…";
            _log($"[extensiones] {m.Id} {m.Version} iniciada (permisos: {ExtensionManifest.Describe(m.Permissions)})");
            var proc = run.Proc;
            _ = proc.Exited.ContinueWith(t => OnExited(folder, run, proc, t.Result), TaskScheduler.Default);
        }
        catch (Exception e) { run.Status = "No se pudo iniciar: " + e.Message; _lastStatus[m.Id] = run.Status; _runs.TryRemove(m.Id, out _); _log($"[extensiones] {m.Id} no se pudo iniciar: {e.GetType().Name}"); }
    }

    private void OnExited(string folder, Run run, IModuleProcess proc, int code)
    {
        if (!ReferenceEquals(run.Proc, proc)) return;
        if (run.Stopping) { run.Status = "Detenida"; _lastStatus[run.M.Id] = run.Status; _runs.TryRemove(run.M.Id, out _); return; }
        run.Crashes++;
        if (run.Crashes > 3) { run.Status = $"Detenida tras {run.Crashes - 1} reinicios (código {code})"; _lastStatus[run.M.Id] = run.Status; _runs.TryRemove(run.M.Id, out _); _log($"[extensiones] {run.M.Id} se cerró demasiadas veces: se deja detenida"); return; }
        run.Status = $"Reiniciando (cierre inesperado, código {code})";
        _log($"[extensiones] {run.M.Id} terminó con código {code}; reinicio {run.Crashes}/3");
        _ = Task.Delay(TimeSpan.FromSeconds(2 << (run.Crashes - 1))).ContinueWith(_ => { if (_runs.TryGetValue(run.M.Id, out var r) && ReferenceEquals(r, run) && !run.Stopping) Launch(folder, run.M); }, TaskScheduler.Default);
    }

    public void Stop(string id)
    {
        if (!_runs.TryGetValue(id, out var r)) return;
        r.Stopping = true; _server?.Send(ModuleId(id), MsgType.Shutdown);
        var p = r.Proc;
        _ = Task.Delay(3000).ContinueWith(_ => { try { if (p is not null && !p.Exited.IsCompleted) p.Kill(); } catch (Exception) { } }, TaskScheduler.Default);
        _log($"[extensiones] {id} detenida");
    }

    private void OnMessage(string module, Envelope e)
    {
        if (e.T != MsgType.Evt || e.Topic is null) return;
        var r = _runs.Values.FirstOrDefault(x => ModuleId(x.M.Id) == module); if (r is null) return;
        var fwd = _broker.Translate(r.M, e.Topic, e.Data, DateTime.UtcNow, out bool kill);
        if (fwd is { } f) { try { _emit(f.Topic, f.Payload); } catch (Exception) { } }
        if (kill) { _log($"[extensiones] {r.M.Id} envía demasiados mensajes no válidos: se detiene"); r.Status = "Detenida: mensajes no válidos"; _lastStatus[r.M.Id] = r.Status; Stop(r.M.Id); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _runs.Keys.ToList()) Stop(id);
        await Task.Delay(300).ConfigureAwait(false);
        foreach (var r in _runs.Values) { try { if (r.Proc is { } p && !p.Exited.IsCompleted) p.Kill(); } catch (Exception) { } }
        if (_server is not null) await _server.DisposeAsync().ConfigureAwait(false);
    }
}
