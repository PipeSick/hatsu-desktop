using WinMac.Dock;
using Xunit;

namespace WinMac.Dock.Core.Tests;

public class DarkMenuTests
{
    private static readonly DockAction Open = new(DockActionKind.SetIconSize, Number: 36), Big = new(DockActionKind.SetIconSize, Number: 64);
    private static readonly IReadOnlyList<MenuEntry> Tree =
    [
        new("Notepad", null, false, IsHeader: true),
        new("Abrir", Open),
        new("Cerrar", null, Enabled: false),
        MenuEntry.Separator,
        new("Ajustes del Dock", null, true, Children: [new("Tamaño", null, true, Children: [new("Grande (64)", Big, true, Checked: true)])]),
    ];

    [Fact]
    public void Wm017_same_entries_actions_and_submenus_inside_the_panel()
    {
        var m = new DarkMenuModel(Tree);
        Assert.Equal((DarkMenuModel.Outcome.Chosen, Open), m.Activate(1));
        Assert.Equal(DarkMenuModel.Outcome.None, m.Activate(2).Result);   // disabled
        Assert.Equal(DarkMenuModel.Outcome.None, m.Activate(0).Result);   // the app name is a header
        Assert.Equal(DarkMenuModel.Outcome.Navigated, m.Activate(4).Result);
        Assert.Equal("‹ Ajustes del Dock", m.Rows[0].Text); Assert.Equal(DarkMenuModel.Outcome.Navigated, m.Activate(1).Result);
        Assert.Equal((DarkMenuModel.Outcome.Chosen, Big), m.Activate(1));
        m.Activate(0); m.Activate(0); Assert.False(m.InSubmenu);   // back twice
        Assert.InRange(m.Width, DarkMenuModel.MinW, DarkMenuModel.MaxW);
        Assert.Equal(1, m.RowAt(m.RowY(1) + 3));
    }

    [Fact]
    public void Wm017_keyboard_skips_disabled_rows_and_esc_goes_back_then_closes()
    {
        var m = new DarkMenuModel(Tree);
        m.Key(0x28); Assert.Equal(1, m.Focus); m.Key(0x28); Assert.Equal(4, m.Focus);   // «Cerrar» (disabled) and the separator are skipped
        Assert.Equal(DarkMenuModel.Outcome.Navigated, m.Key(0x0D).Result);
        Assert.Equal(DarkMenuModel.Outcome.Navigated, m.Key(0x1B).Result); Assert.False(m.InSubmenu);
        Assert.Equal(DarkMenuModel.Outcome.Closed, m.Key(0x1B).Result);
    }
}
