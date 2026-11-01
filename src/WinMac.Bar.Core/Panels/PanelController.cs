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
    private int _hover = -1, _down = -1; private bool _dragging; private double _lastValue;

    public PanelModel Model => _model;
    public PanelFrame Frame => _frame;
    public int Hover => _hover;
    public bool Dragging => _dragging;

    public void SetModel(PanelModel m)
    {
        string? hoverId = _hover >= 0 && _hover < _frame.Items.Count ? _frame.Items[_hover].W.Id : null, downId = _down >= 0 && _down < _frame.Items.Count ? _frame.Items[_down].W.Id : null;
        _model = m; _frame = PanelLayout.Compute(m);
        _hover = hoverId is null ? -1 : _frame.Items.ToList().FindIndex(f => f.W.Id == hoverId);
        _down = downId is null ? -1 : _frame.Items.ToList().FindIndex(f => f.W.Id == downId);
        if (_dragging && _down < 0) _dragging = false;   // el deslizador desapareció durante el arrastre
    }

    private static bool Interactive(Widget w) => w.Enabled && w.Kind is WidgetKind.Tile or WidgetKind.Row or WidgetKind.Button or WidgetKind.Slider or WidgetKind.Month or WidgetKind.Nav
        || (w.Kind == WidgetKind.Header && w.Id == "back");

    public void PointerLeave() { _hover = -1; }

    public PanelEvent? PointerMove(double x, double y)
    {
        if (_dragging && _down >= 0)
        {
            double v = PanelLayout.SliderValueAt(_frame.Items[_down], x);
            if (Math.Abs(v - _lastValue) < 0.5) return null;
            _lastValue = v; return new PanelEvent(PanelEventKind.SliderChange, _frame.Items[_down].W.Id, v);
        }

        int i = PanelLayout.HitTest(_frame, x, y); _hover = i >= 0 && Interactive(_frame.Items[i].W) ? i : -1; return null;
    }

    public PanelEvent? PointerDown(double x, double y)
    {
        int i = PanelLayout.HitTest(_frame, x, y); _down = i >= 0 && Interactive(_frame.Items[i].W) ? i : -1;
        if (_down >= 0 && _frame.Items[_down].W.Kind == WidgetKind.Slider)
        {
            _dragging = true; _lastValue = PanelLayout.SliderValueAt(_frame.Items[_down], x);
            return new PanelEvent(PanelEventKind.SliderChange, _frame.Items[_down].W.Id, _lastValue);
        }

        return null;
    }

    public PanelEvent? PointerUp(double x, double y)
    {
        int down = _down; _down = -1;
        if (_dragging)
        {
            _dragging = false; if (down < 0 || down >= _frame.Items.Count) return null;
            return new PanelEvent(PanelEventKind.SliderCommit, _frame.Items[down].W.Id, PanelLayout.SliderValueAt(_frame.Items[down], x));
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
            default: return new PanelEvent(PanelEventKind.Click, w.Id);
        }
    }
}
