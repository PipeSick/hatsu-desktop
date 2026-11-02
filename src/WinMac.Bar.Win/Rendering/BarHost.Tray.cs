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
            _trayHost.Message += m => Post(() => { if (_trayStore.Apply(m)) SetStateAll(); });   // el estado solo se toca en el hilo de la barra
            _trayHost.Start();
        }
        catch (Exception e) { _log("[bandeja] no arrancó: " + e.GetType().Name + ": " + e.Message); _trayHost = null; }
    }

    private void StopTray()
    {
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

    /// <summary>Clic en un icono: la app recibe lo mismo que le enviaría Explorer, con permiso para pasar al frente (su menú o su ventana).</summary>
    private void TrayClickOn(BarWindow src, BarAction a, BarItemFrame? item, TrayButton button)
    {
        if (a.Uri is not { } key || _trayStore.Get(key) is not { } icon) return;
        IntPtr hwnd = (IntPtr)icon.Hwnd;
        if (!BarNative.IsWindow(hwnd)) { _trayStore.RemoveDead(h => h != icon.Hwnd); SetStateAll(); return; }
        int x = item is null ? src.Left : src.Left + (int)((item.X + item.W / 2) * src.Scale), y = src.Top + src.Height;
        BarNative.GetWindowThreadProcessId(hwnd, out uint pid); AllowSetForegroundWindow(pid);
        foreach (var (w, l) in TrayClick.Messages(icon, button, x, y)) BarNative.PostMessage(hwnd, icon.CallbackMessage, w, l);
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
}
