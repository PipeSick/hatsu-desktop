namespace WinMac.Bar;

/// <summary>Lo que el render dibuja: distribución, elemento bajo el puntero y opacidad (0–1) del resalte de cada elemento.</summary>
public sealed record BarView(BarFrame Frame, BarState State, int HoverIndex, IReadOnlyList<double> HoverAlpha, bool NeedsFrames, string? DragId = null, double? DropX = null);

/// <summary>Comportamiento de la barra (sin Windows): estado → elementos → distribución, resalte con suavizado, y clic → acción. El núcleo no ejecuta nada.</summary>
public sealed class BarController
{
    private readonly BarMetrics _m; private BarConfig _cfg; private BarState _state; private BarFrame _frame = new([], 0, 0);
    private double[] _alpha = []; private int _hover = -1; private double _width;
    private Func<string, bool, double> _measure = (t, _) => t.Length * 7;

    public BarController(BarMetrics m, BarConfig cfg, BarState initial) { _m = m; _cfg = cfg; _state = initial; }

    public BarMetrics Metrics => _m;
    public BarConfig Config { get => _cfg; set { _cfg = value; Relayout(); } }
    public BarState State { get => _state; set { _state = value; Relayout(); } }
    /// <summary>Medida de texto del render (ancho en DIP).</summary>
    public Func<string, bool, double> Measure { set { _measure = value; Relayout(); } }

    public void SetWidth(double width) { _width = width; Relayout(); }

    private void Relayout()
    {
        if (_width <= 0) return;
        var (l, r) = BarModel.Build(_state, _cfg);
        string? hoverId = _hover >= 0 && _hover < _frame.Items.Count ? _frame.Items[_hover].Item.Id : null;
        _frame = BarLayout.Compute(_m, _cfg, _width, l, r, _measure);
        var old = _alpha; _alpha = new double[_frame.Items.Count];
        for (int i = 0; i < _alpha.Length; i++) { int j = FindById(oldFrameIds, _frame.Items[i].Item.Id); _alpha[i] = j >= 0 && j < old.Length ? old[j] : 0; }
        oldFrameIds = _frame.Items.Select(f => f.Item.Id).ToArray();
        _hover = hoverId is null ? -1 : _frame.Items.ToList().FindIndex(f => f.Item.Id == hoverId);
    }

    private string[] oldFrameIds = [];
    private static int FindById(string[] ids, string id) => Array.IndexOf(ids, id);

    /// <summary>Posición del puntero dentro de la barra (DIP) o null si está fuera. Solo los elementos clicables se resaltan.</summary>
    public void SetPointer(double? x, double? y)
    {
        int i = x is double px && y is double py && py >= 0 && py <= _m.Height ? BarLayout.HitTest(_frame, px) : -1;
        _hover = i >= 0 && BarModel.IsClickable(_frame.Items[i].Item.Kind) ? i : -1;
    }

    public BarView Step(double dt)
    {
        bool moving = false; double k = 1 - Math.Exp(-dt / 0.06);
        for (int i = 0; i < _alpha.Length; i++)
        {
            double target = i == _hover ? 1 : 0; double d = target - _alpha[i];
            if (Math.Abs(d) < 0.01) _alpha[i] = target; else { _alpha[i] += d * k; moving = true; }
        }
        return new BarView(_frame, _state, _hover, _alpha.ToArray(), moving, _dragging ? _pressId : null, _dragging ? DropIndicatorX() : null);
    }

    /// <summary>Acción del elemento bajo X (clic izquierdo). Null/None si no hay elemento clicable.</summary>
    public BarAction OnClick(double x)
    {
        int i = BarLayout.HitTest(_frame, x);
        return i < 0 ? new BarAction(BarActionKind.None) : BarModel.ClickAction(_frame.Items[i].Item, _cfg.NotificationCenter);
    }

    /// <summary>Acción del clic derecho en X: el menú de la app en un icono de bandeja; si no, el menú de WinMac.</summary>
    public BarAction OnRightClick(double x) { int i = BarLayout.HitTest(_frame, x); return BarModel.RightClickAction(i < 0 ? null : _frame.Items[i].Item); }

    public BarFrame Frame => _frame;

    // ── It7: reordenar con el ratón. Pulsar y arrastrar un módulo propio movible; un clic sin mover es un clic normal.
    public const double DragThreshold = 5;
    private string? _pressId; private double _pressX; private bool _dragging; private int _gap;
    public bool Dragging => _dragging;

    private List<BarItemFrame> MovableFrames() => _frame.Items.Where(f => BarOrdering.IsMovable(f.Item)).ToList();

    /// <summary>Botón pulsado en X: recuerda el módulo (si es movible) por si empieza un arrastre.</summary>
    public void PressAt(double x)
    {
        _dragging = false; _pressId = null; _pressX = x;
        int i = BarLayout.HitTest(_frame, x);
        if (i >= 0 && BarOrdering.IsMovable(_frame.Items[i].Item)) _pressId = _frame.Items[i].Item.Id;
    }

    /// <summary>Movimiento con el botón pulsado. Devuelve true mientras se está arrastrando (el clic final debe ignorarse).</summary>
    public bool DragTo(double x)
    {
        if (_pressId is null) return false;
        if (!_dragging && Math.Abs(x - _pressX) < DragThreshold) return false;
        _dragging = true; _gap = BarOrdering.GapAt(MovableFrames(), x); return true;
    }

    /// <summary>Suelta el botón. Devuelve el orden nuevo a guardar si hubo un arrastre que cambia algo; null si fue un clic o no cambia nada.</summary>
    public string? Release(double x, out bool wasDrag)
    {
        wasDrag = _dragging; string? result = null;
        if (_dragging && _pressId is { } id)
        {
            var visible = MovableFrames().Select(f => f.Item.Id).ToList();
            var after = BarOrdering.Move(visible, id, BarOrdering.GapAt(MovableFrames(), x));
            if (!after.SequenceEqual(visible)) result = BarOrdering.Merge(_cfg.BarOrder, after);
        }
        _dragging = false; _pressId = null; return result;
    }

    public void CancelDrag() { _dragging = false; _pressId = null; }

    private double? DropIndicatorX()
    {
        var mv = MovableFrames(); if (mv.Count == 0) return null;
        return _gap <= 0 ? mv[0].X - _m.ItemGap / 2 : _gap >= mv.Count ? mv[^1].X + mv[^1].W + _m.ItemGap / 2 : mv[_gap].X - _m.ItemGap / 2;
    }

    /// <summary>Tooltip del elemento bajo X (null si no tiene).</summary>
    public string? TipAt(double x) { int i = BarLayout.HitTest(_frame, x); return i < 0 ? null : _frame.Items[i].Item.Tip; }

    /// <summary>Elemento (Id) bajo X, para que el host ancle un menú junto a él. Null si no hay.</summary>
    public BarItemFrame? ItemAt(double x) { int i = BarLayout.HitTest(_frame, x); return i < 0 ? null : _frame.Items[i]; }
}
