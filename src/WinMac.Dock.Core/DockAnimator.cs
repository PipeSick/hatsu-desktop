namespace WinMac.Dock;

/// <summary>Resortes por ítem (escala, salto de lanzamiento) y resorte global de ocultamiento. Indexado por Id para sobrevivir a cambios de modelo.</summary>
public sealed class DockAnimator
{
    private readonly DockMetrics _m;
    private readonly Dictionary<string, Spring> _scale = new();
    private readonly Dictionary<string, Spring> _lift = new();
    private Spring _hide = new(0, 0, 260, 0.9);
    private bool _first = true;

    public DockAnimator(DockMetrics m) { _m = m; }

    public double HideT => _hide.Value;

    /// <summary>Alinea los resortes con la lista de ítems. Los nuevos aparecen creciendo desde 0 (salvo la primera carga).</summary>
    public void Sync(IReadOnlyList<DockItemModel> items)
    {
        var ids = new HashSet<string>(items.Select(i => i.Id));
        foreach (var k in _scale.Keys.Where(k => !ids.Contains(k)).ToList()) { _scale.Remove(k); _lift.Remove(k); }
        foreach (var it in items)
        {
            if (it.Kind == DockItemKind.Separator || _scale.ContainsKey(it.Id)) continue;
            _scale[it.Id] = new Spring(_first ? 1 : 0, 1);
            _lift[it.Id] = new Spring(0, 0, 160, 0.28);
        }
        _first = false;
    }

    public void SetTargets(IReadOnlyList<DockItemModel> items, IReadOnlyList<double> targets)
    {
        for (int i = 0; i < items.Count; i++)
            if (_scale.TryGetValue(items[i].Id, out var s)) { s.Target = targets[i]; _scale[items[i].Id] = s; }
    }

    public void SetHideTarget(double t) { _hide.Target = t; }

    /// <summary>Salto de «lanzamiento» como en macOS: impulso hacia arriba que rebota.</summary>
    public void Bounce(string id)
    {
        if (_lift.TryGetValue(id, out var s)) { s.Velocity = 380; _lift[id] = s; }
    }

    public void Step(double dt)
    {
        foreach (var k in _scale.Keys.ToList()) { var s = _scale[k]; s.Step(dt); _scale[k] = s; }
        foreach (var k in _lift.Keys.ToList())
        {
            var s = _lift[k]; s.Step(dt);
            if (s.Value < 0) { s.Value = 0; if (s.Velocity < 0) s.Velocity *= -0.35; } // rebota contra el «suelo»
            if (s.Value > _m.BounceHeight) { s.Value = _m.BounceHeight; if (s.Velocity > 0) s.Velocity = 0; }
            _lift[k] = s;
        }
        _hide.Step(dt);
    }

    public double Scale(string id) => _scale.TryGetValue(id, out var s) ? Math.Max(0, s.Value) : 1;
    public double Lift(string id) => _lift.TryGetValue(id, out var s) ? s.Value : 0;
    public double[] Scales(IReadOnlyList<DockItemModel> items) => items.Select(i => i.Kind == DockItemKind.Separator ? 1 : Scale(i.Id)).ToArray();

    public bool IsSettled => _hide.IsSettled && _scale.Values.All(s => s.IsSettled) && _lift.Values.All(s => s.IsSettled);
}
