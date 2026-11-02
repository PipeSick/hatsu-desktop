using System.Diagnostics;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Workspaces;
using WinMac.Bar.Workspaces;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Workspace Manager (docs/29), part of the bar: save the open windows' layout (per monitor), restore it later — also on a different set of monitors —
/// and arrange the windows of the current monitor side by side or in a grid. From Spotlight or <c>WinMac.Bar.App --workspace save|restore|side|grid</c>.
/// Apps are NEVER started unless the user wrote a <c>launchPath</c> for that window in the file.
/// </summary>
public sealed partial class BarHost
{
    public static string WorkspacesFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "workspaces.json");

    private IReadOnlyList<Workspace> LoadWorkspaces()
    {
        try { return File.Exists(WorkspacesFile) ? WorkspaceFile.Parse(File.ReadAllText(WorkspacesFile)) : []; }
        catch (Exception e) { _log("[espacios] archivo ilegible: " + e.GetType().Name); return []; }
    }

    public void SaveWorkspace(string? name)
    {
        try
        {
            var all = LoadWorkspaces().ToList(); var mons = WindowArranger.Monitors(); var live = WindowArranger.Windows();
            name = string.IsNullOrWhiteSpace(name) ? WorkspaceFile.NextName(all) : name.Trim();
            var old = all.FirstOrDefault(w => w.Name == name);
            var wins = live.Select(l => new WsWindow(l.Process, l.Class, l.Title, l.Rect, l.Show, WorkspacePlanner.MonitorOf(l.Rect, mons))
            {
                LaunchPath = old?.Windows.FirstOrDefault(o => string.Equals(o.Process, l.Process, StringComparison.OrdinalIgnoreCase))?.LaunchPath,   // keep what the user configured
            }).ToList();
            all.RemoveAll(w => w.Name == name); all.Insert(0, new Workspace(name, DateTime.UtcNow, mons, wins));
            Directory.CreateDirectory(Path.GetDirectoryName(WorkspacesFile)!); File.WriteAllText(WorkspacesFile, WorkspaceFile.Serialize(all));
            _log($"[espacios] guardado «{name}»: {wins.Count} ventana(s) en {mons.Count} monitor(es)");   // no window titles in the log
        }
        catch (Exception e) { _log("[espacios] no se pudo guardar: " + e.GetType().Name + ": " + e.Message); }
    }

    public void RestoreWorkspace(string? name)
    {
        var all = LoadWorkspaces(); var ws = name is null ? all.FirstOrDefault() : all.FirstOrDefault(w => w.Name == name);
        if (ws is null) { _log("[espacios] no hay ningún espacio guardado" + (name is null ? "" : " con ese nombre")); return; }
        var (moves, missing) = WorkspacePlanner.Restore(ws, WindowArranger.Windows(), WindowArranger.Monitors());
        int ok = 0; var failed = new List<string>();
        foreach (var m in moves) { if (WindowArranger.Apply(m) is { } why) failed.Add(m.Process + ": " + why); else ok++; }
        int launched = 0;
        foreach (var w in missing.Where(w => w.LaunchPath is { } p && Path.IsPathFullyQualified(p) && File.Exists(p)))
            try { using var _ = Process.Start(new ProcessStartInfo(w.LaunchPath!) { UseShellExecute = true }); launched++; } catch (Exception) { }
        _log($"[espacios] restaurado «{ws.Name}»: {ok} colocada(s), {missing.Count} no abierta(s){(launched > 0 ? $", {launched} iniciada(s) por configuración" : "")}{(failed.Count > 0 ? ", no se pudo: " + string.Join("; ", failed) : "")}");
    }

    /// <summary>Side by side (two front-most windows) or grid (all) on the monitor under the cursor.</summary>
    public void ArrangeWindows(bool grid)
    {
        var mons = WindowArranger.Monitors(); BarNativeCursor(out int cx, out int cy);
        var mon = mons.FirstOrDefault(m => m.Work.Contains(cx, cy)) ?? mons.FirstOrDefault(m => m.Primary);
        if (mon is null) return;
        var wins = WindowArranger.Windows().Where(w => w.Show != WinShow.Minimized && WorkspacePlanner.MonitorOf(w.Rect, mons) == mons.ToList().IndexOf(mon)).ToList();
        var rects = grid ? WorkspacePlanner.Grid(mon.Work, Math.Min(wins.Count, 9)) : WorkspacePlanner.SideBySide(mon.Work);
        int ok = 0; var failed = new List<string>();
        foreach (var (w, r) in wins.Zip(rects)) { if (WindowArranger.Apply(new WsMove(w.Handle, r, WinShow.Normal, w.Process)) is { } why) failed.Add(w.Process + ": " + why); else ok++; }
        _log($"[espacios] {(grid ? "cuadrícula" : "lado a lado")}: {ok} ventana(s){(failed.Count > 0 ? ", no se pudo: " + string.Join("; ", failed) : "")}");
    }

    private static void BarNativeCursor(out int x, out int y) { Interop.BarNative.GetCursorPos(out var p); x = p.X; y = p.Y; }

    public IReadOnlyList<string> WorkspaceNames() => LoadWorkspaces().Select(w => w.Name).ToList();
}
