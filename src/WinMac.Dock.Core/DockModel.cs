using WinMac.Apps;

namespace WinMac.Dock;

public enum DockItemKind { App, Separator, Folder, Trash }

/// <summary>Elemento lógico del dock (independiente del render). <see cref="Identity"/> es la identidad estable de la app (null en carpeta, papelera, separadores y en los datos de demostración de las pruebas).</summary>
public sealed record DockItemModel(
    string Id, DockItemKind Kind, string Title, string? Path,
    bool Pinned = false, bool Running = false, int WindowCount = 0, bool Active = false, bool AllMinimized = false,
    AppIdentity? Identity = null, IReadOnlyList<WindowInfo>? Windows = null, string? IconOverride = null);

public sealed record WindowInfo(long Hwnd, string Title, bool Minimized, bool Active);

/// <summary>Aplicación en ejecución con ventanas «de app» visibles (ya filtradas por el adaptador de Windows), con su identidad estable.</summary>
public sealed record RunningApp(AppIdentity Identity, int Pid, IReadOnlyList<WindowInfo> Windows)
{
    /// <summary>Atajo para apps Win32 identificadas por ejecutable (y para las pruebas).</summary>
    public RunningApp(string exePath, int pid, IReadOnlyList<WindowInfo> windows) : this(AppIdentity.FromExe(exePath), pid, windows) { }
    public string ExePath => Identity.ExePath ?? "";
}

public enum DockActionKind { None, Launch, Activate, Restore, Minimize, Open, ActivateWindow, CloseWindow, CloseApp, Pin, Unpin, MoveLeft, MoveRight, Quit, SetIconSize, SetAlignment, NudgeBottom, ResetBottom }

/// <summary>Orden que el núcleo pide al adaptador de Windows. El núcleo nunca ejecuta nada por sí mismo.</summary>
/// <param name="Number">Valor numérico de las acciones de ajustes: tamaño en DIP (<see cref="DockActionKind.SetIconSize"/>), alineación (<see cref="DockActionKind.SetAlignment"/>, el valor de <see cref="DockAlignment"/>) o incremento de altura en DIP (<see cref="DockActionKind.NudgeBottom"/>).</param>
public sealed record DockAction(DockActionKind Kind, string? ItemId = null, string? Path = null, long Hwnd = 0, AppIdentity? App = null, double Number = 0);
