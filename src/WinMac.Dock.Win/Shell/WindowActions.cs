using System.Diagnostics;
using WinMac.Apps;
using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>
/// Ejecuta las acciones que decide <see cref="DockController"/> con APIs documentadas de ventanas y del Shell. NUNCA termina procesos:
/// «cerrar» envía WM_CLOSE (el cierre cooperativo normal, que la app puede aceptar o preguntar «¿guardar cambios?»).
/// Cada acción es independiente y está aislada: un fallo en una app se registra y no afecta al Dock ni a las demás.
/// </summary>
public sealed class WindowActions
{
    private const uint WM_CLOSE = 0x0010;
    private readonly WindowTracker _tracker;
    public WindowActions(WindowTracker tracker) { _tracker = tracker; }
    public Action<string>? Log { get; set; }

    public void Execute(DockAction a)
    {
        try
        {
            switch (a.Kind)
            {
                case DockActionKind.Launch: Launch(a.App, a.Path); break;
                case DockActionKind.Open: OpenShell(a.Path); break;
                case DockActionKind.Activate: { var w = Windows(a.App, a.Path).ToList(); var top = w.FirstOrDefault(x => !x.Minimized) ?? w.FirstOrDefault(); if (top is not null) Activate(new IntPtr(top.Hwnd)); else Launch(a.App, a.Path); break; }
                case DockActionKind.Restore: { var w = Windows(a.App, a.Path).ToList(); var first = w.FirstOrDefault(x => x.Minimized) ?? w.FirstOrDefault(); if (first is not null) Activate(new IntPtr(first.Hwnd)); else Launch(a.App, a.Path); break; }
                case DockActionKind.Minimize: foreach (var w in Windows(a.App, a.Path)) Native.ShowWindow(new IntPtr(w.Hwnd), Native.SW_MINIMIZE); break;
                case DockActionKind.ActivateWindow: if (a.Hwnd != 0) Activate(new IntPtr(a.Hwnd)); break;
                case DockActionKind.CloseWindow: if (a.Hwnd != 0) Native.PostMessage(new IntPtr(a.Hwnd), WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
                case DockActionKind.CloseApp: foreach (var w in Windows(a.App, a.Path)) Native.PostMessage(new IntPtr(w.Hwnd), WM_CLOSE, IntPtr.Zero, IntPtr.Zero); break;
            }
        }
        catch (Exception e) { Log?.Invoke($"Acción {a.Kind} falló: {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Win32 → se ejecuta el .exe (ShellExecute). Empaquetada → «shell:AppsFolder\AUMID» con el Explorador (activación del Shell; no se ejecuta el .exe del paquete).</summary>
    public static void Launch(AppIdentity? app, string? fallbackPath)
    {
        if (app?.Aumid is string aumid) { Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{aumid}") { UseShellExecute = false })?.Dispose(); return; }
        string? target = app?.ExePath ?? fallbackPath; if (string.IsNullOrEmpty(target)) return;
        Process.Start(new ProcessStartInfo { FileName = Environment.ExpandEnvironmentVariables(target), UseShellExecute = true })?.Dispose();
    }

    private static void OpenShell(string? path)
    {
        if (!string.IsNullOrEmpty(path)) Process.Start(new ProcessStartInfo { FileName = Environment.ExpandEnvironmentVariables(path), UseShellExecute = true })?.Dispose();
    }

    private static void Activate(IntPtr h)
    {
        if (!Native.IsWindow(h)) return;
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        Native.SetForegroundWindow(h); // permitido: la última entrada del usuario fue un clic sobre nuestra ventana
    }

    /// <summary>Ventanas actuales de la app (orden Z: la superior primero). Coincide por identidad estable o por ejecutable (apps ancladas por ruta).</summary>
    private IEnumerable<WindowInfo> Windows(AppIdentity? app, string? path)
    {
        var target = app ?? (path is null ? null : AppIdentity.FromExe(path)); if (target is null) return [];
        return _tracker.Snapshot().Where(r => target.SameApp(r.Identity)).SelectMany(r => r.Windows);
    }
}
