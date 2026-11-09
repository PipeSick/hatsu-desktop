using System.ComponentModel;
using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Fase 3.1: causa de los fallos de módulos, onboarding para usuarios existentes e informe de diagnóstico.</summary>
public class FailureClassificationTests
{
    [Theory]
    [InlineData(4551, FailureKind.PolicyBlocked)] [InlineData(1260, FailureKind.PolicyBlocked)] [InlineData(786, FailureKind.PolicyBlocked)] [InlineData(577, FailureKind.PolicyBlocked)]
    [InlineData(225, FailureKind.AntivirusBlocked)] [InlineData(226, FailureKind.AntivirusBlocked)] [InlineData(5, FailureKind.AccessDenied)] [InlineData(740, FailureKind.AccessDenied)]
    [InlineData(2, FailureKind.Missing)] [InlineData(3, FailureKind.Missing)] [InlineData(193, FailureKind.BadImage)] [InlineData(1234, FailureKind.Unknown)]
    public void A_windows_error_code_maps_to_a_cause_without_naming_a_specific_protection(int code, FailureKind expected)
    {
        var f = LaunchDiagnostics.Classify(new Win32Exception(code), 1); Assert.Equal(expected, f.Kind); Assert.Equal(code, f.Win32Code); Assert.Contains($"[error {code}]", f.Technical);
        Assert.DoesNotContain("Smart App Control", f.Reason); Assert.DoesNotContain("Smart App Control", f.Short);   // el código de error no demuestra QUÉ protección fue
    }

    [Fact] public void A_missing_file_is_missing_and_a_policy_block_says_the_cause_is_not_established_and_that_nothing_is_bypassed()
    {
        Assert.Equal(FailureKind.Missing, LaunchDiagnostics.Classify(new FileNotFoundException("x", "/a.exe")).Kind);
        var p = LaunchDiagnostics.Classify(new Win32Exception(4551)); Assert.Contains("el código de error no dice cuál", p.Advice); Assert.Contains("no cambia ni evita", p.Advice); Assert.Equal("Windows lo bloqueó mediante una directiva de seguridad", p.Reason);
    }

    [Theory] [InlineData(unchecked((int)0xC0000428), FailureKind.PolicyBlocked)] [InlineData(unchecked((int)0xC0000005), FailureKind.ExitedUnexpectedly)] [InlineData(1, FailureKind.ExitedUnexpectedly)]
    public void An_exit_code_of_a_module_that_did_start_is_classified_too(int code, FailureKind expected) => Assert.Equal(expected, LaunchDiagnostics.ClassifyExit(code).Kind);

    [Fact] public async Task The_supervisor_records_the_cause_per_module_and_a_user_disabled_module_is_not_a_failure()
    {
        var f = new Fixture(); f.Launcher.Blocked["dock"] = new Win32Exception(4551);
        f.With(("bar", true), ("dock", true), ("notch", false)); await f.StartAllAsync(); f.Bus.RaiseReady("bar");
        await f.Clock.AdvanceAsync(TimeSpan.FromMinutes(3));   // reintentos con espera creciente hasta abrir el cortacircuito
        Assert.Equal(ModuleState.Failed, f.Sup.States["dock"]); var fail = f.Sup.Failures; Assert.Equal(FailureKind.PolicyBlocked, fail["dock"].Kind);
        Assert.False(fail.ContainsKey("notch"));   // desactivado por el usuario ≠ fallo
        lock (f.Log) { Assert.Contains(f.Log, l => l.Contains("[dock] no se pudo lanzar") && l.Contains("[error 4551]")); Assert.Equal(5, f.Log.Count(l => l.Contains("[dock] no se pudo lanzar"))); }   // sin bucle infinito: 5 intentos como máximo (política existente)
        Assert.True(f.Launcher.Count("bar") >= 1);   // el fallo del Dock no impide que la Barra arranque
    }

    [Fact] public void The_tray_row_explains_a_failed_module_briefly_and_only_when_it_failed()
    {
        var st = new Dictionary<string, ModuleState> { ["dock"] = ModuleState.Failed, ["bar"] = ModuleState.Running, ["notch"] = ModuleState.Disabled };
        var items = TrayPanelModel.Build(st, "2.0", new Dictionary<string, ModuleFailure> { ["dock"] = new(FailureKind.PolicyBlocked, "x") });
        Assert.Equal("No se pudo iniciar · Bloqueado por una directiva de Windows", items.Single(i => i.Text == "Dock").Sub); Assert.Equal("En ejecución", items.Single(i => i.Text == "Barra superior").Sub);
        Assert.Equal("Desactivado", items.Single(i => i.Text == "Hatsu Notch").Sub);
    }

    [Fact] public void Settings_lists_failed_modules_with_retry_and_never_lists_disabled_ones()
    {
        var cfg = new HostConfig(); var states = new Dictionary<string, ModuleState> { ["dock"] = ModuleState.Failed, ["notch"] = ModuleState.Disabled, ["bar"] = ModuleState.Running };
        var pages = SettingsBuilder.Build(new SettingsInput(cfg, states, false) { ModuleFailures = new Dictionary<string, ModuleFailure> { ["dock"] = new(FailureKind.PolicyBlocked, "x") } });
        var rows = pages.Single(p => p.Id == "general").Groups.SelectMany(g => g.Rows).ToList();
        var retry = rows.Single(r => r.Id == "module.retry:dock"); Assert.Equal("Reintentar Dock", retry.Title); Assert.Contains("Windows lo bloqueó", retry.Subtitle); Assert.DoesNotContain(rows, r => r.Id == "module.retry:notch");
        Assert.Contains(rows, r => r.Id == "diagnose.run"); Assert.DoesNotContain(SettingsBuilder.Build(new SettingsInput(cfg, new Dictionary<string, ModuleState> { ["bar"] = ModuleState.Running }, false)).Single(p => p.Id == "general").Groups.SelectMany(g => g.Rows), r => r.Id.StartsWith("module.retry"));
    }

    [Fact] public void The_settings_actions_retry_and_diagnose_only_call_the_host_callbacks()
    {
        var cfg = new ConfigService(Path.Combine(Path.GetTempPath(), "hatsu-p31.json"), new HostConfig()); string? retried = null; int diag = 0;
        var a = new SettingsActions(cfg, () => { }, _ => { }) { RetryModule = id => { retried = id; return true; }, RunDiagnostics = () => diag++ };
        Assert.True(a.Apply(new SettingsEdit("module.retry:dock", "button"))); Assert.Equal("dock", retried); Assert.True(a.Apply(new SettingsEdit("diagnose.run", "button"))); Assert.Equal(1, diag);
        Assert.False(new SettingsActions(cfg, () => { }, _ => { }).Apply(new SettingsEdit("module.retry:dock", "button")));
    }
}

public class MigrationWelcomeTests
{
    [Fact] public void A_user_with_a_WinMac_config_is_offered_ONE_migration_welcome_and_their_modules_are_not_gated()
    {
        var (a, rec) = OnboardingDecision.Decide(new HostConfig(), configFileExisted: true, legacyDataExists: false);
        Assert.Equal(OnboardingAction.ShowMigrationWelcome, a); Assert.Equal(OnboardingStatus.Existing, rec.Status); Assert.Equal("winmac-upgrade", rec.Origin); Assert.Null(rec.WelcomeShownUtc);
        var (b, _) = OnboardingDecision.Decide(new HostConfig(), false, legacyDataExists: true); Assert.Equal(OnboardingAction.ShowMigrationWelcome, b);
        // una vez mostrada (o descartada) no se repite, ni en cada actualización
        Assert.Equal(OnboardingAction.None, OnboardingDecision.Decide(new HostConfig { Onboarding = rec with { WelcomeShownUtc = DateTime.UtcNow } }, true, true).Action);
        // el estado que dejó la Fase 3 (Existing sin bienvenida) la recibe una vez
        Assert.Equal(OnboardingAction.ShowMigrationWelcome, OnboardingDecision.Decide(new HostConfig { Onboarding = new OnboardingRecord { Status = OnboardingStatus.Existing, Origin = "winmac-upgrade" } }, true, true).Action);
    }

    [Fact] public void A_fresh_install_still_gets_the_full_wizard_and_a_completed_one_is_left_alone()
    {
        Assert.Equal(OnboardingAction.ShowWizard, OnboardingDecision.Decide(new HostConfig(), false, false).Action);
        foreach (var s in new[] { OnboardingStatus.Completed, OnboardingStatus.Skipped }) Assert.Equal(OnboardingAction.None, OnboardingDecision.Decide(new HostConfig { Onboarding = new OnboardingRecord { Status = s } }, true, true).Action);
    }

    [Fact] public void The_migration_welcome_offers_keep_or_review_and_never_skip_or_apply()
    {
        var c = new OnboardingController(OnboardingMode.Migration, OnboardingChoices.Safe);
        Assert.Equal(["keep", "next"], c.Hits.Select(h => h.Id).OrderBy(x => x == "next" ? 1 : 0).ToArray()); Assert.DoesNotContain(c.Hits, h => h.Id is "skip" or "apply");
        Assert.Equal(OnboardingCommand.Keep, c.Click("keep")); Assert.Equal(OnboardingStep.Welcome, c.Step);   // conservar no cambia de pantalla ni aplica nada
        Assert.Equal(OnboardingCommand.None, c.Click("next")); Assert.Equal(OnboardingStep.Components, c.Step);   // revisar → el mismo flujo, y solo «Aplicar y comenzar» aplica
        Assert.Contains("la conservamos tal cual", OnboardingLayout.MigrationBody); Assert.Contains("No se cambia nada hasta que lo confirmes", OnboardingLayout.MigrationBody);
    }

    [Fact] public void A_policy_block_adds_a_neutral_sentence_to_the_result_and_the_screen_still_fits()
    {
        var r = new ApplyResult([new("mod:dock", "Dock", ApplyOutcome.Warning, "No se pudo iniciar", "Windows lo bloqueó… [error 4551]", Cause: FailureKind.PolicyBlocked), new("mod:bar", "Barra superior", ApplyOutcome.Ok, "En ejecución")], OnboardingStatus.InProgress, false);
        var c = OnboardingLayoutTests.At(OnboardingStep.Result, result: r); Assert.Contains("directiva de seguridad", c.ResultMessage); Assert.DoesNotContain("Smart App Control", c.ResultMessage); Assert.Contains("no la cambia", c.ResultMessage);
        OnboardingLayoutTests.AssertGeometry(c, "Result/directiva"); c.Click("details"); OnboardingLayoutTests.AssertGeometry(c, "Result/directiva+detalles");
        var other = new ApplyResult([new("mod:dock", "Dock", ApplyOutcome.Warning, "No se pudo iniciar", "x", Cause: FailureKind.ExitedUnexpectedly)], OnboardingStatus.InProgress, false); Assert.DoesNotContain("directiva", OnboardingLayoutTests.At(OnboardingStep.Result, result: other).ResultMessage);
    }

    [Fact] public async Task The_applier_reports_the_established_cause_of_a_module_that_did_not_start_without_inventing_one()
    {
        var p = new FakePorts { Config = OnboardingDecision.GateFresh(new HostConfig(), new OnboardingRecord { Status = OnboardingStatus.NotStarted }) }; p.StartFails.Add("dock");
        p.Failures["dock"] = LaunchDiagnostics.Classify(new Win32Exception(4551), 5);
        var res = await new OnboardingApplier(p) { ModuleTimeout = TimeSpan.Zero }.ApplyAsync(OnboardingChoices.Safe); var it = res.Items.Single(i => i.Id == "mod:dock");
        Assert.Equal("No se pudo iniciar", it.Detail); Assert.Equal(FailureKind.PolicyBlocked, it.Cause); Assert.Contains("[error 4551]", it.Technical); Assert.Contains("no cambia ni evita", it.Technical); Assert.False(res.AllOk); Assert.Equal(OnboardingStatus.InProgress, p.Config.Onboarding!.Status);
        // sin causa conocida solo se dice el estado del supervisor
        p.Failures.Clear(); p.Config = OnboardingDecision.GateFresh(new HostConfig(), new OnboardingRecord { Status = OnboardingStatus.NotStarted });
        var res2 = await new OnboardingApplier(p) { ModuleTimeout = TimeSpan.Zero }.ApplyAsync(OnboardingChoices.Safe); Assert.Null(res2.Items.Single(i => i.Id == "mod:dock").Cause); Assert.Equal("estado del supervisor: Failed", res2.Items.Single(i => i.Id == "mod:dock").Technical);
    }

    [Fact] public void The_bar_sends_its_AppBar_messages_with_the_tray_proxy_out_of_the_way()
    {
        // medido en CI: con la ventana-proxy «Shell_TrayWnd» de la Barra encima, SHAppBarMessage hablaba con ella y Windows nunca reservaba el área de trabajo
        string root = FindRoot(); var win = File.ReadAllText(Path.Combine(root, "src", "WinMac.Bar.Win", "Rendering", "BarWindow.cs")); var host = File.ReadAllText(Path.Combine(root, "src", "WinMac.Bar.Win", "Shell", "Tray", "TrayHost.cs"));
        Assert.Contains("_host.RunAppBarOp(AppBarSetPosCore)", win); Assert.Contains("_host.RunAppBarOp(AppBarRemoveCore)", win);
        foreach (var core in new[] { "AppBarSetPosCore", "AppBarRemoveCore" }) { int i = win.IndexOf("private void " + core + "()", StringComparison.Ordinal); Assert.True(i > 0); }
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(win, @"SHAppBarMessage\(").Count);   // NEW, QUERYPOS, SETPOS y REMOVE: todas dentro de los dos métodos envueltos
        Assert.Contains("HWND_NOTOPMOST", host); Assert.Contains("WithExplorerDirect", host); Assert.Contains("if (_suspended) return;", host);   // mientras dura la operación, el temporizador no vuelve a subir el proxy
    }

    [Fact] public void The_wizard_lives_in_the_host_so_a_failed_dock_or_bar_cannot_prevent_it()
    {
        var src = File.ReadAllText(Path.Combine(FindRoot(), "src", "WinMac.Host", "HostApp.cs"));
        Assert.Contains("new OnboardingWindow(", src); Assert.Contains("OpenWizard(OnboardingMode.Migration)", src); Assert.Contains("OpenWizard(OnboardingMode.Rerun)", src);
    }
    private static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "LICENSE"))) d = d.Parent; return d!.FullName; }
}

public class DiagnosticsTests
{
    private static DiagnosticsInput Input(HostConfig cfg, string[]? log = null, string[]? running = null, string autostart = "no está activado") => new("2.0.1", @"C:\Users\ana\AppData\Local\Programs\Hatsu Desktop", "Windows 11 26200", "2026-11-05 10:00", cfg, true, ["bar.json", "dock.json"],
        autostart, false, new Dictionary<string, string> { ["HatsuDesktop.exe"] = "sin firma", ["HatsuDesktop.Dock.exe"] = "sin firma" }, running ?? ["HatsuDesktop", "HatsuDesktop.Bar", "NotchWin.App"], log ?? [], null, @"C:\Users\ana", "ana");

    [Fact] public void The_log_parser_finds_each_modules_last_state_and_cause()
    {
        var m = InstallDiagnostics.ParseLog(["2026 [bar] Starting", "2026 [bar] Running", "2026 [dock] no se pudo lanzar: una directiva de Control de aplicaciones bloqueó el archivo [error 4551]", "2026 [dock] Crashed", "2026 [dock] Failed", "2026 [notch] desactivado por el usuario", "otra cosa"]);
        Assert.Equal("Running", m["bar"].State); Assert.Null(m["bar"].Failure); Assert.Equal("Failed", m["dock"].State); Assert.Contains("[error 4551]", m["dock"].Failure); Assert.Equal("Disabled", m["notch"].State);
    }

    [Fact] public void The_report_says_which_component_is_not_running_and_why_without_blaming_a_specific_protection()
    {
        var r = InstallDiagnostics.Build(Input(new HostConfig(), ["[dock] no se pudo lanzar: una directiva de Control de aplicaciones bloqueó el archivo (Smart App Control o WDAC) [error 4551]", "[dock] Failed", "[bar] Running"]));
        Assert.Contains("Dock: elegido=sí · NO está en ejecución", r); Assert.Contains("[error 4551]", r); Assert.Contains("Barra superior: elegido=sí · en ejecución", r); Assert.Contains("Hatsu Notch: elegido=sí · en ejecución", r);
        Assert.Contains("Hatsu Connect: elegido=no · desactivado por ti (no es un fallo)", r); Assert.Contains("instalación MSI por usuario", r);
    }

    [Fact] public void The_report_never_includes_personal_data_and_redacts_the_profile()
    {
        var cfg = new HostConfig { Bar = new WinMac.Bar.BarConfig { WeatherCity = "Molina|-35.1|-71.2", WeatherFavorites = "Casa|1|2", CalendarIcsPath = @"C:\Users\ana\Documents\agenda.ics" } };
        var r = InstallDiagnostics.Build(Input(cfg, ["[dock] no se pudo lanzar: C:\\Users\\ana\\AppData\\x.exe [error 5]"], autostart: @"""C:\Users\ana\Apps\HatsuDesktop.exe"" --background"));
        Assert.DoesNotContain("Molina", r); Assert.DoesNotContain("agenda.ics", r); Assert.DoesNotContain("Casa", r); Assert.DoesNotContain(@"C:\Users\ana", r); Assert.DoesNotContain("ana\\", r); Assert.Contains("%USERPROFILE%", r);
        Assert.Equal(@"%USERPROFILE%\x", InstallDiagnostics.Redact(@"C:\Users\ana\x", @"C:\Users\ana", "ana")); Assert.Equal(@"D:\Users\<usuario>\y", InstallDiagnostics.Redact(@"D:\Users\ana\y", @"C:\Users\ana", "ana"));
        Assert.Contains("NO se envía a ningún sitio", r);
    }

    [Fact] public void The_report_reads_the_work_area_and_says_whether_windows_reserved_the_top_band_for_the_bar()
    {
        var bad = new[] { new MonitorArea("DISPLAY1", true, 0, 0, 1920, 1080, 0, 0, 1920, 1040, 1.0) };    // solo la barra de tareas de abajo: arriba NO hay reserva
        var good = new[] { new MonitorArea("DISPLAY1", true, 0, 0, 1920, 1080, 0, 32, 1920, 1040, 1.0), new MonitorArea("DISPLAY2", false, 1920, 0, 3840, 1080, 1920, 0, 3840, 1080, 1.25) };
        var i0 = Input(new HostConfig());
        var notReserved = InstallDiagnostics.Build(i0 with { Monitors = bad }); Assert.Contains("Windows NO tiene ninguna banda reservada arriba", notReserved); Assert.Contains("reservó arriba 0 px y abajo 40 px", notReserved);
        var reserved = InstallDiagnostics.Build(i0 with { Monitors = good }); Assert.Contains("Windows SÍ tiene una banda reservada arriba", reserved); Assert.Contains("reservó arriba 32 px", reserved); Assert.Contains("DISPLAY2", reserved); Assert.Contains("escala 1,25", reserved.Replace('.', ','));
        // si el usuario desactivó la reserva, el informe lo dice (causa posible de iconos bajo la Barra) y no afirma un fallo
        var off = InstallDiagnostics.Build(i0 with { Config = new HostConfig { Bar = new WinMac.Bar.BarConfig { ReserveSpace = false } }, Monitors = bad }); Assert.Contains("DESACTIVADO (la Barra se superpone", off); Assert.DoesNotContain("la reserva no se aplicó", off);
        Assert.Contains("NO mueve ni reordena los iconos del escritorio", notReserved);   // no se prometen iconos recolocados
        // las líneas del registro de la Barra sobre la reserva se incluyen (y solo esas)
        var withLog = InstallDiagnostics.Build(i0 with { Monitors = bad, BarLogTail = ["10:00 [bar] ABM_NEW falló en DISPLAY1: la barra se muestra superpuesta, sin reservar espacio.", "10:00 otra cosa"] }); Assert.Contains("ABM_NEW falló", withLog); Assert.DoesNotContain("otra cosa", withLog);
    }

    [Fact] public void The_report_covers_onboarding_legacy_autostart_taskbar_and_signatures()
    {
        var r = InstallDiagnostics.Build(Input(new HostConfig { Onboarding = new OnboardingRecord { Status = OnboardingStatus.Existing, Origin = "winmac-upgrade" }, HideTaskbar = true, Autostart = true }));
        Assert.Contains("Estado: Existing · origen: winmac-upgrade · bienvenida de migración mostrada: no", r); Assert.Contains("Configuración heredada: bar.json, dock.json", r); Assert.Contains("Inicio automático: no está activado", r);
        Assert.Contains("Ocultar la barra de tareas de Windows: activado", r); Assert.Contains("HatsuDesktop.Dock.exe: sin firma", r); Assert.Contains("HatsuDesktop.Connect.exe: no está", r);
    }
}
