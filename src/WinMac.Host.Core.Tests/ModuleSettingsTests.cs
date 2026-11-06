using System.Text.Json;
using WinMac.Bar;
using Xunit;

namespace WinMac.Host.Core.Tests;

public class ModuleSettingsTests
{
    private static (ConfigService Cfg, SettingsActions A, string Path) Make()
    {
        var path = Path.Combine(Path.GetTempPath(), "hatsu-mod-" + Guid.NewGuid() + ".json"); var cfg = new ConfigService(path, new HostConfig());
        return (cfg, new SettingsActions(cfg, () => { }, _ => { }), path);
    }

    [Fact] public void A_valid_hotkey_is_stored_in_canonical_form_and_an_invalid_one_changes_nothing()
    {
        var (cfg, a, _) = Make();
        Assert.True(a.Apply(new SettingsEdit("mod.key:mcHotkey", "text", Text: "ctrl + alt + m"))); Assert.Equal("Ctrl+Alt+M", cfg.Bar.McHotkey);
        Assert.True(a.Apply(new SettingsEdit("mod.key:mcHotkey", "text", Text: "M"))); Assert.Equal("Ctrl+Alt+M", cfg.Bar.McHotkey);        // sin Ctrl/Alt: se rechaza
        Assert.True(a.Apply(new SettingsEdit("mod.key:mcHotkey", "text", Text: "")));  Assert.Equal("", cfg.Bar.McHotkey);                  // quitar el atajo es válido
        Assert.False(a.Apply(new SettingsEdit("mod.key:nombreInventado", "text", Text: "Ctrl+Alt+M")));                                      // solo campos de atajo conocidos
    }

    [Fact] public void Clearing_a_hotkey_does_not_switch_the_module_off()
    {
        var (cfg, a, _) = Make(); cfg.ApplyBarPatch(JsonSerializer.SerializeToElement(new Dictionary<string, object?> { ["mcEnabled"] = true }));
        a.Apply(new SettingsEdit("mod.key:mcHotkey", "text", Text: "")); Assert.True(cfg.Bar.McEnabled); Assert.Equal("", cfg.Bar.McHotkey);
    }

    [Fact] public void Retry_bumps_the_token_so_the_bar_restarts_failed_modules()
    {
        var (cfg, a, _) = Make(); int t0 = cfg.Bar.FeatureRetryToken;
        Assert.True(a.Apply(new SettingsEdit("feature.retry:mission", "button"))); Assert.Equal(t0 + 1, cfg.Bar.FeatureRetryToken);
    }

    [Fact] public void Status_text_tells_the_real_state_including_the_cause_of_a_failure()
    {
        Assert.Equal("Estado: apagado", SettingsBuilder.StatusText(false, null, ""));
        Assert.Contains("sin informar", SettingsBuilder.StatusText(true, null, ""));
        Assert.Contains("en marcha", SettingsBuilder.StatusText(true, new FeatureStatusStore.Item("mission", "Running", null, 0), ""));
        Assert.Contains("No arrancó: sin permiso", SettingsBuilder.StatusText(true, new FeatureStatusStore.Item("mission", "Failed", "No arrancó: sin permiso", 1), ""));
    }

    [Fact] public void The_feature_status_message_from_the_bar_is_parsed_and_malformed_items_are_ignored()
    {
        FeatureStatusStore.Set(JsonDocument.Parse("{\"items\":[{\"id\":\"mission\",\"state\":\"Running\",\"faults\":0},{\"id\":\"\",\"state\":\"x\"},{\"state\":\"Failed\"}]}").RootElement);
        Assert.Equal("Running", FeatureStatusStore.Get("mission")!.State); Assert.Null(FeatureStatusStore.Get("otro"));
        FeatureStatusStore.Set(JsonDocument.Parse("42").RootElement); Assert.Null(FeatureStatusStore.Get("mission"));
    }
}

public class ModulePageTests
{
    private static IReadOnlyList<SettingsPage> Pages(BarConfig bar) => SettingsBuilder.Build(new SettingsInput(new HostConfig { Bar = bar }, new Dictionary<string, ModuleState>(), false));

    [Fact] public void The_modules_page_lists_only_implemented_modules_and_disables_their_settings_while_off()
    {
        var page = Pages(new BarConfig()).Single(p => p.Id == "modules"); var rows = page.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Contains(rows, r => r.Id == "bar:mcEnabled" && !r.On); Assert.Contains(rows, r => r.Id == "bar:snapEnabled" && !r.On);
        Assert.All(rows.Where(r => r.Id is "mod.key:mcHotkey" or "snap.mod" or "snap.layout" or "snap.edit"), r => { Assert.False(r.Enabled); Assert.Equal("Activa el módulo", r.Reason); });
        Assert.DoesNotContain(rows, r => r.Id.Contains("gest", StringComparison.OrdinalIgnoreCase));   // sin controles de módulos que todavía no existen
    }

    [Fact] public void Turning_a_module_on_enables_its_settings_and_shows_its_status()
    {
        var rows = Pages(new BarConfig { McEnabled = true, SnapEnabled = true }).Single(p => p.Id == "modules").Groups.SelectMany(g => g.Rows).ToList();
        Assert.True(rows.Single(r => r.Id == "mod.key:mcHotkey").Enabled); Assert.Equal("Ctrl+Alt+Up", rows.Single(r => r.Id == "mod.key:mcHotkey").Text);
        Assert.Contains("sin informar", rows.Single(r => r.Id == "mod.status:mission").Title); Assert.True(rows.Single(r => r.Id == "snap.edit").Enabled);
        Assert.Contains("Mitades", rows.Single(r => r.Id == "snap.layout").Options!);
    }

    [Fact] public void Snap_edits_are_validated_and_stored()
    {
        var path = Path.Combine(Path.GetTempPath(), "hatsu-snap-" + Guid.NewGuid() + ".json"); var cfg = new ConfigService(path, new HostConfig()); var a = new SettingsActions(cfg, () => { }, _ => { });
        Assert.True(a.Apply(new SettingsEdit("snap.mod", "segment", Index: 1))); Assert.Equal("Ctrl", cfg.Bar.SnapModifier);
        Assert.False(a.Apply(new SettingsEdit("snap.mod", "segment", Index: 9)));
        a.Apply(new SettingsEdit("snap.gap", "text", Text: "12")); Assert.Equal(12, cfg.Bar.SnapGap); a.Apply(new SettingsEdit("snap.gap", "text", Text: "abc")); Assert.Equal(12, cfg.Bar.SnapGap); a.Apply(new SettingsEdit("snap.gap", "text", Text: "500")); Assert.Equal(64, cfg.Bar.SnapGap);
        int t = cfg.Bar.SnapEditToken; a.Apply(new SettingsEdit("snap.edit", "button")); Assert.Equal(t + 1, cfg.Bar.SnapEditToken);
        a.Apply(new SettingsEdit("snap.layout", "segment", Index: 1)); Assert.Equal("Tercios", cfg.Bar.SnapLayout);
    }
}
