namespace WinMac.Bar;

/// <summary>
/// Un monitor activo, tal como lo ve la barra. <see cref="Id"/> es estable (identifica el monitor, no su posición ni su número de pantalla), así cada barra sigue a SU monitor
/// aunque Windows reordene las pantallas. <see cref="Bounds"/> y <see cref="Work"/> están en píxeles de pantalla; <see cref="Scale"/> es el escalado DPI efectivo (1 = 100 %).
/// </summary>
public sealed record MonitorInfo(string Id, PxRect Bounds, PxRect Work, double Scale, bool Primary);

/// <summary>Diferencias entre las barras que hay y las que debería haber.</summary>
public sealed record FleetDiff(IReadOnlyList<MonitorInfo> Add, IReadOnlyList<string> Remove, IReadOnlyList<MonitorInfo> Update)
{
    public bool IsEmpty => Add.Count == 0 && Remove.Count == 0 && Update.Count == 0;
}

/// <summary>
/// Qué monitores llevan barra y qué hay que hacer cuando cambia la topología. Lógica pura (se prueba sin Windows):
/// en modo espejo (duplicar) Windows presenta la misma imagen en varias salidas, así que NO se duplica la barra; el monitor principal manda.
/// </summary>
public static class MonitorPlan
{
    /// <summary>Identificador estable: el de la ruta del dispositivo del monitor (propio de cada monitor físico); si Windows no lo da, el nombre de la salida.</summary>
    public static string MakeId(string? deviceId, string deviceName)
    {
        var s = string.IsNullOrWhiteSpace(deviceId) ? deviceName : deviceId;
        return (s ?? "").Trim();
    }

    /// <summary>Si dos monitores acabaran con el mismo identificador (mismo modelo en la misma ruta), el segundo y siguientes llevan «#2», «#3»… para no confundirlos.</summary>
    public static IReadOnlyList<string> Disambiguate(IEnumerable<string> ids)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var result = new List<string>();
        foreach (var id in ids)
        {
            seen[id] = seen.TryGetValue(id, out int n) ? n + 1 : 1;
            result.Add(seen[id] == 1 ? id : $"{id}#{seen[id]}");
        }
        return result;
    }

    /// <summary>
    /// Los monitores que llevan barra: sin espejos (mismos límites que otro ya aceptado), con el principal primero y luego de izquierda a derecha y de arriba abajo.
    /// Nunca devuelve una lista vacía si había monitores.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> Unique(IEnumerable<MonitorInfo> all)
    {
        var ordered = all.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
        var kept = new List<MonitorInfo>();
        foreach (var m in ordered)
        {
            if (m.Bounds.IsEmpty) continue;
            if (kept.Any(k => k.Bounds == m.Bounds)) continue;   // espejo: la misma imagen en otra salida
            kept.Add(m);
        }

        return kept;
    }

    /// <summary>Huella de la topología que importa a la barra. El área de trabajo NO entra: la propia barra la cambia al reservar su espacio y provocaría un bucle.</summary>
    public static string Signature(IEnumerable<MonitorInfo> monitors) =>
        string.Join(";", monitors.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => $"{m.Id}|{m.Bounds.X},{m.Bounds.Y},{m.Bounds.W},{m.Bounds.H}|{m.Scale:0.###}|{(m.Primary ? 'P' : '-')}"));

    /// <summary>Qué barras crear, cuáles liberar y cuáles recolocar (cambió resolución, posición, escalado o cuál es el principal) para pasar de <paramref name="current"/> a <paramref name="wanted"/>.</summary>
    public static FleetDiff Diff(IReadOnlyList<MonitorInfo> wanted, IReadOnlyDictionary<string, MonitorInfo> current)
    {
        var add = new List<MonitorInfo>(); var update = new List<MonitorInfo>(); var wantedIds = wanted.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var w in wanted)
        {
            if (!current.TryGetValue(w.Id, out var c)) add.Add(w);
            else if (c.Bounds != w.Bounds || Math.Abs(c.Scale - w.Scale) > 0.001 || c.Primary != w.Primary) update.Add(w);
        }

        var remove = current.Keys.Where(id => !wantedIds.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        return new FleetDiff(add, remove, update);
    }
}

/// <summary>
/// Decide CUÁNDO reconciliar las barras con los monitores. Windows avisa del cambio de pantallas (WM_DISPLAYCHANGE) antes de que la nueva disposición esté lista y puede avisar varias
/// veces; además un aviso puede perderse. Por eso solo se reconcilia cuando la huella lleva estable <c>settle</c> y ya pasó ese tiempo desde el último aviso. Puro: el reloj lo pasa quien llama.
/// </summary>
public sealed class TopologyWatcher(TimeSpan settle)
{
    private DateTime _lastNotify = DateTime.MinValue, _seenSince = DateTime.MinValue; private bool _dirty; private string? _applied, _seen;

    /// <summary>Llegó un aviso del sistema (cambio de pantallas, DPI…).</summary>
    public void Notify(DateTime now) { _dirty = true; _lastNotify = now; }

    /// <summary>Se aplicó la topología con esta huella.</summary>
    public void MarkApplied(string signature, DateTime now) { _applied = signature; _seen = signature; _seenSince = now; _dirty = false; }

    /// <summary>¿Hay que reconciliar ahora? <paramref name="signature"/> es la huella de lo que Windows reporta en este instante.</summary>
    public bool ShouldReconcile(string signature, DateTime now)
    {
        if (signature != _seen) { _seen = signature; _seenSince = now; }
        if (now - _seenSince < settle) return false;                     // todavía está cambiando
        if (_dirty && now - _lastNotify < settle) return false;          // acaba de llegar un aviso: dejar que se asiente
        if (signature == _applied) { _dirty = false; return false; }     // el aviso no cambió nada que importe a la barra
        return true;
    }
}

/// <summary>Dónde abrir un panel o menú desplegable para que quede entero dentro del monitor de la barra donde se hizo clic. Puro.</summary>
public static class PopoverPlacement
{
    /// <param name="anchorXpx">Centro del icono pulsado (px de pantalla).</param>
    /// <param name="barBottomPx">Borde inferior de la barra (px de pantalla).</param>
    /// <param name="alignRight">true = pegado al borde derecho del monitor (Control Center).</param>
    public static (int X, int Y) Place(int anchorXpx, int barBottomPx, int w, int h, PxRect monitor, double scale, bool alignRight)
    {
        int margin = (int)Math.Round(8 * scale), gap = (int)Math.Round(4 * scale);
        int minX = monitor.X + margin, maxX = Math.Max(minX, monitor.Right - w - margin);
        int x = alignRight ? maxX : Math.Clamp(anchorXpx - w / 2, minX, maxX);
        int y = barBottomPx + gap, maxY = monitor.Bottom - h - margin;
        if (y > maxY) y = Math.Max(monitor.Y, maxY);   // un panel más alto que el espacio libre sube, pero nunca sale por arriba del monitor
        return (x, y);
    }
}
