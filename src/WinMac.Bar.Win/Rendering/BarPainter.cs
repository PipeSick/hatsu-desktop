using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Dibuja un <see cref="BarView"/> con Direct2D (mismo contexto que usa el Dock). Aspecto PROVISIONAL (docs/12 §D5): sin referencia de macOS todavía.
/// Los iconos de estado son vectoriales (geometrías propias), sin fuentes ni imágenes de terceros. Unidades en DIP.
/// </summary>
public sealed class BarPainter : IDisposable
{
    private readonly D2DDevice _dev; private readonly ID2D1DeviceContext _ctx; private readonly BarMetrics _m;
    private readonly IDWriteFactory _dw; private readonly IDWriteTextFormat _regular, _bold, _logo;
    private readonly ID2D1SolidColorBrush _solid; private readonly ID2D1StrokeStyle _round;
    private ID2D1Bitmap? _backdrop;
    public BarTheme Theme { get; set; } = BarTheme.Dark;
    public BarBackground Background { get; set; } = BarBackground.Glass;

    public BarPainter(D2DDevice dev, BarMetrics m)
    {
        _dev = dev; _ctx = dev.Context; _m = m;
        _solid = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _regular = _dw.CreateTextFormat("Segoe UI", (float)m.FontSize); _bold = _dw.CreateTextFormat("Segoe UI Semibold", (float)m.FontSize); _logo = _dw.CreateTextFormat("Segoe UI Semibold", 10.5f);
        _round = dev.Factory.CreateStrokeStyle(new StrokeStyleProperties { StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round });
    }

    /// <summary>Fondo ya desenfocado del tamaño de la barra (nivel Glass). null = sin fondo.</summary>
    public void SetBackdrop(ID2D1Bitmap? blurred) => _backdrop = blurred;

    public double Measure(string text, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var l = _dw.CreateTextLayout(text, bold ? _bold : _regular, 4000, 40); return l.Metrics.WidthIncludingTrailingWhitespace;
    }

    private Color4 Fg(float a = 1f) => Theme == BarTheme.Dark ? new Color4(1, 1, 1, 0.95f * a) : new Color4(0, 0, 0, 0.86f * a);
    private void Brush(Color4 c) { _solid.Color = c; }

    /// <summary>Pinta la barra en la ventana: borra primero (la ventana es transparente fuera de lo que se dibuja).</summary>
    public void Paint(BarView v) => PaintCore(v, clear: true);
    /// <summary>Pinta la barra SOBRE lo que ya hay en el destino (render fuera de pantalla con un fondo debajo).</summary>
    public void PaintOver(BarView v) => PaintCore(v, clear: false);

    private void PaintCore(BarView v, bool clear)
    {
        var ctx = _ctx; float w = (float)v.Frame.Width, h = (float)v.Frame.Height;
        ctx.BeginDraw(); if (clear) ctx.Clear(new Color4(0, 0, 0, 0));
        DrawBackground(w, h);
        for (int i = 0; i < v.Frame.Items.Count; i++)
        {
            var f = v.Frame.Items[i]; double a = i < v.HoverAlpha.Count ? v.HoverAlpha[i] : 0;
            if (a > 0.005 && BarModel.IsClickable(f.Item.Kind))
            {
                Brush(Theme == BarTheme.Dark ? new Color4(1, 1, 1, (float)(0.20 * a)) : new Color4(0, 0, 0, (float)(0.11 * a)));
                float iy = (float)_m.HighlightInsetY, r = (float)_m.HighlightRadius;
                ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF((float)f.X, iy, (float)(f.X + f.W), h - iy), RadiusX = r, RadiusY = r }, _solid);
            }
            DrawItem(f, v.State, h);
        }
        ctx.EndDraw();
    }

    private void DrawBackground(float w, float h)
    {
        if (Background == BarBackground.Glass)
        {
            if (_backdrop is not null)
            {
                using var br = _ctx.CreateBitmapBrush(_backdrop, new BitmapBrushProperties { ExtendModeX = ExtendMode.Clamp, ExtendModeY = ExtendMode.Clamp, InterpolationMode = BitmapInterpolationMode.Linear });
                _ctx.FillRectangle(new RawRectF(0, 0, w, h), br);
            }
            Brush(Theme == BarTheme.Dark ? new Color4(0.12f, 0.13f, 0.16f, 0.38f) : new Color4(0.97f, 0.97f, 0.99f, 0.45f)); _ctx.FillRectangle(new RawRectF(0, 0, w, h), _solid);
            Brush(Theme == BarTheme.Dark ? new Color4(1, 1, 1, 0.10f) : new Color4(0, 0, 0, 0.10f)); _ctx.FillRectangle(new RawRectF(0, h - 1, w, h), _solid);   // filete inferior
        }
        else
        {   // Transparente: solo un velo muy ligero para que el texto siga legible sobre cualquier fondo
            using var g = _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, h) },
                _ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = Theme == BarTheme.Dark ? new Color4(0, 0, 0, 0.30f) : new Color4(1, 1, 1, 0.32f) }, new GradientStop { Position = 1, Color = Theme == BarTheme.Dark ? new Color4(0, 0, 0, 0.0f) : new Color4(1, 1, 1, 0.0f) }], Gamma.StandardRgb, ExtendMode.Clamp));
            _ctx.FillRectangle(new RawRectF(0, 0, w, h), g);
        }
    }

    private void DrawItem(BarItemFrame f, BarState s, float h)
    {
        float x = (float)(f.X + _m.ItemPadX), cy = h / 2f;
        if (f.Item.GlyphWidth > 0)
        {
            switch (f.Item.Kind)
            {
                case BarItemKind.WinMacMenu: DrawLogo(x, cy); break;
                case BarItemKind.Network: DrawNetwork(x, cy, s.Network.Kind); break;
                case BarItemKind.Volume: DrawVolume(x, cy, s.Volume); break;
                case BarItemKind.Battery: DrawBattery(x, cy, s.Battery); break;
            }
            x += (float)(f.Item.GlyphWidth + (string.IsNullOrEmpty(f.Item.Text) ? 0 : _m.GlyphTextGap));
        }
        if (!string.IsNullOrEmpty(f.Item.Text))
        {
            var fmt = f.Item.Bold ? _bold : _regular; string t = Fit(f.Item.Text, fmt, (float)f.ContentMaxW - (float)(f.Item.GlyphWidth > 0 ? f.Item.GlyphWidth + _m.GlyphTextGap : 0));
            using var l = _dw.CreateTextLayout(t, fmt, 4000, h); l.ParagraphAlignment = ParagraphAlignment.Center;
            Brush(Fg()); _ctx.DrawTextLayout(new Vector2(x, 0), l, _solid);
        }
    }

    private string Fit(string text, IDWriteTextFormat fmt, float maxW)
    {
        if (maxW <= 8) return "…";
        using (var l = _dw.CreateTextLayout(text, fmt, 4000, 40)) if (l.Metrics.WidthIncludingTrailingWhitespace <= maxW + 0.5f) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; using var l = _dw.CreateTextLayout(text[..mid].TrimEnd() + "…", fmt, 4000, 40); if (l.Metrics.WidthIncludingTrailingWhitespace <= maxW) lo = mid; else hi = mid - 1; }
        return lo == 0 ? "…" : text[..lo].TrimEnd() + "…";
    }

    // ------------------------------------------------------------ iconos vectoriales
    private void Stroke(ID2D1Geometry g, float width = 1.4f, float alpha = 1f) { Brush(Fg(alpha)); _ctx.DrawGeometry(g, _solid, width, _round); }

    private ID2D1PathGeometry Arc(float ox, float oy, float r, double a0, double a1)
    {
        var g = _dev.Factory.CreatePathGeometry(); using var s = g.Open();
        Vector2 P(double a) => new((float)(ox + r * Math.Cos(a)), (float)(oy + r * Math.Sin(a)));
        s.BeginFigure(P(a0), FigureBegin.Hollow);
        s.AddArc(new ArcSegment { Point = P(a1), Size = new Vortice.Mathematics.Size(r, r), RotationAngle = 0, SweepDirection = SweepDirection.Clockwise, ArcSize = ArcSize.Small });
        s.EndFigure(FigureEnd.Open); s.Close(); return g;
    }

    private void DrawLogo(float x, float cy)
    {
        var rr = new RoundedRectangle { Rect = new RawRectF(x, cy - 7.5f, x + 15, cy + 7.5f), RadiusX = 4.5f, RadiusY = 4.5f };
        Brush(Fg()); _ctx.DrawRoundedRectangle(rr, _solid, 1.3f);
        using var l = _dw.CreateTextLayout("W", _logo, 15, 15); l.TextAlignment = TextAlignment.Center; l.ParagraphAlignment = ParagraphAlignment.Center;
        _ctx.DrawTextLayout(new Vector2(x, cy - 7.5f), l, _solid);
    }

    private void DrawBattery(float x, float cy, BatteryInfo b)
    {
        float bw = 20.5f, bh = 10.5f, top = cy - bh / 2;
        Brush(Fg(0.85f)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + .6f, top + .6f, x + bw - .6f, top + bh - .6f), RadiusX = 3, RadiusY = 3 }, _solid, 1.2f);
        _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + bw + .6f, cy - 2.2f, x + bw + 2.3f, cy + 2.2f), RadiusX = 0.8f, RadiusY = 0.8f }, _solid);
        float inner = bw - 4.4f; float fw = Math.Max(b.Percent > 0 ? 1.5f : 0, inner * (float)b.Fill);
        Brush(b.Charging ? new Color4(0.30f, 0.80f, 0.40f, 1) : b.Low ? new Color4(1f, 0.30f, 0.27f, 1) : Fg());
        _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + 2.2f, top + 2.2f, x + 2.2f + fw, top + bh - 2.2f), RadiusX = 1.4f, RadiusY = 1.4f }, _solid);
        if (b.Charging)
        {   // rayo
            using var g = _dev.Factory.CreatePathGeometry(); using (var s = g.Open())
            {
                float bx = x + bw / 2f; s.BeginFigure(new Vector2(bx + 1.2f, top + 1.6f), FigureBegin.Filled);
                foreach (var p in new[] { new Vector2(bx - 2.6f, cy + 0.6f), new Vector2(bx - 0.2f, cy + 0.6f), new Vector2(bx - 1.2f, top + bh - 1.6f), new Vector2(bx + 2.6f, cy - 0.6f), new Vector2(bx + 0.2f, cy - 0.6f) }) s.AddLine(p);
                s.EndFigure(FigureEnd.Closed); s.Close();
            }
            Brush(Theme == BarTheme.Dark ? new Color4(0, 0, 0, 0.75f) : new Color4(1, 1, 1, 0.9f)); _ctx.FillGeometry(g, _solid);
        }
    }

    private void DrawVolume(float x, float cy, VolumeInfo v)
    {
        using (var g = _dev.Factory.CreatePathGeometry())
        {
            using (var s = g.Open())
            {
                s.BeginFigure(new Vector2(x + 1, cy - 3), FigureBegin.Filled);
                foreach (var p in new[] { new Vector2(x + 4, cy - 3), new Vector2(x + 8, cy - 6.5f), new Vector2(x + 8, cy + 6.5f), new Vector2(x + 4, cy + 3), new Vector2(x + 1, cy + 3) }) s.AddLine(p);
                s.EndFigure(FigureEnd.Closed); s.Close();
            }
            Brush(Fg()); _ctx.FillGeometry(g, _solid);
        }
        if (v.Muted || v.Waves == 0)
        {
            Brush(Fg(v.Muted ? 1f : 0.55f)); _ctx.DrawLine(new Vector2(x + 10.5f, cy - 3), new Vector2(x + 15f, cy + 3), _solid, 1.4f, _round); _ctx.DrawLine(new Vector2(x + 15f, cy - 3), new Vector2(x + 10.5f, cy + 3), _solid, 1.4f, _round);
            return;
        }
        float[] radii = [3f, 5.5f, 8f];
        for (int i = 0; i < 3; i++) { using var a = Arc(x + 8, cy, radii[i], -Math.PI / 4, Math.PI / 4); Stroke(a, 1.4f, i < v.Waves ? 1f : 0.28f); }
    }

    private void DrawNetwork(float x, float cy, NetKind k)
    {
        if (k == NetKind.Ethernet)
        {
            Brush(Fg()); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + 1.5f, cy - 6.5f, x + 14.5f, cy + 2.5f), RadiusX = 1.8f, RadiusY = 1.8f }, _solid, 1.3f);
            _ctx.DrawLine(new Vector2(x + 8, cy + 2.5f), new Vector2(x + 8, cy + 5.5f), _solid, 1.3f, _round); _ctx.DrawLine(new Vector2(x + 4.5f, cy + 6f), new Vector2(x + 11.5f, cy + 6f), _solid, 1.3f, _round);
            return;
        }
        bool off = k == NetKind.Offline; float ox = x + 8, oy = cy + 5.5f;   // sector de 90° apuntando hacia arriba
        float[] radii = [4f, 7.5f, 11f];
        for (int i = 0; i < 3; i++) { using var a = Arc(ox, oy, radii[i], -3 * Math.PI / 4, -Math.PI / 4); Stroke(a, 1.4f, off ? 0.30f : 1f); }
        Brush(Fg(off ? 0.30f : 1f)); _ctx.FillEllipse(new Ellipse(new Vector2(ox, oy - 0.2f), 1.5f, 1.5f), _solid);
        if (off) { Brush(Fg()); _ctx.DrawLine(new Vector2(x + 2, cy - 6), new Vector2(x + 14, cy + 6.5f), _solid, 1.5f, _round); }
    }

    public void Dispose() { _solid.Dispose(); _round.Dispose(); _regular.Dispose(); _bold.Dispose(); _logo.Dispose(); _dw.Dispose(); }
}
