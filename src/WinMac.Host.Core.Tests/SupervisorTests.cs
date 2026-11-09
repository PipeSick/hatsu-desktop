using WinMac.Host;
using WinMac.Ipc;
using Xunit;

public class StartupAndIsolationTests
{
    [Fact]
    public async Task Enabled_modules_start_in_order_and_a_disabled_one_is_never_launched()
    {
        var f = new Fixture().With(("bar", true), ("dock", true), ("notch", false)); await f.StartAllAsync();
        Assert.Equal(["bar", "dock"], f.Launcher.Started.Select(s => s.Id)); Assert.Equal(ModuleState.Disabled, f.State("notch")); Assert.Equal(ModuleState.Starting, f.State("bar"));
        await f.ReadyAsync("bar"); Assert.Equal(ModuleState.Running, f.State("bar"));
    }

    [Fact]
    public async Task A_module_without_an_executable_is_Failed_and_the_others_keep_running()
    {
        var f = new Fixture().With(("bar", true), ("dock", true)); f.Launcher.Missing.Add("bar"); await f.StartAllAsync(); await f.ReadyAsync("dock");
        Assert.Equal(ModuleState.Failed, f.State("bar")); Assert.Equal(ModuleState.Running, f.State("dock")); Assert.Equal(1, f.Launcher.Count("dock"));
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(60)); Assert.Equal(0, f.Launcher.Count("bar"));   // no se reintenta
    }

    [Fact]
    public async Task A_module_that_keeps_crashing_never_touches_the_other_one()
    {
        var f = new Fixture().With(("bar", true), ("dock", true)); await f.StartAllAsync(); await f.ReadyAsync("dock");
        f.Bus.OnSend = (m, t) => { if (t == "ping" && m == "dock") f.Bus.RaisePong(m); return true; };   // el Dock está sano y contesta a los latidos
        for (int i = 1; i <= 5; i++) { f.Launcher.Last("bar").Exit(7); await Task.Delay(20); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(Math.Pow(2, i - 1) + 0.1)); }
        Assert.Equal(ModuleState.Failed, f.State("bar")); Assert.Equal(ModuleState.Running, f.State("dock")); Assert.Equal(1, f.Launcher.Count("dock")); Assert.False(f.Launcher.Last("dock").Killed);
    }

    [Fact]
    public async Task Known_pids_are_those_of_running_modules_only()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); var p = f.Launcher.Last("bar");
        Assert.True(f.Sup.IsKnownPid((uint)p.Id)); Assert.False(f.Sup.IsKnownPid(99999));
        p.Exit(0); await Task.Delay(30); Assert.False(f.Sup.IsKnownPid((uint)p.Id));
    }
}

public class RestartPolicyTests
{
    [Fact]
    public async Task A_crash_restarts_the_module_with_growing_waits_1_2_4_seconds()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20); Assert.Equal(ModuleState.Crashed, f.State("bar"));
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(0.9)); Assert.Equal(1, f.Launcher.Count("bar")); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(0.2)); Assert.Equal(2, f.Launcher.Count("bar"));
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.9)); Assert.Equal(2, f.Launcher.Count("bar")); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(0.2)); Assert.Equal(3, f.Launcher.Count("bar"));
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(3.9)); Assert.Equal(3, f.Launcher.Count("bar")); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(0.2)); Assert.Equal(4, f.Launcher.Count("bar"));
    }

    [Fact]
    public async Task Five_crashes_in_two_minutes_open_the_circuit_and_stop_retrying()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync();
        for (int i = 1; i <= 5; i++) { f.Launcher.Last("bar").Exit(7); await Task.Delay(20); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(Math.Pow(2, i - 1) + 0.1)); }
        Assert.Equal(ModuleState.Failed, f.State("bar")); Assert.Equal(5, f.Launcher.Count("bar"));
        await f.Clock.AdvanceAsync(TimeSpan.FromMinutes(5)); Assert.Equal(5, f.Launcher.Count("bar"));   // sin bucle
        lock (f.Log) Assert.Contains(f.Log, l => l.Contains("cortacircuito"));
    }

    [Fact]
    public async Task A_module_that_ran_stably_gets_a_fresh_one_second_wait()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.1)); Assert.Equal(2, f.Launcher.Count("bar"));   // la espera siguiente sería 2 s…
        await f.ReadyAsync("bar"); await f.AdvanceHealthyAsync(TimeSpan.FromSeconds(61), "bar");                                                                   // …pero aguantó más de 60 s
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.1)); Assert.Equal(3, f.Launcher.Count("bar"));
    }

    [Fact]
    public async Task A_voluntary_quit_with_bye_and_exit_code_zero_is_not_restarted()
    {
        var f = new Fixture().With(("dock", true)); await f.StartAllAsync(); await f.ReadyAsync("dock");
        f.Bus.RaiseBye("dock"); f.Launcher.Last("dock").Exit(0); await Task.Delay(30);
        Assert.Equal(ModuleState.Stopped, f.State("dock")); await f.Clock.AdvanceAsync(TimeSpan.FromMinutes(2)); Assert.Equal(1, f.Launcher.Count("dock"));
    }

    [Theory]
    [InlineData(false, 0)]   // código 0 pero sin «bye»: no es un cierre voluntario fiable
    [InlineData(true, 3)]    // «bye» pero con código de error
    [InlineData(false, 137)] // lo mataron
    public async Task Any_other_exit_is_a_crash_and_restarts(bool bye, int code)
    {
        var f = new Fixture().With(("dock", true)); await f.StartAllAsync(); await f.ReadyAsync("dock");
        if (bye) f.Bus.RaiseBye("dock"); f.Launcher.Last("dock").Exit(code); await Task.Delay(30);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.2)); Assert.Equal(2, f.Launcher.Count("dock"));
    }

    [Fact]
    public async Task A_module_that_stops_answering_heartbeats_is_killed_and_restarted()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar"); var first = f.Launcher.Last("bar");
        f.Bus.OnSend = null; await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(14)); Assert.False(first.Killed);                  // 3 latidos perdidos (15 s) todavía no
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(7)); Assert.True(first.Killed); Assert.True(f.Bus.Count("bar", "ping") >= 3);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.5)); Assert.Equal(2, f.Launcher.Count("bar"));
    }

    [Fact]
    public async Task A_healthy_module_answering_pings_is_left_alone_for_minutes()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        await f.AdvanceHealthyAsync(TimeSpan.FromMinutes(3), "bar"); Assert.Equal(1, f.Launcher.Count("bar")); Assert.True(f.Bus.Count("bar", "ping") >= 30);
    }

    [Fact]
    public async Task A_module_that_never_becomes_ready_is_killed_and_restarted()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); var first = f.Launcher.Last("bar");
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(9)); Assert.False(first.Killed); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(8)); Assert.True(first.Killed);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.5)); Assert.Equal(2, f.Launcher.Count("bar"));
    }
}

public class ShutdownTests
{
    private static void Cooperative(Fixture f) => f.Bus.OnSend = (m, t) => { if (t == "shutdown") f.Launcher.Last(m).Exit(0); return true; };

    [Fact]
    public async Task Closing_asks_every_module_to_shut_down_in_reverse_order_and_nobody_is_killed()
    {
        var f = new Fixture().With(("bar", true), ("dock", true), ("notch", false)); await f.StartAllAsync(); await f.ReadyAsync("bar"); await f.ReadyAsync("dock"); Cooperative(f);
        await f.Sup.StopAllAsync();
        lock (f.Bus.Sent) Assert.Equal(["dock", "bar"], f.Bus.Sent.Where(s => s.Type == "shutdown").Select(s => s.Module));
        Assert.Equal(ModuleState.Stopped, f.State("bar")); Assert.Equal(ModuleState.Stopped, f.State("dock")); Assert.Equal(ModuleState.Disabled, f.State("notch"));
        Assert.False(f.Launcher.Last("bar").Killed || f.Launcher.Last("dock").Killed || f.Launcher.Last("bar").CloseRequested);
    }

    [Fact]
    public async Task A_module_that_ignores_shutdown_gets_a_polite_close_request_and_only_then_is_terminated()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar"); var p = f.Launcher.Last("bar");
        var stop = f.Sup.StopAllAsync(); await Task.Delay(20);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(2.9)); Assert.False(p.CloseRequested);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(0.2)); Assert.True(p.CloseRequested); Assert.False(p.Killed);
        await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(1.1)); Assert.True(p.Killed);
        await stop.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(ModuleState.Stopped, f.State("bar"));
    }

    [Fact]
    public async Task Closing_while_a_module_waits_to_restart_cancels_the_restart()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20); Assert.Equal(ModuleState.Crashed, f.State("bar"));
        await f.Sup.StopAllAsync(); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(30)); Assert.Equal(1, f.Launcher.Count("bar")); Assert.Equal(ModuleState.Stopped, f.State("bar"));
    }

    [Fact]
    public async Task Closing_with_a_failed_module_still_closes_the_rest_and_keeps_it_Failed()
    {
        var f = new Fixture().With(("bar", true), ("dock", true)); f.Launcher.Missing.Add("bar"); await f.StartAllAsync(); await f.ReadyAsync("dock"); Cooperative(f);
        await f.Sup.StopAllAsync(); Assert.Equal(ModuleState.Failed, f.State("bar")); Assert.Equal(ModuleState.Stopped, f.State("dock"));
    }
}

public class HostConfigTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "winmac-host-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void A_config_from_before_WinMac_2_loads_with_safe_defaults_for_every_new_option()
    {
        // winmac.json escrito por una versión anterior: sin capturas, portapapeles ni extensiones (configuración unificada, docs/31)
        var f = Tmp();
        try
        {
            File.WriteAllText(f, """{ "version": 2, "modules": { "bar": { "enabled": true } }, "notch": { "position": "TopLeft", "glassLevel": 40, "lyricsEnabled": true }, "bar": { "Clock": true } }""");
            var c = HostConfig.Load(f);
            Assert.Equal("TopLeft", c.Notch!.Position); Assert.True(c.Notch.LyricsEnabled);   // lo que había se conserva (letras incluidas)
            Assert.False(c.Notch.ClipboardPersist); Assert.True(c.Notch.ClipboardSkipSecrets); Assert.Equal(50, c.Notch.ClipboardLimit);   // privacidad por defecto
            Assert.True(c.Bar!.CaptureHotkeys); Assert.Equal("Png", c.Bar.CaptureFormat); Assert.Null(c.Bar.CaptureFolder); Assert.True(c.Bar.CaptureCopyToClipboard);   // a pedido del propietario: la captura se copia al portapapeles de inmediato (se puede apagar en Ajustes)
            Assert.Contains("activity", c.Notch.Sections.Select(x => x.Id));                    // pestañas nuevas añadidas al final sin perder el orden guardado
        }
        finally { File.Delete(f); }
    }

    [Fact]
    public void Defaults_start_every_module_including_the_notch_and_autostart_is_off()
    {
        var c = new HostConfig(); Assert.True(c.Modules["bar"].Enabled && c.Modules["dock"].Enabled && c.Modules["notch"].Enabled); Assert.False(c.Autostart); Assert.Equal(3, c.Version);
    }

    [Fact]
    public void Loading_is_tolerant_and_unspecified_modules_keep_their_defaults()
    {
        var f = Tmp();
        try
        {
            Assert.Equal(new HostConfig().Modules.Keys.Order(), HostConfig.Load(f).Modules.Keys.Order());                  // ausente
            File.WriteAllText(f, "{ esto no es json"); Assert.True(HostConfig.Load(f).Modules["bar"].Enabled);                // corrupto
            File.WriteAllText(f, "{\"modules\":{\"DOCK\":{\"enabled\":false},\"notch\":{\"enabled\":true}}}");
            var c = HostConfig.Load(f); Assert.False(c.Modules["dock"].Enabled); Assert.True(c.Modules["bar"].Enabled); Assert.True(c.Modules["notch"].Enabled);
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }

    [Fact]
    public void Resolve_gives_the_start_order_default_paths_and_test_overrides()
    {
        var c = new HostConfig { Modules = new HostConfig().Modules.ToDictionary(k => k.Key, v => v.Value) }; c.Modules["bar"] = new ModuleConfig { Path = "/otro/bar", Args = ["--x"] };
        var r = c.Resolve("/app").ToList();
        Assert.Equal(["bar", "dock", "notch", "connect"], r.Select(x => x.Spec.Id)); Assert.False(r[3].Enabled); Assert.Equal("/otro/bar", r[0].Spec.ExePath); Assert.Equal(["--x"], r[0].Spec.Args);
        Assert.EndsWith("HatsuDesktop.Dock.exe", r[1].Spec.ExePath); Assert.StartsWith("/app", r[1].Spec.ExePath); Assert.True(r[2].Enabled);
        Assert.EndsWith("NotchWin.App.exe", r[2].Spec.ExePath);
    }

    [Fact]
    public void An_optional_module_without_its_executable_is_not_installed_but_the_required_ones_are_still_available()
    {
        var r = new HostConfig().Resolve("/app", exists: _ => false).ToList();
        Assert.True(r[0].Available && r[1].Available);   // la Barra y el Dock se tratan como obligatorios: si faltan, es un fallo (Failed), no «no instalado»
        Assert.False(r[2].Available);
        Assert.True(new HostConfig().Resolve("/app", exists: _ => true).All(x => x.Available));
    }

    [Fact]
    public void Save_is_atomic_roundtrips_and_leaves_no_temp_file()
    {
        var f = Tmp();
        try
        {
            var c = new HostConfig { Notch = new NotchSettings { GlassLevel = 33, Position = "TopLeft" } };
            HostConfig.Save(f, c); HostConfig.Save(f, c with { Autostart = true });   // crea y luego reemplaza
            var l = HostConfig.Load(f); Assert.True(l.Autostart); Assert.Equal(33, l.Notch!.GlassLevel); Assert.Equal("TopLeft", l.Notch.Position); Assert.False(File.Exists(f + ".tmp"));
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }
}

public class IpcHostBusTests
{
    [Fact]
    public async Task The_bus_maps_ready_pong_and_bye_from_the_real_channel_and_sends_pings()
    {
        string pipe = "winmac-test-" + Guid.NewGuid().ToString("N"); await using var server = new IpcServer(new ServerOptions(pipe, "t")); server.Start(); var bus = new IpcHostBus(server);
        var got = new List<string>(); bus.Ready += m => { lock (got) got.Add("ready:" + m); }; bus.Pong += m => { lock (got) got.Add("pong:" + m); }; bus.Bye += m => { lock (got) got.Add("bye:" + m); }; bus.Disconnected += m => { lock (got) got.Add("down:" + m); };
        var client = new IpcClient(new ClientOptions(pipe, "t", "dock")); client.Connected += () => client.Event(Topics.ModuleReady, new { }); client.Start();
        Assert.True(await Wait(() => { lock (got) return got.Contains("ready:dock"); }));
        Assert.True(bus.Send("dock", MsgType.Ping)); Assert.True(await Wait(() => { lock (got) return got.Contains("pong:dock"); }));
        await client.SayByeAsync(TimeSpan.FromSeconds(2)); Assert.True(await Wait(() => { lock (got) return got.Contains("bye:dock"); }));
        await client.DisposeAsync(); Assert.True(await Wait(() => { lock (got) return got.Contains("down:dock"); }));
    }

    private static async Task<bool> Wait(Func<bool> c) { for (int i = 0; i < 300; i++) { if (c()) return true; await Task.Delay(20); } return c(); }
}

public class ManualReopenTests
{
    [Fact]
    public async Task A_module_closed_on_purpose_can_be_reopened_by_the_user()
    {
        var f = new Fixture().With(("dock", true)); await f.StartAllAsync(); await f.ReadyAsync("dock");
        f.Bus.RaiseBye("dock"); f.Launcher.Last("dock").Exit(0); await Task.Delay(30); Assert.Equal(ModuleState.Stopped, f.State("dock"));
        Assert.True(f.Sup.Restart("dock")); await Task.Delay(30); Assert.Equal(2, f.Launcher.Count("dock"));
        await f.ReadyAsync("dock"); Assert.Equal(ModuleState.Running, f.State("dock"));
    }

    [Fact]
    public async Task A_failed_module_can_be_reopened_and_gets_a_clean_crash_history()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync();
        for (int i = 1; i <= 5; i++) { f.Launcher.Last("bar").Exit(7); await Task.Delay(20); await f.Clock.AdvanceAsync(TimeSpan.FromSeconds(Math.Pow(2, i - 1) + 0.1)); }
        Assert.Equal(ModuleState.Failed, f.State("bar"));
        Assert.True(f.Sup.Restart("bar")); await Task.Delay(30); Assert.Equal(6, f.Launcher.Count("bar"));
        f.Launcher.Last("bar").Exit(7); await Task.Delay(20); Assert.Equal(ModuleState.Crashed, f.State("bar"));   // el cortacircuito no se dispara de inmediato: el historial se limpió
    }

    [Fact]
    public async Task Reopening_does_nothing_for_running_or_disabled_modules_or_unknown_ids()
    {
        var f = new Fixture().With(("bar", true), ("notch", false)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        Assert.False(f.Sup.Restart("bar")); Assert.False(f.Sup.Restart("notch")); Assert.False(f.Sup.Restart("nope"));
        Assert.Equal(1, f.Launcher.Count("bar")); Assert.Equal(0, f.Launcher.Count("notch"));
    }

    [Fact]
    public async Task RestartStopped_reopens_only_the_closed_ones()
    {
        var f = new Fixture().With(("bar", true), ("dock", true)); await f.StartAllAsync(); await f.ReadyAsync("bar"); await f.ReadyAsync("dock");
        f.Bus.RaiseBye("bar"); f.Launcher.Last("bar").Exit(0); await Task.Delay(30);
        Assert.Equal(["bar"], f.Sup.RestartStopped()); await Task.Delay(30);
        Assert.Equal(2, f.Launcher.Count("bar")); Assert.Equal(1, f.Launcher.Count("dock")); Assert.Empty(f.Sup.RestartStopped());
    }

    [Fact]
    public async Task Nothing_is_reopened_once_the_host_is_shutting_down()
    {
        var f = new Fixture().With(("bar", true)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        f.Bus.RaiseBye("bar"); f.Launcher.Last("bar").Exit(0); await Task.Delay(30);
        await f.Sup.StopAllAsync(); Assert.False(f.Sup.Restart("bar")); Assert.Equal(1, f.Launcher.Count("bar"));
    }
}
