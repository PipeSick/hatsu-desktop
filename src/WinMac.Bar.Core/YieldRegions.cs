using System.Text.Json;

namespace WinMac.Bar;

/// <summary>Rectángulo en píxeles de pantalla.</summary>
public readonly record struct PxRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public bool IsEmpty => W <= 0 || H <= 0;
    public long Area => IsEmpty ? 0 : (long)W * H;
}

/// <summary>
/// Espacio que otra ventana «siempre encima» (la Isla) necesita libre dentro de la barra. La Isla lo publica en el tópico retenido <see cref="Topic"/>; la barra recorta su ventana
/// (región de ventana) para NO cubrirlo ni quedarse con sus clics, sin depender de quién esté encima en el orden Z. Lógica pura: se prueba en Linux.
/// </summary>
public static class YieldRegions
{
    public const string Topic = "notch.rects";
    public const int MaxRects = 16;

    /// <summary>Lee <c>{"rects":[{"x":..,"y":..,"w":..,"h":..}]}</c>. Tolerante: lo mal formado o de tamaño nulo se ignora; ante un mensaje ilegible, ningún hueco.</summary>
    public static IReadOnlyList<PxRect> Parse(JsonElement? data)
    {
        var list = new List<PxRect>();
        if (data is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("rects", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var r in arr.EnumerateArray())
        {
            if (list.Count >= MaxRects) break;
            if (r.ValueKind == JsonValueKind.Object && Int(r, "x") is { } x && Int(r, "y") is { } y && Int(r, "w") is { } w && Int(r, "h") is { } h && w > 0 && h > 0) list.Add(new PxRect(x, y, w, h));
        }
        return list;
    }

    private static int? Int(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : null;

    /// <summary>La ventana menos los huecos: rectángulos que no se solapan y cubren exactamente lo que queda.</summary>
    public static IReadOnlyList<PxRect> Visible(PxRect window, IEnumerable<PxRect> holes)
    {
        var pieces = new List<PxRect> { window };
        foreach (var h in holes)
        {
            if (h.IsEmpty) continue;
            var next = new List<PxRect>();
            foreach (var p in pieces)
            {
                int ix1 = Math.Max(p.X, h.X), iy1 = Math.Max(p.Y, h.Y), ix2 = Math.Min(p.Right, h.Right), iy2 = Math.Min(p.Bottom, h.Bottom);
                if (ix1 >= ix2 || iy1 >= iy2) { next.Add(p); continue; }   // no se tocan
                if (iy1 > p.Y) next.Add(new PxRect(p.X, p.Y, p.W, iy1 - p.Y));                  // franja superior
                if (iy2 < p.Bottom) next.Add(new PxRect(p.X, iy2, p.W, p.Bottom - iy2));        // franja inferior
                if (ix1 > p.X) next.Add(new PxRect(p.X, iy1, ix1 - p.X, iy2 - iy1));            // izquierda
                if (ix2 < p.Right) next.Add(new PxRect(ix2, iy1, p.Right - ix2, iy2 - iy1));    // derecha
            }
            pieces = next;
        }
        return pieces;
    }

    /// <summary>¿Alguno de los huecos toca la ventana? (si no, no hace falta recortar nada)</summary>
    public static bool Touches(PxRect window, IEnumerable<PxRect> holes) => holes.Any(h => !h.IsEmpty && Math.Max(window.X, h.X) < Math.Min(window.Right, h.Right) && Math.Max(window.Y, h.Y) < Math.Min(window.Bottom, h.Bottom));
}
