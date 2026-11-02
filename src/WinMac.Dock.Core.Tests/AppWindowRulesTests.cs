using WinMac.Dock;
using Xunit;

// Qué ventanas cuentan como apps en el Dock. El caso real: el usuario vio en el Dock el «Administrador de ventanas del escritorio» (dwm.exe)
// con una «DWM Notification Window» «minimizada»: una ventana OCULTA que el sistema marca como minimizada.
public class AppWindowRulesTests
{
    private static WindowFacts W(bool visible = true, bool cloaked = false, bool minimized = false, long ex = 0, bool owner = false, string cls = "Chrome_WidgetWin_1", bool title = true) =>
        new(visible, cloaked, minimized, ex, owner, cls, title);

    [Fact]
    public void A_hidden_window_marked_minimized_is_not_an_app_like_the_dwm_notification_window()
    {
        Assert.False(AppWindowRules.IsAppWindow(W(visible: false, minimized: true, cls: "DWM Notification Window")));
        Assert.False(AppWindowRules.IsAppWindow(W(visible: false)));
    }

    [Fact]
    public void Real_windows_count_also_when_minimized_and_a_minimized_cloaked_one_is_kept()
    {
        Assert.True(AppWindowRules.IsAppWindow(W()));
        Assert.True(AppWindowRules.IsAppWindow(W(minimized: true)));                 // una ventana minimizada de verdad sigue siendo visible
        Assert.True(AppWindowRules.IsAppWindow(W(minimized: true, cloaked: true)));
        Assert.False(AppWindowRules.IsAppWindow(W(cloaked: true)));                  // otro escritorio virtual / app suspendida
    }

    [Fact]
    public void Tool_owned_untitled_and_shell_windows_are_left_out_unless_marked_as_app_windows()
    {
        Assert.False(AppWindowRules.IsAppWindow(W(ex: AppWindowRules.WS_EX_TOOLWINDOW)));
        Assert.True(AppWindowRules.IsAppWindow(W(ex: AppWindowRules.WS_EX_TOOLWINDOW | AppWindowRules.WS_EX_APPWINDOW)));
        Assert.False(AppWindowRules.IsAppWindow(W(owner: true)));
        Assert.True(AppWindowRules.IsAppWindow(W(owner: true, ex: AppWindowRules.WS_EX_APPWINDOW)));
        Assert.False(AppWindowRules.IsAppWindow(W(title: false)));
        foreach (var shell in new[] { "Progman", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "WorkerW", "Windows.UI.Core.CoreWindow" })
            Assert.False(AppWindowRules.IsAppWindow(W(cls: shell)));
    }
}
