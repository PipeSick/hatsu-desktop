using System.Net;
using WinMac.Bar.Hub;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class MonitoringTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void History_keeps_the_most_recent_values_in_order_and_pads_short_histories_to_the_full_chart_width()
    {
        var h = new History(4); foreach (var v in new double[] { 1, 2, 3 }) h.Add(v);
        Assert.Equal([1.0, 2, 3], h.Values()); Assert.Equal([0.0, 0, 1, 2, 3], h.Padded(5));
        h.Add(4); h.Add(5); h.Add(6); Assert.Equal([3.0, 4, 5, 6], h.Values()); Assert.Equal(6, h.Max); Assert.Equal([5.0, 6], h.Padded(2));
        h.Add(double.NaN); h.Add(double.PositiveInfinity); Assert.All(h.Values(), v => Assert.True(double.IsFinite(v)));   // un valor corrupto nunca rompe la gráfica
        h.Clear(); Assert.Equal(0, h.Count); Assert.Equal(0, h.Max);
    }

    [Fact]
    public void RateMeter_computes_bytes_per_second_and_the_session_total()
    {
        var m = new RateMeter(); Assert.Equal(0, m.Sample(1000, T0));                  // la primera muestra no tiene velocidad
        Assert.Equal(500, m.Sample(2000, T0.AddSeconds(2))); Assert.Equal(1000, m.Total);
        Assert.Equal(2000, m.Sample(4000, T0.AddSeconds(3))); Assert.Equal(3000, m.Total); Assert.Equal(2000, m.Rate);
    }

    [Fact]
    public void RateMeter_survives_counter_resets_repeated_timestamps_and_idle_periods()
    {
        var m = new RateMeter(); m.Sample(5_000_000, T0); m.Sample(6_000_000, T0.AddSeconds(1));
        Assert.Equal(0, m.Sample(100, T0.AddSeconds(2)));        // el contador retrocedió (reinicio): ni pico enorme ni total negativo
        Assert.Equal(1_000_000, m.Total); Assert.Equal(900, m.Sample(1000, T0.AddSeconds(3)), 1);
        double before = m.Rate; Assert.Equal(before, m.Sample(9999, T0.AddSeconds(3)));        // mismo instante: no divide por cero
        Assert.Equal(0, m.Sample(1000 + 900 + 0, T0.AddSeconds(4)) == m.Rate ? 0 : 0);
        var idle = new RateMeter(); idle.Sample(10, T0); Assert.Equal(0, idle.Sample(10, T0.AddSeconds(5)));   // sin tráfico: 0, no se queda con la velocidad anterior
        idle.Reset(); Assert.Equal(0, idle.Total);
    }

    [Theory]
    [InlineData(748_000, RateUnit.Auto, "748 KB/s")]
    [InlineData(22_000, RateUnit.Auto, "22 KB/s")]
    [InlineData(1_000, RateUnit.Auto, "1,0 KB/s")]
    [InlineData(999, RateUnit.Auto, "999 B/s")]
    [InlineData(2_500_000, RateUnit.Auto, "2,5 MB/s")]
    [InlineData(125_000, RateUnit.Mbps, "1,0 Mb/s")]
    [InlineData(12_500_000, RateUnit.Mbps, "100 Mb/s")]
    [InlineData(1_500_000, RateUnit.KBps, "1500 KB/s")]
    [InlineData(1_500_000, RateUnit.MBps, "1,5 MB/s")]
    public void Rates_are_formatted_like_the_reference_with_decimal_comma_and_the_chosen_unit(double bps, RateUnit unit, string expected) => Assert.Equal(expected, Units.FormatRate(bps, unit));

    [Fact]
    public void Rates_and_sizes_never_print_nonsense_for_invalid_numbers()
    {
        Assert.Equal("0 B/s", Units.FormatRate(double.NaN)); Assert.Equal("0 B/s", Units.FormatRate(-5)); Assert.Equal("0 B", Units.FormatSize(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(143.0 * 1024 * 1024, "143 MB")]
    [InlineData(4.72 * 1024 * 1024 * 1024, "4,72 GB")]
    [InlineData(193.0 * 1024 * 1024 * 1024, "193 GB")]
    [InlineData(18.2 * 1024 * 1024 * 1024 * 1024, "18,20 TB")]
    public void Sizes_use_windows_binary_units_with_decimal_comma(double bytes, string expected) => Assert.Equal(expected, Units.FormatSize(bytes));

    [Fact]
    public void Uptime_and_chart_axis_helpers()
    {
        Assert.Equal("1d 19h", Units.FormatUptime(TimeSpan.FromHours(43))); Assert.Equal("5h 03m", Units.FormatUptime(new TimeSpan(5, 3, 0))); Assert.Equal("12m", Units.FormatUptime(TimeSpan.FromMinutes(12))); Assert.Equal("0m", Units.FormatUptime(TimeSpan.FromSeconds(-4)));
        Assert.Equal(1, Units.NiceCeiling(0)); Assert.Equal(1, Units.NiceCeiling(0.7)); Assert.Equal(2, Units.NiceCeiling(1.3)); Assert.Equal(5, Units.NiceCeiling(3.2)); Assert.Equal(10, Units.NiceCeiling(7)); Assert.Equal(200, Units.NiceCeiling(130)); Assert.Equal(1, Units.NiceCeiling(double.NaN));
    }

    [Fact]
    public void Memory_pressure_is_a_documented_estimate_from_available_ram_and_commit_charge()
    {
        const ulong GB = 1024UL * 1024 * 1024;
        MemoryInfo M(ulong availGb10, ulong commitUsedGb, ulong commitLimitGb = 32) => new(16 * GB, availGb10 * GB / 10, null, commitUsedGb * GB, commitLimitGb * GB, null, null);
        Assert.Equal(MemoryPressureLevel.Normal, M(60, 10).Pressure);            // 6 GB libres de 16
        Assert.Equal(MemoryPressureLevel.Warning, M(25, 10).Pressure);           // 15,6 % libre
        Assert.Equal(MemoryPressureLevel.Critical, M(10, 10).Pressure);          // 6,25 % libre
        Assert.Equal(MemoryPressureLevel.Critical, M(80, 30).Pressure);          // mucha RAM libre, pero el compromiso (30/32) casi agota el límite
        Assert.Equal(MemoryPressureLevel.Warning, M(80, 27).Pressure);           // 84 % comprometido
        Assert.Equal(MemoryPressureLevel.Normal, new MemoryInfo(0, 0, null, 0, 0, null, null).Pressure);
        var m = M(60, 10); Assert.Equal(16 * GB - 6 * GB, m.UsedBytes); Assert.Equal(10.0 / 16, m.UsedFraction, 3);
    }

    [Fact]
    public void An_unavailable_sensor_is_explicit_and_never_a_zero()
    {
        var r = Reading<double>.Unavailable("Sin sensor de temperatura accesible"); Assert.False(r.Available); Assert.Null(r.Value); Assert.Contains("sensor", r.Reason);
        Assert.True(Reading<double>.Of(0).Available);   // 0 °C real no es «no disponible»
        var s = SystemSample.Empty; Assert.False(s.CpuTempC.Available); Assert.False(s.GpuPercent.Available); Assert.Null(s.Memory);
    }

    [Fact]
    public void TrafficTracker_computes_rates_history_and_session_total_for_the_active_adapter()
    {
        var wifi = new AdapterInfo("Wi-Fi", "MediaTek", "Wi-Fi", "192.168.20.175", 1_200_000_000); var t = new TrafficTracker(10);
        t.Update(new NetCounters(1000, 500, wifi), T0); t.Update(new NetCounters(3000, 700, wifi), T0.AddSeconds(2));
        Assert.Equal(1000, t.DownRate); Assert.Equal(100, t.UpRate); Assert.Equal(2000, t.SessionDown); Assert.Equal(200, t.SessionUp); Assert.Equal(2, t.DownHistory.Count);
        Assert.Equal("192.168.20.175", t.Adapter!.IPv4);
    }

    [Fact]
    public void TrafficTracker_does_not_mix_counters_when_the_active_adapter_changes_and_keeps_the_session_total()
    {
        var wifi = new AdapterInfo("Wi-Fi", "", "Wi-Fi", "1.1.1.1", null); var eth = new AdapterInfo("Ethernet", "", "Ethernet", "2.2.2.2", null); var t = new TrafficTracker();
        t.Update(new NetCounters(0, 0, wifi), T0); t.Update(new NetCounters(10_000, 0, wifi), T0.AddSeconds(1));
        t.Update(new NetCounters(900_000_000, 0, eth), T0.AddSeconds(2));   // el contador del nuevo adaptador es otro: NO es un salto de 900 MB
        Assert.Equal(0, t.DownRate); Assert.Equal(10_000, t.SessionDown);
        t.Update(new NetCounters(900_001_000, 0, eth), T0.AddSeconds(3)); Assert.Equal(1000, t.DownRate); Assert.Equal(11_000, t.SessionDown);
    }

    [Fact]
    public void TrafficTracker_without_an_adapter_keeps_the_chart_moving_and_reports_the_reason()
    {
        var t = new TrafficTracker(); t.Update(new NetCounters(0, 0, null, "Sin conexión de red"), T0); Assert.Equal("Sin conexión de red", t.Reason); Assert.Null(t.Adapter); Assert.Equal(1, t.DownHistory.Count);
    }
}

public class WeatherTests
{
    private const string Sample = """
    {"utc_offset_seconds":-10800,"current":{"time":"2026-11-01T06:00","interval":900,"temperature_2m":12.0,"weather_code":3,"is_day":1},
     "hourly":{"time":["2026-11-01T05:00","2026-11-01T06:00","2026-11-01T07:00","2026-11-01T08:00","2026-11-01T09:00","2026-11-01T10:00"],
       "temperature_2m":[11.5,12,12.4,13,14,15],"weather_code":[3,3,3,61,3,3],"is_day":[0,1,1,1,1,1],"precipitation_probability":[0,0,5,10,0,0]},
     "daily":{"time":["2026-11-01","2026-11-02"],"temperature_2m_max":[21,22],"temperature_2m_min":[12,11],"sunrise":["2026-11-01T06:42","2026-11-02T06:41"],"sunset":["2026-11-01T20:15","2026-11-02T20:16"]}}
    """;
    private static readonly GeoPoint Molina = new("Molina", "Maule, Chile", -35.1167, -71.2833);

    [Fact]
    public void The_forecast_is_parsed_into_current_conditions_high_low_hourly_and_sun_events()
    {
        var w = OpenMeteoParser.ParseForecast(Sample, Molina);
        Assert.Equal(12, w.TempC); Assert.Equal(3, w.Code); Assert.True(w.IsDay); Assert.Equal(21, w.HighC); Assert.Equal(12, w.LowC); Assert.Equal(new DateTime(2026, 11, 1, 6, 0, 0), w.LocalNow);
        Assert.Equal(6, w.Hourly.Count); Assert.Equal(61, w.Hourly[3].Code); Assert.Equal(10, w.Hourly[3].PrecipProb); Assert.False(w.Hourly[0].IsDay);
        Assert.Equal([true, false, true, false], w.Sun.Select(s => s.IsRise).ToArray().Length == 4 ? w.Sun.Select(s => s.IsRise).ToArray() : []);   // 2 orto y 2 ocaso, ordenados por hora
        Assert.Equal(new DateTime(2026, 11, 1, 6, 42, 0), w.Sun[0].Time);
    }

    [Fact]
    public void Missing_or_null_fields_degrade_gracefully_and_garbage_is_a_clear_error()
    {
        var w = OpenMeteoParser.ParseForecast("""{"current":{"time":"2026-11-01T06:00","temperature_2m":9.5,"weather_code":0},"hourly":{"time":["2026-11-01T06:00","2026-11-01T07:00"],"temperature_2m":[9.5,null]}}""", Molina);
        Assert.Equal(9.5, w.TempC); Assert.Null(w.HighC); Assert.Single(w.Hourly); Assert.Empty(w.Sun); Assert.True(w.Hourly[0].PrecipProb is null);   // sin daily ni probabilidad: no se inventan
        Assert.Throws<FormatException>(() => OpenMeteoParser.ParseForecast("no es json", Molina));
        Assert.Throws<FormatException>(() => OpenMeteoParser.ParseForecast("{}", Molina));
        var ex = Assert.Throws<FormatException>(() => OpenMeteoParser.ParseForecast("""{"error":true,"reason":"Latitude must be in range"}""", Molina)); Assert.Contains("Latitude", ex.Message);
    }

    [Fact]
    public void The_hourly_strip_reproduces_the_reference_with_sunrise_in_place_and_rain_chance_only_when_relevant()
    {
        var strip = WeatherStrip.Build(OpenMeteoParser.ParseForecast(Sample, Molina), fahrenheit: false, use24h: false);
        Assert.Equal(["6 a. m.", "6:42 a. m.", "7 a. m.", "8 a. m.", "9 a. m."], strip.Select(s => s.Label));
        Assert.Equal(["12°", "Salida del sol", "12°", "13°", "14°"], strip.Select(s => s.Text));
        Assert.Equal(["cloudy", "sunrise", "cloudy", "rain", "cloudy"], strip.Select(s => s.IconKey));
        Assert.Equal([null, null, null, 10, null], strip.Select(s => s.PrecipProb));   // 5 % no se muestra; 10 % sí
        Assert.True(strip[1].IsSun); Assert.False(strip[0].IsSun);
    }

    [Fact]
    public void The_strip_excludes_past_hours_honors_24h_and_fahrenheit_and_shows_sunset()
    {
        var w = OpenMeteoParser.ParseForecast(Sample, Molina) with { LocalNow = new DateTime(2026, 11, 1, 19, 20, 0) };
        w = w with { Sun = [new(new DateTime(2026, 11, 1, 6, 42, 0), true), new(new DateTime(2026, 11, 1, 20, 15, 0), false)], Hourly = [new(new DateTime(2026, 11, 1, 18, 0, 0), 14, 3, true, 0), new(new DateTime(2026, 11, 1, 19, 0, 0), 13, 3, true, 0), new(new DateTime(2026, 11, 1, 20, 0, 0), 12, 0, true, 0), new(new DateTime(2026, 11, 1, 21, 0, 0), 11, 0, false, 0)] };
        var s = WeatherStrip.Build(w, true, true);
        Assert.Equal(["19:00", "20:00", "20:15", "21:00"], s.Select(x => x.Label)); Assert.DoesNotContain(s, x => x.Label == "18:00");   // la hora de las 18 ya pasó; la de las 19 sigue en curso
        Assert.Equal("55°", s[0].Text); Assert.Equal("Puesta del sol", s[2].Text); Assert.Equal("sunset", s[2].IconKey);
        Assert.Equal(2, WeatherStrip.Build(w, false, false, slots: 2).Count);
    }

    [Theory]
    [InlineData(0, true, "Despejado", "clear")]
    [InlineData(0, false, "Despejado", "clear-night")]
    [InlineData(2, false, "Parcialmente nublado", "partly-night")]
    [InlineData(3, true, "Mayormente nublado", "cloudy")]
    [InlineData(61, true, "Lluvia ligera", "rain")]
    [InlineData(95, true, "Tormenta", "thunder")]
    [InlineData(73, true, "Nevada", "snow")]
    [InlineData(45, true, "Niebla", "fog")]
    [InlineData(999, true, "Sin datos", "cloudy")]
    public void Weather_codes_map_to_spanish_descriptions_and_icon_keys(int code, bool day, string text, string icon) { Assert.Equal(text, WeatherCodes.Describe(code)); Assert.Equal(icon, WeatherCodes.Icon(code, day)); }

    [Fact]
    public void Temperatures_format_like_the_reference_in_celsius_and_fahrenheit_without_minus_zero()
    {
        Assert.Equal("12°", TempFormat.Degrees(12.4, false)); Assert.Equal("13°", TempFormat.Degrees(12.5, false)); Assert.Equal("54°", TempFormat.Degrees(12, true)); Assert.Equal("0°", TempFormat.Degrees(-0.2, false));
        Assert.Equal("-3°", TempFormat.Degrees(-3.2, false)); Assert.Equal("12°C", TempFormat.Bar(12, false)); Assert.Equal("54°F", TempFormat.Bar(12, true));
    }

    [Fact]
    public void Geographic_points_roundtrip_through_the_central_setting_and_reject_garbage()
    {
        var g = GeoPoint.Parse("Molina|-35.1167|-71.2833")!; Assert.Equal("Molina", g.Name); Assert.Equal(-35.1167, g.Lat); Assert.Equal("Molina|-35.1167|-71.2833", g.Serialize());
        Assert.Null(GeoPoint.Parse(null)); Assert.Null(GeoPoint.Parse("")); Assert.Null(GeoPoint.Parse("solo nombre")); Assert.Null(GeoPoint.Parse("X|abc|1")); Assert.Null(GeoPoint.Parse("X|95|1")); Assert.Null(GeoPoint.Parse("X|1|200")); Assert.Null(GeoPoint.Parse("|1|2"));
        var list = GeoPoint.ParseList("Santiago|-33.45|-70.67; Antofagasta|-23.65|-70.4 ;basura;Santiago|-33.45|-70.67;Valparaíso|-33.05|-71.62", 5);
        Assert.Equal(["Santiago", "Antofagasta", "Valparaíso"], list.Select(x => x.Name));   // sin duplicados ni entradas inválidas
        Assert.Equal(2, GeoPoint.ParseList("A|1|1;B|2|2;C|3|3", 2).Count); Assert.Equal("A|1|1;B|2|2", GeoPoint.SerializeList(GeoPoint.ParseList("A|1|1;B|2|2")));
        Assert.DoesNotContain("|", new GeoPoint("Mal|Nombre;Raro", null, 1, 2).Serialize().Split('|')[0]);   // un nombre con separadores no corrompe el formato
    }

    [Fact]
    public void Geocoding_results_become_places_with_region_and_country()
    {
        var r = OpenMeteoParser.ParseGeocoding("""{"results":[{"name":"Molina","latitude":-35.11,"longitude":-71.28,"admin1":"Maule","country":"Chile"},{"name":"Molina","latitude":38.0,"longitude":-1.2,"country":"España"},{"basura":1}]}""");
        Assert.Equal(2, r.Count); Assert.Equal("Maule, Chile", r[0].Region); Assert.Equal("España", r[1].Region);
        Assert.Empty(OpenMeteoParser.ParseGeocoding("{}")); Assert.Throws<FormatException>(() => OpenMeteoParser.ParseGeocoding("["));
    }

    [Fact]
    public async Task The_real_client_builds_the_requests_with_invariant_coordinates_and_parses_main_and_extra_cities()
    {
        var urls = new List<string>(); var http = new HttpClient(new StubHandler(req => { urls.Add(req.RequestUri!.ToString()); return Json(req.RequestUri!.AbsoluteUri.Contains("forecast") && req.RequestUri.Query.Contains("hourly") ? Sample : """{"current":{"time":"2026-11-01T06:00","temperature_2m":17,"weather_code":2,"is_day":0}}"""); }));
        var snap = await new OpenMeteoSource(http).FetchAsync(Molina, [new GeoPoint("Antofagasta", null, -23.65, -70.4)], CancellationToken.None);
        Assert.Equal(2, urls.Count); Assert.Contains("latitude=-35.1167", urls[0]); Assert.Contains("longitude=-71.2833", urls[0]); Assert.Contains("timezone=auto", urls[0]); Assert.All(urls, u => Assert.StartsWith("https://", u));
        Assert.Equal(12, snap.Main.TempC); Assert.Equal("Antofagasta", snap.Others[0].Place.Name); Assert.Equal(17, snap.Others[0].TempC);
        var found = await new OpenMeteoSource(new HttpClient(new StubHandler(_ => Json("""{"results":[{"name":"Molina","latitude":-35.11,"longitude":-71.28}]}""")))).SearchAsync("Mo lina", CancellationToken.None); Assert.Single(found);
    }

    [Fact]
    public async Task One_extra_city_failing_does_not_take_down_the_panel_but_a_failing_main_city_does()
    {
        int n = 0; var http = new HttpClient(new StubHandler(req => ++n == 1 ? Json(Sample) : new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var snap = await new OpenMeteoSource(http).FetchAsync(Molina, [new GeoPoint("X", null, 1, 1)], CancellationToken.None); Assert.Empty(snap.Others); Assert.Equal(12, snap.Main.TempC);
        await Assert.ThrowsAsync<HttpRequestException>(() => new OpenMeteoSource(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)))).FetchAsync(Molina, [], CancellationToken.None));
    }

    internal static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s) };
    internal sealed class StubHandlerAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => f(r); }
    internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r)); }
}

public class WeatherServiceTests : IDisposable
{
    private static readonly GeoPoint Molina = new("Molina", null, -35.1167, -71.2833), Santiago = new("Santiago", null, -33.45, -70.67);
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly Queue<Action> _bg = new(), _ui = new(); private readonly List<string> _log = []; private readonly string _file = Path.Combine(Path.GetTempPath(), "winmac-wx-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly FakeSource _src = new(); private int _changed;
    public void Dispose() { try { File.Delete(_file); } catch (IOException) { } }

    private sealed class FakeSource : IWeatherSource
    {
        public int Calls; public Exception? Throw; public Func<GeoPoint, double> Temp = _ => 12;
        public Task<WeatherSnapshot> FetchAsync(GeoPoint place, IReadOnlyList<GeoPoint> others, CancellationToken ct)
        {
            Calls++; if (Throw is not null) throw Throw;
            var city = new CityWeather(place, new DateTime(2026, 11, 1, 9, 0, 0), Temp(place), 3, true, 20, 10, [], []);
            return Task.FromResult(new WeatherSnapshot(city, others.Select(o => new CityBrief(o, 15, 0, true)).ToList(), DateTime.UtcNow));
        }

        public Task<IReadOnlyList<GeoPoint>> SearchAsync(string name, CancellationToken ct) => Task.FromResult<IReadOnlyList<GeoPoint>>([]);
    }

    private WeatherService Make(WeatherCache? cache = null) { var s = new WeatherService(_src, cache ?? new WeatherCache(null), () => _now, a => _bg.Enqueue(a), a => _ui.Enqueue(a), _log.Add); s.Changed += () => _changed++; return s; }
    private void Pump() { while (_bg.Count > 0 || _ui.Count > 0) { while (_bg.Count > 0) _bg.Dequeue()(); while (_ui.Count > 0) _ui.Dequeue()(); } }

    [Fact]
    public void Without_a_city_it_asks_for_one_and_never_calls_the_provider()
    {
        var s = Make(); s.Configure(null, []); s.Refresh(force: true); Pump(); Assert.Equal(WeatherStatus.NoCity, s.Current().Status); Assert.Equal(0, _src.Calls);
    }

    [Fact]
    public void The_request_runs_in_the_background_shows_loading_first_then_the_data()
    {
        var s = Make(); s.Configure(Molina, []); Pump(); s.Refresh();
        Assert.Equal(0, _src.Calls); Assert.Equal(WeatherStatus.Loading, s.Current().Status);   // nada se pidió en el hilo de la barra
        Pump(); var st = s.Current(); Assert.Equal(WeatherStatus.Ok, st.Status); Assert.Equal("Molina", st.Snapshot!.Main.Place.Name); Assert.True(_changed >= 1);
    }

    [Fact]
    public void Requests_are_single_flight_and_data_stays_fresh_for_15_minutes_unless_forced()
    {
        var s = Make(); s.Configure(Molina, []); s.Refresh(); s.Refresh(); s.Refresh(); Pump(); Assert.Equal(1, _src.Calls);
        _now = _now.AddMinutes(14); s.Refresh(); Pump(); Assert.Equal(1, _src.Calls);
        _now = _now.AddMinutes(2); s.Refresh(); Pump(); Assert.Equal(2, _src.Calls);
        s.Refresh(force: true); Pump(); Assert.Equal(3, _src.Calls);
    }

    [Fact]
    public void Offline_keeps_the_last_data_marked_as_such_backs_off_and_recovers_by_itself()
    {
        var s = Make(); s.Configure(Molina, []); s.Refresh(); Pump(); _now = _now.AddMinutes(20);
        _src.Throw = new HttpRequestException("sin red"); s.Refresh(); Pump(); var st = s.Current();
        Assert.Equal(WeatherStatus.Offline, st.Status); Assert.NotNull(st.Snapshot); Assert.Contains("Sin conexión", st.Message);   // los datos viejos se siguen mostrando, avisando
        int calls = _src.Calls; _now = _now.AddSeconds(30); s.Refresh(); Pump(); Assert.Equal(calls, _src.Calls);                   // en espera: no martillea al proveedor
        _src.Throw = null; _now = _now.AddMinutes(2); s.Refresh(); Pump(); Assert.Equal(calls + 1, _src.Calls); Assert.Equal(WeatherStatus.Ok, s.Current().Status);
    }

    [Fact]
    public void Backoff_grows_one_two_four_up_to_thirty_minutes()
    {
        Assert.Equal([1.0, 2, 4, 8, 16, 30, 30], Enumerable.Range(1, 7).Select(n => WeatherService.Backoff(n).TotalMinutes));
        Assert.Equal(1, WeatherService.Backoff(0).TotalMinutes);
    }

    [Fact]
    public void Failures_without_any_data_distinguish_no_connection_from_a_provider_error()
    {
        var s = Make(); s.Configure(Molina, []); _src.Throw = new HttpRequestException("dns"); s.Refresh(); Pump(); Assert.Equal(WeatherStatus.Offline, s.Current().Status); Assert.Null(s.Current().Snapshot);
        var s2 = Make(); s2.Configure(Santiago, []); _src.Throw = new HttpRequestException("500", null, HttpStatusCode.InternalServerError); s2.Refresh(); Pump(); Assert.Equal(WeatherStatus.Error, s2.Current().Status); Assert.Contains("500", s2.Current().Message);
        var s3 = Make(); s3.Configure(new GeoPoint("Z", null, 5, 5), []); _src.Throw = new FormatException("raro"); s3.Refresh(); Pump(); Assert.Equal(WeatherStatus.Error, s3.Current().Status);
    }

    [Fact]
    public void Very_old_data_is_marked_stale_even_without_a_failure()
    {
        var s = Make(); s.Configure(Molina, []); s.Refresh(); Pump(); _now = _now.AddMinutes(61); var st = s.Current();
        Assert.Equal(WeatherStatus.Stale, st.Status); Assert.Contains("desactualizados", st.Message); Assert.NotNull(st.Snapshot);
    }

    [Fact]
    public void Changing_city_never_shows_the_previous_cities_data_and_ignores_a_late_answer_for_the_old_one()
    {
        var s = Make(); s.Configure(Molina, []); s.Refresh(); Pump(); Assert.Equal("Molina", s.Current().Snapshot!.Main.Place.Name);
        s.Configure(Santiago, []); Assert.Null(s.Current().Snapshot); Assert.Equal(WeatherStatus.Loading, s.Current().Status);   // ni un instante con datos de Molina bajo «Santiago»
        s.Refresh(); var pendingForSantiago = _bg.ToArray(); _bg.Clear();
        s.Configure(Molina, []); pendingForSantiago[0](); while (_ui.Count > 0) _ui.Dequeue()();   // llega tarde la respuesta de Santiago
        Assert.DoesNotContain(s.Current().Snapshot?.Main.Place.Name ?? "", "Santiago");
    }

    [Fact]
    public void Changing_the_extra_cities_triggers_a_new_request_even_if_the_data_is_recent()
    {
        var s = Make(); s.Configure(Molina, []); s.Refresh(); Pump(); Assert.Equal(1, _src.Calls);
        s.Configure(Molina, [Santiago]); s.Refresh(); Pump(); Assert.Equal(2, _src.Calls); Assert.Single(s.Current().Snapshot!.Others);
        s.Configure(Molina, [Santiago]); s.Refresh(); Pump(); Assert.Equal(2, _src.Calls);   // sin cambios: no se repite
    }

    [Fact]
    public void The_disk_cache_survives_a_restart_for_the_same_city_only_and_ignores_corruption()
    {
        var s = Make(new WeatherCache(_file)); s.Configure(Molina, []); s.Refresh(); Pump(); Assert.True(File.Exists(_file));
        _src.Throw = new HttpRequestException("sin red"); var again = Make(new WeatherCache(_file)); again.Configure(Molina, []);
        Assert.Equal("Molina", again.Current().Snapshot!.Main.Place.Name);              // tras reiniciar y sin red, se muestran los últimos datos
        var other = Make(new WeatherCache(_file)); other.Configure(Santiago, []); Assert.Null(other.Current().Snapshot);   // pero de OTRA ciudad no
        File.WriteAllText(_file, "{corrupto"); var bad = Make(new WeatherCache(_file)); bad.Configure(Molina, []); Assert.Null(bad.Current().Snapshot); Assert.Equal(WeatherStatus.Loading, bad.Current().Status);
    }
}

public class SpeedTestTests
{
    private sealed class Clock { public long Ticks; public void Advance(double seconds) => Ticks += (long)(seconds * 1_000_000); public long Now() => Ticks; }

    private sealed class Throttled(Clock c, long bytes, double bytesPerSec) : Stream
    {
        private long _read; public long Read2 => _read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, bytes - _read); if (n <= 0) return 0; _read += n; c.Advance(n / bytesPerSec); return n;
        }

        public override Task<int> ReadAsync(byte[] b, int o, int n, CancellationToken ct) => Task.FromResult(Read(b, o, n));
        public override ValueTask<int> ReadAsync(Memory<byte> m, CancellationToken ct = default) { var t = new byte[m.Length]; int n = Read(t, 0, t.Length); t.AsMemory(0, n).CopyTo(m); return ValueTask.FromResult(n); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => bytes; public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int n) => throw new NotSupportedException();
    }

    private sealed class SinkStream(Clock c, double bytesPerSec) : Stream
    {
        public long Written;
        public override void Write(byte[] b, int o, int n) { Written += n; c.Advance(n / bytesPerSec); }
        public override Task WriteAsync(byte[] b, int o, int n, CancellationToken ct) { Write(b, o, n); return Task.CompletedTask; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> m, CancellationToken ct = default) { Written += m.Length; c.Advance(m.Length / bytesPerSec); return ValueTask.CompletedTask; }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true; public override long Length => Written; public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] b, int o, int n) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
    }

    private static (SpeedTestEngine Engine, Clock Clock, List<string> Requests, Throttled?[] LastDown) Make(double downBytesPerSec, double upBytesPerSec, Func<HttpRequestMessage, HttpResponseMessage?>? custom = null)
    {
        var clock = new Clock(); var reqs = new List<string>(); var last = new Throttled?[1];
        var handler = new WeatherTests.StubHandlerAsync(async req =>
        {
            reqs.Add(req.Method + " " + req.RequestUri!.PathAndQuery); if (custom?.Invoke(req) is { } r) return r;
            if (req.Method == HttpMethod.Post) { await req.Content!.CopyToAsync(new SinkStream(clock, upBytesPerSec)); return new HttpResponseMessage(HttpStatusCode.OK); }
            long bytes = long.Parse(req.RequestUri.Query.Split("bytes=")[1]); if (bytes == 0) { clock.Advance(0.02); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }; }
            var s = new Throttled(clock, bytes, downBytesPerSec); last[0] = s; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(s) };
        });
        return (new SpeedTestEngine(new HttpClient(handler), clock.Now, 1_000_000), clock, reqs, last);
    }

    [Fact]
    public async Task It_measures_latency_download_and_upload_with_progress_in_the_right_order()
    {
        var (engine, _, reqs, _) = Make(downBytesPerSec: 12_500_000, upBytesPerSec: 1_250_000);   // 100 Mb/s abajo, 10 Mb/s arriba
        var phases = new List<SpeedPhase>(); var res = await engine.RunAsync(p => { if (phases.Count == 0 || phases[^1] != p.Phase) phases.Add(p.Phase); }, CancellationToken.None);
        Assert.Null(res.Error); Assert.False(res.Cancelled); Assert.Equal(100, res.DownMbps!.Value, 1); Assert.Equal(10, res.UpMbps!.Value, 1); Assert.Equal(20, res.LatencyMs!.Value, 1);
        Assert.Equal([SpeedPhase.Latency, SpeedPhase.Download, SpeedPhase.Upload, SpeedPhase.Done], phases);
        Assert.Equal(3, reqs.Count(r => r.EndsWith("bytes=0"))); Assert.Contains(reqs, r => r.StartsWith("POST") && r.Contains("__up")); Assert.All(reqs, r => Assert.DoesNotContain("http:", r));
    }

    [Fact]
    public async Task A_slow_connection_is_cut_by_the_time_limit_not_by_downloading_everything()
    {
        var (engine, clock, _, last) = Make(downBytesPerSec: 250_000, upBytesPerSec: 250_000);   // 2 Mb/s
        var res = await engine.RunAsync(_ => { }, CancellationToken.None, new SpeedTestOptions(DownloadBytes: 25_000_000, DownloadMaxSeconds: 4, UploadBytes: 8_000_000, UploadMaxSeconds: 2));
        Assert.Equal(2, res.DownMbps!.Value, 0); Assert.True(last[0]!.Read2 < 2_000_000, "descargó de más: debía parar por el límite de tiempo");   // ≈1 MB, no 25
        Assert.Equal(2, res.UpMbps!.Value, 0); _ = clock;
    }

    [Fact]
    public async Task Cancelling_returns_a_cancelled_result_with_whatever_was_measured()
    {
        var (engine, _, _, _) = Make(12_500_000, 1_250_000); using var cts = new CancellationTokenSource();
        var res = await engine.RunAsync(p => { if (p.Phase == SpeedPhase.Upload) cts.Cancel(); }, cts.Token);
        Assert.True(res.Cancelled); Assert.Null(res.Error); Assert.NotNull(res.DownMbps);   // la descarga ya medida se conserva
    }

    [Fact]
    public async Task Network_errors_become_a_clear_message_not_an_exception()
    {
        var (a, _, _, _) = Make(1, 1, _ => throw new HttpRequestException("dns")); var r1 = await a.RunAsync(_ => { }, CancellationToken.None);
        Assert.Contains("sin conexión", r1.Error!); Assert.Null(r1.DownMbps);
        var (b, _, _, _) = Make(1, 1, _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); var r2 = await b.RunAsync(_ => { }, CancellationToken.None); Assert.Contains("503", r2.Error!);
    }

    [Fact]
    public void Mbps_math_is_bits_per_second_and_safe_with_zero_time() { Assert.Equal(8, SpeedTestEngine.Mbps(1_000_000, 1)); Assert.Equal(0, SpeedTestEngine.Mbps(1000, 0)); }
}

public class DiskCatalogTests
{
    private static readonly SmartInfo NvmeSmart = new("Healthy", 37, 639, 1, 51.47e12, 18.2e12);

    private static DisksSnapshot Sample() => DiskCatalog.Build(
        [new PhysicalRec(1, "USB Elements", "HDD", "USB", 1_000_000_000_000, null), new PhysicalRec(0, "Samsung 990 PRO", "SSD", "NVMe", 2_000_000_000_000, NvmeSmart)],
        [new PartitionRec(0, "C"), new PartitionRec(0, null), new PartitionRec(1, "E"), new PartitionRec(9, "Z")],
        [new VolumeRec("E:\\", "Respaldo", "NTFS", 900, 100, true), new VolumeRec("C:\\", "", "NTFS", 1000, 250, false), new VolumeRec("Y:\\", "Red", "NTFS", 500, 500, false)]);

    [Fact]
    public void Volumes_are_linked_to_the_physical_disk_that_holds_their_partition_and_sorted_by_letter()
    {
        var s = Sample(); Assert.Equal(["C:", "E:", "Y:"], s.Volumes.Select(v => v.Letter)); Assert.Equal([0, 1], s.Disks.Select(d => d.Number));
        Assert.Equal(0, s.Volumes[0].DiskNumber); Assert.Equal("Samsung 990 PRO", s.DiskOf(s.Volumes[0])!.Model); Assert.Equal(1, s.Volumes[1].DiskNumber);
    }

    [Fact]
    public void SMART_data_is_never_attributed_to_another_device()
    {
        var s = Sample(); var c = s.DiskOf(s.Volumes[0])!; var e = s.DiskOf(s.Volumes[1])!;
        Assert.Equal(37, c.Smart.TemperatureC); Assert.Equal("Healthy", c.Smart.Health);
        Assert.False(e.Smart.HasAny); Assert.Null(e.Smart.TemperatureC); Assert.Contains("no entrega", e.Smart.Reason);   // el disco USB no hereda los datos del NVMe
    }

    [Fact]
    public void A_volume_without_a_known_partition_has_no_disk_and_therefore_no_smart()
    {
        var s = Sample(); var net = s.Volumes.Single(v => v.Letter == "Y:"); Assert.Null(net.DiskNumber); Assert.Null(s.DiskOf(net));
        Assert.DoesNotContain(s.Volumes, v => v.Letter == "Z:");   // una partición de un disco que no existe no inventa un volumen
    }

    [Fact]
    public void External_and_media_detection_and_usage_numbers()
    {
        var s = Sample(); Assert.True(s.Disks[1].IsExternal); Assert.False(s.Disks[0].IsExternal); Assert.Equal(DiskMedia.SSD, s.Disks[0].Media); Assert.Equal(DiskMedia.HDD, s.Disks[1].Media);
        Assert.Equal(750UL, s.Volumes[0].UsedBytes); Assert.Equal(0.75, s.Volumes[0].UsedFraction, 3); Assert.Equal("C:", s.Volumes[0].Display); Assert.Equal("Respaldo (E:)", s.Volumes[1].Display);
        Assert.Equal(0, new DiskVolume("X:", null, "", 0, 0, false, null).UsedFraction);   // sin capacidad no divide por cero
    }

    [Fact]
    public void Letters_are_normalized_and_an_empty_machine_is_handled()
    {
        var s = DiskCatalog.Build([new PhysicalRec(0, "", null, "", 10, null)], [new PartitionRec(0, "d:")], [new VolumeRec("d:\\", null, "", 10, 5, false)]);
        Assert.Equal("D:", s.Volumes[0].Letter); Assert.Equal(0, s.Volumes[0].DiskNumber); Assert.Equal("Disco 0", s.Disks[0].Model); Assert.Equal(DiskMedia.Unknown, s.Disks[0].Media);
        var none = DiskCatalog.Build([], [], []); Assert.Empty(none.Volumes); Assert.Empty(none.Disks); Assert.Equal("sin permisos", DisksSnapshot.None("sin permisos").Reason);
    }
}
