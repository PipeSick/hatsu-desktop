using System.Text.Json;
using WinMac.Bar;
using WinMac.Dock;
using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Doble del host: guarda en memoria, puede fallar al escribir, al cambiar el inicio automático o al arrancar un módulo.</summary>
internal sealed class FakePorts : IOnboardingPorts
{
    public HostConfig Config { get; set; } = new();
    public DockConfig? Dock { get; set; } = new DockConfig();
    public bool AutostartOn { get; set; }
    public Dictionary<string, ModuleState> States { get; } = new();
    public int FailConfigWriteAfter = int.MaxValue; public bool FailDockWrite, FailAutostart; public int Writes;
    public string? Update(Func<HostConfig, HostConfig> change)
    {
        if (Writes >= FailConfigWriteAfter) { Writes++; return "disco lleno"; }
        Writes++; var n = change(Config); Config = n;
        // como el host real: lo habilitado arranca, lo deshabilitado se para
        foreach (var (id, m) in n.Modules) States[id] = !m.Enabled ? ModuleState.Disabled : (StartFails.Contains(id) ? ModuleState.Failed : MissingModules.Contains(id) ? ModuleState.NotInstalled : ModuleState.Running);
        return null;
    }
    public HashSet<string> StartFails = [], MissingModules = [];
    public DockConfig? LoadDock() => Dock;
    public bool SaveDock(DockConfig d) { if (FailDockWrite) return false; Dock = d; return true; }
    public void SetAutostart(bool on) { if (FailAutostart) throw new InvalidOperationException("directiva de grupo"); AutostartOn = on; }
    public IReadOnlyDictionary<string, ModuleState> ModuleStates => States;
    public Dictionary<string, ModuleFailure> Failures { get; } = new();
    public IReadOnlyDictionary<string, ModuleFailure> ModuleFailures => Failures;
    public Task DelayAsync(TimeSpan t) => Task.CompletedTask;
}

public class OnboardingRulesTests
{
    [Fact] public void Safe_defaults_match_the_agreed_ones()
    {
        var c = OnboardingChoices.Safe;
        Assert.True(c.Dock && c.Bar && c.Notch); Assert.False(c.Connect || c.Peripherals || c.Cleanup || c.Autostart || c.HideTaskbar);
    }

    [Fact] public void Turning_on_Peripherals_or_Cleanup_turns_the_Bar_on_and_says_so()
    {
        var c = OnboardingChoices.Safe with { Bar = false };
        var (a, n1) = OnboardingRules.Toggle(c, OnboardingRules.ComponentPeripherals, true); Assert.True(a.Bar && a.Peripherals); Assert.Contains("Barra", n1);
        var (b, n2) = OnboardingRules.Toggle(c, OnboardingRules.ComponentCleanup, true); Assert.True(b.Bar && b.Cleanup); Assert.Contains("Barra", n2);
    }

    [Fact] public void Turning_off_the_Bar_turns_off_what_lives_in_it()
    {
        var c = OnboardingChoices.Safe with { Peripherals = true, Cleanup = true };
        var (a, n) = OnboardingRules.Toggle(c, OnboardingRules.ComponentBar, false);
        Assert.False(a.Bar || a.Peripherals || a.Cleanup); Assert.Contains("se desactivaron", n);
    }

    [Fact] public void Hiding_the_Windows_taskbar_needs_the_Dock()
    {
        var c = OnboardingChoices.Safe with { HideTaskbar = true };
        var (a, n) = OnboardingRules.Toggle(c, OnboardingRules.ComponentDock, false); Assert.False(a.HideTaskbar); Assert.Contains("barra de tareas", n);
        var (v, notes) = OnboardingRules.Validate(c with { Dock = false }); Assert.False(v.HideTaskbar); Assert.Single(notes);
    }

    [Fact] public void Validate_fixes_incoherent_choices_and_keeps_coherent_ones_untouched()
    {
        var ok = OnboardingChoices.Safe with { Peripherals = true, Autostart = true, DockSize = 2 }; var (v, notes) = OnboardingRules.Validate(ok); Assert.Equal(ok, v); Assert.Empty(notes);
        var (bad, n) = OnboardingRules.Validate(ok with { Bar = false, DockSize = 99 }); Assert.False(bad.Peripherals); Assert.Equal(1, bad.DockSize); Assert.Equal(2, n.Count);
    }

    [Fact] public void Every_component_has_a_name_and_description_and_only_real_requirements()
    {
        foreach (var id in OnboardingRules.Components) { Assert.NotEmpty(OnboardingRules.Name(id)); Assert.NotEmpty(OnboardingRules.Description(id)); }
        Assert.DoesNotContain(OnboardingRules.Components.Select(OnboardingRules.Description), d => d.Contains("2,4") || d.Contains("ASUS") || d.Contains("Bluetooth 2"));   // no se promete lo que no existe
    }

    [Fact] public void From_current_reads_the_real_settings()
    {
        var cfg = new HostConfig { HideTaskbar = true, Bar = new BarConfig { ShowCleanup = true, Background = BarBackground.Transparent } };
        cfg = cfg with { Modules = new Dictionary<string, ModuleConfig>(cfg.Modules) { ["connect"] = new ModuleConfig { Enabled = true }, ["notch"] = new ModuleConfig { Enabled = false } } };
        var c = OnboardingRules.FromCurrent(cfg, new DockConfig { IconSize = 64, Alignment = DockAlignment.Left, Theme = ThemeMode.Dark }, true);
        Assert.True(c.Connect && c.Cleanup && c.Autostart && c.HideTaskbar); Assert.False(c.Notch || c.Peripherals);
        Assert.Equal(2, c.DockSize); Assert.Equal(DockAlignment.Left, c.DockAlignment); Assert.Equal(ThemeMode.Dark, c.Theme); Assert.Equal(BarBackground.Transparent, c.BarBackground);
    }

    [Fact] public void Review_lists_every_decision_and_what_changes_in_Windows()
    {
        var r = OnboardingRules.Review(OnboardingChoices.Safe with { Autostart = true, HideTaskbar = true });
        Assert.Contains(r, x => x.Label == "Iniciar con Windows" && x.Value.StartsWith("Sí")); Assert.Contains(r, x => x.Label == "Barra de tareas de Windows" && x.Value.Contains("oculta"));
        var safe = OnboardingRules.Review(OnboardingChoices.Safe); Assert.Contains(safe, x => x.Label == "Iniciar con Windows" && x.Value == "No"); Assert.Contains(safe, x => x.Value.Contains("se queda como está", StringComparison.OrdinalIgnoreCase));
    }
}

public class OnboardingDecisionTests
{
    [Fact] public void A_fresh_install_shows_the_wizard_and_gates_every_module()
    {
        var (a, rec) = OnboardingDecision.Decide(new HostConfig(), configFileExisted: false, legacyDataExists: false);
        Assert.Equal(OnboardingAction.ShowWizard, a); Assert.Equal(OnboardingStatus.NotStarted, rec.Status); Assert.Equal("fresh", rec.Origin);
        var g = OnboardingDecision.GateFresh(new HostConfig(), rec); Assert.All(g.Modules.Values, m => Assert.False(m.Enabled)); Assert.Equal(OnboardingStatus.NotStarted, g.Onboarding!.Status);
    }

    [Fact] public void A_WinMac_or_previous_Hatsu_config_is_kept_and_never_forced_through_the_wizard()
    {
        var (a, rec) = OnboardingDecision.Decide(new HostConfig(), configFileExisted: true, legacyDataExists: false);
        Assert.Equal(OnboardingAction.ShowMigrationWelcome, a); Assert.Equal(OnboardingStatus.Existing, rec.Status); Assert.Equal("winmac-upgrade", rec.Origin);
        var (b, rec2) = OnboardingDecision.Decide(new HostConfig(), false, legacyDataExists: true); Assert.Equal(OnboardingAction.ShowMigrationWelcome, b); Assert.Equal("preexisting", rec2.Origin);
    }

    [Theory]
    [InlineData(OnboardingStatus.Completed, OnboardingAction.None)] [InlineData(OnboardingStatus.Skipped, OnboardingAction.None)] [InlineData(OnboardingStatus.Existing, OnboardingAction.ShowMigrationWelcome)]
    [InlineData(OnboardingStatus.InProgress, OnboardingAction.ResumeWizard)] [InlineData(OnboardingStatus.NotStarted, OnboardingAction.ShowWizard)]
    public void The_stored_status_decides_and_a_finished_wizard_never_comes_back(OnboardingStatus s, OnboardingAction expected) =>
        Assert.Equal(expected, OnboardingDecision.Decide(new HostConfig { Onboarding = new OnboardingRecord { Status = s } }, true, true).Action);

    [Fact] public void The_record_survives_a_save_and_load_and_old_readers_ignore_it()
    {
        string f = Path.Combine(Path.GetTempPath(), "hatsu-onb-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var cfg = new HostConfig { Onboarding = new OnboardingRecord { Status = OnboardingStatus.InProgress, Origin = "fresh", Pending = OnboardingChoices.Safe with { Cleanup = true, Theme = ThemeMode.Dark } } };
            HostConfig.Save(f, cfg); var back = HostConfig.Load(f);
            Assert.Equal(OnboardingStatus.InProgress, back.Onboarding!.Status); Assert.True(back.Onboarding.Pending!.Cleanup); Assert.Equal(ThemeMode.Dark, back.Onboarding.Pending.Theme);
            // un lector antiguo (sin la propiedad) no falla con el JSON nuevo: ignora la sección
            var legacy = JsonSerializer.Deserialize<LegacyShape>(File.ReadAllText(f), HostConfig.Json); Assert.NotNull(legacy);
        }
        finally { File.Delete(f); }
    }
    private sealed record LegacyShape { public bool Autostart { get; init; } public Dictionary<string, ModuleConfig> Modules { get; init; } = new(); }

    [Fact] public void A_config_written_by_an_older_version_has_no_record_and_loads_as_before()
    {
        string f = Path.Combine(Path.GetTempPath(), "hatsu-onb-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(f, "{\"version\":3,\"autostart\":true,\"modules\":{\"dock\":{\"enabled\":false}}}"); var c = HostConfig.Load(f); Assert.Null(c.Onboarding); Assert.True(c.Autostart); Assert.False(c.Modules["dock"].Enabled); Assert.True(c.Modules["bar"].Enabled); }
        finally { File.Delete(f); }
    }
}

public class OnboardingApplierTests
{
    private static (FakePorts P, OnboardingApplier A) Fresh()
    {
        var p = new FakePorts(); p.Config = OnboardingDecision.GateFresh(new HostConfig(), new OnboardingRecord { Status = OnboardingStatus.NotStarted, Origin = "fresh" });
        foreach (var id in new[] { "bar", "dock", "notch", "connect" }) p.States[id] = ModuleState.Disabled;
        return (p, new OnboardingApplier(p) { ModuleTimeout = TimeSpan.Zero });
    }

    [Fact] public async Task Applying_the_defaults_starts_the_default_modules_and_completes_the_wizard()
    {
        var (p, a) = Fresh(); var r = await a.ApplyAsync(OnboardingChoices.Safe);
        Assert.True(r.AllOk, string.Join("; ", r.Items.Where(i => i.Outcome != ApplyOutcome.Ok).Select(i => i.Label + ":" + i.Detail)));
        Assert.Equal(OnboardingStatus.Completed, r.Status); Assert.Equal(OnboardingStatus.Completed, p.Config.Onboarding!.Status); Assert.Null(p.Config.Onboarding.Pending);
        Assert.True(p.Config.Modules["dock"].Enabled && p.Config.Modules["bar"].Enabled && p.Config.Modules["notch"].Enabled); Assert.False(p.Config.Modules["connect"].Enabled);
        Assert.Equal(ModuleState.Running, p.States["dock"]); Assert.Equal(ModuleState.Disabled, p.States["connect"]);   // lo desactivado NO se lanza
        Assert.False(p.AutostartOn); Assert.False(p.Config.HideTaskbar);
    }

    [Fact] public async Task Choices_really_change_the_modules_the_bar_features_the_dock_and_windows_integration()
    {
        var (p, a) = Fresh();
        var c = OnboardingChoices.Safe with { Dock = true, Notch = false, Connect = true, Peripherals = true, Cleanup = true, Theme = ThemeMode.Dark, DockSize = 2, DockAlignment = DockAlignment.Left, BarBackground = BarBackground.Transparent, Autostart = true, HideTaskbar = true };
        var r = await a.ApplyAsync(c); Assert.True(r.AllOk);
        Assert.False(p.Config.Modules["notch"].Enabled); Assert.True(p.Config.Modules["connect"].Enabled); Assert.Equal(ModuleState.Disabled, p.States["notch"]); Assert.Equal(ModuleState.Running, p.States["connect"]);
        Assert.True(p.Config.Bar!.ShowPeripherals && p.Config.Bar.ShowCleanup); Assert.Equal(BarBackground.Transparent, p.Config.Bar.Background);
        Assert.Equal(ThemeMode.Dark, p.Dock!.Theme); Assert.Equal(64, p.Dock.IconSize); Assert.Equal(DockAlignment.Left, p.Dock.Alignment);
        Assert.True(p.AutostartOn); Assert.True(p.Config.HideTaskbar); Assert.True(p.Config.Autostart);
    }

    [Fact] public async Task Nothing_is_applied_to_Windows_before_applying_and_pending_choices_are_saved_first()
    {
        var (p, a) = Fresh(); Assert.False(p.AutostartOn); Assert.False(p.Config.HideTaskbar);
        var c = OnboardingChoices.Safe with { Autostart = true, HideTaskbar = true }; p.FailConfigWriteAfter = 1;   // la 1.ª escritura (pendientes) pasa; la 2.ª falla
        var r = await a.ApplyAsync(c);
        Assert.True(r.EssentialFailed); Assert.True(r.RolledBack);
        Assert.False(p.AutostartOn); Assert.False(p.Config.HideTaskbar);                    // inicio automático revertido, barra de tareas intacta
        Assert.NotEqual(OnboardingStatus.Completed, p.Config.Onboarding!.Status);            // nunca «completado» tras un fallo
    }

    [Fact] public async Task A_failed_autostart_rolls_everything_back_and_does_not_complete()
    {
        var (p, a) = Fresh(); p.FailAutostart = true; var before = p.Dock;
        var r = await a.ApplyAsync(OnboardingChoices.Safe with { Autostart = true, Theme = ThemeMode.Dark });
        Assert.True(r.EssentialFailed && r.RolledBack); Assert.Contains(r.Items, i => i.Id == "autostart" && i.Outcome == ApplyOutcome.Failed && i.Detail!.Contains("directiva"));
        Assert.Equal(before, p.Dock); Assert.All(p.Config.Modules.Values, m => Assert.False(m.Enabled)); Assert.Equal(OnboardingStatus.NotStarted, p.Config.Onboarding!.Status);
        // reintento tras resolver el problema: ahora sí
        p.FailAutostart = false; var r2 = await a.ApplyAsync(OnboardingChoices.Safe with { Autostart = true }); Assert.True(r2.AllOk); Assert.True(p.AutostartOn); Assert.Equal(OnboardingStatus.Completed, p.Config.Onboarding!.Status);
    }

    [Fact] public async Task A_module_that_does_not_start_is_reported_never_declared_a_success_and_the_wizard_stays_open()
    {
        var (p, a) = Fresh(); p.StartFails.Add("dock");
        var r = await a.ApplyAsync(OnboardingChoices.Safe);
        Assert.False(r.AllOk); Assert.True(r.HasWarnings); Assert.False(r.EssentialFailed);
        Assert.Contains(r.Items, i => i.Id == "mod:dock" && i.Outcome == ApplyOutcome.Warning); Assert.Equal(OnboardingStatus.InProgress, p.Config.Onboarding!.Status);
        // reintento cuando ya arranca
        p.StartFails.Clear(); p.Config = p.Config with { Modules = p.Config.Modules.ToDictionary(k => k.Key, k => k.Value with { Enabled = false }) };
        var r2 = await a.ApplyAsync(OnboardingChoices.Safe); Assert.True(r2.AllOk); Assert.Equal(OnboardingStatus.Completed, p.Config.Onboarding!.Status);
    }

    [Fact] public async Task Accepting_the_warnings_completes_and_records_them()
    {
        var (p, a) = Fresh(); p.MissingModules.Add("notch"); var r = await a.ApplyAsync(OnboardingChoices.Safe);
        Assert.Contains(r.Items, i => i.Id == "mod:notch" && i.Detail!.Contains("No está incluido")); Assert.Equal(OnboardingStatus.InProgress, r.Status);
        Assert.Null(a.AcceptWarnings(r)); Assert.Equal(OnboardingStatus.Completed, p.Config.Onboarding!.Status); Assert.Contains(p.Config.Onboarding.Warnings!, w => w.Contains("Hatsu Notch"));
    }

    [Fact] public async Task Skipping_applies_only_safe_defaults_and_is_recorded_as_skipped()
    {
        var (p, a) = Fresh(); var r = await a.ApplyAsync(OnboardingChoices.Safe, skipped: true); Assert.True(r.AllOk);
        Assert.False(p.Config.HideTaskbar || p.AutostartOn || p.Config.Bar!.ShowCleanup || p.Config.Bar.ShowPeripherals);
        Assert.Equal(OnboardingStatus.Skipped, p.Config.Onboarding!.Status); Assert.Null(p.Config.Onboarding.Pending);   // omitir = completar con los valores seguros, y no reaparece
        Assert.Equal(OnboardingAction.None, OnboardingDecision.Decide(p.Config, true, true).Action);
    }

    [Fact] public async Task A_failed_dock_json_write_is_a_warning_not_a_silent_success()
    {
        var (p, a) = Fresh(); p.FailDockWrite = true; var r = await a.ApplyAsync(OnboardingChoices.Safe with { Theme = ThemeMode.Light });
        Assert.Contains(r.Items, i => i.Id == "dock" && i.Outcome == ApplyOutcome.Warning); Assert.False(r.AllOk); Assert.Equal(ThemeMode.System, p.Dock!.Theme);
    }

    [Fact] public async Task A_crash_after_saving_pending_choices_resumes_with_them()
    {
        var (p, a) = Fresh(); p.FailConfigWriteAfter = 1; var c = OnboardingChoices.Safe with { Cleanup = true, Theme = ThemeMode.Dark };
        await a.ApplyAsync(c);
        var (act, rec) = OnboardingDecision.Decide(p.Config, true, true);
        Assert.NotEqual(OnboardingAction.None, act);   // el asistente vuelve; no se perdió y no se dio por completado
    }

    [Fact] public async Task Re_running_keeps_previous_settings_that_the_wizard_does_not_own()
    {
        var p = new FakePorts { Config = new HostConfig { Onboarding = new OnboardingRecord { Status = OnboardingStatus.Completed, Origin = "fresh" }, Bar = new BarConfig { ShowSeconds = true, WeatherCity = "Molina|-35.1|-71.2" } } };
        foreach (var id in new[] { "bar", "dock", "notch", "connect" }) p.States[id] = ModuleState.Running;
        var a = new OnboardingApplier(p) { ModuleTimeout = TimeSpan.Zero }; var r = await a.ApplyAsync(OnboardingChoices.Safe with { Connect = true });
        Assert.True(r.AllOk); Assert.True(p.Config.Bar!.ShowSeconds); Assert.Equal("Molina|-35.1|-71.2", p.Config.Bar.WeatherCity); Assert.Equal(OnboardingStatus.Completed, p.Config.Onboarding!.Status);
    }
}
