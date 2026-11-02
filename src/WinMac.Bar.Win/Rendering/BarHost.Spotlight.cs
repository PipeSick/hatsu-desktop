using System.Diagnostics;
using System.Runtime.InteropServices;
using WinMac.Apps;
using WinMac.Bar.Launcher;
using WinMac.Bar.Win.Launcher;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Spotlight (docs/22): mismo índice de apps, mismo buscador, mismo lanzador, mismos iconos y mismas preferencias que Launchpad. Se abre con el atajo configurado
/// (<see cref="GlobalHotkey"/>) o con <c>WinMac.Bar.App --spotlight</c>. Los archivos (recientes + Windows Search) se buscan en segundo plano y se añaden al llegar.
/// Nada se ejecuta solo: abrir es siempre una acción del usuario (Intro o clic) y un ejecutable se MUESTRA en el Explorador en vez de ejecutarse.
/// </summary>
public sealed partial class BarHost
{
    public const string SpotlightEventName = @"Local\WinMac.Bar.Spotlight";

    private SpotlightWindow? _spot;
    private GlobalHotkey? _spotKey; private SpotlightHotkey? _spotKeyKind;
    private EventWaitHandle? _spotEvent;
    private IReadOnlyList<(string Name, string LinkPath, DateTime Modified)> _recent = [];
    private IReadOnlyList<(string Name, string Path)> _folders = [];
    private int _spotGen; private bool _searchBroken;

    private void StartSpotlight()
    {
        try { _spotEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SpotlightEventName); } catch (Exception e) { _log("[spotlight] sin evento de apertura: " + e.Message); }
        ApplySpotlightConfig();
    }

    private void ApplySpotlightConfig()
    {
        if (_spotKeyKind == _cfg.SpotlightHotkey) return;
        _spotKey?.Dispose(); _spotKey = null; _spotKeyKind = _cfg.SpotlightHotkey;
        if (_cfg.SpotlightHotkey == SpotlightHotkey.Off) { _log("[spotlight] atajo desactivado"); return; }
        var (mods, name) = _cfg.SpotlightHotkey == SpotlightHotkey.CtrlSpace ? (GlobalHotkey.ModCtrl, "Ctrl+Espacio") : (GlobalHotkey.ModAlt, "Alt+Espacio");
        _spotKey = new GlobalHotkey(mods, 0x20, () => Post(() => ToggleSpotlight("atajo")), _log, name);
    }

    private void SpotlightTick()
    {
        if (_spotEvent is not null && _spotEvent.WaitOne(0)) ToggleSpotlight("orden externa");
        _spot?.Pump();
    }

    public void ToggleSpotlight(string why)
    {
        try
        {
            if (_spot?.IsOpen == true) { _spot.Hide(); return; }
            if (_lp?.IsOpen == true) _lp.Hide();
            if (_popover is { IsOpen: true }) _popover.Hide();
            _lpIcons ??= CreateLauncherIcons();
            _spot ??= new SpotlightWindow(OnSpotlightCommand, _log) { Icons = SpotIcon };
            if (_catalogDirty || _catalog.UpdatedUtc is null || DateTime.UtcNow - _catalog.UpdatedUtc > CatalogMaxAge) RefreshCatalog();
            _folders = FileSearch.Folders(); LoadScripts();
            if (_cfg.SpotlightFiles) Task.Run(() => { var r = FileSearch.Recent(); Post(() => _recent = r); }); else _recent = [];
            var (mon, scale) = CursorMonitor();
            _spot.Show(mon, scale);
            _log($"[spotlight] abierto ({why})");
        }
        catch (Exception e) { _log("[spotlight] no se pudo abrir: " + e.GetType().Name + ": " + e.Message); }
    }

    private void OnSpotlightCommand(SpotCommand c, bool textChanged)
    {
        if (_spot is null) return;
        if (textChanged) Search(_spot.Controller.Query);
        switch (c.Kind)
        {
            case SpotCommandKind.Close: _spot.Hide(); return;
            case SpotCommandKind.Open when c.Item is { Kind: SpotKind.Action or SpotKind.Tool or SpotKind.Command or SpotKind.Script } pro:
                if (!OpenPro(pro)) _spot.Hide(); else _spot.Refresh();
                return;
            case SpotCommandKind.Open when c.Item is { } it: _spot.Hide(); Open(it, reveal: false); return;
            case SpotCommandKind.Reveal when c.Item is { } it: _spot.Hide(); Open(it, reveal: true); return;
        }

        _spot.Refresh();
    }

    /// <summary>Lo local (apps, calculadora, Configuración, carpetas, recientes) al instante; Windows Search después, si la búsqueda no cambió entretanto.</summary>
    private void Search(string q)
    {
        if (_spot is null) return;
        int gen = ++_spotGen; _toolCts?.Cancel();   // a new search cancels a running ping/DNS
        var recent = _cfg.SpotlightFiles ? SpotlightEngine.MatchRecent(q, _recent) : [];
        IReadOnlyList<SpotItem> Local(IReadOnlyList<SpotItem> files) => FillLocalTools(SpotlightEngine.Query(q, _catalog.Apps, _lprefs.Usage, _folders, files, p => Directory.Exists(p) || File.Exists(p), IsRecording, _scripts, _wsNames));
        _spot.Controller.SetResults(Local(recent)); _spot.Refresh();
        if (!_cfg.SpotlightFiles || _searchBroken || q.Trim().Length < 3) return;
        Task.Run(async () =>
        {
            await Task.Delay(180).ConfigureAwait(false);   // se espera a que el usuario deje de escribir
            if (gen != Volatile.Read(ref _spotGen)) return;
            var found = FileSearch.Indexed(q, _log);
            Post(() =>
            {
                if (found is null) { _searchBroken = true; return; }
                if (gen != _spotGen || _spot is not { IsOpen: true }) return;
                _spot.Controller.SetResults(Local(recent.Concat(found).ToList())); _spot.Refresh();
            });
        });
    }

    private IconBitmap? SpotIcon(SpotItem it)
    {
        try
        {
            if (it.App is { } app) return LauncherIcon(app);   // el mismo icono (y la misma caché) que Launchpad
            if (it.Kind is SpotKind.File or SpotKind.Folder or SpotKind.Path) return _lpIcons?.TryGet(new IconRequest(AppIdentity.FromExe(it.Target, it.Title), null, LauncherIconSize));
        }
        catch (Exception) { }
        return null;
    }

    private void Open(SpotItem it, bool reveal)
    {
        switch (it.Kind)
        {
            case SpotKind.App when it.App is { } app: LaunchApp(app); return;
            case SpotKind.Calc or SpotKind.Convert: CopyText(it.Target); _log("[spotlight] resultado copiado al portapapeles"); return;
            case SpotKind.Setting when SpotSettings.All.Any(s => s.Uri == it.Target): ShellOpen(it.Target, null); return;   // solo URIs de la lista fija
            case SpotKind.Folder or SpotKind.Path or SpotKind.File:
                bool dir = Directory.Exists(it.Target);
                if (!dir && !File.Exists(it.Target)) { _log("[spotlight] ya no existe: " + it.Title); return; }
                if (reveal || (!dir && SpotlightEngine.IsExecutable(it.Target))) Explorer("/select,\"" + it.Target + "\"");   // un ejecutable se muestra, nunca se ejecuta desde la búsqueda
                else if (dir) Explorer("\"" + it.Target + "\"");
                else ShellOpen(it.Target, null);
                return;
        }
    }

    private void Explorer(string args) => Task.Run(() => { try { using var _ = Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = false }); } catch (Exception e) { _log("[spotlight] Explorador: " + e.Message); } });

    private void ShellOpen(string target, string? args) => Task.Run(() => { try { using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "" }); } catch (Exception e) { _log("[spotlight] no se pudo abrir: " + e.Message); } });

    private void CopyText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2; var h = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)bytes); if (h == IntPtr.Zero) return;
            var p = GlobalLock(h); Marshal.Copy(text.ToCharArray(), 0, p, text.Length); Marshal.WriteInt16(p, text.Length * 2, 0); GlobalUnlock(h);
            if (SetClipboardData(13 /* CF_UNICODETEXT */, h) == IntPtr.Zero) GlobalFree(h);
        }
        finally { CloseClipboard(); }
    }

    private void StopSpotlight() { _spotKey?.Dispose(); _spotKey = null; _spot?.Dispose(); _spot = null; _spotEvent?.Dispose(); _spotEvent = null; }

    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr h);
}
