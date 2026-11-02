namespace WinMac.Bar.Hub;

// ───────────── Red
public sealed record AdapterInfo(string Name, string Description, string Kind, string? IPv4, long? LinkSpeedBps);

/// <summary>Contadores acumulados del ADAPTADOR (no de las aplicaciones) en un instante.</summary>
public sealed record NetCounters(ulong BytesReceived, ulong BytesSent, AdapterInfo? Adapter, string? Reason = null);

/// <summary>Una aplicación con conexiones TCP establecidas. OJO: es «usa la red», no «cuánto tráfico genera»: Windows no da bytes por proceso sin privilegios de administrador (ETW).</summary>
public sealed record NetApp(string Name, int Pid, int Connections, string? ExePath);

/// <summary>
/// Velocidades, historial y acumulado del adaptador activo. Si cambia el adaptador activo (Wi-Fi ↔ Ethernet) los contadores del nuevo no se mezclan con los del anterior: se reinician las
/// velocidades y el acumulado de la sesión conserva lo ya contado.
/// </summary>
public sealed class TrafficTracker(int historyLength = 60)
{
    private readonly RateMeter _down = new(), _up = new(); private double _carryDown, _carryUp; private string? _adapterName;
    public History DownHistory { get; } = new(historyLength); public History UpHistory { get; } = new(historyLength);
    public double DownRate => _down.Rate; public double UpRate => _up.Rate;
    public double SessionDown => _carryDown + _down.Total; public double SessionUp => _carryUp + _up.Total;
    public AdapterInfo? Adapter { get; private set; }
    public string? Reason { get; private set; } = "Leyendo…";

    public void MarkGap() { _down.MarkGap(); _up.MarkGap(); }

    public void Update(NetCounters c, DateTime now)
    {
        Reason = c.Reason; Adapter = c.Adapter;
        if (c.Adapter is null) { DownHistory.Add(0); UpHistory.Add(0); return; }
        if (_adapterName is not null && _adapterName != c.Adapter.Name) { _carryDown += _down.Total; _carryUp += _up.Total; _down.Reset(); _up.Reset(); }
        _adapterName = c.Adapter.Name;
        DownHistory.Add(_down.Sample(c.BytesReceived, now)); UpHistory.Add(_up.Sample(c.BytesSent, now));
    }
}

public enum SpeedPhase { Idle, Latency, Download, Upload, Done }
public sealed record SpeedProgress(SpeedPhase Phase, double Mbps, double Fraction);
public sealed record SpeedResult(double? DownMbps, double? UpMbps, double? LatencyMs, string? Error, bool Cancelled);
public sealed record SpeedTestOptions(string BaseUrl = "https://speed.cloudflare.com", long DownloadBytes = 25_000_000, double DownloadMaxSeconds = 8, long UploadBytes = 8_000_000, double UploadMaxSeconds = 5);

/// <summary>
/// Prueba de velocidad REAL contra un servidor de pruebas HTTPS (Cloudflare por defecto). Solo se ejecuta cuando el usuario la pide (la interfaz avisa de que consume datos), con progreso, límite de
/// tamaño y de tiempo, y cancelable. Descarga y subida van de una en una (no compiten entre sí ni con otras conexiones más de lo necesario). El reloj y el HTTP se inyectan para probarla.
/// </summary>
public sealed class SpeedTestEngine(HttpClient http, Func<long> ticks, long ticksPerSecond)
{
    private double Seconds(long from) => (double)(ticks() - from) / ticksPerSecond;

    public async Task<SpeedResult> RunAsync(Action<SpeedProgress> progress, CancellationToken ct, SpeedTestOptions? options = null)
    {
        var o = options ?? new SpeedTestOptions(); double? down = null, up = null, lat = null;
        try
        {
            progress(new SpeedProgress(SpeedPhase.Latency, 0, 0)); lat = await LatencyAsync(o, ct).ConfigureAwait(false);
            down = await DownloadAsync(o, progress, ct).ConfigureAwait(false);
            up = await UploadAsync(o, progress, ct).ConfigureAwait(false);
            progress(new SpeedProgress(SpeedPhase.Done, down ?? 0, 1)); return new SpeedResult(down, up, lat, null, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new SpeedResult(down, up, lat, null, true); }
        catch (HttpRequestException e) { return new SpeedResult(down, up, lat, "No se pudo completar la prueba: " + (e.StatusCode is { } sc ? $"el servidor respondió {(int)sc}" : "sin conexión con el servidor de pruebas"), false); }
        catch (Exception e) when (e is TaskCanceledException or IOException) { return new SpeedResult(down, up, lat, "La prueba se interrumpió: la conexión no respondió a tiempo", false); }
    }

    private async Task<double> LatencyAsync(SpeedTestOptions o, CancellationToken ct)
    {
        var samples = new List<double>();
        for (int i = 0; i < 3; i++)
        {
            long t0 = ticks(); using var r = await http.GetAsync($"{o.BaseUrl}/__down?bytes=0", HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false); r.EnsureSuccessStatusCode();
            samples.Add(Seconds(t0) * 1000);
        }

        samples.Sort(); return samples[samples.Count / 2];   // mediana: un pico aislado no la mueve
    }

    private async Task<double> DownloadAsync(SpeedTestOptions o, Action<SpeedProgress> progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync($"{o.BaseUrl}/__down?bytes={o.DownloadBytes}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[64 * 1024]; long total = 0, t0 = ticks(), lastReport = t0; int n;
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += n; double el = Seconds(t0);
            if (Seconds(lastReport) >= 0.2) { lastReport = ticks(); progress(new SpeedProgress(SpeedPhase.Download, Mbps(total, el), Math.Min(1, Math.Max((double)total / o.DownloadBytes, el / o.DownloadMaxSeconds)))); }
            if (total >= o.DownloadBytes || el >= o.DownloadMaxSeconds) break;   // límite de tamaño o de tiempo: se corta la descarga
        }

        double sec = Seconds(t0); progress(new SpeedProgress(SpeedPhase.Download, Mbps(total, sec), 1)); return Mbps(total, sec);
    }

    private async Task<double> UploadAsync(SpeedTestOptions o, Action<SpeedProgress> progress, CancellationToken ct)
    {
        long t0 = ticks(); var content = new CountingContent(o.UploadBytes, ticks, ticksPerSecond, o.UploadMaxSeconds, (sent, sec) => progress(new SpeedProgress(SpeedPhase.Upload, Mbps(sent, sec), Math.Min(1, Math.Max((double)sent / o.UploadBytes, sec / o.UploadMaxSeconds)))));
        using var resp = await http.PostAsync($"{o.BaseUrl}/__up", content, ct).ConfigureAwait(false); resp.EnsureSuccessStatusCode();
        double sec = Seconds(t0); double mbps = Mbps(content.Sent, sec); progress(new SpeedProgress(SpeedPhase.Upload, mbps, 1)); return mbps;
    }

    public static double Mbps(long bytes, double seconds) => seconds <= 0 ? 0 : bytes * 8.0 / seconds / 1_000_000;

    private sealed class CountingContent(long bytes, Func<long> ticks, long freq, double maxSeconds, Action<long, double> report) : HttpContent
    {
        public long Sent { get; private set; }
        protected override bool TryComputeLength(out long length) { length = -1; return false; }   // sin longitud fija: puede cortarse por tiempo

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            var chunk = new byte[64 * 1024]; Random.Shared.NextBytes(chunk); long t0 = ticks(), last = t0;
            while (Sent < bytes)
            {
                int n = (int)Math.Min(chunk.Length, bytes - Sent); await stream.WriteAsync(chunk.AsMemory(0, n)).ConfigureAwait(false); Sent += n;
                double el = (double)(ticks() - t0) / freq;
                if ((double)(ticks() - last) / freq >= 0.2) { last = ticks(); report(Sent, el); }
                if (el >= maxSeconds) break;   // límite de tiempo
            }
        }
    }
}

// ───────────── Discos
public enum DiskMedia { Unknown, SSD, HDD }

/// <summary>Datos de salud del disco FÍSICO al que pertenecen. Cada campo es opcional: lo que el dispositivo no entrega queda null y la interfaz lo muestra como no disponible.</summary>
public sealed record SmartInfo(string? Health, double? TemperatureC, long? PowerOnHours, double? WearPercent, double? TotalReadBytes, double? TotalWrittenBytes, string? Reason = null, DiskIncidents? Incidents = null)
{
    public static SmartInfo None(string reason) => new(null, null, null, null, null, null, reason);
    public bool HasAny => Health is not null || TemperatureC is not null || PowerOnHours is not null || WearPercent is not null || TotalReadBytes is not null || TotalWrittenBytes is not null;
}

public sealed record PhysicalDisk(int Number, string Model, DiskMedia Media, string Bus, ulong SizeBytes, bool IsExternal, SmartInfo Smart);

public sealed record DiskVolume(string Letter, string? Label, string FileSystem, ulong TotalBytes, ulong FreeBytes, bool IsRemovable, int? DiskNumber)
{
    public ulong UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;
    public double UsedFraction => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes;
    public string Display => string.IsNullOrWhiteSpace(Label) ? Letter : $"{Label} ({Letter})";
}

public sealed record DisksSnapshot(IReadOnlyList<PhysicalDisk> Disks, IReadOnlyList<DiskVolume> Volumes, string? Reason = null)
{
    public static DisksSnapshot None(string reason) => new([], [], reason);
    public PhysicalDisk? DiskOf(DiskVolume v) => v.DiskNumber is { } n ? Disks.FirstOrDefault(d => d.Number == n) : null;
}

// Registros crudos que entrega el adaptador de Windows (WMI/IOCTL); DiskCatalog los une SIN mezclar dispositivos.
public sealed record PhysicalRec(int Number, string Model, string? MediaType, string Bus, ulong SizeBytes, SmartInfo? Smart);
public sealed record PartitionRec(int DiskNumber, string? DriveLetter);
public sealed record VolumeRec(string Letter, string? Label, string FileSystem, ulong TotalBytes, ulong FreeBytes, bool IsRemovable);

public static class DiskCatalog
{
    /// <summary>
    /// Une discos físicos, particiones y volúmenes. Un volumen se asocia al disco que contiene la partición con su letra (si una letra aparece en varios discos —volumen distribuido— se toma el primero
    /// y NO se le atribuyen datos de los demás); un volumen sin partición conocida (unidad de red, lector sin medio) queda sin disco, y por tanto sin SMART. Los datos SMART viajan con SU disco por número.
    /// </summary>
    public static DisksSnapshot Build(IEnumerable<PhysicalRec> physical, IEnumerable<PartitionRec> partitions, IEnumerable<VolumeRec> volumes)
    {
        var disks = physical.OrderBy(p => p.Number).Select(p => new PhysicalDisk(p.Number, string.IsNullOrWhiteSpace(p.Model) ? $"Disco {p.Number}" : p.Model.Trim(), Media(p.MediaType), p.Bus ?? "", p.SizeBytes,
            string.Equals(p.Bus, "USB", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Bus, "1394", StringComparison.OrdinalIgnoreCase), p.Smart ?? SmartInfo.None("El dispositivo no entrega datos de salud"))).ToList();
        var byLetter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in partitions) if (!string.IsNullOrWhiteSpace(p.DriveLetter) && disks.Any(d => d.Number == p.DiskNumber)) byLetter.TryAdd(Norm(p.DriveLetter), p.DiskNumber);
        var vols = volumes.Select(v => new DiskVolume(Norm(v.Letter), v.Label, v.FileSystem, v.TotalBytes, v.FreeBytes, v.IsRemovable, byLetter.TryGetValue(Norm(v.Letter), out int n) ? n : null)).OrderBy(v => v.Letter, StringComparer.OrdinalIgnoreCase).ToList();
        return new DisksSnapshot(disks, vols);
    }

    private static string Norm(string letter) { var l = letter.Trim().TrimEnd('\\', '/'); return l.Length == 1 ? l.ToUpperInvariant() + ":" : l.ToUpperInvariant(); }
    private static DiskMedia Media(string? m) => m?.Trim().ToUpperInvariant() switch { "SSD" or "4" => DiskMedia.SSD, "HDD" or "3" => DiskMedia.HDD, _ => DiskMedia.Unknown };
}
