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
    private int _hover = -1, _down = -1; private bool _dragging, _muteDown, _outDown; private double _lastValue;
    private double _maxHeight = double.PositiveInfinity, _scroll;

    /// <summary>Altura máxima del panel en DIP (la de la pantalla donde está). Si el contenido no cabe, se desplaza el centro y las pestañas y el pie quedan fijos.</summary>
    public double MaxHeight => _maxHeight;
    public double ScrollOffset => _frame.View?.Offset ?? 0;
    public bool CanScroll => _frame.View is { MaxOffset: > 0.5 };

    /// <summary>WM-004: keyboard focus inside the background-apps grid (-1 = none). Arrows move it; Intro opens the app, Mayús+F10 or the menu key opens its menu.</summary>
    public int TrayFocus { get; private set; } = -1;

    public PanelEvent? TrayKey(int vk, bool shift)
    {
        var f = _frame.Items.FirstOrDefault(i => i.W.Kind == WidgetKind.TrayGrid); if (f is null || f.W.Data is not TrayGridData g || g.Cells.Count == 0) return null;
        int n = g.Cells.Count, cols = Math.Max(1, g.Columns), at = TrayFocus;
        switch (vk)
        {
            case 0x27: TrayFocus = at < 0 ? 0 : Math.Min(n - 1, at + 1); return null;            // →
            case 0x25: TrayFocus = at < 0 ? 0 : Math.Max(0, at - 1); return null;                // ←
            case 0x28: TrayFocus = at < 0 ? 0 : Math.Min(n - 1, at + cols); return null;         // ↓
            case 0x26: TrayFocus = at < 0 ? 0 : Math.Max(0, at - cols); return null;             // ↑
            case 0x0D or 0x20 when at >= 0: return new PanelEvent(PanelEventKind.Click, "trayitem:" + g.Cells[at].Key);
            case 0x5D when at >= 0: return new PanelEvent(PanelEventKind.Click, "rclick:trayitem:" + g.Cells[at].Key);
            case 0x79 when shift && at >= 0: return new PanelEvent(PanelEventKind.Click, "rclick:trayitem:" + g.Cells[at].Key);
        }

        return null;
    }

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

    // ── WM-022: keyboard. Tab / ↓ / ↑ go through every control (each button of an action strip or a link row counts on its own), Intro or Espacio activates,
    //    ← → move a focused slider by 10. The focus survives refreshes of the same panel (it is kept by id).
    public sealed record FocusTarget(int Item, int Sub, string Id);
    private string? _focusId; private int _focusSub = -1;
    public FocusTarget? Focus => Targets().FirstOrDefault(t => t.Id == _focusId && (t.Sub == _focusSub || _focusSub < 0));

    public IReadOnlyList<FocusTarget> Targets()
    {
        var r = new List<FocusTarget>();
        for (int i = 0; i < _frame.Items.Count; i++)
        {
            var w = _frame.Items[i].W; if (!w.Enabled) continue;
            switch (w.Kind)
            {
                case WidgetKind.ActionStrip when w.Data is ActionStripData sd: for (int k = 0; k < sd.Actions.Count; k++) r.Add(new(i, k, sd.Actions[k].Id)); break;
                case WidgetKind.LinkRow when w.Data is LinkRowData ld: for (int k = 0; k < ld.Links.Count; k++) if (ld.Links[k].Enabled) r.Add(new(i, k, ld.Links[k].Id)); break;
                case WidgetKind.UserHeader when w.Data is UserHeaderData uh: r.Add(new(i, 0, uh.AccountId)); break;
                case WidgetKind.PhoneHeader when w.Data is PhoneHeaderData ph: r.Add(new(i, 0, ph.SettingsId)); break;
                case WidgetKind.DeviceCard when w.Data is DeviceCardData dc: r.Add(new(i, 0, dc.HideId)); break;
                case WidgetKind.CleanRow when w.Data is CleanRowData cr && !cr.Locked: r.Add(new(i, -1, w.Id)); break;
                case WidgetKind.Tile or WidgetKind.Row or WidgetKind.Button or WidgetKind.Slider or WidgetKind.MenuItem or WidgetKind.CityRow or WidgetKind.ModeCard: r.Add(new(i, -1, w.Id)); break;
                case WidgetKind.Header when w.Id == "back": r.Add(new(i, -1, w.Id)); break;
            }
        }

        return r;
    }

    public PanelEvent? FocusKey(int vk, bool shift)
    {
        var t = Targets(); if (t.Count == 0) return null;
        int at = t.ToList().FindIndex(x => x.Id == _focusId && (x.Sub == _focusSub || _focusSub < 0));
        void Go(int i) { var x = t[(i % t.Count + t.Count) % t.Count]; _focusId = x.Id; _focusSub = x.Sub; }
        switch (vk)
        {
            case 0x09: Go(at < 0 ? (shift ? t.Count - 1 : 0) : at + (shift ? -1 : 1)); return null;   // Tab
            case 0x28: Go(at < 0 ? 0 : at + 1); return null;                                           // ↓
            case 0x26: Go(at < 0 ? t.Count - 1 : at - 1); return null;                                 // ↑
            case 0x25 or 0x27 when at >= 0:                                                             // ← →
            {
                var f = _frame.Items[t[at].Item];
                if (f.W.Kind == WidgetKind.Slider) return new PanelEvent(PanelEventKind.SliderCommit, f.W.Id, Math.Clamp(f.W.Value + (vk == 0x27 ? 10 : -10), 0, 100));
                Go(at + (vk == 0x27 ? 1 : -1)); return null;
            }
            case 0x0D or 0x20 when at >= 0: return _frame.Items[t[at].Item].W.Kind == WidgetKind.Slider ? null : new PanelEvent(PanelEventKind.Click, t[at].Id);
        }

        return null;
    }
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
        or WidgetKind.NcHeader or WidgetKind.NcSection or WidgetKind.NotifGroup or WidgetKind.NotifCard or WidgetKind.MenuItem or WidgetKind.TrayGrid or WidgetKind.UserHeader or WidgetKind.PhoneHeader or WidgetKind.DeviceCard or WidgetKind.CleanRow or WidgetKind.ActionStrip or WidgetKind.LinkRow or WidgetKind.ModeCard
        || (w.Kind == WidgetKind.Header && w.Id == "back");

    private static string ScrollKey(PanelModel m) => m.PinTop > 0 && m.PinTop <= m.Widgets.Count ? m.Kind + "/" + m.Widgets[m.PinTop - 1].Id : m.Kind.ToString();

    public void PointerLeave() { _hover = -1; Pointer = null; }

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

        Pointer = (x, y);
        int i = PanelLayout.HitTest(_frame, x, y); _hover = i >= 0 && Interactive(_frame.Items[i].W) ? i : -1; return null;
    }

    public PanelEvent? PointerDown(double x, double y)
    {
        int i = PanelLayout.HitTest(_frame, x, y); _down = i >= 0 && Interactive(_frame.Items[i].W) ? i : -1; _muteDown = false; _outDown = false;
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
            if (PanelLayout.AppOutputHit(f, x, y)) { _outDown = true; return null; }   // WM-035: el selector de salida se resuelve al soltar
            if (!PanelLayout.AppSliderHit(f, x, y)) { _down = -1; return null; }   // pulsar el nombre o el icono no hace nada
            _dragging = true; _lastValue = PanelLayout.SliderValueAt(f, x);
            return new PanelEvent(PanelEventKind.SliderChange, DragId(f.W), _lastValue);
        }

        return null;
    }

    /// <summary>Clic derecho: solo en un icono de app en segundo plano (su menú). Devuelve «rclick:trayitem:» + clave.</summary>
    public PanelEvent? RightClick(double x, double y)
    {
        int i = PanelLayout.HitTest(_frame, x, y); if (i < 0 || _frame.Items[i].W.Kind != WidgetKind.TrayGrid) return null;
        int cell = PanelLayout.TrayCellAt(_frame.Items[i], x, y);
        return cell >= 0 && _frame.Items[i].W.Data is TrayGridData g ? new PanelEvent(PanelEventKind.Click, "rclick:trayitem:" + g.Cells[cell].Key) : null;
    }

    /// <summary>Último punto del puntero dentro del panel (DIP): el dibujo resalta la casilla de la cuadrícula que hay debajo.</summary>
    public (double X, double Y)? Pointer { get; private set; }

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
            case WidgetKind.AppRow:
                if (_outDown && PanelLayout.AppOutputHit(f, x, y)) { var o = PanelLayout.AppOutput(f); Anchor = new PanelLayout.Box(o.X, o.Y, o.W, o.H); return new PanelEvent(PanelEventKind.Click, "appout:" + w.Id); }
                return _muteDown && PanelLayout.AppMuteHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, "appmute:" + w.Id) : null;
            case WidgetKind.Tabs:
            {
                int t = PanelLayout.TabAt(f, x); return w.Data is TabsData td && t >= 0 ? new PanelEvent(PanelEventKind.Click, "tab:" + td.Tabs[t].Id) : null;
            }
            case WidgetKind.Stats: return PanelLayout.StatsPillHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, "unit") : null;
            case WidgetKind.TrayGrid:
            {
                int cell = PanelLayout.TrayCellAt(f, x, y);
                return cell >= 0 && w.Data is TrayGridData g ? new PanelEvent(PanelEventKind.Click, "trayitem:" + g.Cells[cell].Key) : null;
            }
            case WidgetKind.NcHeader: return PanelLayout.NcCloseHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, "nc:close") : null;
            case WidgetKind.UserHeader: return w.Data is UserHeaderData uh && PanelLayout.UserPowerHit(f, x) ? new PanelEvent(PanelEventKind.Click, uh.AccountId) : null;
            case WidgetKind.PhoneHeader: return w.Data is PhoneHeaderData phd && PanelLayout.PhoneGearHit(f, x) ? new PanelEvent(PanelEventKind.Click, phd.SettingsId) : null;
            case WidgetKind.DeviceCard: return w.Data is DeviceCardData dcd && PanelLayout.DeviceHideHit(f, x, y) ? new PanelEvent(PanelEventKind.Click, dcd.HideId) : null;
            case WidgetKind.CleanRow: return w.Data is CleanRowData { Locked: false } ? new PanelEvent(PanelEventKind.Click, w.Id) : null;
            case WidgetKind.LinkRow: { int k = PanelLayout.LinkAt(f, x); return w.Data is LinkRowData ld && k >= 0 ? new PanelEvent(PanelEventKind.Click, ld.Links[k].Id) : null; }
            case WidgetKind.ActionStrip: { int k = PanelLayout.StripActionAt(f, x); return w.Data is ActionStripData sd2 && k >= 0 && k < sd2.Actions.Count ? new PanelEvent(PanelEventKind.Click, sd2.Actions[k].Id) : null; }
            case WidgetKind.NcSection: return PanelLayout.NcSectionActionHit(f, x) && w.Data is NcSectionData sd ? new PanelEvent(PanelEventKind.Click, sd.ActionId!) : null;
            case WidgetKind.NotifGroup:
                return w.Data is not NotifGroupData gd ? null
                    : new PanelEvent(PanelEventKind.Click, (PanelLayout.NotifGroupClearHit(f, x) ? gd.Origin == "iphone" ? "pgclear:" : "ngclear:" : "ngtoggle:") + gd.GroupKey);
            case WidgetKind.NotifCard:
            {
                if (w.Data is not NotifCardData cd) return null;
                if (PanelLayout.NotifDismissHit(f, x, y)) return new PanelEvent(PanelEventKind.Click, (cd.Origin == "iphone" ? "pdismiss:" : "ndismiss:") + cd.NotificationId);
                if (cd.Stacked > 0) return new PanelEvent(PanelEventKind.Click, "ngtoggle:" + cd.GroupKey);
                return cd.Origin == "iphone" ? null : new PanelEvent(PanelEventKind.Click, "nopen:" + cd.NotificationId);   // una app del iPhone no se puede abrir desde el PC   // tarjeta apilada: despliega el grupo; si no, abre la app
            }
            case WidgetKind.Select: Anchor = PanelLayout.SelectPill(f); return new PanelEvent(PanelEventKind.Click, w.Id);   // el menú que abra se coloca contra este botón
            default: return new PanelEvent(PanelEventKind.Click, w.Id);
        }
    }

    /// <summary>
    /// El control (en DIP del panel) que acaba de generar un clic que abre un menú de opciones (selector de dispositivo o de salida de una app). El anfitrión lo usa para colocar el
    /// desplegable contra él en vez de donde esté el puntero.
    /// </summary>
    public PanelLayout.Box? Anchor { get; private set; }
}
