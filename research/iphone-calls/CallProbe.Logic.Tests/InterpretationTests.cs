using CallProbe.Logic;
using Xunit;

namespace CallProbe.Logic.Tests;

public class AccessInterpreterTests
{
    [Fact]
    public void Denied_by_system_without_package_identity_is_inconclusive_not_blocked()
    {
        var r = AccessInterpreter.Interpret(packaged: false, "DeniedBySystem", null);
        Assert.Equal(Verdict.Inconclusive, r.Verdict); Assert.Contains("NO demuestra", r.Explanation); Assert.Contains("paquete", r.NextStep);
    }

    [Fact]
    public void Denied_by_system_WITH_package_identity_is_blocked_under_those_conditions_and_asks_to_rule_out_local_causes_first()
    {
        var r = AccessInterpreter.Interpret(packaged: true, "DeniedBySystem", null);
        Assert.Equal(Verdict.BlockedUnderTheseConditions, r.Verdict); Assert.Contains("Phone Link", r.NextStep); Assert.Contains("Privacidad", r.NextStep);
    }

    [Fact]
    public void Denied_with_a_developer_signed_package_proves_only_denied_for_sideloading_not_that_a_store_app_is_rejected()
    {
        var r = AccessInterpreter.Interpret(packaged: true, "DeniedBySystem", null, signatureKind: "Developer");
        Assert.Equal(Verdict.BlockedUnderTheseConditions, r.Verdict); Assert.Contains("carga lateral", r.Explanation); Assert.Contains("NO que una app firmada por la Store", r.Explanation); Assert.Contains("Store", r.NextStep);
        Assert.DoesNotContain("DESARROLLO", AccessInterpreter.Interpret(true, "DeniedBySystem", null, "Store").Explanation);   // con firma de Store el aviso no aplica
    }

    [Fact]
    public void Allowed_but_register_has_no_effect_matches_the_26300_report()
    {
        var r = AccessInterpreter.Interpret(packaged: true, "Allowed", registeredAfterRegister: false);
        Assert.Equal(Verdict.BlockedUnderTheseConditions, r.Verdict); Assert.Contains("6041632", r.Explanation);
    }

    [Fact]
    public void Allowed_and_registered_only_proves_registration_never_answering_or_audio()
    {
        var r = AccessInterpreter.Interpret(packaged: true, "Allowed", registeredAfterRegister: true);
        Assert.Equal(Verdict.RegistrationWorks, r.Verdict); Assert.Contains("NO demuestra contestar ni audio", r.Explanation);
    }

    [Theory]
    [InlineData(true, "Allowed")]
    [InlineData(false, "Allowed")]
    public void Allowed_without_trying_register_is_only_promising(bool packaged, string status) => Assert.Equal(Verdict.Promising, AccessInterpreter.Interpret(packaged, status, null).Verdict);

    [Theory]
    [InlineData("DeniedByUser")]
    [InlineData("Unspecified")]
    [InlineData("Otro")]
    public void User_denial_and_unknown_statuses_are_inconclusive(string status) => Assert.Equal(Verdict.Inconclusive, AccessInterpreter.Interpret(true, status, null).Verdict);
}

public class ScorecardTests
{
    [Fact]
    public void Full_integration_requires_every_box_each_one_observed()
    {
        var all = new CallScorecard(true, true, true, true, true, true); Assert.Empty(all.Missing()); Assert.StartsWith("VERIFICADO EN HARDWARE", all.Summary());
        var oneMissing = new CallScorecard(true, true, true, true, true, false).Summary();
        Assert.DoesNotContain("VERIFICADO EN HARDWARE", oneMissing); Assert.Contains("recuperación ante errores", oneMissing);   // un solo faltante rompe el «verificado»
    }

    [Fact]
    public void Answering_without_both_audio_directions_is_partial_and_names_what_is_missing()
    {
        var s = new CallScorecard(true, true, false, false, true, false).Summary();
        Assert.StartsWith("PARCIAL: control sin audio bidireccional", s); Assert.Contains("audio entrante", s); Assert.Contains("audio saliente", s);
    }

    [Fact] public void One_direction_of_audio_is_reported_as_one_way() => Assert.StartsWith("PARCIAL: audio en un solo sentido", new CallScorecard(true, true, true, false, true, true).Summary());
    [Fact] public void Detection_alone_is_unverified() => Assert.StartsWith("SIN VERIFICAR", new CallScorecard(true, false, false, false, false, false).Summary());
}

public class ConfirmationGateTests
{
    [Theory]
    [InlineData("CONTESTAR", true)]
    [InlineData("  CONTESTAR  ", true)]
    [InlineData("contestar", false)]
    [InlineData("s", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_exact_word_confirms(string? typed, bool ok) => Assert.Equal(ok, ConfirmationGate.IsConfirmed(typed, ConfirmationGate.Answer));
}

public class StepGuardTests
{
    [Fact]
    public async Task A_step_that_never_returns_times_out_and_does_not_block_the_caller()
    {
        var log = new List<string>(); var started = DateTime.UtcNow;
        var r = await StepGuard.RunAsync("PhoneLineWatcher.Stop", () => { Thread.Sleep(Timeout.Infinite); }, TimeSpan.FromMilliseconds(150), log.Add);   // como una llamada síncrona de WinRT colgada
        Assert.Equal(StepStatus.TimedOut, r.Status); Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
        Assert.Equal("→ PhoneLineWatcher.Stop", log[0]); Assert.Contains("SIN RESPUESTA", log[1]);   // la línea de inicio identifica el paso aunque luego todo se cuelgue
    }

    [Fact]
    public async Task Success_and_failure_are_reported_with_their_value_or_error()
    {
        var ok = await StepGuard.RunAsync("x", () => Task.FromResult(42), TimeSpan.FromSeconds(2)); Assert.True(ok.Ok); Assert.Equal(42, ok.Value);
        var bad = await StepGuard.RunAsync<int>("y", () => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(2)); Assert.Equal(StepStatus.Failed, bad.Status); Assert.IsType<InvalidOperationException>(bad.Error); Assert.Contains("ERROR", bad.Describe());
    }
}

public class Stage3Tests
{
    private static Stage3Snapshot Good() => new(true, "None", false, false, false, false, true, "Allowed", "a", "b");

    [Fact] public void A_clean_system_may_proceed_and_warns_that_a_developer_package_only_proves_sideload() { var c = Stage3Preconditions.Evaluate(Good(), new()); Assert.True(c.CanProceed); Assert.Contains(c.Warnings, w => w.Contains("Store")); }

    [Theory]
    [InlineData("pkg")] [InlineData("denied")] [InlineData("call")] [InlineData("incoming")] [InlineData("phonelink")] [InlineData("registered")] [InlineData("unreadable")] [InlineData("disconnected")]
    public void Every_risky_precondition_blocks_the_test(string what)
    {
        var s = what switch
        {
            "pkg" => Good() with { Packaged = false }, "denied" => Good() with { AccessStatus = "DeniedBySystem" }, "call" => Good() with { CallActive = true }, "incoming" => Good() with { CallIncoming = true },
            "phonelink" => Good() with { PhoneLinkRunning = true }, "registered" => Good() with { IsRegistered = true }, "unreadable" => Good() with { IsRegistered = null }, _ => Good() with { ClassicConnected = false },
        };
        Assert.False(Stage3Preconditions.Evaluate(s, new()).CanProceed);
    }

    [Fact]
    public void Explicit_overrides_unblock_only_what_they_name()
    {
        Assert.True(Stage3Preconditions.Evaluate(Good() with { PhoneLinkRunning = true }, new(AllowPhoneLink: true)).CanProceed);
        Assert.True(Stage3Preconditions.Evaluate(Good() with { ClassicConnected = false }, new(AllowDisconnected: true)).CanProceed);
        Assert.False(Stage3Preconditions.Evaluate(Good() with { CallActive = true, PhoneLinkRunning = true }, new(AllowPhoneLink: true, AllowDisconnected: true)).CanProceed);   // una llamada real nunca se pasa por alto
        Assert.False(Stage3Preconditions.Evaluate(Good(), new(HoldSeconds: 600)).CanProceed);
    }

    [Fact] public void Register_that_throws_access_denied_is_reported_as_rejected_with_its_hresult() { var o = Stage3Interpreter.Interpret(new("UnauthorizedAccessException", unchecked((int)0x80070005), false, null, null, false, null, false)); Assert.Equal("REGISTRO RECHAZADO", o.Verdict); Assert.Contains("0x80070005", o.Meaning); Assert.False(o.MayAdvanceToCallObservation); }
    [Fact] public void Register_without_error_but_not_registered_is_the_26300_pattern_and_never_advances() { var o = Stage3Interpreter.Interpret(new(null, null, false, null, null, false, null, false)); Assert.Equal("REGISTRO SIN EFECTO", o.Verdict); Assert.Contains("6041632", o.Meaning); Assert.Contains("Phone Link", o.Next); Assert.False(o.MayAdvanceToCallObservation); }
    [Fact] public void Delayed_registration_says_scripts_must_wait() { var o = Stage3Interpreter.Interpret(new(null, null, true, TimeSpan.FromSeconds(3), null, true, true, false)); Assert.Equal("REGISTRADA (con retraso)", o.Verdict); Assert.Contains("asíncrono", o.Meaning); }
    [Fact] public void Registered_with_a_line_proves_registration_only_and_may_advance_to_observation() { var o = Stage3Interpreter.Interpret(new(null, null, true, TimeSpan.Zero, true, true, true, false)); Assert.Equal("REGISTRADA Y CON LÍNEA", o.Verdict); Assert.Contains("NO demuestra contestar ni audio", o.Meaning); Assert.True(o.MayAdvanceToCallObservation); }
    [Fact] public void Connect_false_after_registration_is_inconclusive_like_klangbruecke() { var o = Stage3Interpreter.Interpret(new(null, null, true, TimeSpan.Zero, false, true, true, false)); Assert.Equal("REGISTRADA, SIN CONEXIÓN HFP", o.Verdict); Assert.Contains("no es concluyente", o.Meaning); }
    [Fact] public void A_timeout_never_claims_anything() { var o = Stage3Interpreter.Interpret(new(null, null, true, TimeSpan.Zero, true, true, true, TimedOut: true)); Assert.Equal("SIN RESPUESTA", o.Verdict); Assert.False(o.MayAdvanceToCallObservation); }

    [Fact]
    public void Advancing_to_real_calls_needs_all_four_conditions_each_named_when_missing()
    {
        var ok = new Stage3Outcome("REGISTRADA Y CON LÍNEA", "", "", true);
        Assert.Empty(Stage3Interpreter.MissingToAdvance(ok, true, true, true));
        Assert.Equal(4, Stage3Interpreter.MissingToAdvance(ok with { MayAdvanceToCallObservation = false }, false, false, false).Count);
        Assert.Single(Stage3Interpreter.MissingToAdvance(ok, true, true, false)); Assert.Contains("autorización", Stage3Interpreter.MissingToAdvance(ok, true, true, false)[0]);
    }

    [Theory]
    [InlineData(false, false, null, "none")] [InlineData(true, true, true, "clean")] [InlineData(false, false, true, "clean")]
    public void Journal_clean_cases(bool reverted, bool attempted, bool? nowRegistered, string expected) => Assert.Equal(expected, JournalDecision.Evaluate(expected == "none" ? null : new("k", DateTime.UtcNow, attempted, reverted), nowRegistered));

    [Fact]
    public void A_crash_between_register_and_revert_is_detected_on_the_next_start()
    {
        var crashed = new JournalState("k", DateTime.UtcNow, RegisterAttempted: true, RevertedOk: false);
        Assert.Equal("pending-revert", JournalDecision.Evaluate(crashed, isRegisteredNow: true)); Assert.Equal("stale", JournalDecision.Evaluate(crashed, false)); Assert.Equal("stale", JournalDecision.Evaluate(crashed, null));
        Assert.NotEqual(JournalDecision.KeyOf("a"), JournalDecision.KeyOf("b")); Assert.Equal(12, JournalDecision.KeyOf("transporte-secreto").Length); Assert.DoesNotContain("secreto", JournalDecision.KeyOf("transporte-secreto"), StringComparison.OrdinalIgnoreCase);   // el diario guarda un hash, nunca el identificador
    }
}

public class SnapshotComparerTests
{
    private static Stage3Snapshot S() => new(true, "None", false, false, false, false, true, "Allowed", "r", "c");

    [Fact] public void Identical_states_have_no_differences() => Assert.Empty(SnapshotComparer.Differences(S(), S() with { AccessStatus = "(no consultado)" }));   // el estado de acceso no cuenta: no es un cambio del sistema

    [Fact]
    public void Every_side_effect_is_named()
    {
        var d = SnapshotComparer.Differences(S(), S() with { IsRegistered = true, ClassicConnected = false, CommRenderHash = "x", CommCaptureHash = "y", CallActive = true });
        Assert.Equal(5, d.Count); Assert.Contains(d, x => x.StartsWith("IsRegistered()")); Assert.Contains(d, x => x.Contains("conexión Bluetooth")); Assert.Contains(d, x => x.Contains("salida de audio")); Assert.Contains(d, x => x.Contains("micrófono"));
    }

public class TimelineTests
{
    private static TimelineSample S(double t, bool? reg, string? audio, bool? conn = true, string label = "x") => new(t, label, reg, audio, conn, 0, false, false);

    [Fact] public void Empty_audio_is_NoData() => Assert.Equal(AudioFinding.NoData, TimelineAnalyzer.Summarize([S(0, false, null)], null).Audio);

    [Fact] public void Constant_value_is_reported_as_constant_and_warns_about_unregistered()
    {
        var r = TimelineAnalyzer.Summarize([S(0, false, "Cannot"), S(1, false, "Cannot"), S(2, false, "Cannot")], 0);
        Assert.Equal(AudioFinding.Constant, r.Audio); Assert.Contains(r.Limits, l => l.Contains("NO registrado"));
    }

    [Fact] public void Change_with_connection_is_attributed_to_the_link()
    {
        var r = TimelineAnalyzer.Summarize([S(0, false, "Can", conn: false), S(1, false, "Cannot", conn: true)], null);
        Assert.Equal(AudioFinding.ChangedWithConnection, r.Audio);
    }

    [Fact] public void Change_after_register_without_registration_is_only_temporal_correlation()
    {
        var r = TimelineAnalyzer.Summarize([S(0, false, "Can"), S(0.2, false, "Can"), S(0.5, false, "Cannot")], 0.1);
        Assert.Equal(AudioFinding.ChangedAfterRegister, r.Audio); Assert.Contains("sin causa demostrada", r.AudioReading);
    }

    [Fact] public void Change_without_anything_else_is_unexplained()
        => Assert.Equal(AudioFinding.ChangedUnexplained, TimelineAnalyzer.Summarize([S(0, false, "Can"), S(5, false, "Cannot")], null).Audio);

    [Fact] public void Changes_list_includes_registration_flip()
    {
        var r = TimelineAnalyzer.Summarize([S(0, false, "Can"), S(1, true, "Can")], 0);
        Assert.Contains(r.Changes, c => c.Contains("IsRegistered") && c.Contains("False → True"));
    }

    [Fact] public void Unreadable_values_are_not_treated_as_false()
    {
        var r = TimelineAnalyzer.Summarize([S(0, null, "Can"), S(1, false, "Can")], null);
        Assert.Contains(r.Changes, c => c.Contains("n/d → False"));
    }
}

public class CommandPolicyTests
{
    [Theory]
    [InlineData("stage3 status", 2)] [InlineData("STAGE3 observe --seconds 30", 4)] [InlineData("stage1", 1)]
    public void Allowed_stages_pass(string line, int n) => Assert.Equal(n, CommandPolicy.Parse(line).Length);

    [Theory]
    [InlineData("stage5 answer")] [InlineData("stage4 audio")] [InlineData("")] [InlineData(null)] [InlineData("cmd /c del x")]
    public void Real_call_stages_and_junk_are_rejected(string? line) => Assert.Empty(CommandPolicy.Parse(line));
}
}
