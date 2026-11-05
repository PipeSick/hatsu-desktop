using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace WinMac.Connect.App;

/// <summary>
/// El mecanismo de llamadas completas (docs/60): Windows como cliente HFP del teléfono. Todo con la API pública de telefonía:
/// <list type="number">
/// <item><c>PhoneLineTransportDevice</c> (transporte Bluetooth del teléfono emparejado): <c>RequestAccessAsync</c> → <c>RegisterApp</c> → <c>ConnectAsync</c>. Windows mantiene la sesión HFP (SLC + SCO).</item>
/// <item><c>PhoneLine</c> con transporte Bluetooth → <c>GetAllActivePhoneCallsAsync</c> → <c>PhoneCall</c> (estado, contacto).</item>
/// <item><c>PhoneCall.AcceptIncomingAsync / RejectIncomingAsync / EndAsync / MuteAsync</c> y <c>ChangeAudioDeviceAsync(LocalDevice)</c>: el audio pasa a los dispositivos de audio del PC elegidos en Windows. Hatsu NO transporta audio.</item>
/// </list>
/// Solo se consideran líneas Bluetooth (nunca Teams ni otras apps de VoIP). Si Windows no concede el registro (hace falta la capacidad restringida <c>phoneLineTransportManagement</c> aprobada por Microsoft),
/// el estado es «No disponible» con la causa y no se ofrece ningún botón. Al apagar la función o cerrar el módulo se DESHACE el registro.
/// </summary>
internal sealed class WindowsTelephonyProvider : ITelephonyProvider, IAsyncDisposable
{
    private readonly Func<ProviderCall?, Task> _onCall; private readonly Action<string> _log; private readonly Action _statusChanged;
    private readonly CancellationTokenSource _cts = new(); private readonly Task _loop;
    private readonly Dictionary<string, PhoneCall> _calls = new(); private readonly List<PhoneLineTransportDevice> _registeredByUs = [];
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
                if (_status.Ready) delay = await PollAsync() ? 400 : 1500;   // mientras hay una llamada se mira más seguido
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
            var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
            if (devs.Count == 0) { SetStatus(ProviderState.Unavailable, "Windows no lista ningún teléfono Bluetooth con manos libres (HFP): empareja el teléfono"); return false; }
            bool any = false; string? denied = null;
            foreach (var d in devs.Take(4))
            {
                var t = PhoneLineTransportDevice.FromId(d.Id);
                var access = await t.RequestAccessAsync();
                if (access != DeviceAccessStatus.Allowed) { denied = "Windows no concedió el acceso al teléfono (" + access + ")"; continue; }
                try
                {
                    if (!t.IsRegistered()) { t.RegisterApp(); lock (_registeredByUs) _registeredByUs.Add(t); _log("[connect] registrado como cliente del transporte telefónico"); }
                }
                catch (Exception e) when (e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005))
                { denied = "Windows negó el registro de Hatsu como cliente de telefonía: hace falta la capacidad phoneLineTransportManagement aprobada por Microsoft (docs/60)"; continue; }
                try { await t.ConnectAsync(); } catch (Exception e) { _log("[connect] ConnectAsync: 0x" + e.HResult.ToString("X8")); }   // si Windows ya lo tiene conectado, no pasa nada
                any = true;
            }
            if (!any) { SetStatus(ProviderState.Unavailable, denied ?? "Sin acceso al transporte telefónico"); return false; }
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
        PhoneLineTransportDevice[] mine; lock (_registeredByUs) mine = _registeredByUs.ToArray();
        foreach (var t in mine) { try { t.UnregisterApp(); _log("[connect] registro del transporte telefónico deshecho"); } catch (Exception e) { _log("[connect] no se pudo deshacer el registro: 0x" + e.HResult.ToString("X8")); } }
        _cts.Dispose();
    }
}
