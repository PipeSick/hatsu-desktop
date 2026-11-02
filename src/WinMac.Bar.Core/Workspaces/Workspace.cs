using System.Text.Json;
using System.Text.Json.Serialization;
using WinMac.Bar.Capture;

namespace WinMac.Bar.Workspaces;

public enum WinShow { Normal, Maximized, Minimized }

/// <summary>One window of a saved workspace: who it is (process + class + title) and where (normal rectangle in screen px, its monitor, its state).</summary>
public sealed record WsWindow(string Process, string Class, string Title, CaptureRect Rect, WinShow Show, int Monitor)
{
    /// <summary>Only if the user set it in the file: start this app when it is not open. Never by default.</summary>
    public string? LaunchPath { get; init; }
}

/// <summary>A monitor as it was when the workspace was saved (work area in px).</summary>
public sealed record WsMonitor(CaptureRect Work, bool Primary);

public sealed record Workspace(string Name, DateTime SavedUtc, IReadOnlyList<WsMonitor> Monitors, IReadOnlyList<WsWindow> Windows);

/// <summary>A window that exists now.</summary>
public sealed record LiveWindow(IntPtr Handle, string Process, string Class, string Title, CaptureRect Rect, WinShow Show);

/// <summary>A move to do: which live window goes where, in which state.</summary>
public sealed record WsMove(IntPtr Handle, CaptureRect Rect, WinShow Show, string Process);

/// <summary>
/// Workspace logic (docs/29), pure and tested: matching saved windows to the open ones, moving them between different monitor layouts, and the
/// side-by-side and grid presets. The Windows adapter only reads windows and applies the moves with public APIs.
/// </summary>
public static class WorkspacePlanner
{
    /// <summary>Each saved window takes the best open window of the same process and class (exact title first), each open window used once.</summary>
    public static IReadOnlyList<(WsWindow Saved, LiveWindow Live)> Match(IReadOnlyList<WsWindow> saved, IReadOnlyList<LiveWindow> live)
    {
        var used = new HashSet<IntPtr>(); var result = new List<(WsWindow, LiveWindow)>();
        foreach (var pass in new[] { 0, 1, 2 })
            foreach (var s in saved)
            {
                if (result.Any(r => ReferenceEquals(r.Item1, s))) continue;
                var cand = live.Where(l => !used.Contains(l.Handle) && string.Equals(l.Process, s.Process, StringComparison.OrdinalIgnoreCase)).Where(l => pass switch
                {
                    0 => l.Class == s.Class && l.Title == s.Title,
                    1 => l.Class == s.Class && Similar(l.Title, s.Title),
                    _ => l.Class == s.Class,
                }).FirstOrDefault();
                if (cand is not null) { used.Add(cand.Handle); result.Add((s, cand)); }
            }

        return result;
    }

    /// <summary>Titles often change («Documento1 - Word» → «Informe - Word»): the same last segment counts as similar.</summary>
    public static bool Similar(string a, string b)
    {
        static string Tail(string t) { int i = Math.Max(t.LastIndexOf(" - ", StringComparison.Ordinal), t.LastIndexOf(" — ", StringComparison.Ordinal)); return i >= 0 ? t[(i + 3)..] : t; }
        return string.Equals(Tail(a), Tail(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where a saved rectangle goes now: same monitor index if it still exists, else the primary; position and size kept relative to that monitor's work
    /// area (so a layout saved on a 4K screen fits a 1080p one), then clamped inside it.
    /// </summary>
    public static CaptureRect Remap(CaptureRect r, WsMonitor from, IReadOnlyList<WsMonitor> now, int index)
    {
        var to = index >= 0 && index < now.Count ? now[index] : now.FirstOrDefault(m => m.Primary) ?? now[0];
        double sx = to.Work.W / (double)Math.Max(1, from.Work.W), sy = to.Work.H / (double)Math.Max(1, from.Work.H);
        if (from.Work == to.Work) { sx = 1; sy = 1; }
        int w = Math.Min(to.Work.W, Math.Max(1, (int)Math.Round(r.W * sx))), h = Math.Min(to.Work.H, Math.Max(1, (int)Math.Round(r.H * sy)));
        int x = to.Work.X + (int)Math.Round((r.X - from.Work.X) * sx), y = to.Work.Y + (int)Math.Round((r.Y - from.Work.Y) * sy);
        x = Math.Clamp(x, to.Work.X, to.Work.Right - w); y = Math.Clamp(y, to.Work.Y, to.Work.Bottom - h);
        return new CaptureRect(x, y, w, h);
    }

    /// <summary>The moves to restore a workspace on the current monitors. Windows not open are reported (and only launched if the file asks for it).</summary>
    public static (IReadOnlyList<WsMove> Moves, IReadOnlyList<WsWindow> Missing) Restore(Workspace ws, IReadOnlyList<LiveWindow> live, IReadOnlyList<WsMonitor> now)
    {
        var pairs = Match(ws.Windows, live);
        var moves = pairs.Select(p =>
        {
            var from = p.Saved.Monitor >= 0 && p.Saved.Monitor < ws.Monitors.Count ? ws.Monitors[p.Saved.Monitor] : ws.Monitors.FirstOrDefault(m => m.Primary) ?? new WsMonitor(p.Saved.Rect, true);
            return new WsMove(p.Live.Handle, Remap(p.Saved.Rect, from, now, p.Saved.Monitor), p.Saved.Show, p.Saved.Process);
        }).ToList();
        var missing = ws.Windows.Where(w => !pairs.Any(p => ReferenceEquals(p.Saved, w))).ToList();
        return (moves, missing);
    }

    /// <summary>Two windows side by side on a work area (the first on the left), with a small gap.</summary>
    public static IReadOnlyList<CaptureRect> SideBySide(CaptureRect work, int gap = 8) =>
        [new(work.X + gap, work.Y + gap, work.W / 2 - gap * 3 / 2, work.H - 2 * gap), new(work.X + work.W / 2 + gap / 2, work.Y + gap, work.W / 2 - gap * 3 / 2, work.H - 2 * gap)];

    /// <summary>N windows in a grid of ceil(sqrt(n)) columns.</summary>
    public static IReadOnlyList<CaptureRect> Grid(CaptureRect work, int n, int gap = 8)
    {
        if (n <= 0) return [];
        int cols = (int)Math.Ceiling(Math.Sqrt(n)), rows = (int)Math.Ceiling(n / (double)cols);
        int cw = (work.W - gap * (cols + 1)) / cols, ch = (work.H - gap * (rows + 1)) / rows;
        return Enumerable.Range(0, n).Select(i => new CaptureRect(work.X + gap + (i % cols) * (cw + gap), work.Y + gap + (i / cols) * (ch + gap), cw, ch)).ToList();
    }

    /// <summary>Monitor index of a rectangle (the one containing its centre, else the primary).</summary>
    public static int MonitorOf(CaptureRect r, IReadOnlyList<WsMonitor> monitors)
    {
        int cx = r.X + r.W / 2, cy = r.Y + r.H / 2;
        for (int i = 0; i < monitors.Count; i++) if (monitors[i].Work.Contains(cx, cy)) return i;
        int p = monitors.ToList().FindIndex(m => m.Primary); return p < 0 ? 0 : p;
    }
}

/// <summary>Workspaces file (<c>%APPDATA%\WinMac\workspaces.json</c>): human-readable, versioned, invalid entries skipped.</summary>
public static class WorkspaceFile
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    private sealed record FileV1(int Version, List<Workspace> Workspaces);

    public static string Serialize(IEnumerable<Workspace> all) => JsonSerializer.Serialize(new FileV1(1, all.ToList()), Opt);

    public static IReadOnlyList<Workspace> Parse(string json)
    {
        try
        {
            var f = JsonSerializer.Deserialize<FileV1>(json, Opt);
            return f?.Workspaces?.Where(w => w is { Name.Length: > 0 and <= 80, Windows: not null, Monitors.Count: > 0 }).Take(50).ToList() ?? [];
        }
        catch (JsonException) { return []; }
    }

    public static string NextName(IEnumerable<Workspace> existing)
    {
        for (int i = 1; ; i++) { var n = "Espacio " + i; if (!existing.Any(w => w.Name == n)) return n; }
    }
}
