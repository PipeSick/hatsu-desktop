using System.Runtime.InteropServices;
using WinMac.Apps;
using WinMac.Bar.Capture;
using WinMac.Bar.Features;
using WinMac.Dock;
using WinMac.Dock.Win.Shell;

namespace WinMac.Bar.Win.Features;

/// <summary>
/// Catálogo de ventanas COMPARTIDO por Mission Control, Snap y Spotlight: reutiliza <see cref="WindowTracker"/> (enumeración y reglas de qué es una «ventana de app»), <see cref="WindowThumbnails"/> (capturas)
/// y <see cref="WindowActions"/> (activar/cerrar) del Dock; no hay otra enumeración. Se crea la primera vez que un módulo lo adquiere y se libera cuando el último lo suelta (sin ganchos ni hilos entre medias).
/// Solo ve las ventanas del escritorio virtual ACTUAL (Windows marca las de otros escritorios como «cloaked» y la regla del Dock las descarta): es una limitación documentada, no se usan APIs privadas.
/// </summary>
internal sealed class WindowCatalog : IDisposable
{
    private WindowTracker? _tracker; private WindowThumbnails? _thumbs; private WindowActions? _actions; private int _refs; private readonly Action<string> _log;
    public WindowCatalog(Action<string> log) { _log = log; }

    public WindowThumbnails Thumbs => _thumbs ?? throw new InvalidOperationException("catálogo no adquirido");

    /// <summary>Un módulo lo necesita. El <paramref name="scope"/> lo suelta solo al apagarse.</summary>
    public WindowCatalog Acquire(FeatureScope scope)
    {
        if (_refs++ == 0)
        {
            _tracker = new WindowTracker(); _thumbs = new WindowThumbnails { Log = _log }; _actions = new WindowActions(_tracker) { Log = _log };
            _log("[ventanas] catálogo compartido iniciado");
        }

        bool released = false;
        scope.Add(() => { if (released) return; released = true; Release(); });
        return this;
    }

    private void Release()
    {
        if (--_refs > 0) return;
        _refs = 0; _thumbs?.Dispose(); _tracker?.Dispose(); _thumbs = null; _tracker = null; _actions = null; _log("[ventanas] catálogo compartido liberado");
    }

    /// <summary>Ventanas de app del escritorio actual, con el monitor donde están. Excluye las de Hatsu mismo.</summary>
    public List<McWindow> Snapshot(IReadOnlyList<CaptureMonitor> monitors)
    {
        var list = new List<McWindow>();
        foreach (var app in _tracker!.Snapshot())
        {
            if (IsHatsu(app.Identity)) continue;
            foreach (var w in app.Windows)
                list.Add(new McWindow(w.Hwnd, w.Title, app.Identity.DisplayName, w.Minimized, w.Active, MonitorOf(w.Hwnd, w.Minimized, monitors), _thumbs!.Aspect(w.Hwnd)));
        }

        return list;
    }

    private static bool IsHatsu(AppIdentity id)
    {
        var name = Path.GetFileNameWithoutExtension(id.ExePath ?? ""); return name.StartsWith("HatsuDesktop", StringComparison.OrdinalIgnoreCase) || name.StartsWith("NotchWin", StringComparison.OrdinalIgnoreCase) || name.StartsWith("WinMac", StringComparison.OrdinalIgnoreCase);
    }

    public static int MonitorOf(long hwnd, bool minimized, IReadOnlyList<CaptureMonitor> monitors)
    {
        if (monitors.Count == 0) return 0;
        int cx, cy;
        if (minimized) { var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() }; if (!GetWindowPlacement(new IntPtr(hwnd), ref wp)) return PrimaryIndex(monitors); cx = (wp.rcNormal.L + wp.rcNormal.R) / 2; cy = (wp.rcNormal.T + wp.rcNormal.B) / 2; }
        else { if (!GetWindowRect(new IntPtr(hwnd), out var r)) return PrimaryIndex(monitors); cx = (r.L + r.R) / 2; cy = (r.T + r.B) / 2; }
        for (int i = 0; i < monitors.Count; i++) { var b = monitors[i].Bounds; if (cx >= b.X && cx < b.X + b.W && cy >= b.Y && cy < b.Y + b.H) return i; }
        int best = 0; double bd = double.MaxValue;   // fuera de todos (ventana a medio camino): el más cercano
        for (int i = 0; i < monitors.Count; i++) { var b = monitors[i].Bounds; double dx = Math.Max(Math.Max(b.X - cx, 0), cx - (b.X + b.W)), dy = Math.Max(Math.Max(b.Y - cy, 0), cy - (b.Y + b.H)); double d = dx * dx + dy * dy; if (d < bd) { bd = d; best = i; } }
        return best;
    }

    private static int PrimaryIndex(IReadOnlyList<CaptureMonitor> m) { for (int i = 0; i < m.Count; i++) if (m[i].Primary) return i; return 0; }

    public void Activate(long hwnd) => _actions?.Execute(new DockAction(DockActionKind.ActivateWindow, null, null, hwnd));
    public void Close(long hwnd) => _actions?.Execute(new DockAction(DockActionKind.CloseWindow, null, null, hwnd));

    public void Dispose() { _refs = 1; Release(); }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMin, ptMax; public RECT rcNormal; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr h, ref WINDOWPLACEMENT wp);
}
