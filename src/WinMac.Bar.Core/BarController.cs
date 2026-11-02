namespace WinMac.Bar;

/// <summary>Lo que el render dibuja: distribución, elemento bajo el puntero y opacidad (0–1) del resalte de cada elemento.</summary>
public sealed record BarView(BarFrame Frame, BarState State, int HoverIndex, IReadOnlyList<double> HoverAlpha, bool NeedsFrames);

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
        return new BarView(_frame, _state, _hover, _alpha.ToArray(), moving);
    }

    /// <summary>Acción del elemento bajo X (clic izquierdo). Null/None si no hay elemento clicable.</summary>
    public BarAction OnClick(double x)
    {
        int i = BarLayout.HitTest(_frame, x);
        return i < 0 ? new BarAction(BarActionKind.None) : BarModel.ClickAction(_frame.Items[i].Item, _cfg.NotificationCenter);
    }

    /// <summary>Elemento (Id) bajo X, para que el host ancle un menú junto a él. Null si no hay.</summary>
    public BarItemFrame? ItemAt(double x) { int i = BarLayout.HitTest(_frame, x); return i < 0 ? null : _frame.Items[i]; }
}
