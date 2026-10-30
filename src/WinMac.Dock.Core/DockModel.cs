namespace WinMac.Dock;

public enum DockItemKind { App, Separator, Folder, Trash }

/// <summary>Elemento lógico del dock (independiente del render).</summary>
public sealed record DockItemModel(
    string Id, DockItemKind Kind, string Title, string? Path,
    bool Pinned = false, bool Running = false, int WindowCount = 0, bool Active = false, bool AllMinimized = false);

public sealed record WindowInfo(long Hwnd, string Title, bool Minimized, bool Active);

/// <summary>Aplicación en ejecución con ventanas «de app» visibles (ya filtradas por el adaptador de Windows).</summary>
public sealed record RunningApp(string ExePath, int Pid, IReadOnlyList<WindowInfo> Windows);

public enum DockActionKind { None, Launch, Activate, Restore, Minimize, Open }

public sealed record DockAction(DockActionKind Kind, string? ItemId = null, string? Path = null, long Hwnd = 0);
