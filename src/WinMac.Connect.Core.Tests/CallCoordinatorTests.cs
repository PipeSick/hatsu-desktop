using Xunit;

namespace WinMac.Connect.Core.Tests;

internal sealed class FakeProvider : ITelephonyProvider
{
    public ProviderStatus Status { get; set; } = new(ProviderState.Ready, "ok");
    public List<string> Calls { get; } = [];
    public bool Answer = true, Reject = true, Hangup = true, Route = true, Mute = true; public Func<Task<bool>>? AnswerImpl;
    public Task<bool> AnswerAsync(string id, CancellationToken ct) { Calls.Add("answer:" + id); return AnswerImpl?.Invoke() ?? Task.FromResult(Answer); }
    public Task<bool> RejectAsync(string id, CancellationToken ct) { Calls.Add("reject:" + id); return Task.FromResult(Reject); }
    public Task<bool> HangupAsync(string id, CancellationToken ct) { Calls.Add("hangup:" + id); return Task.FromResult(Hangup); }
    public Task<bool> RouteAudioToPcAsync(string id, CancellationToken ct) { Calls.Add("route:" + id); return Task.FromResult(Route); }
    public Task<bool> SetMuteAsync(string id, bool m, CancellationToken ct) { Calls.Add((m ? "mute:" : "unmute:") + id); return Task.FromResult(Mute); }
}

internal sealed class FakeAncs : IAncsCallActions
{
    public bool Can = true; public List<uint> Rejected { get; } = [];
    public bool CanReject(uint uid) => Can;
    public Task<bool> RejectAsync(uint uid, CancellationToken ct) { Rejected.Add(uid); return Task.FromResult(true); }
}

public class CallCoordinatorTests
{
    private DateTimeOffset _t = new(2026, 11, 5, 18, 0, 0, TimeSpan.FromHours(-3));
    private readonly CallTracker _tr; private readonly FakeProvider _p = new(); private readonly FakeAncs _a = new(); private readonly CallCoordinator _c;
    public CallCoordinatorTests() { _tr = new(() => _t); _c = new(_tr, () => _p, () => _a, TimeSpan.FromMilliseconds(200)); }

    private static AncsEvent E(AncsEventId ev, uint uid, AncsCategory cat, AncsFlags f = AncsFlags.PositiveAction | AncsFlags.NegativeAction) => new(ev, f, cat, 1, uid);
    private static ProviderCall PC(ProviderCallStatus s, string id = "c1", string? name = "Fernando Noc", bool pc = false) => new(id, s, name, null, pc);

    [Fact] public async Task Android_call_without_ancs_rings_activates_and_ends_from_the_system_alone()
    {
        Assert.True(await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "Android"));
        var s = _c.Present(); Assert.Equal(CallPhase.Ringing, s.Phase); Assert.Equal("Android", s.Device); Assert.Equal("Fernando Noc", s.Caller); Assert.True(s.CanAnswer); Assert.True(s.CanReject); Assert.False(s.CanHangup);
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking, pc: true), "Android");
        s = _c.Present(); Assert.Equal(CallPhase.Active, s.Phase); Assert.True(s.CanHangup); Assert.True(s.AudioOnPc); Assert.True(s.CanMute); Assert.False(s.CanAnswer);
        await _c.OnProviderCallAsync(null, "Android");
        s = _c.Present(); Assert.Equal(CallPhase.Ended, s.Phase); Assert.False(s.CanHangup); Assert.False(s.AudioOnPc);
    }

    [Fact] public async Task Iphone_ancs_ring_keeps_its_contact_when_the_system_call_joins_and_answering_routes_audio_to_the_pc()
    {
        _tr.OnEvent(E(AncsEventId.Added, 326, AncsCategory.IncomingCall), 1); _tr.OnCaller(326, 1, "Fernando Noc");
        var s = _c.Present(); Assert.False(s.CanAnswer);   // ANCS sola no puede contestar con audio
        Assert.True(s.CanReject);                          // pero sí rechazar (no necesita audio)
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming, name: null), "iPhone");
        s = _c.Present(); Assert.Equal("Fernando Noc", s.Caller); Assert.Equal("c1", s.ProviderCallId); Assert.True(s.CanAnswer);
        var r = await _c.ExecuteAsync(CallCommand.Answer); Assert.Equal(CommandOutcome.Done, r.Outcome);
        Assert.Equal(["answer:c1", "route:c1"], _p.Calls);
    }

    [Fact] public async Task Answer_is_never_offered_without_a_ready_system_provider_or_a_system_call()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "Android");
        _p.Status = new(ProviderState.Unavailable, "Windows negó el registro"); Assert.False(_c.Present().CanAnswer); Assert.False(_c.Present().CanReject);
        Assert.Equal(CommandOutcome.Unavailable, (await _c.ExecuteAsync(CallCommand.Answer)).Outcome); Assert.Empty(_p.Calls);
        var off = new CallCoordinator(_tr, () => null, () => _a); Assert.False(off.Present().CanAnswer); Assert.False(off.Present().CanReject);   // función apagada: ningún botón
    }

    [Fact] public async Task Ancs_reject_works_for_an_iphone_ring_when_the_system_has_no_call_and_never_answers()
    {
        _p.Status = new(ProviderState.Unavailable, "Windows negó el registro"); _tr.OnEvent(E(AncsEventId.Added, 77, AncsCategory.IncomingCall), 1);
        var s = _c.Present(); Assert.True(s.CanReject); Assert.False(s.CanAnswer);
        Assert.Equal(CommandOutcome.Done, (await _c.ExecuteAsync(CallCommand.Reject)).Outcome); Assert.Equal([77u], _a.Rejected); Assert.Empty(_p.Calls);
        _a.Can = false; Assert.False(_c.Present().CanReject);
    }

    [Fact] public async Task Failed_answer_reports_it_and_does_not_pretend()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone"); _p.Answer = false;
        var r = await _c.ExecuteAsync(CallCommand.Answer); Assert.Equal(CommandOutcome.Failed, r.Outcome); Assert.Contains("no pudo contestar", _c.Present().Notice);
        Assert.Equal(CallPhase.Ringing, _c.Present().Phase);
    }

    [Fact] public async Task If_windows_does_not_move_the_audio_the_user_is_told_and_the_call_stays_active()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone"); _p.Route = false;
        await _c.ExecuteAsync(CallCommand.Answer);
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking, pc: false), "iPhone");   // reintento 2
        var s = _c.Present(); Assert.Equal(CallPhase.Active, s.Phase); Assert.False(s.AudioOnPc); Assert.Contains("sigue en el teléfono", s.Notice); Assert.True(s.CanHangup);
        Assert.Equal(2, _p.Calls.Count(c => c == "route:c1"));
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking, pc: true), "iPhone");   // por fin en el PC → el aviso desaparece
        Assert.Null(_c.Present().Notice); Assert.True(_c.Present().AudioOnPc);
    }

    [Fact] public async Task A_call_answered_on_the_phone_is_not_rerouted_by_hatsu()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone"); await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking), "iPhone");
        Assert.DoesNotContain("route:c1", _p.Calls); Assert.Equal(CallPhase.Active, _c.Present().Phase);
    }

    [Fact] public async Task Hangup_and_mute_only_for_an_active_system_call()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone");
        Assert.Equal(CommandOutcome.Unavailable, (await _c.ExecuteAsync(CallCommand.Hangup)).Outcome);
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking, pc: true), "iPhone");
        Assert.Equal(CommandOutcome.Done, (await _c.ExecuteAsync(CallCommand.ToggleMute)).Outcome); Assert.Contains("mute:c1", _p.Calls);
        Assert.Equal(CommandOutcome.Done, (await _c.ExecuteAsync(CallCommand.Hangup)).Outcome); Assert.Contains("hangup:c1", _p.Calls);
    }

    [Fact] public async Task Double_click_and_slow_windows_cannot_repeat_or_hang_the_module()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone"); var tcs = new TaskCompletionSource<bool>(); _p.AnswerImpl = () => tcs.Task;
        var first = _c.ExecuteAsync(CallCommand.Answer); var second = await _c.ExecuteAsync(CallCommand.Answer);
        Assert.Equal(CommandOutcome.Unavailable, second.Outcome); Assert.Equal(1, _p.Calls.Count(c => c.StartsWith("answer")));
        var r = await first; Assert.Equal(CommandOutcome.Failed, r.Outcome);   // Windows no responde: tiempo límite, no cuelga
    }

    [Fact] public async Task Call_vanishing_while_ringing_closes_neutrally_and_while_active_ends()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone"); await _c.OnProviderCallAsync(null, "iPhone");
        Assert.Equal(CallPhase.None, _c.Present().Phase);   // sin afirmar nada
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking, "c2"), "iPhone"); await _c.OnProviderCallAsync(PC(ProviderCallStatus.Ended, "c2"), "iPhone");
        Assert.Equal(CallPhase.Ended, _c.Present().Phase);
    }

    [Fact] public async Task A_waiting_call_does_not_replace_the_active_one_and_no_system_call_means_no_change()
    {
        Assert.False(await _c.OnProviderCallAsync(null, "iPhone"));
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Talking, "c1", pc: true), "iPhone");
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming, "c2", "Otro"), "iPhone");
        var s = _c.Present(); Assert.Equal("c1", s.ProviderCallId); Assert.Equal(CallPhase.Active, s.Phase);
    }

    [Fact] public async Task Reset_clears_notice_and_pending_routing()
    {
        await _c.OnProviderCallAsync(PC(ProviderCallStatus.Incoming), "iPhone"); _p.Answer = false; await _c.ExecuteAsync(CallCommand.Answer);
        _c.Reset(); _tr.Reset(); Assert.Equal(CallPhase.None, _c.Present().Phase); Assert.Null(_c.Present().Notice);
    }
}

public class RegistrationPlannerTests
{
    [Fact] public void Happy_path_is_ready_only_if_is_registered_is_true_afterwards()
    {
        Assert.True(RegistrationPlanner.Evaluate(AccessResult.Allowed, false, RegisterResult.Returned, true).Ready);
        var silent = RegistrationPlanner.Evaluate(AccessResult.Allowed, false, RegisterResult.Returned, false);   // «RegisterApp puede volver sin hacer nada»
        Assert.False(silent.Ready); Assert.Contains("no tuvo efecto", silent.Reason);
    }

    [Fact] public void Unauthorized_register_names_the_restricted_capability()
    {
        var s = RegistrationPlanner.Evaluate(AccessResult.Allowed, false, RegisterResult.Unauthorized, false);
        Assert.Equal(ProviderState.Unavailable, s.State); Assert.Contains("phoneLineTransportManagement", s.Reason);
    }

    [Fact] public void Denied_access_blames_another_app_first_and_the_capability_second()
    {
        var s = RegistrationPlanner.Evaluate(AccessResult.DeniedBySystem, false, RegisterResult.NotNeeded, false);
        Assert.False(s.Ready); Assert.Contains("Enlace Móvil", s.Reason); Assert.Contains("phoneLineTransportManagement", s.Reason);
        Assert.Contains("Privacidad", RegistrationPlanner.Evaluate(AccessResult.DeniedByUser, false, RegisterResult.NotNeeded, false).Reason);
    }

    [Fact] public void An_existing_registration_is_reused_and_a_lost_one_triggers_reregistration()
    {
        Assert.True(RegistrationPlanner.Evaluate(AccessResult.Allowed, true, RegisterResult.NotNeeded, true).Ready);
        Assert.True(RegistrationPlanner.NeedsReregister(true, false)); Assert.False(RegistrationPlanner.NeedsReregister(true, true));
        Assert.False(RegistrationPlanner.NeedsReregister(true, null));   // lectura fallida: no se actúa a ciegas
        Assert.False(RegistrationPlanner.NeedsReregister(false, false));
    }

    [Fact] public void Reconnect_waits_for_a_quiet_period_and_is_rate_limited()
    {
        Assert.False(RegistrationPlanner.ShouldReconnect(true, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5)));
        Assert.False(RegistrationPlanner.ShouldReconnect(true, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10)));
        Assert.True(RegistrationPlanner.ShouldReconnect(true, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)));
        Assert.False(RegistrationPlanner.ShouldReconnect(false, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)));
    }
}

public class CallRoutingCommandTests
{
    [Fact] public async Task A_call_answered_on_the_phone_can_be_moved_to_the_pc_and_then_muted()
    {
        var t = new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero); var tr = new CallTracker(() => t); var p = new FakeProvider(); var c = new CallCoordinator(tr, () => p, () => null);
        await c.OnProviderCallAsync(new("c1", ProviderCallStatus.Talking, "Ana", null, AudioOnPc: false), "iPhone");
        var s = c.Present(); Assert.True(s.CanRoute); Assert.False(s.CanMute); Assert.True(s.CanHangup);   // sin audio en el PC no hay silenciar
        Assert.Equal(CommandOutcome.Done, (await c.ExecuteAsync(CallCommand.RouteToPc)).Outcome); Assert.Contains("route:c1", p.Calls);
        await c.OnProviderCallAsync(new("c1", ProviderCallStatus.Talking, "Ana", null, AudioOnPc: true), "iPhone");
        s = c.Present(); Assert.False(s.CanRoute); Assert.True(s.CanMute);
        Assert.Equal(CommandOutcome.Unavailable, (await c.ExecuteAsync(CallCommand.RouteToPc)).Outcome);
    }

    [Fact] public async Task A_failed_route_tells_the_user_and_offers_no_false_audio()
    {
        var t = new DateTimeOffset(2026, 11, 5, 20, 0, 0, TimeSpan.Zero); var tr = new CallTracker(() => t); var p = new FakeProvider { Route = false }; var c = new CallCoordinator(tr, () => p, () => null);
        await c.OnProviderCallAsync(new("c1", ProviderCallStatus.Talking, "Ana"), "iPhone");
        var r = await c.ExecuteAsync(CallCommand.RouteToPc); Assert.Equal(CommandOutcome.Failed, r.Outcome);
        var s = c.Present(); Assert.False(s.AudioOnPc); Assert.Contains("sigue en el teléfono", s.Notice);
    }
}
