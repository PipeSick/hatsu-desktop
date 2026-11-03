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

public class SpotClipboardTests
{
    private static readonly SpotItem[] Clip = [new(SpotKind.Clipboard, "Lista de la compra", "Texto copiado", "clip:" + new string('a', 32)) { Arguments = "text" }];

    [Fact]
    public void AskingForTheClipboardShowsItFirstOtherwiseAfterTheRest()
    {
        Assert.Equal(SpotKind.Clipboard, SpotlightEngine.Query("portapapeles", [], null, [], [], clipboard: Clip)[0].Kind);
        Assert.Equal(SpotKind.Clipboard, SpotlightEngine.Query("cb lista", [], null, [], [], clipboard: Clip)[0].Kind);
        Assert.Contains(SpotlightEngine.Query("lista", [], null, [], [], clipboard: Clip), i => i.Kind == SpotKind.Clipboard);
    }

    [Fact]
    public void TabMovesAndCtrlDeleteOnlyRemovesClipboardEntries()
    {
        var ctl = new SpotlightController();
        ctl.SetResults([new SpotItem(SpotKind.Calc, "4", "2+2 =", "4"), Clip[0]]);
        Assert.Equal(0, ctl.Selected);
        ctl.Key(0x09, false, false); Assert.Equal(1, ctl.Selected);
        ctl.Key(0x09, false, true); Assert.Equal(0, ctl.Selected);
        Assert.Equal(SpotCommandKind.None, ctl.Key(0x2E, true, false).Command.Kind);   // a calculator result is not deletable
        ctl.Key(0x09, false, false);
        Assert.Equal(SpotCommandKind.Delete, ctl.Key(0x2E, true, false).Command.Kind);
        Assert.Equal(SpotCommandKind.Reveal, ctl.Key(0x0D, true, false).Command.Kind);  // Ctrl+Intro: copy as plain text
    }
}

public class HotkeyAuditTests
{
    [Fact]
    public void DuplicatesWindowsCombosAndRejectedKeysAreReported()
    {
        var r = HotkeyAudit.Check([
            new HotkeyInfo("Ctrl+Mayús+4", "Capturar región", "Barra", true),
            new HotkeyInfo("Ctrl+Mayús+4", "Otra cosa", "Barra", true),
            new HotkeyInfo("Alt+Espacio", "Spotlight", "Barra", true),
            new HotkeyInfo("Ctrl+Mayús+6", "Grabar", "Barra", false),
            new HotkeyInfo("Ctrl+Espacio", "Spotlight", "Barra", true),
        ]);
        Assert.Contains("también lo usa", r[0].Problem); Assert.Contains("Menú de ventana", r[2].Problem);
        Assert.Contains("no se puede saber cuál", r[3].Problem); Assert.Null(r[4].Problem);
    }
}

public class PingReportTests
{
    [Fact]
    public void SummaryComesOnlyFromRealReplies()
    {
        var r = PingReport.Lines("8.8.8.8", [new(true, 12, "Success", "8.8.8.8"), new(true, 20, "Success", "8.8.8.8"), new(false, 0, "TimedOut"), new(true, 16, "Success")], running: false);
        Assert.Contains("Estado: Responde con pérdidas", r);
        Assert.Contains("Paquetes: 4 enviados · 3 recibidos · 25 % perdidos", r);
        Assert.Contains("Latencia: mín. 12 ms · media 16 ms · máx. 20 ms", r);
        Assert.Contains(r, l => l.StartsWith("Sin respuesta: el equipo no contestó"));
    }

    [Fact]
    public void RunningCancelledAndUnknownHostAreExplained()
    {
        Assert.Contains("Estado: Enviando… 1/4", PingReport.Lines("google.com", [new(true, 9, "Success", "142.250.0.1")], running: true));
        Assert.Contains("Destino: google.com (142.250.0.1)", PingReport.Lines("google.com", [new(true, 9, "Success", "142.250.0.1")], running: true));
        Assert.Contains("Destino: 10.255.255.1", PingReport.Lines("10.255.255.1", [new(false, 0, "TimedOut", "0.0.0.0")], running: false));   // un tiempo agotado no tiene dirección (CI: salía «(0.0.0.0)»)
        Assert.Contains("Latencia: mín. <1 ms · media <1 ms · máx. <1 ms", PingReport.Lines("127.0.0.1", [new(true, 0, "Success", "127.0.0.1")], running: false));
        Assert.Contains("Estado: Cancelado", PingReport.Lines("x.com", [], running: false, cancelled: true));
        var unknown = PingReport.Lines("no-existe.invalid", [], running: false, error: "No such host is known.");
        Assert.Contains("No existe ese nombre: el DNS no lo encontró", unknown);
        Assert.Contains("Estado: Sin respuesta", PingReport.Lines("10.255.255.1", [new(false, 0, "TimedOut"), new(false, 0, "TimedOut")], running: false));
    }
}

public class SpotlightStabilityTests
{
    [Fact]
    public void HeightDoesNotJumpWhileTypingAndARefreshKeepsARunningTool()
    {
        var ctl = new SpotlightController();
        foreach (var c in "ping 8.") ctl.Char(c);
        ctl.SetResults([]);                                       // a moment with no results
        double empty = ctl.Height;
        ctl.SetResults([new SpotItem(SpotKind.Tool, "Hacer ping a 8.8", null, "tool:ping:8.8")]);
        Assert.Equal(empty, ctl.Height);                          // WM-021: same height, no resize while typing
        Assert.True(ctl.Height <= SpotlightController.MaxHeight);
        ctl.Update(ctl.Results[0] with { Details = ["Estado: Enviando… 1/4"] });
        ctl.SetResults([new SpotItem(SpotKind.Tool, "Hacer ping a 8.8", null, "tool:ping:8.8")]);   // a background refresh
        Assert.Equal(["Estado: Enviando… 1/4"], ctl.Results[0].Details);
        Assert.Equal(SpotCommandKind.ToolAction, ctl.Key(0x43, true, false).Command.Kind);        // Ctrl+C copies the tool result
    }
}
