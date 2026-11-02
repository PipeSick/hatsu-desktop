namespace WinMac.Bar;

public enum PanelEventKind { Click, SliderChange, SliderCommit }
public sealed record PanelEvent(PanelEventKind Kind, string Id, double Value = 0);

/// <summary>
/// Comportamiento del panel (sin Windows): resalte, pulsar y soltar sobre el mismo elemento = clic, y arrastre de deslizadores (cambios continuos y confirmación final al soltar).
/// No ejecuta nada: devuelve eventos que el host traduce a operaciones de sistema fuera del hilo de interfaz.
/// </summary>
public sealed class PanelController
{
    private PanelModel _model = new(PanelKind.ControlCenter, "", []); private PanelFrame _frame = new(PanelLayout.Width, 0, []);
    private int _hover = -1, _down = -1; private bool _dragging, _muteDown; private double _lastValue;
    private double _maxHeight = double.PositiveInfinity, _scroll;

    /// <summary>Altura máxima del panel en DIP (la de la pantalla donde está). Si el contenido no cabe, se desplaza el centro y las pestañas y el pie quedan fijos.</summary>
    public double MaxHeight => _maxHeight;
    public double ScrollOffset => _frame.View?.Offset ?? 0;
    public bool CanScroll => _frame.View is { MaxOffset: > 0.5 };

    public void SetMaxHeight(double maxHeight)
    {
        if (Math.Abs(maxHeight - _maxHeight) < 0.01) return;
        _maxHeight = maxHeight; _frame = PanelLayout.Compute(_model, _maxHeight, _scroll); _scroll = _frame.View?.Offset ?? 0;
    }

    /// <summary>Desplaza el contenido (positivo = hacia abajo). Devuelve true si algo cambió.</summary>
    public bool Scroll(double deltaDip)
    {
        if (!CanScroll) return false; double before = ScrollOffset;
        _scroll = Math.Clamp(before + deltaDip, 0, _frame.View!.MaxOffset); if (Math.Abs(_scroll - before) < 0.01) return false;
        _frame = PanelLayout.Compute(_model, _maxHeight, _scroll); _scroll = _frame.View?.Offset ?? 0; return true;
    }

    public PanelModel Model => _model;
    public PanelFrame Frame => _frame;
    public int Hover => _hover;
    public bool Dragging => _dragging;

    public void SetModel(PanelModel m)
    {
        string? hoverId = _hover >= 0 && _hover < _frame.Items.Count ? _frame.Items[_hover].W.Id : null, downId = _down >= 0 && _down < _frame.Items.Count ? _frame.Items[_down].W.Id : null;
        if (ScrollKey(_model) != ScrollKey(m)) _scroll = 0;   // otra pestaña: vuelve arriba
        _model = m; _frame = PanelLayout.Compute(m, _maxHeight, _scroll); _scroll = _frame.View?.Offset ?? 0;
        _hover = hoverId is null ? -1 : _frame.Items.ToList().FindIndex(f => f.W.Id == hoverId);
        _down = downId is null ? -1 : _frame.Items.ToList().FindIndex(f => f.W.Id == downId);
        if (_dragging && _down < 0) _dragging = false;   // el deslizador desapareció durante el arrastre
    }

    private static bool Interactive(Widget w) => w.Enabled && w.Kind is WidgetKind.Tile or WidgetKind.Row or WidgetKind.Button or WidgetKind.Slider or WidgetKind.Month or WidgetKind.Nav or WidgetKind.Tabs or WidgetKind.TabHeader or WidgetKind.Select or WidgetKind.AppRow or WidgetKind.Stats or WidgetKind.CityRow
        or WidgetKind.NcHeader or WidgetKind.NcSection or WidgetKind.NotifGroup or WidgetKind.NotifCard or WidgetKind.MenuItem
        || (w.Kind == WidgetKind.Header && w.Id == "back");

    private static string ScrollKey(PanelModel m) => m.PinTop > 0 && m.PinTop <= m.Widgets.Count ? m.Kind + "/" + m.Widgets[m.PinTop - 1].Id : m.Kind.ToString();

    public void PointerLeave() { _hover = -1; }

    /// <summary>Identificador del evento de deslizador: el de la fila de una aplicación lleva el prefijo «appvol:» (su Id es el de la sesión de audio).</summary>
    private static string DragId(Widget w) => w.Kind == WidgetKind.AppRow ? "appvol:" + w.Id : w.Id;

    public PanelEvent? PointerMove(double x, double y)
    {
        if (_dragging && _down >= 0)
        {
            double v = PanelLayout.SliderValueAt(_frame.Items[_down], x);
            if (Math.Abs(v - _lastValue) < 0.5) return null;
            _lastValue = v; return new PanelEvent(PanelEventKind.SliderChange, DragId(_frame.Items[_down].W), v);
        }

        int i = PanelLayout.HitTest(_frame, x, y); _hover = i >= 0 && Interactive(_frame.Items[i].W) ? i : -1; return null;
    }

    public PanelEvent? PointerDown(double x, double y)
    {
        int i = PanelLayout.HitTest(_frame, x, y); _down = i >= 0 && Interactive(_frame.Items[i].W) ? i : -1; _muteDown = false;
        if (_down < 0) return null;
        var f = _frame.Items[_down];
        if (f.W.Kind == WidgetKind.Slider)
        {
            if (PanelLayout.SliderMuteHit(f, x)) { _muteDown = true; return null; }   // el icono de silencio no arrastra: se resuelve al soltar
            _dragging = true; _lastValue = PanelLayout.SliderValueAt(f, x);
            return new PanelEvent(PanelEventKind.SliderChange, DragId(f.W), _lastValue);
        }

        if (f.W.Kind == WidgetKind.AppRow)
        {
            if (PanelLayout.AppMuteHit(f, x, y)) { _muteDown = true; return null; }
            if (!PanelLayout.AppSliderHit(f, x, y)) { _down = -1; return null; }   // pulsar el nombre o el icono no hace nada
            _dragging = true; _lastValue = PanelLayout.SliderValueAt(f, x);
            return new PanelEvent(PanelEventKind.SliderChange, DragId(f.W), _lastValue);
        }

        return null;
    }

    public PanelEvent? PointerUp(double x, double y)
    {
        int down = _down; _down = -1;
        if (_dragging)
        {
            _dragging = false; if (down < 0 || down >= _frame.Items.Count) return null;
            return new PanelEvent(PanelEventKind.SliderCommit, DragId(_frame.Items[down].W), PanelLayout.SliderValueAt(_frame.Items[down], x));
        }

        if (down < 0 || down >= _frame.Items.Count) return null;
        int up = PanelLayout.HitTest(_frame, x, y); if (up != down) return null;   // soltar fuera cancela el clic
        var f = _frame.Items[down]; var w = f.W;
        switch (w.Kind)
        {
            case WidgetKind.Month:
            {
                int cell = PanelLayout.MonthCellAt(f, x, y); var md = w.Data as MonthData; if (cell < 0 || md is null || cell >= md.Days.Count) return null;
                return new PanelEvent(PanelEventKind.Click, "day:" + md.Days[cell].Date.ToString("yyyy-MM-dd"));
            }
            case WidgetKind.Nav: return new PanelEvent(PanelEventKind.Click, x < f.X + f.Width / 2 ? "prev" : "next");
            case WidgetKind.Slider: return _muteDown && PanelLayout.SliderMuteHit(f, x) && w.Data is string muteId ? new PanelEvent(PanelEventKind.Click, muteId) : null;
            case WidgetKind.AppRow: return _muteDown && PanelLayout.AppMuteHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, "appmute:" + w.Id) : null;
            case WidgetKind.Tabs:
            {
                int t = PanelLayout.TabAt(f, x); return w.Data is TabsData td && t >= 0 ? new PanelEvent(PanelEventKind.Click, "tab:" + td.Tabs[t].Id) : null;
            }
            case WidgetKind.Stats: return PanelLayout.StatsPillHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, "unit") : null;
            case WidgetKind.NcHeader: return PanelLayout.NcCloseHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, "nc:close") : null;
            case WidgetKind.NcSection: return PanelLayout.NcSectionActionHit(f, x) && w.Data is NcSectionData sd ? new PanelEvent(PanelEventKind.Click, sd.ActionId!) : null;
            case WidgetKind.NotifGroup:
                return w.Data is not NotifGroupData gd ? null
                    : new PanelEvent(PanelEventKind.Click, (PanelLayout.NotifGroupClearHit(f, x) ? "ngclear:" : "ngtoggle:") + gd.GroupKey);
            case WidgetKind.NotifCard:
            {
                if (w.Data is not NotifCardData cd) return null;
                if (PanelLayout.NotifDismissHit(f, x, y)) return new PanelEvent(PanelEventKind.Click, "ndismiss:" + cd.NotificationId);
                return new PanelEvent(PanelEventKind.Click, cd.Stacked > 0 ? "ngtoggle:" + cd.GroupKey : "nopen:" + cd.NotificationId);   // tarjeta apilada: despliega el grupo; si no, abre la app
            }
            default: return new PanelEvent(PanelEventKind.Click, w.Id);
        }
    }
}
