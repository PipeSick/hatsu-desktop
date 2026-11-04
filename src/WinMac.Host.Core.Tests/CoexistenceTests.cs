using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

public class CoexistenceTests
{
    private const string Msi = @"C:\Users\u\AppData\Local\Programs\Hatsu Desktop\HatsuDesktop.exe", Portable = @"D:\Portable\HatsuDesktop\HatsuDesktop.exe", Old = @"C:\Old\WinMac\WinMac.exe";

    [Fact] public void No_other_process_means_nothing_to_decide() => Assert.Equal(CoexistenceKind.NoOther, HostCoexistence.Classify(Msi, []));
    [Fact] public void The_same_install_only_asks_to_reopen_modules_and_never_prompts() { Assert.Equal(CoexistenceKind.SameInstall, HostCoexistence.Classify(Msi, [Msi.ToUpperInvariant()])); Assert.False(HostCoexistence.ShouldAsk(CoexistenceKind.SameInstall, false, false)); }
    [Theory] [InlineData(@"D:\Portable\HatsuDesktop\HatsuDesktop.exe")] [InlineData(@"C:\Old\WinMac\WinMac.exe")]
    public void A_different_install_is_detected_whether_portable_or_the_old_WinMac(string other) => Assert.Equal(CoexistenceKind.OtherInstall, HostCoexistence.Classify(Msi, [other]));
    [Fact] public void Unreadable_process_paths_are_ignored_not_guessed() => Assert.Equal(CoexistenceKind.NoOther, HostCoexistence.Classify(Msi, [null, "  "]));
    [Fact] public void The_user_is_asked_only_when_interactive_never_for_autostart_or_tests()
    {
        Assert.True(HostCoexistence.ShouldAsk(CoexistenceKind.OtherInstall, background: false, noTray: false));
        Assert.False(HostCoexistence.ShouldAsk(CoexistenceKind.OtherInstall, background: true, noTray: false));   // inicio automático: nunca secuestra otra copia
        Assert.False(HostCoexistence.ShouldAsk(CoexistenceKind.OtherInstall, background: false, noTray: true));
    }
    [Fact] public void The_other_path_is_offered_for_the_message() => Assert.Equal(Portable, HostCoexistence.OtherPath(Msi, [Msi, Portable]));

    [Fact] public void Uninstall_removes_only_an_autostart_that_belongs_to_the_removed_install()
    {
        Assert.True(HostCoexistence.AutostartBelongsTo($"\"{Msi}\" --background", Msi));
        Assert.False(HostCoexistence.AutostartBelongsTo($"\"{Portable}\" --background", Msi));   // otra copia portable: se respeta
        Assert.False(HostCoexistence.AutostartBelongsTo($"\"{Old}\" --background", Msi));
        Assert.False(HostCoexistence.AutostartBelongsTo(null, Msi));
    }
}
