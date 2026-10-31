using System.Diagnostics;
using WinMac.Host;
using WinMac.Ipc;
using Xunit;

/// <summary>
/// Supervisor + canal real + procesos REALES (el módulo de mentira, lanzado con «dotnet»). Valida de punta a punta lo que no se ve con dobles:
/// arranque, listo, latido, caída, reinicio, cierre voluntario, cierre ordenado y módulos que siguen solos cuando cae el host.
/// </summary>
public class EndToEndTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "winmac-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly List<int> _spawned = [];
    private static readonly string FakeDll = FindFake();

    private static string FindFake()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "WinMac.FakeModule"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("no se encontró la raíz del repositorio");
        return Directory.GetFiles(Path.Combine(dir.FullName, "src", "WinMac.FakeModule", "bin"), "WinMac.FakeModule.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).First();
    }

    private ModuleSpec Fake(string id, string mode) => new(id, "dotnet", [FakeDll, "--id", id, "--mode", mode, "--out", _out]);
    private static readonly SupervisorPolicy Fast = new()
    {
        ReadyTimeout = TimeSpan.FromSeconds(15), HeartbeatInterval = TimeSpan.FromMilliseconds(500), MissedBeats = 3, StopGrace = TimeSpan.FromSeconds(8), CloseGrace = TimeSpan.FromSeconds(1),
        BackoffStart = TimeSpan.FromMilliseconds(100), BackoffMax = TimeSpan.FromMilliseconds(400), MaxCrashes = 5, CrashWindow = TimeSpan.FromMinutes(1), StartStagger = TimeSpan.FromMilliseconds(50),
    };

    private (IpcServer Server, Supervisor Sup, SystemProcessLauncher Launcher, string Pipe) Rig(SupervisorPolicy? policy = null)
    {
        string pipe = "winmac-e2e-" + Guid.NewGuid().ToString("N"), token = PipeNames.NewToken();
        var server = new IpcServer(new ServerOptions(pipe, token)); server.Start();
        var launcher = new SystemProcessLauncher { OnStarted = p => { lock (_spawned) _spawned.Add(p.Id); } };
        return (server, new Supervisor(launcher, new IpcHostBus(server), new SystemClock(), policy ?? Fast, pipe, token), launcher, pipe);
    }

    private int Starts(string id) { var f = Path.Combine(_out, id + ".log"); return File.Exists(f) ? File.ReadAllLines(f).Count(l => l.EndsWith(" started")) : 0; }
    private bool Logged(string id, string what) { var f = Path.Combine(_out, id + ".log"); return File.Exists(f) && File.ReadAllLines(f).Any(l => l.EndsWith(" " + what)); }
    private static async Task<bool> Until(Func<bool> c, int ms = 20000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { if (c()) return true; await Task.Delay(25); } return c(); }
    private bool AllGone() { lock (_spawned) return _spawned.All(pid => { try { using var p = Process.GetProcessById(pid); return p.HasExited; } catch (ArgumentException) { return true; } }); }

    [Fact]
    public async Task Two_modules_start_become_ready_and_close_in_order_leaving_no_process_behind()
    {
        var (server, sup, _, _) = Rig(); await using var _s = server;
        sup.Add(Fake("bar", "normal"), true); sup.Add(Fake("dock", "normal"), true); sup.Add(Fake("notch", "normal"), false);
        await sup.StartAsync();
        Assert.True(await Until(() => sup.States["bar"] == ModuleState.Running && sup.States["dock"] == ModuleState.Running), "no llegaron a «listo»: " + string.Join(", ", sup.States));
        Assert.Equal(ModuleState.Disabled, sup.States["notch"]); Assert.Equal(0, Starts("notch"));
        await sup.StopAllAsync();
        Assert.Equal(ModuleState.Stopped, sup.States["bar"]); Assert.Equal(ModuleState.Stopped, sup.States["dock"]);
        Assert.True(Logged("bar", "bye") && Logged("dock", "bye"), "no cerraron por su camino normal (shutdown → bye)");
        Assert.Equal(1, Starts("bar")); Assert.Equal(1, Starts("dock")); Assert.True(await Until(AllGone, 5000), "quedó algún proceso");
    }

    [Fact]
    public async Task Turning_a_real_module_off_and_on_through_the_config_closes_only_it_and_starts_it_again()
    {
        var (server, sup, _, _) = Rig(); await using var _s = server; var bus = new IpcHostBus(server);
        var cfgPath = Path.Combine(_out, "winmac.json"); Directory.CreateDirectory(_out);
        var cfg = new ConfigService(cfgPath, new HostConfig()); var ctl = new HostController(cfg, sup, bus); ctl.Start();
        sup.Add(Fake("bar", "normal"), true); sup.Add(Fake("notch", "normal"), true); await sup.StartAsync();
        Assert.True(await Until(() => sup.States["bar"] == ModuleState.Running && sup.States["notch"] == ModuleState.Running), "no llegaron a «listo»: " + string.Join(", ", sup.States));
        cfg.SetModuleEnabled("notch", false);
        Assert.True(await Until(() => sup.States["notch"] == ModuleState.Disabled), "la Isla no se desactivó: " + string.Join(", ", sup.States));
        Assert.True(Logged("notch", "bye"), "no cerró por su camino normal"); Assert.Equal(ModuleState.Running, sup.States["bar"]); Assert.Equal(1, Starts("bar"));   // la Barra ni se enteró
        cfg.SetModuleEnabled("notch", true);
        Assert.True(await Until(() => sup.States["notch"] == ModuleState.Running && Starts("notch") == 2), "la Isla no volvió: " + string.Join(", ", sup.States));
        Assert.False(HostConfig.Load(cfgPath).Modules["notch"].Enabled is false);   // y el estado final quedó guardado
        await sup.StopAllAsync(); Assert.True(await Until(AllGone, 5000), "quedó algún proceso");
    }

    [Fact]
    public async Task A_crashing_module_is_restarted_then_given_up_on_while_the_other_module_is_untouched()
    {
        var (server, sup, _, _) = Rig(); await using var _s = server;
        sup.Add(Fake("bar", "crash"), true); sup.Add(Fake("dock", "normal"), true); await sup.StartAsync();
        Assert.True(await Until(() => sup.States["bar"] == ModuleState.Failed, 40000), "la barra no llegó a Failed: " + string.Join(", ", sup.States));
        Assert.True(Starts("bar") >= 5, $"arranques de bar: {Starts("bar")}"); int after = Starts("bar"); await Task.Delay(1200); Assert.Equal(after, Starts("bar"));   // sin bucle
        Assert.Equal(ModuleState.Running, sup.States["dock"]); Assert.Equal(1, Starts("dock"));
        await sup.StopAllAsync(); Assert.True(await Until(AllGone, 5000));
    }

    [Fact]
    public async Task A_module_that_quits_on_its_own_with_bye_is_not_brought_back()
    {
        var (server, sup, _, _) = Rig(); await using var _s = server; sup.Add(Fake("dock", "exit-clean"), true); await sup.StartAsync();
        Assert.True(await Until(() => sup.States["dock"] == ModuleState.Stopped, 20000)); await Task.Delay(1500); Assert.Equal(1, Starts("dock")); Assert.Equal(ModuleState.Stopped, sup.States["dock"]);
    }

    [Fact]
    public async Task A_module_that_goes_silent_is_detected_by_the_heartbeat_and_restarted()
    {
        var (server, sup, _, _) = Rig(); await using var _s = server; sup.Add(Fake("bar", "hang"), true); await sup.StartAsync();
        Assert.True(await Until(() => Starts("bar") >= 2, 30000), $"no se reinició el módulo colgado (arranques: {Starts("bar")})");
        await sup.StopAllAsync(); Assert.True(await Until(AllGone, 8000));
    }

    [Fact]
    public async Task A_module_that_never_connects_is_given_up_on_after_the_ready_timeout_and_restarted()
    {
        var (server, sup, _, _) = Rig(Fast with { ReadyTimeout = TimeSpan.FromSeconds(2) }); await using var _s = server; sup.Add(Fake("bar", "never-ready"), true); await sup.StartAsync();
        Assert.True(await Until(() => Starts("bar") >= 2, 30000), $"arranques: {Starts("bar")}"); await sup.StopAllAsync(); Assert.True(await Until(AllGone, 8000));
    }

    [Fact]
    public async Task A_module_that_ignores_shutdown_is_terminated_as_the_last_resort()
    {
        var (server, sup, _, _) = Rig(Fast with { StopGrace = TimeSpan.FromSeconds(1), CloseGrace = TimeSpan.FromMilliseconds(500) }); await using var _s = server;
        sup.Add(Fake("bar", "ignore-shutdown"), true); await sup.StartAsync(); Assert.True(await Until(() => sup.States["bar"] == ModuleState.Running));
        await sup.StopAllAsync(); Assert.True(Logged("bar", "shutdown")); Assert.True(await Until(AllGone, 8000), "el módulo rebelde sigue vivo");
    }

    [Fact]
    public async Task A_missing_executable_fails_that_module_only()
    {
        var (server, sup, _, _) = Rig(); await using var _s = server;
        sup.Add(new ModuleSpec("bar", Path.Combine(_out, "no-existe.exe"), []), true); sup.Add(Fake("dock", "normal"), true); await sup.StartAsync();
        Assert.True(await Until(() => sup.States["dock"] == ModuleState.Running)); Assert.Equal(ModuleState.Failed, sup.States["bar"]);
        await sup.StopAllAsync(); Assert.True(await Until(AllGone, 5000));
    }

    [Fact]
    public async Task When_the_channel_to_the_host_is_lost_a_hosted_module_keeps_running_alone_and_reconnects_when_it_returns()
    {
        string pipe = "winmac-e2e-" + Guid.NewGuid().ToString("N"), token = PipeNames.NewToken();
        var launcher = new SystemProcessLauncher { OnStarted = p => { lock (_spawned) _spawned.Add(p.Id); } };
        var server = new IpcServer(new ServerOptions(pipe, token)); server.Start();
        var proc = launcher.Start(Fake("dock", "normal"), pipe, token);
        Assert.True(await Until(() => server.IsConnected("dock")));
        await server.DisposeAsync();                                                   // se pierde el canal (el proceso host, en Windows, habría cerrado al módulo con el Job Object)
        await Task.Delay(2000); Assert.False(proc.Exited.IsCompleted, "el módulo se cerró al caer el host");   // sigue funcionando solo
        await using var again = new IpcServer(new ServerOptions(pipe, token)); again.Start();
        Assert.True(await Until(() => again.IsConnected("dock"), 15000), "no se reconectó al volver el host");
        again.Send("dock", MsgType.Shutdown); Assert.Equal(0, await proc.Exited.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Without_hosted_a_module_runs_exactly_as_before_and_ignores_the_host_entirely()
    {
        var psi = new ProcessStartInfo("dotnet") { UseShellExecute = false }; foreach (var a in new[] { FakeDll, "--id", "solo", "--mode", "normal", "--out", _out }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!; lock (_spawned) _spawned.Add(p.Id);
        Assert.True(await Until(() => Logged("solo", "standalone"))); await Task.Delay(500); Assert.False(p.HasExited);
    }

    public void Dispose()
    {
        lock (_spawned) foreach (var pid in _spawned) { try { using var p = Process.GetProcessById(pid); if (!p.HasExited) p.Kill(true); } catch (Exception) { } }
        try { Directory.Delete(_out, true); } catch (Exception) { }
    }
}
