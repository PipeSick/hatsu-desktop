using WinMac.Bar;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Los dos pasos nuevos del asistente: «Funciones extra» (módulos de productividad, apagados de entrada) e «Iconos de la barra» (uno por uno).</summary>
public class OnboardingExtrasTests
{
    [Fact] public void Extras_start_off_and_icons_start_as_the_bars_factory_values()
    {
        var c = OnboardingChoices.Safe; Assert.All(OnboardingRules.ExtraIds, id => Assert.False(OnboardingRules.GetExtra(c, id)));
        var f = new BarConfig();
        Assert.Equal((f.ShowVolume, f.ShowWeather, f.ShowNetwork, f.ShowBluetooth, f.ShowBattery, f.ShowVpn, f.ShowTaskManager, f.ShowTrayIcons, f.ShowPeripherals, f.ShowCleanup),
            (c.IconVolume, c.IconWeather, c.IconWifi, c.IconBluetooth, c.IconBattery, c.IconVpn, c.IconTaskManager, c.IconTray, c.Peripherals, c.Cleanup));
        Assert.Equal(13, OnboardingRules.IconIds.Length); Assert.Equal(4, OnboardingRules.Components.Length);
    }

    [Fact] public void Bar_extras_need_the_Bar_and_Shelf_needs_the_Notch()
    {
        var c = OnboardingChoices.Safe with { Mission = true, Gestures = true, Shelf = true };
        var (nb, note) = OnboardingRules.Toggle(c, OnboardingRules.ComponentBar, false); Assert.False(nb.Mission || nb.Gestures); Assert.True(nb.Shelf); Assert.Contains("Barra superior", note);
        var (nn, note2) = OnboardingRules.Toggle(c, OnboardingRules.ComponentNotch, false); Assert.False(nn.Shelf); Assert.True(nn.Mission); Assert.Contains("Shelf+", note2);
        var v = OnboardingRules.Validate(c with { Bar = false, Notch = false }).Choices; Assert.False(v.Mission || v.Gestures || v.Shelf);
        Assert.False(OnboardingRules.ExtraAvailable(c with { Bar = false }, "snap")); Assert.False(OnboardingRules.ExtraAvailable(c with { Notch = false }, "shelf"));
    }

    [Fact] public void Clicking_a_row_toggles_it_and_a_blocked_extra_explains_why()
    {
        var ctl = new OnboardingController(OnboardingMode.Fresh, OnboardingChoices.Safe with { Notch = false });
        ctl.Click("x:snap"); Assert.True(ctl.Choices.Snap); ctl.Click("x:snap"); Assert.False(ctl.Choices.Snap);
        ctl.Click("x:shelf"); Assert.False(ctl.Choices.Shelf); Assert.Contains("Hatsu Notch", ctl.Note);
        ctl.Click("i:vpn"); Assert.True(ctl.Choices.IconVpn); ctl.Click("i:cleanup"); Assert.True(ctl.Choices.Cleanup); ctl.Click("i:performance"); Assert.False(ctl.Choices.IconPerformance);
    }

    [Fact] public void The_icons_step_is_skipped_without_the_Bar_and_the_flow_reaches_every_step()
    {
        var with = new OnboardingController(OnboardingMode.Fresh, OnboardingChoices.Safe); var seen = new List<OnboardingStep> { with.Step };
        for (int i = 0; i < 8; i++) { with.Click("next"); if (seen[^1] != with.Step) seen.Add(with.Step); }
        Assert.Equal([OnboardingStep.Welcome, OnboardingStep.Components, OnboardingStep.Extras, OnboardingStep.Icons, OnboardingStep.Appearance, OnboardingStep.Windows, OnboardingStep.Review], seen);
        var without = new OnboardingController(OnboardingMode.Fresh, OnboardingChoices.Safe with { Bar = false }); Assert.DoesNotContain(OnboardingStep.Icons, without.ActiveFlow);
        without.Click("next"); without.Click("next"); Assert.Equal(OnboardingStep.Extras, without.Step); without.Click("next"); Assert.Equal(OnboardingStep.Appearance, without.Step); without.Click("back"); Assert.Equal(OnboardingStep.Extras, without.Step);
    }

    [Fact] public void The_plan_writes_every_extra_and_every_icon_into_the_real_settings()
    {
        var c = OnboardingChoices.Safe with { Mission = true, Snap = true, LiveText = true, SpotActions = false, Gestures = true, Shelf = false, IconVolume = false, IconPerformance = false, IconNetwork = true, IconDisks = false, IconWeather = false, IconWifi = false, IconBluetooth = false, IconBattery = false, IconVpn = true, IconTaskManager = true, IconTray = false, Peripherals = true, Cleanup = true };
        var cfg = OnboardingPlan.Apply(new HostConfig(), c, new OnboardingRecord()); var b = cfg.Bar!;
        Assert.True(b.McEnabled && b.SnapEnabled && b.LiveTextEnabled && b.GesturesEnabled); Assert.False(b.SpotActionsEnabled);
        Assert.False(b.ShowVolume || b.ShowHubSystem || b.ShowHubDisks || b.ShowWeather || b.ShowNetwork || b.ShowBluetooth || b.ShowBattery || b.ShowTrayIcons); Assert.True(b.ShowHubNetwork && b.ShowHub && b.ShowVpn && b.ShowTaskManager && b.ShowPeripherals && b.ShowCleanup);
        Assert.False(cfg.Notch!.Sections.Single(s => s.Id == "shelf").Enabled);   // Shelf+ apagado = la pestaña «shelf» de la Isla apagada
        Assert.False(OnboardingPlan.Apply(new HostConfig(), c with { IconPerformance = false, IconNetwork = false, IconDisks = false }, new OnboardingRecord()).Bar!.ShowHub);
        var on = OnboardingPlan.Apply(new HostConfig(), OnboardingChoices.Safe with { Shelf = true }, new OnboardingRecord()); Assert.Null(on.Notch);   // coincide con la fábrica: no se crea nada
    }

    [Fact] public void Reopening_the_wizard_shows_what_is_on_today()
    {
        var cfg = OnboardingPlan.Apply(new HostConfig(), OnboardingChoices.Safe with { Snap = true, Shelf = false, IconVpn = true, IconTray = false }, new OnboardingRecord());
        var back = OnboardingRules.FromCurrent(cfg, null, false); Assert.True(back.Snap && back.IconVpn); Assert.False(back.Shelf || back.IconTray || back.Mission);
    }

    [Fact] public void Review_names_the_new_choices_in_one_line_each()
    {
        var rows = OnboardingRules.Review(OnboardingChoices.Safe with { Mission = true, Snap = true, Gestures = true, LiveText = true });
        Assert.Contains(rows, r => r.Label == "Funciones extra" && r.Value.StartsWith("4:")); Assert.Contains(rows, r => r.Label == "Iconos de la barra" && r.Value.StartsWith("9 de 13"));
        Assert.DoesNotContain(OnboardingRules.Review(OnboardingChoices.Safe with { Bar = false }), r => r.Label == "Iconos de la barra");
        Assert.All(OnboardingRules.Review(OnboardingChoices.Safe with { Mission = true, Snap = true, Gestures = true, LiveText = true, SpotActions = true, Shelf = true, IconVpn = true, IconTaskManager = true, Cleanup = true, Peripherals = true }), r => Assert.Equal(1, TextFit.Lines(r.Value, 420, OnboardingLayout.FontBody)));
    }
}
