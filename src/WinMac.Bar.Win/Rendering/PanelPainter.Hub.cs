using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Dibujo del panel multifunción y del Tiempo (pestañas, gráficas, medidores, estadísticas, selectores, aplicaciones con su icono real, pronóstico por horas y los iconos vectoriales).
/// Todo vectorial y propio (sin fuentes ni imágenes de terceros), a DIP, con las proporciones de las capturas de referencia.
/// </summary>
public sealed partial class PanelPainter
{
    private IDWriteTextFormat _big = null!, _huge = null!, _cap = null!, _boldSmall = null!, _boldTiny = null!, _stat = null!; private bool _fontsReady; private ID2D1StrokeStyle? _dash;
    private readonly Dictionary<string, ID2D1Bitmap?> _icons = [];

    /// <summary>Entrega los píxeles BGRA premultiplicados 32×32 del icono de un ejecutable (o null). Se consulta una vez por ejecutable y se guarda como bitmap.</summary>
    public Func<string, byte[]?>? IconProvider { get; set; }

    private void EnsureFonts()
    {
        if (_fontsReady) return; _fontsReady = true;
        _big = _dw.CreateTextFormat("Segoe UI Semibold", 20); _huge = _dw.CreateTextFormat("Segoe UI Light", 44); _cap = _dw.CreateTextFormat("Segoe UI Semibold", 11.5f);
        _boldSmall = _dw.CreateTextFormat("Segoe UI Semibold", 12); _boldTiny = _dw.CreateTextFormat("Segoe UI Semibold", 10.5f); _stat = _dw.CreateTextFormat("Segoe UI Semibold", 17);
    }

    private void DisposeHub()
    {
        if (_fontsReady) { _big.Dispose(); _huge.Dispose(); _cap.Dispose(); _boldSmall.Dispose(); _boldTiny.Dispose(); _stat.Dispose(); } _dash?.Dispose();
        foreach (var b in _icons.Values) b?.Dispose(); _icons.Clear();
    }

    private static Color4 Named(string? name) => name switch
    {
        "green" => new Color4(0.19f, 0.82f, 0.35f, 1), "red" => new Color4(1f, 0.27f, 0.23f, 1), "yellow" => new Color4(1f, 0.80f, 0.04f, 1),
        "teal" => new Color4(0.25f, 0.80f, 0.70f, 1), "lightblue" => new Color4(0.35f, 0.78f, 0.98f, 1), _ => Accent,
    };

    private void TextAlign(string text, IDWriteTextFormat fmt, double x, double y, double w, double h, Color4 c, TextAlignment align = TextAlignment.Leading, bool vcenter = true, bool wrap = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var l = _dw.CreateTextLayout(text, fmt, (float)Math.Max(1, w), (float)Math.Max(1, h));
        l.WordWrapping = wrap ? WordWrapping.Wrap : WordWrapping.NoWrap; l.ParagraphAlignment = vcenter ? ParagraphAlignment.Center : ParagraphAlignment.Near; l.TextAlignment = align;
        if (!wrap) { using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell); }
        Brush(c); _ctx.DrawTextLayout(new Vector2((float)x, (float)y), l, _solid);
    }

    private double MeasureText(string text, IDWriteTextFormat fmt)
    {
        if (string.IsNullOrEmpty(text)) return 0; using var l = _dw.CreateTextLayout(text, fmt, 4000, 40); return l.Metrics.WidthIncludingTrailingWhitespace;
    }

    private ID2D1Bitmap? IconFor(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || IconProvider is null) return null;
        if (_icons.TryGetValue(exePath, out var hit)) return hit;
        ID2D1Bitmap? bmp = null;
        try
        {
            var px = IconProvider(exePath);
            if (px is { Length: 32 * 32 * 4 })
                unsafe { fixed (byte* p = px) bmp = _ctx.CreateBitmap(new SizeI(32, 32), (IntPtr)p, 32 * 4, new BitmapProperties1(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96)); }
        }
        catch (Exception) { bmp = null; }
        if (_icons.Count > 64) { foreach (var b in _icons.Values) b?.Dispose(); _icons.Clear(); }
        _icons[exePath] = bmp; return bmp;
    }

    private void DrawHubWidget(WidgetFrame f, bool hover)
    {
        EnsureFonts(); var w = f.W; double x = f.X, y = f.Y, wd = f.Width, h = f.Height; float dim = w.Enabled ? 1f : 0.5f;
        switch (w.Kind)
        {
            case WidgetKind.Tabs: DrawTabs(f); break;
            case WidgetKind.TabHeader:
                TextAlign(w.Text, _cap, x + 4, y, wd - 30, h, Fg(0.62f)); Glyph("sliders", x + wd - 14, y + h / 2, 7.5f, Fg(hover ? 0.95f : 0.6f)); break;
            case WidgetKind.Caption: TextAlign(w.Text, _boldSmall, x + 2, y, wd - 4, h, Fg(0.6f)); break;
            case WidgetKind.Separator: if (_kind is PanelKind.Hub or PanelKind.Weather) Line(x + 2, y + h / 2, x + wd - 2, y + h / 2, Fill(0.13f), 1f); break;   // línea fina entre secciones (solo en estos paneles)
            case WidgetKind.Chart: DrawChart(f); break;
            case WidgetKind.MeterRow: DrawMeterRow(f); break;
            case WidgetKind.KeyValue:
            {
                double tx = x + 2;
                if (w.Glyph is { } g && g.StartsWith("dot-")) { FillCircle(x + 7, y + h / 2, 4.5f, Named(g[4..])); tx = x + 20; }
                else if (w.Glyph is { } g2) { Glyph(g2, x + 9, y + h / 2, 6.5f, Fg(0.7f * dim)); tx = x + 24; }
                double rw = Math.Min(wd * 0.62, MeasureText(w.Sub ?? "", _bodyBold) + 6);
                TextAlign(w.Text, _body, tx, y, wd - (tx - x) - rw - 6, h, Fg((w.Enabled ? 0.82f : 0.5f)));
                TextAlign(w.Sub ?? "", _bodyBold, x + wd - rw - 2, y, rw, h, w.Enabled ? Fg() : Fg(0.45f), TextAlignment.Trailing);
                break;
            }
            case WidgetKind.Stats: DrawStats(f); break;
            case WidgetKind.Select: DrawSelect(f, hover); break;
            case WidgetKind.AppRow: DrawAppRow(f); break;
            case WidgetKind.WeatherHead: DrawWeatherHead(f); break;
            case WidgetKind.Strip: DrawStrip(f); break;
            case WidgetKind.CityRow:
                if (hover) FillRR(x, y, x + wd, y + h, 8, Fill(0.08f));
                TextAlign(w.Text, _body, x + 4, y, wd - 90, h, Fg()); WeatherIcon(w.Glyph ?? "cloudy", x + wd - 62, y + h / 2, 8.5f); TextAlign(w.Sub ?? "", _bodyBold, x + wd - 50, y, 46, h, Fg(), TextAlignment.Trailing); break;
        }
    }

    // ───────────────────────────── pestañas
    private void DrawTabs(WidgetFrame f)
    {
        if (f.W.Data is not TabsData td) return; double x = f.X, y = f.Y + 4, wd = f.Width, h = f.Height - 8, cw = (wd - 12) / Math.Max(1, td.Tabs.Count);
        FillRR(x, y, x + wd, y + h, 16, Fill(0.09f)); Brush(Fill(0.13f)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(x + 0.5, y + 0.5, x + wd - 0.5, y + h - 0.5), RadiusX = 16, RadiusY = 16 }, _solid, 1f);
        for (int i = 0; i < td.Tabs.Count; i++)
        {
            double cx = x + 6 + cw * i, mid = cx + cw / 2; bool sel = i == td.Selected;
            if (sel) { double hw = Math.Min(cw - 6, 54) / 2; FillRR(mid - hw, y + 4, mid + hw, y + h - 4, 11, new Color4(Accent.R, Accent.G, Accent.B, 0.30f)); }
            Glyph(td.Tabs[i].Glyph, mid, y + h / 2, 9.5f, sel ? Accent : Fg(0.68f));
        }
    }

    // ───────────────────────────── gráficas y medidores
    private void DrawChart(WidgetFrame f)
    {
        if (f.W.Data is not ChartData cd) return; double x = f.X, wd = f.Width, top = f.Y + 12, bottom = f.Y + f.Height - 4, max = cd.Max <= 0 ? 1 : cd.Max;
        _dash ??= _dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { DashStyle = DashStyle.Dash, StartCap = CapStyle.Flat, EndCap = CapStyle.Flat });
        Brush(Fill(0.22f)); _ctx.DrawLine(new Vector2((float)x, (float)top), new Vector2((float)(x + wd), (float)top), _solid, 1f, _dash);
        Line(x, bottom, x + wd, bottom, Fill(0.16f), 1f);
        for (int s = 0; s < cd.Series.Count; s++)
        {
            var v = cd.Series[s].Values; if (v.Count < 2) continue; var col = Named(cd.Series[s].Color);
            var pts = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++) pts[i] = new Vector2((float)(x + wd * i / (v.Count - 1)), (float)(bottom - Math.Clamp(v[i] / max, 0, 1) * (bottom - top)));
            if (s == 0)   // el área bajo la primera serie, como en las referencias
            {
                var area = new Vector2[pts.Length + 2]; Array.Copy(pts, area, pts.Length); area[^2] = new Vector2((float)(x + wd), (float)bottom); area[^1] = new Vector2((float)x, (float)bottom);
                using var ag = Poly(true, area); Brush(new Color4(col.R, col.G, col.B, 0.16f)); _ctx.FillGeometry(ag, _solid);
            }

            using var g = Poly(false, pts); Stroke(g, col, 1.7f);
        }

        if (!string.IsNullOrEmpty(cd.MaxLabel)) { double tw = MeasureText(cd.MaxLabel, _boldTiny) + 10; FillRR(x, f.Y + 3, x + tw, f.Y + 20, 5, Dark ? new Color4(0.25f, 0.25f, 0.28f, 0.95f) : new Color4(0.82f, 0.82f, 0.85f, 0.95f)); TextAlign(cd.MaxLabel, _boldTiny, x + 5, f.Y + 3, tw, 17, Fg(0.9f)); }
    }

    private void DrawMeterRow(WidgetFrame f)
    {
        var w = f.W; double x = f.X, y = f.Y, wd = f.Width, h = f.Height; var col = Named((w.Data as MeterData)?.Color); double frac = Math.Clamp(w.Value, 0, 1);
        if (string.IsNullOrEmpty(w.Text))   // sin etiqueta: barra a todo el ancho y el texto debajo (uso del disco)
        {
            FillRR(x + 2, y + 3, x + wd - 2, y + 10, 3.5f, Fill(0.16f)); if (frac > 0) FillRR(x + 2, y + 3, x + 2 + (wd - 4) * frac, y + 10, 3.5f, col);
            TextAlign(w.Sub ?? "", _small, x + 2, y + 10, wd, h - 10, Fg(0.78f)); return;
        }

        Glyph("chevron", x + 7, y + h / 2, 4.5f, Fg(0.55f)); TextAlign(w.Text, _body, x + 18, y, 84, h, Fg(0.85f));
        double bx = x + 96, bw = Math.Max(20, wd - 96 - 52); FillRR(bx, y + h / 2 - 3, bx + bw, y + h / 2 + 3, 3, Fill(0.16f)); if (frac > 0) FillRR(bx, y + h / 2 - 3, bx + Math.Max(8, bw * frac), y + h / 2 + 3, 3, col);
        TextAlign(w.Sub ?? "", _bodyBold, x + wd - 48, y, 46, h, Fg(), TextAlignment.Trailing);
    }

    private void DrawStats(WidgetFrame f)
    {
        if (f.W.Data is not StatsData sd) return; double x = f.X, y = f.Y, wd = f.Width, h = f.Height;
        double pillW = sd.Pill is null ? 0 : 42, split = x + wd * 0.46;   // la unidad (arriba a la derecha) NUNCA pisa la cifra de subida: la columna derecha termina donde empieza el botón
        void Col(StatItem it, double cx0, double cx1)
        {
            var col = Named(it.Color); Glyph(it.Arrow == "down" ? "arrow-down" : "arrow-up", cx0 + 8, y + 15, 6f, col);
            TextAlign(it.Value, _stat, cx0 + 22, y + 3, cx1 - cx0 - 22, 24, Fg()); TextAlign(it.Label, _body, cx0 + 22, y + 28, cx1 - cx0 - 22, 20, Fg(0.62f), vcenter: false);
        }

        Col(sd.Left, x + 2, split - 8); Line(split, y + 6, split, y + h - 6, Fill(0.18f), 1f); Col(sd.Right, split + 8, x + wd - (pillW > 0 ? pillW + 6 : 0));
        if (sd.Pill is not null) { FillRR(x + wd - pillW, y + 3, x + wd, y + 27, 8, Fill(0.13f)); TextAlign(sd.Pill, _boldSmall, x + wd - pillW, y + 3, pillW, 24, Fg(0.9f), TextAlignment.Center); }
    }

    private void DrawSelect(WidgetFrame f, bool hover)
    {
        if (f.W.Data is not SelectData sd) return; var w = f.W; double x = f.X, y = f.Y, wd = f.Width, h = f.Height; float dim = w.Enabled ? 1f : 0.5f;
        Glyph(sd.Glyph, x + 14, y + h / 2, 7.5f, Fg(0.7f * dim)); TextAlign(sd.Label, _body, x + 30, y, wd * 0.4 - 30, h, Fg(0.78f * dim));
        double px = x + wd * 0.40; FillRR(px, y + 3, x + wd, y + h - 3, 9, Fill(hover && w.Enabled ? 0.20f : 0.13f));
        TextAlign(w.Enabled ? sd.Current : (w.Reason is { Length: > 0 } ? "No disponible" : sd.Current), _body, px + 10, y + 3, wd - (px - x) - 34, h - 6, Fg(dim)); Glyph("updown", x + wd - 14, y + h / 2, 6f, Fg(0.65f * dim));
    }

    private void DrawAppRow(WidgetFrame f)
    {
        if (f.W.Data is not AppRowData ad) return; var w = f.W; double x = f.X, y = f.Y, wd = f.Width, h = f.Height; float dim = ad.Active ? 1f : 0.62f;
        var bmp = IconFor(ad.ExePath);
        if (bmp is not null) _ctx.DrawBitmap(bmp, (RawRectF?)R(x + 6, y + 4, x + 36, y + 34), dim, BitmapInterpolationMode.Linear, (RawRectF?)null);
        else { FillRR(x + 6, y + 4, x + 36, y + 34, 8, Fill(0.16f)); TextAlign(string.IsNullOrEmpty(ad.Name) ? "?" : ad.Name[..1].ToUpperInvariant(), _big, x + 6, y + 4, 30, 30, Fg(0.8f), TextAlignment.Center); }   // sin icono: una inicial, nunca un icono falso
        TextAlign(ad.Name, _bodyBold, x + 46, y + 2, wd - 46 - 34, 20, Fg(dim));
        var (l, r) = PanelLayout.AppSliderTrack(f); double cy = y + h - 14, pct = Math.Clamp(ad.Volume, 0, 100) / 100.0;
        FillRR(l, cy - 4, r, cy + 4, 4, Fill(0.18f * dim)); if (!ad.Muted) FillRR(l, cy - 4, l + (r - l) * pct, cy + 4, 4, Dark ? new Color4(0.55f, 0.65f, 1f, 0.9f * dim) : new Color4(0.25f, 0.4f, 0.9f, dim));
        double tx = l + (r - l) * pct; FillCircle(tx, cy, 8, new Color4(0.62f, 0.68f, 0.82f, 1f * dim)); Brush(new Color4(0, 0, 0, 0.22f)); _ctx.DrawEllipse(new Ellipse(new Vector2((float)tx, (float)cy), 8, 8), _solid, 0.8f);
        TextAlign(ad.Muted ? "—" : $"{ad.Volume}%", _body, r + 8, cy - 10, 36, 20, Fg(0.65f * dim), TextAlignment.Trailing);
        Glyph(ad.Muted ? "sound-muted" : "sound", x + wd - 17, cy, 6.5f, Fg(0.65f * dim));
    }

    // ───────────────────────────── Tiempo
    private void DrawWeatherHead(WidgetFrame f)
    {
        if (f.W.Data is not WeatherHeadData d) return; double x = f.X, y = f.Y, wd = f.Width;
        TextAlign(d.Place, _title, x + 4, y + 2, wd * 0.55, 24, Fg()); if (d.Located) Glyph("locate", x + 8 + Math.Min(MeasureText(d.Place, _title), wd * 0.5) + 8, y + 14, 5f, Fg(0.85f));
        TextAlign(d.Temp, _huge, x + 2, y + 22, wd * 0.5, 66, Fg());
        WeatherIcon(d.IconKey, x + wd - 14, y + 14, 11f, true); TextAlign(d.Description, _boldSmall, x + wd * 0.38, y + 30, wd * 0.62 - 2, 24, Fg(), TextAlignment.Trailing, wrap: false);
        if (d.HighLow is not null) TextAlign(d.HighLow, _boldSmall, x + wd * 0.38, y + 52, wd * 0.62 - 2, 24, Fg(0.8f), TextAlignment.Trailing);
    }

    private void DrawStrip(WidgetFrame f)
    {
        if (f.W.Data is not StripData sd || sd.Items.Count == 0) return; double x = f.X, y = f.Y;
        double[] wts = sd.Items.Select(i => i.IsSun ? 1.7 : 1.0).ToArray(); double tot = wts.Sum(), cur = x;   // la columna del amanecer/atardecer es más ancha: su texto cabe en una sola línea, como la referencia
        for (int i = 0; i < sd.Items.Count; i++)
        {
            var it = sd.Items[i]; double cw = f.Width * wts[i] / tot, cx = cur + cw / 2;
            TextAlign(it.Label, _boldSmall, cur, y + 1, cw, 18, Fg(), TextAlignment.Center);
            WeatherIcon(it.IconKey, cx, y + 36, 11f);
            if (it.PrecipProb is { } p) TextAlign($"{p}%", _tiny, cur, y + 50, cw, 14, new Color4(0.45f, 0.75f, 1f, 0.95f), TextAlignment.Center);
            TextAlign(it.Text, _boldSmall, cur, y + 66, cw, 20, Fg(), TextAlignment.Center);
            cur += cw;
        }
    }

    private void Cloud(double cx, double cy, double s, Color4 c)
    {
        FillCircle(cx - s * .38, cy + s * .12, (float)(s * .46), c); FillCircle(cx + s * .06, cy - s * .18, (float)(s * .58), c); FillCircle(cx + s * .52, cy + s * .14, (float)(s * .40), c);
        FillRR(cx - s * .84, cy + s * .12, cx + s * .90, cy + s * .56, (float)(s * .22), c);
    }

    private void SunDisc(double cx, double cy, double s, Color4 c, bool rays = true)
    {
        FillCircle(cx, cy, (float)(s * .52), c);
        if (rays) for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(cx + Math.Cos(a) * s * .78, cy + Math.Sin(a) * s * .78, cx + Math.Cos(a) * s * 1.02, cy + Math.Sin(a) * s * 1.02, c, 1.6f); }
    }

    /// <summary>Iconos meteorológicos vectoriales propios. <paramref name="s"/> es el «radio» del icono.</summary>
    private void WeatherIcon(string key, double cx, double cy, float s, bool large = false)
    {
        var white = new Color4(1, 1, 1, Dark ? 0.95f : 0.9f); var cloudC = Dark ? new Color4(0.93f, 0.94f, 0.97f, 1f) : new Color4(0.55f, 0.58f, 0.64f, 1f); var yellow = Named("yellow"); var blue = new Color4(0.35f, 0.70f, 1f, 1f);
        switch (key)
        {
            case "clear": SunDisc(cx, cy, s * .85, yellow); break;
            case "clear-night": { FillCircle(cx, cy, (float)(s * .78), white); FillCircle(cx + s * .38, cy - s * .30, (float)(s * .68), Dark ? new Color4(0.13f, 0.13f, 0.15f, 1f) : new Color4(0.96f, 0.96f, 0.97f, 1f)); break; }
            case "partly": SunDisc(cx - s * .42, cy - s * .38, s * .62, yellow); Cloud(cx + s * .12, cy + s * .22, s * .72, cloudC); break;
            case "partly-night": FillCircle(cx - s * .38, cy - s * .38, (float)(s * .46), white); Cloud(cx + s * .12, cy + s * .22, s * .72, cloudC); break;
            case "cloudy": Cloud(cx, cy, s * .86, cloudC); break;
            case "fog": Cloud(cx, cy - s * .22, s * .74, cloudC); for (int i = 0; i < 2; i++) Line(cx - s * .7, cy + s * (.56 + i * .34), cx + s * .7, cy + s * (.56 + i * .34), cloudC, 1.8f); break;
            case "drizzle": case "rain": { Cloud(cx, cy - s * .22, s * .74, cloudC); int n = key == "rain" ? 3 : 2; for (int i = 0; i < n; i++) { double dx = (i - (n - 1) / 2.0) * s * .5; Line(cx + dx + s * .1, cy + s * .55, cx + dx - s * .08, cy + s * .95, blue, 1.9f); } break; }
            case "snow": { Cloud(cx, cy - s * .22, s * .74, cloudC); for (int i = 0; i < 3; i++) FillCircle(cx + (i - 1) * s * .46, cy + s * (.62 + (i % 2) * .22), 1.9f, white); break; }
            case "thunder": { Cloud(cx, cy - s * .22, s * .74, cloudC); using var bolt = Poly(true, new Vector2((float)(cx + s * .12), (float)(cy + s * .30)), new Vector2((float)(cx - s * .22), (float)(cy + s * .72)), new Vector2((float)cx, (float)(cy + s * .72)), new Vector2((float)(cx - s * .1), (float)(cy + s * 1.1)), new Vector2((float)(cx + s * .3), (float)(cy + s * .55)), new Vector2((float)(cx + s * .08), (float)(cy + s * .55))); Brush(yellow); _ctx.FillGeometry(bolt, _solid); break; }
            case "sunrise": case "sunset":
            {
                Line(cx - s * .9, cy + s * .38, cx + s * .9, cy + s * .38, white, 1.6f); { using var a = Arc(cx, cy + s * .38, s * .5, Math.PI, 0); Stroke(a, yellow, 1.8f); }
                bool up = key == "sunrise"; double ay = up ? cy - s * .62 : cy - s * .12; Line(cx, cy - s * .35, cx, cy - s * .05 + (up ? 0 : 0), white, 1.6f);
                Line(cx - s * .28, ay + (up ? s * .3 : -s * .3), cx, ay, white, 1.6f); Line(cx + s * .28, ay + (up ? s * .3 : -s * .3), cx, ay, white, 1.6f); break;
            }
            default: Cloud(cx, cy, s * .86, cloudC); break;
        }
    }

    // ───────────────────────────── logotipo (identidad desacoplada: se elige por clave)
    private void DrawLogo(string key, double cx, double cy, double box)
    {
        var c = Fg(1f);
        switch (key)
        {
            default:   // «planet»: esfera con anillo inclinado
            {
                _ctx.Transform = Matrix3x2.CreateRotation(-0.38f, new Vector2((float)cx, (float)cy));
                using (var ringBack = EllipseArc(cx, cy, 36, 9.5, Math.PI, Math.PI * 2)) Stroke(ringBack, c, 4.2f);
                FillCircle(cx, cy, 14.5f, c);
                using (var ringFront = EllipseArc(cx, cy, 36, 9.5, 0, Math.PI)) Stroke(ringFront, Dark ? new Color4(0.13f, 0.13f, 0.15f, 1) : new Color4(0.96f, 0.96f, 0.97f, 1), 7f);   // recorte alrededor para separar el anillo de la esfera
                using (var ringFront2 = EllipseArc(cx, cy, 36, 9.5, 0, Math.PI)) Stroke(ringFront2, c, 4.2f);
                _ctx.Transform = Matrix3x2.Identity; break;
            }
        }
    }

    private ID2D1PathGeometry EllipseArc(double cx, double cy, double rx, double ry, double a0, double a1)
    {
        var g = _dev.Factory.CreatePathGeometry(); using var s = g.Open(); Vector2 P(double a) => new((float)(cx + rx * Math.Cos(a)), (float)(cy + ry * Math.Sin(a)));
        s.BeginFigure(P(a0), FigureBegin.Hollow); s.AddArc(new ArcSegment { Point = P(a1), Size = new Size((float)rx, (float)ry), SweepDirection = SweepDirection.Clockwise, ArcSize = ArcSize.Small }); s.EndFigure(FigureEnd.Open); s.Close(); return g;
    }

    // ───────────────────────────── glifos nuevos
    private void HubGlyph(string id, double cx, double cy, float s, Color4 c)
    {
        switch (id)
        {
            case "mic": case "mic-muted":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s * .38, cy - s, cx + s * .38, cy + s * .25), RadiusX = (float)(s * .38), RadiusY = (float)(s * .38) }, _solid, 1.5f);
                { using var a = Arc(cx, cy - s * .05, s * .72, 0, Math.PI); Stroke(a, c, 1.5f); } Line(cx, cy + s * .67, cx, cy + s * 1.0, c, 1.5f); Line(cx - s * .35, cy + s * 1.0, cx + s * .35, cy + s * 1.0, c, 1.5f);
                if (id == "mic-muted") Line(cx - s * .85, cy - s * .85, cx + s * .85, cy + s * .85, c, 1.8f); break;
            case "cpu":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s * .62, cy - s * .62, cx + s * .62, cy + s * .62), RadiusX = 2, RadiusY = 2 }, _solid, 1.5f);
                Brush(c); _ctx.DrawRectangle(R(cx - s * .28, cy - s * .28, cx + s * .28, cy + s * .28), _solid, 1.2f);
                for (int i = -1; i <= 1; i++) { double p = i * s * .34; Line(cx + p, cy - s * .62, cx + p, cy - s * .95, c, 1.3f); Line(cx + p, cy + s * .62, cx + p, cy + s * .95, c, 1.3f); Line(cx - s * .62, cy + p, cx - s * .95, cy + p, c, 1.3f); Line(cx + s * .62, cy + p, cx + s * .95, cy + p, c, 1.3f); }
                break;
            case "gpu":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s, cy - s * .5, cx + s, cy + s * .55), RadiusX = 2, RadiusY = 2 }, _solid, 1.4f);
                Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)(cx + s * .28), (float)(cy + s * .02)), (float)(s * .3), (float)(s * .3)), _solid, 1.3f); Line(cx - s * .78, cy - s * .12, cx - s * .25, cy - s * .12, c, 1.3f); Line(cx - s * .78, cy + s * .2, cx - s * .4, cy + s * .2, c, 1.3f); break;
            case "globe":
                Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), s, s), _solid, 1.5f); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)(s * .45), s), _solid, 1.3f);
                Line(cx - s, cy, cx + s, cy, c, 1.3f); Line(cx - s * .86, cy - s * .5, cx + s * .86, cy - s * .5, c, 1.1f); Line(cx - s * .86, cy + s * .5, cx + s * .86, cy + s * .5, c, 1.1f); break;
            case "disk":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s, cy - s * .38, cx + s, cy + s * .42), RadiusX = (float)(s * .28), RadiusY = (float)(s * .28) }, _solid, 1.5f); Line(cx - s * .7, cy + s * .03, cx + s * .2, cy + s * .03, c, 1.3f); FillCircle(cx + s * .62, cy + s * .03, 1.5f, c); break;
            case "clock":
                Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), s, s), _solid, 1.4f); Line(cx, cy, cx, cy - s * .6, c, 1.4f); Line(cx, cy, cx + s * .45, cy + s * .2, c, 1.4f); break;
            case "gear":
                Brush(c); _ctx.DrawEllipse(new Ellipse(new Vector2((float)cx, (float)cy), (float)(s * .55), (float)(s * .55)), _solid, 1.5f);
                for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(cx + Math.Cos(a) * s * .72, cy + Math.Sin(a) * s * .72, cx + Math.Cos(a) * s * 1.0, cy + Math.Sin(a) * s * 1.0, c, 2.4f); }
                break;
            case "eject":
            { using var tri = Poly(true, new Vector2((float)cx, (float)(cy - s * .75)), new Vector2((float)(cx + s * .85), (float)(cy + s * .25)), new Vector2((float)(cx - s * .85), (float)(cy + s * .25))); Brush(c); _ctx.FillGeometry(tri, _solid); FillRR(cx - s * .85, cy + s * .5, cx + s * .85, cy + s * .85, 1.5f, c); break; }
            case "folder":
                Brush(c); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(cx - s, cy - s * .55, cx + s, cy + s * .7), RadiusX = 2, RadiusY = 2 }, _solid, 1.4f); Line(cx - s, cy - s * .2, cx - s * .1, cy - s * .2, c, 1.3f); Line(cx - s * .1, cy - s * .2, cx + s * .1, cy - s * .55, c, 1.3f); break;
            case "refresh":
            { using var a = Arc(cx, cy, s * .85, -Math.PI * .25, Math.PI * 1.35); Stroke(a, c, 1.6f); Line(cx + s * .55, cy - s * .75, cx + s * .85, cy - s * .35, c, 1.6f); Line(cx + s * .55, cy - s * .75, cx + s * .1, cy - s * .55, c, 1.6f); break; }
            case "speed":
            { using var a = Arc(cx, cy + s * .45, s, Math.PI, Math.PI * 2); Stroke(a, c, 1.6f); Line(cx, cy + s * .45, cx + s * .5, cy - s * .15, c, 1.7f); FillCircle(cx, cy + s * .45, 1.8f, c); break; }
            case "sliders":
                for (int i = 0; i < 3; i++) { double yy = cy - s * .62 + i * s * .62; Line(cx - s, yy, cx + s, yy, c, 1.4f); FillCircle(cx + s * (i == 1 ? -.35 : .4), yy, 2.4f, c); }
                break;
            case "updown": Line(cx - s * .55, cy - s * .25, cx, cy - s * .8, c, 1.5f); Line(cx, cy - s * .8, cx + s * .55, cy - s * .25, c, 1.5f); Line(cx - s * .55, cy + s * .25, cx, cy + s * .8, c, 1.5f); Line(cx, cy + s * .8, cx + s * .55, cy + s * .25, c, 1.5f); break;
            case "arrow-down": Line(cx, cy - s, cx, cy + s * .85, c, 2f); Line(cx - s * .6, cy + s * .25, cx, cy + s * .9, c, 2f); Line(cx + s * .6, cy + s * .25, cx, cy + s * .9, c, 2f); break;
            case "arrow-up": Line(cx, cy + s, cx, cy - s * .85, c, 2f); Line(cx - s * .6, cy - s * .25, cx, cy - s * .9, c, 2f); Line(cx + s * .6, cy - s * .25, cx, cy - s * .9, c, 2f); break;
            case "locate": { using var tri = Poly(true, new Vector2((float)(cx + s), (float)(cy - s)), new Vector2((float)(cx - s * .9), (float)(cy + s * .1)), new Vector2((float)(cx - s * .1), (float)(cy + s * .1)), new Vector2((float)(cx - s * .1), (float)(cy + s * .95))); Brush(c); _ctx.FillGeometry(tri, _solid); break; }
        }
    }
}
