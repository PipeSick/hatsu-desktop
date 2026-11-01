using WinMac.SystemControl;

namespace WinMac.Bar.Hub;

public interface ISystemMonitor { SystemSample Read(); }
public interface INetworkMonitor { NetCounters ReadCounters(); IReadOnlyList<NetApp> ReadApps(); }

/// <summary>Contadores acumulados de lectura/escritura de un disco FÍSICO (por número), de los contadores de rendimiento de Windows.</summary>
public sealed record DiskIo(int DiskNumber, ulong ReadBytes, ulong WrittenBytes);

public interface IDiskMonitor
{
    DisksSnapshot ReadCatalog();
    IReadOnlyList<DiskIo> ReadIo();
    /// <summary>Expulsión SEGURA (la misma del Explorador): si la unidad está en uso, Windows lo dice y no se fuerza nada.</summary>
    ServiceResult Eject(string volumeLetter);
}

/// <summary>Icono de una aplicación como píxeles BGRA premultiplicados (<c>size × size</c>), o null si no se puede obtener.</summary>
public interface IAppIcons { byte[]? GetBgra(string exePath, int size); }

/// <summary>Ubicación del equipo (opcional, solo con permiso del usuario).</summary>
public interface ILocationProvider { Task<GeoPoint?> GetAsync(CancellationToken ct); }

public sealed record HubServices(ISystemMonitor System, INetworkMonitor Network, IDiskMonitor Disks, IWeatherSource Weather, ILocationProvider? Location, IAppIcons Icons, SpeedTestEngine? SpeedTest);

/// <summary>
/// Datos en vivo de las pestañas Sistema, Red y Discos. NO consulta nada por su cuenta: el anfitrión llama a <c>Refresh*</c> solo mientras el panel está abierto en la pestaña correspondiente, con la
/// frecuencia configurada. Cada lectura es de un solo vuelo y va a segundo plano; un proveedor que falla deja su estado como «no disponible» y no afecta a los demás.
/// </summary>
public sealed class HubMonitor
{
    public const int HistoryLength = 60;
    private readonly HubServices _s; private readonly Action<Action> _background, _post; private readonly Action<string> _log; private readonly Func<DateTime> _clock;
    private int _sys, _net, _disk; private DateTime _lastCatalog = DateTime.MinValue;
    private readonly Dictionary<int, DiskMeters> _io = [];
    public event Action? Changed;   // en el hilo de la barra

    public sealed class DiskMeters { public RateMeter Read { get; } = new(); public RateMeter Write { get; } = new(); public History ReadHistory { get; } = new(HistoryLength); public History WriteHistory { get; } = new(HistoryLength); }

    public HubMonitor(HubServices services, Action<Action> background, Action<Action> post, Action<string> log, Func<DateTime> clock)
    { _s = services; _background = background; _post = post; _log = log; _clock = clock; }

    public HubServices Services => _s;
    public SystemSample Sample { get; private set; } = SystemSample.Empty;
    public History CpuHistory { get; } = new(HistoryLength); public History GpuHistory { get; } = new(HistoryLength); public History MemoryHistory { get; } = new(HistoryLength);
    public TrafficTracker Traffic { get; } = new(HistoryLength);
    public IReadOnlyList<NetApp> NetApps { get; private set; } = [];
    public string? NetAppsReason { get; private set; }
    public DisksSnapshot Disks { get; private set; } = DisksSnapshot.None("Leyendo…");
    public DiskMeters? MetersOf(int diskNumber) => _io.GetValueOrDefault(diskNumber);

    /// <summary>
    /// Se abrió el panel: empieza una sesión de lectura nueva. Las gráficas parten vacías y la primera muestra tras la pausa no calcula velocidad (el acumulado de la sesión se conserva).
    /// </summary>
    public void StartSession()
    {
        CpuHistory.Clear(); GpuHistory.Clear(); MemoryHistory.Clear(); Traffic.DownHistory.Clear(); Traffic.UpHistory.Clear(); Traffic.MarkGap();
        foreach (var d in _io.Values) { d.Read.MarkGap(); d.Write.MarkGap(); d.ReadHistory.Clear(); d.WriteHistory.Clear(); }
    }

    // ───────── Sistema
    public void RefreshSystem()
    {
        if (Interlocked.Exchange(ref _sys, 1) == 1) return;
        _background(() =>
        {
            SystemSample? s = null;
            try { s = _s.System.Read(); } catch (Exception e) { _log("[hub] sistema: " + e.GetType().Name + ": " + e.Message); }
            _post(() =>
            {
                try
                {
                    if (s is not null)
                    {
                        Sample = s;
                        if (s.CpuPercent.Available) CpuHistory.Add(Math.Clamp(s.CpuPercent.Value!.Value, 0, 100));
                        if (s.GpuPercent.Available) GpuHistory.Add(Math.Clamp(s.GpuPercent.Value!.Value, 0, 100));   // sin sensor no se dibujan ceros: la gráfica no existe
                        if (s.Memory is not null) MemoryHistory.Add(s.Memory.UsedFraction * 100);
                    }

                    Changed?.Invoke();
                }
                finally { Volatile.Write(ref _sys, 0); }
            });
        });
    }

    // ───────── Red
    public void RefreshNetwork(bool withApps)
    {
        if (Interlocked.Exchange(ref _net, 1) == 1) return;
        _background(() =>
        {
            NetCounters? c = null; IReadOnlyList<NetApp>? apps = null; string? appsReason = null; var now = _clock();
            try { c = _s.Network.ReadCounters(); } catch (Exception e) { _log("[hub] red: " + e.GetType().Name + ": " + e.Message); }
            if (withApps) { try { apps = _s.Network.ReadApps(); } catch (Exception e) { appsReason = "No se pudo leer la lista de aplicaciones"; _log("[hub] aplicaciones de red: " + e.GetType().Name + ": " + e.Message); } }
            _post(() =>
            {
                try
                {
                    Traffic.Update(c ?? new NetCounters(0, 0, null, "No se pudo leer la red"), now);
                    if (withApps) { NetApps = apps ?? []; NetAppsReason = appsReason; }
                    Changed?.Invoke();
                }
                finally { Volatile.Write(ref _net, 0); }
            });
        });
    }

    // ───────── Discos
    /// <summary>Contadores de E/S (baratos) en cada llamada; el catálogo (unidades, SMART: caro) solo si pasaron 30 s o se fuerza.</summary>
    public void RefreshDisks(bool forceCatalog = false)
    {
        if (Interlocked.Exchange(ref _disk, 1) == 1) return;
        bool catalog = forceCatalog || _clock() - _lastCatalog > TimeSpan.FromSeconds(30) || Disks.Reason == "Leyendo…";
        _background(() =>
        {
            DisksSnapshot? snap = null; IReadOnlyList<DiskIo>? io = null; var now = _clock();
            if (catalog) { try { snap = _s.Disks.ReadCatalog(); } catch (Exception e) { snap = DisksSnapshot.None("No se pudo leer la información de discos"); _log("[hub] discos: " + e.GetType().Name + ": " + e.Message); } }
            try { io = _s.Disks.ReadIo(); } catch (Exception e) { _log("[hub] E/S de discos: " + e.GetType().Name + ": " + e.Message); }
            _post(() =>
            {
                try
                {
                    if (snap is not null) { Disks = snap; _lastCatalog = now; }
                    if (io is not null)
                        foreach (var d in io)
                        {
                            if (!_io.TryGetValue(d.DiskNumber, out var m)) _io[d.DiskNumber] = m = new DiskMeters();
                            m.ReadHistory.Add(m.Read.Sample(d.ReadBytes, now)); m.WriteHistory.Add(m.Write.Sample(d.WrittenBytes, now));
                        }

                    Changed?.Invoke();
                }
                finally { Volatile.Write(ref _disk, 0); }
            });
        });
    }

    /// <summary>Expulsión segura en segundo plano; el resultado vuelve al hilo de la barra y el catálogo se relee para que la unidad desaparezca (o no, si estaba en uso).</summary>
    public void Eject(string letter, Action<ServiceResult> done)
    {
        _background(() =>
        {
            ServiceResult r; try { r = _s.Disks.Eject(letter); } catch (Exception e) { r = ServiceResult.Failed(e.GetType().Name + ": " + e.Message); }
            _post(() => { done(r); RefreshDisks(forceCatalog: true); });
        });
    }
}

public enum SpeedTestStatus { Idle, Running, Done, Failed, Cancelled }
public sealed record SpeedTestState(SpeedTestStatus Status, SpeedProgress? Progress, SpeedResult? Result);

/// <summary>Una sola prueba de velocidad a la vez, solo cuando el usuario la pide, con progreso y cancelación. Nunca se lanza sola.</summary>
public sealed class SpeedTestRunner
{
    private readonly SpeedTestEngine? _engine; private readonly Action<Action> _background, _post; private CancellationTokenSource? _cts; private int _running;
    public event Action? Changed;
    public SpeedTestState State { get; private set; } = new(SpeedTestStatus.Idle, null, null);
    public bool Available => _engine is not null;

    public SpeedTestRunner(SpeedTestEngine? engine, Action<Action> background, Action<Action> post) { _engine = engine; _background = background; _post = post; }

    public bool Start()
    {
        if (_engine is null || Interlocked.Exchange(ref _running, 1) == 1) return false;
        _cts = new CancellationTokenSource(); var ct = _cts.Token; State = new SpeedTestState(SpeedTestStatus.Running, new SpeedProgress(SpeedPhase.Latency, 0, 0), null); Changed?.Invoke();
        _background(() =>
        {
            SpeedResult r;
            try { r = _engine.RunAsync(p => _post(() => { if (State.Status == SpeedTestStatus.Running) { State = State with { Progress = p }; Changed?.Invoke(); } }), ct).GetAwaiter().GetResult(); }
            catch (Exception e) { r = new SpeedResult(null, null, null, "La prueba falló: " + e.Message, false); }
            _post(() =>
            {
                State = new SpeedTestState(r.Cancelled ? SpeedTestStatus.Cancelled : r.Error is not null ? SpeedTestStatus.Failed : SpeedTestStatus.Done, null, r);
                Volatile.Write(ref _running, 0); Changed?.Invoke();
            });
        });
        return true;
    }

    public void Cancel() => _cts?.Cancel();
}
