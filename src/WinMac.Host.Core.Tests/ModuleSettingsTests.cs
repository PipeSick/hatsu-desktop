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
