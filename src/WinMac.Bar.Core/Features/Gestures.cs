namespace WinMac.Bar.Features;

public enum HotCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Esquinas activas (pura): el puntero debe quedarse <c>delay</c> ms en la esquina FÍSICA del conjunto de pantallas (una esquina donde otro monitor continúa no cuenta: el puntero la cruza al cambiar de pantalla)
/// con ningún botón del ratón pulsado (arrastrar una ventana a una esquina no dispara nada). Dispara una vez y no vuelve a armarse hasta que el puntero se aleja de la esquina.
/// </summary>
public sealed class HotCornerEngine
{
    public const int Zone = 3, Rearm = 48;
    public readonly record struct Mon(int X, int Y, int W, int H);
    private HotCorner? _in; private int _mon = -1; private long _since; private bool _fired;

    /// <summary>Esquina física bajo el puntero, o null.</summary>
    public static (HotCorner Corner, int Monitor)? CornerAt(int x, int y, IReadOnlyList<Mon> mons)
    {
        for (int i = 0; i < mons.Count; i++)
        {
            var m = mons[i]; if (x < m.X || y < m.Y || x >= m.X + m.W || y >= m.Y + m.H) continue;
            bool left = x < m.X + Zone, right = x >= m.X + m.W - Zone, top = y < m.Y + Zone, bottom = y >= m.Y + m.H - Zone;
            HotCorner? c = (left, right, top, bottom) switch { (true, _, true, _) => HotCorner.TopLeft, (_, true, true, _) => HotCorner.TopRight, (true, _, _, true) => HotCorner.BottomLeft, (_, true, _, true) => HotCorner.BottomRight, _ => null };
            if (c is null) return null;
            int px = c is HotCorner.TopLeft or HotCorner.BottomLeft ? m.X : m.X + m.W - 1, py = c is HotCorner.TopLeft or HotCorner.TopRight ? m.Y : m.Y + m.H - 1;
            int ox = c is HotCorner.TopLeft or HotCorner.BottomLeft ? px - 1 : px + 1, oy = c is HotCorner.TopLeft or HotCorner.TopRight ? py - 1 : py + 1;
            bool Inside(int qx, int qy) { foreach (var o in mons) if (qx >= o.X && qx < o.X + o.W && qy >= o.Y && qy < o.Y + o.H) return true; return false; }
            if (Inside(ox, py) || Inside(px, oy)) return null;          // otro monitor continúa por ese lado
            return (c.Value, i);
        }

        return null;
    }

    /// <summary>Actualiza con la posición del puntero. Devuelve la esquina que se acaba de disparar (una vez), o null. <paramref name="armedMs"/> = ms que faltan si está armada y esperando (para programar la próxima evaluación), o -1.</summary>
    public HotCorner? Update(int x, int y, bool buttonsDown, IReadOnlyList<Mon> mons, long nowMs, int delayMs, Func<HotCorner, bool> assigned, out int armedMs)
    {
        armedMs = -1;
        var at = buttonsDown ? null : CornerAt(x, y, mons);
        if (at is null || !assigned(at.Value.Corner))
        {
            if (_fired && _in is { } prev && !FarFrom(prev, _mon, x, y, mons)) { return null; }   // sigue cerca de la esquina que acaba de disparar: no se rearma
            _in = null; _fired = false; _mon = -1; return null;
        }

        var (corner, mon) = at.Value;
        if (_in != corner || _mon != mon) { _in = corner; _mon = mon; _since = nowMs; _fired = false; }
        if (_fired) return null;
        long left = delayMs - (nowMs - _since);
        if (left > 0) { armedMs = (int)left; return null; }
        _fired = true; return corner;
    }

    private static bool FarFrom(HotCorner c, int mon, int x, int y, IReadOnlyList<Mon> mons)
    {
        if (mon < 0 || mon >= mons.Count) return true;
        var m = mons[mon]; int cx = c is HotCorner.TopLeft or HotCorner.BottomLeft ? m.X : m.X + m.W - 1, cy = c is HotCorner.TopLeft or HotCorner.TopRight ? m.Y : m.Y + m.H - 1;
        return Math.Abs(x - cx) > Rearm || Math.Abs(y - cy) > Rearm;
    }

    public void Reset() { _in = null; _fired = false; _mon = -1; }
}

/// <summary>Reglas de qué acciones están disponibles para gestos y atajos de Gestures (puro): una acción de un módulo apagado no se ofrece ni se ejecuta.</summary>
public static class GestureRules
{
    public static bool IsAvailable(string actionId, BarConfig cfg)
    {
        var d = GestureActions.All.FirstOrDefault(a => a.Id == actionId); if (d is null || actionId == "none") return false;
        return d.Module switch
        {
            null => true,
            FeatureConfig.MissionControl => cfg.McEnabled, FeatureConfig.Snap => cfg.SnapEnabled, FeatureConfig.LiveText => cfg.LiveTextEnabled,
            _ => false,
        };
    }

    public static string ForCorner(BarConfig c, HotCorner k) => k switch { HotCorner.TopLeft => c.GestCornerTL, HotCorner.TopRight => c.GestCornerTR, HotCorner.BottomLeft => c.GestCornerBL, _ => c.GestCornerBR };
}
