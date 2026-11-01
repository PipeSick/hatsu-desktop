using WinMac.SystemControl;
using WinMac.SystemControl.Win;

namespace WinMac.Bar.Win.Shell;

/// <summary>
/// Lectura y operación de los servicios del sistema FUERA del hilo de interfaz. Mantiene la última <see cref="SystemSnapshot"/>; cada lectura es de un solo vuelo (si la anterior sigue en
/// curso no se lanza otra), las operaciones lentas se hacen en el grupo de hilos y los deslizadores se coalescen (solo importa el último valor). Todo resultado vuelve al hilo de la
/// barra por <c>post</c>. Un fallo de un servicio nunca se propaga: queda en su estado («no disponible» + motivo).
/// </summary>
internal sealed class SystemHub
{
    private readonly SystemServices _s; private readonly Action<Action> _post; private readonly Action<string> _log; private readonly object _lock = new();
    private SystemSnapshot _snap = SystemSnapshot.Empty; private string _lastKey = "";
    private int _audio, _wifi, _bt, _slow; private readonly Dictionary<string, int> _latest = []; private readonly HashSet<string> _running = [];
    public event Action? Changed;   // en el hilo de la barra

    public SystemHub(SystemServices services, Action<Action> post, Action<string> log) { _s = services; _post = post; _log = log; }

    public SystemSnapshot Snapshot { get { lock (_lock) return _snap; } }
    public AudioService? AudioImpl => _s.Audio as AudioService;
    public SystemServices Services => _s;

    private void Update(Func<SystemSnapshot, SystemSnapshot> f)
    {
        bool changed; lock (_lock) { _snap = f(_snap); string k = Key(_snap); changed = k != _lastKey; _lastKey = k; }
        if (changed) _post(() => Changed?.Invoke());
    }

    /// <summary>Resumen de lo que se dibuja: evita redibujar la barra si una lectura no cambió nada visible.</summary>
    private static string Key(SystemSnapshot s) =>
        $"{s.Audio.Available}|{s.Audio.Volume}|{s.Audio.Muted}|{string.Join(",", s.Audio.Outputs.Select(o => o.Id + o.IsDefault))}|{s.Wifi.Available}|{s.Wifi.Connected}|{s.Wifi.Ssid}|{s.Wifi.Bars}|{s.Wifi.RadioOn}|{string.Join(",", s.Wifi.Networks.Select(n => n.Ssid + n.Bars + n.Connected))}"
        + $"|{s.Bluetooth.Available}|{s.Bluetooth.RadioOn}|{string.Join(",", s.Bluetooth.Devices.Select(d => d.Id + d.Connected))}|{s.Brightness.Available}|{s.Brightness.Percent}|{s.Power.Available}|{s.Power.Mode}|{s.Appearance.Available}|{s.Appearance.Dark}|{s.OutputSwitchOk}|{s.CanRestoreOutput}";

    // Cada Refresh* es de un solo vuelo con su propia bandera (ref no se puede capturar en la lambda: se resuelve aquí con métodos explícitos).
    public void RefreshAudio()
    {
        if (Interlocked.Exchange(ref _audio, 1) == 1) return;
        Task.Run(() => { try { var a = _s.Audio.Read(); Update(s => s with { Audio = a, CanRestoreOutput = CanRestore(a) }); } catch (Exception e) { _log("[hub] audio: " + e.Message); } finally { Volatile.Write(ref _audio, 0); } });
    }

    public void RefreshWifi(bool scan)
    {
        if (Interlocked.Exchange(ref _wifi, 1) == 1) return;
        Task.Run(() => { try { var w = _s.Wifi.Read(scan); Update(s => s with { Wifi = w }); } catch (Exception e) { _log("[hub] wifi: " + e.Message); } finally { Volatile.Write(ref _wifi, 0); } });
    }

    public void RefreshBluetooth(bool withDevices)
    {
        if (Interlocked.Exchange(ref _bt, 1) == 1) return;
        Task.Run(() => { try { var b = _s.Bluetooth.Read(withDevices); Update(s => s with { Bluetooth = withDevices || !s.Bluetooth.Available || s.Bluetooth.Devices.Count == 0 ? b : b with { Devices = s.Bluetooth.Devices } }); } catch (Exception e) { _log("[hub] bluetooth: " + e.Message); } finally { Volatile.Write(ref _bt, 0); } });
    }

    /// <summary>Brillo, modo de energía, apariencia y compatibilidad del cambio de salida: cambian poco y algunos son lentos (DDC/CI).</summary>
    public void RefreshSlow()
    {
        if (Interlocked.Exchange(ref _slow, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                // Lo rápido se publica ANTES que el brillo: DDC/CI puede tardar varios segundos (monitores sin DDC/CI) y no debe retrasar el reflejo del modo de energía o del tema.
                var pw = _s.Power.ReadMode(); var ap = _s.Appearance.Read(); var sw = _s.Audio.CanSwitchOutput();
                Update(s => s with { Power = pw, Appearance = ap, OutputSwitchOk = sw.IsOk, OutputSwitchReason = sw.IsOk ? null : sw.Message });
                var br = _s.Brightness.Read();
                Update(s => s with { Brightness = br });
            }
            catch (Exception e) { _log("[hub] lecturas lentas: " + e.Message); }
            finally { Volatile.Write(ref _slow, 0); }
        });
    }

    private bool CanRestore(AudioState a) => AudioImpl?.PreviousDefaultId is { } p && a.Outputs.Any(o => o.Id == p && !o.IsDefault);

    /// <summary>Muestra al instante lo que el usuario acaba de pedir (p. ej. la posición del deslizador mientras arrastra); la lectura real lo corrige al terminar.</summary>
    public void Patch(Func<SystemSnapshot, SystemSnapshot> f) => Update(f);

    /// <summary>Ejecuta una operación en un hilo de trabajo y entrega el resultado en el hilo de la barra.</summary>
    public void Run(Func<ServiceResult> op, Action<ServiceResult> done)
    {
        Task.Run(() =>
        {
            ServiceResult r; try { r = op(); } catch (Exception e) { r = ServiceResult.Failed(e.GetType().Name + ": " + e.Message); }
            _post(() => done(r));
        });
    }

    /// <summary>Deslizador: solo se aplica el último valor pedido, de uno en uno (DDC/CI tarda). <paramref name="done"/> recibe el resultado del último aplicado.</summary>
    public void SetLatest(string key, int value, Func<int, ServiceResult> apply, Action<ServiceResult> done)
    {
        lock (_lock) { _latest[key] = value; if (!_running.Add(key)) return; }
        Task.Run(() =>
        {
            ServiceResult last = ServiceResult.Ok;
            while (true)
            {
                int v; lock (_lock) { if (!_latest.TryGetValue(key, out v)) { _running.Remove(key); break; } _latest.Remove(key); }
                try { last = apply(v); } catch (Exception e) { last = ServiceResult.Failed(e.GetType().Name + ": " + e.Message); }
            }

            _post(() => done(last));
        });
    }
}
