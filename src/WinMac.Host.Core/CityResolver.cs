using WinMac.Bar.Hub;

namespace WinMac.Host;

public sealed record CityResolution(GeoPoint? Place, string Message);

/// <summary>
/// Convierte lo que el usuario escribe en Ajustes («Molina») en un lugar con coordenadas, buscándolo en el proveedor del tiempo. También acepta «Nombre|lat|lon» tal cual. Nunca lanza:
/// sin conexión o sin resultados devuelve un mensaje claro y NO cambia la ciudad configurada. Puro respecto de la interfaz (se prueba con un proveedor simulado).
/// </summary>
public static class CityResolver
{
    public static async Task<CityResolution> ResolveOneAsync(string input, IWeatherSource src, CancellationToken ct)
    {
        input = (input ?? "").Trim(); if (input.Length == 0) return new CityResolution(null, "");
        if (GeoPoint.Parse(input) is { } direct) return new CityResolution(direct, direct.Name);
        try
        {
            var found = await src.SearchAsync(input, ct).ConfigureAwait(false);
            if (found.Count == 0) return new CityResolution(null, $"No se encontró «{input}»");
            var p = found[0]; return new CityResolution(p, string.IsNullOrWhiteSpace(p.Region) ? p.Name : $"{p.Name}, {p.Region}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or FormatException) { return new CityResolution(null, "No se pudo buscar la ciudad: sin conexión con el proveedor del tiempo"); }
    }

    /// <summary>Varias ciudades separadas por coma. Las que no se encuentran se reportan y se omiten; las repetidas y el exceso (máximo 5) se descartan.</summary>
    public static async Task<(IReadOnlyList<GeoPoint> Places, IReadOnlyList<string> Problems)> ResolveManyAsync(string input, IWeatherSource src, CancellationToken ct, int max = 5)
    {
        var places = new List<GeoPoint>(); var problems = new List<string>();
        foreach (var part in (input ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (places.Count >= max) break;
            var r = await ResolveOneAsync(part, src, ct).ConfigureAwait(false);
            if (r.Place is null) { problems.Add(r.Message); continue; }
            if (!places.Any(p => p.Key == r.Place.Key)) places.Add(r.Place);
        }

        return (places, problems);
    }
}
