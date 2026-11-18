using WinMac.Dock;
using Xunit;

namespace WinMac.Dock.Core.Tests;

public class FullscreenRulesTests
{
    private static bool S(int state, bool fg = true, bool min = false, bool shell = false, bool covers = true, bool here = true) => FullscreenRules.ShouldSuppress(state, fg, min, shell, covers, here);

    [Fact] public void A_game_covering_this_monitor_hides_the_dock()
    {
        Assert.True(S(FullscreenRules.D3DFullScreen)); Assert.True(S(FullscreenRules.Busy)); Assert.True(S(FullscreenRules.App));
    }

    [Fact] public void A_game_on_another_monitor_does_not_hide_this_one()
    {
        Assert.False(S(FullscreenRules.D3DFullScreen, here: false)); Assert.False(S(FullscreenRules.Busy, here: false));
    }

    [Fact] public void A_stuck_fullscreen_state_with_Steam_minimized_does_not_hide_anything()
    {
        // Windows deja el estado 3 pegado y la ventana en primer plano es la minimizada (o el escritorio): no hay nada a pantalla completa
        Assert.False(S(FullscreenRules.D3DFullScreen, min: true)); Assert.False(S(FullscreenRules.D3DFullScreen, shell: true)); Assert.False(S(FullscreenRules.D3DFullScreen, fg: false));
        Assert.False(S(FullscreenRules.D3DFullScreen, covers: false)); Assert.False(S(FullscreenRules.Busy, covers: false));
    }

    [Fact] public void Presentation_mode_is_a_deliberate_global_choice_and_other_states_never_hide()
    {
        Assert.True(S(FullscreenRules.Presentation, fg: false, here: false)); Assert.False(S(0)); Assert.False(S(1)); Assert.False(S(5)); Assert.False(S(6));
    }
}
