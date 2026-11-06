using WinMac.Apps;
using WinMac.Bar.Launcher;

namespace WinMac.Bar.Features;

/// <summary>Cuánta cautela exige una acción: <see cref="Normal"/> se ejecuta con Intro; <see cref="Confirm"/> pide un segundo Intro (acciones que cierran sesión, apagan, ejecutan scripts…).</summary>
public enum ActionRisk { Normal, Confirm }

public sealed record ActionResult(bool Ok, string? Error = null)
{
    public static readonly ActionResult Success = new(true);
    public static ActionResult Fail(string why) => new(false, why);
}

/// <summary>
/// Una acción de Spotlight: identificador estable (es el «destino» del resultado), nombre, descripción, módulo propietario (null = núcleo de Hatsu), riesgo, parámetros (solo informativos: nombres de lo que
/// acepta; un resultado de búsqueda NUNCA aporta parámetros ejecutables), disponibilidad en este momento y lo que hace. Una acción de un módulo apagado no está registrada: no se muestra ni se ejecuta.
/// </summary>
public sealed record SpotAction(string Id, string Title, string Description, string Keywords, string? Module, ActionRisk Risk, IReadOnlyList<string> Parameters, Func<bool>? Available, Func<ActionResult> Run);

/// <summary>
/// Registro central de acciones de Spotlight. Los módulos registran las suyas al arrancar (con su <see cref="FeatureScope"/>) y se retiran solas al apagarse. Admite proveedores dinámicos (p. ej. una acción por cada
/// distribución de Snap). Todo se ejecuta aquí dentro con aislamiento de errores: una acción que falla devuelve un resultado de error, nunca tumba Spotlight. Seguro entre hilos.
/// </summary>
public sealed class ActionRegistry
{
    private sealed record Provider(string Owner, Func<string, IEnumerable<SpotAction>> Search, Func<string, SpotAction?> Resolve);
    private readonly Dictionary<string, SpotAction> _actions = new(StringComparer.Ordinal); private readonly List<Provider> _providers = []; private readonly object _l = new();

    /// <summary>Registra una acción. Devuelve false si ese id ya existe (no se pisa la de otro módulo).</summary>
    public bool Register(SpotAction a) { lock (_l) return _actions.TryAdd(a.Id, a); }
    public bool Unregister(string id) { lock (_l) return _actions.Remove(id); }
    public int UnregisterModule(string module)
    {
        lock (_l) { int n = 0; foreach (var k in _actions.Where(x => x.Value.Module == module).Select(x => x.Key).ToList()) { _actions.Remove(k); n++; } n += _providers.RemoveAll(p => p.Owner == module); return n; }
    }

    public void RegisterProvider(string owner, Func<string, IEnumerable<SpotAction>> search, Func<string, SpotAction?> resolve) { lock (_l) _providers.Add(new Provider(owner, search, resolve)); }

    public SpotAction? Find(string id)
    {
        lock (_l) { if (_actions.TryGetValue(id, out var a)) return a; }
        Provider[] ps; lock (_l) ps = [.. _providers];
        foreach (var p in ps) { try { if (p.Resolve(id) is { } r) return r; } catch (Exception) { } }
        return null;
    }

    public IReadOnlyList<SpotAction> All { get { lock (_l) return [.. _actions.Values]; } }
    public bool IsAvailable(SpotAction a) { try { return a.Available?.Invoke() ?? true; } catch (Exception) { return false; } }

    /// <summary>Resultados de Spotlight para lo escrito: solo acciones disponibles ahora.</summary>
    public IEnumerable<(int Score, SpotItem Item)> Search(string q)
    {
        var f = AppMatcher.Fold(q.Trim()); List<SpotAction> pool; lock (_l) pool = [.. _actions.Values];
        Provider[] ps; lock (_l) ps = [.. _providers];
        foreach (var p in ps) { try { pool.AddRange(p.Search(q)); } catch (Exception) { /* un proveedor roto no oculta el resto */ } }
        foreach (var a in pool)
        {
            if (!IsAvailable(a)) continue;
            int s = AppMatcher.Score(q, a.Title);
            if (s is >= 0 and < SpotlightEngine.Weak) s = -1;
            if (s < 0 && f.Length >= 4 && a.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(k => k.StartsWith(f, StringComparison.Ordinal))) s = 320;
            if (s >= 0) yield return (s, new SpotItem(SpotKind.Action, a.Title, a.Description, a.Id) { Confirm = a.Risk == ActionRisk.Confirm });
        }
    }

    /// <summary>Ejecuta una acción por id. No existe / no disponible (módulo apagado, grabando…) / falla → resultado de error con el motivo.</summary>
    public ActionResult Execute(string id)
    {
        var a = Find(id); if (a is null) return ActionResult.Fail("La acción ya no está disponible (¿se apagó su módulo?)");
        if (!IsAvailable(a)) return ActionResult.Fail("La acción no está disponible ahora");
        try { return a.Run(); } catch (Exception e) { return ActionResult.Fail(e.GetType().Name + ": " + e.Message); }
    }
}
