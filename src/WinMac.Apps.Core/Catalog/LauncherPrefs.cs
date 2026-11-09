using System.Text.Json;

namespace WinMac.Apps;

/// <summary>
/// Preferencias compartidas por Launchpad y Spotlight: el orden que el usuario dio a las apps en Launchpad, las que ocultó y cuántas veces abrió cada una (sube en la
/// búsqueda). Se guardan en <c>%LOCALAPPDATA%\WinMac\launcher.json</c>; un archivo dañado se ignora (se empieza de cero, sin romper nada).
/// </summary>
public sealed class LauncherPrefs
{
    public List<string> Order { get; set; } = [];
    public HashSet<string> Hidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Usage { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Las apps visibles en el orden de Launchpad: primero las que el usuario colocó, en su orden; después el resto, por nombre (el catálogo ya viene ordenado).</summary>
    public IReadOnlyList<InstalledApp> Arrange(IReadOnlyList<InstalledApp> apps, bool includeHidden = false)
    {
        var byId = apps.Where(a => includeHidden || !Hidden.Contains(a.Id)).GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledApp>(byId.Count); var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Order) if (byId.TryGetValue(id, out var a) && placed.Add(a.Id)) result.Add(a);
        foreach (var a in apps) if (byId.ContainsKey(a.Id) && placed.Add(a.Id)) result.Add(a);
        return result;
    }

    /// <summary>Mueve una app a la posición <paramref name="index"/> del orden visible actual (arrastrar y soltar). Fija el orden completo para que no se mueva solo después.</summary>
    public bool Move(IReadOnlyList<InstalledApp> visible, string id, int index)
    {
        var ids = visible.Select(a => a.Id).ToList(); int from = ids.FindIndex(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase));
        if (from < 0) return false;
        index = Math.Clamp(index, 0, ids.Count - 1); if (index == from) return false;
        var moving = ids[from]; ids.RemoveAt(from); ids.Insert(index, moving);
        Order = ids.Concat(Order.Where(o => !ids.Contains(o, StringComparer.OrdinalIgnoreCase))).ToList();   // conserva el sitio de las ocultas o desinstaladas por si vuelven
        return true;
    }

    public void Hide(string id) => Hidden.Add(id);
    public void UnhideAll() => Hidden.Clear();
    public void ResetOrder() => Order.Clear();
    public void RecordLaunch(string id) => Usage[id] = Math.Min(1000, (Usage.TryGetValue(id, out int n) ? n : 0) + 1);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static LauncherPrefs Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var p = JsonSerializer.Deserialize<LauncherPrefs>(File.ReadAllText(path), Json) ?? new();
            p.Order ??= []; p.Hidden = new(p.Hidden ?? [], StringComparer.OrdinalIgnoreCase); p.Usage = new(p.Usage ?? [], StringComparer.OrdinalIgnoreCase);
            p.Order = p.Order.Where(s => !string.IsNullOrWhiteSpace(s)).Take(5000).ToList();
            return p;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return new(); }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json)); File.Move(tmp, path, overwrite: true);   // escritura atómica
    }
}
