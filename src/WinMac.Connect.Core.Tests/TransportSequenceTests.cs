using Xunit;

namespace WinMac.Connect.Core.Tests;

internal sealed class FakeOps : ITransportOps, IDisposable
{
    public AccessResult Access = AccessResult.Allowed; public bool Registered; public bool RegisterHasEffect = true; public Exception? RegisterThrows, ConnectThrows;
    public ManualResetEventSlim? HangRegister, HangConnect, HangAccess; public readonly List<string> Calls = []; public readonly ManualResetEventSlim Release = new(false);
    private void Log(string s) { lock (Calls) Calls.Add(s); }
    public AccessResult RequestAccess() { Log("access"); if (HangAccess is not null) Release.Wait(); return Access; }
    public bool IsRegistered() { Log("isreg"); return Registered; }
    public void RegisterApp() { Log("register"); if (HangRegister is not null) Release.Wait(); if (RegisterThrows is not null) throw RegisterThrows; if (RegisterHasEffect) Registered = true; }
    public bool Connect() { Log("connect"); if (HangConnect is not null) Release.Wait(); if (ConnectThrows is not null) throw ConnectThrows; return false; }   // false: el valor no se evalúa
    public bool RegisterForUserHasEffect;
    public void RegisterAppForUser() { Log("registerForUser"); if (RegisterForUserHasEffect) Registered = true; }
    public void UnregisterAppForUser() { Log("unregisterForUser"); Registered = false; }
    public void UnregisterApp() { Log("unregister"); Registered = false; }
    public int Count(string c) { lock (Calls) return Calls.Count(x => x == c); }
    public void Dispose() => Release.Set();
}

public class TransportSequenceTests
{
    private static SequenceBudget Fast(int totalMs = 2000) => new(TimeSpan.FromMilliseconds(totalMs), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300));
    private static readonly ManualResetEventSlim Hang = new(false);

    [Fact] public async Task Success_registers_verifies_connects_and_reports_every_stage()
    {
        using var ops = new FakeOps(); using var seq = new TransportSequence(ops, Fast()); var seen = new List<string>();
        var r = await seq.RegisterAndConnectAsync(progress: s => seen.Add(s.Stage));
        Assert.True(r.Ready); Assert.True(r.RegisteredByUsNow); Assert.True(r.RegisteredAfter); Assert.False(r.Wedged); Assert.True(r.ConnectReturned);   // ConnectAsync terminó (su bool, aquí false, no se evalúa) y no invalida el registro
        Assert.Equal(["access", "isreg", "register", "isreg", "connect", "isreg"], ops.Calls);
        Assert.Equal(6, r.Stages.Count); Assert.All(r.Stages, s => Assert.Equal(StageOutcome.Ok, s.Outcome)); Assert.Equal(6, seen.Count);
    }

    [Fact] public async Task A_failing_connect_does_not_cancel_a_verified_registration()
    {
        using var ops = new FakeOps { ConnectThrows = new InvalidOperationException("sin conexión") }; using var seq = new TransportSequence(ops, Fast());
        var r = await seq.RegisterAndConnectAsync();
        Assert.True(r.Ready); Assert.True(r.RegisteredAfter); Assert.False(r.ConnectReturned); Assert.Equal(StageOutcome.Threw, r.Stages.Single(s => s.Stage.StartsWith("conexión")).Outcome); Assert.Equal(StageOutcome.Ok, r.Stages.Last().Outcome);
    }

    [Fact] public async Task RegisterApp_without_effect_is_reported_and_never_ready()
    {
        using var ops = new FakeOps { RegisterHasEffect = false }; using var seq = new TransportSequence(ops, Fast());
        var r = await seq.RegisterAndConnectAsync();
        Assert.False(r.Ready); Assert.Contains("no tuvo efecto", r.Status.Reason); Assert.Contains("Enlace Móvil", r.Status.Reason); Assert.False(r.Wedged); Assert.Equal(0, ops.Count("connect"));   // sin registro verificado no se conecta (ConnectAsync sobre un transporte no registrado es lo que probablemente colgaba 2.0.3.0)
        Assert.Equal(1, ops.Count("registerForUser"));   // se probó la variante por usuario una vez, y no sirvió
    }

    [Fact] public async Task RegisterAppForUser_is_the_fallback_when_RegisterApp_does_nothing_and_it_unregisters_cleanly()
    {
        using var ops = new FakeOps { RegisterHasEffect = false, RegisterForUserHasEffect = true }; using var seq = new TransportSequence(ops, Fast());
        var r = await seq.RegisterAndConnectAsync();
        Assert.True(r.Ready); Assert.True(r.RegisteredAfter); Assert.Equal(1, ops.Count("connect")); Assert.Contains(r.Stages, s => s.Stage.StartsWith("registro alternativo") && s.Ok);
        ops.Registered = true; var c = await seq.CleanupAsync(); Assert.Equal("deshecho", c.Detail);
    }

    [Fact] public async Task Denied_access_stops_before_registering()
    {
        using var ops = new FakeOps { Access = AccessResult.DeniedBySystem }; using var seq = new TransportSequence(ops, Fast());
        var r = await seq.RegisterAndConnectAsync();
        Assert.False(r.Ready); Assert.Contains("Enlace Móvil", r.Status.Reason); Assert.Equal(0, ops.Count("register")); Assert.Equal(0, ops.Count("connect"));
    }

    [Fact] public async Task Windows_unauthorized_keeps_its_hresult_and_names_the_capability()
    {
        using var ops = new FakeOps { RegisterThrows = new UnauthorizedAccessException("denegado") }; using var seq = new TransportSequence(ops, Fast());
        var r = await seq.RegisterAndConnectAsync();
        var st = r.Stages.Single(s => s.Stage.StartsWith("registro")); Assert.Equal(StageOutcome.Threw, st.Outcome); Assert.Equal(unchecked((int)0x80070005), st.HResult);
        Assert.Contains("phoneLineTransportManagement", r.Status.Reason); Assert.False(r.Ready); Assert.False(r.Wedged);
    }

    [Fact] public async Task ConnectAsync_that_never_returns_is_attributed_to_connect_and_isolates_the_transport()
    {
        using var ops = new FakeOps { HangConnect = Hang }; using var seq = new TransportSequence(ops, Fast());
        var sw = System.Diagnostics.Stopwatch.StartNew(); var r = await seq.RegisterAndConnectAsync(); sw.Stop();
        Assert.True(r.Wedged); Assert.Equal("conexión HFP (ConnectAsync)", r.WedgedStage); Assert.False(r.Ready); Assert.Contains("conexión HFP", r.Status.Reason);
        Assert.True(sw.ElapsedMilliseconds < 1500, "no espera más de su tope"); Assert.True(seq.Worker.Poisoned);
        Assert.Equal(StageOutcome.Timeout, r.Stages.Single(s => s.Stage.StartsWith("conexión")).Outcome);
        var c = await seq.CleanupAsync(); Assert.Equal(StageOutcome.Skipped, c.Outcome); Assert.Equal(0, ops.Count("unregister"));   // nada más sobre el transporte
        ops.Release.Set();   // el bloqueo se libera tarde: no ejecuta nada nuevo
        await Task.Delay(100); Assert.Equal(1, ops.Count("connect")); Assert.Equal(0, ops.Count("unregister"));
    }

    [Fact] public async Task RegisterApp_that_never_returns_stops_the_sequence_at_register()
    {
        using var ops = new FakeOps { HangRegister = Hang }; using var seq = new TransportSequence(ops, Fast());
        var r = await seq.RegisterAndConnectAsync();
        Assert.True(r.Wedged); Assert.Equal("registro (RegisterApp)", r.WedgedStage); Assert.Equal(1, ops.Count("isreg")); Assert.Equal(0, ops.Count("connect"));   // ni verificación ni conexión tras el bloqueo
        Assert.Contains("registro", r.Status.Reason);
    }

    [Fact] public async Task Cancelling_during_connect_returns_promptly_and_leaves_no_further_work()
    {
        using var ops = new FakeOps { HangConnect = Hang }; using var seq = new TransportSequence(ops, new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)));
        using var cts = new CancellationTokenSource(); var run = seq.RegisterAndConnectAsync(cts.Token);
        await Task.Delay(150); cts.Cancel(); var r = await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(StageOutcome.Cancelled, r.Stages.Last().Outcome); Assert.True(seq.Worker.Poisoned); Assert.False(r.Ready);
        Assert.Equal(0, ops.Count("unregister"));
    }

    [Fact] public async Task Concurrent_attempts_do_not_register_twice()
    {
        using var ops = new FakeOps { HangConnect = Hang }; using var seq = new TransportSequence(ops, new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(600), TimeSpan.FromSeconds(1)));
        var first = seq.RegisterAndConnectAsync(); await Task.Delay(100);
        var second = await seq.RegisterAndConnectAsync();
        Assert.Equal(StageOutcome.Skipped, second.Stages.Single().Outcome); Assert.Contains("en curso", second.Status.Reason);
        await first; Assert.Equal(1, ops.Count("register"));
    }

    [Fact] public async Task The_total_budget_caps_the_whole_sequence_and_skips_what_does_not_fit()
    {
        using var ops = new FakeOps { HangAccess = Hang }; var b = new SequenceBudget(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
        using var seq = new TransportSequence(ops, b); var sw = System.Diagnostics.Stopwatch.StartNew(); var r = await seq.RegisterAndConnectAsync(); sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1500, "una etapa con tope de 10 s no excede lo que queda del presupuesto de 250 ms"); Assert.True(r.Wedged); Assert.Equal(0, ops.Count("register"));
    }

    [Fact] public async Task Lost_registration_is_detected_and_a_fresh_sequence_recovers_after_a_wedge()
    {
        using var ops = new FakeOps(); using var seq = new TransportSequence(ops, Fast()); await seq.RegisterAndConnectAsync();
        ops.Registered = false;   // se volvió a emparejar / otra app tomó el rol
        var still = await seq.ReadRegisteredAsync(TimeSpan.FromMilliseconds(300)); Assert.False(still ?? true); Assert.True(RegistrationPlanner.NeedsReregister(true, still));
        using var wedged = new FakeOps { HangConnect = Hang }; using var s1 = new TransportSequence(wedged, Fast()); Assert.True((await s1.RegisterAndConnectAsync()).Wedged);
        using var s2 = new TransportSequence(ops, Fast()); Assert.True((await s2.RegisterAndConnectAsync()).Ready);   // tras reiniciar el módulo: transporte y trabajador nuevos
        wedged.Release.Set();
    }

    [Fact] public async Task Cleanup_unregisters_only_when_registered_and_reads_never_hang_the_loop()
    {
        using var ops = new FakeOps(); using var seq = new TransportSequence(ops, Fast()); await seq.RegisterAndConnectAsync();
        Assert.Equal("deshecho", (await seq.CleanupAsync()).Detail); Assert.Equal("no estaba registrado", (await seq.CleanupAsync()).Detail); Assert.Equal(1, ops.Count("unregister"));
        using var h = new FakeOps { HangAccess = Hang }; using var s = new TransportSequence(h, Fast()); h.Registered = true;
        var w = s.Worker; _ = w.Run(() => { h.Release.Wait(); return 0; });   // el trabajador está ocupado por una llamada colgada
        Assert.Null(await s.ReadRegisteredAsync(TimeSpan.FromMilliseconds(100))); Assert.True(w.Poisoned);   // la lectura periódica no cuelga el bucle
        h.Release.Set();
    }
}

public class WedgeGuardTests
{
    [Fact] public void Cooldown_grows_and_clears_after_a_quiet_day()
    {
        var t0 = new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
        var s1 = WedgeGuard.Record(null, "registro (RegisterApp)", t0); Assert.Equal(1, s1.Count); Assert.True(WedgeGuard.InCooldown(s1, t0.AddMinutes(4))); Assert.False(WedgeGuard.InCooldown(s1, t0.AddMinutes(6)));
        var s2 = WedgeGuard.Record(s1, "conexión", t0.AddMinutes(10)); Assert.Equal(2, s2.Count); Assert.True(WedgeGuard.InCooldown(s2, t0.AddMinutes(10 + 14))); Assert.False(WedgeGuard.InCooldown(s2, t0.AddMinutes(10 + 16)));
        Assert.Equal(TimeSpan.FromHours(6), WedgeGuard.Cooldown(50)); Assert.Null(WedgeGuard.Decay(s2, t0.AddDays(2))); Assert.NotNull(WedgeGuard.Decay(s2, t0.AddHours(3)));
        Assert.False(WedgeGuard.InCooldown(null, t0));
    }

    [Fact] public void State_survives_a_roundtrip_and_garbage_is_ignored()
    {
        var s = WedgeGuard.Record(null, "conexión HFP", new DateTime(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc)); var back = WedgeGuard.Parse(WedgeGuard.Serialize(s));
        Assert.Equal(s, back); Assert.Null(WedgeGuard.Parse("{ not json")); Assert.Null(WedgeGuard.Parse("")); Assert.Null(WedgeGuard.Parse("{\"x\":1}"));
    }
}
