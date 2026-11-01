using System.Globalization;
using System.Text.Json;

namespace WinMac.Bar.Hub;

/// <summary>Un lugar. Se guarda como «Nombre|lat|lon» (cultura invariante) en la configuración central; varios, separados por «;».</summary>
public sealed record GeoPoint(string Name, string? Region, double Lat, double Lon)
{
    /// <summary>Clave estable para saber si dos lugares son el mismo (≈110 m): evita mostrar los datos de una ciudad como si fueran de otra.</summary>
    public string Key => $"{Lat.ToString("0.000", CultureInfo.InvariantCulture)},{Lon.ToString("0.000", CultureInfo.InvariantCulture)}";
    public string Serialize() => $"{Name.Replace("|", " ").Replace(";", " ")}|{Lat.ToString("0.####", CultureInfo.InvariantCulture)}|{Lon.ToString("0.####", CultureInfo.InvariantCulture)}";

    public static GeoPoint? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null; var p = s.Split('|');
        if (p.Length != 3 || string.IsNullOrWhiteSpace(p[0])) return null;
        if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180) return null;
        return new GeoPoint(p[0].Trim(), null, lat, lon);
    }

    public static IReadOnlyList<GeoPoint> ParseList(string? s, int max = 5) =>
        (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Parse).OfType<GeoPoint>().DistinctBy(g => g.Key).Take(max).ToList();

    public static string SerializeList(IEnumerable<GeoPoint> list) => string.Join(";", list.Select(g => g.Serialize()));
}

public sealed record HourlyPoint(DateTime Time, double TempC, int Code, bool IsDay, int? PrecipProb);
public sealed record SunEvent(DateTime Time, bool IsRise);
public sealed record CityBrief(GeoPoint Place, double TempC, int Code, bool IsDay);

/// <summary>Tiempo de un lugar. Todas las horas son LOCALES del lugar (así las entrega el proveedor con timezone=auto), no las del equipo.</summary>
public sealed record CityWeather(GeoPoint Place, DateTime LocalNow, double TempC, int Code, bool IsDay, double? HighC, double? LowC, IReadOnlyList<HourlyPoint> Hourly, IReadOnlyList<SunEvent> Sun);

public sealed record WeatherSnapshot(CityWeather Main, IReadOnlyList<CityBrief> Others, DateTime FetchedAtUtc);

/// <summary>Códigos WMO (los que usa Open-Meteo) → descripción en español y clave de icono.</summary>
public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Despejado", 1 => "Mayormente despejado", 2 => "Parcialmente nublado", 3 => "Mayormente nublado",
        45 or 48 => "Niebla", 51 or 53 or 55 => "Llovizna", 56 or 57 => "Llovizna helada",
        61 => "Lluvia ligera", 63 => "Lluvia", 65 => "Lluvia fuerte", 66 or 67 => "Lluvia helada",
        71 => "Nevada ligera", 73 => "Nevada", 75 => "Nevada fuerte", 77 => "Granos de nieve",
        80 => "Chubascos ligeros", 81 => "Chubascos", 82 => "Chubascos fuertes", 85 or 86 => "Chubascos de nieve",
        95 => "Tormenta", 96 or 99 => "Tormenta con granizo", _ => "Sin datos",
    };

    /// <summary>Clave de icono que dibuja el panel: clear, clear-night, partly, partly-night, cloudy, fog, drizzle, rain, snow, thunder.</summary>
    public static string Icon(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "clear" : "clear-night",
        2 => isDay ? "partly" : "partly-night",
        3 => "cloudy", 45 or 48 => "fog", 51 or 53 or 55 or 56 or 57 => "drizzle",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "rain", 71 or 73 or 75 or 77 or 85 or 86 => "snow", 95 or 96 or 99 => "thunder",
        _ => "cloudy",
    };
}

public static class TempFormat
{
    private static int Round(double c, bool f) { double v = f ? c * 9 / 5 + 32 : c; int r = (int)Math.Round(v, MidpointRounding.AwayFromZero); return r == 0 ? 0 : r; }
    /// <summary>«12°» (sin letra, como en el panel de referencia).</summary>
    public static string Degrees(double c, bool fahrenheit) => $"{Round(c, fahrenheit)}°";
    /// <summary>«12°C» (como en la barra).</summary>
    public static string Bar(double c, bool fahrenheit) => $"{Round(c, fahrenheit)}°{(fahrenheit ? 'F' : 'C')}";
}

/// <summary>Una casilla de la tira horizontal de pronóstico: una hora o el orto/ocaso.</summary>
public sealed record StripItem(string Label, string IconKey, string Text, int? PrecipProb, bool IsSun);

public static class WeatherStrip
{
    /// <summary>
    /// La tira de la referencia: las próximas horas desde la hora en curso, con la salida/puesta del sol intercaladas en su lugar («6 a. m. · 6:42 a. m. salida del sol · 7 a. m. …»).
    /// La probabilidad de precipitación solo se muestra si es de al menos 10 %.
    /// </summary>
    public static IReadOnlyList<StripItem> Build(CityWeather w, bool fahrenheit, bool use24h, int slots = 5)
    {
        var start = new DateTime(w.LocalNow.Year, w.LocalNow.Month, w.LocalNow.Day, w.LocalNow.Hour, 0, 0);
        var entries = new List<(DateTime At, StripItem Item)>();
        foreach (var h in w.Hourly.Where(h => h.Time >= start).OrderBy(h => h.Time))
            entries.Add((h.Time, new StripItem(HourLabel(h.Time, use24h, false), WeatherCodes.Icon(h.Code, h.IsDay), TempFormat.Degrees(h.TempC, fahrenheit), h.PrecipProb is >= 10 ? h.PrecipProb : null, false)));
        foreach (var s in w.Sun.Where(s => s.Time >= start))
            entries.Add((s.Time, new StripItem(HourLabel(s.Time, use24h, true), s.IsRise ? "sunrise" : "sunset", s.IsRise ? "Salida del sol" : "Puesta del sol", null, true)));
        return entries.OrderBy(e => e.At).Take(slots).Select(e => e.Item).ToList();
    }

    public static string HourLabel(DateTime t, bool use24h, bool withMinutes)
    {
        if (use24h) return withMinutes || t.Minute != 0 ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : t.ToString("HH", CultureInfo.InvariantCulture) + ":00";
        string ampm = t.Hour < 12 ? "a. m." : "p. m."; int h12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return withMinutes || t.Minute != 0 ? $"{h12}:{t.Minute:00} {ampm}" : $"{h12} {ampm}";
    }
}

/// <summary>Lee las respuestas JSON de Open-Meteo (pronóstico y búsqueda de lugares). Tolerante: un campo ausente no rompe el resto; una respuesta ilegible lanza <see cref="FormatException"/>.</summary>
public static class OpenMeteoParser
{
    public static CityWeather ParseForecast(string json, GeoPoint place)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True) throw new FormatException(root.TryGetProperty("reason", out var r) ? r.GetString() : "el proveedor devolvió un error");
            var cur = root.GetProperty("current");
            double temp = cur.GetProperty("temperature_2m").GetDouble(); int code = cur.GetProperty("weather_code").GetInt32(); bool isDay = cur.TryGetProperty("is_day", out var d) && d.GetInt32() == 1;
            var now = ParseTime(cur.GetProperty("time").GetString());
            var hourly = new List<HourlyPoint>();
            if (root.TryGetProperty("hourly", out var h))
            {
                var times = h.GetProperty("time").EnumerateArray().Select(e => ParseTime(e.GetString())).ToList();
                var temps = Doubles(h, "temperature_2m"); var codes = Ints(h, "weather_code"); var days = Ints(h, "is_day"); var prec = Ints(h, "precipitation_probability");
                for (int i = 0; i < times.Count; i++)
                {
                    if (i >= temps.Count || temps[i] is null) continue;
                    hourly.Add(new HourlyPoint(times[i], temps[i]!.Value, i < codes.Count && codes[i] is { } c ? c : 0, i < days.Count ? days[i] == 1 : true, i < prec.Count ? prec[i] : null));
                }
            }

            double? hi = null, lo = null; var sun = new List<SunEvent>();
            if (root.TryGetProperty("daily", out var dly))
            {
                var dTimes = dly.GetProperty("time").EnumerateArray().Select(e => ParseTime(e.GetString())).ToList(); var his = Doubles(dly, "temperature_2m_max"); var los = Doubles(dly, "temperature_2m_min");
                int today = dTimes.FindIndex(t => t.Date == now.Date); if (today < 0) today = 0;
                if (today < his.Count) hi = his[today]; if (today < los.Count) lo = los[today];
                foreach (var (name, rise) in new[] { ("sunrise", true), ("sunset", false) })
                    if (dly.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var e in arr.EnumerateArray()) if (e.ValueKind == JsonValueKind.String && TryParseTime(e.GetString(), out var t)) sun.Add(new SunEvent(t, rise));
            }

            return new CityWeather(place, now, temp, code, isDay, hi, lo, hourly, sun.OrderBy(s => s.Time).ToList());
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException or FormatException)
        { throw e as FormatException ?? new FormatException("Respuesta del proveedor del tiempo ilegible: " + e.Message, e); }
    }

    public static CityBrief ParseCurrent(string json, GeoPoint place)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var cur = doc.RootElement.GetProperty("current");
            return new CityBrief(place, cur.GetProperty("temperature_2m").GetDouble(), cur.GetProperty("weather_code").GetInt32(), cur.TryGetProperty("is_day", out var d) && d.GetInt32() == 1);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException)
        { throw new FormatException("Respuesta del proveedor del tiempo ilegible: " + e.Message, e); }
    }

    public static IReadOnlyList<GeoPoint> ParseGeocoding(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var list = new List<GeoPoint>();
            if (!doc.RootElement.TryGetProperty("results", out var res) || res.ValueKind != JsonValueKind.Array) return list;
            foreach (var r in res.EnumerateArray())
            {
                if (!r.TryGetProperty("name", out var n) || !r.TryGetProperty("latitude", out var la) || !r.TryGetProperty("longitude", out var lo)) continue;
                string? region = r.TryGetProperty("admin1", out var a) ? a.GetString() : null, country = r.TryGetProperty("country", out var c) ? c.GetString() : null;
                list.Add(new GeoPoint(n.GetString() ?? "", string.Join(", ", new[] { region, country }.Where(x => !string.IsNullOrWhiteSpace(x))), la.GetDouble(), lo.GetDouble()));
            }

            return list;
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { throw new FormatException("Respuesta de búsqueda de lugares ilegible: " + e.Message, e); }
    }

    private static DateTime ParseTime(string? s) => TryParseTime(s, out var t) ? t : throw new FormatException("hora ilegible: " + s);
    private static bool TryParseTime(string? s, out DateTime t) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out t);   // hora local del LUGAR, sin zona: se conserva tal cual (Kind sin especificar)
    private static List<double?> Doubles(JsonElement o, string name) => o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? (double?)e.GetDouble() : null).ToList() : [];
    private static List<int?> Ints(JsonElement o, string name) => o.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.Number ? (int?)(int)Math.Round(e.GetDouble()) : null).ToList() : [];
}

/// <summary>De dónde salen los datos (Open-Meteo en producción; un doble en las pruebas).</summary>
public interface IWeatherSource
{
    Task<WeatherSnapshot> FetchAsync(GeoPoint place, IReadOnlyList<GeoPoint> others, CancellationToken ct);
    Task<IReadOnlyList<GeoPoint>> SearchAsync(string name, CancellationToken ct);
}

/// <summary>Open-Meteo: gratuito, sin clave y sin registro; HTTPS. La petición solo lleva coordenadas (no datos personales).</summary>
public sealed class OpenMeteoSource(HttpClient http) : IWeatherSource
{
    private const string Forecast = "https://api.open-meteo.com/v1/forecast", Geocoding = "https://geocoding-api.open-meteo.com/v1/search";
    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    public async Task<WeatherSnapshot> FetchAsync(GeoPoint place, IReadOnlyList<GeoPoint> others, CancellationToken ct)
    {
        string main = $"{Forecast}?latitude={N(place.Lat)}&longitude={N(place.Lon)}&current=temperature_2m,weather_code,is_day&hourly=temperature_2m,weather_code,is_day,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=2";
        var city = OpenMeteoParser.ParseForecast(await GetAsync(main, ct).ConfigureAwait(false), place);
        var briefs = new List<CityBrief>();
        foreach (var o in others)
        {
            try { briefs.Add(OpenMeteoParser.ParseCurrent(await GetAsync($"{Forecast}?latitude={N(o.Lat)}&longitude={N(o.Lon)}&current=temperature_2m,weather_code,is_day&timezone=auto", ct).ConfigureAwait(false), o)); }
            catch (Exception e) when (e is HttpRequestException or FormatException) { /* una ciudad adicional que falla no tumba el panel: se omite esta vez */ }
        }

        return new WeatherSnapshot(city, briefs, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<GeoPoint>> SearchAsync(string name, CancellationToken ct) =>
        OpenMeteoParser.ParseGeocoding(await GetAsync($"{Geocoding}?name={Uri.EscapeDataString(name)}&count=6&language=es&format=json", ct).ConfigureAwait(false));

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}

public enum WeatherStatus { NoCity, Loading, Ok, Stale, Offline, Error }

/// <summary>Lo que el panel dibuja: datos (si los hay), estado y un mensaje. Con datos antiguos y sin conexión se SIGUEN mostrando, marcados como desactualizados.</summary>
public sealed record WeatherState(WeatherStatus Status, WeatherSnapshot? Snapshot, string? Message, DateTime? FetchedAtUtc)
{
    public static WeatherState NoCity { get; } = new(WeatherStatus.NoCity, null, "Elige una ciudad en Ajustes", null);
}

/// <summary>Última respuesta guardada en disco (sobrevive a reinicios; sin conexión se sigue mostrando, marcada como desactualizada).</summary>
public sealed class WeatherCache(string? path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private WeatherSnapshot? _mem;

    public WeatherSnapshot? Get(string placeKey)
    {
        if (_mem is null && path is not null && File.Exists(path)) { try { _mem = JsonSerializer.Deserialize<WeatherSnapshot>(File.ReadAllText(path), Json); } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _mem = null; } }
        return _mem is not null && _mem.Main.Place.Key == placeKey ? _mem : null;   // datos de OTRA ciudad nunca se presentan como de esta
    }

    public void Put(WeatherSnapshot s)
    {
        _mem = s; if (path is null) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json)); File.Move(tmp, path, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* la caché es una comodidad */ }
    }
}

/// <summary>
/// Orquesta el tiempo SIN bloquear la barra: la petición va a segundo plano y de un solo vuelo, se actualiza como mucho cada 15 minutos (o al forzarlo), reintenta con espera creciente tras un
/// fallo (1, 2, 4… hasta 30 min), conserva y marca como desactualizados los últimos datos, y nunca presenta los de otra ciudad.
/// </summary>
public sealed class WeatherService
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15), StaleAfter = TimeSpan.FromMinutes(60);
    private readonly IWeatherSource _source; private readonly WeatherCache _cache; private readonly Func<DateTime> _clock; private readonly Action<Action> _background, _post; private readonly Action<string> _log;
    private GeoPoint? _place; private IReadOnlyList<GeoPoint> _others = []; private WeatherSnapshot? _snap; private int _inflight, _failures; private bool _dirty; private DateTime _nextTry = DateTime.MinValue; private string? _lastError; private bool _lastWasOffline;
    public event Action? Changed;   // en el hilo de la barra

    public WeatherService(IWeatherSource source, WeatherCache cache, Func<DateTime> clock, Action<Action> background, Action<Action> post, Action<string> log)
    { _source = source; _cache = cache; _clock = clock; _background = background; _post = post; _log = log; }

    /// <summary>Define la ciudad y las adicionales. Cambiar de ciudad descarta los datos de la anterior (se usa la caché solo si es de la misma).</summary>
    public void Configure(GeoPoint? place, IReadOnlyList<GeoPoint> others)
    {
        bool cityChanged = place?.Key != _place?.Key; bool othersChanged = !others.Select(o => o.Key).SequenceEqual(_others.Select(o => o.Key));
        _place = place; _others = others;
        if (cityChanged) { _snap = place is null ? null : _cache.Get(place.Key); _failures = 0; _nextTry = DateTime.MinValue; _lastError = null; }
        if (othersChanged && _snap is not null) { _nextTry = DateTime.MinValue; _dirty = true; }   // las adicionales cambiaron: se vuelve a pedir aunque los datos actuales sean recientes
        if (cityChanged || othersChanged) _post(() => Changed?.Invoke());
    }

    public WeatherState Current()
    {
        if (_place is null) return WeatherState.NoCity;
        var now = _clock();
        if (_snap is null) return Volatile.Read(ref _inflight) == 1 || _lastError is null ? new WeatherState(WeatherStatus.Loading, null, "Cargando el tiempo…", null)
            : new WeatherState(_lastWasOffline ? WeatherStatus.Offline : WeatherStatus.Error, null, _lastError, null);
        var age = now.ToUniversalTime() - _snap.FetchedAtUtc;
        if (_lastError is not null) return new WeatherState(_lastWasOffline ? WeatherStatus.Offline : WeatherStatus.Stale, _snap, _lastError, _snap.FetchedAtUtc);
        if (age >= StaleAfter) return new WeatherState(WeatherStatus.Stale, _snap, "Datos desactualizados", _snap.FetchedAtUtc);
        return new WeatherState(WeatherStatus.Ok, _snap, null, _snap.FetchedAtUtc);
    }

    /// <summary>Pide datos si hacen falta (no hay, están viejos o se fuerza) y no estamos en espera por un fallo. Nunca bloquea al que llama.</summary>
    public void Refresh(bool force = false)
    {
        if (_place is null) return; var place = _place; var others = _others; var now = _clock();
        if (!force)
        {
            if (now < _nextTry) return;
            if (_snap is not null && _lastError is null && !_dirty && now.ToUniversalTime() - _snap.FetchedAtUtc < FreshFor) return;
        }

        if (Interlocked.Exchange(ref _inflight, 1) == 1) return;
        _background(() =>
        {
            WeatherSnapshot? ok = null; string? error = null; bool offline = false;
            try { using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)); ok = _source.FetchAsync(place, others, cts.Token).GetAwaiter().GetResult(); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or System.Net.Sockets.SocketException)
            { offline = true; error = e is HttpRequestException { StatusCode: not null } ? $"El proveedor respondió con error ({(int)((HttpRequestException)e).StatusCode!})" : "Sin conexión con el proveedor del tiempo"; offline = e is not HttpRequestException { StatusCode: not null }; _log("[tiempo] " + e.GetType().Name + ": " + e.Message); }
            catch (Exception e) { error = "No se pudo leer la respuesta del proveedor del tiempo"; _log("[tiempo] " + e.GetType().Name + ": " + e.Message); }
            _post(() =>
            {
                if (place.Key != _place?.Key) { Volatile.Write(ref _inflight, 0); return; }   // la ciudad cambió mientras se pedía: este resultado ya no corresponde
                if (ok is not null) { ok = ok with { FetchedAtUtc = _clock().ToUniversalTime() }; _snap = ok; _cache.Put(ok);   // la frescura la marca ESTE reloj, no el del proveedor
                     _failures = 0; _lastError = null; _dirty = false; _nextTry = DateTime.MinValue; }
                else { _failures++; _lastError = error; _lastWasOffline = offline; _nextTry = _clock() + Backoff(_failures); }
                Volatile.Write(ref _inflight, 0); Changed?.Invoke();
            });
        });
    }

    public static TimeSpan Backoff(int failures) => TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, Math.Max(0, failures - 1))));
}
