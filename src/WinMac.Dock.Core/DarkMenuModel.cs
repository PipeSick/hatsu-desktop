namespace WinMac.Dock;

/// <summary>
/// WM-017: the Dock's own dark context menu (it replaces the white Win32 menu) with EXACTLY the same entries and actions. Submenus open inside the same panel
/// («‹ Ajustes del Dock» to go back), so nothing cascades off-screen. Pure: rows, width, hit-testing and keyboard focus are tested without Windows.
/// </summary>
public sealed class DarkMenuModel
{
    public const double ItemH = 28, SeparatorH = 9, HeaderH = 26, Pad = 6, MinW = 210, MaxW = 340;

    private readonly IReadOnlyList<MenuEntry> _root; private readonly Stack<(string Title, IReadOnlyList<MenuEntry> Items)> _path = new();

    public DarkMenuModel(IReadOnlyList<MenuEntry> root) { _root = root; Focus = -1; }

    /// <summary>Rows shown now: the root, or a submenu preceded by its «back» row.</summary>
    public IReadOnlyList<MenuEntry> Rows => _path.Count == 0 ? _root : [new MenuEntry("‹ " + _path.Peek().Title, null, true, IsHeader: true), .. _path.Peek().Items];
    public bool InSubmenu => _path.Count > 0;
    public int Focus { get; private set; }

    public static double HeightOf(MenuEntry e) => e.IsSeparator ? SeparatorH : e.IsHeader && e.Text.StartsWith('‹') ? ItemH : e.IsHeader ? HeaderH : ItemH;
    public double Height => Rows.Sum(HeightOf) + 2 * Pad;

    /// <summary>Wide enough for the longest label of the WHOLE tree (the panel keeps its width when entering a submenu).</summary>
    public double Width => Math.Clamp(Longest(_root) * 7.2 + 64, MinW, MaxW);
    private static int Longest(IReadOnlyList<MenuEntry> l) => l.Count == 0 ? 0 : l.Max(e => Math.Max(e.Text.Length, e.Children is { } c ? Longest(c) : 0));

    public int RowAt(double y)
    {
        double t = Pad; var rows = Rows;
        for (int i = 0; i < rows.Count; i++) { double h = HeightOf(rows[i]); if (y >= t && y < t + h) return i; t += h; }
        return -1;
    }

    public double RowY(int index) { double t = Pad; var rows = Rows; for (int i = 0; i < index && i < rows.Count; i++) t += HeightOf(rows[i]); return t; }

    private static bool Selectable(MenuEntry e) => !e.IsSeparator && e.Enabled && (!e.IsHeader || e.Text.StartsWith('‹'));

    public enum Outcome { None, Chosen, Navigated, Closed }

    /// <summary>Activates a row: a submenu opens, «back» returns, an item returns its action. Disabled rows and plain headers do nothing.</summary>
    public (Outcome Result, DockAction? Action) Activate(int index)
    {
        var rows = Rows; if (index < 0 || index >= rows.Count) return (Outcome.None, null);
        var e = rows[index];
        if (!Selectable(e)) return (Outcome.None, null);
        if (InSubmenu && index == 0) { _path.Pop(); Focus = -1; return (Outcome.Navigated, null); }
        if (e.Children is { Count: > 0 } kids) { _path.Push((e.Text, kids)); Focus = -1; return (Outcome.Navigated, null); }
        return e.Action is { } a ? (Outcome.Chosen, a) : (Outcome.None, null);
    }

    /// <summary>Keyboard: ↑ ↓ move over selectable rows, Intro/→ activate, ← or Esc go back (Esc at the root closes).</summary>
    public (Outcome Result, DockAction? Action) Key(int vk)
    {
        var rows = Rows; var sel = Enumerable.Range(0, rows.Count).Where(i => Selectable(rows[i])).ToList();
        switch (vk)
        {
            case 0x28: if (sel.Count > 0) Focus = sel.FirstOrDefault(i => i > Focus, sel[0]); return (Outcome.None, null);
            case 0x26: if (sel.Count > 0) Focus = sel.LastOrDefault(i => i < Focus, sel[^1]); return (Outcome.None, null);
            case 0x0D or 0x27: return Focus >= 0 ? Activate(Focus) : (Outcome.None, null);
            case 0x25 when InSubmenu: _path.Pop(); Focus = -1; return (Outcome.Navigated, null);
            case 0x1B: if (InSubmenu) { _path.Pop(); Focus = -1; return (Outcome.Navigated, null); } return (Outcome.Closed, null);
        }

        return (Outcome.None, null);
    }

    public void Hover(int index) => Focus = index >= 0 && index < Rows.Count && Selectable(Rows[index]) ? index : -1;
}
