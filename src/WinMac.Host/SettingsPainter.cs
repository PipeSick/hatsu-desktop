using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using WinMac.Dock.Win.Rendering;
using WinMac.Host.SettingsV2;

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
    private readonly IDWriteTextFormat _section, _sideTitle, _cardTitle;

    // Settings V2: los colores salen de SettingsPalette (claro, oscuro o el tema de Windows); aquí solo se nombran para el dibujo.
    private readonly SettingsPalette _p;
    private static Color4 K(Rgba c, float mul = 1f) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f * mul);
    private Color4 Bg => K(_p.BackdropBottom);
    private Color4 Side => K(_p.Sidebar);
    private Color4 Card => K(_p.Surface);
    private Color4 Raised => K(_p.SurfaceRaised);
    private Color4 Sep => K(_p.Stroke);
    private Color4 Label(float a = 1f) => K(_p.Text, a);
    private Color4 Secondary => K(_p.TextSecondary);
    private Color4 Tint => K(_p.Accent);
    private Color4 TintSoft => K(_p.AccentSoft);
    private Color4 OnTint => K(_p.OnAccent);
    private Color4 Green => K(_p.Success);
    private Color4 Field => K(_p.Field);
    private Color4 Off => K(_p.SwitchOff);
    private Color4 SegSel => K(_p.AccentSoft);
    private static Color4 C(int rgb, float a = 1f) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    private static RawRectF R(double l, double t, double r, double b) => new((float)l, (float)t, (float)r, (float)b);

    public SettingsPainter(D2DDevice dev, SettingsPalette palette)
    {
        _p = palette; _dev = dev; _ctx = dev.Context; _dark = palette.Dark; _b = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1)); _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _title = _dw.CreateTextFormat("Segoe UI Semibold", 28); _side = _dw.CreateTextFormat("Segoe UI", 15); _sideSel = _dw.CreateTextFormat("Segoe UI Semibold", 15);
        _body = _dw.CreateTextFormat("Segoe UI", 15); _bodyBold = _dw.CreateTextFormat("Segoe UI Semibold", 13.5f); _sub = _dw.CreateTextFormat("Segoe UI", 12.5f);
        _head = _dw.CreateTextFormat("Segoe UI", 12); _small = _dw.CreateTextFormat("Segoe UI", 13.5f);
        _section = _dw.CreateTextFormat("Segoe UI Semibold", 17); _sideTitle = _dw.CreateTextFormat("Segoe UI Semibold", 22); _cardTitle = _dw.CreateTextFormat("Segoe UI Semibold", 14.5f);
        SettingsLayout.Measurer = new DirectWriteMeasurer(_dw);   // la altura de cada fila se mide con el mismo motor que dibuja
        _round = dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
    }

    private void Pill(double x, double y, double w, double h, string text, bool primary = true)
    {
        RR(x, y, w, h, h / 2, primary ? Tint : Label(0.10f));
        Text(text, _small, x, y, w, h, primary ? OnTint : Label(), center: true);
    }

    /// <summary>El asa ⋮⋮ (dos columnas de tres puntos) para arrastrar un icono de la barra.</summary>
    private void Grip(double cx, double cy, Color4 c) { for (int i = -1; i <= 1; i++) { Dot(cx - 3, cy + i * 5, 1.5, c); Dot(cx + 3, cy + i * 5, 1.5, c); } }

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
            case "grid": for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) RRStroke(cx - s * .85 + i * s * .95, cy - s * .85 + j * s * .95, s * .75, s * .75, s * .15, c, 1.5f); break;
            case "snap": RRStroke(cx - s, cy - s * .75, s * 2, s * 1.5, s * .2, c, 1.5f); Ln(cx, cy - s * .75, cx, cy + s * .75, c, 1.5f); break;
            case "text": Ln(cx - s * .8, cy - s * .55, cx + s * .8, cy - s * .55, c, 1.8f); Ln(cx, cy - s * .55, cx, cy + s * .8, c, 1.8f); Ln(cx - s * .45, cy + s * .8, cx + s * .45, cy + s * .8, c, 1.5f); break;
            case "gesture": for (int i = 0; i < 3; i++) Dot(cx - s * .6 + i * s * .6, cy + (i == 1 ? -s * .25 : s * .25), 2.2, c); Ln(cx - s * .6, cy + s * .25, cx, cy - s * .25, c, 1.3f); Ln(cx, cy - s * .25, cx + s * .6, cy + s * .25, c, 1.3f); break;
            case "shelf": RRStroke(cx - s * .9, cy - s * .2, s * 1.8, s * .95, s * .2, c, 1.6f); Ln(cx - s * .9, cy - s * .2, cx - s * .5, cy - s * .7, c, 1.5f); Ln(cx - s * .5, cy - s * .7, cx + s * .5, cy - s * .7, c, 1.5f); Ln(cx + s * .5, cy - s * .7, cx + s * .9, cy - s * .2, c, 1.5f); break;
            case "periph": RRStroke(cx - s * .55, cy - s * .9, s * 1.1, s * 1.8, s * .55, c, 1.6f); Ln(cx, cy - s * .9, cx, cy - s * .15, c, 1.5f); break;
            case "chevron": Ln(cx - s * .3, cy - s * .62, cx + s * .3, cy, c, 1.8f); Ln(cx + s * .3, cy, cx - s * .3, cy + s * .62, c, 1.8f); break;
            case "up": Ln(cx - s * .55, cy + s * .25, cx, cy - s * .3, c, 1.8f); Ln(cx, cy - s * .3, cx + s * .55, cy + s * .25, c, 1.8f); break;
            case "down": Ln(cx - s * .55, cy - s * .25, cx, cy + s * .3, c, 1.8f); Ln(cx, cy + s * .3, cx + s * .55, cy - s * .25, c, 1.8f); break;
            case "info": Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)s, (float)s), _b, 1.5f); Ln(cx, cy - s * .08, cx, cy + s * .5, c, 1.8f); Dot(cx, cy - s * .42, 1.3, c); break;
        }
    }

    private static readonly Dictionary<string, int> PageColors = new()
    {
        ["general"] = 0x8B5CF6, ["bar"] = 0x3B82F6, ["dock"] = 0x14B8A6, ["notch"] = 0xA855F7, ["modules"] = 0x8E8AA6, ["gestures"] = 0x0EA5E9, ["spot"] = 0x06B6D4,
        ["nc"] = 0xDB5EA9, ["capture"] = 0x8E8AA6, ["weather"] = 0x3B9BEF, ["connect"] = 0x3B82F6, ["periph"] = 0x14A38B, ["ext"] = 0x8B5CF6,
    };
    private static readonly Dictionary<string, int> GlyphColors = new() { ["bar"] = 0x3B82F6, ["dock"] = 0x14B8A6, ["island"] = 0xA855F7, ["phone"] = 0x3B82F6, ["grid"] = 0x6366F1, ["snap"] = 0x0EA5E9, ["text"] = 0xF59E0B, ["search"] = 0x06B6D4, ["gesture"] = 0xDB5EA9, ["shelf"] = 0x14A38B };
    private static int ColorOfGlyph(string? g) => g is not null && GlyphColors.TryGetValue(g, out var c) ? c : 0x8B5CF6;
    private static int ColorOf(string pageId) => PageColors.TryGetValue(pageId, out var c) ? c : 0x8B5CF6;

    /// <summary>Degradado lineal entre dos colores en un rectángulo (vertical u horizontal).</summary>
    private void Gradient(double x, double y, double w, double h, Color4 a, Color4 b, double radius = 0, bool vertical = true)
    {
        using var stops = _ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = a }, new GradientStop { Position = 1, Color = b }]);
        using var br = _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2((float)x, (float)y), EndPoint = vertical ? new Vector2((float)x, (float)(y + h)) : new Vector2((float)(x + w), (float)y) }, stops);
        if (radius > 0) _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(x, y, x + w, y + h), RadiusX = (float)radius, RadiusY = (float)radius }, br); else _ctx.FillRectangle(R(x, y, x + w, y + h), br);
    }

    /// <summary>Sombra suave bajo una superficie: capas translúcidas cada vez más grandes.</summary>
    private void Shadow(double x, double y, double w, double h, double r)
    {
        for (int i = 4; i >= 1; i--) RR(x - i, y + i * 1.6, w + 2 * i, h + 2 * i, r + i, K(_p.Shadow, 0.16f));
    }

    private void Tile(double x, double y, double size, int rgb, string glyph)
    {
        var top = C(rgb); var bottom = new Color4(top.R * 0.78f, top.G * 0.78f, top.B * 0.78f, 1f);
        Gradient(x, y, size, size, top, bottom, size * 0.28); Glyph(glyph, x + size / 2, y + size / 2, size * 0.28, new Color4(1, 1, 1, 1));
    }

    // ───────────────────────────── ventana
    /// <param name="edit">Campo en edición (texto, selección y cursor se dibujan aquí).</param>
    public void Paint(SettingsController c, double w, double h, EditView? edit, bool clear = true)
    {
        _ctx.BeginDraw(); if (clear) _ctx.Clear(Bg);
        Gradient(0, 0, w, h, K(_p.BackdropTop), K(_p.BackdropBottom));
        _searchEdit = edit is { Id: "search" } ? edit : null;
        DrawSidebar(c, h);
        double ox = SettingsLayout.SidebarW, oy = SettingsLayout.TitleH, cw = SettingsLayout.ContentW;
        if (c.Page is { } page)
        {
            var f = c.Frame;
            _ctx.PushAxisAlignedClip(R(ox, oy, w, h), AntialiasMode.Aliased);
            _ctx.Transform = Matrix3x2.CreateTranslation((float)ox, (float)oy);
            foreach (var g in f.Groups) DrawGroup(g, f, c, edit);
            if (c.Reordering is { } ro && f.Rows.FirstOrDefault(r => r.Row.Id == ro.Id) is { } dragged)
            {   // la fila que se arrastra se resalta y una línea marca dónde caerá
                RR(dragged.X + 2, dragged.Y + 2, dragged.W - 4, dragged.H - 4, 8, new Color4(Tint.R, Tint.G, Tint.B, 0.16f));
                var grips = f.Rows.Where(r => SettingsLayout.HasGrip(r.Row)).ToList(); var tgt = grips[Math.Clamp(ro.To, 0, grips.Count - 1)];
                double ly = ro.To > ro.From ? tgt.Y + tgt.H : tgt.Y; Ln(tgt.X + 12, ly, tgt.X + tgt.W - 12, ly, Tint, 3f);
            }
            if (f.MaxScroll > 0.5) { double th = Math.Max(30, SettingsLayout.ViewportH * SettingsLayout.ViewportH / f.ContentHeight), ty = 6 + (SettingsLayout.ViewportH - th - 12) * f.Scroll / f.MaxScroll; RR(cw - 7, ty, 3.5, th, 1.75, Label(0.28f)); }
            DrawSuggestions(c, f);
            _ctx.Transform = Matrix3x2.Identity; _ctx.PopAxisAlignedClip();
            DrawHeader(page, ox, cw, f.Scroll > 1);
        }

        _ctx.EndDraw();
    }

    private void DrawHeader(SettingsPage page, double ox, double cw, bool scrolled)
    {
        double x = ox + SettingsLayout.ColX, tw = SettingsLayout.ColW;
        if (scrolled) { Gradient(ox, 0, cw, SettingsLayout.TitleH, K(_p.BackdropTop, 0.96f), K(_p.BackdropTop, 0.0f)); }
        Tile(x, 22, 52, ColorOf(page.Id), page.Glyph);
        Text(page.Title, _title, x + 68, 16, tw - 68, 40, Label());
        if (!string.IsNullOrEmpty(page.Subtitle)) Text(page.Subtitle, _small, x + 68, 56, tw - 68, 36, Secondary, wrap: true, top: true);
    }

    private EditView? _searchEdit;
    private void DrawSidebar(SettingsController c, double h)
    {
        double sw = SettingsLayout.SidebarW; bool compact = SettingsLayout.CompactSidebar;
        Brush(Side); _ctx.FillRectangle(R(0, 0, sw, h), _b); Ln(sw - 0.5, 0, sw - 0.5, h, Sep, 1f);
        if (!compact)
        {
            Text("Ajustes", _sideTitle, 20, 14, sw - 32, 40, Label());
            var sf = SettingsLayout.FieldRect(c.SearchFrame); DrawSearchBox(c.SearchFrame, _searchEdit, sf);
        }

        foreach (var e in SettingsLayout.SideEntries(c.Pages))
        {
            int i = e.PageIndex; var r = e.Rect; bool sel = i == c.PageIndex && c.SearchText.Length == 0, hov = i == c.HoverSide && !sel; var pg = c.Pages[i];
            if (e.Header is { } hd) Text(hd.ToUpperInvariant(), _head, r.X + 10, e.HeaderY, r.W - 12, SettingsLayout.SideHeaderH, Secondary);
            if (sel) { RR(r.X, r.Y, r.W, r.H, 12, TintSoft); RRStroke(r.X, r.Y, r.W, r.H, 12, new Color4(Tint.R, Tint.G, Tint.B, 0.35f), 1f); } else if (hov) RR(r.X, r.Y, r.W, r.H, 12, Label(0.06f));
            double ts = Math.Min(26, r.H - 6); Tile(compact ? r.X + (r.W - ts) / 2 : r.X + 8, r.Y + (r.H - ts) / 2, ts, ColorOf(pg.Id), pg.Glyph);
            if (!compact) Text(pg.Title, sel ? _sideSel : _side, r.X + 44, r.Y, r.W - 50, r.H, sel ? Label() : Label(0.92f));
        }
    }

    /// <summary>La caja de búsqueda de la barra lateral: píldora con lupa, con el estado de edición de siempre.</summary>
    private void DrawSearchBox(RowFrame frame, EditView? edit, (double X, double Y, double W, double H) f)
    {
        DrawField(frame, edit, 1f, radius: f.H / 2);
        Glyph("search", f.X + f.W - 20, f.Y + f.H / 2, 6.5, Secondary);
    }

    private void DrawGroup(GroupFrame g, PageFrame f, SettingsController c, EditView? edit)
    {
        var rowsOf = f.Rows.Where(r => g.Group.Rows.Contains(r.Row)).ToList();
        if (g.Group.Rows.Count == 1 && g.Group.Rows[0].Kind == RowKind.Hero) { DrawHero(rowsOf[0]); return; }
        if (!string.IsNullOrEmpty(g.Group.Header)) Text(g.Group.Header, _section, g.X + 4, g.HeaderY + 2, g.W - 8, 28, Label());
        if (g.Group.Cards)
        {
            foreach (var r in rowsOf) DrawCard(r, c);
            if (g.FooterH > 0) Text(g.Group.Footer, _sub, g.X + 4, g.FooterY, g.W - 8, g.FooterH, Secondary, wrap: true, top: true);
            return;
        }

        Shadow(g.X, g.Y, g.W, g.H, SettingsLayout.Radius); RR(g.X, g.Y, g.W, g.H, SettingsLayout.Radius, Card); RRStroke(g.X, g.Y, g.W, g.H, SettingsLayout.Radius, Sep, 0.9f);
        _ctx.PushAxisAlignedClip(R(g.X, g.Y, g.X + g.W, g.Y + g.H), AntialiasMode.Aliased);   // el resalte de una fila no se sale de las esquinas redondeadas
        foreach (var r in rowsOf)
        {
            bool hover = c.Hover is { } hv && hv.Id == r.Row.Id && r.Row.Kind is RowKind.Toggle or RowKind.Button;
            if (hover) { Brush(Label(0.05f)); _ctx.FillRectangle(R(r.X, r.Y, r.X + r.W, r.Y + r.H), _b); }
            DrawRow(r, c, edit);
            if (!r.Last) Ln(r.X + 16, r.Y + r.H - 0.5, r.X + r.W - 16, r.Y + r.H - 0.5, Sep, 0.8f);
        }

        _ctx.PopAxisAlignedClip();
        if (g.FooterH > 0) Text(g.Group.Footer, _sub, g.X + 16, g.FooterY, g.W - 32, g.FooterH, Secondary, wrap: true, top: true);
    }

    /// <summary>Tarjeta principal de General: identidad de la app y su estado real (sin botones que no existen).</summary>
    private void DrawHero(RowFrame r)
    {
        double x = r.X, y = r.Y + 4, w = r.W, h = r.H - 8;
        Shadow(x, y, w, h, 22); Gradient(x, y, w, h, K(_p.AccentSoft, _p.Dark ? 1f : 0.95f), K(_p.Surface), 22, vertical: false); RRStroke(x, y, w, h, 22, Sep, 0.9f);
        double ts = Math.Min(92, h - 28), tx = x + 24, ty = y + (h - ts) / 2;
        Gradient(tx, ty, ts, ts, C(0x8B5CF6), C(0x5B34C7), ts * 0.26); Text("H", _title, tx, ty, ts, ts, new Color4(1, 1, 1, 1), center: true);
        double bx = tx + ts + 22, bw = Math.Max(60, w - (bx - x) - 24);
        Text(r.Row.Title, _title, bx, ty + 2, bw, 38, Label());
        Text(r.Row.Subtitle, _small, bx, ty + 42, bw, 22, Secondary);
        double cx = bx, cy = ty + ts - 28;
        foreach (var (chip, i) in (r.Row.Options ?? []).Select((t, i) => (t, i)))
        {
            double cwid = Measure(chip, _small) + (i == 0 ? 40 : 26);
            RR(cx, cy, cwid, 26, 13, i == 0 ? new Color4(Green.R, Green.G, Green.B, 0.16f) : Label(0.07f));
            if (i == 0) { Dot(cx + 14, cy + 13, 4, Green); Text(chip, _small, cx + 24, cy, cwid - 24, 26, Label()); } else Text(chip, _small, cx, cy, cwid, 26, Label(0.85f), center: true);
            cx += cwid + 10;
        }
    }

    /// <summary>Tarjeta de un componente (la misma fila Toggle, con el mismo id): icono, nombre, descripción medida y su interruptor.</summary>
    private void DrawCard(RowFrame r, SettingsController c)
    {
        var row = r.Row; float dim = row.Enabled ? 1f : 0.5f; bool hover = c.Hover is { } hv && hv.Id == row.Id;
        Shadow(r.X, r.Y, r.W, r.H, 16); RR(r.X, r.Y, r.W, r.H, 16, hover ? Raised : Card); RRStroke(r.X, r.Y, r.W, r.H, 16, hover ? new Color4(Tint.R, Tint.G, Tint.B, 0.5f) : Sep, hover ? 1.3f : 0.9f);
        Tile(r.X + 16, r.Y + 18, 48, ColorOfGlyph(row.Icon), row.Icon ?? "gear");
        double tx = r.X + 16 + 48 + 14, sw = SettingsLayout.SwitchW, tw = r.W - (tx - r.X) - sw - 24;
        Text(row.Title, _cardTitle, tx, r.Y + 18, tw, 22, Label(dim));
        var sub = SettingsLayout.SubText(row); Text(sub, _sub, tx, r.Y + 44, tw, SettingsLayout.SubLineH * SettingsLayout.Lines(sub, tw) + 3, Secondary, wrap: true, top: true);
        DrawSwitch(SettingsLayout.SwitchRect(r), row.On, dim);
        if (row.Id.StartsWith("card:", StringComparison.Ordinal))
        {   // estado real del módulo (el mismo interruptor): «Activo» o «Desactivado»
            string st = !row.Enabled ? "No disponible" : row.On ? "Activo" : "Desactivado"; double cw = Measure(st, _head) + 30, cy = r.Y + r.H - 32;
            RR(tx, cy, cw, 22, 11, row.On && row.Enabled ? new Color4(Green.R, Green.G, Green.B, 0.16f) : Label(0.07f)); Dot(tx + 12, cy + 11, 3.5, row.On && row.Enabled ? Green : Label(0.4f)); Text(st, _head, tx + 22, cy, cw - 22, 22, Label(0.9f));
        }
    }

    private void DrawRow(RowFrame r, SettingsController c, EditView? edit)
    {
        var row = r.Row; float dim = row.Enabled ? 1f : 0.42f; double x = r.X + 16, midY = r.Y + r.H / 2;
        bool hasSub = !string.IsNullOrEmpty(row.Subtitle) || (!row.Enabled && !string.IsNullOrEmpty(row.Reason));
        if (row.Kind == RowKind.Preview && row.Id == "dock.preview") { DrawDockPreview(r); return; }
        if (row.Kind == RowKind.Preview && row.Id == "notch.preview") { DrawNotchPreview(r); return; }
        if (row.Kind == RowKind.Preview && row.Id == "gest.preview") { DrawGestPreview(r); return; }
        if (row.Kind == RowKind.Preview)
        {   // tira con la barra de arriba tal como queda: el nombre, los menús, los iconos en su orden y la hora
            double bx = r.X + 16, by = r.Y + 8, bw = r.W - 32, bh = r.H - 16; var white = new Color4(1, 1, 1, 0.9f);
            RR(bx, by, bw, bh, 10, new Color4(0.06f, 0.04f, 0.12f, 1));
            Text("Hatsu    Archivo", _sub, bx + 14, by, 130, bh, white);
            var pill = SettingsLayout.PreviewPill(r); Pill(pill.X, pill.Y, pill.W, pill.H, SettingsLayout.CustomizeLabel);
            double ix = pill.X - 10 - 40; Text("16:08", _sub, ix, by, 40, bh, white);
            foreach (var name in (row.Options ?? []).Reverse()) { ix -= 20; if (ix < bx + 120) break; RR(ix, by + bh / 2 - 7, 14, 14, 4, new Color4(1, 1, 1, 0.28f)); }
            return;
        }

        if (row.Kind == RowKind.Info) { Glyph("info", x + 10, midY, 9, Secondary); Text(row.Title, _sub, x + 30, r.Y + 4, r.W - 62, r.H - 8, Secondary, wrap: true); return; }
        bool grip = SettingsLayout.HasGrip(row); if (grip) x += SettingsLayout.GripW - 10;
        double tw = r.W - 32 - SettingsLayout.ControlSpace(row, r.W);
        if (row.Kind == RowKind.Button && row.Options is { Count: > 0 } pills)
        {   // fila de acción con píldoras azules (Agregar, Cancelar / Guardar)
            double ptw = r.W - 32 - SettingsLayout.ControlSpace(row, r.W);
            if (!string.IsNullOrEmpty(row.Subtitle)) { Text(row.Title, _body, x, r.Y + 8, ptw, 22, Label()); Text(row.Subtitle, _sub, x, r.Y + 30, Math.Max(40, ptw), SettingsLayout.SubLineH * SettingsLayout.Lines(row.Subtitle, ptw) + 3, Secondary, wrap: true, top: true); }
            else Text(row.Title, _body, x, r.Y, ptw, r.H - (SettingsLayout.IsStacked(row, r.W) ? SettingsLayout.ControlH + 14 : 0), Label());
            for (int i = 0; i < pills.Count; i++) { var pr = SettingsLayout.PillRect(r, i); Pill(pr.X, pr.Y, pr.W, pr.H, pills[i], primary: i == pills.Count - 1); }
            return;
        }

        if (row.Kind == RowKind.Button) { Text(row.Title, _body, x, r.Y, r.W - 60, r.H, row.Enabled ? Tint : Label(0.42f)); if (row.Enabled) Glyph("chevron", r.X + r.W - 20, midY, 6, Secondary); return; }
        if (hasSub) { var sub = SettingsLayout.SubText(row); Text(row.Title, _body, x, r.Y + 8, tw, 22, Label(dim)); Text(sub, _sub, x, r.Y + 30, Math.Max(40, tw), SettingsLayout.SubLineH * SettingsLayout.Lines(sub, tw) + 3, Secondary, wrap: true, top: true); }
        else Text(row.Title, _body, x, r.Y, tw, r.H - (SettingsLayout.IsStacked(row, r.W) ? SettingsLayout.ControlH + 14 : 0), Label(dim));
        switch (row.Kind)
        {
            case RowKind.Toggle:
            {
                DrawSwitch(SettingsLayout.SwitchRect(r), row.On, dim);
                if (grip) Grip(r.X + 6 + SettingsLayout.GripW / 2, midY, Label(0.45f));
                else if (row.CanUp || row.CanDown) { var u = SettingsLayout.UpRect(r); var d = SettingsLayout.DownRect(r); Glyph("up", u.X + u.W / 2, u.Y + u.H / 2, 7, Label(row.CanUp ? 0.7f : 0.2f)); Glyph("down", d.X + d.W / 2, d.Y + d.H / 2, 7, Label(row.CanDown ? 0.7f : 0.2f)); }
                break;
            }
            case RowKind.Segmented: DrawSegmented(r, dim); break;
            case RowKind.Field: DrawField(r, edit is not null && edit.Id == row.Id ? edit : null, dim); break;
            case RowKind.Slider:
            {
                var (x0, x1, cy) = SettingsLayout.SliderTrack(r); double v = c.Dragging is { } dg && dg.Id == row.Id ? dg.Value : row.Value, px = x0 + (x1 - x0) * v / 100;
                RR(x0, cy - 2, x1 - x0, 4, 2, Off); RR(x0, cy - 2, px - x0, 4, 2, new Color4(Tint.R, Tint.G, Tint.B, dim));
                Dot(px, cy + 1, 13, new Color4(0, 0, 0, 0.18f)); Dot(px, cy, 12, new Color4(1, 1, 1, dim)); RRStroke(px - 12, cy - 12, 24, 24, 12, new Color4(0, 0, 0, _dark ? 0.35f : 0.12f), 0.8f);
                Text(row.SliderLabel(v), _small, x1 + 2, cy - 14, 44, 28, Secondary, right: true); break;
            }
        }
    }

    private static double ControlSpace(SettingsRow r) => SettingsLayout.ControlSpace(r);

    private static readonly int[] AppColors = [0x3B82F6, 0xF5B83D, 0x2D9CDB, 0x7C5CFF, 0x22C55E, 0x1D9BF0, 0xE8E4F2];

    /// <summary>Fondo de escritorio de muestra para las vistas previas (violeta, como los mockups).</summary>
    private void Wallpaper(double x, double y, double w, double h, double radius)
    {
        Gradient(x, y, w, h, C(0x4B2FA6), C(0x9A74F0), radius); Gradient(x, y, w, h, new Color4(1, 1, 1, 0.0f), new Color4(1, 1, 1, 0.14f), radius, vertical: false);
    }

    /// <summary>Vista previa del Dock: representativa (colores de muestra, no tus apps); el tamaño y la ampliación reflejan los ajustes reales.</summary>
    private void DrawDockPreview(RowFrame r)
    {
        double px = r.X + 14, py = r.Y + 14, pw = r.W - 28, ph = r.H - 28; Wallpaper(px, py, pw, ph, 14);
        double size = Math.Round(r.Row.SliderMin + r.Row.Value / 100 * (r.Row.SliderMax - r.Row.SliderMin)), icon = Math.Clamp(size * 0.62, 20, 54), gap = icon * 0.28, n = 7;
        double dw = n * icon + (n + 1) * gap, dh = icon + gap * 1.6, dx = px + (pw - dw) / 2, dy = py + ph - dh - 14;
        RR(dx, dy, dw, dh, dh * 0.34, new Color4(1, 1, 1, 0.20f)); RRStroke(dx, dy, dw, dh, dh * 0.34, new Color4(1, 1, 1, 0.38f), 1f);
        for (int i = 0; i < n; i++)
        {
            double ix = dx + gap + i * (icon + gap), iy = dy + gap * 0.8 - (r.Row.On && i == 3 ? icon * 0.18 : 0), sz = r.Row.On && i == 3 ? icon * 1.18 : icon;
            Gradient(ix - (sz - icon) / 2, iy - (sz - icon) / 2, sz, sz, C(AppColors[i % AppColors.Length]), C(AppColors[i % AppColors.Length] & 0x7F7F7F | 0x202020), sz * 0.24);
            if (i is 0 or 2 or 3 or 5) Dot(ix + icon / 2, dy + dh - gap * 0.45, 2, new Color4(1, 1, 1, 0.85f));
        }

        Text(r.Row.SliderLabel(r.Row.Value), _head, px + 14, py + 8, 80, 18, new Color4(1, 1, 1, 0.85f));
    }

    /// <summary>Vista previa de la Isla: reposo con un estado de ejemplo y sus cuatro estados (música, descarga, temporizador, notificación).</summary>
    private void DrawNotchPreview(RowFrame r)
    {
        double px = r.X + 14, py = r.Y + 14, pw = r.W - 28, ph = r.H - 28; Wallpaper(px, py, pw, ph, 14);
        double nw = Math.Min(380, pw * 0.62), nh = 52, nx = px + (pw - nw) / 2, ny = py - 1;
        RR(nx, ny, nw, nh + 14, 24, new Color4(0.05f, 0.04f, 0.08f, 1)); RR(nx - 1, ny, nw + 2, 14, 2, new Color4(0.05f, 0.04f, 0.08f, 1));
        Gradient(nx + 16, ny + 18, 30, 30, C(0xB78CFF), C(0x6B3FD1), 8);
        RR(nx + 56, ny + 21, 92, 7, 3.5, new Color4(1, 1, 1, 0.8f)); RR(nx + 56, ny + 34, 60, 6, 3, new Color4(1, 1, 1, 0.38f));
        Text("◀   ❚❚   ▶", _small, nx + nw - 150, ny + 14, 120, 36, new Color4(1, 1, 1, 0.9f), center: true);
        string[] marks = ["♪", "↓", "00:00", "•"]; double tw = Math.Min(150, (pw - 24 - 3 * 12) / 4), tx0 = px + (pw - (4 * tw + 3 * 12)) / 2, ty = py + ph - 64;
        for (int i = 0; i < 4 && i < (r.Row.Options?.Count ?? 0); i++)
        {
            double tx = tx0 + i * (tw + 12); RR(tx, ty, tw, 52, 14, new Color4(1, 1, 1, i == 0 ? 0.30f : 0.16f)); if (i == 0) RRStroke(tx, ty, tw, 52, 14, new Color4(1, 1, 1, 0.7f), 1.4f);
            RR(tx + tw / 2 - 34, ty + 8, 68, 20, 10, new Color4(0.05f, 0.04f, 0.08f, 1)); Text(marks[i], _head, tx + tw / 2 - 34, ty + 8, 68, 20, new Color4(1, 1, 1, 0.92f), center: true);
            Text(r.Row.Options![i], _head, tx, ty + 30, tw, 18, new Color4(1, 1, 1, 0.9f), center: true);
        }
    }

    /// <summary>Vista previa de las esquinas activas: un monitor con las cuatro esquinas y la acción asignada a cada una (lo real, de los ajustes).</summary>
    private void DrawGestPreview(RowFrame r)
    {
        var o = r.Row.Options ?? []; double mw = Math.Min(360, r.W - 60), mh = r.H - 64, mx = r.X + (r.W - mw) / 2, my = r.Y + 18;
        RR(mx - 6, my - 6, mw + 12, mh + 12, 16, K(_p.SurfaceRaised)); RRStroke(mx - 6, my - 6, mw + 12, mh + 12, 16, Sep, 1f); Wallpaper(mx, my, mw, mh, 10);
        RR(mx + mw / 2 - 40, my + mh + 8, 80, 8, 4, Label(0.25f)); RR(mx + mw / 2 - 18, my + mh + 6, 36, 4, 2, Label(0.25f));
        double cwid = Math.Min(130, mw / 2 - 14), ch = 24; (double X, double Y)[] pos = [(mx + 8, my + 8), (mx + mw - cwid - 8, my + 8), (mx + 8, my + mh - ch - 8), (mx + mw - cwid - 8, my + mh - ch - 8)];
        for (int i = 0; i < 4; i++)
        {
            bool set = i < o.Count && o[i] != "Nada"; RR(pos[i].X, pos[i].Y, cwid, ch, 8, set && r.Row.On ? new Color4(1, 1, 1, 0.85f) : new Color4(1, 1, 1, 0.22f));
            Text(i < o.Count ? o[i] : "Nada", _head, pos[i].X + 6, pos[i].Y, cwid - 12, ch, set && r.Row.On ? C(0x2B1B63) : new Color4(1, 1, 1, 0.75f), center: true);
        }

        Text(r.Row.On ? "Esquinas activas" : "Activa Hatsu Gestures para usar las esquinas", _small, r.X + 16, r.Y + r.H - 28, r.W - 32, 22, Secondary, center: true);
    }

    private void DrawSwitch((double X, double Y, double W, double H) s, bool on, float dim)
    {
        RR(s.X, s.Y, s.W, s.H, s.H / 2, on ? new Color4(Tint.R, Tint.G, Tint.B, dim) : new Color4(Off.R, Off.G, Off.B, dim));
        double r = s.H / 2 - 3, kx = on ? s.X + s.W - s.H / 2 : s.X + s.H / 2, cy = s.Y + s.H / 2;
        Dot(kx, cy + 1, r + 0.5, new Color4(0, 0, 0, 0.18f * dim)); Dot(kx, cy, r, on ? new Color4(OnTint.R, OnTint.G, OnTint.B, dim) : new Color4(1, 1, 1, dim));
    }

    private void DrawSegmented(RowFrame r, float dim)
    {
        var s = SettingsLayout.SegmentRect(r); var o = r.Row.Options ?? []; if (o.Count == 0) return; double sw = s.W / o.Count;
        RR(s.X, s.Y, s.W, s.H, 12, new Color4(Field.R, Field.G, Field.B, dim)); RRStroke(s.X, s.Y, s.W, s.H, 12, Sep, 0.8f);
        if (r.Row.Selected >= 0 && r.Row.Selected < o.Count) { RR(s.X + sw * r.Row.Selected + 3, s.Y + 3, sw - 6, s.H - 6, 9, new Color4(Tint.R, Tint.G, Tint.B, dim)); }
        for (int i = 0; i < o.Count; i++) Text(o[i], i == r.Row.Selected ? _bodyBold : _small, s.X + sw * i, s.Y, sw, s.H, i == r.Row.Selected ? new Color4(OnTint.R, OnTint.G, OnTint.B, dim) : Label(dim * 0.85f), center: true);
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

    private void DrawField(RowFrame r, EditView? e, float dim, double radius = 12)
    {
        var f = SettingsLayout.FieldRect(r); RR(f.X, f.Y, f.W, f.H, radius, new Color4(Field.R, Field.G, Field.B, dim)); if (e is null) RRStroke(f.X, f.Y, f.W, f.H, radius, Sep, 0.8f);
        if (e is null)
        {
            bool empty = string.IsNullOrEmpty(r.Row.Text);
            Text(empty ? r.Row.Placeholder : r.Row.Text, _small, f.X + FieldPadX, f.Y, f.W - 2 * FieldPadX, f.H, empty ? Secondary : Label(dim)); return;
        }

        RRStroke(f.X, f.Y, f.W, f.H, radius, Tint, 1.6f);
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
        RR(s.X + 1, s.Y + 3, s.W, s.H, 12, new Color4(0, 0, 0, _dark ? 0.45f : 0.16f)); RR(s.X, s.Y, s.W, s.H, 12, new Color4(Raised.R, Raised.G, Raised.B, 1f)); RRStroke(s.X, s.Y, s.W, s.H, 12, Sep, 0.8f);
        for (int i = 0; i < c.Suggestions.Count; i++)
        {
            double y = s.Y + 4 + i * SettingsLayout.SuggestRowH; if (i == c.SuggestIndex) RR(s.X + 4, y, s.W - 8, SettingsLayout.SuggestRowH, 8, new Color4(Tint.R, Tint.G, Tint.B, _dark ? 0.35f : 0.16f));
            double off = c.SuggestHasChecks ? 24 : 0;
            if (c.SuggestHasChecks && i == c.SuggestChecked) Text("✓", _small, s.X + 14, y, 18, SettingsLayout.SuggestRowH, new Color4(Tint.R, Tint.G, Tint.B, 1f));
            bool apps = c.SuggestFor == "gest.find"; double tagW = apps ? 82 : 0;
            Text(c.Suggestions[i], _small, s.X + 16 + off, y, s.W - 32 - off - tagW, SettingsLayout.SuggestRowH, Label());
            if (apps) { RRStroke(s.X + s.W - 16 - 70, y + 9, 70, 20, 10, Sep, 0.9f); Text("Instalada", _head, s.X + s.W - 16 - 70, y + 9, 70, 20, Secondary, center: true); }
        }
    }

    public void Dispose() { _b.Dispose(); _round.Dispose(); _title.Dispose(); _side.Dispose(); _sideSel.Dispose(); _body.Dispose(); _bodyBold.Dispose(); _sub.Dispose(); _head.Dispose(); _small.Dispose(); _section.Dispose(); _sideTitle.Dispose(); _cardTitle.Dispose(); SettingsLayout.Measurer = null; _dw.Dispose(); }
}

/// <summary>Medidor de texto con DirectWrite para el layout de Ajustes V2: la altura de cada fila sale del mismo motor que dibuja, no de una estimación.</summary>
internal sealed class DirectWriteMeasurer(IDWriteFactory dw) : ITextMeasurer
{
    private readonly Dictionary<string, IDWriteTextFormat> _formats = [];
    private IDWriteTextFormat Format(TextStyle s)
    {
        string key = s.Key + s.Size + s.Weight;
        if (!_formats.TryGetValue(key, out var f)) { f = dw.CreateTextFormat(s.Weight == TextWeight.Semibold ? "Segoe UI Semibold" : "Segoe UI", (float)s.Size); _formats[key] = f; }
        return f;
    }

    public WinMac.Host.SettingsV2.Size Measure(string text, TextStyle style, double maxWidth)
    {
        using var l = dw.CreateTextLayout(string.IsNullOrEmpty(text) ? " " : text, Format(style), (float)Math.Max(1, maxWidth), 10000f); l.WordWrapping = WordWrapping.Wrap;
        var m = l.Metrics; return new WinMac.Host.SettingsV2.Size(m.Width, m.Height);
    }

    public double LineHeight(TextStyle style) => Measure("Ag", style, 4000).H;
}
