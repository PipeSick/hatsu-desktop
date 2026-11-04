using System.Runtime.InteropServices;
using WinMac.Bar.Tray;
using WinMac.Bar.Win.Interop;
using WinMac.Bar.Win.Shell.Tray;

namespace WinMac.Bar.Win.Rendering;

/// <summary>Iconos de la bandeja de Windows (apps en segundo plano) en todas las barras: los recibe <see cref="TrayHost"/>, los guarda <see cref="TrayIconStore"/> y los clics van a cada app.</summary>
public sealed partial class BarHost
{
    private TrayHost? _trayHost; private readonly TrayIconStore _trayStore = new(); private DateTime _lastTrayClean = DateTime.MinValue;

    /// <summary>Estado de la bandeja para el diagnóstico.</summary>
    public string TrayReport => _trayHost is null ? "bandeja: apagada" : $"bandeja: {(_trayHost.IsPrimary ? "activa" : "Windows aún usa la de Explorer")}, Explorer {(_trayHost.ExplorerFound ? "encontrado" : "NO encontrado")}, {_trayHost.MessagesSeen} mensajes, {_trayStore.Visible.Count} iconos visibles";

    private void StartTray()
    {
        if (_trayHost is not null || !_cfg.ShowTrayIcons) return;
        try
        {
            _trayHost = new TrayHost(_log);
            _trayHost.Message += m => Post(() => { if (_trayStore.Apply(m)) { SetStateAll(); TrayDiag(); if (_popover is { IsOpen: true } && _pkind == PanelKind.Tray) RebuildPanel(); } });   // el estado solo se toca en el hilo de la barra
            _trayHost.Start();
        }
        catch (Exception e) { _log("[bandeja] no arrancó: " + e.GetType().Name + ": " + e.Message); _trayHost = null; }
    }

    /// <summary>
    /// Solo con WINMAC_TRAY_DIAG=1 (la prueba visual del CI): anota en bar.log los iconos visibles con su texto, para comprobar el estado interno de ESTA barra.
    /// Apagado por defecto: en uso normal no se registra el texto de los iconos de las apps.
    /// </summary>
    private static readonly bool TrayDiagOn = Environment.GetEnvironmentVariable("WINMAC_TRAY_DIAG") == "1";
    private void TrayDiag()
    {
        if (!TrayDiagOn) return;
        var v = _trayStore.Visible; _log($"[bandeja] visibles ({v.Count}): " + string.Join(" | ", v.Select(i => i.Tip)));
        foreach (var w in _wins)   // dónde está la flecha en la pantalla (la prueba visual del CI la pulsa para abrir el desplegable)
            foreach (var f in w.Ctl.Frame.Items.Where(i => i.Item.Kind == BarItemKind.TrayMenu))
                _log($"[bandeja] botón: x={w.Left + (int)((f.X + f.W / 2) * w.Scale)} y={w.Top + w.Height / 2}");
    }

    private void StopTray()
    {
        EndTrayMenuWatch(hide: false);
        var t = _trayHost; _trayHost = null; t?.Dispose();
        if (_trayStore.Count > 0) { _trayStore.RemoveDead(_ => false); SetStateAll(); }
    }

    /// <summary>Cada 10 s: fuera los iconos de apps que se cerraron sin borrarlo (Explorer hace lo mismo al pasar el ratón por encima).</summary>
    private void TrayTick()
    {
        if (_trayHost is null || (DateTime.UtcNow - _lastTrayClean).TotalSeconds < 10) return;
        _lastTrayClean = DateTime.UtcNow;
        if (_trayStore.RemoveDead(h => BarNative.IsWindow((IntPtr)h))) SetStateAll();
    }

    /// <summary>
    /// Clic en un icono del desplegable: se cierra el desplegable y la app recibe lo mismo que le enviaría Explorer (clic izquierdo = abrirla, derecho = su menú),
    /// con permiso para pasar al frente y con el punto de anclaje bajo la flecha de la barra (donde aparece su menú).
    /// </summary>
    private void TrayPanelAction(PanelEvent e)
    {
        if (e.Id == "open:windows-tray")   // WM-023: Windows' own overflow (UI Automation); without it, Settings › Taskbar › other tray icons
        {
            _popover?.Hide();
            Task.Run(() => { if (!WinMac.Bar.Win.Menus.AppMenuReader.OpenWindowsTrayOverflow(_log)) Post(() => BarNative.ShellExecuteW(IntPtr.Zero, "open", "ms-settings:taskbar", null, null, 1)); });
            return;
        }

        bool right = e.Id.StartsWith("rclick:", StringComparison.Ordinal); string id = right ? e.Id[7..] : e.Id;
        if (!id.StartsWith("trayitem:", StringComparison.Ordinal) || _trayStore.Get(id[9..]) is not { } icon) return;
        var src = _popSource; int x = _anchorX, y = src is null ? 0 : src.Top + src.Height;
        IntPtr hwnd = (IntPtr)icon.Hwnd;
        if (!BarNative.IsWindow(hwnd)) { _popover?.Hide(); _trayStore.RemoveDead(h => h != icon.Hwnd); SetStateAll(); return; }
        BarNative.GetWindowThreadProcessId(hwnd, out uint pid); AllowSetForegroundWindow(pid);
        // Clic derecho: el panel sigue visible (y con el foco) mientras la app muestra su menú; se cierra cuando el menú se cierra (ver TrayMenuWatch). Clic izquierdo: se cierra como siempre.
        if (right && _popover is { IsOpen: true }) BeginTrayMenuWatch(); else _popover?.Hide();
        foreach (var (w, l) in TrayClick.Messages(icon, right ? TrayButton.Right : TrayButton.Left, x, y)) BarNative.PostMessage(hwnd, icon.CallbackMessage, w, l);
    }

    private TrayMenuWatch? _trayWatch; private System.Threading.Timer? _trayWatchTimer; private long _trayWatchStart;

    private void BeginTrayMenuWatch()
    {
        EndTrayMenuWatch(hide: false);
        if (_popover is null) return;
        _popover.KeepOpen = true; _trayWatch = new TrayMenuWatch(); _trayWatchStart = Environment.TickCount64;
        _trayWatchTimer = new System.Threading.Timer(_ => { bool vis; try { vis = TrayMenuProbe.AnyMenuVisible(); } catch (Exception) { vis = false; } Post(() => StepTrayMenuWatch(vis)); }, null, 80, 80);
    }

    private void StepTrayMenuWatch(bool menuVisible)
    {
        if (_trayWatch is null) return;
        if (_popover is not { IsOpen: true }) { EndTrayMenuWatch(hide: false); return; }   // el panel se cerró por otro lado (otro icono de la barra, Esc…)
        if (_trayWatch.Step((int)(Environment.TickCount64 - _trayWatchStart), menuVisible) == TrayMenuPhase.Finished) EndTrayMenuWatch(hide: true);
    }

    private void EndTrayMenuWatch(bool hide)
    {
        bool was = _trayWatch is not null; _trayWatchTimer?.Dispose(); _trayWatchTimer = null; _trayWatch = null;
        if (!was) return;
        if (_popover is not null) _popover.KeepOpen = false;
        if (hide) _popover?.Hide();
    }

    /// <summary>Iconos del desplegable: el HICON de la app a 32×32 BGRA (el que pide el dibujo de los paneles).</summary>
    private static byte[]? TrayIconBgra(string key)
    {
        if (!uint.TryParse(key.AsSpan(PanelBuilder.TrayIconPrefix.Length), out uint hicon) || TrayIconPixels.Read(hicon) is not { } px) return null;
        return px.W > 32 || px.H > 32 ? Shell.Notifications.WinNotificationSource.Downscale(px.Bgra, px.W, px.H, 32) : px.W == 32 && px.H == 32 ? px.Bgra : Shell.Hub.AppIcons.Resize(px.Bgra, px.W, px.H, 32);
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
