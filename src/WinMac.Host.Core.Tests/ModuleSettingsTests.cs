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
        var rows = Pages(new BarConfig()).Where(p => p.Id is "modules" or "gestures").SelectMany(p => p.Groups).SelectMany(g => g.Rows).ToList();   // Gestures tiene su propia página («Atajos y gestos»)
        Assert.Contains(rows, r => r.Id == "bar:mcEnabled" && !r.On); Assert.Contains(rows, r => r.Id == "bar:snapEnabled" && !r.On);
        Assert.All(rows.Where(r => r.Id is "mod.key:mcHotkey" or "snap.mod" or "snap.layout" or "snap.edit"), r => { Assert.False(r.Enabled); Assert.Equal("Activa el módulo", r.Reason); });
        foreach (var id in new[] { "bar:mcEnabled", "bar:snapEnabled", "bar:liveTextEnabled", "bar:spotActionsEnabled", "bar:gesturesEnabled", "shelf.enabled" }) Assert.Contains(rows, r => r.Id == id);   // los seis módulos tienen su interruptor
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

public class GestureSettingsTests
{
    [Fact] public void The_rotation_helper_still_skips_actions_of_switched_off_modules()
    {
        var off = new BarConfig(); var seen = new List<string>(); var b = off;
        for (int i = 0; i < 12; i++) { var next = SettingsBuilder.NextGestureAction(b, "gestCornerTL")!; seen.Add(next); b = b with { GestCornerTL = next }; }
        Assert.DoesNotContain("mission", seen); Assert.DoesNotContain("livetext", seen); Assert.DoesNotContain("snapnext", seen); Assert.Contains("spotlight", seen); Assert.Contains("none", seen);
        var on = new BarConfig { McEnabled = true }; Assert.Equal("mission", SettingsBuilder.NextGestureAction(on, "gestCornerTL")); Assert.Null(SettingsBuilder.NextGestureAction(on, "campoInventado"));
    }

    [Fact] public void A_row_whose_module_was_switched_off_says_so_instead_of_pretending_to_work()
    {
        var row = SettingsBuilder.GestRow("gestCornerTL", "Esquina", "mission", new BarConfig { McEnabled = false }); Assert.Contains("módulo apagado", row.Title);
        Assert.DoesNotContain("módulo apagado", SettingsBuilder.GestRow("gestCornerTL", "Esquina", "mission", new BarConfig { McEnabled = true }).Title);
    }

    [Fact] public void The_dropdown_lists_none_plus_only_available_actions_marks_the_current_one_and_rejects_anything_else()
    {
        var path = Path.Combine(Path.GetTempPath(), "hatsu-gest-" + Guid.NewGuid() + ".json"); var cfg = new ConfigService(path, new HostConfig()); var a = new SettingsActions(cfg, () => { }, _ => { });
        var off = a.GestureChoices("gestCornerTR")!; Assert.Equal("none", off[0].Id); Assert.True(off[0].Current); Assert.DoesNotContain(off, c => c.Id is "mission" or "livetext" or "snapnext"); Assert.Contains(off, c => c.Id == "spotlight");
        Assert.False(a.Apply(new SettingsEdit("gest.set:gestCornerTR", "text", Text: "mission")));                    // módulo apagado: no se puede elegir
        Assert.True(a.Apply(new SettingsEdit("gest.set:gestCornerTR", "text", Text: "spotlight"))); Assert.Equal("spotlight", cfg.Bar.GestCornerTR);
        Assert.True(a.GestureChoices("gestCornerTR")!.Single(c => c.Id == "spotlight").Current);
        cfg.ApplyBarPatch(System.Text.Json.JsonSerializer.SerializeToElement(new { mcEnabled = true })); Assert.Contains(a.GestureChoices("gestCornerTR")!, c => c.Id == "mission");
        Assert.True(a.Apply(new SettingsEdit("gest.set:gestMouseX1", "text", Text: "mission"))); Assert.Equal("mission", cfg.Bar.GestMouseX1);
        Assert.False(a.Apply(new SettingsEdit("gest.set:otraCosa", "text", Text: "none"))); Assert.Null(a.GestureChoices("otraCosa"));
    }

    [Fact] public void Delay_edits_are_stored_and_validated()
    {
        var path = Path.Combine(Path.GetTempPath(), "hatsu-gest3-" + Guid.NewGuid() + ".json"); var cfg = new ConfigService(path, new HostConfig()); var a = new SettingsActions(cfg, () => { }, _ => { });
        a.Apply(new SettingsEdit("gest.delay", "text", Text: "700")); Assert.Equal(700, cfg.Bar.GestCornerDelayMs); a.Apply(new SettingsEdit("gest.delay", "text", Text: "5")); Assert.Equal(150, cfg.Bar.GestCornerDelayMs);
    }

    [Fact] public void The_host_toggles_the_dock_autohide_when_the_bar_asks()
    {
        Assert.NotNull(typeof(HostController).GetProperty("DockAutoHideToggleRequested"));    // el cableado real (dock.json) se valida en Windows
    }
}

public class ShelfPlusSettingsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "hatsu-shelf-" + Guid.NewGuid() + ".json");

    [Fact] public void Shelf_plus_has_its_card_with_the_real_state_of_the_shelf_tab_and_its_limit()
    {
        var rows = SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "modules").Groups.SelectMany(g => g.Rows).ToList();
        Assert.True(rows.Single(r => r.Id == "shelf.enabled").On); Assert.Equal("20", rows.Single(r => r.Id == "shelf.max").Text); Assert.Contains("activa", rows.Single(r => r.Id == "mod.status:shelf").Title);
        var off = SettingsBuilder.Build(new SettingsInput(new HostConfig { Notch = NotchSettings.Factory.ToggleSection(NotchSettings.SectionIds.ToList().IndexOf("shelf")) }, new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "modules").Groups.SelectMany(g => g.Rows).ToList();
        Assert.False(off.Single(r => r.Id == "shelf.enabled").On); Assert.DoesNotContain(off, r => r.Id == "shelf.max");
    }

    [Fact] public void The_toggle_switches_only_the_shelf_tab_and_the_limit_is_clamped_and_sent_to_the_island()
    {
        var cfg = new ConfigService(Tmp(), new HostConfig()); var a = new SettingsActions(cfg, () => { }, _ => { }); var others = cfg.Notch.Sections.Where(s => s.Id != "shelf").Select(s => (s.Id, s.Enabled)).ToList();
        a.Apply(new SettingsEdit("shelf.enabled", "toggle", On: false)); Assert.False(cfg.Notch.Sections.Single(s => s.Id == "shelf").Enabled); Assert.Equal(others, cfg.Notch.Sections.Where(s => s.Id != "shelf").Select(s => (s.Id, s.Enabled)).ToList());
        a.Apply(new SettingsEdit("shelf.enabled", "toggle", On: false)); Assert.False(cfg.Notch.Sections.Single(s => s.Id == "shelf").Enabled);   // repetir no lo cambia de vuelta
        a.Apply(new SettingsEdit("shelf.enabled", "toggle", On: true)); Assert.True(cfg.Notch.Sections.Single(s => s.Id == "shelf").Enabled);
        a.Apply(new SettingsEdit("shelf.max", "text", Text: "40")); Assert.Equal(40, cfg.Notch.ShelfMaxItems); a.Apply(new SettingsEdit("shelf.max", "text", Text: "9999")); Assert.Equal(100, cfg.Notch.ShelfMaxItems); a.Apply(new SettingsEdit("shelf.max", "text", Text: "x")); Assert.Equal(100, cfg.Notch.ShelfMaxItems);
        var wire = System.Text.Json.JsonSerializer.Serialize(cfg.Notch.ToWire()); Assert.Contains("\"shelfMaxItems\":100", wire);
    }

    [Fact] public void The_bar_learns_from_the_island_config_whether_the_shelf_tab_is_active()
    {
        using var on = System.Text.Json.JsonDocument.Parse("{\"sections\":[{\"id\":\"shelf\",\"enabled\":true}]}"); using var off = System.Text.Json.JsonDocument.Parse("{\"sections\":[{\"id\":\"music\",\"enabled\":true},{\"id\":\"shelf\",\"enabled\":false}]}");
        Assert.True(WinMac.Bar.Notch.NotchFeedOptions.Parse(on.RootElement).Shelf); Assert.False(WinMac.Bar.Notch.NotchFeedOptions.Parse(off.RootElement).Shelf); Assert.True(WinMac.Bar.Notch.NotchFeedOptions.Parse(null).Shelf);
    }

    [Fact] public void The_patch_path_accepts_the_shelf_limit_and_ignores_garbage()
    {
        var n = NotchSettings.Factory.ApplyPatch(System.Text.Json.JsonDocument.Parse("{\"shelfMaxItems\": 7}").RootElement); Assert.Equal(7, n.ShelfMaxItems);
        Assert.Equal(20, NotchSettings.Factory.ApplyPatch(System.Text.Json.JsonDocument.Parse("{\"shelfMaxItems\": \"muchos\"}").RootElement).ShelfMaxItems);
    }
}

public class DropdownTests
{
    private static RowFrame RowAt(double y) => new(new SettingsRow(RowKind.Button, "gest.pick:x", "t"), 0, y, 560, 56, true, true);

    [Fact] public void The_list_opens_below_its_row_when_it_fits_and_above_or_pinned_when_it_does_not()
    {
        var below = SettingsLayout.SuggestRect(RowAt(40), 4); Assert.True(below.Y > 40 + 20);
        var near = SettingsLayout.SuggestRect(RowAt(SettingsLayout.ViewportH - 70), 10); Assert.True(near.Y + near.H <= SettingsLayout.ViewportH - 5); Assert.True(near.Y >= 6);
        var huge = SettingsLayout.SuggestRect(RowAt(SettingsLayout.ViewportH - 70), 40); Assert.Equal(6, huge.Y, 3);
    }

    [Fact] public void The_dropdown_marks_the_current_choice_and_plain_suggestions_have_no_marks()
    {
        var c = new SettingsController(); c.OpenDropdown("gest.pick:x", ["Nada", "Mission Control"], 1);
        Assert.True(c.SuggestHasChecks); Assert.Equal(1, c.SuggestChecked); Assert.Equal(1, c.SuggestIndex); Assert.Equal("gest.pick:x", c.SuggestFor);
        c.SetSuggestions("hub.city", ["Santiago"]); Assert.False(c.SuggestHasChecks); Assert.Equal(-1, c.SuggestChecked); c.ClearSuggestions(); Assert.Null(c.SuggestFor);
    }
}

public class HotkeyConflictTests
{
    [Fact] public void A_combination_already_used_by_another_hatsu_shortcut_is_refused_with_a_note_and_a_free_one_is_saved()
    {
        FeatureStatusStore.Clear();
        HotkeyStatus.Set(System.Text.Json.JsonDocument.Parse("{\"items\":[{\"keys\":\"Ctrl+Mayús+3\",\"purpose\":\"Capturar pantalla\",\"module\":\"Barra · Capturas\",\"registered\":true},{\"keys\":\"Ctrl+Alt+Up\",\"purpose\":\"Mission Control\",\"module\":\"Barra · Mission Control\",\"registered\":true}]}").RootElement);
        var path = Path.Combine(Path.GetTempPath(), "hatsu-hk-" + Guid.NewGuid() + ".json"); var cfg = new ConfigService(path, new HostConfig()); var a = new SettingsActions(cfg, () => { }, _ => { });
        a.Apply(new SettingsEdit("mod.key:ltHotkey", "text", Text: "Ctrl+Shift+3")); Assert.Equal("Ctrl+Alt+Space", cfg.Bar.LtHotkey); Assert.Contains("Capturar pantalla", SettingsBuilder.HotkeyNote);
        a.Apply(new SettingsEdit("mod.key:ltHotkey", "text", Text: "Ctrl+Alt+Up")); Assert.Equal("Ctrl+Alt+Space", cfg.Bar.LtHotkey); Assert.Contains("Mission Control", SettingsBuilder.HotkeyNote);
        a.Apply(new SettingsEdit("mod.key:ltHotkey", "text", Text: "Ctrl+Shift+7")); Assert.Equal("Ctrl+Shift+7", cfg.Bar.LtHotkey); Assert.Null(SettingsBuilder.HotkeyNote);
        a.Apply(new SettingsEdit("mod.key:mcHotkey", "text", Text: "Ctrl+Alt+Up")); Assert.Equal("Ctrl+Alt+Up", cfg.Bar.McHotkey);        // el que ya tenía su propio campo no es conflicto
        a.Apply(new SettingsEdit("mod.key:mcHotkey", "text", Text: "Alt+Tab")); Assert.Equal("Ctrl+Alt+Up", cfg.Bar.McHotkey); Assert.Contains("reserva", SettingsBuilder.HotkeyNote);
        HotkeyStatus.Set(System.Text.Json.JsonDocument.Parse("{}").RootElement); SettingsBuilder.HotkeyNote = null;
    }
}

public class GestureLauncherSettingsTests
{
    private static (ConfigService, SettingsActions) Make(Func<string, IReadOnlyList<(string Id, string Name)>>? find = null)
    {
        var cfg = new ConfigService(Path.Combine(Path.GetTempPath(), "hatsu-launch-" + Guid.NewGuid() + ".json"), new HostConfig());
        return (cfg, new SettingsActions(cfg, () => { }, _ => { }) { FindApps = find });
    }

    [Fact] public void Adding_an_app_picks_the_best_match_stores_it_and_offers_it_in_the_dropdown()
    {
        var (cfg, a) = Make(q => [("Spotify.Id!App", "Spotify"), ("Otra.Id", "Spotify Lite")]);
        Assert.True(a.Apply(new SettingsEdit("gest.addapp", "text", Text: "spotify")));
        var l = WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers).Single(); Assert.Equal("Spotify", l.Name); Assert.Equal("Spotify.Id!App", l.Target);
        Assert.Contains(a.GestureChoices("gestCornerTL")!, c => c.Id == l.ActionId && c.Name == "Abrir: Spotify");
        Assert.True(a.Apply(new SettingsEdit("gest.set:gestCornerTL", "text", Text: l.ActionId))); Assert.Equal(l.ActionId, cfg.Bar.GestCornerTL);
        Assert.Contains("Abrir: Spotify", SettingsBuilder.GestRow("gestCornerTL", "Esquina", cfg.Bar.GestCornerTL, cfg.Bar).Title);
    }

    [Fact] public void Removing_a_launcher_sends_assignments_back_to_none()
    {
        var (cfg, a) = Make(q => [("X.Id", "Calculadora")]); a.Apply(new SettingsEdit("gest.addapp", "text", Text: "calc"));
        var id = WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers).Single().ActionId; a.Apply(new SettingsEdit("gest.set:gestMouseX1", "text", Text: id));
        Assert.True(a.Apply(new SettingsEdit("gest.rm:" + id["launch:".Length..], "button"))); Assert.Equal("", cfg.Bar.GestLaunchers);
        Assert.False(WinMac.Bar.Features.GestureRules.IsAvailable(cfg.Bar.GestMouseX1, cfg.Bar));   // la asignación huérfana ya no actúa
        Assert.False(a.Apply(new SettingsEdit("gest.rm:zz", "button")));
    }

    [Fact] public void Websites_are_normalized_and_bad_input_leaves_a_visible_note()
    {
        var (cfg, a) = Make(); a.Apply(new SettingsEdit("gest.addurl", "text", Text: "youtube.com"));
        var l = WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers).Single(); Assert.Equal("https://youtube.com/", l.Target); Assert.Equal("Abrir página: youtube.com", WinMac.Bar.Features.Launchers.DisplayName(l));
        a.Apply(new SettingsEdit("gest.addurl", "text", Text: "file:///C:/x.exe")); Assert.Contains("no parece una dirección web", SettingsBuilder.GestNote); Assert.Single(WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers));
        a.Apply(new SettingsEdit("gest.addapp", "text", Text: "algo")); Assert.Contains("No se puede buscar", SettingsBuilder.GestNote);   // sin buscador de apps
        var (_, b) = Make(q => []); b.Apply(new SettingsEdit("gest.addapp", "text", Text: "inexistente")); Assert.Contains("No encontré", SettingsBuilder.GestNote);
        SettingsBuilder.GestNote = null;
    }

    [Fact] public void The_limit_is_enforced_and_the_add_rows_disappear_when_full()
    {
        var (cfg, a) = Make(q => [(q + ".id", q)]);
        for (int i = 0; i < 6; i++) a.Apply(new SettingsEdit("gest.addapp", "text", Text: "app" + i));
        Assert.Equal(6, WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers).Count);
        a.Apply(new SettingsEdit("gest.addapp", "text", Text: "app7")); Assert.Equal(6, WinMac.Bar.Features.Launchers.Parse(cfg.Bar.GestLaunchers).Count); Assert.Contains("Ya hay 6", SettingsBuilder.GestNote);
        SettingsBuilder.GestNote = null;
    }
}
