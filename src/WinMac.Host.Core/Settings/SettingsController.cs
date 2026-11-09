namespace WinMac.Host;

/// <summary>
/// Comportamiento de la ventana de Ajustes (sin Windows): página activa, desplazamiento, resalte, clic (pulsar y soltar sobre lo mismo), deslizador arrastrable y lista de sugerencias bajo un campo.
/// No cambia ninguna configuración: devuelve <see cref="SettingsEdit"/> y la ventana los aplica con <see cref="SettingsActions"/>.
/// </summary>
public sealed class SettingsController
{
    private IReadOnlyList<SettingsPage> _pages = [];
    private int _page; private double _scroll;
    private string? _downId, _downPart; private int _downSide = -1; private bool _drag; private double _dragValue;
    public PageFrame Frame { get; private set; } = new([], [], 0, 0, 0);
    public (string Id, string Part)? Hover { get; private set; }
    public int HoverSide { get; private set; } = -1;
    public IReadOnlyList<SettingsPage> Pages => _pages;
    public int PageIndex => _page;
    public SettingsPage? Page => _page >= 0 && _page < _pages.Count ? _pages[_page] : null;
    /// <summary>Valor que se está arrastrando en un deslizador (la configuración se guarda solo al soltar).</summary>
    public (string Id, double Value)? Dragging => _drag && _downId is not null ? (_downId, _dragValue) : null;

    // ── Sugerencias (autocompletar)
    public string? SuggestFor { get; private set; }
    public IReadOnlyList<string> Suggestions { get; private set; } = [];
    public int SuggestIndex { get; private set; } = -1;
    /// <summary>Con una lista de elecciones (desplegable), cuál es la actual (se dibuja con una marca); -1 = lista sin marcas (sugerencias de ciudad).</summary>
    public int SuggestChecked { get; private set; } = -1;
    public bool SuggestHasChecks { get; private set; }
    public void SetSuggestions(string fieldId, IReadOnlyList<string> labels) { SuggestFor = labels.Count == 0 ? null : fieldId; Suggestions = labels; SuggestIndex = -1; SuggestChecked = -1; SuggestHasChecks = false; }
    /// <summary>Abre un desplegable de elecciones bajo la fila <paramref name="rowId"/> con la elección actual marcada.</summary>
    public void OpenDropdown(string rowId, IReadOnlyList<string> labels, int current) { SetSuggestions(rowId, labels); SuggestChecked = current; SuggestHasChecks = true; SuggestIndex = current; }
    public void ClearSuggestions() { SuggestFor = null; Suggestions = []; SuggestIndex = -1; SuggestChecked = -1; SuggestHasChecks = false; }
    public void MoveSuggestion(int delta) { if (Suggestions.Count == 0) return; SuggestIndex = Math.Clamp(SuggestIndex + delta, 0, Suggestions.Count - 1); }

    public void SetPages(IReadOnlyList<SettingsPage> pages)
    {
        string? keep = Page?.Id; _pages = pages; int i = keep is null ? 0 : pages.ToList().FindIndex(p => p.Id == keep); _page = i < 0 ? 0 : i; Recompute();
    }

    public void SelectPage(int i) { if (i < 0 || i >= _pages.Count || i == _page) return; _page = i; _scroll = 0; ClearSuggestions(); Hover = null; Recompute(); }
    public void SelectPage(string id) => SelectPage(_pages.ToList().FindIndex(p => p.Id == id));
    private void Recompute() { var p = Page; Frame = p is null ? new([], [], 0, 0, 0) : SettingsLayout.Compute(p, _scroll); _scroll = Frame.Scroll; }

    public bool Scroll(double deltaDip) { double before = _scroll; _scroll = Math.Clamp(before + deltaDip, 0, Frame.MaxScroll); Recompute(); return Math.Abs(_scroll - before) > 0.01; }
    public double ScrollOffset => _scroll;

    /// <summary>Fila de un campo por id (para colocar el campo de texto nativo encima y la lista de sugerencias).</summary>
    public RowFrame? RowOf(string id) => Frame.Rows.FirstOrDefault(r => r.Row.Id == id);

    private static (double lx, double ly) Local(double x, double y) => (x - SettingsLayout.SidebarW, y - SettingsLayout.TitleH);
    private bool InContent(double x, double y) => x >= SettingsLayout.SidebarW && y >= SettingsLayout.TitleH;

    private int SideHit(double x, double y)
    {
        if (x >= SettingsLayout.SidebarW) return -1;
        foreach (var e in SettingsLayout.SideEntries(_pages)) { int i = e.PageIndex; var r = e.Rect; if (x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return i; }
        return -1;
    }

    private int SuggestHit(double x, double y)
    {
        if (SuggestFor is null || Suggestions.Count == 0 || RowOf(SuggestFor) is not { } row) return -1;
        var (lx, ly) = Local(x, y); var r = SettingsLayout.SuggestRect(row, Suggestions.Count);
        if (lx < r.X || lx >= r.X + r.W || ly < r.Y + 4 || ly >= r.Y + r.H - 4) return -1;
        return Math.Clamp((int)((ly - r.Y - 4) / SettingsLayout.SuggestRowH), 0, Suggestions.Count - 1);
    }

    public void PointerLeave() { Hover = null; HoverSide = -1; }

    /// <summary>Devuelve true si algo visible cambió.</summary>
    public bool PointerMove(double x, double y)
    {
        if (_drag && _downId is not null && RowOf(_downId) is { } sr) { var (lx, _) = Local(x, y); double v = SettingsLayout.SliderValueAt(sr, lx); bool ch = Math.Abs(v - _dragValue) > 0.4; _dragValue = v; return ch; }
        var old = (Hover, HoverSide, SuggestIndex);
        HoverSide = SideHit(x, y); Hover = null;
        int sg = SuggestHit(x, y); if (sg >= 0) SuggestIndex = sg;
        else if (InContent(x, y) && y < SettingsLayout.WinH) { var (lx, ly) = Local(x, y); if (SettingsLayout.Hit(Frame, lx, ly) is { } h && h.Row.Row.Enabled) Hover = (h.Row.Row.Id, h.Part); }
        return old != (Hover, HoverSide, SuggestIndex);
    }

    /// <summary>Pulsar: la barra lateral y las filas actúan al SOLTAR sobre lo mismo; el deslizador empieza a arrastrar; una sugerencia se elige al pulsar (antes de que el campo pierda el foco).</summary>
    public SettingsEdit? PointerDown(double x, double y)
    {
        _downId = null; _downPart = null; _downSide = -1; _drag = false;
        int sg = SuggestHit(x, y); if (sg >= 0) { var field = SuggestFor!; return new SettingsEdit(field, "suggest", Index: sg); }
        if (SuggestFor is not null) ClearSuggestions();
        _downSide = SideHit(x, y); if (_downSide >= 0) return null;
        if (!InContent(x, y)) return null;
        var (lx, ly) = Local(x, y); if (SettingsLayout.Hit(Frame, lx, ly) is not { } hit || !hit.Row.Row.Enabled) return null;
        _downId = hit.Row.Row.Id; _downPart = hit.Part;
        if (hit.Part == "slider") { _drag = true; _dragValue = SettingsLayout.SliderValueAt(hit.Row, lx); }
        if (hit.Part == "field") return new SettingsEdit(_downId, "focus");
        return null;
    }

    public SettingsEdit? PointerUp(double x, double y)
    {
        int side = _downSide; string? id = _downId, part = _downPart; bool drag = _drag; _downId = null; _downPart = null; _downSide = -1; _drag = false;
        if (side >= 0) { if (SideHit(x, y) == side) SelectPage(side); return null; }
        if (id is null) return null;
        if (drag) return new SettingsEdit(id, "slider", Number: _dragValue);
        var (lx, ly) = Local(x, y); if (SettingsLayout.Hit(Frame, lx, ly) is not { } hit || hit.Row.Row.Id != id || hit.Part != part) return null;   // soltar fuera cancela
        var row = hit.Row.Row;
        if (part == "switch") return new SettingsEdit(id, "toggle", On: !row.On);
        if (part is "up" or "down") return new SettingsEdit(id, part);
        if (part!.StartsWith("seg:", StringComparison.Ordinal)) return new SettingsEdit(id, "segment", Index: int.Parse(part[4..]));
        if (part == "button") return new SettingsEdit(id, "button");
        return null;
    }
}
