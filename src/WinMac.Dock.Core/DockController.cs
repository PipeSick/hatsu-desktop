using WinMac.Apps;
namespace WinMac.Dock;

public readonly record struct PointerSample(double X, double Y);

public sealed record ItemView(int Index, DockItemModel Model, double X, double Size, double Scale, double Lift);

/// <summary>Instantánea que el render dibuja. Coordenadas en DIP relativas a la ventana del dock.</summary>
public sealed record DockView(
    ItemView[] Items, double WindowWidth, double WindowHeight, double BarLeft, double BarWidth, double BarHeight,
    double HideT, int HoverIndex, bool NeedsFrames, bool Suppressed, int DragIndex = -1, PreviewView? Preview = null);

/// <summary>
/// Núcleo del comportamiento del Dock: modelo, magnificación con resortes, ocultamiento automático, reglas de clic y salto de lanzamiento.
/// No conoce Windows ni ninguna tecnología de render: es 100 % probable en CI/Linux.
/// </summary>
public sealed class DockController
{
    private readonly DockMetrics _m; private DockConfig _cfg; private readonly DockAnimator _anim;
    private IReadOnlyList<DockItemModel> _items = [];
    private PointerSample? _pointer; private bool _edgeHot, _suppressed;
    private double _outside, _edge;
    private bool _hiddenTarget;
    private double _windowWidth;
    // reordenación por arrastre
    private string? _dragId; private double _dragX; private IReadOnlyList<DockItemModel>? _dragOriginal, _pendingItems;

    private readonly PreviewController _preview;
    private bool _dragTemporary;
    /// <summary>Último arrastre terminado: true si movió una app TEMPORAL (no anclada); su orden solo dura mientras el dock está abierto.</summary>
    public bool LastDropTemporary { get; private set; }
    /// <summary>Última vista previa calculada (null si no hay).</summary>
    public PreviewView? Preview { get; private set; }
    public Func<long, double>? ThumbAspect { get => _preview.Aspect; set => _preview.Aspect = value; }
    public void DismissPreview() => _preview.Dismiss();
    /// <summary>Clic sobre una tarjeta de la vista previa (activa esa ventana). Null si el clic no fue sobre una tarjeta.</summary>
    public DockAction? PreviewClick() { var a = _preview.Click(Preview); return a; }
    public bool PreviewContains(double x, double y) => Preview is { } p && (p.Frame.Panel.Inflate(2).Contains(x, y));

    /// <summary>Configuración en vivo (alineación, autoocultado, magnificación…). El tamaño NO se aplica aquí: cambia las métricas y exige un controlador nuevo.</summary>
    public DockConfig Config { get => _cfg; set { _cfg = value; _preview.Config = value; } }

    /// <summary>X (en la ventana) del borde izquierdo de una barra del ancho dado, según la alineación: centro = centrada (como siempre); izquierda/derecha = pegada a su lado de la ventana.</summary>
    private double BarLeftFor(double barWidth) => _cfg.Alignment switch
    {
        DockAlignment.Left => _m.BarInset,
        DockAlignment.Right => _windowWidth - _m.BarInset - barWidth,
        _ => (_windowWidth - barWidth) / 2,
    };

    public DockController(DockMetrics m, DockConfig cfg) { _m = m; _cfg = cfg; _anim = new DockAnimator(m); _preview = new PreviewController(m, cfg); }

    public DockMetrics Metrics => _m;
    public IReadOnlyList<DockItemModel> Items => _items;
    public double WindowWidth => _windowWidth;
    public double WindowHeight => _m.WindowHeight;

    public void SetItems(IReadOnlyList<DockItemModel> items)
    {
        if (_dragId is not null) { _pendingItems = items; return; }   // no se cambia el modelo bajo un arrastre en curso
        _items = items; _anim.Sync(items); _windowWidth = DockLayout.WindowWidth(_m, items);
    }

    public bool IsDragging => _dragId is not null;
    /// <summary>Las apps ancladas y las temporales (abiertas, no ancladas) se pueden reordenar, cada grupo dentro de sí mismo.</summary>
    public bool CanDrag(int index) => (uint)index < (uint)_items.Count && _items[index] is { Kind: DockItemKind.App } it && (it.Pinned || it.Running);

    public bool BeginDrag(int index)
    {
        if (!CanDrag(index) || _dragId is not null) return false;
        _dragId = _items[index].Id; _dragTemporary = !_items[index].Pinned; _dragOriginal = _items; _dragX = double.NaN; _preview.Dismiss(); return true;
    }

    /// <summary>Mueve el ítem arrastrado: su posición entre las ancladas sale de comparar el puntero (coordenadas de la ventana) con los centros de las demás.</summary>
    public void UpdateDrag(double pointerX)
    {
        if (_dragId is null) return; _dragX = pointerX;
        var dragged = _items.First(i => i.Id == _dragId); var rest = _items.Where(i => i.Id != _dragId).ToList();
        // Grupo en el que se puede soltar: las ancladas (prefijo) o las temporales (bloque tras «sep:recents»).
        int gs = 0, ge;
        if (_dragTemporary)
        {
            int sep = rest.FindIndex(i => i.Id == "sep:recents"); gs = sep < 0 ? rest.Count : sep + 1; ge = gs;
            while (ge < rest.Count && rest[ge] is { Kind: DockItemKind.App, Pinned: false }) ge++;
        }
        else { ge = 0; while (ge < rest.Count && rest[ge] is { Kind: DockItemKind.App, Pinned: true }) ge++; }
        var frame = DockLayout.Compute(_m, rest, null); double left = BarLeftFor(frame.BarWidth);
        int target = gs; for (int i = gs; i < ge; i++) if (left + frame.Items[i].X + frame.Items[i].Size / 2 < pointerX) target = i + 1;
        var order = new List<DockItemModel>(rest); order.Insert(target, dragged);
        if (!order.Select(x => x.Id).SequenceEqual(_items.Select(x => x.Id))) _items = order;
    }

    /// <summary>Termina el arrastre. Con <paramref name="commit"/>, devuelve el nuevo orden de Id de las ancladas (null si no cambió); si no, restaura el orden original.</summary>
    public IReadOnlyList<string>? EndDrag(bool commit)
    {
        if (_dragId is null) return null;
        var changed = !_items.Select(x => x.Id).SequenceEqual(_dragOriginal!.Select(x => x.Id));
        IReadOnlyList<string>? result = null;
        LastDropTemporary = _dragTemporary;
        if (commit && changed) result = (_dragTemporary ? _items.Where(i => i is { Kind: DockItemKind.App, Pinned: false }) : _items.TakeWhile(i => i is { Kind: DockItemKind.App, Pinned: true })).Select(i => i.Identity?.Id ?? i.Id).ToList();
        else if (!commit) _items = _dragOriginal!;
        _dragId = null; _dragOriginal = null; _dragX = double.NaN;
        if (_pendingItems is not null) { var p = _pendingItems; _pendingItems = null; SetItems(p); }
        return result;
    }

    /// <summary>Menú contextual del ítem (lógica pura; el host lo muestra y ejecuta la acción elegida).</summary>
    public IReadOnlyList<MenuEntry> MenuFor(int index)
    {
        if ((uint)index >= (uint)_items.Count || _items[index].Kind == DockItemKind.Separator) return DockMenu.ForBackground(_cfg);
        int pinnedCount = _items.Count(i => i is { Kind: DockItemKind.App, Pinned: true });
        int pinnedIndex = _items[index].Pinned ? _items.Take(index).Count(i => i is { Kind: DockItemKind.App, Pinned: true }) : -1;
        return DockMenu.ForItem(_items[index], pinnedIndex, pinnedCount, _cfg);
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
        double restLeft = BarLeftFor(rest);
        var cur = DockLayout.Compute(_m, _items, _anim.Scales(_items));
        double barLeft = BarLeftFor(cur.BarWidth);
        bool inZone = _pointer is PointerSample p0 && !_hiddenTarget && InZone(p0, barLeft, cur.BarWidth);

        // Magnificación: el objetivo se calcula en coordenadas de reposo; los resortes suavizan.
        double? restX = inZone && _cfg.MagnificationEnabled && _dragId is null ? _pointer!.Value.X - restLeft : null;   // sin ampliación mientras se arrastra
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
                bool over = _edgeHot || (_pointer is PointerSample p && (InZone(p, barLeft, cur.BarWidth) || PreviewContains(p.X, p.Y))); // el borde caliente y la vista previa cuentan como «encima»
                _outside = over ? 0 : _outside + dt;
                if (_outside >= _cfg.HideDelaySeconds) _hiddenTarget = true;
            }
        }
        else _hiddenTarget = false;
        _anim.SetHideTarget(_hiddenTarget ? 1 : 0);
        _anim.Step(dt);

        var frame = DockLayout.Compute(_m, _items, _anim.Scales(_items));
        double left = BarLeftFor(frame.BarWidth);
        var views = new ItemView[_items.Count]; int dragIndex = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            var f = frame.Items[i];
            views[i] = new ItemView(i, _items[i], left + f.X, f.Size, f.Scale, _anim.Lift(_items[i].Id));
            if (_items[i].Id == _dragId && !double.IsNaN(_dragX))   // el ítem arrastrado sigue al puntero, ligeramente elevado
            {
                double x = Math.Clamp(_dragX - f.Size / 2, left, left + frame.BarWidth - f.Size); views[i] = views[i] with { X = x, Lift = 8 }; dragIndex = i;
            }
        }
        int hover = inZone && _pointer is PointerSample pp ? DockLayout.HitTestX(frame, _items, pp.X - left) : -1;
        var baseView = new DockView(views, _windowWidth, _m.WindowHeight, left, frame.BarWidth, _m.BarHeight, _anim.HideT, hover, false, _suppressed, dragIndex);
        var restFrame = DockLayout.Compute(_m, _items, null); double restL = BarLeftFor(restFrame.BarWidth);
        Preview = _preview.Step(dt, baseView, _pointer, _dragId is not null, restFrame.Items.Select(f => restL + f.X + f.Size / 2).ToArray());
        bool needs = !_anim.IsSettled || inZone || (_cfg.AutoHide && !_hiddenTarget) || _dragId is not null || Preview is not null || _preview.IsActive;
        return baseView with { NeedsFrames = needs, Preview = Preview };
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
                return new DockAction(DockActionKind.Launch, it.Id, it.Path, 0, it.Identity);
            case DockItemKind.App when it.AllMinimized: return new DockAction(DockActionKind.Restore, it.Id, it.Path, 0, it.Identity);
            case DockItemKind.App when it.Active && _cfg.ClickActiveMinimizes: return new DockAction(DockActionKind.Minimize, it.Id, it.Path, 0, it.Identity);
            case DockItemKind.App when !it.Active: return new DockAction(DockActionKind.Activate, it.Id, it.Path, 0, it.Identity);
            default: return new DockAction(DockActionKind.None, it.Id);
        }
    }
}
