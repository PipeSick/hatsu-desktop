namespace WinMac.Dock;

/// <summary>Evita lanzar la misma app varias veces por clics repetidos mientras todavía arranca (aún sin ventanas). Se libera pasado el plazo.</summary>
public sealed class LaunchGuard
{
    private readonly TimeSpan _window; private readonly Func<DateTime> _now; private readonly Dictionary<string, DateTime> _last = new();
    public LaunchGuard(TimeSpan? window = null, Func<DateTime>? clock = null) { _window = window ?? TimeSpan.FromSeconds(4); _now = clock ?? (() => DateTime.UtcNow); }

    public bool TryBegin(string id)
    {
        var t = _now(); if (_last.TryGetValue(id, out var prev) && t - prev < _window) return false;
        _last[id] = t; return true;
    }

    /// <summary>La app ya tiene ventanas: se puede volver a lanzar si se cierra.</summary>
    public void Clear(string id) => _last.Remove(id);
}
