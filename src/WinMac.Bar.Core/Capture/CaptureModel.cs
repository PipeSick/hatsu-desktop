namespace WinMac.Bar.Capture;

/// <summary>Capture modes (docs/26). <see cref="Monitor"/> = the monitor clicked; <see cref="AllMonitors"/> = the whole virtual screen.</summary>
public enum CaptureMode { Region, Window, Monitor, AllMonitors }

/// <summary>A rectangle in PHYSICAL pixels of the virtual screen (can have negative coordinates: monitors left of or above the primary).</summary>
public readonly record struct CaptureRect(int X, int Y, int W, int H)
{
    public int Right => X + W; public int Bottom => Y + H;
    public bool IsEmpty => W <= 0 || H <= 0;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>The rectangle between two drag points, in any direction (dragging up/left too).</summary>
    public static CaptureRect FromPoints(int x1, int y1, int x2, int y2) => new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public CaptureRect Intersect(CaptureRect o)
    {
        int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > l && b > t ? new(l, t, r - l, b - t) : new(l, t, 0, 0);
    }

    public static CaptureRect Union(IEnumerable<CaptureRect> rects)
    {
        var list = rects.Where(r => !r.IsEmpty).ToList();
        if (list.Count == 0) return default;
        int l = list.Min(r => r.X), t = list.Min(r => r.Y), r2 = list.Max(r => r.Right), b = list.Max(r => r.Bottom);
        return new(l, t, r2 - l, b - t);
    }
}

/// <summary>A monitor of the virtual screen: its bounds in physical px and its own scale (DPI can differ per monitor).</summary>
/// <param name="Work">The monitor's work area (without the taskbar and the reserved Dock/bar bands); empty = the whole monitor.</param>
public sealed record CaptureMonitor(CaptureRect Bounds, double Scale, bool Primary, CaptureRect Work = default)
{
    public CaptureRect WorkArea => Work.W > 0 && Work.H > 0 ? Work : Bounds;
}

/// <summary>A top-level window that can be picked in window mode (visible bounds without the invisible resize borders), front to back.</summary>
public sealed record CaptureWindow(IntPtr Handle, string Title, CaptureRect Bounds);

/// <summary>Geometry of a capture session, pure (tested on Linux with simulated layouts: monitors left/right/above/below, negative coordinates, mixed DPI).</summary>
public static class CaptureGeometry
{
    /// <summary>
    /// A small top-down thumbnail (nearest pixel) of a BOTTOM-UP BGRA frame, for the recording's still in the Island. Writes into <paramref name="into"/>
    /// when it is big enough, so a recording allocates nothing per frame for it.
    /// </summary>
    public static (byte[] Px, int W, int H) Thumbnail(byte[] bottomUp, int w, int h, int maxW, byte[]? into = null)
    {
        int tw = Math.Max(1, Math.Min(maxW, w)), th = Math.Max(1, (int)Math.Round(h * (tw / (double)w)));
        var o = into is { } b && b.Length >= tw * th * 4 ? b : new byte[tw * th * 4];
        for (int y = 0; y < th; y++)
        {
            int sy = h - 1 - Math.Min(h - 1, y * h / th);   // bottom-up source row
            for (int x = 0; x < tw; x++)
            {
                int sx = Math.Min(w - 1, x * w / tw), si = (sy * w + sx) * 4, di = (y * tw + x) * 4;
                o[di] = bottomUp[si]; o[di + 1] = bottomUp[si + 1]; o[di + 2] = bottomUp[si + 2]; o[di + 3] = 255;
            }
        }

        return (o, tw, th);
    }

    /// <summary>Recording area: H.264 needs even dimensions, so it is trimmed by at most one pixel per axis.</summary>
    public static CaptureRect EvenRect(CaptureRect r) => new(r.X, r.Y, Math.Max(2, r.W & ~1), Math.Max(2, r.H & ~1));

    /// <summary>«Grabación 2026-11-02 a las 17.20.05.mp4».</summary>
    public static string RecordingName(DateTime local) => "Grabación " + local.ToString("yyyy-MM-dd 'a las' HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture) + ".mp4";

    /// <summary>Smallest region that counts as a drag; anything smaller is a click (in region mode a click captures nothing).</summary>
    public const int MinDrag = 4;

    public static CaptureRect VirtualScreen(IReadOnlyList<CaptureMonitor> monitors) => CaptureRect.Union(monitors.Select(m => m.Bounds));

    public static CaptureMonitor? MonitorAt(IReadOnlyList<CaptureMonitor> monitors, int x, int y) =>
        monitors.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();

    /// <summary>The front-most window under the point (the list is in z-order, front first), clipped to the virtual screen.</summary>
    public static CaptureWindow? WindowAt(IReadOnlyList<CaptureWindow> windows, int x, int y, CaptureRect screen) =>
        windows.FirstOrDefault(w => w.Bounds.Contains(x, y) && !w.Bounds.Intersect(screen).IsEmpty);

    /// <summary>What a mode captures for a pointer position/drag. Empty when nothing would be captured.</summary>
    public static CaptureRect Target(CaptureMode mode, IReadOnlyList<CaptureMonitor> monitors, IReadOnlyList<CaptureWindow> windows, int x, int y, CaptureRect? drag)
    {
        var screen = VirtualScreen(monitors);
        return mode switch
        {
            CaptureMode.Region => drag is { } d && d.W >= MinDrag && d.H >= MinDrag ? d.Intersect(screen) : default,
            CaptureMode.Window => WindowAt(windows, x, y, screen) is { } w ? OnMonitors(monitors, w.Bounds) : default,
            CaptureMode.Monitor => MonitorAt(monitors, x, y)?.Bounds ?? default,
            _ => screen,
        };
    }

    /// <summary>A rectangle clipped to the monitors it touches (not to the whole bounding box: the corners between monitors of different sizes are no screen at all).</summary>
    public static CaptureRect OnMonitors(IReadOnlyList<CaptureMonitor> monitors, CaptureRect r)
    {
        var touched = monitors.Where(m => !m.Bounds.Intersect(r).IsEmpty).Select(m => m.Bounds).ToList();
        return touched.Count == 0 ? default : r.Intersect(CaptureRect.Union(touched));
    }

    /// <summary>«1280 × 720» shown next to the selection, in physical pixels (what the file will have).</summary>
    public static string Dimensions(CaptureRect r) => $"{r.W} × {r.H}";

    /// <summary>Copies a rectangle out of the frozen virtual-screen frame (BGRA, top-down). Coordinates are virtual-screen px.</summary>
    public static byte[] Crop(byte[] frame, CaptureRect frameRect, CaptureRect r)
    {
        r = r.Intersect(frameRect);
        var outPx = new byte[Math.Max(0, r.W) * Math.Max(0, r.H) * 4];
        for (int row = 0; row < r.H; row++)
            Buffer.BlockCopy(frame, ((r.Y - frameRect.Y + row) * frameRect.W + (r.X - frameRect.X)) * 4, outPx, row * r.W * 4, r.W * 4);
        return outPx;
    }

    /// <summary>«Captura 2026-11-02 a las 16.24.31.png» (no characters Windows forbids), with « (2)» if the name exists.</summary>
    public static string FileName(DateTime local, string ext, Func<string, bool> exists)
    {
        string baseName = $"Captura {local:yyyy-MM-dd} a las {local:HH.mm.ss}";
        string name = baseName + ext;
        for (int i = 2; exists(name) && i < 1000; i++) name = $"{baseName} ({i}){ext}";
        return name;
    }
}
