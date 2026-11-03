using WinMac.Apps;

namespace WinMac.Bar.Launcher;

public readonly record struct LpRect(double X, double Y, double W, double H)
{
    public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
    public double Cx => X + W / 2; public double Cy => Y + H / 2;
}

/// <summary>Dónde va cada cosa en Launchpad (DIP, relativo a la ventana, que cubre el monitor). Cuadrícula como la de macOS: 7×5 en una pantalla normal, menos si no cabe.</summary>
public sealed record LaunchpadGeometry(double Width, double Height, LpRect Search, IReadOnlyList<LpRect> Cells, int Cols, int Rows, double Icon, double DotsY)
{
    public int PerPage => Cols * Rows;

    /// <summary>WM-018: the compact panel (search, grid and page dots) centred on the monitor; outside it the desktop shows through, dimmed, and a click closes.</summary>
    public LpRect Panel { get; init; }

    public static LaunchpadGeometry Compute(double w, double h)
    {
        w = Math.Max(320, w); h = Math.Max(240, h);
        double icon = Math.Clamp(Math.Round(h / 14), 44, 68), cellW = Math.Round(icon * 1.95), cellH = icon + 42;
        int cols = (int)Math.Clamp(Math.Floor((w * 0.62 - 48) / cellW), 3, 6);
        double searchH = 30, padX = 24, top = 18, gap = 18, dotsH = 34;
        int rows = (int)Math.Clamp(Math.Floor((h * 0.66 - top - searchH - gap - dotsH) / cellH), 1, 4);
        double pw = cols * cellW + 2 * padX, ph = top + searchH + gap + rows * cellH + dotsH;
        var panel = new LpRect(Math.Round((w - pw) / 2), Math.Round((h - ph) / 2), pw, ph);
        double searchW = Math.Min(pw - 2 * padX, 300);
        var search = new LpRect(panel.X + (pw - searchW) / 2, panel.Y + top, searchW, searchH);
        double x0 = panel.X + padX, y0 = search.Y + searchH + gap;
        var cells = new List<LpRect>(cols * rows);
        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) cells.Add(new LpRect(x0 + c * cellW, y0 + r * cellH, cellW, cellH));
        return new LaunchpadGeometry(w, h, search, cells, cols, rows, icon, panel.Y + ph - dotsH / 2) { Panel = panel };
    }

    /// <summary>Rectángulo del icono dentro de su casilla (el nombre va debajo).</summary>
    public LpRect IconRect(LpRect cell) => new(cell.Cx - Icon / 2, cell.Y + Math.Max(4, (cell.H - Icon - 30) / 2), Icon, Icon);

    /// <summary>Los puntos de página, centrados (uno por página, 8 DIP con 10 de separación).</summary>
    public LpRect Dot(int i, int pages) { double step = 18, x0 = Width / 2 - (pages - 1) * step / 2; return new LpRect(x0 + i * step - 8, DotsY - 8, 16, 16); }
}

public enum LpCommandKind { None, Redraw, Launch, Close, Move, Hide, UnhideAll, ResetOrder }

/// <summary>Lo que el anfitrión debe hacer tras un evento. <see cref="Index"/> es el destino de <see cref="LpCommandKind.Move"/> en el orden completo.</summary>
public readonly record struct LpCommand(LpCommandKind Kind, InstalledApp? App = null, int Index = -1)
{
    public static readonly LpCommand None = new(LpCommandKind.None), Redraw = new(LpCommandKind.Redraw), Close = new(LpCommandKind.Close);
}

public sealed record LpMenuItem(string Id, string Text);

/// <summary>Menú contextual dentro de Launchpad (clic derecho): sobre una app, o sobre el fondo.</summary>
public sealed record LpMenu(LpRect Rect, IReadOnlyList<LpMenuItem> Items, InstalledApp? Target)
{
    public const double ItemH = 30, W = 230;
    public LpRect ItemRect(int i) => new(Rect.X + 6, Rect.Y + 6 + i * ItemH, Rect.W - 12, ItemH);
}

/// <summary>
/// Launchpad: páginas de apps, búsqueda al escribir, teclado (flechas, Re Pág/Av Pág, Intro, Esc), ratón (clic abre, clic en el fondo cierra, rueda cambia de página),
/// arrastrar para reordenar y menú contextual (mover al principio, ocultar, mostrar ocultas, restablecer orden). Puro: el anfitrión aplica las órdenes y vuelve a pasar las apps.
/// </summary>
public sealed class LaunchpadController
{
    private IReadOnlyList<InstalledApp> _all = [];
    private IReadOnlyDictionary<string, int>? _usage;
    private (int Index, double X, double Y)? _press;

    public LaunchpadGeometry Geometry { get; private set; } = LaunchpadGeometry.Compute(1280, 800);
    public string Query { get; private set; } = "";
    public int Page { get; private set; }
    /// <summary>Casilla elegida con el teclado (en la página actual), -1 = ninguna.</summary>
    public int Selected { get; private set; } = -1;
    public int Hover { get; private set; } = -1;
    public int HiddenCount { get; private set; }
    public LpMenu? Menu { get; private set; }
    public int MenuHover { get; private set; } = -1;
    /// <summary>Arrastrando: la app y dónde está el puntero.</summary>
    public (InstalledApp App, double X, double Y)? Drag { get; private set; }

    public bool Searching => Query.Length > 0;
    public IReadOnlyList<InstalledApp> Visible => Searching ? AppMatcher.Search(_all, Query, _usage, Geometry.PerPage) : _all;
    public int PageCount => Searching ? 1 : Math.Max(1, (_all.Count + Geometry.PerPage - 1) / Geometry.PerPage);
    public IReadOnlyList<InstalledApp> PageItems => Visible.Skip((Searching ? 0 : Page) * Geometry.PerPage).Take(Geometry.PerPage).ToList();

    public void SetSize(double w, double h) { Geometry = LaunchpadGeometry.Compute(w, h); Page = Math.Clamp(Page, 0, PageCount - 1); ClampSelection(); }

    public void SetApps(IReadOnlyList<InstalledApp> arranged, int hiddenCount, IReadOnlyDictionary<string, int>? usage = null)
    {
        _all = arranged; HiddenCount = hiddenCount; _usage = usage; Page = Math.Clamp(Page, 0, PageCount - 1); ClampSelection();
    }

    /// <summary>Al abrir: primera página, sin búsqueda ni selección.</summary>
    public void Reset() { Query = ""; Page = 0; Selected = -1; Hover = -1; Menu = null; Drag = null; _press = null; }

    // ───────────────────────────── teclado
    public LpCommand Key(int vk)
    {
        if (Menu is not null && vk == 0x1B) { Menu = null; return LpCommand.Redraw; }
        Menu = null;
        int n = PageItems.Count, cols = Geometry.Cols;
        switch (vk)
        {
            case 0x1B: if (Searching) { Query = ""; Selected = -1; return LpCommand.Redraw; } return LpCommand.Close;   // Esc
            case 0x0D: { var items = PageItems; int i = Selected >= 0 ? Selected : Searching ? 0 : -1; return i >= 0 && i < items.Count ? new LpCommand(LpCommandKind.Launch, items[i]) : LpCommand.None; }
            case 0x08: if (!Searching) return LpCommand.None; Query = Query[..^1]; Selected = Searching ? 0 : -1; return LpCommand.Redraw;   // Retroceso
            case 0x21: return GoPage(Page - 1);   // Re Pág
            case 0x22: return GoPage(Page + 1);   // Av Pág
            case 0x24: Selected = n > 0 ? 0 : -1; return LpCommand.Redraw;   // Inicio
            case 0x23: Selected = n - 1; return LpCommand.Redraw;            // Fin
            case 0x25: // ←
                if (Selected < 0) { Selected = n > 0 ? 0 : -1; return LpCommand.Redraw; }
                if (Selected % cols == 0 && Page > 0 && !Searching) { Page--; Selected = Math.Min(PageItems.Count - 1, Selected + cols - 1); return LpCommand.Redraw; }
                Selected = Math.Max(0, Selected - 1); return LpCommand.Redraw;
            case 0x27: // →
                if (Selected < 0) { Selected = n > 0 ? 0 : -1; return LpCommand.Redraw; }
                if ((Selected % cols == cols - 1 || Selected == n - 1) && Page < PageCount - 1 && !Searching) { Page++; Selected = Math.Min(PageItems.Count - 1, Selected - Selected % cols); return LpCommand.Redraw; }
                Selected = Math.Min(n - 1, Selected + 1); return LpCommand.Redraw;
            case 0x26: if (Selected < 0) Selected = n > 0 ? 0 : -1; else if (Selected - cols >= 0) Selected -= cols; return LpCommand.Redraw;   // ↑
            case 0x28: if (Selected < 0) Selected = n > 0 ? 0 : -1; else if (Selected + cols < n) Selected += cols; return LpCommand.Redraw;  // ↓
        }

        return LpCommand.None;
    }

    /// <summary>Texto escrito (WM_CHAR): empieza o sigue la búsqueda, como en macOS.</summary>
    public LpCommand Char(char c)
    {
        if (char.IsControl(c) || Query.Length >= 64) return LpCommand.None;
        if (c == ' ' && !Searching) return LpCommand.None;
        Menu = null; Query += c; Page = 0; Selected = 0; return LpCommand.Redraw;
    }

    private LpCommand GoPage(int p)
    {
        p = Math.Clamp(p, 0, PageCount - 1); if (p == Page) return LpCommand.None;
        Page = p; Selected = Selected >= 0 ? Math.Min(Selected, PageItems.Count - 1) : -1; return LpCommand.Redraw;
    }

    /// <summary>Rueda: una muesca hacia abajo (negativa) = página siguiente.</summary>
    public LpCommand Wheel(int delta) => Menu is not null ? LpCommand.None : GoPage(Page + (delta < 0 ? 1 : -1));

    // ───────────────────────────── ratón
    public int CellAt(double x, double y)
    {
        var cells = Geometry.Cells; int n = PageItems.Count;
        for (int i = 0; i < Math.Min(n, cells.Count); i++) if (cells[i].Contains(x, y)) return i;
        return -1;
    }

    public LpCommand PointerMove(double x, double y)
    {
        if (Menu is { } m) { int mh = -1; for (int i = 0; i < m.Items.Count; i++) if (m.ItemRect(i).Contains(x, y)) mh = i; if (mh == MenuHover) return LpCommand.None; MenuHover = mh; return LpCommand.Redraw; }
        if (_press is { } p && !Searching && Drag is null && (Math.Abs(x - p.X) > 8 || Math.Abs(y - p.Y) > 8) && p.Index < PageItems.Count) Drag = (PageItems[p.Index], x, y);
        if (Drag is { } d) { Drag = (d.App, x, y); Hover = CellAt(x, y); return LpCommand.Redraw; }
        int h = CellAt(x, y); if (h == Hover) return LpCommand.None; Hover = h; return LpCommand.Redraw;
    }

    public LpCommand PointerDown(double x, double y)
    {
        if (Menu is not null) return LpCommand.None;   // el menú se resuelve al soltar
        int i = CellAt(x, y); _press = i >= 0 ? (i, x, y) : null; return LpCommand.None;
    }

    public LpCommand PointerUp(double x, double y)
    {
        var press = _press; _press = null;
        if (Menu is { } m)
        {
            Menu = null; MenuHover = -1;
            for (int i = 0; i < m.Items.Count; i++) if (m.ItemRect(i).Contains(x, y)) return MenuCommand(m.Items[i].Id, m.Target);
            return LpCommand.Redraw;
        }

        if (Drag is { } d)
        {
            Drag = null; int target = CellAt(x, y);
            if (target < 0) { var cells = Geometry.Cells; target = cells.Count == 0 ? 0 : Enumerable.Range(0, Math.Min(cells.Count, PageItems.Count)).OrderBy(c => Dist(cells[c], x, y)).FirstOrDefault(); }
            return new LpCommand(LpCommandKind.Move, d.App, Page * Geometry.PerPage + target);
        }

        int cell = CellAt(x, y);
        if (press is { } p && cell >= 0 && cell == p.Index) return new LpCommand(LpCommandKind.Launch, PageItems[cell]);
        if (cell >= 0) return LpCommand.None;
        for (int i = 0; i < PageCount && PageCount > 1; i++) if (Geometry.Dot(i, PageCount).Contains(x, y)) return GoPage(i);
        if (Geometry.Search.Contains(x, y) || Geometry.Panel.Contains(x, y)) return LpCommand.None;   // inside the panel but on no icon: nothing
        return LpCommand.Close;   // clic fuera del panel: se cierra, como en macOS
    }

    public LpCommand RightClick(double x, double y)
    {
        Drag = null; _press = null; int cell = CellAt(x, y);
        var items = new List<LpMenuItem>(); InstalledApp? target = null;
        if (cell >= 0)
        {
            target = PageItems[cell]; items.Add(new("open", "Abrir"));
            if (!Searching) items.Add(new("front", "Mover al principio"));
            items.Add(new("hide", "Ocultar de Launchpad"));
        }
        else
        {
            if (HiddenCount > 0) items.Add(new("unhide", $"Mostrar apps ocultas ({HiddenCount})"));
            items.Add(new("reset", "Restablecer el orden"));
        }

        double h = 12 + items.Count * LpMenu.ItemH, mx = Math.Min(x, Geometry.Width - LpMenu.W - 8), my = Math.Min(y, Geometry.Height - h - 8);
        Menu = new LpMenu(new LpRect(Math.Max(8, mx), Math.Max(8, my), LpMenu.W, h), items, target); MenuHover = -1;
        return LpCommand.Redraw;
    }

    private static LpCommand MenuCommand(string id, InstalledApp? app) => id switch
    {
        "open" when app is not null => new LpCommand(LpCommandKind.Launch, app),
        "front" when app is not null => new LpCommand(LpCommandKind.Move, app, 0),
        "hide" when app is not null => new LpCommand(LpCommandKind.Hide, app),
        "unhide" => new LpCommand(LpCommandKind.UnhideAll),
        "reset" => new LpCommand(LpCommandKind.ResetOrder),
        _ => LpCommand.Redraw,
    };

    private void ClampSelection() { if (Selected >= PageItems.Count) Selected = PageItems.Count - 1; }

    private static double Dist(LpRect r, double x, double y) { double dx = r.Cx - x, dy = r.Cy - y; return dx * dx + dy * dy; }
}
