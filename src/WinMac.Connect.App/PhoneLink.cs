using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace WinMac.Connect.App;

/// <summary>
/// Enlace con el iPhone por Bluetooth LE (fases 1 y 2 de WinMac Connect, docs/39): batería (Battery Service) y notificaciones (ANCS).
/// <para>Todo el estado se toca en UN hilo lógico: cada evento de Windows encola un trabajo (<see cref="Post"/>) y se ejecutan de uno en uno. Un error de Bluetooth
/// se captura, se registra sin contenido y se reintenta con espera creciente: nunca bloquea al resto de WinMac (es otro proceso) ni al propio módulo.</para>
/// <para>Recursos: sin sondeo. Una comprobación barata cada 30 s y los avisos de Windows (conexión, servicios cambiados, valores notificados).</para>
/// <para>Privacidad: el contenido de las notificaciones solo vive en memoria y solo sale por el canal local con el host; nunca va a disco ni a los registros.</para>
/// </summary>
internal sealed class PhoneLink : IAsyncDisposable, IAncsCallActions
{
    private static readonly Guid BatteryService = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb"), BatteryLevel = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");
    private const ushort AppearancePhoneCategory = 1;   // Bluetooth «Generic Phone» (0x0040 >> 6). El iPhone de la prueba real anuncia 0x0040

    private readonly Channel<Func<Task>> _q = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Action<PhoneState> _publishState; private readonly Action<object> _publishList, _alert; private readonly Action<string> _log;
    private readonly CallTracker _call = new(); private Action<CallState>? _publishCall; private bool _callTicking;   // WM-038
    private readonly CallCoordinator _coord; private WindowsTelephonyProvider? _telephony; private bool _ringNegative; private uint _ringUid;   // docs/60: llamadas completas
    private readonly PhoneNotificationBook _book = new(); private readonly AncsDataAssembler _asm = new(); private readonly Backoff _backoff = new();
    private readonly CancellationTokenSource _stop = new(); private readonly Task _loop; private readonly Timer _watchdog;

    private ConnectSettings _cfg = new(); private bool _configured; private PhoneState _state = new() { Status = PhoneLinkStatus.Connecting };
    private BluetoothLEDevice? _dev; private GattSession? _session;
    private GattDeviceService? _batSvc, _ancsSvc; private GattCharacteristic? _bat, _ns, _cp, _ds;
    private DateTime _nextAttempt = DateTime.MinValue, _disconnectedAt = DateTime.MinValue; private bool _listDirty, _listScheduled;
    private readonly Queue<(byte[] Cmd, byte Kind, int Attrs, uint Uid)> _cpQueue = new(); private int _cpInflight;   // un comando del Control Point a la vez
    public DateTime LastBeatUtc { get; private set; } = DateTime.UtcNow;

    public PhoneLink(Action<PhoneState> publishState, Action<object> publishList, Action<object> alert, Action<string> log)
    {
        _publishState = publishState; _publishList = publishList; _alert = alert; _log = log;
        _coord = new CallCoordinator(_call, () => _telephony, () => this);
        _loop = Task.Run(RunAsync);
        _watchdog = new Timer(_ => Post(CheckAsync), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Post(Func<Task> work) => _q.Writer.TryWrite(work);

    /// <summary>WM-038: dónde se publica el estado de la llamada (tópico retenido).</summary>
    public Action<CallState>? PublishCall { set => _publishCall = value; }

    private void CallChanged()
    {
        try { _publishCall?.Invoke(_coord.Present()); } catch (Exception e) { _log("[connect] llamada: " + e.GetType().Name); }
        if (_call.NeedsTicks && !_callTicking) { _callTicking = true; _ = Task.Delay(1000).ContinueWith(_ => Post(TickCallAsync)); }
    }

    private Task TickCallAsync()
    {
        _callTicking = false;
        if (_call.Tick()) _log("[connect] llamada: " + _call.State.Phase);
        CallChanged();   // vuelve a programarse mientras haya una llamada que seguir
        return Task.CompletedTask;
    }

    private void ResetCall() { _coord.Reset(); _ringNegative = false; if (_call.Reset()) CallChanged(); }

    // ── llamadas completas (docs/60) ──
    /// <summary>ANCS solo sirve para rechazar (no necesita audio): «Perform Notification Action» con la acción negativa, si el iPhone la anunció para ESTA llamada.</summary>
    public bool CanReject(uint uid) => _cp is not null && _ringNegative && uid != 0 && uid == _ringUid;
    public async Task<bool> RejectAsync(uint uid, CancellationToken ct)
    {
        if (!CanReject(uid) || _cp is null) return false;
        try { var w = new DataWriter(); w.WriteBytes(Ancs.PerformAction(uid, positive: false)); var r = await _cp.WriteValueWithResultAsync(w.DetachBuffer(), GattWriteOption.WriteWithResponse); _log("[connect] rechazo por ANCS: " + r.Status); return r.Status == GattCommunicationStatus.Success; }
        catch (Exception e) { _log("[connect] rechazo por ANCS: 0x" + e.HResult.ToString("X8")); return false; }
    }

    /// <summary>La Isla/Barra pide una orden de llamada (contestar, rechazar, colgar, silenciar). Solo se ejecuta si el sistema puede de verdad (se comprueba al ejecutar).</summary>
    public void Command(string cmd) => Post(async () =>
    {
        var c = cmd switch { "answer" => (CallCommand?)CallCommand.Answer, "reject" => CallCommand.Reject, "hangup" => CallCommand.Hangup, "mute" => CallCommand.ToggleMute, "route" => CallCommand.RouteToPc, _ => null };
        if (c is null) return;
        var r = await _coord.ExecuteAsync(c.Value);
        _log($"[connect] orden de llamada «{cmd}»: {r.Outcome}{(r.Notice is null ? "" : " — " + r.Notice)}");
        CallChanged();
    });

    private async Task SetTelephonyAsync(bool on)
    {
        if (on && _telephony is null)
        {
            _telephony = new WindowsTelephonyProvider(
                c => { Post(async () => { var dev = _state.Status == PhoneLinkStatus.Connected ? "iPhone" : "Teléfono"; if (await _coord.OnProviderCallAsync(c, dev)) { _log("[connect] llamada (sistema): " + _call.State.Phase); CallChanged(); } }); return Task.CompletedTask; },   // todo el estado se toca en el hilo lógico único
                _log, () => Post(() => { CallChanged(); return Task.CompletedTask; }));
            _log("[connect] control de llamadas desde el PC activado");
        }
        else if (!on && _telephony is not null)
        {
            var t = _telephony; _telephony = null; await t.DisposeAsync(); ResetCall(); _log("[connect] control de llamadas desde el PC desactivado (registro deshecho)");
        }
    }

    private SystemCallObserver? _sysCalls; private Timer? _obsStop;
    /// <summary>Observador EXPERIMENTAL de la telefonía del sistema (docs/57-58), SOLO LECTURA. Se enciende a petición (Ajustes › Diagnóstico) y se apaga solo a los
    /// <see cref="SystemCallObserver.MaxMinutes"/> min, al desactivarlo, al cerrar el módulo o al reiniciarse (no queda guardado en ningún sitio).</summary>
    public void SetObservation(bool on) => Post(() =>
    {
        _obsStop?.Dispose(); _obsStop = null;
        _sysCalls?.Dispose(); _sysCalls = null;   // siempre se parte de cero: nada de observadores duplicados
        if (!on) { _log("[connect] observación de telefonía del sistema detenida"); return Task.CompletedTask; }
        _sysCalls = new SystemCallObserver(active => Post(() =>
        {
            if (_call.OnTelephony(active)) { _log("[connect] llamada (fuente: sistema): " + _call.State.Phase); CallChanged(); }
            else _log("[connect] telefonía del sistema ignorada: sin correlación con una llamada de este iPhone");   // p. ej. Teams: nunca se atribuye al iPhone
            return Task.CompletedTask;
        }), _log);
        SystemCallObserver.TemporaryFeed = true;
        _sysCalls.Start();
        _obsStop = new Timer(_ => SetObservation(false), null, TimeSpan.FromMinutes(SystemCallObserver.MaxMinutes), Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    });

    public void ApplySettings(ConnectSettings c) => Post(async () =>
    {
        var old = _cfg; _cfg = c; bool first = !_configured; _configured = true;
        if (first && c.CallControl) await SetTelephonyAsync(true);
        if (!first && old.DeviceName != c.DeviceName) { _log("[connect] cambió el iPhone elegido"); await TeardownAsync(forgetDevice: true); }
        else
        {
            if (old.Notifications && !c.Notifications || old.Content != c.Content && _ns is not null) await StopAncsAsync(clear: !c.Notifications);   // nueva privacidad: se vuelve a pedir todo con lo que toca
            if (old.Battery && !c.Battery) await StopBatteryAsync();
            if (old.Calls != c.Calls) MarkListDirty();
            if (old.CallControl != c.CallControl) await SetTelephonyAsync(c.CallControl);
        }
        _nextAttempt = DateTime.MinValue; await CheckAsync();
    });

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Func<Task> work;
            try { work = await _q.Reader.ReadAsync(_stop.Token); } catch (OperationCanceledException) { break; }
            LastBeatUtc = DateTime.UtcNow;
            try { await work(); }
            catch (Exception e) { _log($"[connect] error {e.GetType().Name} 0x{e.HResult:X8}: se reintentará"); _nextAttempt = DateTime.UtcNow + _backoff.Next(); }
        }
    }

    // ───────────────────────────── conexión
    private async Task CheckAsync()
    {
        if (!_configured) return;
        if (!_cfg.Battery && !_cfg.Notifications) { await TeardownAsync(forgetDevice: false); SetState(_state with { Status = PhoneLinkStatus.Disconnected, BatteryStatus = PhoneFeatureStatus.Off, NotificationsStatus = PhoneFeatureStatus.Off, Message = "Batería y notificaciones desactivadas en Ajustes" }); return; }
        GraceExpired();
        if (DateTime.UtcNow < _nextAttempt) return;

        var adapter = await BluetoothAdapter.GetDefaultAsync();
        var radio = adapter is null ? null : await adapter.GetRadioAsync();
        if (adapter is null || !adapter.IsLowEnergySupported || radio?.State != Windows.Devices.Radios.RadioState.On)
        {
            SetState(_state with { Status = PhoneLinkStatus.NoBluetooth, Message = adapter is null ? "Este PC no tiene Bluetooth disponible" : "Activa el Bluetooth de Windows" });
            _nextAttempt = DateTime.UtcNow + _backoff.Next(); return;
        }

        if (_dev is null && !await OpenDeviceAsync()) { _nextAttempt = DateTime.UtcNow + _backoff.Next(); return; }

        if (_cfg.Battery && _bat is null) await StartBatteryAsync();
        if (_cfg.Notifications && _ns is null) await StartAncsAsync();

        bool up = _dev!.ConnectionStatus == BluetoothConnectionStatus.Connected && (_bat is not null || _ns is not null);
        if (up) { _backoff.Reset(); _disconnectedAt = DateTime.MinValue; SetState(_state with { Status = PhoneLinkStatus.Connected, LastSeenUtc = DateTime.UtcNow, Message = Explain() }); }
        else { MarkDisconnected(); _nextAttempt = DateTime.UtcNow + _backoff.Next(); }
    }

    private async Task<bool> OpenDeviceAsync()
    {
        var paired = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
        var phones = new List<(DeviceInformation Info, BluetoothLEDevice Dev)>();
        foreach (var d in paired)
        {
            BluetoothLEDevice? le = null;
            try { le = await BluetoothLEDevice.FromIdAsync(d.Id); } catch (Exception) { }
            if (le is null) continue;
            bool phone = le.Appearance.Category == AppearancePhoneCategory || le.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase);
            if (phone) phones.Add((d, le)); else le.Dispose();
        }
        var names = phones.Select(p => p.Dev.Name).ToList();
        var pick = phones.FirstOrDefault(p => _cfg.DeviceName is null || p.Dev.Name.Contains(_cfg.DeviceName, StringComparison.OrdinalIgnoreCase));
        foreach (var p in phones) if (!ReferenceEquals(p.Dev, pick.Dev)) p.Dev.Dispose();
        if (pick.Dev is null)
        {
            SetState(new PhoneState
            {
                Status = PhoneLinkStatus.NoDevice, Candidates = names,
                Message = phones.Count == 0 ? "Empareja el iPhone en Configuración › Bluetooth y dispositivos › Agregar dispositivo" : $"Ningún iPhone emparejado se llama «{_cfg.DeviceName}»",
            });
            return false;
        }

        _dev = pick.Dev;
        _dev.ConnectionStatusChanged += (_, _) => Post(OnConnectionChangedAsync);
        _dev.GattServicesChanged += (_, _) => Post(OnServicesChangedAsync);   // ANCS puede publicarse y retirarse en cualquier momento (especificación de Apple)
        try
        {
            _session = await GattSession.FromDeviceIdAsync(_dev.BluetoothDeviceId);
            if (_session is not null) _session.MaintainConnection = true;   // sin esto Windows corta la conexión inactiva y no llegan avisos (prueba real, docs/39 §6b)
        }
        catch (Exception e) { _log($"[connect] sin sesión mantenida (0x{e.HResult:X8})"); }
        _log("[connect] iPhone elegido (" + names.Count + " emparejado(s))");
        SetState(_state with { Status = PhoneLinkStatus.Connecting, DeviceName = _dev.Name, Candidates = names, Message = null });
        return true;
    }

    private async Task OnConnectionChangedAsync()
    {
        if (_dev is null) return;
        if (_dev.ConnectionStatus == BluetoothConnectionStatus.Connected) { _log("[connect] conexión LE activa"); _nextAttempt = DateTime.MinValue; await CheckAsync(); return; }
        _log("[connect] conexión LE perdida");
        ResetCall();   // WM-038: sin conexión no se sabe nada de la llamada: no queda ninguna fijada
        await DropGattAsync();   // al volver, servicios y suscripciones se rehacen (los UID de ANCS no sobreviven a la sesión)
        MarkDisconnected();
    }

    private async Task OnServicesChangedAsync()
    {
        if (_dev is null) return;
        _log("[connect] el iPhone cambió sus servicios: se vuelven a abrir");
        await DropGattAsync(); _nextAttempt = DateTime.MinValue;
        await Task.Delay(1500);   // iOS publica la tabla GATT por partes tras reiniciar: se espera un poco antes de volver a descubrir
        await CheckAsync();
    }

    /// <summary>Desconectado: la batería queda con su hora (la barra deja de mostrarla); la lista se mantiene 60 s por si es un corte breve y después se vacía.</summary>
    private void MarkDisconnected()
    {
        if (_disconnectedAt == DateTime.MinValue) _disconnectedAt = DateTime.UtcNow;
        SetState(_state with { Status = _dev is null ? _state.Status : PhoneLinkStatus.Disconnected, Message = "El iPhone está fuera de alcance, bloqueado sin conexión o con el Bluetooth apagado" });
    }

    private void GraceExpired()
    {
        if (_disconnectedAt == DateTime.MinValue || DateTime.UtcNow - _disconnectedAt < TimeSpan.FromSeconds(60) || _book.Count == 0) return;
        _book.NewSession(); MarkListDirty();
    }

    // ───────────────────────────── batería
    private async Task StartBatteryAsync()
    {
        var r = await _dev!.GetGattServicesForUuidAsync(BatteryService, BluetoothCacheMode.Uncached);
        if (r.Status != GattCommunicationStatus.Success) { SetState(_state with { BatteryStatus = Status(r.Status) }); return; }
        if (r.Services.Count == 0) { SetState(_state with { BatteryStatus = PhoneFeatureStatus.Unavailable }); return; }
        _batSvc = r.Services[0];
        var cr = await _batSvc.GetCharacteristicsForUuidAsync(BatteryLevel, BluetoothCacheMode.Uncached);
        if (cr.Status != GattCommunicationStatus.Success || cr.Characteristics.Count == 0) { SetState(_state with { BatteryStatus = Status(cr.Status) }); await StopBatteryAsync(); return; }
        _bat = cr.Characteristics[0];
        var v = await _bat.ReadValueAsync(BluetoothCacheMode.Uncached);
        if (v.Status == GattCommunicationStatus.Success && v.Value.Length >= 1) OnBattery(DataReader.FromBuffer(v.Value).ReadByte());
        if (_bat.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
        {
            _bat.ValueChanged += (_, e) => { if (e.CharacteristicValue.Length >= 1) { byte b = DataReader.FromBuffer(e.CharacteristicValue).ReadByte(); Post(() => { OnBattery(b); return Task.CompletedTask; }); } };
            await _bat.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
        }
        SetState(_state with { BatteryStatus = PhoneFeatureStatus.Active });
    }

    private void OnBattery(byte level) { if (level > 100) return; SetState(_state with { Battery = level, BatteryAtUtc = DateTime.UtcNow, LastSeenUtc = DateTime.UtcNow }); }

    private async Task StopBatteryAsync()
    {
        if (_bat is not null) { try { await _bat.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None); } catch (Exception) { } }
        _bat = null; _batSvc?.Dispose(); _batSvc = null;
        SetState(_state with { BatteryStatus = PhoneFeatureStatus.Off, Battery = _cfg.Battery ? _state.Battery : null, BatteryAtUtc = _cfg.Battery ? _state.BatteryAtUtc : null });
    }

    // ───────────────────────────── ANCS
    private async Task StartAncsAsync()
    {
        SetState(_state with { NotificationsStatus = PhoneFeatureStatus.Starting });
        var r = await _dev!.GetGattServicesForUuidAsync(Ancs.Service, BluetoothCacheMode.Uncached);
        if (r.Status != GattCommunicationStatus.Success || r.Services.Count == 0) { SetState(_state with { NotificationsStatus = r.Status == GattCommunicationStatus.Success ? PhoneFeatureStatus.Unavailable : Status(r.Status) }); return; }
        _ancsSvc = r.Services[0];
        await _ancsSvc.OpenAsync(GattSharingMode.SharedReadAndWrite);
        async Task<GattCharacteristic?> Char(Guid id) { var c = await _ancsSvc.GetCharacteristicsForUuidAsync(id, BluetoothCacheMode.Uncached); return c.Status == GattCommunicationStatus.Success && c.Characteristics.Count > 0 ? c.Characteristics[0] : null; }
        _ns = await Char(Ancs.NotificationSource); _cp = await Char(Ancs.ControlPoint); _ds = await Char(Ancs.DataSource);
        if (_ns is null) { SetState(_state with { NotificationsStatus = PhoneFeatureStatus.Unavailable }); await StopAncsAsync(clear: false); return; }

        _book.NewSession(); _asm.Reset(); _cpQueue.Clear(); _cpInflight = 0; MarkListDirty();
        if (_ds is not null)
        {
            _ds.ValueChanged += (_, e) => { var b = Bytes(e.CharacteristicValue); Post(() => { OnData(b); return Task.CompletedTask; }); };
            await _ds.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
        }
        _ns.ValueChanged += (_, e) => { var b = Bytes(e.CharacteristicValue); Post(() => OnSourceAsync(b)); };
        var sub = await _ns.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
        if (sub.Status != GattCommunicationStatus.Success)
        {
            _log($"[connect] ANCS rechazó la suscripción ({sub.Status}{(sub.ProtocolError is { } p ? $", 0x{p:X2}" : "")})");
            SetState(_state with { NotificationsStatus = PhoneFeatureStatus.NotAuthorized }); await StopAncsAsync(clear: true); return;
        }
        _log("[connect] notificaciones del iPhone activas (importación en silencio de las existentes)");
        SetState(_state with { NotificationsStatus = PhoneFeatureStatus.Active });
    }

    private async Task OnSourceAsync(byte[] b)
    {
        if (AncsEvent.Parse(b) is not { } e) return;
        if (!_cfg.Calls && e.Category is AncsCategory.IncomingCall or AncsCategory.MissedCall) return;
        if (e.Category == AncsCategory.IncomingCall && e.Event == AncsEventId.Added) { _ringUid = e.Uid; _ringNegative = e.Flags.HasFlag(AncsFlags.NegativeAction); }
        if (_call.OnEvent(e, _book.Session)) { _log("[connect] llamada: " + _call.State.Phase); CallChanged(); }
        if (_book.Apply(e) is { } uid && _cp is not null && _ds is not null)
        {
            var attrs = Ancs.AttributesFor(_cfg.Content);
            _cpQueue.Enqueue((Ancs.GetNotificationAttributes(uid, attrs), Ancs.CmdGetNotificationAttributes, attrs.Count, uid));
            await PumpControlPointAsync();
        }
        if (e.Event == AncsEventId.Added && !e.PreExisting) ScheduleAlertFlush();   // si el iPhone no da los datos, el aviso sale igual con lo que haya
        MarkListDirty();
    }

    private void OnData(byte[] b)
    {
        foreach (var r in _asm.Feed(b))
        {
            if (r is AncsNotificationAttributes na)
            {
                if (_book.SetAttributes(na) is { } bundle) _cpQueue.Enqueue((Ancs.GetAppAttributes(bundle), Ancs.CmdGetAppAttributes, 1, 0));
                if (_call.OnCaller(na.Uid, _book.Session, na.Values.TryGetValue(Ancs.AttrTitle, out var who) ? who : null)) CallChanged();
            }
            else if (r is AncsAppAttributes aa) _book.SetAppName(aa);
            _cpInflight = 0;
        }
        FlushAlerts(); MarkListDirty();
        Post(PumpControlPointAsync);   // el siguiente comando, también dentro de la cola única (nunca en paralelo con otro trabajo)
    }

    /// <summary>Un comando a la vez: el iPhone responde en orden y así el reensamblado no se equivoca. Si no responde en 3 s, se sigue con el siguiente.</summary>
    private async Task PumpControlPointAsync()
    {
        if (_cp is null || _cpInflight != 0 || _cpQueue.Count == 0) return;
        var (cmd, kind, attrs, uid) = _cpQueue.Dequeue(); int token = Environment.TickCount | 1; _cpInflight = token;
        _asm.Expect(kind, attrs);
        GattWriteResult? res;
        try { var w = new DataWriter(); w.WriteBytes(cmd); res = await _cp.WriteValueWithResultAsync(w.DetachBuffer(), GattWriteOption.WriteWithResponse); }
        catch (Exception e) { _log($"[connect] Control Point: 0x{e.HResult:X8}"); res = null; }
        if (res is null || res.Status != GattCommunicationStatus.Success)
        {   // 0xA2: la notificación ya no existe (normal si se borró enseguida, prueba real)
            _asm.Rejected(kind); if (kind == Ancs.CmdGetNotificationAttributes) _book.AttributesFailed(uid);
            _cpInflight = 0; FlushAlerts(); MarkListDirty(); await PumpControlPointAsync(); return;
        }
        _ = Task.Delay(3000).ContinueWith(_ => Post(async () => { if (_cpInflight == token) { _cpInflight = 0; if (kind == Ancs.CmdGetNotificationAttributes) _book.AttributesFailed(uid); _asm.Reset(); FlushAlerts(); await PumpControlPointAsync(); } }));
    }

    private void ScheduleAlertFlush() => _ = Task.Delay(3500).ContinueWith(_ => Post(() => { FlushAlerts(); return Task.CompletedTask; }));

    private void FlushAlerts()
    {
        foreach (var n in _book.TakeAlerts())
        {
            if (!_cfg.Calls && n.Call != PhoneCallKind.None) continue;
            try { _alert(ConnectWire.ToItem(n)); } catch (Exception e) { _log("[connect] aviso: " + e.GetType().Name); }
        }
    }

    private async Task StopAncsAsync(bool clear)
    {
        foreach (var c in new[] { _ns, _ds }) if (c is not null) { try { await c.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None); } catch (Exception) { } }
        _ns = _cp = _ds = null; _ancsSvc?.Dispose(); _ancsSvc = null; _asm.Reset(); _cpQueue.Clear(); _cpInflight = 0;
        ResetCall();
        if (clear) _book.Clear(); else _book.NewSession();
        MarkListDirty(); SetState(_state with { NotificationsStatus = _cfg.Notifications ? _state.NotificationsStatus : PhoneFeatureStatus.Off });
    }

    // ───────────────────────────── publicación
    /// <summary>La lista se publica como mucho cada 400 ms (la importación inicial trae cientos de cambios seguidos).</summary>
    private void MarkListDirty()
    {
        _listDirty = true; if (_listScheduled) return; _listScheduled = true;
        _ = Task.Delay(400).ContinueWith(_ => Post(() =>
        {
            _listScheduled = false; if (!_listDirty) return Task.CompletedTask; _listDirty = false;
            try { _publishList(ConnectWire.List(_cfg.Notifications ? _book.Snapshot() : [], _cfg.Calls)); } catch (Exception e) { _log("[connect] lista: " + e.GetType().Name); }
            return Task.CompletedTask;
        }));
    }

    private void SetState(PhoneState s) { if (s == _state && s.Candidates.SequenceEqual(_state.Candidates)) return; _state = s; try { _publishState(s); } catch (Exception e) { _log("[connect] estado: " + e.GetType().Name); } }

    private string? Explain() => _state.NotificationsStatus == PhoneFeatureStatus.NotAuthorized
        ? "Para ver las notificaciones: en el iPhone, Ajustes › Bluetooth › (i) junto a este PC › activa «Compartir notificaciones del sistema»"
        : _state.NotificationsStatus == PhoneFeatureStatus.Unavailable && _cfg.Notifications ? "El iPhone no ofrece sus notificaciones ahora; se volverá a intentar" : null;

    private static PhoneFeatureStatus Status(GattCommunicationStatus s) => s == GattCommunicationStatus.AccessDenied ? PhoneFeatureStatus.NotAuthorized : PhoneFeatureStatus.Unavailable;
    private static byte[] Bytes(IBuffer buf) { var b = new byte[buf.Length]; DataReader.FromBuffer(buf).ReadBytes(b); return b; }

    private async Task DropGattAsync()
    {
        _bat = null; _batSvc?.Dispose(); _batSvc = null;
        _ns = _cp = _ds = null; _ancsSvc?.Dispose(); _ancsSvc = null; _asm.Reset(); _cpQueue.Clear(); _cpInflight = 0;
        SetState(_state with { BatteryStatus = _cfg.Battery ? PhoneFeatureStatus.Starting : PhoneFeatureStatus.Off, NotificationsStatus = _cfg.Notifications ? PhoneFeatureStatus.Starting : PhoneFeatureStatus.Off });
        await Task.CompletedTask;
    }

    private async Task TeardownAsync(bool forgetDevice)
    {
        await StopBatteryAsync(); await StopAncsAsync(clear: true);
        if (forgetDevice || !_cfg.Battery && !_cfg.Notifications)
        {
            if (_session is not null) { try { _session.MaintainConnection = false; } catch (Exception) { } _session.Dispose(); _session = null; }   // ya no se mantiene la conexión: sin consumo
            _dev?.Dispose(); _dev = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_telephony is { } tp) { _telephony = null; await tp.DisposeAsync(); }
        _obsStop?.Dispose(); _sysCalls?.Dispose();
        await _watchdog.DisposeAsync();
        var done = new TaskCompletionSource(); Post(async () => { try { await TeardownAsync(forgetDevice: true); } finally { done.TrySetResult(); } });
        await Task.WhenAny(done.Task, Task.Delay(3000));
        _stop.Cancel(); try { await _loop; } catch (Exception) { }
    }
}
