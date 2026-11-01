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
        // tarjeta
        FillRR(0.5, 0.5, f.Width - 0.5, f.Height - 0.5, 16, Dark ? new Color4(0.13f, 0.13f, 0.15f, 0.94f) : new Color4(0.96f, 0.96f, 0.97f, 0.95f));
        Brush(Fill(0.14f)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(0.5, 0.5, f.Width - 0.5, f.Height - 0.5), RadiusX = 16, RadiusY = 16 }, _solid, 1f);
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
        var g = _dev.Factory.CreatePathGeometry(); using var s = g.Open(); Vector2 P(double a) => new((float)(ox + r * Math.Cos(a)), (float)(oy + r * Math.Sin(a)));
        s.BeginFigure(P(a0), FigureBegin.Hollow); s.AddArc(new ArcSegment { Point = P(a1), Size = new Vortice.Mathematics.Size((float)r, (float)r), SweepDirection = SweepDirection.Clockwise, ArcSize = ArcSize.Small }); s.EndFigure(FigureEnd.Open); s.Close(); return g;
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
            case "chevron": Line(cx - s * .35, cy - s * .7, cx + s * .35, cy, c, 1.7f); Line(cx + s * .35, cy, cx - s * .35, cy + s * .7, c, 1.7f); break;
            case "back": Line(cx + s * .35, cy - s * .7, cx - s * .35, cy, c, 1.7f); Line(cx - s * .35, cy, cx + s * .35, cy + s * .7, c, 1.7f); break;
            default: HubGlyph(id, cx, cy, s, c); break;
        }
    }

    public void Dispose() { DisposeHub(); _solid.Dispose(); _round.Dispose(); _title.Dispose(); _body.Dispose(); _bodyBold.Dispose(); _small.Dispose(); _tiny.Dispose(); _dw.Dispose(); }
}
