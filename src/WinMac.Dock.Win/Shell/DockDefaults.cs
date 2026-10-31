using System.Runtime.InteropServices;
using WinMac.Apps;
using WinMac.Apps.Win;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Dock.Win.Shell;

/// <summary>Configuración inicial y utilidades de rutas. Solo ejecutables propios de Windows (sin recursos de terceros).</summary>
public static class DockDefaults
{
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, [MarshalAs(UnmanagedType.LPWStr)] out string path);

    public const string QuitEventName = @"Local\WinMac.Dock.Quit";

    public static string? DownloadsPath()
    {
        try { if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0) return p; } catch (Exception) { }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? null : Path.Combine(home, "Downloads");
    }

    public static DockConfig Create()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows), sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var cfg = new DockConfig { DownloadsPath = DownloadsPath() };
        foreach (var p in new[] { Path.Combine(win, "explorer.exe"), Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "calc.exe"), Path.Combine(sys, "mspaint.exe") })
            if (File.Exists(p)) cfg = DockPinning.Pin(cfg, AppIdentity.FromExe(p), DisplayNames.Resolve(AppIdentity.FromExe(p)));
        return cfg;
    }

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac");
    public static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinMac", "dock.json");
    public static string LogPath => Path.Combine(DataDir, "logs", "dock.log");
    /// <summary>Caché de iconos en disco, compartible con otros módulos (Spotlight, Launchpad).</summary>
    public static string IconCachePath => Path.Combine(DataDir, "icons");

    /// <summary>Título legible de una app (descripción del archivo, nombre del Shell para apps empaquetadas, o el nombre del ejecutable).</summary>
    public static string Title(AppIdentity id) => DisplayNames.Resolve(id);
}

/// <summary>Adapta el servicio de iconos (reutilizable) al painter. No bloquea: devuelve el icono si ya está cargado y, si no, encola la carga.</summary>
public sealed class AppIconSource : IIconSource, IDisposable
{
    public IconService Service { get; }
    private int _dirty;
    public const int RequestSize = 256;

    public AppIconSource(string? cacheDir = null, IIconProvider? provider = null)
    {
        Service = new IconService(provider ?? new ShellIconProvider(), new IconCache(512, cacheDir ?? DockDefaults.IconCachePath));
        Service.Loaded += _ => Interlocked.Exchange(ref _dirty, 1);
    }

    /// <summary>True (una sola vez) si llegó algún icono nuevo desde la última consulta: hay que repintar.</summary>
    public bool TakeDirty() => Interlocked.Exchange(ref _dirty, 0) == 1;

    public IconBitmap? Get(DockItemModel item) => item.Identity is null ? null : Service.TryGet(new IconRequest(item.Identity, item.IconOverride, RequestSize));

    public void Dispose() => Service.Dispose();
}

/// <summary>Sin iconos reales (modo demostración y pruebas automatizadas).</summary>
public sealed class NoIconSource : IIconSource { public IconBitmap? Get(DockItemModel item) => null; }
