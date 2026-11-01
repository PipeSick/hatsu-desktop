using WinMac.Bar.Hub;

namespace WinMac.Host;

public sealed record CityResolution(GeoPoint? Place, string Message);

/// <summary>
/// Convierte lo que el usuario escribe en Ajustes («Molina») en un lugar con coordenadas, buscándolo en el proveedor del tiempo. También acepta «Nombre|lat|lon» tal cual. Nunca lanza:
/// sin conexión o sin resultados devuelve un mensaje claro y NO cambia la ciudad configurada. Puro respecto de la interfaz (se prueba con un proveedor simulado).
/// </summary>
public static class CityResolver
{
    /// <summary>Minúsculas y sin tildes. Tabla propia (no depende de ICU/Normalize: con la globalización invariante, Normalize no descompone).</summary>
    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        const string from = "áàäâãåéèëêíìïîóòöôõøúùüûñçýÿ", to = "aaaaaaeeeeiiiiooooouuuuncyy"; var sb = new System.Text.StringBuilder(s.Length);
        foreach (var raw in s) { char c = char.ToLowerInvariant(raw); int i = from.IndexOf(c); sb.Append(i >= 0 ? to[i] : c); }
        return sb.ToString().Trim();
    }

    /// <summary>«Molina, Maule, Chile».</summary>
    public static string DisplayLabel(GeoPoint p) => Label(p);

    /// <summary>Lo que se busca en el proveedor: el nombre, sin la pista de país o región tras la coma.</summary>
    public static string QueryOf(string input) { input = (input ?? "").Trim(); int c = input.IndexOf(','); return (c > 0 ? input[..c] : input).Trim(); }

    /// <summary>
    /// Sugerencias mientras se escribe: con pista («Molina, Chi») solo las que la contengan; sin pista, primero las del país del equipo. Sin repetidos y como máximo <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<GeoPoint> Suggest(IReadOnlyList<GeoPoint> found, string input, string? preferCountry, int max = 5)
    {
        input = (input ?? "").Trim(); int c = input.IndexOf(','); string hint = c > 0 ? Norm(input[(c + 1)..]) : "";
        IEnumerable<GeoPoint> q = found.DistinctBy(g => g.Key);
        if (hint.Length > 0) q = q.Where(g => Norm(g.Region).Contains(hint) || Norm(g.Name).Contains(hint));
        else if (!string.IsNullOrWhiteSpace(preferCountry)) { string pc = Norm(preferCountry); q = q.OrderBy(g => Norm(g.Region).EndsWith(pc) ? 0 : 1); }   // OrderBy es estable: el resto conserva el orden del proveedor
        return q.Take(max).ToList();
    }

    private static string Label(GeoPoint p) => string.IsNullOrWhiteSpace(p.Region) ? p.Name : $"{p.Name}, {p.Region}";

    /// <summary>
    /// «Molina» → el primer resultado (el de TU país si se indica <paramref name="preferCountry"/>); «Molina, Chile» o «Molina, Maule» → el que lleve esa pista en su región/país.
    /// Con varios resultados distintos, el mensaje dice cuál se eligió y cómo pedir otro: así «Molina» ya no cae en España sin avisar.
    /// </summary>
    public static async Task<CityResolution> ResolveOneAsync(string input, IWeatherSource src, CancellationToken ct, string? preferCountry = null, bool allowHint = true)
    {
        input = (input ?? "").Trim(); if (input.Length == 0) return new CityResolution(null, "");
        if (GeoPoint.Parse(input) is { } direct) return new CityResolution(direct, direct.Name);
        string query = input, hint = ""; int comma = allowHint ? input.IndexOf(',') : -1;
        if (comma > 0) { query = input[..comma].Trim(); hint = input[(comma + 1)..].Trim(); }
        if (query.Length == 0) return new CityResolution(null, "");
        try
        {
            var found = await src.SearchAsync(query, ct).ConfigureAwait(false);
            if (found.Count == 0) return new CityResolution(null, $"No se encontró «{query}»");
            IReadOnlyList<GeoPoint> pool = found;
            if (hint.Length > 0)
            {
                string h = Norm(hint); pool = found.Where(g => Norm(g.Region).Contains(h) || Norm(g.Name).Contains(h)).ToList();
                if (pool.Count == 0) return new CityResolution(null, $"«{query}» no está en «{hint}». Hay: " + string.Join(" · ", found.Take(4).Select(Label)));
            }
            else if (!string.IsNullOrWhiteSpace(preferCountry))
            {
                string c = Norm(preferCountry); var mine = found.Where(g => Norm(g.Region).EndsWith(c)).ToList(); if (mine.Count > 0) pool = mine;
            }

            var p = pool[0]; string msg = Label(p);
            var others = found.Where(g => g.Key != p.Key).Select(g => string.IsNullOrWhiteSpace(g.Region) ? g.Name : g.Region!).Distinct().Take(2).ToList();
            if (others.Count > 0) msg += $". Otras: {string.Join(" · ", others)}. Para elegir, escribe «{query}, país»";
            return new CityResolution(p, msg);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or FormatException) { return new CityResolution(null, "No se pudo buscar la ciudad: sin conexión con el proveedor del tiempo"); }
    }

    /// <summary>Varias ciudades separadas por coma. Las que no se encuentran se reportan y se omiten; las repetidas y el exceso (máximo 5) se descartan.</summary>
    public static async Task<(IReadOnlyList<GeoPoint> Places, IReadOnlyList<string> Problems)> ResolveManyAsync(string input, IWeatherSource src, CancellationToken ct, int max = 5, string? preferCountry = null)
    {
        var places = new List<GeoPoint>(); var problems = new List<string>();
        foreach (var part in (input ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (places.Count >= max) break;
            var r = await ResolveOneAsync(part, src, ct, preferCountry, allowHint: false).ConfigureAwait(false);
            if (r.Place is null) { problems.Add(r.Message); continue; }
            if (!places.Any(p => p.Key == r.Place.Key)) places.Add(r.Place);
        }

        return (places, problems);
    }
}
