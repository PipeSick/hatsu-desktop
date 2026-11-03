using WinMac.SystemControl;

namespace WinMac.Bar;

/// <summary>
/// Lectura y operación de los servicios del sistema FUERA del hilo de interfaz. Mantiene la última <see cref="SystemSnapshot"/>; cada lectura es de un solo vuelo (si la anterior sigue en
/// curso no se lanza otra), las operaciones lentas se hacen en el grupo de hilos y los deslizadores se coalescen (solo importa el último valor). Todo resultado vuelve al hilo de la
/// barra por <c>post</c>. Un fallo de un servicio nunca se propaga: queda en su estado («no disponible» + motivo).
/// </summary>
public sealed class SystemHub
{
    private readonly SystemServices _s; private readonly Action<Action> _post; private readonly Action<string> _log; private readonly object _lock = new();
    private SystemSnapshot _snap = SystemSnapshot.Empty; private string _lastKey = "";
    private int _audio, _wifi, _bt, _fast, _bright, _input, _sessions; private long _brightAt; private readonly Dictionary<string, (int Value, Func<int, ServiceResult> Apply, Action<ServiceResult> Done)> _latest = []; private readonly HashSet<string> _running = [];
    public event Action? Changed;   // en el hilo de la barra

    private readonly Action<Action> _background;

    /// <param name="post">Entrega un resultado al hilo de la barra.</param>
    /// <param name="background">Ejecuta trabajo fuera de ese hilo (por defecto, el grupo de hilos). Las pruebas lo sustituyen por uno determinista.</param>
    public SystemHub(SystemServices services, Action<Action> post, Action<string> log, Action<Action>? background = null)
    { _s = services; _post = post; _log = log; _background = background ?? (a => Task.Run(a)); }

    public SystemSnapshot Snapshot { get { lock (_lock) return _snap; } }
    /// <summary>La salida de audio que había antes del último cambio hecho por WinMac (para «Restaurar»), si el servicio la recuerda.</summary>
    public string? PreviousOutputId => (_s.Audio as IOutputRestore)?.PreviousDefaultId;
    public SystemServices Services => _s;

    private void Update(Func<SystemSnapshot, SystemSnapshot> f)
    {
        bool changed; lock (_lock) { _snap = f(_snap); string k = Key(_snap); changed = k != _lastKey; _lastKey = k; }
        if (changed) _post(() => Changed?.Invoke());
    }

    /// <summary>Resumen de lo que se dibuja: evita redibujar la barra si una lectura no cambió nada visible.</summary>
    private static string Key(SystemSnapshot s) =>
        $"{s.Audio.Available}|{s.Audio.Volume}|{s.Audio.Muted}|{string.Join(",", s.Audio.Outputs.Select(o => o.Id + o.IsDefault))}|{s.Wifi.Available}|{s.Wifi.Connected}|{s.Wifi.Ssid}|{s.Wifi.Bars}|{s.Wifi.RadioOn}|{string.Join(",", s.Wifi.Networks.Select(n => n.Ssid + n.Bars + n.Connected))}"
        + $"|{s.Mic.Available}|{s.Mic.Volume}|{s.Mic.Muted}|{string.Join(",", s.Mic.Devices.Select(d => d.Id + d.IsDefault))}|{string.Join(",", s.AppSessions.Sessions.Select(a => a.Id + a.Volume + a.Muted + a.Active))}"
        + $"|{s.Bluetooth.Available}|{s.Bluetooth.RadioOn}|{string.Join(",", s.Bluetooth.Devices.Select(d => d.Id + d.Connected))}|{s.Brightness.Available}|{s.Brightness.Percent}|{s.Power.Available}|{s.Power.Mode}|{s.Appearance.Available}|{s.Appearance.Dark}|{s.Appearance.SystemDark}|{s.OutputSwitchOk}|{s.CanRestoreOutput}";

    // Cada Refresh* es de un solo vuelo con su propia bandera (ref no se puede capturar en la lambda: se resuelve aquí con métodos explícitos).
    public void RefreshAudio()
    {
        if (Interlocked.Exchange(ref _audio, 1) == 1) return;
        _background(() => { try { var a = _s.Audio.Read(); Update(s => s with { Audio = a, CanRestoreOutput = CanRestore(a) }); } catch (Exception e) { _log("[hub] audio: " + e.Message); } finally { Volatile.Write(ref _audio, 0); } });
    }

    /// <summary>Micrófono: lo lee la pestaña Sonido mientras está visible.</summary>
    public void RefreshInput()
    {
        if (Interlocked.Exchange(ref _input, 1) == 1) return;
        _background(() => { try { var i = _s.Audio.ReadInput(); Update(s => s with { Input = i }); } catch (Exception e) { _log("[hub] micrófono: " + e.Message); } finally { Volatile.Write(ref _input, 0); } });
    }

    /// <summary>Aplicaciones con audio: se vuelve a leer mientras la pestaña Sonido está abierta, así aparecen y desaparecen al abrir o cerrar aplicaciones.</summary>
    public void RefreshSessions()
    {
        if (Interlocked.Exchange(ref _sessions, 1) == 1) return;
        _background(() => { try { var a = _s.Audio.ReadSessions(); Update(s => s with { Sessions = a }); } catch (Exception e) { _log("[hub] sesiones de audio: " + e.Message); } finally { Volatile.Write(ref _sessions, 0); } });
    }

    public void RefreshWifi(bool scan)
    {
        if (Interlocked.Exchange(ref _wifi, 1) == 1) return;
        _background(() => { try { var w = _s.Wifi.Read(scan); Update(s => s with { Wifi = w }); } catch (Exception e) { _log("[hub] wifi: " + e.Message); } finally { Volatile.Write(ref _wifi, 0); } });
    }

    public void RefreshBluetooth(bool withDevices)
    {
        if (Interlocked.Exchange(ref _bt, 1) == 1) return;
        _background(() => { try { var b = _s.Bluetooth.Read(withDevices); Update(s => s with { Bluetooth = withDevices || !s.Bluetooth.Available || s.Bluetooth.Devices.Count == 0 ? b : b with { Devices = s.Bluetooth.Devices } }); } catch (Exception e) { _log("[hub] bluetooth: " + e.Message); } finally { Volatile.Write(ref _bt, 0); } });
    }

    /// <summary>Todo lo que cambia poco: lo rápido (energía, apariencia, compatibilidad de salida) y el brillo, cada uno por su lado.</summary>
    public void RefreshSlow() { RefreshFast(); RefreshBrightness(false); }

    /// <summary>Modo de energía, apariencia y compatibilidad del cambio de salida: instantáneos. NUNCA esperan al brillo (DDC/CI puede tardar varios segundos y descartaría esta lectura).</summary>
    public void RefreshFast()
    {
        if (Interlocked.Exchange(ref _fast, 1) == 1) return;
        _background(() =>
        {
            try
            {
                var pw = _s.Power.ReadMode(); var ap = _s.Appearance.Read(); var sw = _s.Audio.CanSwitchOutput();
                Update(s => s with { Power = pw, Appearance = ap, OutputSwitchOk = sw.IsOk, OutputSwitchReason = sw.IsOk ? null : sw.Message });
            }
            catch (Exception e) { _log("[hub] lecturas rápidas: " + e.Message); }
            finally { Volatile.Write(ref _fast, 0); }
        });
    }

    /// <summary>Brillo (DDC/CI/WMI, lento). Si ya se vio que no hay pantallas controlables no se vuelve a sondear en 60 s, salvo que se fuerce (p. ej. tras mover el deslizador).</summary>
    public void RefreshBrightness(bool force)
    {
        if (!force && !Snapshot.Brightness.Available && Snapshot.Brightness.Reason != "Leyendo…" && Environment.TickCount64 - Interlocked.Read(ref _brightAt) < 60_000) return;
        if (Interlocked.Exchange(ref _bright, 1) == 1) return;
        _background(() =>
        {
            try { var br = _s.Brightness.Read(); Update(s => s with { Brightness = br }); }
            catch (Exception e) { _log("[hub] brillo: " + e.Message); }
            finally { Interlocked.Exchange(ref _brightAt, Environment.TickCount64); Volatile.Write(ref _bright, 0); }
        });
    }

    private bool CanRestore(AudioState a) => PreviousOutputId is { } p && a.Outputs.Any(o => o.Id == p && !o.IsDefault);

    /// <summary>Muestra al instante lo que el usuario acaba de pedir (p. ej. la posición del deslizador mientras arrastra); la lectura real lo corrige al terminar.</summary>
    public void Patch(Func<SystemSnapshot, SystemSnapshot> f) => Update(f);

    /// <summary>Ejecuta una operación en un hilo de trabajo y entrega el resultado en el hilo de la barra.</summary>
    public void Run(Func<ServiceResult> op, Action<ServiceResult> done)
    {
        _background(() =>
        {
            ServiceResult r; try { r = op(); } catch (Exception e) { r = ServiceResult.Failed(e.GetType().Name + ": " + e.Message); }
            _post(() => done(r));
        });
    }

    /// <summary>
    /// Deslizador: solo se aplica el último valor pedido, de uno en uno (DDC/CI tarda). <paramref name="done"/> recibe el resultado del último aplicado. Si llegan más movimientos mientras se aplica uno,
    /// manda el aviso del ÚLTIMO (p. ej. el de soltar el deslizador, que relee el valor real), no el del primero.
    /// </summary>
    public void SetLatest(string key, int value, Func<int, ServiceResult> apply, Action<ServiceResult> done)
    {
        lock (_lock) { _latest[key] = (value, apply, done); if (!_running.Add(key)) return; }
        _background(() =>
        {
            ServiceResult last = ServiceResult.Ok; Action<ServiceResult> doneNow = done;
            while (true)
            {
                (int Value, Func<int, ServiceResult> Apply, Action<ServiceResult> Done) item;
                lock (_lock) { if (!_latest.TryGetValue(key, out item)) { _running.Remove(key); break; } _latest.Remove(key); }
                doneNow = item.Done;
                try { last = item.Apply(item.Value); } catch (Exception e) { last = ServiceResult.Failed(e.GetType().Name + ": " + e.Message); }
            }

            _post(() => doneNow(last));
        });
    }
}
