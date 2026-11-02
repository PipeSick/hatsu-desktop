using WinMac.Bar.Launcher;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Spotlight Pro (docs/27): nada de lo escrito se ejecuta al escribir; herramientas solo con Intro; scripts con doble Intro.
public class SpotProTests
{
    private static IReadOnlyList<SpotItem> Q(string q, bool rec = false, IReadOnlyList<SpotScript>? scripts = null) =>
        SpotlightEngine.Query(q, [], null, [], [], null, rec, scripts);

    [Fact]
    public void WinMacActionsAppearAndRecordingTogglesWithState()
    {
        Assert.Contains(Q("captura"), i => i.Target == "winmac:capture:region");
        Assert.Contains(Q("grabar"), i => i.Target == "winmac:record");
        Assert.DoesNotContain(Q("grabar", rec: true), i => i.Target == "winmac:record");
        Assert.Contains(Q("detener", rec: true), i => i.Target == "winmac:record:stop");
        Assert.DoesNotContain(Q("detener"), i => i.Target == "winmac:record:stop");
    }

    [Fact]
    public void NetworkToolsAreOfferedButNeverRunAndHostsAreStrict()
    {
        Assert.Contains(Q("ip"), i => i.Target == "tool:ip");
        Assert.Contains(Q("ping example.com"), i => i.Target == "tool:ping:example.com");
        Assert.Contains(Q("dns github.com"), i => i.Target == "tool:dns:github.com");
        Assert.DoesNotContain(Q("ping a.com & calc"), i => i.Kind == SpotKind.Tool && i.Target.StartsWith("tool:ping"));
        Assert.DoesNotContain(Q("ping $(whoami)"), i => i.Kind == SpotKind.Tool && i.Target.StartsWith("tool:ping"));
        Assert.True(SpotPro.IsHost("::1")); Assert.False(SpotPro.IsHost("-oProxyCommand=x"));
    }

    [Fact]
    public void SshIsParsedIntoSeparateSafeArguments()
    {
        var s = SpotPro.ParseSsh("ssh dev@server.local:2222")!.Value;
        Assert.Equal(["-p", "2222", "dev@server.local"], SpotPro.SshArguments(s));
        Assert.Null(SpotPro.ParseSsh("ssh -oProxyCommand=calc host"));
        Assert.Null(SpotPro.ParseSsh("ssh host;rm"));
        Assert.Null(SpotPro.ParseSsh("ssh host:70000"));
    }

    [Fact]
    public void TypedCommandsAreNeverExecutedOnlyTerminalOrCopy()
    {
        var r = Q("> del /s C:\\");
        Assert.All(r, i => Assert.True(i.Target == "terminal:" || i.Target.StartsWith("copy:")));
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void ScriptsNeedASecondEnter()
    {
        var ctl = new SpotlightController();
        ctl.SetResults(Q("copia", scripts: [new SpotScript("Copia de seguridad", "C:\\scripts\\backup.ps1")]).Where(i => i.Kind == SpotKind.Script).ToList());
        Assert.Equal(SpotCommandKind.Redraw, ctl.Key(0x0D, false).Command.Kind);
        Assert.Equal(0, ctl.Pending);
        Assert.Equal(SpotCommandKind.Open, ctl.Key(0x0D, false).Command.Kind);
        ctl.Key(0x0D, false); ctl.Char('x'); Assert.Equal(-1, ctl.Pending);   // typing cancels the pending confirmation
    }

    [Fact]
    public void PercentOfAndPreview()
    {
        Assert.Contains(Q("20% de 150"), i => i.Kind == SpotKind.Calc && i.Title == "30");
        var (lines, keys) = SpotPro.Preview(new SpotItem(SpotKind.File, "setup.exe", "Reciente", "C:\\x\\setup.exe"));
        Assert.Contains("no se ejecuta", keys);
        Assert.Contains("C:\\x\\setup.exe", lines);
    }
}
