using System.Runtime.InteropServices;
using WinMac.Apps;
using WinMac.Apps.Win;
using WinMac.Bar.Launcher;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Launcher;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Launchpad (docs/22). El índice de apps (<see cref="AppCatalog"/>), las preferencias (<see cref="LauncherPrefs"/>), los iconos y el lanzador se comparten con Spotlight.
/// Se abre con la tecla Windows sola (<see cref="WinKeyHook"/>, si está activado), con <c>WinMac.Bar.App --launchpad</c> o desde la propia barra.
/// El catálogo se lee al arrancar y se relee en segundo plano al abrir si cambió el menú Inicio o pasó un minuto (las apps empaquetadas no tocan esas carpetas).
/// </summary>
public sealed partial class BarHost
{
    public const string LaunchpadEventName = @"Local\WinMac.Bar.Launchpad";
    private static readonly TimeSpan CatalogMaxAge = TimeSpan.FromMinutes(1);

    private readonly AppCatalog _catalog = new();
    private LauncherPrefs _lprefs = new();
    private string _lprefsPath = "";
    private Apps.IconService? _lpIcons;
    private LaunchpadWindow? _lp;
    private WinKeyHook? _winKey;
    private EventWaitHandle? _lpEvent;
    private readonly List<FileSystemWatcher> _startMenuWatchers = [];
    private volatile bool _catalogDirty = true; private bool _catalogReading;

    /// <summary>Animación de entrada de Launchpad en curso: el bucle de la barra da fotogramas seguidos mientras dura.</summary>
    private bool LauncherAnimating => _lp?.Animating == true;

    private void StartLauncher()
    {
        _lprefsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "launcher.json");
        _lprefs = LauncherPrefs.Load(_lprefsPath);
        try { _lpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, LaunchpadEventName); } catch (Exception e) { _log("[launchpad] sin evento de apertura: " + e.Message); }
        foreach (var f in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
        {
            try
            {
                var dir = Environment.GetFolderPath(f); if (!Directory.Exists(dir)) continue;
                var w = new FileSystemWatcher(dir) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                FileSystemEventHandler h = (_, _) => _catalogDirty = true; w.Created += h; w.Deleted += h; w.Changed += h; w.Renamed += (_, _) => _catalogDirty = true;
                w.EnableRaisingEvents = true; _startMenuWatchers.Add(w);
            }
            catch (Exception e) { _log("[launchpad] sin vigilancia del menú Inicio: " + e.GetType().Name); }
        }

        RefreshCatalog();
        ApplyLauncherConfig();
    }

    /// <summary>Instala o quita el gancho de la tecla Windows según el ajuste (en vivo).</summary>
    private void ApplyLauncherConfig()
    {
        if (_cfg.LaunchpadOnWinKey && _winKey is null)
        {
            _winKey = new WinKeyHook(() => Post(() => ToggleLaunchpad("tecla Windows")), _log); _winKey.Start();
        }
        else if (!_cfg.LaunchpadOnWinKey && _winKey is not null) { _winKey.Dispose(); _winKey = null; _log("[launchpad] tecla Windows: gancho quitado (vuelve el menú Inicio)"); }
    }

    private void LauncherTick()
    {
        if (_lpEvent is not null && _lpEvent.WaitOne(0)) ToggleLaunchpad("orden externa");
        _lp?.Pump();
    }

    private void RefreshCatalog()
    {
        if (_catalogReading) return;
        _catalogReading = true; _catalogDirty = false;
        Task.Run(() =>
        {
            IReadOnlyList<InstalledApp> list = []; string? error = null;
            try { list = AppsFolderReader.Read(_log); } catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            Post(() =>
            {
                _catalogReading = false;
                if (error is not null) { _log("[launchpad] no se pudo leer la lista de apps: " + error); return; }
                bool changed = _catalog.Update(list, DateTime.UtcNow);
                if (changed) { _log($"[launchpad] índice de apps: {_catalog.Apps.Count} (de {list.Count} entradas de Windows)"); PushLaunchpadApps(); PrefetchLauncherIcons(); }
            });
        });
    }

    private void PushLaunchpadApps()
    {
        if (_lp is null) return;
        var arranged = _lprefs.Arrange(_catalog.Apps);
        _lp.Controller.SetApps(arranged, _catalog.Apps.Count - arranged.Count, _lprefs.Usage); _lp.Invalidate();
    }

    /// <summary>Abre Launchpad en el monitor del ratón, o lo cierra si ya está abierto (la tecla Windows otra vez lo cierra, como en macOS).</summary>
    public void ToggleLaunchpad(string why)
    {
        try
        {
            if (_lp?.IsOpen == true) { _lp.Hide(); return; }
            if (_spot?.IsOpen == true) _spot.Hide();
            if (_popover is { IsOpen: true }) _popover.Hide();
            _lpIcons ??= CreateLauncherIcons();
            _lp ??= new LaunchpadWindow(OnLaunchpadCommand, _log) { Icons = LauncherIcon };
            if (_catalogDirty || _catalog.UpdatedUtc is null || DateTime.UtcNow - _catalog.UpdatedUtc > CatalogMaxAge) RefreshCatalog();
            PushLaunchpadApps();
            var (mon, scale) = CursorMonitor();
            _lp.Show(mon, scale, AnimationsEnabled());
            _log($"[launchpad] abierto ({why}): {_catalog.Apps.Count} apps");
            LogLauncherIcons("al abrir");
            Task.Delay(3000).ContinueWith(_ => Post(() => { if (_lp?.IsOpen == true) LogLauncherIcons("3 s después"); }));
        }
        catch (Exception e)
        {
            _log("[launchpad] no se pudo abrir: " + e.GetType().Name + ": " + e.Message + (why == "tecla Windows" ? " → menú Inicio" : ""));
            if (why == "tecla Windows") WinKeyHook.OpenStartMenu();   // nunca dejar al usuario sin menú: si Launchpad falla, el menú Inicio de siempre
        }
    }

    private void OnLaunchpadCommand(LpCommand c)
    {
        switch (c.Kind)
        {
            case LpCommandKind.Close: _lp?.Hide(); return;
            case LpCommandKind.Launch when c.App is { } app:
                _lp?.Hide(); LaunchApp(app); return;
            case LpCommandKind.Move when c.App is { } app:
                if (_lprefs.Move(_lprefs.Arrange(_catalog.Apps), app.Id, c.Index)) SaveLauncherPrefs(); break;
            case LpCommandKind.Hide when c.App is { } app: _lprefs.Hide(app.Id); SaveLauncherPrefs(); break;
            case LpCommandKind.UnhideAll: _lprefs.UnhideAll(); SaveLauncherPrefs(); break;
            case LpCommandKind.ResetOrder: _lprefs.ResetOrder(); SaveLauncherPrefs(); break;
        }

        PushLaunchpadApps();
    }

    /// <summary>Lanzador compartido (Launchpad y Spotlight): el Explorador abre la app como lo haría el menú Inicio; se cuenta el uso para ordenar la búsqueda.</summary>
    private void LaunchApp(InstalledApp app)
    {
        _lprefs.RecordLaunch(app.Id); SaveLauncherPrefs();
        Task.Run(() => { var err = AppLauncher.Launch(app); if (err is not null) _log($"[launchpad] no se pudo abrir «{app.Name}»: {err}"); });
    }

    private void SaveLauncherPrefs()
    {
        try { _lprefs.Save(_lprefsPath); } catch (Exception e) { _log("[launchpad] no se pudieron guardar las preferencias: " + e.GetType().Name); }
    }

    /// <summary>Tamaño único de los iconos de Launchpad y Spotlight: una sola carga y una sola caché (en disco) por app; Spotlight los dibuja reducidos.</summary>
    private const int LauncherIconSize = 128;
    private int _iconFailLogs;

    private Apps.IconService CreateLauncherIcons()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "icon-cache-launcher");
        // 4 hilos: cada carga usa su propio hilo STA del Shell (no bloquea la interfaz); con 2 la primera página tardaba varios segundos.
        var svc = new Apps.IconService(new ShellIconProvider(), new Apps.IconCache(1024, dir), workers: 4);
        svc.Loaded += _ => Post(() => { _lp?.Invalidate(); _spot?.Refresh(); });
        svc.Failed += (r, why) => { if (Interlocked.Increment(ref _iconFailLogs) <= 25) _log($"[launchpad] sin icono real para «{r.App.DisplayName}» ({r.App.Aumid ?? r.App.ExePath}): {why}"); };
        return svc;
    }

    /// <summary>Precarga en segundo plano los iconos de TODO el índice en cuanto se lee (y quedan en la caché de disco): al abrir Launchpad ya están, o llegan enseguida.</summary>
    private void PrefetchLauncherIcons()
    {
        _lpIcons ??= CreateLauncherIcons();
        foreach (var a in _lprefs.Arrange(_catalog.Apps)) LauncherIcon(a);   // TryGet encola lo que falte; lo que ya está en memoria o disco no cuesta nada
    }

    private IconBitmap? LauncherIcon(InstalledApp app)
    {
        try { return _lpIcons?.TryGet(new IconRequest(app.Identity, null, LauncherIconSize)); } catch (Exception) { return null; }
    }

    /// <summary>Diagnóstico: de las apps de la página visible, cuántas tienen ya su icono REAL (la inicial de reemplazo no cuenta).</summary>
    private void LogLauncherIcons(string when)
    {
        if (_lp is null || _lpIcons is null) return;
        var page = _lp.Controller.PageItems; int real = page.Count(a => LauncherIcon(a) is not null);
        var st = _lpIcons.Stats;
        _log($"[launchpad] iconos ({when}): {real} reales de {page.Count} en la página; servicio: {st.Loaded} cargados, {st.DiskHits} de disco, {st.Failed + st.Rejected} sin icono, {st.TimedOut} agotados, {st.Pending} pendientes");
    }

    private static (PxRect Monitor, double Scale) CursorMonitor()
    {
        BarNative.GetCursorPos(out var p);
        var hm = BarNative.MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */);
        var mi = new BarNative.MONITORINFO { cbSize = (uint)Marshal.SizeOf<BarNative.MONITORINFO>() };
        BarNative.GetMonitorInfo(hm, ref mi);
        double scale = BarNative.GetDpiForMonitor(hm, 0, out uint dx, out _) == 0 && dx >= 48 ? dx / 96.0 : 1.0;
        var r = mi.rcMonitor; return (new PxRect(r.Left, r.Top, r.Width, r.Height), scale);
    }

    private static bool AnimationsEnabled() { try { return !BarNative.SystemParametersInfoW(0x1042, 0, out int on, 0) || on != 0; } catch (Exception) { return true; } }

    private void StopLauncher()
    {
        _winKey?.Dispose(); _winKey = null;
        foreach (var w in _startMenuWatchers) { try { w.Dispose(); } catch (Exception) { } }
        _startMenuWatchers.Clear();
        _lp?.Dispose(); _lp = null; _lpIcons?.Dispose(); _lpIcons = null; _lpEvent?.Dispose(); _lpEvent = null;
    }
}
