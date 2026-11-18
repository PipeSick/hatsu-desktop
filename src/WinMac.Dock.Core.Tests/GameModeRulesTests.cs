using WinMac.Dock;
using Xunit;

namespace WinMac.Dock.Core.Tests;

public class GameModeRulesTests
{
    [Fact] public void Names_are_normalized_to_lowercase_exe_without_paths()
    {
        Assert.Equal("eldenring.exe", GameModeRules.Normalize(@"C:\Juegos\EldenRing.EXE")); Assert.Equal("eldenring.exe", GameModeRules.Normalize("EldenRing")); Assert.Equal("a.exe", GameModeRules.Normalize("\"a.exe\""));
        Assert.Null(GameModeRules.Normalize("  ")); Assert.Null(GameModeRules.Normalize(null)); Assert.Null(GameModeRules.Normalize("a|b"));
    }

    [Fact] public void The_list_parses_with_any_separator_drops_repeats_and_round_trips()
    {
        var l = GameModeRules.ParseList("EldenRing.exe; valorant , eldenring.exe\nhades2"); Assert.Equal(["eldenring.exe", "valorant.exe", "hades2.exe"], l);
        Assert.Equal(l, GameModeRules.ParseList(GameModeRules.FormatList(l))); Assert.Empty(GameModeRules.ParseList("")); Assert.Equal(GameModeRules.MaxApps, GameModeRules.ParseList(string.Join(",", Enumerable.Range(0, 200).Select(i => "g" + i))).Count);
    }

    [Fact] public void A_game_is_a_listed_exe_or_anything_installed_by_Steam()
    {
        Assert.True(GameModeRules.IsGame(@"D:\Juegos\EldenRing.exe", ["eldenring.exe"])); Assert.False(GameModeRules.IsGame(@"D:\Juegos\Otro.exe", ["eldenring.exe"]));
        Assert.True(GameModeRules.IsGame(@"C:\Program Files (x86)\Steam\steamapps\common\Hades II\Hades2.exe", [])); Assert.False(GameModeRules.IsGame(@"C:\Program Files (x86)\Steam\steam.exe", []));
        Assert.False(GameModeRules.IsGame(@"C:\Program Files (x86)\Steam\steamapps\common\X\x.exe", [], steam: false)); Assert.False(GameModeRules.IsGame(null, ["a.exe"]));
    }

    [Fact] public void An_unknown_foreground_keeps_the_state_so_hovering_the_dock_does_not_turn_it_off()
    {
        Assert.True(GameModeRules.Next(null, true)); Assert.False(GameModeRules.Next(null, false)); Assert.True(GameModeRules.Next(true, false)); Assert.False(GameModeRules.Next(false, true));
    }

    [Fact] public void The_setting_defaults_to_on_and_survives_a_json_round_trip()
    {
        var c = new DockConfig(); Assert.True(c.GameMode); Assert.Empty(c.GameApps);
        var back = DockConfig.FromJson((c with { GameMode = false, GameApps = ["a.exe"] }).ToJson()); Assert.False(back.GameMode); Assert.Equal(["a.exe"], back.GameApps);
        Assert.True(DockConfig.FromJson("{\"Version\":4}").GameMode);   // un dock.json anterior no trae el campo: queda activado
    }
}
