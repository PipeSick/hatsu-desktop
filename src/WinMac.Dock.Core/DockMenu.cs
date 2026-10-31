using WinMac.Apps;

namespace WinMac.Dock;

public sealed record MenuEntry(string Text, DockAction? Action = null, bool Enabled = true, bool IsSeparator = false, bool IsHeader = false, bool Checked = false, IReadOnlyList<MenuEntry>? Children = null)
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

    public static IReadOnlyList<MenuEntry> ForItem(DockItemModel it, int pinnedIndex = -1, int pinnedCount = 0, DockConfig? cfg = null)
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
        if (cfg is not null) { if (m.Count > 0) m.Add(MenuEntry.Separator); m.Add(Settings(cfg)); }
        return m;
    }

    /// <summary>Clic derecho sobre el fondo de la barra (no sobre un ícono): ajustes y cierre normal del Dock.</summary>
    public static IReadOnlyList<MenuEntry> ForBackground(DockConfig? cfg = null) =>
        cfg is null ? [new MenuEntry("Salir del Dock", new DockAction(DockActionKind.Quit))]
                    : [Settings(cfg), MenuEntry.Separator, new MenuEntry("Salir del Dock", new DockAction(DockActionKind.Quit))];

    /// <summary>«Ajustes del Dock»: tamaño (presets), posición (izquierda/centro/derecha) y altura. Marca el valor actual.</summary>
    public static MenuEntry Settings(DockConfig c)
    {
        var sizes = new List<MenuEntry>(); bool matched = false;
        foreach (var (name, size) in DockSettings.Presets)
        {
            bool on = Math.Abs(c.IconSize - size) < 0.5; matched |= on;
            sizes.Add(new MenuEntry($"{name} ({size:0})", new DockAction(DockActionKind.SetIconSize, Number: size), true, Checked: on));
        }
        if (!matched) sizes.Add(new MenuEntry($"Personalizado ({c.IconSize:0})", null, false, Checked: true));
        var pos = new List<MenuEntry>
        {
            new("Izquierda", new DockAction(DockActionKind.SetAlignment, Number: (double)DockAlignment.Left), true, Checked: c.Alignment == DockAlignment.Left),
            new("Centro", new DockAction(DockActionKind.SetAlignment, Number: (double)DockAlignment.Center), true, Checked: c.Alignment == DockAlignment.Center),
            new("Derecha", new DockAction(DockActionKind.SetAlignment, Number: (double)DockAlignment.Right), true, Checked: c.Alignment == DockAlignment.Right),
        };
        var height = new List<MenuEntry>
        {
            new($"Subir ({DockSettings.NudgeStep:0} px)", new DockAction(DockActionKind.NudgeBottom, Number: DockSettings.NudgeStep), c.BottomOffset < DockSettings.MaxBottomOffset),
            new($"Bajar ({DockSettings.NudgeStep:0} px)", new DockAction(DockActionKind.NudgeBottom, Number: -DockSettings.NudgeStep), c.BottomOffset > 0),
            new("Altura normal", new DockAction(DockActionKind.ResetBottom), c.BottomOffset > 0),
        };
        return new MenuEntry("Ajustes del Dock", null, true, Children: [new("Tamaño", null, true, Children: sizes), new("Posición", null, true, Children: pos), new("Altura", null, true, Children: height)]);
    }
}
