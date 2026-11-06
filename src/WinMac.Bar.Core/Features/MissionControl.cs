using System.Globalization;
using System.Text;

namespace WinMac.Bar.Features;

public readonly record struct McRect(double X, double Y, double W, double H)
{
    public double Right => X + W; public double Bottom => Y + H;
    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
}

/// <summary>Una ventana tal como la ve Mission Control. <see cref="Monitor"/> = índice del monitor donde está (por su centro). <see cref="Aspect"/> = ancho/alto de su última captura (0 = desconocida).</summary>
public sealed record McWindow(long Hwnd, string Title, string App, bool Minimized, bool Active, int Monitor, double Aspect);

public sealed record McCard(McWindow Win, int Index, McRect Card, McRect Thumb, McRect Label, McRect Close);

public enum McCmdKind { None, Redraw, Close, Activate, CloseWindow }
public readonly record struct McCommand(McCmdKind Kind, long Hwnd = 0)
{
    public static readonly McCommand None = new(McCmdKind.None), Redraw = new(McCmdKind.Redraw), Close = new(McCmdKind.Close);
}

/// <summary>Cuadrícula de tarjetas de Mission Control (pura). Elige las columnas que dan las miniaturas más grandes y centra la última fila.</summary>
public static class McLayout
{
    public const double Margin = 56, Gap = 28, LabelH = 30, SearchTop = 28, SearchH = 44, MaxThumbW = 460, DefaultAspect = 1.6;
    public static double ContentTop => SearchTop + SearchH + 36;

    public static (IReadOnlyList<McCard> Cards, int Cols) Compute(IReadOnlyList<(McWindow W, int Index)> items, double width, double height)
    {
        int n = items.Count; if (n == 0) return ([], 1);
        double aw = Math.Max(100, width - 2 * Margin), ah = Math.Max(100, height - ContentTop - Margin);
        int bestCols = 1; double bestScale = -1;
        for (int cols = 1; cols <= n; cols++)
        {
            int rows = (n + cols - 1) / cols;
            double cw = (aw - (cols - 1) * Gap) / cols, ch = (ah - (rows - 1) * Gap) / rows - LabelH;
            if (cw < 40 || ch < 30) break;
            double s = Math.Min(cw / DefaultAspect, ch);                       // alto de miniatura posible con la proporción típica
            if (s > bestScale + 0.5) { bestScale = s; bestCols = cols; }
        }

        if (bestScale < 0) bestCols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));
        int c = bestCols, r = (n + c - 1) / c;
        double cellW = Math.Max(40, (aw - (c - 1) * Gap) / c), cellH = Math.Max(30, (ah - (r - 1) * Gap) / r - LabelH);
        var cards = new List<McCard>(n);
        for (int row = 0; row < r; row++)
        {
            int inRow = Math.Min(c, n - row * c);
            double rowW = inRow * cellW + (inRow - 1) * Gap, x0 = Margin + (aw - rowW) / 2, y0 = ContentTop + row * (cellH + LabelH + Gap);
            for (int k = 0; k < inRow; k++)
            {
                var (w, idx) = items[row * c + k]; double a = w.Aspect > 0.2 && w.Aspect < 8 ? w.Aspect : DefaultAspect;
                double tw = Math.Min(cellW, cellH * a), th = tw / a; if (tw > MaxThumbW) { tw = MaxThumbW; th = tw / a; }
                double cx = x0 + k * (cellW + Gap) + cellW / 2, ty = y0 + (cellH - th) / 2;
                var thumb = new McRect(cx - tw / 2, ty, tw, th); var label = new McRect(cx - cellW / 2, ty + th + 6, cellW, LabelH - 6);
                var card = new McRect(Math.Min(thumb.X, label.X) - 6, thumb.Y - 6, Math.Max(thumb.W, 0) + 12, th + LabelH + 6);
                card = new McRect(thumb.X - 6, thumb.Y - 6, thumb.W + 12, thumb.H + LabelH + 6);
                cards.Add(new McCard(w, idx, card, thumb, label, new McRect(thumb.Right - 26, thumb.Y + 6, 20, 20)));
            }
        }

        return (cards, c);
    }
}

/// <summary>Estado y entrada de Mission Control (puro, se prueba en Linux): búsqueda, selección con flechas, clic para activar, cerrar una ventana. Windows solo traduce mensajes y dibuja.</summary>
public sealed class McController
{
    private IReadOnlyList<McWindow> _all = [];
    private IReadOnlyList<McWindow> _filtered = [];
    private readonly Dictionary<int, int> _cols = [];
    private int _hover = -1; private bool _hoverClose;

    public string Query { get; private set; } = "";
    public int Selected { get; private set; } = -1;
    public IReadOnlyList<McWindow> Filtered => _filtered;
    public int HoverIndex => _hover;
    public bool HoverClose => _hoverClose;
    public int Total => _all.Count;

    public void SetWindows(IReadOnlyList<McWindow> wins)
    {
        long? keep = Selected >= 0 && Selected < _filtered.Count ? _filtered[Selected].Hwnd : null;
        _all = wins; Refilter();
        Selected = keep is { } h ? Math.Max(-1, IndexOf(h)) : (Query.Length > 0 && _filtered.Count > 0 ? 0 : -1);
    }

    private int IndexOf(long hwnd) { for (int i = 0; i < _filtered.Count; i++) if (_filtered[i].Hwnd == hwnd) return i; return -1; }

    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    public static bool Matches(McWindow w, string query)
    {
        var q = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries); if (q.Length == 0) return true;
        var hay = Fold(w.App + " " + w.Title); return q.All(t => hay.Contains(t, StringComparison.Ordinal));
    }

    private void Refilter() { _filtered = Query.Length == 0 ? _all : _all.Where(w => Matches(w, Query)).ToList(); _cols.Clear(); }

    /// <summary>Tarjetas de un monitor (las ventanas que están en él, en el orden global de la lista filtrada).</summary>
    public IReadOnlyList<McCard> CardsFor(int monitor, double width, double height)
    {
        var items = new List<(McWindow, int)>(); for (int i = 0; i < _filtered.Count; i++) if (_filtered[i].Monitor == monitor) items.Add((_filtered[i], i));
        var (cards, cols) = McLayout.Compute(items, width, height); _cols[monitor] = cols; return cards;
    }

    public McCommand Char(char c)
    {
        if (char.IsControl(c)) return McCommand.None;
        Query += c; Refilter(); Selected = _filtered.Count > 0 ? 0 : -1; return McCommand.Redraw;
    }

    public McCommand Key(int vk)
    {
        switch (vk)
        {
            case 0x1B: if (Query.Length > 0) { Query = ""; Refilter(); Selected = -1; return McCommand.Redraw; } return McCommand.Close;                         // Esc: primero borra la búsqueda
            case 0x08: if (Query.Length == 0) return McCommand.None; Query = Query[..^1]; Refilter(); Selected = Query.Length > 0 && _filtered.Count > 0 ? 0 : -1; return McCommand.Redraw;
            case 0x0D: { int i = Selected >= 0 ? Selected : (_filtered.Count == 1 ? 0 : -1); return i >= 0 && i < _filtered.Count ? new McCommand(McCmdKind.Activate, _filtered[i].Hwnd) : McCommand.None; }
            case 0x2E: return Selected >= 0 && Selected < _filtered.Count ? new McCommand(McCmdKind.CloseWindow, _filtered[Selected].Hwnd) : McCommand.None;   // Supr cierra la ventana seleccionada
            case 0x25 or 0x27 or 0x09 or 0x26 or 0x28:
                if (_filtered.Count == 0) return McCommand.None;
                if (Selected < 0) { Selected = 0; return McCommand.Redraw; }
                int cols = _cols.TryGetValue(_filtered[Selected].Monitor, out var c) ? c : 1;
                int step = vk switch { 0x25 => -1, 0x27 or 0x09 => 1, 0x26 => -cols, _ => cols };
                int next = Selected + step; if (next < 0 || next >= _filtered.Count) next = Math.Clamp(next, 0, _filtered.Count - 1);
                if (next == Selected) return McCommand.None; Selected = next; return McCommand.Redraw;
        }

        return McCommand.None;
    }

    private bool Hit(int monitor, IReadOnlyList<McCard> cards, double x, double y, out McCard card)
    {
        foreach (var c in cards) if (c.Card.Contains(x, y)) { card = c; return true; }
        card = null!; return false;
    }

    public McCommand PointerMove(IReadOnlyList<McCard> cards, double x, double y)
    {
        int h = -1; bool close = false;
        foreach (var c in cards) if (c.Card.Contains(x, y)) { h = c.Index; close = c.Close.Contains(x, y); break; }
        if (h == _hover && close == _hoverClose) return McCommand.None;
        _hover = h; _hoverClose = close; if (h >= 0) Selected = h; return McCommand.Redraw;
    }

    /// <summary>Clic: sobre la «×» cierra esa ventana; sobre una tarjeta la activa; sobre el fondo cierra Mission Control.</summary>
    public McCommand Click(IReadOnlyList<McCard> cards, double x, double y)
    {
        foreach (var c in cards)
            if (c.Card.Contains(x, y)) return c.Close.Contains(x, y) ? new McCommand(McCmdKind.CloseWindow, c.Win.Hwnd) : new McCommand(McCmdKind.Activate, c.Win.Hwnd);
        return McCommand.Close;
    }

    /// <summary>Una ventana se cerró desde aquí: se quita de la vista al instante (el siguiente refresco la confirma).</summary>
    public void Forget(long hwnd) { _all = _all.Where(w => w.Hwnd != hwnd).ToList(); Refilter(); if (Selected >= _filtered.Count) Selected = _filtered.Count - 1; _hover = -1; }

    public void Reset() { Query = ""; _all = []; _filtered = []; Selected = -1; _hover = -1; _hoverClose = false; _cols.Clear(); }
}
