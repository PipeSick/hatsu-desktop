using System.Diagnostics;
using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>Ejecuta las acciones que decide <see cref="DockController"/>. Solo APIs públicas de ventanas y ShellExecute; nunca toca procesos ajenos más allá de mostrar/minimizar/activar.</summary>
public sealed class WindowActions
{
    private readonly WindowTracker _tracker;
    public WindowActions(WindowTracker tracker) { _tracker = tracker; }
    public Action<string>? Log { get; set; }

    public void Execute(DockAction a)
    {
        try
        {
            switch (a.Kind)
            {
                case DockActionKind.Launch:
                case DockActionKind.Open:
                    if (!string.IsNullOrEmpty(a.Path)) Process.Start(new ProcessStartInfo { FileName = Environment.ExpandEnvironmentVariables(a.Path), UseShellExecute = true });
                    break;
                case DockActionKind.Activate: ForEachWindow(a.Path, FirstWindow(a.Path), (h, w) => Activate(h)); break;
                case DockActionKind.Restore: ForEachWindow(a.Path, null, (h, w) => { if (w.Minimized) Native.ShowWindow(h, Native.SW_RESTORE); }); ActivateLast(a.Path); break;
                case DockActionKind.Minimize: ForEachWindow(a.Path, null, (h, w) => Native.ShowWindow(h, Native.SW_MINIMIZE)); break;
            }
        }
        catch (Exception e) { Log?.Invoke($"Acción {a.Kind} falló: {e.Message}"); }
    }

    private static void Activate(IntPtr h)
    {
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        Native.SetForegroundWindow(h); // permitido: la última entrada del usuario fue un clic sobre nuestra ventana
    }

    private WindowInfo? FirstWindow(string? path) => Windows(path).FirstOrDefault();
    private IEnumerable<WindowInfo> Windows(string? path) =>
        path is null ? [] : _tracker.Snapshot().Where(r => string.Equals(DockModelBuilder.Norm(r.ExePath), DockModelBuilder.Norm(path), StringComparison.Ordinal)).SelectMany(r => r.Windows);

    private void ForEachWindow(string? path, WindowInfo? only, Action<IntPtr, WindowInfo> act)
    {
        foreach (var w in only is null ? Windows(path) : [only]) act(new IntPtr(w.Hwnd), w);
    }
    private void ActivateLast(string? path) { var w = Windows(path).LastOrDefault(); if (w is not null) Activate(new IntPtr(w.Hwnd)); }
}
