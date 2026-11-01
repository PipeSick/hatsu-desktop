using System.Net;
using WinMac.Bar.Hub;
using WinMac.SystemControl;
using Xunit;

namespace WinMac.Bar.Core.Tests;

internal sealed class FakeSys : ISystemMonitor
{
    public int Calls; public Exception? Throw; public SystemSample Next = Sample(25, 72, 57, 54);
    public static SystemSample Sample(double? cpu, double? gpu, double? cpuT, double? gpuT) => new(cpu is null ? Reading<double>.Unavailable("sin dato") : Reading<double>.Of(cpu.Value), gpu is null ? Reading<double>.Unavailable("sin GPU") : Reading<double>.Of(gpu.Value),
        cpuT is null ? Reading<double>.Unavailable("sin sensor") : Reading<double>.Of(cpuT.Value), gpuT is null ? Reading<double>.Unavailable("sin sensor") : Reading<double>.Of(gpuT.Value), new MemoryInfo(16UL << 30, 4UL << 30, 4UL << 30, 10UL << 30, 32UL << 30, null, null), null, TimeSpan.FromHours(43), Reading<int>.Of(0));
    public SystemSample Read() { Calls++; if (Throw is not null) throw Throw; return Next; }
}

internal sealed class FakeNet : INetworkMonitor
{
    public int CounterCalls, AppCalls; public Exception? Throw, AppsThrow; public NetCounters Next = new(1000, 500, new AdapterInfo("Wi-Fi", "MediaTek", "Wi-Fi", "192.168.20.175", 1_200_000_000));
    public IReadOnlyList<NetApp> Apps = [new NetApp("chrome", 10, 12, null)];
    public NetCounters ReadCounters() { CounterCalls++; if (Throw is not null) throw Throw; return Next; }
    public IReadOnlyList<NetApp> ReadApps() { AppCalls++; if (AppsThrow is not null) throw AppsThrow; return Apps; }
}

internal sealed class FakeDisks : IDiskMonitor
{
    public int CatalogCalls, IoCalls; public Exception? CatalogThrow, IoThrow; public ServiceResult EjectResult = ServiceResult.Ok; public List<string> Ejected = [];
    public DisksSnapshot Catalog = DiskCatalog.Build([new PhysicalRec(0, "NVMe", "SSD", "NVMe", 1000, new SmartInfo("Healthy", 37, 639, 1, null, null))], [new PartitionRec(0, "C")], [new VolumeRec("C:\\", "", "NTFS", 1000, 250, false)]);
    public List<DiskIo> Io = [new DiskIo(0, 1000, 500)];
    public DisksSnapshot ReadCatalog() { CatalogCalls++; if (CatalogThrow is not null) throw CatalogThrow; return Catalog; }
    public IReadOnlyList<DiskIo> ReadIo() { IoCalls++; if (IoThrow is not null) throw IoThrow; return Io; }
    public ServiceResult Eject(string volumeLetter) { Ejected.Add(volumeLetter); return EjectResult; }
}

internal sealed class NoIcons : IAppIcons { public byte[]? GetBgra(string exePath, int size) => null; }

public class HubMonitorTests
{
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc); private readonly Queue<Action> _bg = new(), _ui = new(); private readonly List<string> _log = [];
    private readonly FakeSys _sys = new(); private readonly FakeNet _net = new(); private readonly FakeDisks _disks = new(); private int _changed;
    private HubMonitor Make() { var m = new HubMonitor(new HubServices(_sys, _net, _disks, new NullWeather(), null, new NoIcons(), null), a => _bg.Enqueue(a), a => _ui.Enqueue(a), _log.Add, () => _now); m.Changed += () => _changed++; return m; }
    private void Pump() { while (_bg.Count > 0 || _ui.Count > 0) { while (_bg.Count > 0) _bg.Dequeue()(); while (_ui.Count > 0) _ui.Dequeue()(); } }

    private sealed class NullWeather : IWeatherSource
    {
        public Task<WeatherSnapshot> FetchAsync(GeoPoint place, IReadOnlyList<GeoPoint> others, CancellationToken ct) => throw new HttpRequestException();
        public Task<IReadOnlyList<GeoPoint>> SearchAsync(string name, CancellationToken ct) => Task.FromResult<IReadOnlyList<GeoPoint>>([]);
    }

    [Fact]
    public void Nothing_is_read_until_the_host_asks_and_reads_run_in_the_background()
    {
        var m = Make(); Assert.Equal(0, _sys.Calls + _net.CounterCalls + _disks.IoCalls + _disks.CatalogCalls); Assert.False(m.Sample.CpuPercent.Available);
        m.RefreshSystem(); Assert.Equal(0, _sys.Calls); Pump(); Assert.Equal(1, _sys.Calls); Assert.Equal(25, m.Sample.CpuPercent.Value); Assert.Equal(1, _changed);
    }

    [Fact]
    public void System_histories_only_record_sensors_that_exist_and_memory_is_percent_used()
    {
        var m = Make(); m.RefreshSystem(); Pump(); _sys.Next = FakeSys.Sample(40, null, null, null); m.RefreshSystem(); Pump();
        Assert.Equal([25.0, 40.0], m.CpuHistory.Values()); Assert.Equal([72.0], m.GpuHistory.Values());   // sin GPU no se dibujan ceros inventados
        Assert.Equal(2, m.MemoryHistory.Count); Assert.Equal(75, m.MemoryHistory.Values()[0], 1);
        Assert.False(m.Sample.GpuTempC.Available); Assert.Contains("sensor", m.Sample.GpuTempC.Reason);
    }

    [Fact]
    public void Reads_are_single_flight_and_a_failing_provider_keeps_the_last_data_and_does_not_block_the_next_read()
    {
        var m = Make(); m.RefreshSystem(); m.RefreshSystem(); m.RefreshSystem(); Pump(); Assert.Equal(1, _sys.Calls);
        _sys.Throw = new InvalidOperationException("PDH roto"); m.RefreshSystem(); Pump(); Assert.Equal(25, m.Sample.CpuPercent.Value); Assert.Contains(_log, l => l.Contains("PDH roto"));
        _sys.Throw = null; _sys.Next = FakeSys.Sample(10, 5, 1, 1); m.RefreshSystem(); Pump(); Assert.Equal(10, m.Sample.CpuPercent.Value);   // el fallo no dejó la bandera de «en curso» puesta
    }

    [Fact]
    public void Network_updates_rates_and_session_totals_and_reads_the_app_list_only_when_asked()
    {
        var m = Make(); m.RefreshNetwork(withApps: false); Pump(); _now = _now.AddSeconds(2); _net.Next = _net.Next with { BytesReceived = 3000, BytesSent = 700 }; m.RefreshNetwork(withApps: true); Pump();
        Assert.Equal(1000, m.Traffic.DownRate); Assert.Equal(100, m.Traffic.UpRate); Assert.Equal(2000, m.Traffic.SessionDown); Assert.Equal(1, _net.AppCalls); Assert.Equal("chrome", m.NetApps[0].Name);
        Assert.Equal("192.168.20.175", m.Traffic.Adapter!.IPv4);
    }

    [Fact]
    public void Network_failures_are_reported_without_inventing_traffic_and_apps_failures_are_independent()
    {
        var m = Make(); _net.Throw = new InvalidOperationException("sin adaptadores"); m.RefreshNetwork(true); Pump(); Assert.Null(m.Traffic.Adapter); Assert.Contains("No se pudo leer la red", m.Traffic.Reason);
        _net.Throw = null; _net.AppsThrow = new UnauthorizedAccessException(); m.RefreshNetwork(true); Pump(); Assert.NotNull(m.Traffic.Adapter); Assert.Empty(m.NetApps); Assert.Contains("aplicaciones", m.NetAppsReason);
    }

    [Fact]
    public void Disks_read_io_every_time_but_the_expensive_catalog_only_every_30_seconds_or_when_forced()
    {
        var m = Make(); m.RefreshDisks(); Pump(); Assert.Equal(1, _disks.CatalogCalls); Assert.Equal("C:", m.Disks.Volumes[0].Letter);
        _now = _now.AddSeconds(5); m.RefreshDisks(); Pump(); Assert.Equal(1, _disks.CatalogCalls); Assert.Equal(2, _disks.IoCalls);
        _now = _now.AddSeconds(31); m.RefreshDisks(); Pump(); Assert.Equal(2, _disks.CatalogCalls);
        m.RefreshDisks(forceCatalog: true); Pump(); Assert.Equal(3, _disks.CatalogCalls);
    }

    [Fact]
    public void Disk_io_is_tracked_per_physical_disk_with_rates_and_history_and_never_crossed_between_disks()
    {
        var m = Make(); m.RefreshDisks(); Pump();
        _now = _now.AddSeconds(1); _disks.Io = [new DiskIo(0, 1_001_000, 500), new DiskIo(1, 5_000, 9_000)]; m.RefreshDisks(); Pump();
        Assert.Equal(1_000_000, m.MetersOf(0)!.Read.Rate); Assert.Equal(0, m.MetersOf(0)!.Write.Rate); Assert.Equal(0, m.MetersOf(1)!.Read.Rate);   // el disco 1 apareció ahora: sin velocidad todavía
        _now = _now.AddSeconds(1); _disks.Io = [new DiskIo(0, 1_001_000, 500), new DiskIo(1, 7_000, 19_000)]; m.RefreshDisks(); Pump();
        Assert.Equal(2000, m.MetersOf(1)!.Read.Rate); Assert.Equal(10_000, m.MetersOf(1)!.Write.Rate); Assert.Equal(0, m.MetersOf(0)!.Read.Rate); Assert.Null(m.MetersOf(7));
    }

    [Fact]
    public void Disk_failures_are_isolated_the_catalog_reports_why_and_io_keeps_working()
    {
        var m = Make(); _disks.CatalogThrow = new InvalidOperationException("WMI"); m.RefreshDisks(); Pump(); Assert.Contains("No se pudo leer", m.Disks.Reason); Assert.Empty(m.Disks.Volumes); Assert.NotNull(m.MetersOf(0));
        _disks.IoThrow = new InvalidOperationException("PDH"); _disks.CatalogThrow = null; m.RefreshDisks(forceCatalog: true); Pump(); Assert.Equal("C:", m.Disks.Volumes[0].Letter);   // el catálogo se recuperó aunque la E/S siga fallando
    }

    [Fact]
    public void Ejecting_uses_the_safe_path_reports_the_result_and_rereads_the_drives_without_forcing()
    {
        var m = Make(); m.RefreshDisks(); Pump(); ServiceResult? got = null; int cat = _disks.CatalogCalls;
        _disks.EjectResult = ServiceResult.Failed("La unidad está en uso: ciérrala antes de expulsar"); m.Eject("E:", r => got = r); Pump();
        Assert.Equal(["E:"], _disks.Ejected); Assert.False(got!.Value.IsOk); Assert.Contains("en uso", got.Value.Message); Assert.Equal(cat + 1, _disks.CatalogCalls);
        _disks.EjectResult = ServiceResult.Ok; m.Eject("E:", r => got = r); Pump(); Assert.True(got!.Value.IsOk);
    }

    [Fact]
    public void An_exception_while_ejecting_becomes_a_failed_result()
    {
        var disks = new ThrowingEject(); var m = new HubMonitor(new HubServices(_sys, _net, disks, new NullWeather(), null, new NoIcons(), null), a => _bg.Enqueue(a), a => _ui.Enqueue(a), _log.Add, () => _now);
        ServiceResult? got = null; m.Eject("E:", r => got = r); Pump(); Assert.False(got!.Value.IsOk); Assert.Contains("boom", got.Value.Message);
    }

    private sealed class ThrowingEject : IDiskMonitor
    {
        public DisksSnapshot ReadCatalog() => DisksSnapshot.None("x"); public IReadOnlyList<DiskIo> ReadIo() => [];
        public ServiceResult Eject(string volumeLetter) => throw new InvalidOperationException("boom");
    }
}

public class SpeedTestRunnerTests
{
    private readonly Queue<Action> _bg = new(), _ui = new();
    private void Pump() { while (_bg.Count > 0 || _ui.Count > 0) { while (_bg.Count > 0) _bg.Dequeue()(); while (_ui.Count > 0) _ui.Dequeue()(); } }
    private static SpeedTestEngine Engine(Func<HttpRequestMessage, HttpResponseMessage>? custom = null, Action? onDownload = null) => new(new HttpClient(new WeatherTests.StubHandlerAsync(async req =>
    {
        if (custom is not null) return custom(req);
        if (req.Method == HttpMethod.Post) { await req.Content!.CopyToAsync(Stream.Null); return new HttpResponseMessage(HttpStatusCode.OK); }
        if (req.RequestUri!.Query.EndsWith("bytes=0")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        onDownload?.Invoke(); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4000]) };
    })), System.Diagnostics.Stopwatch.GetTimestamp, System.Diagnostics.Stopwatch.Frequency);

    [Fact]
    public void The_test_never_starts_by_itself_runs_once_and_ends_in_done_with_the_result()
    {
        var r = new SpeedTestRunner(Engine(), a => _bg.Enqueue(a), a => _ui.Enqueue(a)); Pump(); Assert.Equal(SpeedTestStatus.Idle, r.State.Status); Assert.Empty(_bg);
        Assert.True(r.Start()); Assert.Equal(SpeedTestStatus.Running, r.State.Status); Assert.False(r.Start());   // la segunda pulsación no lanza otra
        Pump(); Assert.Equal(SpeedTestStatus.Done, r.State.Status); Assert.NotNull(r.State.Result!.DownMbps); Assert.Null(r.State.Result.Error);
        Assert.True(r.Start());   // terminada: se puede repetir
    }

    [Fact]
    public void Cancelling_ends_in_cancelled()
    {
        SpeedTestRunner? runner = null; runner = new SpeedTestRunner(Engine(onDownload: () => runner!.Cancel()), a => _bg.Enqueue(a), a => _ui.Enqueue(a));
        runner.Start(); Pump(); Assert.Equal(SpeedTestStatus.Cancelled, runner.State.Status); Assert.True(runner.State.Result!.Cancelled);
    }

    [Fact]
    public void A_network_failure_ends_in_failed_with_a_readable_message_and_unavailable_engines_refuse_to_start()
    {
        var r = new SpeedTestRunner(Engine(_ => throw new HttpRequestException("dns")), a => _bg.Enqueue(a), a => _ui.Enqueue(a)); r.Start(); Pump();
        Assert.Equal(SpeedTestStatus.Failed, r.State.Status); Assert.Contains("sin conexión", r.State.Result!.Error);
        var none = new SpeedTestRunner(null, a => _bg.Enqueue(a), a => _ui.Enqueue(a)); Assert.False(none.Available); Assert.False(none.Start()); Assert.Equal(SpeedTestStatus.Idle, none.State.Status);
    }
}
