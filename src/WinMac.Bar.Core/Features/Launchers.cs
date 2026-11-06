using System.Text.Json;

namespace WinMac.Bar.Features;

/// <summary>Un acceso rápido de Gestures: abrir una aplicación instalada o una página web. <see cref="Target"/>: para una app, su Id del menú Inicio/AUMID (se abre como <c>shell:AppsFolder\Id</c>); para una página, la URL http(s).</summary>
public sealed record GestLauncher(string Id, string Name, string Kind, string Target)
{
    public const string App = "app", Url = "url";
    public string ActionId => Launchers.Prefix + Id;
}

/// <summary>Accesos rápidos del usuario (puro): lectura/escritura saneada del JSON guardado en la configuración y reglas de qué se puede abrir. Máximo <see cref="Max"/>.</summary>
public static class Launchers
{
    public const string Prefix = "launch:";
    public const int Max = 6;

    public static bool IsSafeUrl(string? s, out Uri? uri)
    {
        uri = null; if (string.IsNullOrWhiteSpace(s) || s.Length > 500) return false;
        return Uri.TryCreate(s.Trim(), UriKind.Absolute, out uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>Normaliza lo que escribe el usuario: una dirección sin esquema se completa con «https://». Null si no es una página web válida.</summary>
    public static string? NormalizeUrl(string? text)
    {
        var t = (text ?? "").Trim(); if (t.Length == 0 || t.Contains(' ')) return null;
        if (!t.Contains("://", StringComparison.Ordinal)) t = "https://" + t;
        return IsSafeUrl(t, out var u) && u!.Host.Contains('.') ? u.ToString() : null;
    }

    private static bool Valid(GestLauncher l) =>
        l.Id.Length is >= 1 and <= 12 && l.Id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9') && l.Name.Trim().Length is >= 1 and <= 60 &&
        (l.Kind == GestLauncher.Url ? IsSafeUrl(l.Target, out _) : l.Kind == GestLauncher.App && l.Target.Length is >= 1 and <= 400 && !l.Target.Any(c => c < 32 || c == '"'));

    public static IReadOnlyList<GestLauncher> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var list = new List<GestLauncher>();
            foreach (var e in JsonDocument.Parse(json).RootElement.EnumerateArray())
            {
                string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var l = new GestLauncher(S("id"), S("name").Trim(), S("kind"), S("target").Trim());
                if (Valid(l) && !list.Any(x => x.Id == l.Id)) list.Add(l);
                if (list.Count >= Max) break;
            }

            return list;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return []; }
    }

    public static string Serialize(IReadOnlyList<GestLauncher> list) => list.Count == 0 ? "" : JsonSerializer.Serialize(list.Select(l => new { id = l.Id, name = l.Name, kind = l.Kind, target = l.Target }));

    /// <summary>Agrega un acceso. Devuelve la lista nueva y su Id; o null si no cabe (máximo) o no es válido.</summary>
    public static (IReadOnlyList<GestLauncher> List, GestLauncher Added)? Add(IReadOnlyList<GestLauncher> list, string name, string kind, string target)
    {
        if (list.Count >= Max) return null;
        if (list.Any(l => l.Kind == kind && string.Equals(l.Target, target, StringComparison.OrdinalIgnoreCase))) return null;
        string id; int n = 1; do id = "l" + (n++); while (list.Any(l => l.Id == id));
        var item = new GestLauncher(id, name.Trim(), kind, target.Trim()); if (!Valid(item)) return null;
        return ([.. list, item], item);
    }

    public static IReadOnlyList<GestLauncher> Remove(IReadOnlyList<GestLauncher> list, string id) => list.Where(l => l.Id != id).ToList();

    public static GestLauncher? Find(BarConfig cfg, string actionId) =>
        actionId.StartsWith(Prefix, StringComparison.Ordinal) ? Parse(cfg.GestLaunchers).FirstOrDefault(l => l.Id == actionId[Prefix.Length..]) : null;

    public static string DisplayName(GestLauncher l) => (l.Kind == GestLauncher.Url ? "Abrir página: " : "Abrir: ") + l.Name;

    /// <summary>Nombre de una página para mostrar: el host sin «www.».</summary>
    public static string SiteName(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host) : url;
}
