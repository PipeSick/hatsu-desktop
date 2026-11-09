using WinMac.Apps;

namespace WinMac.Dock;

/// <summary>Lo que el render dibuja de la vista previa. <see cref="Windows"/> son las ventanas mostradas (máximo <see cref="PreviewSpec.MaxCards"/>); <see cref="Extra"/> las que no caben.</summary>
public sealed record PreviewView(string ItemId, int ItemIndex, DockItemModel Item, IReadOnlyList<WindowInfo> Windows, int Extra, PreviewFrame Frame, int HoverCard, double Alpha, bool HoverClose = false);

/// <summary>
/// Comportamiento de la vista previa (lógica pura, sin Windows): aparece tras un retardo al pasar sobre una app con ventanas, cambia al instante entre apps,
/// permanece mientras el puntero esté en el ícono, en el pasillo hacia el panel o en el panel, y se oculta tras un breve margen al salir.
/// </summary>
public sealed class PreviewController
{
    public const double FadeSeconds = 0.12, GraceSeconds = 0.25;
    private readonly DockMetrics _m; private DockConfig _cfg;
    private string? _id, _cand, _blockedId; private double _dwell, _outside, _alpha; private bool _shown;

    public PreviewController(DockMetrics m, DockConfig cfg) { _m = m; _cfg = cfg; }
    public DockConfig Config { set => _cfg = value; }
    /// <summary>Proporción ancho/alto de una ventana (la aporta el servicio de miniaturas; null → proporción por defecto).</summary>
    public Func<long, double>? Aspect { get; set; }
    public bool IsActive => _shown || _alpha > 0;

    /// <summary>Oculta la vista previa y no la vuelve a mostrar sobre ese ícono hasta que el puntero salga de él (p. ej. tras activar una ventana).</summary>
    public void Dismiss() { _blockedId = _id; _shown = false; _alpha = 0; _cand = null; _dwell = 0; }

    /// <summary>
    /// El panel se ancla a la posición EN REPOSO del ícono, con holgura para la magnificación máxima: así no se mueve mientras el puntero sube hacia él
    /// (la magnificación se relaja al salir de la barra) ni se solapa con el ícono ampliado.
    /// </summary>
    private PreviewFrame Layout(ItemView it, DockView v, int count, IReadOnlyList<double> restCenters)
    {
        double barBottom = v.WindowHeight - _m.ShadowMargin, iconTop = barBottom - _m.PadBottom - _m.BaseSize * (_cfg.MagnificationEnabled ? _m.MaxScale : 1);
        var asp = new List<double>(count); foreach (var w in it.Model.Windows!.Take(count)) asp.Add(Aspect?.Invoke(w.Hwnd) ?? 0);
        double cx = it.Index < restCenters.Count ? restCenters[it.Index] : it.X + it.Size / 2;
        // Con el Dock pegado a un lado, el borde de la pantalla queda dentro de la ventana (la ventana se desplaza BarInset − EdgeMargin): el panel respeta ese borde.
        double screenEdge = _m.BarInset - _cfg.EdgeMargin;
        double? minX = _cfg.Alignment == DockAlignment.Left ? screenEdge + PreviewSpec.Margin : null, maxRight = _cfg.Alignment == DockAlignment.Right ? v.WindowWidth - screenEdge - PreviewSpec.Margin : null;
        return PreviewSpec.Compute(asp, cx, iconTop, v.WindowWidth, Math.Min(PreviewSpec.MaxPanelWidth, _m.PreviewMaxWidth > 0 ? _m.PreviewMaxWidth : PreviewSpec.MaxPanelWidth), minX, maxRight);
    }

    /// <summary>Zona que mantiene viva la vista previa: el panel (con margen) y el pasillo entre el panel y el ícono.</summary>
    private bool Inside(PreviewFrame f, double centerX, DockView v, PointerSample p)
    {
        double barBottom = v.WindowHeight - _m.ShadowMargin, half = _m.BaseSize / 2 + 12;
        return f.Panel.Inflate(6).Contains(p.X, p.Y) || new PRect(centerX - half, f.Panel.Bottom, 2 * half, barBottom - f.Panel.Bottom).Contains(p.X, p.Y);
    }

    public PreviewView? Step(double dt, DockView v, PointerSample? ptr, bool dragging, IReadOnlyList<double> restCenters)
    {
        bool allowed = _cfg.PreviewsEnabled && _m.PreviewHeadroom > 0 && !dragging && !v.Suppressed && v.HideT < 0.5;
        ItemView? hov = null;
        if (allowed && v.HoverIndex >= 0 && v.HoverIndex < v.Items.Length && v.Items[v.HoverIndex] is { Model: { Kind: DockItemKind.App, Windows.Count: > 0 } } h) hov = h;
        ItemView? cur = _id is null ? null : v.Items.FirstOrDefault(i => i.Model.Id == _id && i.Model.Windows is { Count: > 0 });
        if (!allowed) { _shown = false; _cand = null; _dwell = 0; }

        int count = cur?.Model.Windows is { } cw ? Math.Min(cw.Count, PreviewSpec.MaxCards) : 0;
        PreviewFrame? frame = _shown && cur is not null ? Layout(cur, v, count, restCenters) : null;
        bool inside = frame is not null && ptr is PointerSample p0 && Inside(frame, cur!.Index < restCenters.Count ? restCenters[cur.Index] : cur.X + cur.Size / 2, v, p0);

        if (_blockedId is not null && hov?.Model.Id != _blockedId && !inside) _blockedId = null;   // el puntero ya salió del ícono: se permite de nuevo
        if (hov is not null && hov.Model.Id == _blockedId) hov = null;

        if (hov is not null)
        {
            _outside = 0;
            if (_shown) _id = hov.Model.Id;                                                          // entre apps con la vista previa abierta: cambio inmediato
            else
            {
                if (_cand != hov.Model.Id) { _cand = hov.Model.Id; _dwell = 0; }
                _dwell += dt;
                if (_dwell >= _cfg.PreviewDelaySeconds) { _shown = true; _id = hov.Model.Id; }
            }
        }
        else if (inside) _outside = 0;
        else
        {
            _cand = null; _dwell = 0;
            if (_shown) { _outside += dt; if (_outside >= GraceSeconds) { _shown = false; _outside = 0; } }
        }

        cur = _id is null ? null : v.Items.FirstOrDefault(i => i.Model.Id == _id && i.Model.Windows is { Count: > 0 });
        bool visible = _shown && cur is not null;
        _alpha = Math.Clamp(_alpha + (visible ? dt : -dt) / FadeSeconds, 0, 1);
        if (!visible && _alpha <= 0) { _id = _shown ? _id : null; return null; }
        if (cur is null) return null;

        var wins = cur.Model.Windows!; count = Math.Min(wins.Count, PreviewSpec.MaxCards);
        var fr = Layout(cur, v, count, restCenters);
        int hover = -1;
        if (ptr is PointerSample p) for (int i = 0; i < fr.Cards.Length; i++) if (fr.Cards[i].Pill.Contains(p.X, p.Y) || fr.Cards[i].Thumb.Contains(p.X, p.Y) || BetweenPillAndThumb(fr.Cards[i], p)) { hover = i; break; }
        bool onClose = hover >= 0 && ptr is PointerSample pc && fr.Cards[hover].Close.Inflate(3).Contains(pc.X, pc.Y);
        return new PreviewView(cur.Model.Id, cur.Index, cur.Model, wins.Take(count).ToList(), wins.Count - count, fr, hover, _alpha, onClose);
    }

    private static bool BetweenPillAndThumb(PreviewCardFrame c, PointerSample p) => p.X >= c.Pill.X && p.X <= c.Pill.Right && p.Y >= c.Pill.Bottom && p.Y <= c.Thumb.Y;

    /// <summary>Clic sobre una tarjeta → activar (y restaurar si está minimizada) esa ventana. Null si no se pulsó ninguna tarjeta o aún se está mostrando.</summary>
    public DockAction? Click(PreviewView? pv)
    {
        if (pv is null || pv.Alpha < 0.5 || pv.HoverCard < 0 || pv.HoverCard >= pv.Windows.Count) return null;
        var w = pv.Windows[pv.HoverCard];
        // La «×» de la tarjeta cierra ESA ventana (cierre cooperativo, WM_CLOSE) y deja la vista previa abierta: al cambiar la lista de ventanas se actualiza sola.
        if (pv.HoverClose) return new DockAction(DockActionKind.CloseWindow, pv.ItemId, null, w.Hwnd, pv.Item.Identity);
        Dismiss();
        return new DockAction(DockActionKind.ActivateWindow, pv.ItemId, null, w.Hwnd, pv.Item.Identity);
    }
}
