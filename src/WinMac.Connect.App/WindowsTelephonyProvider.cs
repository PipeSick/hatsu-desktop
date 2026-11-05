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
    private readonly Dictionary<string, PhoneCall> _calls = new(); private readonly List<PhoneLineTransportDevice> _transports = [];   // registrados y accesibles
    private DateTime _lastLine = DateTime.UtcNow, _lastConnect = DateTime.UtcNow, _lastReconcile = DateTime.UtcNow;
    private volatile ProviderStatus _status = new(ProviderState.Unavailable, "Iniciando…"); private string _lastSnapshot = "~"; private DateTime _nextSetup = DateTime.MinValue;
    private PhoneLineWatcher? _watcher; private readonly List<Guid> _lineIds = [];

    public ProviderStatus Status => _status;

    public WindowsTelephonyProvider(Func<ProviderCall?, Task> onCall, Action<string> log, Action statusChanged)
    { _onCall = onCall; _log = log; _statusChanged = statusChanged; _loop = Task.Run(RunAsync); }

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
            var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
            if (devs.Count == 0) { SetStatus(ProviderState.Unavailable, "Windows no lista ningún teléfono Bluetooth con manos libres (HFP): empareja el teléfono"); return false; }
            ProviderStatus? last = null; var ready = new List<PhoneLineTransportDevice>();
            foreach (var d in devs.Take(4))
            {
                var t = PhoneLineTransportDevice.FromId(d.Id);   // se asigna antes de cualquier espera: un cierre a mitad aún puede deshacer el registro
                var acc = await t.RequestAccessAsync();          // SIEMPRE antes de RegisterApp
                var access = acc == DeviceAccessStatus.Allowed ? AccessResult.Allowed : acc == DeviceAccessStatus.DeniedByUser ? AccessResult.DeniedByUser : acc == DeviceAccessStatus.DeniedBySystem ? AccessResult.DeniedBySystem : AccessResult.Other;
                bool before = Registered(t) ?? false; var reg = RegisterResult.NotNeeded;
                if (access == AccessResult.Allowed && !before)
                {
                    _log("[connect] registrando Hatsu como cliente del transporte telefónico…");   // antes de la llamada: si el proceso muriera dentro, este es el único rastro
                    try { t.RegisterApp(); reg = RegisterResult.Returned; _log("[connect] RegisterApp volvió"); }
                    catch (Exception e) when (e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005)) { reg = RegisterResult.Unauthorized; }
                    catch (Exception e) { reg = RegisterResult.Threw; _log("[connect] RegisterApp: 0x" + e.HResult.ToString("X8")); }
                }
                bool after = Registered(t) ?? false;   // se RELEE: RegisterApp puede volver sin hacer nada
                var st = RegistrationPlanner.Evaluate(access, before, reg, after); last = st;
                _log($"[connect] transporte: acceso={acc}, registrado antes={before}, después={after} → {st.State}");
                if (!st.Ready) continue;
                try { var ok = await t.ConnectAsync(); _log("[connect] ConnectAsync devolvió " + ok + " (no se evalúa: no es fiable)"); _lastConnect = DateTime.UtcNow; } catch (Exception e) { _log("[connect] ConnectAsync: 0x" + e.HResult.ToString("X8")); }
                ready.Add(t);
            }
            lock (_transports) { _transports.Clear(); _transports.AddRange(ready); }
            if (ready.Count == 0) { SetStatus(last?.State ?? ProviderState.Unavailable, last?.Reason ?? "Sin acceso al transporte telefónico"); return false; }
            await StartWatcherAsync();
            SetStatus(ProviderState.Ready, "Windows gestiona la llamada del teléfono; el audio puede pasar al PC");
            return true;
        }
        catch (Exception e)
        {
            SetStatus(ProviderState.Unavailable, e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005) ? "Acceso denegado por Windows a la telefonía" : "Telefonía de Windows no disponible (0x" + e.HResult.ToString("X8") + ")");
            return false;
        }
    }

    private static bool? Registered(PhoneLineTransportDevice t) { try { return t.IsRegistered(); } catch (Exception) { return null; } }

    /// <summary>Cada 30 s: ¿sigue registrada la app? (se pierde al volver a emparejar el teléfono o si otra app toma el rol) y ¿hay que reintentar la conexión HFP?</summary>
    private async Task ReconcileAsync()
    {
        if (DateTime.UtcNow - _lastReconcile < TimeSpan.FromSeconds(30)) return;
        _lastReconcile = DateTime.UtcNow;
        PhoneLineTransportDevice[] ts; lock (_transports) ts = _transports.ToArray();
        foreach (var t in ts)
        {
            if (RegistrationPlanner.NeedsReregister(_status.Ready, Registered(t)))
            {
                _log("[connect] el registro de telefonía se perdió (¿nuevo emparejamiento u otra app?): se vuelve a registrar");
                try { _watcher?.Stop(); } catch (Exception) { } _watcher = null; lock (_lineIds) _lineIds.Clear();
                lock (_transports) _transports.Clear(); SetStatus(ProviderState.Unavailable, "Registro perdido: volviendo a registrar…"); _nextSetup = DateTime.MinValue; return;
            }
        }
        if (RegistrationPlanner.ShouldReconnect(_status.Ready, DateTime.UtcNow - _lastLine, DateTime.UtcNow - _lastConnect))
        {
            _lastConnect = DateTime.UtcNow;
            foreach (var t in ts) { try { var ok = await t.ConnectAsync(); _log("[connect] reconexión HFP pedida (ConnectAsync " + ok + ")"); } catch (Exception e) { _log("[connect] reconexión HFP: 0x" + e.HResult.ToString("X8")); } }
        }
    }

    private async Task StartWatcherAsync()
    {
        if (_watcher is not null) return;
        var store = await PhoneCallManager.RequestStoreAsync();
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
            try { line = await PhoneLine.FromIdAsync(id); } catch (Exception) { continue; }
            if (line.Transport != PhoneLineTransport.Bluetooth) continue;   // NUNCA Teams, SIM ni otras apps
            if (!OwnsTransport(line.TransportDeviceId)) continue;           // solo líneas de los transportes que ESTA app registró (no basta «Bluetooth»)
            _lastLine = DateTime.UtcNow;
            try
            {
                var res = await line.GetAllActivePhoneCallsAsync();
                foreach (var c in res.AllActivePhoneCalls)
                {
                    string? name = null; bool outgoing = false;
                    try { var inf = await c.GetPhoneCallInfoAsync(); outgoing = inf.CallDirection == PhoneCallDirection.Outgoing; name = !string.IsNullOrWhiteSpace(inf.DisplayName) ? inf.DisplayName : (string.IsNullOrWhiteSpace(inf.PhoneNumber) ? null : inf.PhoneNumber); } catch (Exception) { }
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
        lock (_transports) return _transports.Any(t => { try { return string.Equals(t.DeviceId, lineTransportId, StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } });
    }

    private PhoneCall? Find(string id) { lock (_calls) return _calls.TryGetValue(id, out var c) ? c : null; }

    public async Task<bool> AnswerAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.AcceptIncomingAsync() == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> RejectAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.RejectIncomingAsync() == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> HangupAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.EndAsync() == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> RouteAudioToPcAsync(string id, CancellationToken ct) => Find(id) is { } c && await c.ChangeAudioDeviceAsync(PhoneCallAudioDevice.LocalDevice) == PhoneCallOperationStatus.Succeeded;
    public async Task<bool> SetMuteAsync(string id, bool mute, CancellationToken ct) => Find(id) is { } c && await (mute ? c.MuteAsync() : c.UnmuteAsync()) == PhoneCallOperationStatus.Succeeded;

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel(); try { await _loop; } catch (Exception) { }
        try { _watcher?.Stop(); } catch (Exception) { }
        PhoneLineTransportDevice[] mine; lock (_transports) mine = _transports.ToArray();
        foreach (var t in mine)
        {   // solo si sigue registrada: un UnregisterApp de más no hace daño, pero un registro que se queda impide el siguiente RegisterApp
            try { if (Registered(t) == true) { t.UnregisterApp(); _log("[connect] registro del transporte telefónico deshecho"); } } catch (Exception e) { _log("[connect] no se pudo deshacer el registro: 0x" + e.HResult.ToString("X8")); }
        }
        _cts.Dispose();
    }
}
