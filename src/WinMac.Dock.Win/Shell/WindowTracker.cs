using System.Text;
using WinMac.Apps;
using WinMac.Apps.Win;
using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>
/// Seguimiento de ventanas «de app» con SetWinEventHook (sin polling) y consolidación por ejecutable.
/// Debe crearse y usarse en el hilo que ejecuta el bucle de mensajes. Los eventos se acumulan y se notifican con un pequeño retardo (debounce).
/// LIMITACIONES CONOCIDAS (pendientes de validación): apps UWP agrupadas bajo ApplicationFrameHost; procesos elevados (UIPI);
/// ventanas de un mismo ejecutable con propósitos distintos.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private readonly Native.WinEventProc _proc; private readonly List<IntPtr> _hooks = new();
    private DateTime _dirtySince = DateTime.MinValue; private bool _dirty = true;
    private readonly string _self;
    public event Action? Changed;

    public WindowTracker()
    {
        _self = Environment.ProcessPath ?? "";
        _proc = (h, ev, hwnd, idObject, idChild, t, time) => { if (idObject == 0 && idChild == 0) { _dirty = true; _dirtySince = _dirtySince == DateTime.MinValue ? DateTime.UtcNow : _dirtySince; } };
        void Hook(uint a, uint b) { var h = Native.SetWinEventHook(a, b, IntPtr.Zero, _proc, 0, 0, Native.WINEVENT_OUTOFCONTEXT); if (h != IntPtr.Zero) _hooks.Add(h); }
        Hook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
        Hook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND);
        Hook(Native.EVENT_OBJECT_CREATE, Native.EVENT_OBJECT_HIDE);
        Hook(Native.EVENT_OBJECT_NAMECHANGE, Native.EVENT_OBJECT_NAMECHANGE);
        Hook(Native.EVENT_OBJECT_CLOAKED, Native.EVENT_OBJECT_UNCLOAKED);
        HookCount = _hooks.Count;
    }

    public int HookCount { get; }

    /// <summary>Llamar periódicamente desde el bucle principal: emite <see cref="Changed"/> si hubo eventos y pasó el debounce.</summary>
    public void Pump()
    {
        if (_dirty && (DateTime.UtcNow - _dirtySince).TotalMilliseconds >= 120) { _dirty = false; _dirtySince = DateTime.MinValue; Changed?.Invoke(); }
    }

    public bool HasPending => _dirty;

    /// <summary>Lee lo necesario de una ventana para <see cref="AppWindowRules"/> (la regla, pura y con pruebas, vive en Dock.Core).</summary>
    internal static WindowFacts Facts(IntPtr h, out string title)
    {
        var t = new StringBuilder(256); bool hasTitle = Native.GetWindowTextW(h, t, 256) > 0; title = t.ToString();
        bool cloaked = Native.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int c, 4) == 0 && c != 0;
        var cls = new StringBuilder(256); Native.GetClassNameW(h, cls, 256);
        return new WindowFacts(Native.IsWindowVisible(h), cloaked, Native.IsIconic(h), Native.GetWindowLongPtr(h, -20).ToInt64(), Native.GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero, cls.ToString(), hasTitle);
    }

    internal static bool IsAppWindow(IntPtr h) => AppWindowRules.IsAppWindow(Facts(h, out _));

    /// <summary>Instantánea de apps en ejecución con ventanas de app, agrupadas por IDENTIDAD estable (AUMID para apps empaquetadas, ejecutable para Win32). Excluye al propio dock. El orden de ventanas es el Z-order (la superior primero).</summary>
    public IReadOnlyList<RunningApp> Snapshot()
    {
        var fg = Native.GetAncestor(Native.GetForegroundWindow(), 2 /* GA_ROOT */);
        var groups = new Dictionary<string, (AppIdentity Id, uint Pid, List<WindowInfo> W)>(); var pathOf = new Dictionary<uint, string?>();
        Native.EnumWindows((h, _) =>
        {
            try
            {
                var facts = Facts(h, out var title); bool minimized = facts.Minimized;
                if (!AppWindowRules.IsAppWindow(facts)) return true;   // también las minimizadas deben ser visibles (antes no: se colaban ventanas ocultas del sistema)
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (!pathOf.TryGetValue(pid, out var path)) pathOf[pid] = path = Native.ProcessPath(pid);
                if (path is null || string.Equals(path, _self, StringComparison.OrdinalIgnoreCase)) return true;
                var id = WindowIdentityResolver.Resolve(h, pid, path); if (id is null) return true;   // p. ej. marco de ApplicationFrameHost sin AUMID
                if (!groups.TryGetValue(id.Id, out var g)) groups[id.Id] = g = (id, pid, new List<WindowInfo>());
                g.W.Add(new WindowInfo(h.ToInt64(), title, minimized, h == fg));
            }
            catch (Exception) { /* una ventana problemática no debe romper el dock */ }
            return true;
        }, IntPtr.Zero);
        return groups.Values.Select(g => new RunningApp(g.Id, (int)g.Pid, g.W)).ToList();
    }

    public void Dispose() { foreach (var h in _hooks) Native.UnhookWinEvent(h); _hooks.Clear(); }
}
