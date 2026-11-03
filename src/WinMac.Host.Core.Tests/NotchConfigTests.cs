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
        var n = new NotchSettings(); var m = n.MoveSection(0, 1); Assert.Equal(NotchSettings.SectionIds[1], m.Sections[0].Id); Assert.Equal(NotchSettings.SectionIds[0], n.Sections[0].Id);   // el original no cambia
        Assert.Same(n, n.MoveSection(0, -1)); Assert.Same(n, n.MoveSection(99, 1)); Assert.Same(n, n.ToggleSection(-1));
        Assert.False(n.ToggleSection(0).Sections[0].Enabled);
    }

    [Fact]
    public void The_wire_format_is_camelCase_and_roundtrips_through_a_patch()
    {
        var n = new NotchSettings { GlassLevel = 41, LyricsInPill = true, Position = "TopRight" }.MoveSection(2, -2);
        string json = JsonSerializer.Serialize(n.ToWire()); Assert.Contains("\"glassLevel\":41", json); Assert.Contains("\"sections\":[{\"id\":\"" + NotchSettings.SectionIds[2] + "\"", json);
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
        var (f, _, h, file) = Make(); try { h.Start(); lock (f.Bus.Published) { Assert.Equal(3, f.Bus.Published.Count); Assert.Equal(NotchSettings.Topic, f.Bus.Published[0].Topic); Assert.Equal(HostController.BarTopic, f.Bus.Published[1].Topic); Assert.Equal(WinMac.Connect.ConnectTopics.Config, f.Bus.Published[2].Topic); } } finally { File.Delete(file); }
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
    public async Task When_the_notch_crashes_or_is_turned_off_the_space_is_released_even_if_the_channel_did_not_report_a_disconnect()
    {
        var (f, c, h, file) = Make();
        try
        {
            await f.StartAllAsync(); await f.ReadyAsync("bar"); await f.ReadyAsync("dock"); await f.ReadyAsync("notch");
            int Empties() { lock (f.Bus.Published) return f.Bus.Published.Count(p => p.Topic == NotchSettings.RectsTopic && System.Text.Json.JsonSerializer.Serialize(p.Data) == "{\"rects\":[]}"); }
            int before = Empties();
            f.Launcher.Last("notch").Exit(1);   // cae sin avisar por el canal
            for (int i = 0; i < 100 && Empties() == before; i++) await Task.Delay(20);
            Assert.True(Empties() > before, "la caída de la Isla no liberó su espacio en las Barras");
            int dockBefore = Empties(); f.Launcher.Last("dock").Exit(1); await Task.Delay(100); Assert.Equal(dockBefore, Empties());   // la caída de otro módulo no toca el espacio de la Isla
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

public class BarMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winmac-mig-" + Guid.NewGuid().ToString("N"));
    public BarMigrationTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
    private string P(string n) => Path.Combine(_dir, n);
    private static string Sha(string f) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));

    [Fact]
    public void The_first_start_copies_bar_json_into_winmac_json_without_touching_the_original()
    {
        File.WriteAllText(P("bar.json"), new WinMac.Bar.BarConfig { ShowSeconds = true, Use24Hour = false, Background = WinMac.Bar.BarBackground.Transparent, CalendarIcsPath = @"C:\cal\mi.ics" }.ToJson());   // tal como lo escribe la propia Barra (PascalCase)
        string before = Sha(P("bar.json")); var log = new List<string>();
        var cfg = BarMigration.ImportOnce(new HostConfig(), P("bar.json"), P("winmac.json"), log.Add);
        Assert.True(cfg.Bar!.ShowSeconds); Assert.False(cfg.Bar.Use24Hour); Assert.Equal(WinMac.Bar.BarBackground.Transparent, cfg.Bar.Background); Assert.Equal(@"C:\cal\mi.ics", cfg.Bar.CalendarIcsPath);
        var onDisk = HostConfig.Load(P("winmac.json")); Assert.True(onDisk.Bar!.ShowSeconds); Assert.Equal(cfg.Bar, onDisk.Bar);   // quedó guardado en el archivo central
        Assert.Equal(before, Sha(P("bar.json"))); Assert.Single(log); Assert.Contains("no se modificó", log[0]);        // el original sigue idéntico
    }

    [Fact]
    public void Once_the_central_file_has_bar_settings_a_later_bar_json_never_overwrites_them()
    {
        File.WriteAllText(P("bar.json"), new WinMac.Bar.BarConfig { ShowSeconds = true }.ToJson());
        var first = BarMigration.ImportOnce(new HostConfig(), P("bar.json"), P("winmac.json"), _ => { });
        var edited = first with { Bar = first.Bar! with { ShowSeconds = false } };   // el usuario lo cambió desde Ajustes
        File.WriteAllText(P("bar.json"), new WinMac.Bar.BarConfig { ShowSeconds = true, ShowDate = false }.ToJson()); File.Delete(P("winmac.json"));
        var again = BarMigration.ImportOnce(edited, P("bar.json"), P("winmac.json"), _ => { });
        Assert.Same(edited, again); Assert.False(again.Bar!.ShowSeconds); Assert.False(File.Exists(P("winmac.json")));   // no reescribe nada
    }

    [Fact]
    public void Without_a_bar_json_nothing_is_imported_and_nothing_is_written()
    {
        var cfg = new HostConfig(); var res = BarMigration.ImportOnce(cfg, P("no-existe.json"), P("winmac.json"), _ => { });
        Assert.Same(cfg, res); Assert.Null(res.Bar); Assert.False(File.Exists(P("winmac.json")));
    }

    [Fact]
    public void A_corrupt_bar_json_falls_back_to_defaults_and_values_out_of_range_are_sanitized()
    {
        File.WriteAllText(P("bar.json"), "{esto no es json");
        var cfg = BarMigration.ImportOnce(new HostConfig(), P("bar.json"), P("winmac.json"), _ => { });
        Assert.Equal(new WinMac.Bar.BarConfig().Sanitize(), cfg.Bar);
        File.WriteAllText(P("bar2.json"), "{\"CenterGap\":99,\"Language\":\"fr\"}");
        var c2 = BarMigration.ImportOnce(new HostConfig(), P("bar2.json"), P("w2.json"), _ => { });
        Assert.Equal(0.6, c2.Bar!.CenterGap); Assert.Equal("es", c2.Bar.Language);
    }

    [Fact]
    public void After_the_migration_the_bar_receives_its_settings_from_the_host_and_its_changes_round_trip_through_the_central_file()
    {
        File.WriteAllText(P("bar.json"), new WinMac.Bar.BarConfig { ShowSeconds = true }.ToJson());
        var cfg = BarMigration.ImportOnce(new HostConfig(), P("bar.json"), P("winmac.json"), _ => { });
        var svc = new ConfigService(P("winmac.json"), cfg);
        svc.ApplyBarPatch(System.Text.Json.JsonDocument.Parse("{\"reserveSpace\":false}").RootElement.Clone());   // lo que envía la Barra cuando el usuario desmarca «Reservar espacio arriba»
        var reloaded = HostConfig.Load(P("winmac.json")); Assert.False(reloaded.Bar!.ReserveSpace); Assert.True(reloaded.Bar.ShowSeconds);
    }
}

public class BarPatchActionTests
{
    private static System.Text.Json.JsonElement J(string json) => System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "winmac-bp-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Invalid_or_foreign_fields_in_a_bar_patch_change_nothing_and_valid_ones_in_the_same_patch_still_apply()
    {
        var f = Tmp();
        try
        {
            var s = new ConfigService(f, new HostConfig());
            Assert.False(s.ApplyBarPatch(J("{\"nopExiste\":1,\"showSeconds\":\"si\",\"theme\":\"Fucsia\"}")));   // todo inválido: ni cambia ni guarda
            Assert.False(File.Exists(f));
            Assert.True(s.ApplyBarPatch(J("{\"showSeconds\":true,\"theme\":\"Fucsia\",\"wifiDetails\":true}")));      // lo válido pasa aunque haya algo inválido al lado
            Assert.True(s.Bar.ShowSeconds); Assert.True(s.Bar.WifiDetails); Assert.Equal(WinMac.Bar.BarThemeMode.System, s.Bar.Theme);
            Assert.False(s.ApplyBarPatch(J("[1,2,3]"))); Assert.False(s.ApplyBarPatch(J("\"hola\"")));
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }

    [Fact]
    public void A_bar_patch_does_not_alter_the_notch_settings_or_the_modules_and_the_other_way_round()
    {
        var f = Tmp();
        try
        {
            var s = new ConfigService(f, new HostConfig()); s.ApplyNotchPatch(J("{\"glassLevel\":33}")); s.SetModuleEnabled("dock", false);
            s.ApplyBarPatch(J("{\"showDate\":false}"));
            var l = HostConfig.Load(f); Assert.Equal(33, l.Notch!.GlassLevel); Assert.False(l.Modules["dock"].Enabled); Assert.False(l.Bar!.ShowDate);
            s.ApplyNotchPatch(J("{\"glassLevel\":44}")); Assert.False(HostConfig.Load(f).Bar!.ShowDate);
        }
        finally { try { File.Delete(f); } catch (IOException) { } }
    }
}

public class CityResolverTests
{
    private sealed class Src(Func<string, IReadOnlyList<WinMac.Bar.Hub.GeoPoint>> search) : WinMac.Bar.Hub.IWeatherSource
    {
        public List<string> Queries { get; } = [];
        public Task<WinMac.Bar.Hub.WeatherSnapshot> FetchAsync(WinMac.Bar.Hub.GeoPoint place, IReadOnlyList<WinMac.Bar.Hub.GeoPoint> others, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WinMac.Bar.Hub.GeoPoint>> SearchAsync(string name, CancellationToken ct) { Queries.Add(name); return Task.FromResult(search(name)); }
    }

    private static readonly WinMac.Bar.Hub.GeoPoint Molina = new("Molina", "Maule, Chile", -35.1167, -71.2833), Santiago = new("Santiago", "Región Metropolitana, Chile", -33.45, -70.67);

    [Fact]
    public async Task A_typed_city_is_resolved_by_name_to_its_first_match_with_region_for_the_status_line()
    {
        var src = new Src(n => n == "Molina" ? [Molina, new("Molina", "Murcia, España", 38, -1.2)] : []);
        var r = await CityResolver.ResolveOneAsync("  Molina ", src, CancellationToken.None); Assert.Equal("Molina", r.Place!.Name); Assert.Equal(-35.1167, r.Place.Lat); Assert.StartsWith("Molina, Maule, Chile", r.Message); Assert.Contains("Murcia", r.Message); Assert.Contains("«Molina, país»", r.Message); Assert.Equal(["Molina"], src.Queries);   // avisa de que hay otras y cómo elegir
    }

    private static readonly WinMac.Bar.Hub.GeoPoint MolinaEs = new("Molina de Segura", "Región de Murcia, España", 38.05, -1.21);

    [Fact]
    public async Task A_country_or_region_hint_picks_that_city_even_when_another_one_ranks_first()
    {
        var src = new Src(n => n == "Molina" ? [MolinaEs, Molina] : []);   // el proveedor pone primero la de España
        var chile = await CityResolver.ResolveOneAsync("Molina, Chile", src, CancellationToken.None); Assert.Equal(-35.1167, chile.Place!.Lat); Assert.Equal(["Molina"], src.Queries);   // busca solo el nombre
        var maule = await CityResolver.ResolveOneAsync("Molina, MAULÉ", src, CancellationToken.None); Assert.Equal(-35.1167, maule.Place!.Lat);   // sin importar mayúsculas ni tildes
        var murcia = await CityResolver.ResolveOneAsync("Molina, murcia", src, CancellationToken.None); Assert.Equal(38.05, murcia.Place!.Lat);
        var none = await CityResolver.ResolveOneAsync("Molina, Japón", src, CancellationToken.None); Assert.Null(none.Place); Assert.Contains("no está en «Japón»", none.Message); Assert.Contains("Maule, Chile", none.Message);
    }

    [Fact]
    public async Task Without_a_hint_the_city_of_the_computers_own_country_is_preferred()
    {
        var src = new Src(_ => [MolinaEs, Molina]);
        Assert.Equal(-35.1167, (await CityResolver.ResolveOneAsync("Molina", src, CancellationToken.None, "Chile")).Place!.Lat);
        Assert.Equal(38.05, (await CityResolver.ResolveOneAsync("Molina", src, CancellationToken.None)).Place!.Lat);   // sin país conocido: el primero del proveedor
        Assert.Equal(38.05, (await CityResolver.ResolveOneAsync("Molina", src, CancellationToken.None, "Japón")).Place!.Lat);   // un país sin coincidencias no deja sin resultado
        var many = await CityResolver.ResolveManyAsync("Molina, Santiago", new Src(n => n == "Molina" ? [MolinaEs, Molina] : [Santiago]), CancellationToken.None, preferCountry: "Chile");
        Assert.Equal(["Molina", "Santiago"], many.Places.Select(p => p.Name)); Assert.Equal(-35.1167, many.Places[0].Lat);   // en la lista de ciudades la coma separa ciudades (no es una pista)
    }

    [Fact]
    public async Task Explicit_coordinates_need_no_network_and_empty_input_does_nothing()
    {
        var src = new Src(_ => throw new InvalidOperationException("no debía buscar")); var r = await CityResolver.ResolveOneAsync("Casa|-35.1|-71.2", src, CancellationToken.None);
        Assert.Equal("Casa", r.Place!.Name); Assert.Empty(src.Queries); Assert.Null((await CityResolver.ResolveOneAsync("   ", src, CancellationToken.None)).Place);
    }

    [Fact]
    public async Task Not_found_and_offline_leave_the_city_unchanged_with_a_clear_message_instead_of_throwing()
    {
        var none = await CityResolver.ResolveOneAsync("Xyzzyx", new Src(_ => []), CancellationToken.None); Assert.Null(none.Place); Assert.Contains("No se encontró «Xyzzyx»", none.Message);
        var off = await CityResolver.ResolveOneAsync("Molina", new Src(_ => throw new HttpRequestException("dns")), CancellationToken.None); Assert.Null(off.Place); Assert.Contains("sin conexión", off.Message);
        var bad = await CityResolver.ResolveOneAsync("Molina", new Src(_ => throw new FormatException("json")), CancellationToken.None); Assert.Null(bad.Place);
    }

    [Fact]
    public async Task Several_cities_skip_the_unknown_ones_drop_duplicates_and_cap_at_five()
    {
        var src = new Src(n => n switch { "Molina" => [Molina], "Santiago" => [Santiago], "Chillán" => [new("Chillán", null, -36.6, -72.1)], _ => [] });
        var (places, problems) = await CityResolver.ResolveManyAsync("Molina, Nada, Santiago, molina|-35.1167|-71.2833, Santiago", src, CancellationToken.None);
        Assert.Equal(["Molina", "Santiago"], places.Select(p => p.Name)); Assert.Single(problems); Assert.Contains("Nada", problems[0]);
        var many = await CityResolver.ResolveManyAsync(string.Join(",", Enumerable.Range(0, 9).Select(i => $"C{i}|{i + 1}|{i + 1}")), new Src(_ => []), CancellationToken.None, max: 5); Assert.Equal(5, many.Places.Count);
        Assert.Empty((await CityResolver.ResolveManyAsync("", src, CancellationToken.None)).Places);
    }
}

public class BarCommandTests
{
    private static (Fixture F, HostController H, List<string> Log, string File) Make(out int[] counters, out List<string?> sections)
    {
        var f = new Fixture().With(("bar", true), ("dock", true), ("notch", true)); var file = Path.Combine(Path.GetTempPath(), "winmac-cmd-" + Guid.NewGuid().ToString("N") + ".json"); var log = new List<string>();
        var h = new HostController(new ConfigService(file, new HostConfig()), f.Sup, f.Bus, log.Add); var c = new int[1]; var s = new List<string?>(); counters = c; sections = s;
        h.QuitRequested = () => c[0]++; h.SettingsRequested = s.Add; return (f, h, log, file);
    }

    [Fact]
    public void Quit_from_the_bar_asks_the_host_for_its_orderly_shutdown_and_nobody_else_can()
    {
        var (f, _, _, file) = Make(out var n, out _);
        try
        {
            f.Bus.RaiseEvent("dock", HostController.CommandTopic, "{\"cmd\":\"quit\"}"); f.Bus.RaiseEvent("notch", HostController.CommandTopic, "{\"cmd\":\"quit\"}"); Assert.Equal(0, n[0]);   // otro módulo no puede cerrar WinMac
            f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"quit\"}"); Assert.Equal(1, n[0]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Settings_requests_carry_only_known_sections_and_unknown_commands_or_garbage_do_nothing()
    {
        var (f, _, log, file) = Make(out var n, out var sections);
        try
        {
            f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"settings\",\"section\":\"network\"}"); f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"settings\",\"section\":\"weather\"}");
            f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"settings\",\"section\":\"../../etc\"}"); f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"settings\"}");
            Assert.Equal(["network", "weather", null, null], sections);   // una sección desconocida se ignora, la ventana igual se abre
            int before = sections.Count; f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"format-disk\"}"); f.Bus.RaiseEvent("bar", HostController.CommandTopic, "[1,2]"); f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":5}");
            Assert.Equal(before, sections.Count); Assert.Equal(0, n[0]); Assert.DoesNotContain(log, l => l.Contains("falló"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Without_a_handler_commands_are_harmless()
    {
        var f = new Fixture().With(("bar", true), ("dock", true), ("notch", true)); var file = Path.Combine(Path.GetTempPath(), "winmac-cmd-" + Guid.NewGuid().ToString("N") + ".json");
        try { _ = new HostController(new ConfigService(file, new HostConfig()), f.Sup, f.Bus); f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"quit\"}"); f.Bus.RaiseEvent("bar", HostController.CommandTopic, "{\"cmd\":\"settings\"}"); }
        finally { File.Delete(file); }
    }
}

public class NotchPillSettingsTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Pill_options_default_on_travel_on_the_wire_and_are_clamped()
    {
        var n = new NotchSettings(); Assert.True(n.PillVolume && n.PillBrightness && n.PillNotifications && n.PillAnimations); Assert.Equal(2, n.PillHudSeconds); Assert.Equal(5, n.PillNotifySeconds);
        var p = n.ApplyPatch(J("{\"pillVolume\":false,\"pillHudSeconds\":60,\"pillNotifySeconds\":1,\"pillAnimations\":\"no\"}"));
        Assert.False(p.PillVolume); Assert.Equal(10, p.PillHudSeconds); Assert.Equal(2, p.PillNotifySeconds); Assert.True(p.PillAnimations);
        var wire = JsonDocument.Parse(JsonSerializer.Serialize(p.ToWire())).RootElement;
        Assert.False(wire.GetProperty("pillVolume").GetBoolean()); Assert.Equal(10, wire.GetProperty("pillHudSeconds").GetInt32()); Assert.True(wire.GetProperty("pillNotifications").GetBoolean());
        Assert.False(WinMac.Host.ConfigService.SameNotch(n, p));   // a change here is published to the notch
    }

    [Fact]
    public void Settings_page_has_the_closed_notch_group_and_its_edits_are_saved()
    {
        string f = Path.Combine(Path.GetTempPath(), "winmac-pill-" + Guid.NewGuid().ToString("N") + ".json"); var cfg = new ConfigService(f, new HostConfig());
        try
        {
            var a = new SettingsActions(cfg, () => { }, _ => { });
            var group = SettingsBuilder.Build(new SettingsInput(cfg.Current, new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "notch").Groups.Single(g => g.Header == "Notch cerrado");
            Assert.Equal(["notch.pillvol", "notch.pillbright", "notch.pillnotif", "notch.hudsec", "notch.notifsec", "notch.pillanim"], group.Rows.Select(r => r.Id));
            Assert.True(a.Apply(new SettingsEdit("notch.pillnotif", "toggle", On: false))); Assert.False(cfg.Current.Notch!.PillNotifications);
            Assert.True(a.Apply(new SettingsEdit("notch.hudsec", "text", Text: " 4 "))); Assert.Equal(4, cfg.Current.Notch!.PillHudSeconds);
            Assert.True(a.Apply(new SettingsEdit("notch.notifsec", "text", Text: "abc"))); Assert.Equal(5, cfg.Current.Notch!.PillNotifySeconds);
            Assert.True(a.Apply(new SettingsEdit("notch.pillanim", "toggle", On: false))); Assert.False(cfg.Current.Notch!.PillAnimations);
        }
        finally { File.Delete(f); }
    }
}
