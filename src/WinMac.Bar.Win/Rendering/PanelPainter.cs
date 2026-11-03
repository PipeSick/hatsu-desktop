using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Dibuja un <see cref="PanelFrame"/> con Direct2D (estética de macOS: tarjeta redondeada translúcida, mosaicos con acento azul al activarse, deslizadores blancos). Unidades en DIP.
/// Un elemento deshabilitado se dibuja atenuado con su motivo debajo: nunca parece funcionar.
/// </summary>
public sealed partial class PanelPainter : IDisposable
{
    private readonly D2DDevice _dev; private readonly ID2D1DeviceContext _ctx;
    private readonly IDWriteFactory _dw; private readonly IDWriteTextFormat _title, _body, _bodyBold, _small, _tiny;
    private readonly ID2D1SolidColorBrush _solid; private readonly ID2D1StrokeStyle _round;
    public BarTheme Theme { get; set; } = BarTheme.Dark;
    /// <summary>Fondo de pantalla desenfocado tras el panel lateral (lo crea y libera <see cref="PopoverWindow"/>); null = tarjeta opaca.</summary>
    public ID2D1Bitmap? Backdrop { get; set; }
    /// <summary>Punto del ratón dentro del panel (DIP), para resaltar la casilla de la cuadrícula de apps en segundo plano que hay debajo.</summary>
    public (double X, double Y)? HoverPoint { get; set; }
    /// <summary>WM-004: focused cell of the background-apps grid (keyboard).</summary>
    public int TrayFocus { get; set; } = -1;
    /// <summary>WM-022: keyboard focus (drawn as an accent ring around the control or sub-button).</summary>
    public PanelController.FocusTarget? Focus { get; set; }

    private void DrawFocus(PanelFrame f)
    {
        if (Focus is not { } fc || fc.Item < 0 || fc.Item >= f.Items.Count) return;
        var it = f.Items[fc.Item]; double x = it.X, y = it.Y, w = it.Width, h = it.Height;
        if (it.W.Data is ActionStripData sd && fc.Sub >= 0) { double cw = w / sd.Actions.Count; Brush(Accent); _ctx.DrawEllipse(new Ellipse(new Vector2((float)(x + cw * fc.Sub + cw / 2), (float)(y + 24)), 25, 25), _solid, 2f); return; }
        if (it.W.Data is LinkRowData ld && fc.Sub >= 0) { double cw = w / ld.Links.Count; x += cw * fc.Sub; w = cw; }
        if (it.W.Data is UserHeaderData) { var ar = PanelLayout.UserAccountRect(it); x = ar.X; y = ar.Y; w = ar.W; h = ar.H; }
        Brush(Accent); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(x - 1, y - 1, x + w + 1, y + h + 1), RadiusX = 12, RadiusY = 12 }, _solid, 2f);
    }
    private static readonly Color4 Accent = new(0.04f, 0.52f, 1f, 1f);

    public PanelPainter(D2DDevice dev)
    {
        _dev = dev; _ctx = dev.Context; _solid = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _title = _dw.CreateTextFormat("Segoe UI Semibold", 15); _body = _dw.CreateTextFormat("Segoe UI", 13); _bodyBold = _dw.CreateTextFormat("Segoe UI Semibold", 13);
        _small = _dw.CreateTextFormat("Segoe UI", 11); _tiny = _dw.CreateTextFormat("Segoe UI", 10.5f);
        _round = dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
    }

    private bool Dark => Theme == BarTheme.Dark;
    private Color4 Fg(float a = 1f) => Dark ? new Color4(1, 1, 1, 0.95f * a) : new Color4(0, 0, 0, 0.86f * a);
    private Color4 Fill(float a) => Dark ? new Color4(1, 1, 1, a) : new Color4(0, 0, 0, a * 0.7f);
    private void Brush(Color4 c) => _solid.Color = c;
    private static RawRectF R(double l, double t, double r, double b) => new((float)l, (float)t, (float)r, (float)b);
    private void FillRR(double l, double t, double r, double b, float rad, Color4 c) { Brush(c); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(l, t, r, b), RadiusX = rad, RadiusY = rad }, _solid); }

    private PanelKind _kind;

    public void Paint(PanelFrame f, PanelModel m, int hover, bool clear = true)
    {
        _kind = m.Kind; _ctx.BeginDraw();
        if (clear) _ctx.Clear(new Color4(0, 0, 0, 0));
        // tarjeta (el panel lateral, con el vidrio del fondo de pantalla si lo hay y esquinas algo más amplias)
        float rad = m.Kind == PanelKind.Notifications ? 18 : m.Kind == PanelKind.AppMenu ? 10 : 16;
        if (m.Kind == PanelKind.Notifications && Backdrop is { } bd)
        {
            using var br = _ctx.CreateBitmapBrush(bd, new BitmapBrushProperties { ExtendModeX = ExtendMode.Clamp, ExtendModeY = ExtendMode.Clamp, InterpolationMode = BitmapInterpolationMode.Linear });
            _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(0.5, 0.5, f.Width - 0.5, f.Height - 0.5), RadiusX = rad, RadiusY = rad }, br);
            FillRR(0.5, 0.5, f.Width - 0.5, f.Height - 0.5, rad, Dark ? new Color4(0.10f, 0.10f, 0.12f, 0.58f) : new Color4(0.97f, 0.97f, 0.98f, 0.62f));
        }
        else
        {
            // WM-022/027: the Control Center and the Energy panel carry dense text: almost opaque so what is behind never competes with it
            float a = m.Kind is PanelKind.AppleMenu or PanelKind.Battery ? 0.985f : 0.94f;
            FillRR(0.5, 0.5, f.Width - 0.5, f.Height - 0.5, rad, Dark ? new Color4(0.12f, 0.12f, 0.14f, a) : new Color4(0.96f, 0.96f, 0.97f, a));
        }
        Brush(Fill(0.14f)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(0.5, 0.5, f.Width - 0.5, f.Height - 0.5), RadiusX = rad, RadiusY = rad }, _solid, 1f);
        var v = f.View; int i = 0;
        if (v is not null) for (; i < v.From; i++) DrawWidget(f.Items[i], i == hover);   // logotipo, pestañas y título: fijos, sin recorte
        if (v is not null) _ctx.PushAxisAlignedClip(R(0, v.Top, f.Width, v.Bottom), AntialiasMode.Aliased);   // lo desplazado no invade las pestañas ni el pie
        if (f.Card is { } cr) { FillRR(cr.X, cr.Y, cr.X + cr.W, cr.Y + cr.H, 14, Fill(0.075f)); Brush(Fill(0.12f)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cr.X + 0.5, cr.Y + 0.5, cr.X + cr.W - 0.5, cr.Y + cr.H - 0.5), RadiusX = 14, RadiusY = 14 }, _solid, 1f); }   // la tarjeta del contenido, como en las pestañas de referencia
        for (; i < f.Items.Count; i++)
        {
            if (v is not null && i == v.To) { _ctx.PopAxisAlignedClip(); v = null; }   // a partir de aquí, los elementos fijos del pie
            DrawWidget(f.Items[i], i == hover);
        }

        if (v is not null) _ctx.PopAxisAlignedClip();
        DrawFocus(f);
        if (f.View is { MaxOffset: > 0.5 } sv)   // indicador de desplazamiento
        {
            double tr0 = sv.Top + 8, tr1 = sv.Bottom - 8, th = Math.Max(24, (tr1 - tr0) * (sv.Bottom - sv.Top) / sv.ContentHeight), ty = tr0 + (tr1 - tr0 - th) * (sv.Offset / sv.MaxOffset);
            FillRR(f.Width - 8, ty, f.Width - 5, ty + th, 1.5f, Fill(0.38f));
        }

        _ctx.EndDraw();
    }

    private void Text(string text, IDWriteTextFormat fmt, double x, double y, double w, double h, Color4 c, bool center = false, bool vcenter = true)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var l = _dw.CreateTextLayout(text, fmt, (float)Math.Max(1, w), (float)Math.Max(1, h));
        l.WordWrapping = WordWrapping.NoWrap; l.ParagraphAlignment = vcenter ? ParagraphAlignment.Center : ParagraphAlignment.Near;
        if (center) l.TextAlignment = TextAlignment.Center;
        using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell);
        Brush(c); _ctx.DrawTextLayout(new Vector2((float)x, (float)y), l, _solid);
    }

    private void DrawWidget(WidgetFrame f, bool hover)
    {
        var w = f.W; float dim = w.Enabled ? 1f : 0.45f; double x = f.X, y = f.Y, wd = f.Width, h = f.Height;
        switch (w.Kind)
        {
            case WidgetKind.Header:
            {
                if (w.Glyph == "logo") { DrawLogo(w.Data as string ?? "", x + wd / 2, y + h / 2 + 2, h); break; }
                bool back = w.Id == "back" || w.Glyph == "back";
                if (w.Glyph == "section") { Text(w.Text, _small, x + 4, y, wd, h, Fg(0.55f)); break; }
                if (back) { Glyph("back", x + 12, y + h / 2, 7, Fg(hover ? 1f : 0.8f)); Text(w.Text, _title, x + 28, y, wd - 28, h, Fg()); }
                else Text(w.Text, _title, x + 4, y, wd, h, Fg());
                break;
            }
            case WidgetKind.Nav:
                Glyph("back", x + 14, y + h / 2, 7, Fg(0.8f)); Glyph("chevron", x + wd - 14, y + h / 2, 7, Fg(0.8f));
                Text(w.Text, _title, x, y, wd, h, Fg(), center: true);
                break;
            case WidgetKind.Tile:
            {
                var bg = w.On ? Accent : Fill(hover && w.Enabled ? 0.17f : 0.11f);
                var fg = w.On ? new Color4(1, 1, 1, 1) : Fg(dim);
                if (_kind == PanelKind.Hub && w.Id.StartsWith("temp-", StringComparison.Ordinal))   // temperatura: etiqueta con icono arriba y el valor debajo, centrados (como la referencia)
                {
                    EnsureFonts(); FillRR(x, y, x + wd, y + h, 11, Fill(0.07f));
                    double lw = MeasureText(w.Sub ?? "", _small) + 20, lx = x + wd / 2 - lw / 2;
                    Glyph(w.Glyph ?? "", lx + 7, y + 11, 5.5f, Fg(0.6f)); Text(w.Sub ?? "", _small, lx + 20, y + 3, lw, 17, Fg(0.62f));
                    Text(w.Text, w.Enabled ? _big : _boldSmall, x, y + 19, wd, 24, w.Enabled ? fg : Fg(0.55f), center: true); break;
                }

                if (h < 60)   // fila compacta (discos): icono a la izquierda, nombre y detalle
                {
                    FillRR(x, y, x + wd, y + h, 10, w.Enabled ? bg : Fill(0.07f));
                    Glyph(w.Glyph ?? "", x + 22, y + h / 2, 7.5f, fg);
                    Text(w.Text, _bodyBold, x + 40, y + h / 2 - 19, wd - 46, 20, fg);
                    Text(w.Sub ?? "", _small, x + 40, y + h / 2 - 1, wd - 46, 18, w.On ? new Color4(1, 1, 1, 0.85f) : Fg(0.6f * dim)); break;
                }

                FillRR(x, y, x + wd, y + h, 12, w.Enabled ? bg : Fill(0.07f));
                FillCircle(x + 26, y + 26, 15, w.On ? new Color4(1, 1, 1, 0.22f) : Fill(0.12f * dim));
                Glyph(w.Glyph ?? "", x + 26, y + 26, 8.5f, fg, w.Bars);
                Text(w.Text, _bodyBold, x + 50, y + 8, wd - 56, 22, fg);
                string sub = !w.Enabled && !string.IsNullOrEmpty(w.Reason) ? w.Reason! : w.Sub ?? "";
                Text(sub, _small, x + 50, y + 28, wd - 56, 28, w.On ? new Color4(1, 1, 1, 0.85f) : Fg(0.6f * dim), vcenter: false);
                break;
            }
            case WidgetKind.Slider:
            {
                var (l, r) = PanelLayout.SliderTrack(f); double cy = w.Text.Length == 0 && h < 40 ? y + h / 2 : y + 23, pct = Math.Clamp(w.Value, 0, 100) / 100;
                Text(w.Text, _small, x + 4, y - 1, wd, 16, Fg(0.55f * dim), vcenter: false);
                FillRR(l, cy - 4, r, cy + 4, 4, Fill(0.18f * dim));
                if (w.Enabled) FillRR(l, cy - 4, l + (r - l) * pct, cy + 4, 4, Dark ? new Color4(1, 1, 1, 0.95f) : new Color4(0.25f, 0.25f, 0.28f, 1f));
                double tx = l + (r - l) * pct; FillCircle(tx, cy, 8, w.Enabled ? new Color4(1, 1, 1, 1) : Fill(0.3f));
                if (w.Enabled) { Brush(new Color4(0, 0, 0, 0.18f)); _ctx.DrawEllipse(new Ellipse(new Vector2((float)tx, (float)cy), 8, 8), _solid, 0.8f); }
                Glyph(w.Glyph ?? "sound", x + 20, cy, 8, Fg(dim));
                if (!string.IsNullOrEmpty(w.Sub)) TextAlign(w.Sub, _small, x + wd - 46, cy - 9, 44, 18, Fg(0.7f * dim), TextAlignment.Trailing);
                if (!w.Enabled && !string.IsNullOrEmpty(w.Reason)) Text(w.Reason!, _tiny, x + 4, y + 36, wd - 8, 26, Fg(0.6f), vcenter: false);
                break;
            }
            case WidgetKind.Row:
            case WidgetKind.Info:
            {
                bool clickable = w.Kind == WidgetKind.Row;
                if (hover && w.Enabled && clickable) FillRR(x, y, x + wd, y + h, 8, Fill(0.1f));
                double tx = x + 10;
                if (w.Bars >= 0) { Bars(x + 10, y + h / 2, w.Bars, Fg(dim)); tx = x + 34; }
                else if (w.On && w.Kind == WidgetKind.Info) { FillCircle(x + 14, y + h / 2, 3.5f, new Color4(0.30f, 0.80f, 0.40f, 1)); tx = x + 28; }
                else if (w.Glyph == "check") { Glyph("check", x + 16, y + h / 2, 7, Accent); tx = x + 34; }
                else if (w.On && clickable) { FillCircle(x + 16, y + h / 2, 4, Accent); tx = x + 34; }
                double tw = wd - (tx - x) - (w.Glyph is "lock" or "chevron" ? 28 : 8);
                bool two = !string.IsNullOrEmpty(w.Sub);
                Text(w.Text, _body, tx, two ? y + 3 : y, tw, two ? 22 : h, Fg(dim));
                if (two) Text(w.Sub!, _small, tx, y + 24, tw, 18, Fg(0.58f * dim), vcenter: false);
                if (w.Glyph == "lock") Glyph("lock", x + wd - 16, y + h / 2, 6, Fg(0.55f * dim));
                if (w.Glyph == "chevron") Glyph("chevron", x + wd - 14, y + h / 2, 6, Fg(0.55f));
                break;
            }
            case WidgetKind.Button:
            {
                FillRR(x, y, x + wd, y + h, 8, Fill(hover && w.Enabled ? 0.17f : 0.10f));
                var bc = w.Enabled ? Fg() : Fg(0.45f);
                if (w.Glyph is not null)
                {
                    double tw = MeasureText(w.Text, _body), room = wd - 40;   // glifo + texto no pueden salirse del botón: si no caben, el texto se recorta con «…»
                    if (tw > room) { Glyph(w.Glyph, x + 16, y + h / 2, 7, bc); Text(w.Text, _body, x + 32, y, wd - 38, h, bc); }
                    else { double gx = x + wd / 2 - tw / 2 - 12; Glyph(w.Glyph, gx, y + h / 2, 7, bc); Text(w.Text, _body, gx + 14, y, tw + 8, h, bc); }
                }
                else Text(w.Text, _body, x, y, wd, h, bc, center: true);
                break;
            }
            case WidgetKind.Note:
            {
                using var l = _dw.CreateTextLayout(w.Text, _small, (float)wd - 8, (float)h); Brush(Fg(0.6f)); _ctx.DrawTextLayout(new Vector2((float)x + 4, (float)y + 2), l, _solid);
                break;
            }
            case WidgetKind.Month: DrawMonth(f); break;
            case WidgetKind.MenuItem:
            {
                bool hot = hover && w.Enabled;
                if (hot) FillRR(x, y, x + wd, y + h, 5, Accent);   // resalte azul, como en macOS
                var tc = hot ? new Color4(1, 1, 1, 1) : Fg(w.Enabled ? 0.92f : 0.38f);
                if (w.On) Glyph("check", x + 12, y + h / 2, 4.5f, tc);
                double subW = string.IsNullOrEmpty(w.Sub) ? 0 : Math.Min(120, MeasureText(w.Sub, _body) + 6);   // atajo («Ctrl+N») o «›» de submenú, a la derecha y atenuado, como en macOS
                Text(w.Text, _body, x + 22, y, wd - 30 - subW, h, tc);
                if (subW > 0) TextAlign(w.Sub!, _body, x + wd - subW - 10, y, subW, h, hot ? tc : Fg(w.Enabled ? 0.5f : 0.3f), TextAlignment.Trailing);
                break;
            }
            case WidgetKind.UserHeader when w.Data is UserHeaderData uh:
            {
                // WM-022: avatar with the initials, the full name (two lines when long, no absurd truncation), the session and the «Cuenta» button
                double r = 21, ax = x + 2 + r, ay = y + h / 2;
                FillCircle(ax, ay, (float)r, Accent); TextAlign(uh.Initials, _title, ax - r, ay - r, 2 * r, 2 * r, new Color4(1, 1, 1, 1), TextAlignment.Center);
                var ar = PanelLayout.UserAccountRect(f); double tx = ax + r + 12, tw = ar.X - tx - 8;
                bool two = MeasureText(w.Text, _bodyBold) > tw;
                if (two) { TextAlign(w.Text, _bodyBold, tx, y + 4, tw, 36, Fg(0.95f), vcenter: false, wrap: true); if (w.Sub is { } s2) Text(s2, _small, tx, y + 40, tw, 16, Fg(0.55f)); }
                else { Text(w.Text, _title, tx, y + 10, tw, 22, Fg(0.95f)); if (w.Sub is { } sub) Text(sub, _small, tx, y + 33, tw, 18, Fg(0.55f)); }
                bool hot = hover && HoverPoint is { } hp1 && hp1.X >= ar.X;
                FillRR(ar.X, ar.Y, ar.X + ar.W, ar.Y + ar.H, 14, Fill(hot ? 0.22f : 0.12f));
                TextAlign("Cuenta", _small, ar.X, ar.Y, ar.W, ar.H, Fg(0.9f), TextAlignment.Center);
                break;
            }
            case WidgetKind.ActionStrip when w.Data is ActionStripData sd:
            {
                // WM-022: five large round buttons (44 DIP) with their label under them (two lines if needed); same confirmations as always
                double cw = wd / sd.Actions.Count;
                for (int i = 0; i < sd.Actions.Count; i++)
                {
                    var a = sd.Actions[i]; double cx = x + cw * i + cw / 2, cy = y + 24;
                    bool hot = hover && HoverPoint is { } hp2 && hp2.X >= x + cw * i && hp2.X < x + cw * (i + 1);
                    FillCircle(cx, cy, 22, Fill(hot ? 0.26f : 0.13f));
                    Glyph(a.Glyph, cx, cy, 9.5f, Fg(0.95f));
                    using var l = _dw.CreateTextLayout(a.Label, _small, (float)(cw - 4), 30); l.TextAlignment = TextAlignment.Center; l.WordWrapping = WordWrapping.Wrap;
                    Brush(Fg(hot ? 0.95f : 0.72f)); _ctx.DrawTextLayout(new Vector2((float)(x + cw * i + 2), (float)(y + 50)), l, _solid);
                }

                break;
            }
            case WidgetKind.LinkRow when w.Data is LinkRowData lr:
            {
                double cw = wd / lr.Links.Count;
                for (int i = 0; i < lr.Links.Count; i++)
                {
                    var li = lr.Links[i]; bool hot = li.Enabled && hover && HoverPoint is { } hp3 && hp3.X >= x + cw * i && hp3.X < x + cw * (i + 1);
                    if (hot) FillRR(x + cw * i + 2, y + 2, x + cw * (i + 1) - 2, y + h - 2, 8, Fill(0.12f));
                    TextAlign(li.Label, _small, x + cw * i, y, cw, h, li.Enabled ? (hot ? Fg(0.95f) : Accent) : Fg(0.35f), TextAlignment.Center);
                }

                break;
            }
            case WidgetKind.TrayGrid when w.Data is TrayGridData g:
            {
                double cs = PanelLayout.TrayCell;
                for (int c = 0; c < g.Cells.Count; c++)
                {
                    double cx0 = x + c % g.Columns * cs, cy0 = y + c / g.Columns * cs;
                    if (HoverPoint is { } hp && hp.X >= cx0 && hp.X < cx0 + cs && hp.Y >= cy0 && hp.Y < cy0 + cs) FillRR(cx0 + 2, cy0 + 2, cx0 + cs - 2, cy0 + cs - 2, 7, Fill(0.16f));   // casilla bajo el ratón
                    if (c == TrayFocus) { Brush(Accent); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx0 + 2, cy0 + 2, cx0 + cs - 2, cy0 + cs - 2), RadiusX = 7, RadiusY = 7 }, _solid, 1.6f); }   // keyboard focus
                    if (IconFor(g.Cells[c].IconKey) is { } bmp) _ctx.DrawBitmap(bmp, R(cx0 + cs / 2 - 10, cy0 + cs / 2 - 10, cx0 + cs / 2 + 10, cy0 + cs / 2 + 10), 1f, BitmapInterpolationMode.Linear, null);
                    else FillCircle(cx0 + cs / 2, cy0 + cs / 2, 3, Fg(0.5f));   // icono ilegible: un punto discreto
                }
                break;
            }
            case WidgetKind.Separator when _kind is PanelKind.AppleMenu or PanelKind.AppMenu: Line(x + 8, y + h / 2, x + wd - 8, y + h / 2, Fill(0.16f), 1f); break;
            case WidgetKind.NcHeader or WidgetKind.NcSection or WidgetKind.NotifGroup or WidgetKind.NotifCard or WidgetKind.NcEmpty: DrawNcWidget(f, hover); break;
            default: DrawHubWidget(f, hover); break;
        }
    }

    private void DrawMonth(WidgetFrame f)
    {
        if (f.W.Data is not MonthData md) return; double cw = f.Width / 7;
        for (int c = 0; c < 7 && c < md.Headers.Count; c++) Text(md.Headers[c], _tiny, f.X + c * cw, f.Y, cw, PanelLayout.MonthHeader, Fg(0.5f), center: true);
        for (int i = 0; i < md.Days.Count && i < 42; i++)
        {
            var d = md.Days[i]; int row = i / 7, col = i % 7; double x = f.X + col * cw, y = f.Y + PanelLayout.MonthHeader + row * PanelLayout.MonthRow, cx = x + cw / 2, cy = y + PanelLayout.MonthRow / 2 - 1;
            bool selected = md.Selected == d.Date;
            if (d.IsToday) FillCircle(cx, cy, 13, Accent);
            else if (selected) { Brush(Fg(0.55f)); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), 13, 13), _solid, 1.2f); }
            Text(d.Date.Day.ToString(), _body, x, y - 1, cw, PanelLayout.MonthRow, d.IsToday ? new Color4(1, 1, 1, 1) : Fg(d.InMonth ? 1f : 0.3f), center: true);
            if (d.Events > 0) FillCircle(cx, y + PanelLayout.MonthRow - 4, 2, d.IsToday ? new Color4(1, 1, 1, 0.9f) : Accent);
        }
    }

    private void FillCircle(double cx, double cy, float r, Color4 c) { Brush(c); _ctx.FillEllipse(new Ellipse(new Vector2((float)cx, (float)cy), r, r), _solid); }

    private void Bars(double x, double cy, int n, Color4 c)
    {
        for (int i = 0; i < 4; i++) { double bh = 4 + i * 3.2; Brush(new Color4(c.R, c.G, c.B, c.A * (i < n ? 1f : 0.25f))); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(x + i * 4.5, cy + 6 - bh, x + i * 4.5 + 3, cy + 6), RadiusX = 1, RadiusY = 1 }, _solid); }
    }

    private ID2D1PathGeometry Poly(bool closed, params Vector2[] pts)
    {
        var g = _dev.Factory.CreatePathGeometry(); using var s = g.Open(); s.BeginFigure(pts[0], closed ? FigureBegin.Filled : FigureBegin.Hollow);
        for (int i = 1; i < pts.Length; i++) s.AddLine(pts[i]); s.EndFigure(closed ? FigureEnd.Closed : FigureEnd.Open); s.Close(); return g;
    }

    private ID2D1PathGeometry Arc(double ox, double oy, double r, double a0, double a1)
    {
        // WM-022: an arc of more than half a turn needs ArcSize.Large (it was always Small, so the power, restart and moon icons came out inverted)
        double sweep = ((a1 - a0) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        var g = _dev.Factory.CreatePathGeometry(); using var s = g.Open(); Vector2 P(double a) => new((float)(ox + r * Math.Cos(a)), (float)(oy + r * Math.Sin(a)));
        s.BeginFigure(P(a0), FigureBegin.Hollow); s.AddArc(new ArcSegment { Point = P(a1), Size = new Vortice.Mathematics.Size((float)r, (float)r), SweepDirection = SweepDirection.Clockwise, ArcSize = sweep > Math.PI ? ArcSize.Large : ArcSize.Small }); s.EndFigure(FigureEnd.Open); s.Close(); return g;
    }

    private void Line(double x1, double y1, double x2, double y2, Color4 c, float w = 1.5f) { Brush(c); _ctx.DrawLine(new Vector2((float)x1, (float)y1), new Vector2((float)x2, (float)y2), _solid, w, _round); }
    private void Stroke(ID2D1Geometry g, Color4 c, float w = 1.5f) { Brush(c); _ctx.DrawGeometry(g, _solid, w, _round); }

    /// <summary>Iconos vectoriales propios (sin fuentes ni imágenes de terceros), centrados en (cx, cy) con «radio» s.</summary>
    private void Glyph(string id, double cx, double cy, float s, Color4 c, int bars = -1)
    {
        switch (id)
        {
            case "wifi":
            {
                double oy = cy + s * 0.75; float[] r = [s * 0.45f, s * 0.95f, s * 1.45f];
                int active = bars < 0 ? 3 : (int)Math.Ceiling(bars * 3 / 4.0);   // 0 barras = sin señal; sin dato (-1) = todas
                for (int i = 0; i < 3; i++) { using var a = Arc(cx, oy, r[i], -3 * Math.PI / 4, -Math.PI / 4); Stroke(a, i < active ? c : new Color4(c.R, c.G, c.B, c.A * 0.3f)); }
                FillCircle(cx, oy - 0.2, 1.4f, c); break;
            }
            case "bluetooth": Line(cx - s * .5, cy - s * .55, cx + s * .5, cy + s * .55, c); Line(cx + s * .5, cy + s * .55, cx, cy + s, c); Line(cx, cy + s, cx, cy - s, c); Line(cx, cy - s, cx + s * .5, cy - s * .55, c); Line(cx + s * .5, cy - s * .55, cx - s * .5, cy + s * .55, c); break;
            case "sound": case "sound-muted":
            {
                using (var g = Poly(true, new Vector2((float)(cx - s * .9), (float)(cy - s * .35)), new Vector2((float)(cx - s * .35), (float)(cy - s * .35)), new Vector2((float)(cx + s * .25), (float)(cy - s * .9)), new Vector2((float)(cx + s * .25), (float)(cy + s * .9)), new Vector2((float)(cx - s * .35), (float)(cy + s * .35)), new Vector2((float)(cx - s * .9), (float)(cy + s * .35)))) { Brush(c); _ctx.FillGeometry(g, _solid); }
                if (id == "sound-muted") { Line(cx + s * .55, cy - s * .4, cx + s * 1.1, cy + s * .4, c); Line(cx + s * 1.1, cy - s * .4, cx + s * .55, cy + s * .4, c); }
                else { using var a1 = Arc(cx + s * .25, cy, s * .6, -Math.PI / 4, Math.PI / 4); Stroke(a1, c); using var a2 = Arc(cx + s * .25, cy, s * 1.0, -Math.PI / 4, Math.PI / 4); Stroke(a2, c); }
                break;
            }
            case "sun":
                Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), s * .45f, s * .45f), _solid, 1.5f);
                for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(cx + Math.Cos(a) * s * .75, cy + Math.Sin(a) * s * .75, cx + Math.Cos(a) * s * 1.05, cy + Math.Sin(a) * s * 1.05, c, 1.4f); }
                break;
            case "moon": { using var a = Arc(cx, cy, s * .9, -Math.PI * .15, Math.PI * 1.05); Stroke(a, c, 1.6f); using var b = Arc(cx + s * .35, cy - s * .15, s * .75, Math.PI * 1.1, -Math.PI * .1); Stroke(b, c, 1.6f); break; }
            case "power":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s, cy - s * .55, cx + s * .85, cy + s * .55), RadiusX = 2, RadiusY = 2 }, _solid, 1.5f);
                Line(cx + s * 1.05, cy - s * .2, cx + s * 1.05, cy + s * .2, c, 1.8f); break;
            case "display":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s, cy - s * .75, cx + s, cy + s * .35), RadiusX = 2, RadiusY = 2 }, _solid, 1.5f);
                Line(cx, cy + s * .35, cx, cy + s * .8, c); Line(cx - s * .5, cy + s * .85, cx + s * .5, cy + s * .85, c); break;
            case "bell": { using var a = Arc(cx, cy - s * .1, s * .65, Math.PI, 0); Stroke(a, c); Line(cx - s * .65, cy - s * .1, cx - s * .85, cy + s * .55, c); Line(cx + s * .65, cy - s * .1, cx + s * .85, cy + s * .55, c); Line(cx - s * .85, cy + s * .55, cx + s * .85, cy + s * .55, c); FillCircle(cx, cy + s * .85, 1.4f, c); break; }
            case "check": Line(cx - s * .6, cy, cx - s * .1, cy + s * .5, c, 1.9f); Line(cx - s * .1, cy + s * .5, cx + s * .7, cy - s * .5, c, 1.9f); break;
            case "lock": Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s * .7, cy - s * .15, cx + s * .7, cy + s * .8), RadiusX = 1.5f, RadiusY = 1.5f }, _solid, 1.3f); { using var a = Arc(cx, cy - s * .15, s * .45, Math.PI, 0); Stroke(a, c, 1.3f); } break;
            case "onoff": { using var a = Arc(cx, cy + s * .08, s * .82, -Math.PI * .32, Math.PI * 1.32); Stroke(a, c, 1.7f); Line(cx, cy - s * .95, cx, cy - s * .05, c, 1.8f); break; }
            case "restart": { using var a = Arc(cx, cy, s * .8, -Math.PI * .05, Math.PI * 1.55); Stroke(a, c, 1.7f); double ax = cx + s * .8, ay = cy - s * .04; Line(ax, ay, ax - s * .42, ay - s * .12, c, 1.7f); Line(ax, ay, ax + s * .1, ay - s * .44, c, 1.7f); break; }
            case "logout": Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s * .9, cy - s * .85, cx + s * .15, cy + s * .85), RadiusX = 2, RadiusY = 2 }, _solid, 1.5f); Line(cx - s * .25, cy, cx + s * .95, cy, c, 1.7f); Line(cx + s * .95, cy, cx + s * .6, cy - s * .35, c, 1.7f); Line(cx + s * .95, cy, cx + s * .6, cy + s * .35, c, 1.7f); break;
            case "chevron": Line(cx - s * .35, cy - s * .7, cx + s * .35, cy, c, 1.7f); Line(cx + s * .35, cy, cx - s * .35, cy + s * .7, c, 1.7f); break;
            case "back": Line(cx + s * .35, cy - s * .7, cx - s * .35, cy, c, 1.7f); Line(cx - s * .35, cy, cx + s * .35, cy + s * .7, c, 1.7f); break;
            default: HubGlyph(id, cx, cy, s, c); break;
        }
    }

    public void Dispose() { DisposeHub(); _solid.Dispose(); _round.Dispose(); _title.Dispose(); _body.Dispose(); _bodyBold.Dispose(); _small.Dispose(); _tiny.Dispose(); _dw.Dispose(); }
}
