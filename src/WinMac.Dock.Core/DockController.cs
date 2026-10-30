namespace WinMac.Dock;

public readonly record struct PointerSample(double X, double Y);

public sealed record ItemView(int Index, DockItemModel Model, double X, double Size, double Scale, double Lift);

/// <summary>Instantánea que el render dibuja. Coordenadas en DIP relativas a la ventana del dock.</summary>
public sealed record DockView(
    ItemView[] Items, double WindowWidth, double WindowHeight, double BarLeft, double BarWidth, double BarHeight,
    double HideT, int HoverIndex, bool NeedsFrames, bool Suppressed);

/// <summary>
/// Núcleo del comportamiento del Dock: modelo, magnificación con resortes, ocultamiento automático, reglas de clic y salto de lanzamiento.
/// No conoce Windows ni ninguna tecnología de render: es 100 % probable en CI/Linux.
/// </summary>
public sealed class DockController
{
    private readonly DockMetrics _m; private readonly DockConfig _cfg; private readonly DockAnimator _anim;
    private IReadOnlyList<DockItemModel> _items = [];
    private PointerSample? _pointer; private bool _edgeHot, _suppressed;
    private double _outside, _edge;
    private bool _hiddenTarget;
    private double _windowWidth;

    public DockController(DockMetrics m, DockConfig cfg) { _m = m; _cfg = cfg; _anim = new DockAnimator(m); }

    public DockMetrics Metrics => _m;
    public IReadOnlyList<DockItemModel> Items => _items;
    public double WindowWidth => _windowWidth;
    public double WindowHeight => _m.WindowHeight;

    public void SetItems(IReadOnlyList<DockItemModel> items)
    {
        _items = items; _anim.Sync(items); _windowWidth = DockLayout.WindowWidth(_m, items);
    }

    public void SetPointer(PointerSample? p) => _pointer = p;
    public void SetEdgeHot(bool hot) => _edgeHot = hot;
    /// <summary>Un juego/app a pantalla completa pide que el dock se aparte; no se vuelve a mostrar hasta que se libere.</summary>
    public void SetSuppressed(bool s) => _suppressed = s;
    public void Bounce(string id) => _anim.Bounce(id);

    private double BarTop => _m.WindowHeight - _m.ShadowMargin - _m.BarHeight;
    private bool InZone(PointerSample p, double barLeft, double barWidth) =>
        p.Y >= BarTop - (_m.MaxScale - 1) * _m.BaseSize * 0.6 && p.Y <= _m.WindowHeight &&
        p.X >= barLeft - 8 && p.X <= barLeft + barWidth + 8;

    public DockView Step(double dt)
    {
        double rest = DockLayout.RestWidth(_m, _items);
        double restLeft = (_windowWidth - rest) / 2;
        var cur = DockLayout.Compute(_m, _items, _anim.Scales(_items));
        double barLeft = (_windowWidth - cur.BarWidth) / 2;
        bool inZone = _pointer is PointerSample p0 && !_hiddenTarget && InZone(p0, barLeft, cur.BarWidth);

        // Magnificación: el objetivo se calcula en coordenadas de reposo; los resortes suavizan.
        double? restX = inZone && _cfg.MagnificationEnabled ? _pointer!.Value.X - restLeft : null;
        _anim.SetTargets(_items, DockLayout.TargetScales(_m, _items, restX));

        // Ocultamiento automático
        if (_suppressed) _hiddenTarget = true;
        else if (_cfg.AutoHide)
        {
            if (_hiddenTarget)
            {
                _edge = _edgeHot ? _edge + dt : 0;
                if (_edge >= _cfg.ShowDelaySeconds) { _hiddenTarget = false; _outside = 0; _edge = 0; }
            }
            else
            {
                bool over = _edgeHot || (_pointer is PointerSample p && InZone(p, barLeft, cur.BarWidth)); // el borde caliente cuenta como «encima»
                _outside = over ? 0 : _outside + dt;
                if (_outside >= _cfg.HideDelaySeconds) _hiddenTarget = true;
            }
        }
        else _hiddenTarget = false;
        _anim.SetHideTarget(_hiddenTarget ? 1 : 0);
        _anim.Step(dt);

        var frame = DockLayout.Compute(_m, _items, _anim.Scales(_items));
        double left = (_windowWidth - frame.BarWidth) / 2;
        var views = new ItemView[_items.Count];
        for (int i = 0; i < _items.Count; i++)
        {
            var f = frame.Items[i];
            views[i] = new ItemView(i, _items[i], left + f.X, f.Size, f.Scale, _anim.Lift(_items[i].Id));
        }
        int hover = inZone && _pointer is PointerSample pp ? DockLayout.HitTestX(frame, _items, pp.X - left) : -1;
        bool needs = !_anim.IsSettled || inZone || (_cfg.AutoHide && !_hiddenTarget);
        return new DockView(views, _windowWidth, _m.WindowHeight, left, frame.BarWidth, _m.BarHeight, _anim.HideT, hover, needs, _suppressed);
    }

    /// <summary>Convierte un clic en una acción para el adaptador de Windows (el núcleo no ejecuta nada).</summary>
    public DockAction OnClick(int index)
    {
        if ((uint)index >= (uint)_items.Count) return new DockAction(DockActionKind.None);
        var it = _items[index];
        switch (it.Kind)
        {
            case DockItemKind.Folder:
            case DockItemKind.Trash: return new DockAction(DockActionKind.Open, it.Id, it.Path);
            case DockItemKind.App when !it.Running:
                _anim.Bounce(it.Id);
                return new DockAction(DockActionKind.Launch, it.Id, it.Path);
            case DockItemKind.App when it.AllMinimized: return new DockAction(DockActionKind.Restore, it.Id, it.Path);
            case DockItemKind.App when it.Active && _cfg.ClickActiveMinimizes: return new DockAction(DockActionKind.Minimize, it.Id, it.Path);
            case DockItemKind.App when !it.Active: return new DockAction(DockActionKind.Activate, it.Id, it.Path);
            default: return new DockAction(DockActionKind.None, it.Id);
        }
    }
}
