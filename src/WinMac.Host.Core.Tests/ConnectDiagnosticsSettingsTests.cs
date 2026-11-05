using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Ajustes › Hatsu Connect › Diagnóstico de llamadas (docs/58): botones reales, estados, y NINGÚN botón de contestar/rechazar.</summary>
public class ConnectDiagnosticsSettingsTests
{
    private static IReadOnlyList<SettingsRow> Rows(ConnectDiagView? v = null) =>
        SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false) { ConnectDiag = v ?? new() }).Single(p => p.Id == "connect").Groups.SelectMany(g => g.Rows).ToList();

    [Fact] public void Fresh_page_offers_run_and_observe_but_report_actions_are_disabled_with_a_reason()
    {
        var r = Rows();
        Assert.True(r.Single(x => x.Id == "connect.diag.run").Enabled);
        foreach (var id in new[] { "connect.diag.open", "connect.diag.copy", "connect.diag.save" }) { var row = r.Single(x => x.Id == id); Assert.False(row.Enabled); Assert.False(string.IsNullOrEmpty(row.Reason)); }
        Assert.False(r.Single(x => x.Id == "connect.diag.observe").On);
    }

    [Fact] public void While_running_the_run_button_is_disabled_and_a_finished_report_enables_open_copy_save_and_shows_the_summary()
    {
        Assert.False(Rows(new ConnectDiagView(Running: true, Status: "Ejecutando…")).Single(x => x.Id == "connect.diag.run").Enabled);
        var r = Rows(new ConnectDiagView("Terminado 12:00", ["Transporte telefónico Bluetooth: Acceso denegado — DeniedBySystem", "Endpoints de audio Hands-Free: Detectado"], HasReport: true));
        Assert.All(new[] { "connect.diag.open", "connect.diag.copy", "connect.diag.save" }, id => Assert.True(r.Single(x => x.Id == id).Enabled));
        Assert.Contains(r, x => x.Id == "connect.diag.sum:0" && x.Title.Contains("Acceso denegado")); Assert.Contains(r, x => x.Title == "Estado: Terminado 12:00");
    }

    [Fact] public void Observer_toggle_reflects_the_temporary_state_and_never_touches_the_saved_config()
    {
        Assert.True(Rows(new ConnectDiagView(Observing: true, ObserveStatus: "Activa hasta las 12:15")).Single(x => x.Id == "connect.diag.observe").On);
        var path = Path.Combine(Path.GetTempPath(), "hatsu-diag-" + Guid.NewGuid() + ".json"); var cfg = new ConfigService(path, new HostConfig()); bool? obs = null;
        var before = cfg.Current;
        var a = new SettingsActions(cfg, () => { }, _ => { }) { DiagObserve = on => obs = on };
        Assert.True(a.Apply(new SettingsEdit("connect.diag.observe", "toggle", On: true))); Assert.True(obs);
        Assert.Equal(before, cfg.Current); Assert.False(File.Exists(path));   // nada persistente
    }

    [Fact] public void Actions_call_the_host_callbacks_and_report_false_when_there_is_no_service()
    {
        var cfg = new ConfigService(Path.Combine(Path.GetTempPath(), "hatsu-diag2.json"), new HostConfig()); var calls = new List<string>();
        var a = new SettingsActions(cfg, () => { }, _ => { }) { DiagRun = () => calls.Add("run"), DiagOpen = () => calls.Add("open"), DiagCopy = () => calls.Add("copy"), DiagSave = () => calls.Add("save") };
        foreach (var id in new[] { "run", "open", "copy", "save" }) Assert.True(a.Apply(new SettingsEdit("connect.diag." + id, "button")));
        Assert.Equal(["run", "open", "copy", "save"], calls);
        var none = new SettingsActions(cfg, () => { }, _ => { });
        Assert.False(none.Apply(new SettingsEdit("connect.diag.run", "button"))); Assert.False(none.Apply(new SettingsEdit("connect.diag.observe", "toggle", On: true)));
    }

    [Fact] public void The_page_has_no_answer_reject_or_hangup_controls_and_explains_the_ios_and_android_sequences()
    {
        var r = Rows();
        Assert.DoesNotContain(r, x => x.Id.Contains("answer") || x.Id.Contains("reject") || x.Id.Contains("hangup") || x.Id.Contains("dial"));
        var ios = r.Single(x => x.Id == "connect.diag.ios").Title; var and = r.Single(x => x.Id == "connect.diag.android").Title;
        Assert.Contains("NO debe aparecer «Llamada finalizada»", ios); Assert.Contains("Android no usa ANCS", and);
    }

    [Fact] public void Experiments_are_separate_labelled_as_modifying_and_routed_with_their_kind_only()
    {
        var r = Rows();
        foreach (var id in new[] { "connect.diag.exp:hfp", "connect.diag.exp:audio", "connect.diag.exp:restore" }) Assert.True(r.Single(x => x.Id == id).Enabled);
        Assert.Contains("MODIFICA", r.Single(x => x.Id == "connect.diag.exp:hfp").Subtitle!); Assert.Contains("confirmación", r.Single(x => x.Id == "connect.diag.exp:audio").Subtitle!);
        Assert.False(Rows(new ConnectDiagView(Running: true)).Single(x => x.Id == "connect.diag.exp:hfp").Enabled);   // nunca dos a la vez
        var cfg = new ConfigService(Path.Combine(Path.GetTempPath(), "hatsu-diag3.json"), new HostConfig()); var got = new List<string>();
        var a = new SettingsActions(cfg, () => { }, _ => { }) { DiagExperiment = k => got.Add(k) };
        foreach (var k in new[] { "hfp", "audio", "restore" }) Assert.True(a.Apply(new SettingsEdit("connect.diag.exp:" + k, "button")));
        Assert.Equal(["hfp", "audio", "restore"], got);
        Assert.False(a.Apply(new SettingsEdit("connect.diag.exp:bogus", "button"))); Assert.False(new SettingsActions(cfg, () => { }, _ => { }).Apply(new SettingsEdit("connect.diag.exp:hfp", "button")));
    }
}

public class CallControlSettingsTests
{
    [Fact] public void Call_control_is_opt_in_off_by_default_explains_the_requirement_and_persists_only_when_toggled()
    {
        Assert.False(new WinMac.Connect.ConnectSettings().CallControl);
        var cfg = new HostConfig { Connect = new WinMac.Connect.ConnectSettings() };
        var rows = SettingsBuilder.Build(new SettingsInput(cfg, new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "connect").Groups.SelectMany(g => g.Rows).ToList();
        var t = rows.Single(r => r.Id == "connect.callcontrol"); Assert.False(t.On); Assert.Contains("audio en el PC", t.Title);
        Assert.Contains("capacidad restringida", rows.Single(r => r.Id == "connect.callcontrol.why").Title);
        var path = Path.Combine(Path.GetTempPath(), "hatsu-cc-" + Guid.NewGuid() + ".json"); var svc = new ConfigService(path, cfg);
        Assert.True(new SettingsActions(svc, () => { }, _ => { }).Apply(new SettingsEdit("connect.callcontrol", "toggle", On: true)));
        Assert.True(svc.Current.Connect!.CallControl);
        Assert.True(WinMac.Connect.ConnectSettings.Parse(System.Text.Json.JsonSerializer.SerializeToElement(svc.Current.Connect, WinMac.Connect.ConnectSettings.Wire)).CallControl);   // viaja al módulo por el canal
    }
}
