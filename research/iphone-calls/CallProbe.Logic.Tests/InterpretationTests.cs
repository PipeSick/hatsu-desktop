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
