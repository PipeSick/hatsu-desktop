using WinMac.Apps;
using Xunit;

public class IdentityTests
{
    [Fact]
    public void Win32_id_is_stable_across_case_slashes_and_trailing_separators()
    {
        var a = AppIdentity.FromExe(@"C:\Windows\System32\notepad.exe"); var b = AppIdentity.FromExe("c:/windows/system32/NOTEPAD.EXE");
        Assert.Equal(a.Id, b.Id); Assert.Equal("exe:C:\\WINDOWS\\SYSTEM32\\NOTEPAD.EXE", a.Id); Assert.Equal(AppKind.Win32, a.Kind);
    }

    [Fact]
    public void Packaged_id_uses_the_aumid_and_activates_through_appsfolder()
    {
        var p = AppIdentity.FromAumid("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculadora", @"C:\Program Files\WindowsApps\x\CalculatorApp.exe");
        Assert.Equal("aumid:microsoft.windowscalculator_8wekyb3d8bbwe!app", p.Id); Assert.Equal(AppKind.Packaged, p.Kind);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", p.LaunchTarget);   // NO el .exe del paquete
    }

    [Fact]
    public void SameApp_matches_by_id_or_by_executable()
    {
        var pinned = AppIdentity.FromExe(@"C:\A\app.exe"); var running = AppIdentity.FromAumid("Vendor.App_1!App", "App", @"c:\a\APP.exe");
        Assert.True(pinned.SameApp(running)); Assert.True(running.SameApp(pinned));
        Assert.False(pinned.SameApp(AppIdentity.FromExe(@"C:\B\app.exe"))); Assert.False(pinned.SameApp(null));
    }

    [Fact]
    public void From_rebuilds_identities_from_legacy_and_new_config_fields()
    {
        Assert.Equal(AppKind.Win32, AppIdentity.From(null, @"C:\x\a.exe", null, "A")!.Kind);
        Assert.Equal(AppKind.Packaged, AppIdentity.From(null, null, "P.App_1!App", null)!.Kind);
        Assert.Equal(AppKind.Packaged, AppIdentity.From("aumid:p.app_1!app", null, null, null)!.Kind);
        Assert.Null(AppIdentity.From(null, null, null, null));
        Assert.Equal("Notepad", AppIdentity.From(null, @"C:\Windows\notepad.exe", null, null)!.DisplayName);
    }
}

public class IconModelTests
{
    private static IconBitmap Solid(int w, int h, byte a = 255) { var b = new byte[w * h * 4]; for (int i = 0; i < b.Length; i += 4) { b[i] = 10; b[i + 1] = 20; b[i + 2] = 30; b[i + 3] = a; } return new IconBitmap(w, h, b); }

    [Fact] public void Valid_icon_passes() => Assert.True(IconValidator.IsValid(Solid(64, 64)));

    [Theory]
    [InlineData(0, 0)] [InlineData(2000, 8)] [InlineData(1, 64)]
    public void Impossible_sizes_are_rejected(int w, int h)
    {
        Assert.NotNull(IconValidator.Reject(new IconBitmap(w, h, new byte[Math.Max(0, w * h * 4 > 100_000_000 ? 4 : w * h * 4)])));
    }

    [Fact] public void Null_and_inconsistent_buffers_are_rejected() { Assert.NotNull(IconValidator.Reject(null)); Assert.NotNull(IconValidator.Reject(new IconBitmap(8, 8, new byte[10]))); }
    [Fact] public void Fully_transparent_icon_is_rejected() => Assert.NotNull(IconValidator.Reject(Solid(32, 32, 0)));

    [Theory]
    [InlineData(256, 256, 60, 60, 60, 60, 0, 0)]
    [InlineData(256, 128, 60, 60, 60, 30, 0, 15)]    // apaisado: no se deforma, se centra
    [InlineData(100, 200, 60, 60, 30, 60, 15, 0)]    // vertical
    public void AspectFit_never_distorts_and_centers(int sw, int sh, double bw, double bh, double ew, double eh, double ex, double ey)
    {
        var (x, y, w, h) = IconLayout.AspectFit(sw, sh, 0, 0, bw, bh);
        Assert.Equal(ew, w, 6); Assert.Equal(eh, h, 6); Assert.Equal(ex, x, 6); Assert.Equal(ey, y, 6); Assert.Equal((double)sw / sh, w / h, 6);
    }

    [Fact]
    public void Fallback_hue_is_deterministic_and_spreads()
    {
        Assert.Equal(FallbackIcon.Hue("exe:C:\\A\\B.EXE"), FallbackIcon.Hue("exe:C:\\A\\B.EXE"));
        Assert.InRange(FallbackIcon.Hue("x"), 0, 1);
        var hues = Enumerable.Range(0, 40).Select(i => Math.Round(FallbackIcon.Hue("app" + i), 2)).Distinct().Count(); Assert.True(hues > 20);
        Assert.Equal("W", FallbackIcon.Initial("whatsapp")); Assert.Equal("?", FallbackIcon.Initial(""));
    }

    [Fact]
    public void Override_path_changes_the_request_key() =>
        Assert.NotEqual(new IconRequest(AppIdentity.FromExe(@"C:\a.exe")).Key, new IconRequest(AppIdentity.FromExe(@"C:\a.exe"), @"C:\mi.png").Key);
}

public class CacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wm-icons-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private static IconBitmap Bmp(byte seed = 7) { var b = new byte[16 * 16 * 4]; for (int i = 0; i < b.Length; i += 4) { b[i] = seed; b[i + 1] = 2; b[i + 2] = 3; b[i + 3] = 255; } return new IconBitmap(16, 16, b); }

    [Fact]
    public void Memory_cache_is_LRU()
    {
        var c = new IconCache(2); c.PutMemory("a", Bmp()); c.PutMemory("b", Bmp()); Assert.NotNull(c.GetMemory("a")); c.PutMemory("c", Bmp());   // «a» se usó: se expulsa «b»
        Assert.NotNull(c.GetMemory("a")); Assert.Null(c.GetMemory("b")); Assert.NotNull(c.GetMemory("c")); Assert.Equal(2, c.MemoryCount);
    }

    [Fact]
    public void Disk_cache_roundtrips_and_respects_the_stamp()
    {
        var c = new IconCache(8, _dir); var b = Bmp(); c.PutDisk("k", "s1", b);
        var got = c.GetDisk("k", "s1"); Assert.NotNull(got); Assert.Equal(b.Bgra, got!.Bgra); Assert.Equal(16, got.Width);
        Assert.Null(c.GetDisk("k", "s2"));                       // el origen cambió: se descarta
        Assert.Null(c.GetDisk("k", "s1"));                       // y el archivo se borró
    }

    [Fact]
    public void Corrupt_disk_entries_are_ignored_and_removed_without_throwing()
    {
        var c = new IconCache(8, _dir); c.PutDisk("k", "s", Bmp()); var f = Directory.GetFiles(_dir).Single();
        File.WriteAllBytes(f, new byte[] { 1, 2, 3, 4, 5 }); Assert.Null(c.GetDisk("k", "s")); Assert.False(File.Exists(f));
        c.PutDisk("k2", "s", Bmp()); f = Directory.GetFiles(_dir).Single(); var data = File.ReadAllBytes(f); File.WriteAllBytes(f, data[..(data.Length / 2)]); Assert.Null(c.GetDisk("k2", "s"));
    }

    [Fact] public void Unwritable_disk_location_does_not_throw() { var c = new IconCache(2, Path.Combine(_dir, "x\0y")); c.PutDisk("k", "s", Bmp()); Assert.Null(c.GetDisk("k", "s")); }
}

public class ServiceTests
{
    private sealed class Fake : IIconProvider
    {
        public int Calls; public Func<IconRequest, IconBitmap?> Impl = _ => Good();
        public string Stamp(IconRequest r) => "s"; public IconBitmap? Load(IconRequest r) { Interlocked.Increment(ref Calls); return Impl(r); }
    }
    private static IconBitmap Good() { var b = new byte[32 * 32 * 4]; for (int i = 0; i < b.Length; i += 4) { b[i] = 1; b[i + 1] = 2; b[i + 2] = 3; b[i + 3] = 255; } return new IconBitmap(32, 32, b); }
    private static IconRequest Req(string n = "a") => new(AppIdentity.FromExe($@"C:\{n}.exe"));

    [Fact]
    public async Task TryGet_is_nonblocking_then_the_icon_arrives_and_the_event_fires()
    {
        var f = new Fake(); using var s = new IconService(f, new IconCache()); var tcs = new TaskCompletionSource<IconRequest>(); s.Loaded += r => tcs.TrySetResult(r);
        Assert.Null(s.TryGet(Req())); var r = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(Req().Key, r.Key);
        Assert.NotNull(s.TryGet(Req())); Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_icon_load_it_once()
    {
        var f = new Fake { Impl = _ => { Thread.Sleep(100); return Good(); } }; using var s = new IconService(f, new IconCache());
        var all = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => s.GetAsync(Req()))); Assert.All(all, b => Assert.NotNull(b)); Assert.Equal(1, f.Calls);
    }

    [Fact]
    public async Task A_provider_that_hangs_times_out_without_blocking_the_caller_or_other_icons()
    {
        var gate = new ManualResetEventSlim(); var f = new Fake { Impl = r => { if (r.App.Id.Contains("HANG")) { gate.Wait(TimeSpan.FromSeconds(20)); return Good(); } return Good(); } };
        using var s = new IconService(f, new IconCache(), loadTimeout: TimeSpan.FromMilliseconds(200), workers: 2);
        var sw = System.Diagnostics.Stopwatch.StartNew(); var hung = s.GetAsync(Req("hang")); Assert.Null(s.TryGet(Req("hang"))); Assert.True(sw.ElapsedMilliseconds < 100);   // TryGet no bloquea
        Assert.NotNull(await s.GetAsync(Req("ok")).WaitAsync(TimeSpan.FromSeconds(5)));          // otro icono se sirve igualmente
        Assert.Null(await hung.WaitAsync(TimeSpan.FromSeconds(5))); Assert.Equal(1, s.Stats.TimedOut); gate.Set();
    }

    [Fact]
    public async Task Exceptions_and_corrupt_icons_become_failures_and_are_not_retried_until_the_retry_time()
    {
        var now = DateTime.UtcNow; var f = new Fake { Impl = r => r.App.Id.Contains("BOOM") ? throw new InvalidOperationException("x") : new IconBitmap(4, 4, new byte[3]) };
        using var s = new IconService(f, new IconCache(), retryAfter: TimeSpan.FromMinutes(5), clock: () => now);
        Assert.Null(await s.GetAsync(Req("boom"))); Assert.Null(await s.GetAsync(Req("corrupt")));
        Assert.Equal(1, s.Stats.Failed); Assert.Equal(1, s.Stats.Rejected); int calls = f.Calls;
        Assert.Null(await s.GetAsync(Req("boom"))); Assert.Null(s.TryGet(Req("corrupt"))); await Task.Delay(50); Assert.Equal(calls, f.Calls);   // caché negativa
        now += TimeSpan.FromMinutes(6); f.Impl = _ => Good(); Assert.NotNull(await s.GetAsync(Req("boom")).WaitAsync(TimeSpan.FromSeconds(5)));   // pasado el plazo, reintenta
    }

    [Fact]
    public async Task The_disk_cache_serves_a_second_run_without_calling_the_provider()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wm-svc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f1 = new Fake(); using (var s1 = new IconService(f1, new IconCache(8, dir))) Assert.NotNull(await s1.GetAsync(Req()));
            var f2 = new Fake { Impl = _ => throw new InvalidOperationException("no debería llamarse") }; using var s2 = new IconService(f2, new IconCache(8, dir));
            Assert.NotNull(await s2.GetAsync(Req()).WaitAsync(TimeSpan.FromSeconds(5))); Assert.Equal(0, f2.Calls); Assert.Equal(1, s2.Stats.DiskHits);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Invalidate_forces_a_reload_for_a_changed_override()
    {
        var f = new Fake(); using var s = new IconService(f, new IconCache()); var app = AppIdentity.FromExe(@"C:\a.exe");
        Assert.NotNull(await s.GetAsync(new IconRequest(app))); s.Invalidate(app); Assert.NotNull(await s.GetAsync(new IconRequest(app)).WaitAsync(TimeSpan.FromSeconds(5))); Assert.Equal(2, f.Calls);
    }
}
