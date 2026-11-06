using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

namespace WinMac.Connect.App;

/// <summary>
/// El mecanismo de llamadas completas (docs/60): Windows como cliente HFP del teléfono. Todo con la API pública de telefonía:
/// <list type="number">
/// <item><c>PhoneLineTransportDevice</c> (transporte Bluetooth del teléfono emparejado): <c>RequestAccessAsync</c> → <c>RegisterApp</c> → <c>ConnectAsync</c>. Windows mantiene la sesión HFP (SLC + SCO).</item>
/// <item><c>PhoneLine</c> con transporte Bluetooth → <c>GetAllActivePhoneCallsAsync</c> → <c>PhoneCall</c> (estado, contacto).</item>
/// <item><c>PhoneCall.AcceptIncomingAsync / RejectIncomingAsync / EndAsync / MuteAsync</c> y <c>ChangeAudioDeviceAsync(LocalDevice)</c>: el audio pasa a los dispositivos de audio del PC elegidos en Windows. Hatsu NO transporta audio.</item>
/// </list>
/// Secuencia contrastada con Klangbruecke y MyPhone (docs/61; solo como referencia técnica, sin copiar código): RequestAccessAsync SIEMPRE antes de RegisterApp; se RELEE IsRegistered() porque RegisterApp puede volver sin
/// hacer nada; el booleano de ConnectAsync no es fiable y no se evalúa; las líneas solo aparecen con la app registrada y se filtran por <c>TransportDeviceId</c> == <c>DeviceId</c> del transporte registrado (no basta «Bluetooth»);
/// el registro se vigila (se pierde al volver a emparejar o si otra app toma el rol) y la conexión HFP se reintenta con límite. Las llamadas del sistema son del contrato 6 (Windows 11).
/// Si Windows no concede el registro (hace falta la capacidad restringida <c>phoneLineTransportManagement</c> aprobada por Microsoft) el estado es «No disponible» con la causa y no hay botones. Al apagar o cerrar se DESHACE el registro.
/// </summary>
internal sealed class WindowsTelephonyProvider : ITelephonyProvider, IAsyncDisposable
{
    private readonly Func<ProviderCall?, Task> _onCall; private readonly Action<string> _log; private readonly Action _statusChanged;
    private readonly CancellationTokenSource _cts = new(); private readonly Task _loop;
    private readonly Dictionary<string, PhoneCall> _calls = new();
    private sealed class Tp { public string Id = ""; public string DeviceId = ""; public TransportSequence Seq = null!; }
    private readonly List<Tp> _tps = [];   // transportes registrados y verificados
    private FileStream? _lock; private readonly string _wedgePath; private readonly Action<string>? _onWedged;
    private DateTime _lastLine = DateTime.UtcNow, _lastConnect = DateTime.UtcNow, _lastReconcile = DateTime.UtcNow;
    private volatile ProviderStatus _status = new(ProviderState.Unavailable, "Iniciando…"); private string _lastSnapshot = "~"; private DateTime _nextSetup = DateTime.MinValue;
    private PhoneLineWatcher? _watcher; private readonly List<Guid> _lineIds = [];

    public ProviderStatus Status => _status;

    /// <param name="wedgePath">Archivo donde se anota un bloqueo de Windows (para no reintentar en bucle tras el reinicio).</param>
    /// <param name="onWedged">Una llamada de Windows no volvió: el módulo debe reiniciarse (único modo legítimo de recuperar un hilo bloqueado).</param>
    public WindowsTelephonyProvider(Func<ProviderCall?, Task> onCall, Action<string> log, Action statusChanged, string wedgePath, Action<string>? onWedged)
    { _onCall = onCall; _log = log; _statusChanged = statusChanged; _wedgePath = wedgePath; _onWedged = onWedged; _loop = Task.Run(RunAsync); }

    /// <summary>Lectura/escritura mínima del archivo de bloqueos (sin datos personales).</summary>
    private WedgeGuard.State? ReadWedge() { try { return WedgeGuard.Decay(WedgeGuard.Parse(File.Exists(_wedgePath) ? File.ReadAllText(_wedgePath) : null), DateTime.UtcNow); } catch (Exception) { return null; } }
    private void WriteWedge(string stage)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_wedgePath)!); File.WriteAllText(_wedgePath, WedgeGuard.Serialize(WedgeGuard.Record(ReadWedge(), stage, DateTime.UtcNow))); } catch (Exception) { }
    }

    /// <summary>Acota una lectura asíncrona de Windows (no muta nada): si no vuelve, se sigue sin ella.</summary>
    private static async Task<T?> Bounded<T>(Func<Task<T>> f, TimeSpan cap)
    {
        var t = Task.Run(f); var done = await Task.WhenAny(t, Task.Delay(cap));
        if (done != t) { _ = t.ContinueWith(x => { _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted); return default; }
        return await t;
    }

    private void Wedge(string stage)
    {
        WriteWedge(stage);
        SetStatus(ProviderState.Unavailable, $"Windows dejó de responder en «{stage}»: Hatsu reinicia su módulo de llamadas y no volverá a tocar el transporte hasta dentro de {WedgeGuard.Cooldown(ReadWedge()?.Count ?? 1).TotalMinutes:0} min");
        _nextSetup = DateTime.UtcNow.AddHours(6);
        try { _onWedged?.Invoke(stage); } catch (Exception) { }
    }

    private void SetStatus(ProviderState s, string reason)
    {
        if (_status.State == s && _status.Reason == reason) return;
        _status = new(s, reason); _log($"[connect] telefonía del sistema: {s} — {reason}");
        try { _statusChanged(); } catch (Exception) { }
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            int delay = 1500;
            try
            {
                if (DateTime.UtcNow >= _nextSetup && !await SetupAsync()) _nextSetup = DateTime.UtcNow.AddSeconds(30);
                if (_status.Ready) { await ReconcileAsync(); if (_status.Ready) delay = await PollAsync() ? 400 : 1500; }   // mientras hay una llamada se mira más seguido
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { _log("[connect] telefonía del sistema: " + e.GetType().Name + " 0x" + e.HResult.ToString("X8")); _nextSetup = DateTime.UtcNow.AddSeconds(15); }
            try { await Task.Delay(delay, _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Registro y conexión. Devuelve true si el proveedor quedó listo.</summary>
    private async Task<bool> SetupAsync()
    {
        try
        {
            if (!ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsPhoneContract", RegistrationPlanner.RequiredCallsContract))
            { SetStatus(ProviderState.Unavailable, "Esta versión de Windows no permite controlar llamadas de teléfono (requiere Windows 11)"); _nextSetup = DateTime.UtcNow.AddHours(1); return false; }
            if (ReadWedge() is { } w && WedgeGuard.InCooldown(w, DateTime.UtcNow))
            { SetStatus(ProviderState.Unavailable, $"Windows se bloqueó en «{w.Stage}» (bloqueo n.º {w.Count}): no se toca el transporte hasta las {WedgeGuard.Until(w).ToLocalTime():HH:mm}"); _nextSetup = WedgeGuard.Until(w); return false; }
            if (!TakeLock()) { SetStatus(ProviderState.Unavailable, "Otro proceso de Hatsu (un experimento) está usando el transporte telefónico"); return false; }

            var devs = await Bounded(async () => await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth)), TimeSpan.FromSeconds(10));
            if (devs is null) { SetStatus(ProviderState.Unavailable, "Windows no respondió al listar los teléfonos Bluetooth (10 s)"); return false; }
            if (devs.Count == 0) { SetStatus(ProviderState.Unavailable, "Windows no lista ningún teléfono Bluetooth con manos libres (HFP): empareja el teléfono"); return false; }
            ProviderStatus? last = null; var ready = new List<Tp>();
            foreach (var d in devs.Take(4))
            {
                WinRtTransportOps? ops = await Bounded(() => Task.FromResult(WinRtTransportOps.FromId(d.Id)), TimeSpan.FromSeconds(5));
                if (ops is null) { last = new(ProviderState.Unavailable, "Windows no respondió al abrir el transporte (5 s)"); continue; }
                var seq = new TransportSequence(ops);
                _log("[connect] registrando Hatsu como cliente del transporte telefónico (etapas con tope; presupuesto 40 s)…");
                var res = await seq.RegisterAndConnectAsync(_cts.Token, st => _log("[connect] etapa: " + st));   // cada etapa: resultado, milisegundos y HRESULT
                last = res.Status;
                _log($"[connect] transporte: registrado antes de esta secuencia={res.Stages.FirstOrDefault(x => x.Stage.StartsWith("¿registrado"))?.Outcome}, registrado después={res.RegisteredAfter}, conexión terminó={res.ConnectReturned} → {res.Status.State}: {res.Status.Reason}");
                if (res.Wedged) { await CleanupFresh(d.Id); seq.Dispose(); Wedge(res.WedgedStage ?? "transporte"); return false; }
                if (!res.Ready) { seq.Dispose(); continue; }
                string devId = ""; try { devId = ops.DeviceId; } catch (Exception) { }
                _lastConnect = DateTime.UtcNow;
                ready.Add(new Tp { Id = d.Id, DeviceId = devId, Seq = seq });
            }
            lock (_tps) { _tps.Clear(); _tps.AddRange(ready); }
            if (ready.Count == 0) { ReleaseLock(); SetStatus(last?.State ?? ProviderState.Unavailable, last?.Reason ?? "Sin acceso al transporte telefónico"); return false; }
            await StartWatcherAsync();
            SetStatus(ProviderState.Ready, "Registro HFP verificado (IsRegistered); Windows gestiona la llamada del teléfono y el audio puede pasar al PC");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception e)
        {
            SetStatus(ProviderState.Unavailable, e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005) ? "Acceso denegado por Windows a la telefonía" : "Telefonía de Windows no disponible (0x" + e.HResult.ToString("X8") + ")");
            return false;
        }
    }

    /// <summary>Solo UN proceso de Hatsu toca el transporte a la vez (el módulo o un experimento): sin registros duplicados entre procesos. Cerrojo de archivo exclusivo (un Mutex tiene afinidad de hilo y aquí se cruzan awaits).</summary>
    private bool TakeLock() { if (_lock is not null) return true; _lock = HfpLock.TryTake(); return _lock is not null; }
    private void ReleaseLock() { try { _lock?.Dispose(); } catch (Exception) { } _lock = null; }

    /// <summary>Tras un bloqueo, intenta deshacer el registro con un trabajador NUEVO y tope de 4 s (el anterior puede seguir colgado). Si tampoco vuelve, el reinicio del módulo es lo que lo libera.</summary>
    private async Task CleanupFresh(string transportId)
    {
        using var w = new StageWorker("hatsu-hfp-cleanup");
        var t = w.Run(() => { var o = WinRtTransportOps.FromId(transportId); if (o.IsRegistered()) { o.UnregisterApp(); return true; } return false; });
        var done = await Task.WhenAny(t, Task.Delay(4000));
        if (done != t) { w.Poison(); _log("[connect] no se pudo deshacer el registro tras el bloqueo (Windows sigue sin responder)"); return; }
        try { _log(await t ? "[connect] registro deshecho tras el bloqueo" : "[connect] tras el bloqueo no había registro"); } catch (Exception e) { _log("[connect] limpieza tras el bloqueo: 0x" + e.HResult.ToString("X8")); }
    }

    /// <summary>Cada 30 s: ¿sigue registrada la app? (se pierde al volver a emparejar el teléfono o si otra app toma el rol) y ¿hay que reintentar la conexión HFP? Todo con tope por operación.</summary>
    private async Task ReconcileAsync()
    {
        if (DateTime.UtcNow - _lastReconcile < TimeSpan.FromSeconds(30)) return;
        _lastReconcile = DateTime.UtcNow;
        Tp[] ts; lock (_tps) ts = _tps.ToArray();
        foreach (var tp in ts)
        {
            var reg = await tp.Seq.ReadRegisteredAsync(TimeSpan.FromSeconds(5));
            if (reg is null && tp.Seq.Worker.Poisoned) { Wedge("lectura del registro (IsRegistered)"); return; }
            if (RegistrationPlanner.NeedsReregister(_status.Ready, reg))
            {
                _log("[connect] el registro de telefonía se perdió (¿nuevo emparejamiento u otra app?): se vuelve a registrar");
                await TeardownTransportsAsync(unregister: false);
                SetStatus(ProviderState.Unavailable, "Registro perdido: volviendo a registrar…"); _nextSetup = DateTime.MinValue; return;
            }
        }
        if (RegistrationPlanner.ShouldReconnect(_status.Ready, DateTime.UtcNow - _lastLine, DateTime.UtcNow - _lastConnect))
        {
            _lastConnect = DateTime.UtcNow;
            foreach (var tp in ts)
            {
                var r = await tp.Seq.ReconnectAsync(); _log("[connect] reconexión HFP pedida: " + r);
                if (r.Outcome is StageOutcome.Timeout or StageOutcome.Cancelled) { Wedge("reconexión HFP (ConnectAsync)"); return; }
            }
        }
    }

    private async Task TeardownTransportsAsync(bool unregister)
    {
        try { _watcher?.Stop(); } catch (Exception) { } _watcher = null; lock (_lineIds) _lineIds.Clear();
        Tp[] ts; lock (_tps) { ts = _tps.ToArray(); _tps.Clear(); }
        foreach (var tp in ts)
        {
            if (unregister)
            {
                var c = tp.Seq.Worker.Poisoned ? null : await tp.Seq.CleanupAsync();
                if (c is null) await CleanupFresh(tp.Id); else _log("[connect] " + c);
            }
            tp.Seq.Dispose();
        }
        ReleaseLock();
    }

    private async Task StartWatcherAsync()
    {
        if (_watcher is not null) return;
        var store = await Bounded(async () => await PhoneCallManager.RequestStoreAsync(), TimeSpan.FromSeconds(10));
        if (store is null) { _log("[connect] PhoneCallManager.RequestStoreAsync no respondió (10 s)"); return; }
        _watcher = store.RequestLineWatcher();
        _watcher.LineAdded += (_, e) => { lock (_lineIds) if (!_lineIds.Contains(e.LineId)) _lineIds.Add(e.LineId); };
        _watcher.LineRemoved += (_, e) => { lock (_lineIds) _lineIds.Remove(e.LineId); };
        _watcher.Start();
    }

    /// <summary>Lee las llamadas de las líneas Bluetooth y avisa si cambió algo. Devuelve true si hay una llamada (para mirar más seguido).</summary>
    private async Task<bool> PollAsync()
    {
        Guid[] ids; lock (_lineIds) ids = _lineIds.ToArray();
        var found = new List<(PhoneCall Call, string Name, PhoneCallStatus Status)>();
        foreach (var id in ids)
        {
            PhoneLine line;
            try { var l0 = await Bounded(async () => await PhoneLine.FromIdAsync(id), TimeSpan.FromSeconds(5)); if (l0 is null) continue; line = l0; } catch (Exception) { continue; }
            if (line.Transport != PhoneLineTransport.Bluetooth) continue;   // NUNCA Teams, SIM ni otras apps
            if (!OwnsTransport(line.TransportDeviceId)) continue;           // solo líneas de los transportes que ESTA app registró (no basta «Bluetooth»)
            _lastLine = DateTime.UtcNow;
            try
            {
                var res = await Bounded(async () => await line.GetAllActivePhoneCallsAsync(), TimeSpan.FromSeconds(5));
                if (res is null) continue;
                foreach (var c in res.AllActivePhoneCalls)
                {
                    string? name = null; bool outgoing = false;
                    try { var inf = await Bounded(async () => await c.GetPhoneCallInfoAsync(), TimeSpan.FromSeconds(3)); if (inf is null) throw new TimeoutException(); outgoing = inf.CallDirection == PhoneCallDirection.Outgoing; name = !string.IsNullOrWhiteSpace(inf.DisplayName) ? inf.DisplayName : (string.IsNullOrWhiteSpace(inf.PhoneNumber) ? null : inf.PhoneNumber); } catch (Exception) { }
                    if (outgoing) continue;   // las llamadas salientes están fuera de alcance
                    found.Add((c, name ?? "", c.Status));
                }
            }
            catch (Exception e) { _log("[connect] llamadas de la línea: 0x" + e.HResult.ToString("X8")); }
        }

        var pick = found.FirstOrDefault(f => f.Status == PhoneCallStatus.Talking);
        if (pick.Call is null) pick = found.FirstOrDefault(f => f.Status == PhoneCallStatus.Incoming);
        if (pick.Call is null) pick = found.FirstOrDefault(f => f.Status == PhoneCallStatus.Held);
        lock (_calls) { _calls.Clear(); foreach (var f in found) _calls[f.Call.CallId.ToString()] = f.Call; }

        ProviderCall? pc = pick.Call is null ? null : new(pick.Call.CallId.ToString(),
            pick.Status switch { PhoneCallStatus.Incoming => ProviderCallStatus.Incoming, PhoneCallStatus.Talking => ProviderCallStatus.Talking, PhoneCallStatus.Held => ProviderCallStatus.Held, _ => ProviderCallStatus.Ended },
            string.IsNullOrEmpty(pick.Name) ? null : pick.Name, null, pick.Call.AudioDevice == PhoneCallAudioDevice.LocalDevice, pick.Call.IsMuted);
        string snap = pc is null ? "-" : $"{pc.Id}|{pc.Status}|{pc.Name}|{pc.AudioOnPc}|{pc.Muted}";
        if (snap != _lastSnapshot)
        {
            // Un instante sin llamadas no cuenta como «terminada» si venía una activa: dos lecturas seguidas vacías (evita parpadeos al re-enumerar líneas)
            if (pc is null && _lastSnapshot != "-" && !_emptyOnce) { _emptyOnce = true; return true; }
            _emptyOnce = false; _lastSnapshot = snap;
            await _onCall(pc);
        }
        else _emptyOnce = false;
        return pc is not null;
    }
    private bool _emptyOnce;

    private bool OwnsTransport(string? lineTransportId)
    {
        if (string.IsNullOrEmpty(lineTransportId)) return false;
        lock (_tps) return _tps.Any(t => string.Equals(t.DeviceId, lineTransportId, StringComparison.OrdinalIgnoreCase));
    }

    private PhoneCall? Find(string id) { lock (_calls) return _calls.TryGetValue(id, out var c) ? c : null; }

    public async Task<bool> AnswerAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.AcceptIncomingAsync() == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> RejectAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.RejectIncomingAsync() == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> HangupAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.EndAsync() == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> RouteAudioToPcAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice) == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> SetMuteAsync(string id, bool mute, CancellationToken ct) => Find(id) is { } c && await (mute ? c.MuteAsync() : c.UnmuteAsync()) == PhoneCallOperationStatus.Succeeded;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Task.WhenAny(_loop, Task.Delay(3000));   // el bucle puede estar dentro de una llamada de Windows que no vuelve: no se espera más de 3 s
        await TeardownTransportsAsync(unregister: true);
        _cts.Dispose();
    }
}
