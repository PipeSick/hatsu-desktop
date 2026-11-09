using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Host.App;

/// <summary>
/// Dibuja la ventana de Ajustes con Direct2D en el estilo de las listas agrupadas de iOS: barra lateral con iconos de color, tarjetas redondeadas, interruptores verdes, controles segmentados,
/// deslizador y menú de sugerencias. Todo vectorial y propio. Unidades en DIP; el dispositivo ya trae el DPI.
/// </summary>
/// <summary>Estado de edición de un campo (lo que el cuadro de entrada nativo contiene): se dibuja aquí mismo porque un control hijo de Windows no se ve sobre una superficie DXGI.</summary>
internal sealed record EditView(string Id, string Text, int SelStart, int SelEnd, bool CaretOn);

internal sealed class SettingsPainter : IDisposable
{
    private readonly D2DDevice _dev; private readonly ID2D1DeviceContext _ctx; private readonly IDWriteFactory _dw;
    private readonly IDWriteTextFormat _title, _side, _sideSel, _body, _bodyBold, _sub, _head, _small;
    private readonly ID2D1SolidColorBrush _b; private readonly ID2D1StrokeStyle _round;
    private readonly bool _dark;

    // paleta (iOS): fondo, barra lateral, tarjeta, separador, etiqueta, secundaria, acento, verde, campo, pista del interruptor
    private Color4 Bg => _dark ? C(0x000000) : C(0xF2F2F7);
    private Color4 Side => _dark ? C(0x161618) : C(0xE8E8ED);
    private Color4 Card => _dark ? C(0x1C1C1E) : C(0xFFFFFF);
    private Color4 Sep => _dark ? C(0x38383A) : C(0xD1D1D6);
    private Color4 Label(float a = 1f) => _dark ? new Color4(1, 1, 1, a) : new Color4(0, 0, 0, a);
    private Color4 Secondary => _dark ? C(0x98989F) : C(0x6C6C70);
    private Color4 Tint => _dark ? C(0x0A84FF) : C(0x007AFF);
    private static Color4 Green => C(0x34C759);
    private Color4 Field => _dark ? C(0x2C2C2E) : C(0xEEEEF3);
    private Color4 Off => _dark ? C(0x39393D) : C(0xE5E5EA);
    private Color4 SegSel => _dark ? C(0x636366) : C(0xFFFFFF);
    private static Color4 C(int rgb, float a = 1f) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    private static RawRectF R(double l, double t, double r, double b) => new((float)l, (float)t, (float)r, (float)b);

    public SettingsPainter(D2DDevice dev, bool dark)
    {
        _dev = dev; _ctx = dev.Context; _dark = dark; _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _title = _dw.CreateTextFormat("Segoe UI Semibold", 28); _side = _dw.CreateTextFormat("Segoe UI", 15); _sideSel = _dw.CreateTextFormat("Segoe UI Semibold", 15);
        _body = _dw.CreateTextFormat("Segoe UI", 15); _bodyBold = _dw.CreateTextFormat("Segoe UI Semibold", 13.5f); _sub = _dw.CreateTextFormat("Segoe UI", 12.5f);
        _head = _dw.CreateTextFormat("Segoe UI", 12); _small = _dw.CreateTextFormat("Segoe UI", 13.5f);
        _round = dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
    }

    private void Brush(Color4 c) => _b.Color = c;
    private void RR(double x, double y, double w, double h, double r, Color4 c) { Brush(c); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(x, y, x + w, y + h), RadiusX = (float)r, RadiusY = (float)r }, _b); }
    private void RRStroke(double x, double y, double w, double h, double r, Color4 c, float t = 1f) { Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(x + t / 2, y + t / 2, x + w - t / 2, y + h - t / 2), RadiusX = (float)r, RadiusY = (float)r }, _b, t); }
    private void Dot(double cx, double cy, double r, Color4 c) { Brush(c); _ctx.FillEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)r, (float)r), _b); }
    private void Ln(double x1, double y1, double x2, double y2, Color4 c, float t = 1.5f) { Brush(c); _ctx.DrawLine(new Vector2((float)x1, (float)y1), new Vector2((float)x2, (float)y2), _b, t, _round); }

    private void Text(string? s, IDWriteTextFormat f, double x, double y, double w, double h, Color4 c, bool right = false, bool center = false, bool wrap = false, bool top = false)
    {
        if (string.IsNullOrEmpty(s)) return;
        using var l = _dw.CreateTextLayout(s, f, (float)Math.Max(1, w), (float)Math.Max(1, h)); l.WordWrapping = wrap ? WordWrapping.Wrap : WordWrapping.NoWrap; l.ParagraphAlignment = top ? ParagraphAlignment.Near : ParagraphAlignment.Center;
        l.TextAlignment = right ? TextAlignment.Trailing : center ? TextAlignment.Center : TextAlignment.Leading;
        if (!wrap) { using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell); }
        Brush(c); _ctx.DrawTextLayout(new Vector2((float)x, (float)y), l, _b);
    }

    private double Measure(string s, IDWriteTextFormat f) { using var l = _dw.CreateTextLayout(s, f, 4000, 40); return l.Metrics.WidthIncludingTrailingWhitespace; }

    // ───────────────────────────── iconos
    private void Glyph(string id, double cx, double cy, double s, Color4 c)
    {
        switch (id)
        {
            case "gear": Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)(s * .42), (float)(s * .42)), _b, 1.6f); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Ln(cx + Math.Cos(a) * s * .62, cy + Math.Sin(a) * s * .62, cx + Math.Cos(a) * s * .92, cy + Math.Sin(a) * s * .92, c, 2.4f); } break;
            case "bar": RRStroke(cx - s, cy - s * .62, s * 2, s * 1.24, 3, c, 1.6f); Ln(cx - s * .7, cy - s * .22, cx + s * .7, cy - s * .22, c, 1.6f); break;
            case "globe": Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)s, (float)s), _b, 1.5f); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)(s * .42), (float)s), _b, 1.3f); Ln(cx - s, cy, cx + s, cy, c, 1.3f); break;
            case "island": RR(cx - s * .95, cy - s * .38, s * 1.9, s * .76, s * .38, c); break;
            case "phone": RRStroke(cx - s * .5, cy - s * .9, s, s * 1.8, s * .22, c, 1.6f); Dot(cx, cy + s * .62, 1.6, c); break;
            case "puzzle": RRStroke(cx - s * .7, cy - s * .55, s * 1.25, s * 1.25, s * .2, c, 1.6f); Dot(cx + s * .7, cy + s * .08, 2.4, c); Dot(cx - s * .08, cy - s * .7, 2.4, c); break;
            case "dock": RRStroke(cx - s, cy + s * .05, s * 2, s * .62, s * .2, c, 1.6f); for (int i = -1; i <= 1; i++) RR(cx + i * s * .55 - s * .18, cy - s * .55, s * .36, s * .36, s * .1, c); break;
            case "camera": RRStroke(cx - s, cy - s * .55, s * 2, s * 1.3, s * .25, c, 1.6f); Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)(cy + s * .1)), (float)(s * .38), (float)(s * .38)), _b, 1.5f); break;
            case "search": Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)(cx - s * .18), (float)(cy - s * .18)), (float)(s * .58), (float)(s * .58)), _b, 1.7f); Ln(cx + s * .25, cy + s * .25, cx + s * .85, cy + s * .85, c, 2f); break;
            case "clipboard": RRStroke(cx - s * .72, cy - s * .7, s * 1.44, s * 1.6, s * .2, c, 1.6f); RR(cx - s * .35, cy - s * .9, s * .7, s * .34, s * .1, c); break;
            case "bell": RRStroke(cx - s * .62, cy - s * .78, s * 1.24, s * 1.3, s * .55, c, 1.6f); Ln(cx - s * .9, cy + s * .5, cx + s * .9, cy + s * .5, c, 1.6f); Dot(cx, cy + s * .82, 1.4, c); break;
            case "chevron": Ln(cx - s * .3, cy - s * .62, cx + s * .3, cy, c, 1.8f); Ln(cx + s * .3, cy, cx - s * .3, cy + s * .62, c, 1.8f); break;
            case "up": Ln(cx - s * .55, cy + s * .25, cx, cy - s * .3, c, 1.8f); Ln(cx, cy - s * .3, cx + s * .55, cy + s * .25, c, 1.8f); break;
            case "down": Ln(cx - s * .55, cy - s * .25, cx, cy + s * .3, c, 1.8f); Ln(cx, cy + s * .3, cx + s * .55, cy - s * .25, c, 1.8f); break;
            case "info": Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)s, (float)s), _b, 1.5f); Ln(cx, cy - s * .08, cx, cy + s * .5, c, 1.8f); Dot(cx, cy - s * .42, 1.3, c); break;
        }
    }

    private static readonly int[] IconColors = [0x8E8E93, 0x0A84FF, 0x30B0C7, 0xAF52DE];

    // ───────────────────────────── ventana
    /// <param name="edit">Campo en edición (texto, selección y cursor se dibujan aquí).</param>
    public void Paint(SettingsController c, double w, double h, EditView? edit, bool clear = true)
    {
        _ctx.BeginDraw(); if (clear) _ctx.Clear(Bg);
        _searchEdit = edit is { Id: "search" } ? edit : null;
        DrawSidebar(c, h);
        double ox = SettingsLayout.SidebarW, oy = SettingsLayout.TitleH, cw = SettingsLayout.ContentW;
        if (c.Page is { } page)
        {
            var f = c.Frame;
            _ctx.PushAxisAlignedClip(R(ox, oy, w, h), AntialiasMode.Aliased);
            _ctx.Transform = Matrix3x2.CreateTranslation((float)ox, (float)oy);
            foreach (var g in f.Groups) DrawGroup(g, f, c, edit);
            if (f.MaxScroll > 0.5) { double th = Math.Max(30, SettingsLayout.ViewportH * SettingsLayout.ViewportH / f.ContentHeight), ty = 6 + (SettingsLayout.ViewportH - th - 12) * f.Scroll / f.MaxScroll; RR(cw - 7, ty, 3.5, th, 1.75, Label(0.28f)); }
            DrawSuggestions(c, f);
            _ctx.Transform = Matrix3x2.Identity; _ctx.PopAxisAlignedClip();
            Text(page.Title, _title, ox + SettingsLayout.Pad, 18, cw - 2 * SettingsLayout.Pad, 44, Label());
            if (f.Scroll > 1) Ln(ox, oy, w, oy, Sep, 1f);
        }

        _ctx.EndDraw();
    }

    private EditView? _searchEdit;
    private void DrawSidebar(SettingsController c, double h)
    {
        Brush(Side); _ctx.FillRectangle(R(0, 0, SettingsLayout.SidebarW, h), _b); Ln(SettingsLayout.SidebarW - 0.5, 0, SettingsLayout.SidebarW - 0.5, h, Sep, 1f);
        Text("Ajustes", _title, 20, 8, SettingsLayout.SidebarW - 32, 44, Label());
        DrawField(c.SearchFrame, _searchEdit, 1f); Glyph("search", SettingsLayout.FieldRect(c.SearchFrame).X + SettingsLayout.FieldRect(c.SearchFrame).W - 18, SettingsLayout.FieldRect(c.SearchFrame).Y + 16, 6.5, Secondary);
        foreach (var e in SettingsLayout.SideEntries(c.Pages))
        {
            int i = e.PageIndex; var r = e.Rect; bool sel = i == c.PageIndex, hov = i == c.HoverSide && !sel;
            if (e.Header is { } hd) Text(hd.ToUpperInvariant(), _head, r.X + 10, e.HeaderY, r.W - 12, SettingsLayout.SideHeaderH, Secondary);
            if (sel) RR(r.X, r.Y, r.W, r.H, 10, new Color4(Tint.R, Tint.G, Tint.B, _dark ? 0.28f : 0.16f)); else if (hov) RR(r.X, r.Y, r.W, r.H, 10, Label(0.06f));
            RR(r.X + 8, r.Y + 4, 24, 24, 6, C(IconColors[i % IconColors.Length])); Glyph(c.Pages[i].Glyph, r.X + 20, r.Y + 16, 7.5, new Color4(1, 1, 1, 1));
            Text(c.Pages[i].Title, sel ? _sideSel : _side, r.X + 42, r.Y, r.W - 48, r.H, sel ? Tint : Label());
        }
    }

    private void DrawGroup(GroupFrame g, PageFrame f, SettingsController c, EditView? edit)
    {
        if (!string.IsNullOrEmpty(g.Group.Header)) Text(g.Group.Header!.ToUpperInvariant(), _head, g.X + 16, g.HeaderY + 4, g.W - 32, 24, Secondary);
        RR(g.X, g.Y, g.W, g.H, SettingsLayout.Radius, Card); if (!_dark) RRStroke(g.X, g.Y, g.W, g.H, SettingsLayout.Radius, Sep, 0.6f);
        _ctx.PushAxisAlignedClip(R(g.X, g.Y, g.X + g.W, g.Y + g.H), AntialiasMode.Aliased);   // el resalte de una fila no se sale de las esquinas redondeadas
        foreach (var r in f.Rows.Where(r => g.Group.Rows.Contains(r.Row)))
        {
            bool hover = c.Hover is { } hv && hv.Id == r.Row.Id && r.Row.Kind is RowKind.Toggle or RowKind.Button;
            if (hover) { Brush(Label(0.05f)); _ctx.FillRectangle(R(r.X, r.Y, r.X + r.W, r.Y + r.H), _b); }
            DrawRow(r, c, edit);
            if (!r.Last) Ln(r.X + 16, r.Y + r.H - 0.5, r.X + r.W, r.Y + r.H - 0.5, Sep, 0.8f);
        }

        _ctx.PopAxisAlignedClip();
        if (g.FooterH > 0) Text(g.Group.Footer, _sub, g.X + 16, g.FooterY, g.W - 32, g.FooterH, Secondary, wrap: true, top: true);
    }

    private void DrawRow(RowFrame r, SettingsController c, EditView? edit)
    {
        var row = r.Row; float dim = row.Enabled ? 1f : 0.42f; double x = r.X + 16, midY = r.Y + r.H / 2;
        bool hasSub = !string.IsNullOrEmpty(row.Subtitle) || (!row.Enabled && !string.IsNullOrEmpty(row.Reason));
        if (row.Kind == RowKind.Preview)
        {   // tira con la barra de arriba tal como queda: el nombre, los menús, los iconos en su orden y la hora
            double bx = r.X + 16, by = r.Y + 8, bw = r.W - 32, bh = r.H - 16; var white = new Color4(1, 1, 1, 0.9f);
            RR(bx, by, bw, bh, 10, new Color4(0.06f, 0.04f, 0.12f, 1));
            Text("Hatsu    Archivo    Editar    Ver", _sub, bx + 14, by, bw * 0.4, bh, white);
            double ix = bx + bw - 14 - 38; Text("16:08", _sub, ix, by, 40, bh, white);
            foreach (var name in (row.Options ?? []).Reverse()) { ix -= 26; RR(ix, by + bh / 2 - 8, 18, 16, 5, new Color4(1, 1, 1, 0.28f)); }
            return;
        }

        if (row.Kind == RowKind.Info) { Glyph("info", x + 10, midY, 9, Secondary); Text(row.Title, _sub, x + 30, r.Y + 4, r.W - 62, r.H - 8, Secondary, wrap: true); return; }
        double tw = r.W - 32 - ControlSpace(row);
        if (row.Kind == RowKind.Button) { Text(row.Title, _body, x, r.Y, r.W - 60, r.H, row.Enabled ? Tint : Label(0.42f)); if (row.Enabled) Glyph("chevron", r.X + r.W - 20, midY, 6, Secondary); return; }
        if (hasSub) { var sub = SettingsLayout.SubText(row); Text(row.Title, _body, x, r.Y + 8, tw, 22, Label(dim)); Text(sub, _sub, x, r.Y + 30, Math.Max(40, tw), SettingsLayout.SubLineH * SettingsLayout.Lines(sub, tw) + 3, Secondary, wrap: true, top: true); }
        else Text(row.Title, _body, x, r.Y, tw, r.H, Label(dim));
        switch (row.Kind)
        {
            case RowKind.Toggle:
            {
                DrawSwitch(SettingsLayout.SwitchRect(r), row.On, dim);
                if (row.CanUp || row.CanDown) { var u = SettingsLayout.UpRect(r); var d = SettingsLayout.DownRect(r); Glyph("up", u.X + u.W / 2, u.Y + u.H / 2, 7, Label(row.CanUp ? 0.7f : 0.2f)); Glyph("down", d.X + d.W / 2, d.Y + d.H / 2, 7, Label(row.CanDown ? 0.7f : 0.2f)); }
                break;
            }
            case RowKind.Segmented: DrawSegmented(r, dim); break;
            case RowKind.Field: DrawField(r, edit is not null && edit.Id == row.Id ? edit : null, dim); break;
            case RowKind.Slider:
            {
                var (x0, x1, cy) = SettingsLayout.SliderTrack(r); double v = c.Dragging is { } dg && dg.Id == row.Id ? dg.Value : row.Value, px = x0 + (x1 - x0) * v / 100;
                RR(x0, cy - 2, x1 - x0, 4, 2, Off); RR(x0, cy - 2, px - x0, 4, 2, new Color4(Tint.R, Tint.G, Tint.B, dim));
                Dot(px, cy + 1, 13, new Color4(0, 0, 0, 0.18f)); Dot(px, cy, 12, new Color4(1, 1, 1, dim)); RRStroke(px - 12, cy - 12, 24, 24, 12, new Color4(0, 0, 0, _dark ? 0.35f : 0.12f), 0.8f);
                Text(row.SliderLabel(v), _small, x1 + 2, r.Y, 44, r.H, Secondary, right: true); break;
            }
        }
    }

    private static double ControlSpace(SettingsRow r) => SettingsLayout.ControlSpace(r);

    private void DrawSwitch((double X, double Y, double W, double H) s, bool on, float dim)
    {
        RR(s.X, s.Y, s.W, s.H, s.H / 2, on ? new Color4(Green.R, Green.G, Green.B, dim) : new Color4(Off.R, Off.G, Off.B, dim));
        double kx = on ? s.X + s.W - s.H / 2 : s.X + s.H / 2, cy = s.Y + s.H / 2;
        Dot(kx, cy + 1.2, 13.5, new Color4(0, 0, 0, 0.18f * dim)); Dot(kx, cy, 13.5, new Color4(1, 1, 1, dim));
    }

    private void DrawSegmented(RowFrame r, float dim)
    {
        var s = SettingsLayout.SegmentRect(r); var o = r.Row.Options ?? []; if (o.Count == 0) return; double sw = s.W / o.Count;
        RR(s.X, s.Y, s.W, s.H, 9, new Color4(Field.R, Field.G, Field.B, dim));
        if (r.Row.Selected >= 0 && r.Row.Selected < o.Count) { RR(s.X + sw * r.Row.Selected + 2, s.Y + 2, sw - 4, s.H - 4, 7, new Color4(SegSel.R, SegSel.G, SegSel.B, dim)); }
        for (int i = 0; i < o.Count; i++) Text(o[i], i == r.Row.Selected ? _bodyBold : _small, s.X + sw * i, s.Y, sw, s.H, Label(dim * (i == r.Row.Selected ? 1f : 0.78f)), center: true);
    }

    private const double FieldPadX = 11;

    private IDWriteTextLayout Layout(string text, double h) { var l = _dw.CreateTextLayout(text.Length == 0 ? " " : text, _small, 4000, (float)h); l.WordWrapping = WordWrapping.NoWrap; l.ParagraphAlignment = ParagraphAlignment.Center; return l; }

    private static double XOf(IDWriteTextLayout l, string text, int idx)
    {
        if (idx <= 0 || text.Length == 0) return 0; idx = Math.Min(idx, text.Length); l.HitTestTextPosition((uint)(idx - 1), true, out float x, out _, out _); return x;
    }

    /// <summary>Cuánto se desplaza el texto a la izquierda para que el cursor siga a la vista en un campo de ancho <paramref name="fieldW"/>.</summary>
    public double EditScroll(string text, int caret, double fieldW)
    {
        using var l = Layout(text, 30); double inner = fieldW - 2 * FieldPadX; return Math.Max(0, XOf(l, text, caret) - inner + 2);
    }

    /// <summary>Posición de texto (0…n) más cercana a un clic a <paramref name="xInText"/> DIP del inicio del texto (ya sumado el desplazamiento).</summary>
    public int IndexAt(string text, double xInText)
    {
        if (text.Length == 0) return 0; using var l = Layout(text, 30); var m = l.HitTestPoint((float)Math.Max(0, xInText), 15, out var trailing, out _);
        return Math.Clamp((int)m.TextPosition + (trailing ? 1 : 0), 0, text.Length);
    }

    private void DrawField(RowFrame r, EditView? e, float dim)
    {
        var f = SettingsLayout.FieldRect(r); RR(f.X, f.Y, f.W, f.H, 9, new Color4(Field.R, Field.G, Field.B, dim));
        if (e is null)
        {
            bool empty = string.IsNullOrEmpty(r.Row.Text);
            Text(empty ? r.Row.Placeholder : r.Row.Text, _small, f.X + FieldPadX, f.Y, f.W - 2 * FieldPadX, f.H, empty ? Secondary : Label(dim)); return;
        }

        RRStroke(f.X, f.Y, f.W, f.H, 9, Tint, 1.6f);
        double inner = f.W - 2 * FieldPadX, x0 = f.X + FieldPadX; int caret = Math.Clamp(e.SelEnd, 0, e.Text.Length), a = Math.Clamp(Math.Min(e.SelStart, e.SelEnd), 0, e.Text.Length), b = Math.Clamp(Math.Max(e.SelStart, e.SelEnd), 0, e.Text.Length);
        using var l = Layout(e.Text, f.H); double scroll = Math.Max(0, XOf(l, e.Text, caret) - inner + 2);
        _ctx.PushAxisAlignedClip(R(x0, f.Y, x0 + inner, f.Y + f.H), AntialiasMode.Aliased);
        if (e.Text.Length == 0) Text(r.Row.Placeholder, _small, x0, f.Y, inner, f.H, Secondary);
        if (b > a) { double sx = XOf(l, e.Text, a), ex = XOf(l, e.Text, b); RR(x0 - scroll + sx, f.Y + 6, ex - sx, f.H - 12, 3, new Color4(Tint.R, Tint.G, Tint.B, _dark ? 0.45f : 0.30f)); }
        if (e.Text.Length > 0) { Brush(Label()); _ctx.DrawTextLayout(new Vector2((float)(x0 - scroll), (float)f.Y), l, _b); }
        if (e.CaretOn && a == b) { double cx = x0 - scroll + XOf(l, e.Text, caret); Ln(cx, f.Y + 7, cx, f.Y + f.H - 7, Label(), 1.4f); }
        _ctx.PopAxisAlignedClip();
    }

    private void DrawSuggestions(SettingsController c, PageFrame f)
    {
        if (c.SuggestFor is null || c.Suggestions.Count == 0 || c.RowOf(c.SuggestFor) is not { } row) return;
        var s = SettingsLayout.SuggestRect(row, c.Suggestions.Count);
        RR(s.X + 1, s.Y + 3, s.W, s.H, 12, new Color4(0, 0, 0, _dark ? 0.45f : 0.16f)); RR(s.X, s.Y, s.W, s.H, 12, _dark ? C(0x2C2C2E) : C(0xFFFFFF)); RRStroke(s.X, s.Y, s.W, s.H, 12, Sep, 0.8f);
        for (int i = 0; i < c.Suggestions.Count; i++)
        {
            double y = s.Y + 4 + i * SettingsLayout.SuggestRowH; if (i == c.SuggestIndex) RR(s.X + 4, y, s.W - 8, SettingsLayout.SuggestRowH, 8, new Color4(Tint.R, Tint.G, Tint.B, _dark ? 0.35f : 0.16f));
            double off = c.SuggestHasChecks ? 24 : 0;
            if (c.SuggestHasChecks && i == c.SuggestChecked) Text("✓", _small, s.X + 14, y, 18, SettingsLayout.SuggestRowH, new Color4(Tint.R, Tint.G, Tint.B, 1f));
            Text(c.Suggestions[i], _small, s.X + 16 + off, y, s.W - 32 - off, SettingsLayout.SuggestRowH, Label());
        }
    }

    public void Dispose() { _b.Dispose(); _round.Dispose(); _title.Dispose(); _side.Dispose(); _sideSel.Dispose(); _body.Dispose(); _bodyBold.Dispose(); _sub.Dispose(); _head.Dispose(); _small.Dispose(); _dw.Dispose(); }
}
