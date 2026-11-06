using System.Text.Json;
using WinMac.Bar.Capture;

namespace WinMac.Bar.Features;

/// <summary>Una zona de Snap en fracciones (0–1) del área de trabajo del monitor: independiente de la resolución y del DPI.</summary>
public readonly record struct SnapZone(double X, double Y, double W, double H)
{
    public bool IsValid => X is >= 0 and <= 1 && Y is >= 0 and <= 1 && W >= 0.05 && H >= 0.05 && X + W <= 1.0001 && Y + H <= 1.0001;
}

public sealed record SnapLayout(string Name, IReadOnlyList<SnapZone> Zones, bool Preset = false);

/// <summary>Distribuciones de Snap: las predeterminadas y las del usuario (archivo <c>snap-layouts.json</c>, versionado, tolerante a edición a mano). Puro.</summary>
public static class SnapLayouts
{
    public const int MaxLayouts = 20, MaxZones = 12;

    public static readonly IReadOnlyList<SnapLayout> Presets =
    [
        new("Mitades", [new(0, 0, 0.5, 1), new(0.5, 0, 0.5, 1)], true),
        new("Tercios", [new(0, 0, 1.0 / 3, 1), new(1.0 / 3, 0, 1.0 / 3, 1), new(2.0 / 3, 0, 1.0 / 3, 1)], true),
        new("Cuadrícula", [new(0, 0, 0.5, 0.5), new(0.5, 0, 0.5, 0.5), new(0, 0.5, 0.5, 0.5), new(0.5, 0.5, 0.5, 0.5)], true),
        new("Principal y lateral", [new(0, 0, 0.65, 1), new(0.65, 0, 0.35, 1)], true),
    ];

    public static IReadOnlyList<SnapLayout> All(IReadOnlyList<SnapLayout> custom) => [.. Presets, .. custom];
    public static SnapLayout Find(string? name, IReadOnlyList<SnapLayout> custom) => All(custom).FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];

    /// <summary>Lee las distribuciones del usuario. Lo ilegible o inválido se descarta capa a capa (nunca se pierde el resto); los nombres no pueden repetir los predeterminados.</summary>
    public static IReadOnlyList<SnapLayout> Parse(string? json)
    {
        var list = new List<SnapLayout>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json); if (!doc.RootElement.TryGetProperty("layouts", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in arr.EnumerateArray())
            {
                if (list.Count >= MaxLayouts) break;
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String || !e.TryGetProperty("zones", out var zs) || zs.ValueKind != JsonValueKind.Array) continue;
                string name = (n.GetString() ?? "").Trim(); if (name.Length is 0 or > 40) continue;
                var zones = new List<SnapZone>();
                foreach (var z in zs.EnumerateArray())
                {
                    if (zones.Count >= MaxZones || z.ValueKind != JsonValueKind.Object) break;
                    double G(string k) => z.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
                    var sz = new SnapZone(G("x"), G("y"), G("w"), G("h")); if (sz.IsValid) zones.Add(sz);
                }

                if (zones.Count == 0) continue;
                list.Add(new SnapLayout(UniqueName(name, list), zones));
            }
        }
        catch (JsonException) { return []; }
        return list;
    }

    private static string UniqueName(string name, IReadOnlyList<SnapLayout> existing)
    {
        string n = name; int i = 2;
        while (Presets.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) || existing.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))) n = $"{name} ({i++})";
        return n;
    }

    public static string Serialize(IReadOnlyList<SnapLayout> custom) => JsonSerializer.Serialize(new
    {
        version = 1,
        layouts = custom.Select(l => new { name = l.Name, zones = l.Zones.Select(z => new { x = Math.Round(z.X, 4), y = Math.Round(z.Y, 4), w = Math.Round(z.W, 4), h = Math.Round(z.H, 4) }) }),
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Añade o reemplaza una distribución del usuario; devuelve la lista nueva (con el nombre ya único).</summary>
    public static (IReadOnlyList<SnapLayout> List, string Name) Add(IReadOnlyList<SnapLayout> custom, string name, IReadOnlyList<SnapZone> zones)
    {
        var rest = custom.Where(l => !string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        string unique = UniqueName(name.Trim(), rest); rest.Add(new SnapLayout(unique, zones.Take(MaxZones).ToList()));
        return (rest.TakeLast(MaxLayouts).ToList(), unique);
    }

    public static string NextCustomName(IReadOnlyList<SnapLayout> custom) { int i = 1; while (custom.Any(l => l.Name == "Personalizada " + i)) i++; return "Personalizada " + i; }
}

public static class SnapMath
{
    /// <summary>Rectángulos en píxeles de cada zona sobre un área de trabajo, con la separación entre ventanas (mitad a cada lado de cada borde interior; los bordes de la pantalla no pierden espacio).</summary>
    public static CaptureRect[] Rects(SnapLayout layout, CaptureRect work, int gap)
    {
        var r = new CaptureRect[layout.Zones.Count];
        for (int i = 0; i < r.Length; i++)
        {
            var z = layout.Zones[i];
            int x0 = work.X + (int)Math.Round(z.X * work.W), y0 = work.Y + (int)Math.Round(z.Y * work.H), x1 = work.X + (int)Math.Round((z.X + z.W) * work.W), y1 = work.Y + (int)Math.Round((z.Y + z.H) * work.H);
            int g = Math.Max(0, gap) / 2;
            int l = z.X > 0.001 ? g : 0, t = z.Y > 0.001 ? g : 0, rr = z.X + z.W < 0.999 ? g : 0, bb = z.Y + z.H < 0.999 ? g : 0;
            r[i] = new CaptureRect(x0 + l, y0 + t, Math.Max(1, x1 - x0 - l - rr), Math.Max(1, y1 - y0 - t - bb));
        }

        return r;
    }

    public static int ZoneAt(IReadOnlyList<CaptureRect> rects, int x, int y)
    {
        int best = -1; long bestArea = long.MaxValue;
        for (int i = 0; i < rects.Count; i++) { var r = rects[i]; if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) { long a = (long)r.W * r.H; if (a < bestArea) { bestArea = a; best = i; } } }   // si se solapan, la más pequeña
        return best;
    }

    public static int Next(int count, int current) => count <= 0 ? -1 : (current + 1 + count) % count;

    /// <summary>Windows dibuja las ventanas con un borde invisible de redimensión: para que el borde VISIBLE llene la zona, el rectángulo que se pide es mayor en ese borde.</summary>
    public static CaptureRect Compensate(CaptureRect visible, int leftInset, int topInset, int rightInset, int bottomInset) =>
        new(visible.X - leftInset, visible.Y - topInset, visible.W + leftInset + rightInset, visible.H + topInset + bottomInset);

    /// <summary>Zona nueva arrastrada en el editor (píxeles → fracciones, pegada a una rejilla de <paramref name="grid"/> divisiones). Null si es demasiado pequeña.</summary>
    public static SnapZone? FromDrag(double x0, double y0, double x1, double y1, double width, double height, int grid = 24)
    {
        if (width <= 0 || height <= 0) return null;
        double Snap(double v, double size) => Math.Round(Math.Clamp(v / size, 0, 1) * grid) / grid;
        double ax = Snap(Math.Min(x0, x1), width), bx = Snap(Math.Max(x0, x1), width), ay = Snap(Math.Min(y0, y1), height), by = Snap(Math.Max(y0, y1), height);
        var z = new SnapZone(ax, ay, bx - ax, by - ay); return z.IsValid ? z : null;
    }
}
