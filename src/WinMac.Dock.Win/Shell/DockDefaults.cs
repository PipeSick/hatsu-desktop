using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMac.Dock.Win.Shell;

/// <summary>Configuración inicial y utilidades de rutas. Solo ejecutables propios de Windows (sin recursos de terceros).</summary>
public static class DockDefaults
{
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    public static string? DownloadsPath()
    {
        try { if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0) return p; } catch (Exception) { }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Downloads");
    }

    public static DockConfig Create()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows), sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var pinned = new List<PinnedApp>();
        foreach (var p in new[] { Path.Combine(win, "explorer.exe"), Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "calc.exe"), Path.Combine(sys, "mspaint.exe") })
            if (File.Exists(p)) pinned.Add(new PinnedApp(p));
        return new DockConfig { Pinned = pinned, DownloadsPath = DownloadsPath() };
    }

    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "dock.json");
    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "dock.log");

    /// <summary>Título legible: descripción del archivo (FileVersionInfo) o nombre del ejecutable.</summary>
    public static string Title(string path)
    {
        try { var d = FileVersionInfo.GetVersionInfo(Environment.ExpandEnvironmentVariables(path)).FileDescription; if (!string.IsNullOrWhiteSpace(d)) return d.Trim(); } catch (Exception) { }
        return DockModelBuilder.DefaultTitle(path);
    }
}

public sealed class ShellIconSource : Rendering.IIconSource
{
    private readonly Dictionary<string, IconPixels?> _cache = new();
    public IconPixels? Get(DockItemModel item)
    {
        if (item.Kind != DockItemKind.App || item.Path is null) return null;
        if (!_cache.TryGetValue(item.Path, out var px)) _cache[item.Path] = px = IconProvider.Load(item.Path);
        return px;
    }
    public int Loaded => _cache.Values.Count(v => v is not null);
    public int Requested => _cache.Count;
}
