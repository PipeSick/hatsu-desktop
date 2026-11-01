using System.Text.Json;
using WinMac.Host;
using Xunit;

public class NotchSettingsTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Defaults_match_what_NotchWin_always_had_with_the_camera_off()
    {
        var n = new NotchSettings(); Assert.Equal("TopCenter", n.Position); Assert.Equal(60, n.GlassLevel); Assert.Equal(30, n.PollIntervalSeconds); Assert.False(n.LyricsEnabled);
        Assert.Equal(NotchSettings.SectionIds, n.Sections.Select(s => s.Id)); Assert.False(n.Sections.Single(s => s.Id == "camera").Enabled); Assert.All(n.Sections.Where(s => s.Id != "camera"), s => Assert.True(s.Enabled));
    }

    [Fact]
    public void A_patch_changes_only_the_fields_it_names_and_ignores_unknown_ones()
    {
        var n = new NotchSettings { GlassLevel = 70 }.ApplyPatch(J("{\"lyricsEnabled\":true,\"position\":\"topright\",\"futureThing\":123}"));
        Assert.True(n.LyricsEnabled); Assert.Equal("TopRight", n.Position); Assert.Equal(70, n.GlassLevel);   // lo no nombrado se conserva
    }

    [Theory]
    [InlineData("{\"glassLevel\":250}", 100)]
    [InlineData("{\"glassLevel\":-4}", 0)]
    [InlineData("{\"glassLevel\":\"mucho\"}", 60)]     // tipo equivocado: se ignora
    [InlineData("{\"glassLevel\":12.5}", 60)]          // no es entero: se ignora
    public void Out_of_range_or_wrongly_typed_values_never_get_in(string patch, int expected) => Assert.Equal(expected, new NotchSettings().ApplyPatch(J(patch)).GlassLevel);

    [Fact]
    public void Enums_are_validated_and_unknown_values_keep_the_previous_one()
    {
        var n = new NotchSettings { Position = "TopLeft", MusicPlayer = "Feishin" }.ApplyPatch(J("{\"position\":\"Bottom\",\"musicPlayer\":\"Winamp\"}"));
        Assert.Equal("TopCenter", n.Position);   // Sanitize cae al valor de fábrica si el texto no es válido
        Assert.Equal("Spotify", n.MusicPlayer);
        Assert.Equal("Custom", new NotchSettings().ApplyPatch(J("{\"musicPlayer\":\"custom\"}")).MusicPlayer);
    }

    [Fact]
    public void Poll_interval_and_player_path_are_clamped_and_trimmed()
    {
        var n = new NotchSettings().ApplyPatch(J("{\"pollIntervalSeconds\":1,\"customPlayerPath\":\"  \\\"C:\\\\x\\\\p.exe\\\"  \"}"));
        Assert.Equal(5, n.PollIntervalSeconds); Assert.Equal("C:\\x\\p.exe", n.CustomPlayerPath);
        Assert.Null(n.ApplyPatch(J("{\"customPlayerPath\":null}")).CustomPlayerPath); Assert.Equal(3600, n.ApplyPatch(J("{\"pollIntervalSeconds\":99999}")).PollIntervalSeconds);
    }

    [Fact]
    public void Sections_keep_the_given_order_drop_unknown_and_duplicate_ids_and_append_the_missing()
    {
        var n = new NotchSettings().ApplyPatch(J("{\"sections\":[{\"id\":\"timer\",\"enabled\":false},{\"id\":\"music\",\"enabled\":true},{\"id\":\"ghost\",\"enabled\":true},{\"id\":\"timer\",\"enabled\":true}]}"));
        Assert.Equal("timer", n.Sections[0].Id); Assert.False(n.Sections[0].Enabled); Assert.Equal("music", n.Sections[1].Id);
        Assert.Equal(NotchSettings.SectionIds.Length, n.Sections.Count); Assert.DoesNotContain(n.Sections, s => s.Id == "ghost"); Assert.False(n.Sections.Single(s => s.Id == "camera").Enabled);
    }

    [Fact]
    public void Non_object_patches_and_malformed_section_entries_change_nothing()
    {
        var d = new NotchSettings(); Assert.Equal(d, d.ApplyPatch(J("[1,2]"))); Assert.Equal(d, d.ApplyPatch(J("\"hola\"")));
        var n = d.ApplyPatch(J("{\"sections\":[{\"id\":5},{\"enabled\":true},3]}")); Assert.Equal(NotchSettings.DefaultSections().Select(s => s.Id), n.Sections.Select(s => s.Id));
    }

    [Fact]
    public void Move_and_toggle_are_pure_and_safe_at_the_edges()
    {
        var n = new NotchSettings(); var m = n.MoveSection(0, 1); Assert.Equal("agents", m.Sections[0].Id); Assert.Equal("music", n.Sections[0].Id);   // el original no cambia
        Assert.Same(n, n.MoveSection(0, -1)); Assert.Same(n, n.MoveSection(99, 1)); Assert.Same(n, n.ToggleSection(-1));
        Assert.False(n.ToggleSection(0).Sections[0].Enabled);
    }

    [Fact]
    public void The_wire_format_is_camelCase_and_roundtrips_through_a_patch()
    {
        var n = new NotchSettings { GlassLevel = 41, LyricsInPill = true, Position = "TopRight" }.MoveSection(2, -2);
        string json = JsonSerializer.Serialize(n.ToWire()); Assert.Contains("\"glassLevel\":41", json); Assert.Contains("\"sections\":[{\"id\":\"timer\"", json);
        Assert.True(ConfigService.SameNotch(n, new NotchSettings().ApplyPatch(J(json))));
    }

    [Fact]
    public void Import_takes_only_the_central_fields_and_never_a_secret()
    {
        const string old = "{\"NotchModuleEnabled\":true,\"MusicModuleEnabled\":false,\"AiAgentsModuleEnabled\":true,\"PollIntervalSeconds\":45,\"AnthropicAdminApiKey\":\"sk-ant-admin-SECRETO\",\"ClaudeHomeOverride\":\"D:\\\\c\",\"NotchPosition\":2,\"MusicPlayer\":1,\"GlassLevel\":80,\"LyricsEnabled\":true,\"LyricsInPill\":true,\"NotchSections\":[{\"Id\":\"timer\",\"Enabled\":true},{\"Id\":\"music\",\"Enabled\":false}]}";
        var n = NotchSettings.ImportFromNotchWin(old)!;
        Assert.Equal("TopRight", n.Position); Assert.Equal("Feishin", n.MusicPlayer); Assert.Equal(80, n.GlassLevel); Assert.False(n.MusicEnabled); Assert.True(n.AgentsEnabled); Assert.Equal(45, n.PollIntervalSeconds);
        Assert.True(n.LyricsEnabled && n.LyricsInPill); Assert.Equal("timer", n.Sections[0].Id); Assert.False(n.Sections.Single(s => s.Id == "music").Enabled);
        string all = JsonSerializer.Serialize(n) + JsonSerializer.Serialize(n.ToWire()); Assert.DoesNotContain("SECRETO", all); Assert.DoesNotContain("sk-ant", all); Assert.DoesNotContain("D:\\\\c", all);   // ni la clave ni las rutas locales
    }

    [Fact]
    public void Import_accepts_text_enums_and_rejects_garbage()
    {
        Assert.Equal("TopLeft", NotchSettings.ImportFromNotchWin("{\"NotchPosition\":\"TopLeft\"}")!.Position);
        Assert.Null(NotchSettings.ImportFromNotchWin("no es json")); Assert.Null(NotchSettings.ImportFromNotchWin("[1]"));
        Assert.Equal("Spotify", NotchSettings.ImportFromNotchWin("{\"MusicPlayer\":99}")!.MusicPlayer);
    }
}

public class ConfigServiceTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "winmac-cfg-" + Guid.NewGuid().ToString("N") + ".json");
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void A_change_is_saved_and_reloaded_and_a_no_op_neither_saves_nor_notifies()
    {
        var f = Tmp();
        try
        {
            var s = new ConfigService(f, new HostConfig()); int changes = 0; s.Changed += (_, _) => changes++;
            Assert.True(s.ApplyNotchPatch(J("{\"glassLevel\":25}"))); Assert.Equal(1, changes); Assert.Equal(25, HostConfig.Load(f).Notch!.GlassLevel);
            File.Delete(f); Assert.False(s.ApplyNotchPatch(J("{\"glassLevel\":25}"))); Assert.Equal(1, changes); Assert.False(File.Exists(f));   // sin cambio efectivo: nada se escribe
            Assert.True(s.SetModuleEnabled("notch", false)); Assert.False(HostConfig.Load(f).Modules["notch"].Enabled); Assert.False(s.SetModuleEnabled("notch", false)); Assert.False(s.SetModuleEnabled("nope", true));
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }

    [Fact]
    public void A_save_failure_keeps_the_change_in_memory_and_is_logged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winmac-cfg-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var log = new List<string>(); var s = new ConfigService(dir, new HostConfig(), log.Add);   // la «ruta del archivo» es una carpeta: guardar falla
        try { Assert.True(s.ApplyNotchPatch(J("{\"glassLevel\":10}"))); Assert.Equal(10, s.Notch.GlassLevel); Assert.Contains(log, l => l.Contains("no se pudo guardar")); }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Saving_the_notch_keeps_every_other_field_of_winmac_json()
    {
        var f = Tmp();
        try
        {
            var start = new HostConfig { Autostart = true, Modules = new HostConfig().Modules.ToDictionary(k => k.Key, v => v.Value) }; start.Modules["bar"] = new ModuleConfig { Enabled = false, Path = "D:\\bar.exe" };
            var s = new ConfigService(f, start); s.ApplyNotchPatch(J("{\"lyricsEnabled\":true}"));
            var l = HostConfig.Load(f); Assert.True(l.Autostart); Assert.False(l.Modules["bar"].Enabled); Assert.Equal("D:\\bar.exe", l.Modules["bar"].Path); Assert.True(l.Notch!.LyricsEnabled);
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }
}

public class HostControllerTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static (Fixture F, ConfigService C, HostController H, string File) Make()
    {
        var f = new Fixture().With(("bar", true), ("dock", true), ("notch", true)); var file = Path.Combine(Path.GetTempPath(), "winmac-hc-" + Guid.NewGuid().ToString("N") + ".json");
        var c = new ConfigService(file, new HostConfig()); var h = new HostController(c, f.Sup, f.Bus); return (f, c, h, file);
    }

    [Fact]
    public void Start_publishes_the_current_notch_settings_as_retained_state()
    {
        var (f, _, h, file) = Make(); try { h.Start(); lock (f.Bus.Published) { Assert.Equal(2, f.Bus.Published.Count); Assert.Equal(NotchSettings.Topic, f.Bus.Published[0].Topic); Assert.Equal(HostController.BarTopic, f.Bus.Published[1].Topic); } } finally { File.Delete(file); }
    }

    [Fact]
    public void A_patch_from_the_notch_is_saved_and_republished_but_one_from_another_module_is_ignored()
    {
        var (f, c, h, file) = Make();
        try
        {
            f.Bus.RaiseEvent("notch", NotchSettings.SetTopic, "{\"glassLevel\":15}"); Assert.Equal(15, c.Notch.GlassLevel); lock (f.Bus.Published) Assert.Equal(NotchSettings.Topic, f.Bus.Published[^1].Topic);
            f.Bus.RaiseEvent("dock", NotchSettings.SetTopic, "{\"glassLevel\":99}"); Assert.Equal(15, c.Notch.GlassLevel);                // otro módulo no puede cambiar los ajustes de la Isla
            f.Bus.RaiseEvent("notch", "otro.topico", "{\"glassLevel\":99}"); Assert.Equal(15, c.Notch.GlassLevel);
            int n; lock (f.Bus.Published) n = f.Bus.Published.Count; f.Bus.RaiseEvent("notch", NotchSettings.SetTopic, "{\"glassLevel\":15}"); lock (f.Bus.Published) Assert.Equal(n, f.Bus.Published.Count);   // sin cambio efectivo no se republica
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Turning_the_notch_off_closes_only_that_module_and_turning_it_on_starts_it_again()
    {
        var (f, c, h, file) = Make();
        try
        {
            await f.StartAllAsync(); await f.ReadyAsync("bar"); await f.ReadyAsync("dock"); await f.ReadyAsync("notch");
            var p = f.Launcher.Last("notch"); f.Bus.OnSend = (m, t) => { if (t == "shutdown") f.Launcher.Last(m).Exit(0); return true; };
            c.SetModuleEnabled("notch", false);
            for (int i = 0; i < 100 && f.State("notch") != ModuleState.Disabled; i++) await Task.Delay(20);
            Assert.Equal(ModuleState.Disabled, f.State("notch")); Assert.Equal(ModuleState.Running, f.State("bar")); Assert.Equal(ModuleState.Running, f.State("dock")); Assert.False(p.Killed);
            Assert.Equal(1, f.Launcher.Count("bar")); Assert.Equal(1, f.Launcher.Count("dock"));   // los demás ni se tocaron
            c.SetModuleEnabled("notch", true);
            for (int i = 0; i < 100 && f.Launcher.Count("notch") < 2; i++) await Task.Delay(20);
            Assert.Equal(2, f.Launcher.Count("notch")); await f.ReadyAsync("notch"); Assert.Equal(ModuleState.Running, f.State("notch"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void When_the_notch_disconnects_the_space_it_held_in_the_bar_is_released()
    {
        var (f, _, h, file) = Make();
        try
        {
            f.Bus.RaiseDisconnected("dock"); lock (f.Bus.Published) Assert.DoesNotContain(f.Bus.Published, p => p.Topic == NotchSettings.RectsTopic);   // otro módulo no afecta
            f.Bus.RaiseDisconnected("notch");
            lock (f.Bus.Published) { var last = f.Bus.Published.Last(p => p.Topic == NotchSettings.RectsTopic); Assert.Equal("{\"rects\":[]}", System.Text.Json.JsonSerializer.Serialize(last.Data)); }
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task OpenNotchLocalSettings_emits_a_non_retained_command_to_the_module()
    {
        var (f, _, h, file) = Make(); try { h.OpenNotchLocalSettings(); lock (f.Bus.Emitted) Assert.Equal([NotchSettings.CommandTopic], f.Bus.Emitted); } finally { File.Delete(file); }
        await Task.CompletedTask;
    }
}

public class LiveToggleTests
{
    [Fact]
    public async Task A_not_installed_module_never_starts_and_cannot_be_toggled()
    {
        var f = new Fixture(); f.Sup.Add(new ModuleSpec("bar", "/x/bar", []), true); f.Sup.Add(new ModuleSpec("notch", "/x/notch", []), true, available: false);
        await f.StartAllAsync(); await f.ReadyAsync("bar");
        Assert.Equal(ModuleState.NotInstalled, f.State("notch")); Assert.Equal(0, f.Launcher.Count("notch")); Assert.Equal(ModuleState.Running, f.State("bar"));
        Assert.False(await f.Sup.SetEnabledAsync("notch", true)); Assert.False(await f.Sup.SetEnabledAsync("notch", false)); Assert.Equal(0, f.Launcher.Count("notch"));
    }

    [Fact]
    public async Task A_module_disabled_at_startup_can_be_enabled_live_and_a_crashing_one_is_not_resurrected_by_disabling()
    {
        var f = new Fixture().With(("bar", true), ("notch", false)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        Assert.True(await f.Sup.SetEnabledAsync("notch", true)); await Task.Delay(40); Assert.Equal(1, f.Launcher.Count("notch")); await f.ReadyAsync("notch");
        f.Bus.OnSend = (m, t) => { if (t == "shutdown") f.Launcher.Last(m).Exit(0); return true; };
        Assert.True(await f.Sup.SetEnabledAsync("notch", false)); await f.Clock.AdvanceAsync(TimeSpan.FromMinutes(2)); Assert.Equal(1, f.Launcher.Count("notch")); Assert.Equal(ModuleState.Disabled, f.State("notch"));   // desactivado = no se reinicia
        Assert.False(await f.Sup.SetEnabledAsync("notch", false)); Assert.False(await f.Sup.SetEnabledAsync("nope", true));
    }

    [Fact]
    public async Task Nothing_can_be_toggled_while_the_host_is_shutting_down()
    {
        var f = new Fixture().With(("bar", true), ("notch", false)); await f.StartAllAsync(); await f.ReadyAsync("bar");
        f.Bus.OnSend = (m, t) => { if (t == "shutdown") f.Launcher.Last(m).Exit(0); return true; }; await f.Sup.StopAllAsync();
        Assert.False(await f.Sup.SetEnabledAsync("notch", true)); Assert.Equal(0, f.Launcher.Count("notch"));
    }
}

public class NotchChannelRoundtripTests
{
    [Fact]
    public async Task The_notch_receives_the_settings_on_connect_asks_for_a_change_and_gets_the_new_state_back_over_the_real_channel()
    {
        string pipe = "winmac-cfg-" + Guid.NewGuid().ToString("N"), token = "t", file = Path.Combine(Path.GetTempPath(), "winmac-rt-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await using var server = new WinMac.Ipc.IpcServer(new WinMac.Ipc.ServerOptions(pipe, token)); server.Start(); var bus = new IpcHostBus(server);
            var f = new Fixture(); var cfg = new ConfigService(file, new HostConfig { Notch = new NotchSettings { GlassLevel = 50 } }); var ctl = new HostController(cfg, f.Sup, bus); ctl.Start();

            await using var notch = new WinMac.Ipc.IpcClient(new WinMac.Ipc.ClientOptions(pipe, token, "notch") { MaxBackoff = TimeSpan.FromMilliseconds(200) });
            var seen = new List<JsonElement>(); var cmds = new List<string>();
            notch.Subscribe(NotchSettings.Topic, d => { if (d is { } e) lock (seen) seen.Add(e.Clone()); });
            notch.Subscribe(NotchSettings.CommandTopic, d => { lock (cmds) cmds.Add(d?.GetProperty("cmd").GetString() ?? ""); });
            notch.Start();
            Assert.True(await Until(() => { lock (seen) return seen.Count >= 1; }), "no recibió los ajustes iniciales (retenidos)");
            lock (seen) Assert.Equal(50, seen[0].GetProperty("glassLevel").GetInt32());

            notch.Event(NotchSettings.SetTopic, new { glassLevel = 85, lyricsEnabled = true, sections = new[] { new { id = "timer", enabled = true } }, position = "Bogus" });
            Assert.True(await Until(() => { lock (seen) return seen.Any(e => e.GetProperty("glassLevel").GetInt32() == 85); }), "no recibió el estado nuevo");
            lock (seen) { var last = seen[^1]; Assert.Equal(85, last.GetProperty("glassLevel").GetInt32()); Assert.True(last.GetProperty("lyricsEnabled").GetBoolean()); Assert.Equal("TopCenter", last.GetProperty("position").GetString()); Assert.Equal("timer", last.GetProperty("sections")[0].GetProperty("id").GetString()); }
            Assert.Equal(85, HostConfig.Load(file).Notch!.GlassLevel);   // el host es el dueño: lo guardó él

            ctl.OpenNotchLocalSettings(); Assert.True(await Until(() => { lock (cmds) return cmds.Count == 1; })); Assert.Equal("open-local-settings", cmds[0]);
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }

    private static async Task<bool> Until(Func<bool> c, int ms = 8000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { if (c()) return true; await Task.Delay(20); } return c(); }
}


public class BarCentralConfigTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "winmac-bar-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void A_bar_patch_is_saved_in_winmac_json_and_roundtrips_without_touching_the_notch()
    {
        var f = Tmp();
        try
        {
            var s = new ConfigService(f, new HostConfig()); s.ApplyNotchPatch(J("{\"glassLevel\":30}"));
            Assert.True(s.ApplyBarPatch(J("{\"showSeconds\":true}"))); Assert.False(s.ApplyBarPatch(J("{\"showSeconds\":true}")));
            var l = HostConfig.Load(f); Assert.True(l.Bar!.ShowSeconds); Assert.Equal(30, l.Notch!.GlassLevel);
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }

    [Fact]
    public void The_bar_patch_travels_only_from_the_bar_module_and_is_republished()
    {
        var file = Tmp(); var f = new Fixture().With(("bar", true), ("dock", true), ("notch", true));
        try
        {
            var c = new ConfigService(file, new HostConfig()); var h = new HostController(c, f.Sup, f.Bus); h.Start();
            f.Bus.RaiseEvent("dock", HostController.BarSetTopic, "{\"showSeconds\":true}"); Assert.False(c.Bar.ShowSeconds);
            f.Bus.RaiseEvent("bar", HostController.BarSetTopic, "{\"showSeconds\":true}"); Assert.True(c.Bar.ShowSeconds);
            lock (f.Bus.Published) Assert.Equal(HostController.BarTopic, f.Bus.Published[^1].Topic);
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }
}
