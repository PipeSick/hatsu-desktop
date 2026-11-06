namespace WinMac.Bar.Features;

public enum FeatureState { Disabled, Starting, Running, Failed, Stopping }

/// <summary>Estado de salud de un módulo (lo que Ajustes muestra). <see cref="Detail"/> = causa legible del último fallo o aviso.</summary>
public sealed record FeatureStatus(string Id, FeatureState State, string? Detail, int Faults);

/// <summary>
/// Todo lo que un módulo abre (atajos, suscripciones, ganchos, temporizadores, ventanas) se apunta aquí; al apagarlo, o si el arranque falla a medias, se libera TODO en orden
/// inverso y un error al liberar un elemento no impide liberar los demás. <see cref="Token"/> se cancela al apagar: las tareas pendientes deben mirarlo.
/// </summary>
public sealed class FeatureScope : IDisposable
{
    private readonly List<Action> _cleanup = []; private readonly object _l = new(); private readonly CancellationTokenSource _cts = new(); private readonly Action<string>? _log; private bool _disposed;
    private readonly Action<Exception>? _fault;
    public FeatureScope(string id, Action<string>? log = null, Action<Exception>? fault = null) { Id = id; _log = log; _fault = fault; Token = _cts.Token; }   // el token se copia: sigue siendo válido tras liberar el origen
    public string Id { get; }
    public CancellationToken Token { get; }
    public bool IsDisposed { get { lock (_l) return _disposed; } }

    /// <summary>Apunta un recurso. Si el módulo ya se apagó, se libera al instante (carrera apagar/arrancar).</summary>
    public T Add<T>(T resource) where T : IDisposable { Add(resource.Dispose); return resource; }
    public void Add(Action cleanup)
    {
        bool now; lock (_l) { now = _disposed; if (!now) _cleanup.Add(cleanup); }
        if (now) Run(cleanup);
    }

    /// <summary>Un fallo en tiempo de ejecución (tarea, IPC, gancho). El gestor reinicia el módulo con un límite; nunca propaga la excepción al resto de Hatsu.</summary>
    public void Fault(Exception e) { _log?.Invoke($"[{Id}] fallo: {e.GetType().Name}: {e.Message}"); _fault?.Invoke(e); }

    public void Dispose()
    {
        List<Action> list;
        lock (_l) { if (_disposed) return; _disposed = true; list = [.. _cleanup]; _cleanup.Clear(); }
        try { _cts.Cancel(); } catch (Exception) { /* nada que hacer */ }
        for (int i = list.Count - 1; i >= 0; i--) Run(list[i]);
        _cts.Dispose();
    }

    private void Run(Action a) { try { a(); } catch (Exception e) { _log?.Invoke($"[{Id}] error al liberar un recurso: {e.GetType().Name}: {e.Message}"); } }
}

/// <summary>Un módulo funcional de Hatsu que se enciende y se apaga en caliente.</summary>
public interface IHatsuFeature
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    /// <summary>Abre lo que necesita APUNTÁNDOLO en <paramref name="scope"/>. Rápido (se llama en el hilo de la barra). Una excepción aborta el arranque y se limpia lo ya abierto.</summary>
    void Start(FeatureScope scope);
    /// <summary>Ajustes cambiados con el módulo en marcha. Debe aplicarlos sin reiniciar; una excepción deja el módulo en «con aviso» pero en marcha.</summary>
    void Reconfigure(BarConfig cfg) { }
    /// <summary>Información corta para Ajustes (p. ej. «OCR: español, inglés»). Null = nada.</summary>
    string? Info() => null;
}

/// <summary>
/// Enciende, apaga y vigila los módulos. Reglas: un módulo defectuoso queda en <see cref="FeatureState.Failed"/> con su causa y NO afecta a los demás; el arranque a medias se limpia;
/// apagar libera los recursos exclusivos del módulo (los servicios compartidos los gestionan quienes los crean); un fallo en marcha reinicia el módulo hasta <see cref="MaxRestarts"/> veces.
/// </summary>
public sealed class FeatureManager : IDisposable
{
    public const int MaxRestarts = 3;
    private sealed class Entry(IHatsuFeature f, Func<BarConfig, bool> on)
    {
        public readonly IHatsuFeature Feature = f; public readonly Func<BarConfig, bool> On = on;
        public FeatureState State = FeatureState.Disabled; public string? Detail; public int Faults; public FeatureScope? Scope; public bool Warn;
    }

    private readonly List<Entry> _entries = []; private readonly object _l = new(); private readonly Action<string> _log; private BarConfig _cfg = new();
    private readonly Queue<(string Id, Exception E)> _faults = new();
    public FeatureManager(Action<string>? log = null) { _log = log ?? (_ => { }); }

    /// <summary>Se dispara cuando cambia el estado de algún módulo (fuera del cerrojo).</summary>
    public event Action? StatusChanged;

    public void Register(IHatsuFeature f, Func<BarConfig, bool> isEnabled) { lock (_l) _entries.Add(new Entry(f, isEnabled)); }

    public IReadOnlyList<FeatureStatus> Snapshot() { lock (_l) return _entries.Select(e => new FeatureStatus(e.Feature.Id, e.State, e.Detail ?? (e.State == FeatureState.Running ? SafeInfo(e) : null), e.Faults)).ToList(); }
    public FeatureState StateOf(string id) { lock (_l) return Find(id)?.State ?? FeatureState.Disabled; }
    private static string? SafeInfo(Entry e) { try { return e.Feature.Info(); } catch (Exception) { return null; } }
    private Entry? Find(string id) => _entries.FirstOrDefault(x => x.Feature.Id == id);

    /// <summary>Aplica la configuración: arranca los que se activaron, apaga los que se desactivaron, reconfigura los que siguen. Configuración inválida (excepción al evaluarla) = módulo apagado con aviso.</summary>
    public void Apply(BarConfig cfg)
    {
        List<Entry> list; lock (_l) { _cfg = cfg; list = [.. _entries]; }
        foreach (var e in list)
        {
            bool want;
            try { want = e.On(cfg); } catch (Exception ex) { want = false; Mark(e, FeatureState.Failed, "Configuración no válida: " + ex.Message); continue; }
            lock (_l)
            {
                if (want && e.State is FeatureState.Disabled) StartLocked(e, cfg);
                else if (want && e.State is FeatureState.Running)
                {
                    try { e.Feature.Reconfigure(cfg); if (e.Warn) { e.Warn = false; e.Detail = null; } }
                    catch (Exception ex) { e.Warn = true; e.Detail = "Aviso al aplicar los ajustes: " + ex.Message; _log($"[{e.Feature.Id}] {e.Detail}"); }
                }
                else if (!want && e.State is not FeatureState.Disabled) StopLocked(e, FeatureState.Disabled, null);
                else if (want && e.State is FeatureState.Failed && e.Faults < MaxRestarts && e.Detail is { } d && d.StartsWith("Configuración", StringComparison.Ordinal)) StartLocked(e, cfg);
            }
        }
        StatusChanged?.Invoke();
    }

    /// <summary>Reintento manual de un módulo en fallo (botón de Ajustes). Reinicia el contador.</summary>
    public bool Retry(string id)
    {
        lock (_l)
        {
            var e = Find(id); if (e is null || e.State != FeatureState.Failed) return false;
            bool want; try { want = e.On(_cfg); } catch (Exception) { want = false; }
            if (!want) return false;
            e.Faults = 0; StartLocked(e, _cfg);
        }
        StatusChanged?.Invoke(); return true;
    }

    /// <summary>Reintenta todos los módulos en fallo (botón «Reintentar» de Ajustes).</summary>
    public void RetryAllFailed() { string[] ids; lock (_l) ids = _entries.Where(e => e.State == FeatureState.Failed).Select(e => e.Feature.Id).ToArray(); foreach (var id in ids) Retry(id); }

    /// <summary>Atiende los fallos en marcha comunicados por los módulos (<see cref="FeatureScope.Fault"/>). Llamar desde el hilo de la barra.</summary>
    public void Pump()
    {
        bool any = false;
        while (true)
        {
            (string Id, Exception E) f; lock (_l) { if (_faults.Count == 0) break; f = _faults.Dequeue(); }
            lock (_l)
            {
                var e = Find(f.Id); if (e is null || e.State != FeatureState.Running) continue;
                e.Faults++; any = true;
                StopLocked(e, FeatureState.Failed, "Falló en marcha: " + f.E.Message);
                if (e.Faults <= MaxRestarts) { _log($"[{e.Feature.Id}] reiniciando ({e.Faults}/{MaxRestarts})"); var cfg = _cfg; StartLocked(e, cfg, keepFaults: true); }
                else e.Detail = $"Se detuvo tras {MaxRestarts} fallos: {f.E.Message}. Pulsa «Reintentar».";
            }
        }

        if (any) StatusChanged?.Invoke();
    }

    private void StartLocked(Entry e, BarConfig cfg, bool keepFaults = false)
    {
        e.State = FeatureState.Starting; e.Detail = null; e.Warn = false;
        var scope = new FeatureScope(e.Feature.Id, _log, ex => { lock (_l) _faults.Enqueue((e.Feature.Id, ex)); });
        try
        {
            e.Feature.Start(scope);
            if (scope.IsDisposed) throw new InvalidOperationException("se apagó durante el arranque");
            e.Scope = scope; e.State = FeatureState.Running; _log($"[{e.Feature.Id}] en marcha");
            try { e.Feature.Reconfigure(cfg); } catch (Exception ex) { e.Warn = true; e.Detail = "Aviso al aplicar los ajustes: " + ex.Message; }
        }
        catch (Exception ex)
        {
            scope.Dispose();                                              // arranque a medias: se libera todo lo ya abierto
            e.Scope = null; e.State = FeatureState.Failed; e.Detail = "No arrancó: " + ex.Message; if (!keepFaults) e.Faults++;
            _log($"[{e.Feature.Id}] {e.Detail}");
        }
    }

    private void StopLocked(Entry e, FeatureState final, string? detail)
    {
        e.State = FeatureState.Stopping;
        try { e.Scope?.Dispose(); } catch (Exception ex) { _log($"[{e.Feature.Id}] error al apagar: {ex.Message}"); }
        e.Scope = null; e.State = final; e.Detail = detail; e.Warn = false;
        if (final == FeatureState.Disabled) e.Faults = 0;
        _log($"[{e.Feature.Id}] {(final == FeatureState.Disabled ? "apagado" : "detenido")}");
    }

    private void Mark(Entry e, FeatureState s, string detail) { lock (_l) { if (e.State is FeatureState.Running) StopLocked(e, s, detail); else { e.State = s; e.Detail = detail; } } }

    public void Dispose() { List<Entry> list; lock (_l) list = [.. _entries]; foreach (var e in list) lock (_l) if (e.State is not FeatureState.Disabled) StopLocked(e, FeatureState.Disabled, null); }
}
