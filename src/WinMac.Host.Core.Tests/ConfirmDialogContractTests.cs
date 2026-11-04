using WinMac.Bar;
using WinMac.Dock;
using WinMac.SystemControl;
using Xunit;

/// <summary>It7: el diálogo de confirmación (Dock) y los iconos que pide la Barra (Bar.Core) hablan el mismo idioma; la acción y su confirmación no cambian.</summary>
public class ConfirmDialogContractTests
{
    [Fact]
    public void Every_bar_icon_has_a_dialog_kind_with_the_same_name()
    {
        foreach (var n in Enum.GetNames<ConfirmIcon>()) Assert.True(Enum.TryParse<ConfirmKind>(n, out _), n);   // el anfitrión los convierte por nombre
        Assert.Equal(Enum.GetNames<ConfirmIcon>().Order(), Enum.GetNames<ConfirmKind>().Order());
    }

    [Theory]
    [InlineData(SystemActionKind.Shutdown, ConfirmIcon.Power)] [InlineData(SystemActionKind.Restart, ConfirmIcon.Restart)]
    [InlineData(SystemActionKind.LogOff, ConfirmIcon.SignOut)] [InlineData(SystemActionKind.ForceQuit, ConfirmIcon.ForceQuit)]
    public void Each_destructive_system_action_gets_its_own_icon(SystemActionKind k, ConfirmIcon icon) => Assert.Equal(icon, ConfirmIcons.ForAction(k));

    [Fact]
    public void The_confirmations_of_the_destructive_actions_still_exist_and_are_still_destructive()
    {
        foreach (var k in new[] { SystemActionKind.Shutdown, SystemActionKind.Restart, SystemActionKind.LogOff, SystemActionKind.ForceQuit })
        {
            var i = SystemActions.Get(k); Assert.True(i.Destructive); Assert.False(string.IsNullOrEmpty(i.ConfirmText)); Assert.True(SystemActions.NeedsConfirmation(k, true));
        }
    }
}
