using WinMac.Apps;

namespace WinMac.Dock;

public sealed record MenuEntry(string Text, DockAction? Action = null, bool Enabled = true, bool IsSeparator = false, bool IsHeader = false)
{
    public static MenuEntry Separator => new("", null, false, true);
}

/// <summary>
/// Modelo del menú contextual (sin render): el adaptador de Windows lo muestra con un menú nativo y ejecuta la <see cref="DockAction"/> elegida.
/// Nada fuerza la terminación de procesos: «Salir» pide el cierre de las ventanas (WM_CLOSE), que es la forma segura y cooperativa.
/// </summary>
public static class DockMenu
{
    public const int MaxWindows = 10, MaxTitle = 56;

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public static IReadOnlyList<MenuEntry> ForItem(DockItemModel it, int pinnedIndex = -1, int pinnedCount = 0)
    {
        var m = new List<MenuEntry>();
        switch (it.Kind)
        {
            case DockItemKind.Folder:
            case DockItemKind.Trash:
                m.Add(new MenuEntry("Abrir", new DockAction(DockActionKind.Open, it.Id, it.Path))); break;
            case DockItemKind.App:
                var wins = it.Windows ?? [];
                if (wins.Count > 0)
                {
                    m.Add(new MenuEntry("Ventanas", null, false, false, true));
                    foreach (var w in wins.Take(MaxWindows))
                        m.Add(new MenuEntry(Truncate(string.IsNullOrWhiteSpace(w.Title) ? "(sin título)" : w.Title, MaxTitle) + (w.Minimized ? "  (minimizada)" : ""), new DockAction(DockActionKind.ActivateWindow, it.Id, null, w.Hwnd, it.Identity)));
                    if (wins.Count > MaxWindows) m.Add(new MenuEntry($"… y {wins.Count - MaxWindows} más", null, false));
                    m.Add(MenuEntry.Separator);
                }
                var open = !it.Running ? new DockAction(DockActionKind.Launch, it.Id, it.Path, 0, it.Identity)
                    : new DockAction(it.AllMinimized ? DockActionKind.Restore : DockActionKind.Activate, it.Id, it.Path, 0, it.Identity);
                m.Add(new MenuEntry("Abrir", open));
                m.Add(MenuEntry.Separator);
                if (it.Identity is not null)
                    m.Add(it.Pinned ? new MenuEntry("Quitar del Dock", new DockAction(DockActionKind.Unpin, it.Id, null, 0, it.Identity))
                                    : new MenuEntry("Anclar al Dock", new DockAction(DockActionKind.Pin, it.Id, null, 0, it.Identity)));
                if (it.Pinned && pinnedIndex >= 0)
                {
                    m.Add(new MenuEntry("Mover a la izquierda", new DockAction(DockActionKind.MoveLeft, it.Id, null, 0, it.Identity), pinnedIndex > 0));
                    m.Add(new MenuEntry("Mover a la derecha", new DockAction(DockActionKind.MoveRight, it.Id, null, 0, it.Identity), pinnedIndex < pinnedCount - 1));
                }
                if (wins.Count > 0)
                {
                    m.Add(MenuEntry.Separator);
                    if (wins.Count == 1) m.Add(new MenuEntry("Cerrar ventana", new DockAction(DockActionKind.CloseWindow, it.Id, null, wins[0].Hwnd, it.Identity)));
                    else foreach (var w in wins.Take(MaxWindows)) m.Add(new MenuEntry("Cerrar ventana: " + Truncate(string.IsNullOrWhiteSpace(w.Title) ? "(sin título)" : w.Title, MaxTitle - 16), new DockAction(DockActionKind.CloseWindow, it.Id, null, w.Hwnd, it.Identity)));
                    m.Add(new MenuEntry("Salir (cierra todas las ventanas)", new DockAction(DockActionKind.CloseApp, it.Id, null, 0, it.Identity)));
                }
                break;
        }
        return m;
    }

    /// <summary>Clic derecho sobre el fondo de la barra (no sobre un ícono): cierre normal del Dock.</summary>
    public static IReadOnlyList<MenuEntry> ForBackground() => [new MenuEntry("Salir del Dock", new DockAction(DockActionKind.Quit))];
}
