using System.Text.Json;

namespace WinMac.Dock;

/// <summary>
/// Launchpad + Dock: mientras Launchpad está abierto en el monitor de un Dock, ese Dock se mantiene visible aunque esté en «Ocultar automáticamente» (o suprimido por una
/// ventana a pantalla completa, que es lo que Launchpad es). La Barra lo avisa por el canal del host (<see cref="Topic"/>) con un latido cada ~1,5 s; si los latidos dejan de
/// llegar (la Barra se cerró o falló con Launchpad abierto) el estado caduca solo a los <see cref="TtlSeconds"/> s y el Dock recupera su comportamiento normal.
/// No toca ninguna configuración: es un estado de memoria que solo el controlador consulta.
/// </summary>
public sealed class LaunchpadPin
{
    public const string Topic = "launchpad.state";
    public const double TtlSeconds = 5;
    private double _ttl;

    public bool Active => _ttl > 0;
    /// <summary>Un mensaje de la Barra: <c>true</c> (abierto, renueva la caducidad) o <c>false</c> (cerrado: se libera de inmediato).</summary>
    public void Apply(bool open) => _ttl = open ? TtlSeconds : 0;
    public void Step(double dt) { if (_ttl > 0) _ttl = Math.Max(0, _ttl - dt); }
}

/// <summary>Estado de Launchpad que publica la Barra: abierto/cerrado y el monitor (px de pantalla) donde está.</summary>
public readonly record struct LaunchpadState(bool Open, int X, int Y, int W, int H)
{
    public static LaunchpadState? Parse(JsonElement? d)
    {
        try
        {
            if (d is not { ValueKind: JsonValueKind.Object } e) return null;
            bool open = e.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
            int Get(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;
            return new LaunchpadState(open, Get("x"), Get("y"), Get("w"), Get("h"));
        }
        catch (Exception) { return null; }
    }
}

public static class LaunchpadRouting
{
    /// <summary>
    /// A qué Dock le toca (varios monitores: uno por Dock): el que tiene el centro del monitor de Launchpad dentro de su rectángulo. Las claves son las de
    /// <see cref="DockFleet.Key"/> («izq,arr,der,aba»). null = ningún Dock en ese monitor (p. ej. el Dock de ese monitor está desactivado o aún no existe): no se muestra ninguno.
    /// </summary>
    public static string? MonitorKeyFor(LaunchpadState s, IEnumerable<string> dockKeys)
    {
        if (s.W <= 0 || s.H <= 0) return null;
        int cx = s.X + s.W / 2, cy = s.Y + s.H / 2;
        foreach (var k in dockKeys)
        {
            var p = k.Split(','); if (p.Length != 4 || !int.TryParse(p[0], out int l) || !int.TryParse(p[1], out int t) || !int.TryParse(p[2], out int r) || !int.TryParse(p[3], out int b)) continue;
            if (cx >= l && cx < r && cy >= t && cy < b) return k;
        }
        return null;
    }
}
