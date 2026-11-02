using WinMac.Bar.Capture;
using WinMac.Bar.Workspaces;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Workspace Manager (docs/29): distribuciones de monitores SIMULADAS, no una validación con monitores reales.
public class WorkspaceTests
{
    private static readonly WsMonitor Big = new(new CaptureRect(0, 0, 3840, 2100), true), Small = new(new CaptureRect(0, 0, 1920, 1040), true), Left = new(new CaptureRect(-1920, 0, 1920, 1040), false);

    [Fact]
    public void MatchPrefersSameTitleThenSimilarThenSameClass()
    {
        var saved = new[] { new WsWindow("WINWORD", "OpusApp", "Informe - Word", new(0, 0, 10, 10), WinShow.Normal, 0), new WsWindow("notepad", "Notepad", "a.txt - Bloc de notas", new(0, 0, 10, 10), WinShow.Normal, 0) };
        var live = new[] { new LiveWindow(1, "notepad", "Notepad", "b.txt - Bloc de notas", default, WinShow.Normal), new LiveWindow(2, "WINWORD", "OpusApp", "Otro - Word", default, WinShow.Normal), new LiveWindow(3, "notepad", "Notepad", "a.txt - Bloc de notas", default, WinShow.Normal) };
        var m = WorkspacePlanner.Match(saved, live);
        Assert.Equal(2, m.Count);
        Assert.Equal(3, m.Single(p => p.Saved.Process == "notepad").Live.Handle);   // exact title wins over another notepad
        Assert.Equal(2, m.Single(p => p.Saved.Process == "WINWORD").Live.Handle);   // «- Word» is similar
    }

    [Fact]
    public void RemapScalesFromA4kScreenToA1080pOneAndClampsInside()
    {
        var r = WorkspacePlanner.Remap(new CaptureRect(1920, 0, 1920, 2100), Big, [Small], 0);
        Assert.Equal(new CaptureRect(960, 0, 960, 1040), r);
        var off = WorkspacePlanner.Remap(new CaptureRect(-1900, 10, 800, 600), Left, [Small], 1);   // that monitor is gone: primary, inside
        Assert.True(off.X >= 0 && off.Right <= 1920 && off.Bottom <= 1040);
    }

    [Fact]
    public void RestoreReportsMissingWindowsAndKeepsStates()
    {
        var ws = new Workspace("Trabajo", DateTime.UtcNow, [Small], [new WsWindow("code", "Chrome_WidgetWin_1", "x - Visual Studio Code", new(0, 0, 960, 1040), WinShow.Maximized, 0), new WsWindow("slack", "Chrome_WidgetWin_1", "Slack", new(960, 0, 960, 1040), WinShow.Normal, 0)]);
        var (moves, missing) = WorkspacePlanner.Restore(ws, [new LiveWindow(7, "Code", "Chrome_WidgetWin_1", "y - Visual Studio Code", default, WinShow.Normal)], [Small]);
        Assert.Single(moves); Assert.Equal(WinShow.Maximized, moves[0].Show);
        Assert.Single(missing); Assert.Equal("slack", missing[0].Process);
        Assert.Null(missing[0].LaunchPath);   // nothing is launched unless the file says so
    }

    [Fact]
    public void PresetsTileTheWorkAreaWithoutOverlap()
    {
        var two = WorkspacePlanner.SideBySide(Small.Work);
        Assert.True(two[0].Right <= two[1].X);
        var grid = WorkspacePlanner.Grid(Small.Work, 5);
        Assert.Equal(5, grid.Count);
        for (int i = 0; i < grid.Count; i++) for (int j = i + 1; j < grid.Count; j++) Assert.True(grid[i].Intersect(grid[j]).IsEmpty);
        Assert.All(grid, g => Assert.True(g.X >= 0 && g.Right <= 1920 && g.Bottom <= 1040));
    }

    [Fact]
    public void FileRoundTripsAndSkipsInvalidEntries()
    {
        var ws = new Workspace("Espacio 1", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), [Small, Left], [new WsWindow("notepad", "Notepad", "t", new(1, 2, 3, 4), WinShow.Minimized, 1)]);
        var back = WorkspaceFile.Parse(WorkspaceFile.Serialize([ws, ws with { Name = "" }]));
        Assert.Single(back); Assert.Equal(WinShow.Minimized, back[0].Windows[0].Show); Assert.Equal(2, back[0].Monitors.Count);
        Assert.Empty(WorkspaceFile.Parse("{nope"));
        Assert.Equal("Espacio 2", WorkspaceFile.NextName(back));
    }
}
