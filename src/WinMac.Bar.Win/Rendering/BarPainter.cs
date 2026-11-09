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

    /// <summary>Botón de las apps en segundo plano: flecha hacia ABAJO (abre el desplegable hacia abajo); con el desplegable abierto, hacia ARRIBA (lo oculta).</summary>
    private void DrawTrayMenu(float x, float cy, bool open)
    {
        Brush(Fg()); float cx = x + 8, d = open ? -1 : 1;   // abajo: la punta abajo; arriba: la punta arriba
        _ctx.DrawLine(new Vector2(cx - 4.5f, cy - 2.2f * d), new Vector2(cx, cy + 2.4f * d), _solid, 1.6f, _round);
        _ctx.DrawLine(new Vector2(cx, cy + 2.4f * d), new Vector2(cx + 4.5f, cy - 2.2f * d), _solid, 1.6f, _round);
    }

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
            if (v.DragId is not null && f.Item.Id == v.DragId) a = 1;   // el módulo que se arrastra queda resaltado
            if (a > 0.005 && BarModel.IsClickable(f.Item.Kind))
            {
                Brush(Theme == BarTheme.Dark ? new Color4(1, 1, 1, (float)(0.20 * a)) : new Color4(0, 0, 0, (float)(0.11 * a)));
                float iy = (float)_m.HighlightInsetY, r = (float)_m.HighlightRadius;
                ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF((float)f.X, iy, (float)(f.X + f.W), h - iy), RadiusX = r, RadiusY = r }, _solid);
            }
            DrawItem(f, v.State, h);
        }
        if (v.DropX is double dx)
        {   // indicador de inserción: línea fina con el color de acento de Windows (neutro si no hay)
            Brush(Theme == BarTheme.Dark ? new Color4(0.40f, 0.70f, 1f, 1f) : new Color4(0.0f, 0.40f, 0.85f, 1f));
            ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF((float)dx - 1f, 5f, (float)dx + 1f, h - 5f), RadiusX = 1f, RadiusY = 1f }, _solid);
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
                case BarItemKind.Network: DrawNetwork(x, cy, s.Network.Kind, s.Network.Bars); break;
                case BarItemKind.Bluetooth: DrawBluetooth(x, cy, s.Bt.On); break;
                case BarItemKind.ControlCenter: DrawControlCenter(x, cy); break;
                case BarItemKind.Hub: DrawHubIcon(x + 8, cy); break;
                case BarItemKind.Weather: DrawWeatherGlyph(x + 8, cy, s.Weather.IconKey); break;
                case BarItemKind.Volume: DrawVolume(x, cy, s.Volume); break;
                case BarItemKind.Battery: DrawBattery(x, cy, s.Battery); break;
                case BarItemKind.TrayMenu: DrawTrayMenu(x, cy, s.TrayOpen); break;
                case BarItemKind.Phone: DrawPhone(x, cy, f.Item.Look); break;
                case BarItemKind.TaskManager: DrawActivity(x, cy); break;
                case BarItemKind.PowerPlug: DrawPlug(x + 8, cy); break;
                case BarItemKind.Peripherals: DrawMouse(x + 8, cy); break;
                case BarItemKind.Cleanup: DrawSparkles(x + 8, cy); break;
                case BarItemKind.Vpn: DrawShield(x + 8, cy, f.Item.Look == BarItemLook.Normal); break;
                case BarItemKind.HubSystem: DrawChip(x + 8, cy); break;
                case BarItemKind.HubNetwork: DrawGlobe(x + 8, cy); break;
                case BarItemKind.HubDisks: DrawDisk(x + 8, cy); break;
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

    /// <summary>El icono único de la aplicación (planeta con anillo, como el logotipo del panel). La identidad es provisional: este dibujo es lo único que cambia al cambiar el logotipo.</summary>
    private void DrawHubIcon(float cx, float cy)
    {
        // Marca de Hatsu: una «H» redondeada con la barra inclinada en cinta (monocroma, tomando el color de la barra)
        Brush(Fg());
        _ctx.DrawLine(new Vector2(cx - 4.6f, cy - 6.4f), new Vector2(cx - 4.6f, cy + 6.4f), _solid, 2.1f, _round);
        _ctx.DrawLine(new Vector2(cx + 4.6f, cy - 6.4f), new Vector2(cx + 4.6f, cy + 6.4f), _solid, 2.1f, _round);
        _ctx.DrawLine(new Vector2(cx - 4.6f, cy + 1.6f), new Vector2(cx + 4.6f, cy - 1.6f), _solid, 2.1f, _round);
    }

    /// <summary>Icono del Tiempo en la barra: pequeño y reconocible (sol, luna, nube, lluvia…), con los mismos códigos que el panel.</summary>
    private void DrawWeatherGlyph(float cx, float cy, string key)
    {
        var yellow = new Color4(1f, 0.80f, 0.04f, 1f); var blue = new Color4(0.35f, 0.70f, 1f, 1f);
        void Cloud(float ox, float oy, float k)
        {
            Brush(Fg()); _ctx.FillEllipse(new Ellipse(new Vector2(ox - 3.2f * k, oy + 1f * k), 3.4f * k, 3.4f * k), _solid); _ctx.FillEllipse(new Ellipse(new Vector2(ox + 0.5f * k, oy - 1.4f * k), 4.4f * k, 4.4f * k), _solid); _ctx.FillEllipse(new Ellipse(new Vector2(ox + 4f * k, oy + 1.2f * k), 3f * k, 3f * k), _solid);
            _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(ox - 6.4f * k, oy + 1f * k, ox + 6.8f * k, oy + 4.3f * k), RadiusX = 1.6f * k, RadiusY = 1.6f * k }, _solid);
        }

        switch (key)
        {
            case "clear": Brush(yellow); _ctx.FillEllipse(new Ellipse(new Vector2(cx, cy), 4.2f, 4.2f), _solid); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; _ctx.DrawLine(new Vector2(cx + (float)Math.Cos(a) * 6.2f, cy + (float)Math.Sin(a) * 6.2f), new Vector2(cx + (float)Math.Cos(a) * 7.8f, cy + (float)Math.Sin(a) * 7.8f), _solid, 1.3f, _round); } break;
            case "clear-night": Brush(Fg()); _ctx.FillEllipse(new Ellipse(new Vector2(cx, cy), 5.6f, 5.6f), _solid); Brush(new Color4(0.13f, 0.13f, 0.15f, 1f)); _ctx.FillEllipse(new Ellipse(new Vector2(cx + 2.8f, cy - 2.2f), 4.8f, 4.8f), _solid); break;
            case "partly": Brush(yellow); _ctx.FillEllipse(new Ellipse(new Vector2(cx - 3f, cy - 3f), 3.6f, 3.6f), _solid); Cloud(cx + 1f, cy + 2f, 0.85f); break;
            case "partly-night": Brush(Fg(0.85f)); _ctx.FillEllipse(new Ellipse(new Vector2(cx - 3f, cy - 3f), 3.4f, 3.4f), _solid); Cloud(cx + 1f, cy + 2f, 0.85f); break;
            case "rain": case "drizzle": Cloud(cx, cy - 2.2f, 0.9f); Brush(blue); for (int i = -1; i <= 1; i += 2) _ctx.DrawLine(new Vector2(cx + i * 2.8f + 0.8f, cy + 3.2f), new Vector2(cx + i * 2.8f - 0.6f, cy + 6.4f), _solid, 1.4f, _round); break;
            case "snow": Cloud(cx, cy - 2.2f, 0.9f); for (int i = -1; i <= 1; i += 2) _ctx.FillEllipse(new Ellipse(new Vector2(cx + i * 2.8f, cy + 4.6f), 1.2f, 1.2f), _solid); break;
            case "thunder": Cloud(cx, cy - 2.2f, 0.9f); Brush(yellow); _ctx.DrawLine(new Vector2(cx + 0.8f, cy + 2.8f), new Vector2(cx - 1.4f, cy + 5.2f), _solid, 1.6f, _round); _ctx.DrawLine(new Vector2(cx - 1.4f, cy + 5.2f), new Vector2(cx + 1f, cy + 5.2f), _solid, 1.6f, _round); break;
            case "fog": Cloud(cx, cy - 2.6f, 0.85f); Brush(Fg()); _ctx.DrawLine(new Vector2(cx - 5f, cy + 4f), new Vector2(cx + 5f, cy + 4f), _solid, 1.3f, _round); _ctx.DrawLine(new Vector2(cx - 4f, cy + 6.2f), new Vector2(cx + 4f, cy + 6.2f), _solid, 1.3f, _round); break;
            default: Cloud(cx, cy, 1f); break;
        }
    }

    private ID2D1Bitmap? _brand32, _brand128; private bool _brandTried;

    private void DrawLogo(float x, float cy)
    {
        // Hatsu Desktop (Flux): el icono de la marca, 16 DIP; si el recurso no está, el contorno con la «H» de siempre
        if (!_brandTried) { _brandTried = true; _brand32 = BrandIcon.Create(_ctx, 32); _brand128 = BrandIcon.Create(_ctx, 128); }
        var bmp = BrandIcon.PickSize(16 * _ctx.Dpi.Width / 96.0) == 32 ? _brand32 : _brand128;
        if (bmp is not null) { _ctx.DrawBitmap(bmp, new RawRectF(x - 0.5f, cy - 8f, x + 15.5f, cy + 8f), 1f, BitmapInterpolationMode.Linear, null); return; }
        var rr = new RoundedRectangle { Rect = new RawRectF(x, cy - 7.5f, x + 15, cy + 7.5f), RadiusX = 4.5f, RadiusY = 4.5f };
        Brush(Fg()); _ctx.DrawRoundedRectangle(rr, _solid, 1.3f);
        using var l = _dw.CreateTextLayout("H", _logo, 15, 15); l.TextAlignment = TextAlignment.Center; l.ParagraphAlignment = ParagraphAlignment.Center;
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
        else if (b.Plugged)
        {   // con corriente pero sin cargar (Windows lo distingue): enchufe pequeño en lugar del rayo
            float bx = x + bw / 2f; Brush(Theme == BarTheme.Dark ? new Color4(0, 0, 0, 0.75f) : new Color4(1, 1, 1, 0.9f));
            _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(bx - 2.4f, cy - 0.9f, bx + 2.4f, cy + 2.6f), RadiusX = 1f, RadiusY = 1f }, _solid);
            _ctx.DrawLine(new Vector2(bx - 1.1f, cy - 3.2f), new Vector2(bx - 1.1f, cy - 0.9f), _solid, 1f); _ctx.DrawLine(new Vector2(bx + 1.1f, cy - 3.2f), new Vector2(bx + 1.1f, cy - 0.9f), _solid, 1f);
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

    private void DrawNetwork(float x, float cy, NetKind k, int bars = -1)
    {
        if (k == NetKind.Ethernet)
        {
            Brush(Fg()); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + 1.5f, cy - 6.5f, x + 14.5f, cy + 2.5f), RadiusX = 1.8f, RadiusY = 1.8f }, _solid, 1.3f);
            _ctx.DrawLine(new Vector2(x + 8, cy + 2.5f), new Vector2(x + 8, cy + 5.5f), _solid, 1.3f, _round); _ctx.DrawLine(new Vector2(x + 4.5f, cy + 6f), new Vector2(x + 11.5f, cy + 6f), _solid, 1.3f, _round);
            return;
        }
        bool off = k == NetKind.Offline; float ox = x + 8, oy = cy + 5.5f;   // sector de 90° apuntando hacia arriba
        float[] radii = [4f, 7.5f, 11f];
        int active = bars < 0 ? 3 : (int)Math.Ceiling(bars * 3 / 4.0);   // intensidad real de la señal (sin dato = todas)
        for (int i = 0; i < 3; i++) { using var a = Arc(ox, oy, radii[i], -3 * Math.PI / 4, -Math.PI / 4); Stroke(a, 1.4f, off ? 0.30f : i < active ? 1f : 0.28f); }
        Brush(Fg(off ? 0.30f : 1f)); _ctx.FillEllipse(new Ellipse(new Vector2(ox, oy - 0.2f), 1.5f, 1.5f), _solid);
        if (off) { Brush(Fg()); _ctx.DrawLine(new Vector2(x + 2, cy - 6), new Vector2(x + 14, cy + 6.5f), _solid, 1.5f, _round); }
    }

    /// <summary>WinMac Connect: un iPhone de perfil (contorno redondeado con la isla arriba). Atenuado al conectar; tachado sin conexión.</summary>
    private void DrawPhone(float x, float cy, BarItemLook look)
    {
        float cx = x + 8, a = look == BarItemLook.Normal ? 1f : look == BarItemLook.Dim ? 0.45f : 0.6f; Brush(Fg(a));
        _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 4.2f, cy - 7.2f, cx + 4.2f, cy + 7.2f), RadiusX = 2.2f, RadiusY = 2.2f }, _solid, 1.3f);
        _ctx.DrawLine(new Vector2(cx - 1.4f, cy - 5.1f), new Vector2(cx + 1.4f, cy - 5.1f), _solid, 1.3f, _round);
        if (look == BarItemLook.Slashed) { Brush(Fg()); _ctx.DrawLine(new Vector2(x + 2, cy - 6.5f), new Vector2(x + 14, cy + 6.5f), _solid, 1.5f, _round); }
    }

    /// <summary>Administrador de tareas: icono de actividad (pulso).</summary>
    private void DrawActivity(float x, float cy)
    {
        Brush(Fg());
        using var g = _dev.Factory.CreatePathGeometry(); using (var s = g.Open())
        {
            s.BeginFigure(new Vector2(x + 0.8f, cy), FigureBegin.Hollow);
            foreach (var p in new[] { new Vector2(x + 4.2f, cy), new Vector2(x + 6.6f, cy - 6.2f), new Vector2(x + 9.6f, cy + 6.2f), new Vector2(x + 11.8f, cy), new Vector2(x + 15.2f, cy) }) s.AddLine(p);
            s.EndFigure(FigureEnd.Open); s.Close();
        }
        _ctx.DrawGeometry(g, _solid, 1.4f, _round);
    }

    /// <summary>Periféricos: un ratón de perfil.</summary>
    private void DrawMouse(float cx, float cy)
    {
        Brush(Fg());
        _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 4.6f, cy - 7f, cx + 4.6f, cy + 7f), RadiusX = 4.6f, RadiusY = 4.6f }, _solid, 1.3f);
        _ctx.DrawLine(new Vector2(cx, cy - 7f), new Vector2(cx, cy - 1.5f), _solid, 1.2f, _round); _ctx.DrawLine(new Vector2(cx - 4.6f, cy - 1.5f), new Vector2(cx + 4.6f, cy - 1.5f), _solid, 1.2f, _round);
    }

    /// <summary>Rendimiento: un chip (cuadrado con patitas).</summary>
    private void DrawChip(float cx, float cy)
    {
        Brush(Fg());
        _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 4.6f, cy - 4.6f, cx + 4.6f, cy + 4.6f), RadiusX = 1.6f, RadiusY = 1.6f }, _solid, 1.5f);
        _ctx.FillRectangle(new RawRectF(cx - 1.8f, cy - 1.8f, cx + 1.8f, cy + 1.8f), _solid);
        for (int i = -1; i <= 1; i++)
        {
            float o = i * 3f;
            _ctx.DrawLine(new Vector2(cx + o, cy - 7.4f), new Vector2(cx + o, cy - 4.6f), _solid, 1.2f, _round); _ctx.DrawLine(new Vector2(cx + o, cy + 4.6f), new Vector2(cx + o, cy + 7.4f), _solid, 1.2f, _round);
            _ctx.DrawLine(new Vector2(cx - 7.4f, cy + o), new Vector2(cx - 4.6f, cy + o), _solid, 1.2f, _round); _ctx.DrawLine(new Vector2(cx + 4.6f, cy + o), new Vector2(cx + 7.4f, cy + o), _solid, 1.2f, _round);
        }
    }

    /// <summary>Red: un globo (círculo con meridiano y ecuador).</summary>
    private void DrawGlobe(float cx, float cy)
    {
        Brush(Fg()); _ctx.DrawEllipse(new Ellipse(new Vector2(cx, cy), 6.6f, 6.6f), _solid, 1.5f);
        _ctx.DrawEllipse(new Ellipse(new Vector2(cx, cy), 2.8f, 6.6f), _solid, 1.2f);
        _ctx.DrawLine(new Vector2(cx - 6.6f, cy), new Vector2(cx + 6.6f, cy), _solid, 1.2f, _round);
    }

    /// <summary>Discos: una unidad (rectángulo redondeado con una luz).</summary>
    private void DrawDisk(float cx, float cy)
    {
        Brush(Fg()); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 7f, cy - 4.2f, cx + 7f, cy + 4.2f), RadiusX = 2.4f, RadiusY = 2.4f }, _solid, 1.5f);
        _ctx.DrawLine(new Vector2(cx - 7f, cy + 0.4f), new Vector2(cx + 7f, cy + 0.4f), _solid, 1.2f, _round);
        _ctx.FillEllipse(new Ellipse(new Vector2(cx + 4.2f, cy + 2.4f), 0.9f, 0.9f), _solid);
    }

    /// <summary>VPN: escudo; relleno con una marca cuando hay una conexión activa, solo contorno cuando no.</summary>
    private void DrawShield(float cx, float cy, bool on)
    {
        using var g = _dev.Factory.CreatePathGeometry(); using (var s = g.Open())
        {
            s.BeginFigure(new Vector2(cx, cy - 7.2f), FigureBegin.Filled);
            s.AddLine(new Vector2(cx + 6.2f, cy - 4.6f)); s.AddLine(new Vector2(cx + 6.2f, cy + 0.6f));
            s.AddBezier(new BezierSegment { Point1 = new Vector2(cx + 6.2f, cy + 4.2f), Point2 = new Vector2(cx + 3f, cy + 6.2f), Point3 = new Vector2(cx, cy + 7.4f) });
            s.AddBezier(new BezierSegment { Point1 = new Vector2(cx - 3f, cy + 6.2f), Point2 = new Vector2(cx - 6.2f, cy + 4.2f), Point3 = new Vector2(cx - 6.2f, cy + 0.6f) });
            s.AddLine(new Vector2(cx - 6.2f, cy - 4.6f)); s.EndFigure(FigureEnd.Closed); s.Close();
        }

        Brush(Fg());
        if (on)
        {
            _ctx.FillGeometry(g, _solid);
            Brush(new Color4(0.13f, 0.13f, 0.15f, 1f)); _ctx.DrawLine(new Vector2(cx - 2.6f, cy + 0.2f), new Vector2(cx - 0.6f, cy + 2.4f), _solid, 1.7f, _round); _ctx.DrawLine(new Vector2(cx - 0.6f, cy + 2.4f), new Vector2(cx + 3f, cy - 2.2f), _solid, 1.7f, _round);
        }
        else _ctx.DrawGeometry(g, _solid, 1.5f);
    }

    /// <summary>Cleanup: destellos (una estrella de cuatro puntas grande y una pequeña).</summary>
    private void DrawSparkles(float cx, float cy)
    {
        void Star(float sx, float sy, float r)
        {
            using var g = _dev.Factory.CreatePathGeometry(); using (var s = g.Open())
            {
                s.BeginFigure(new Vector2(sx, sy - r), FigureBegin.Filled);
                foreach (var p in new[] { new Vector2(sx + r * .28f, sy - r * .28f), new Vector2(sx + r, sy), new Vector2(sx + r * .28f, sy + r * .28f), new Vector2(sx, sy + r), new Vector2(sx - r * .28f, sy + r * .28f), new Vector2(sx - r, sy), new Vector2(sx - r * .28f, sy - r * .28f) }) s.AddLine(p);
                s.EndFigure(FigureEnd.Closed); s.Close();
            }
            Brush(Fg()); _ctx.FillGeometry(g, _solid);
        }
        Star(cx - 1.5f, cy + 1.5f, 6.2f); Star(cx + 5.2f, cy - 5f, 2.8f);
    }

    /// <summary>Enchufe (equipo sin batería conectado a la corriente).</summary>
    private void DrawPlug(float cx, float cy)
    {
        Brush(Fg());
        _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - 4.2f, cy - 2.2f, cx + 4.2f, cy + 3.4f), RadiusX = 2f, RadiusY = 2f }, _solid, 1.3f);
        _ctx.DrawLine(new Vector2(cx - 2f, cy - 2.2f), new Vector2(cx - 2f, cy - 6.4f), _solid, 1.3f, _round); _ctx.DrawLine(new Vector2(cx + 2f, cy - 2.2f), new Vector2(cx + 2f, cy - 6.4f), _solid, 1.3f, _round);
        _ctx.DrawLine(new Vector2(cx, cy + 3.4f), new Vector2(cx, cy + 7f), _solid, 1.3f, _round);
    }

    private void DrawBluetooth(float x, float cy, bool on)
    {
        float cx = x + 8, a = on ? 1f : 0.4f; Brush(Fg(a));
        _ctx.DrawLine(new Vector2(cx - 3.8f, cy - 3.8f), new Vector2(cx + 3.8f, cy + 3.8f), _solid, 1.4f, _round); _ctx.DrawLine(new Vector2(cx + 3.8f, cy + 3.8f), new Vector2(cx, cy + 7f), _solid, 1.4f, _round);
        _ctx.DrawLine(new Vector2(cx, cy + 7f), new Vector2(cx, cy - 7f), _solid, 1.4f, _round); _ctx.DrawLine(new Vector2(cx, cy - 7f), new Vector2(cx + 3.8f, cy - 3.8f), _solid, 1.4f, _round); _ctx.DrawLine(new Vector2(cx + 3.8f, cy - 3.8f), new Vector2(cx - 3.8f, cy + 3.8f), _solid, 1.4f, _round);
        if (!on) _ctx.DrawLine(new Vector2(x + 2, cy - 6.5f), new Vector2(x + 14, cy + 6.5f), _solid, 1.5f, _round);   // radio apagada
    }

    /// <summary>Icono del Centro de control: dos interruptores superpuestos, como en macOS.</summary>
    private void DrawControlCenter(float x, float cy)
    {
        Brush(Fg()); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + 1f, cy - 6.5f, x + 15f, cy - 0.5f), RadiusX = 3, RadiusY = 3 }, _solid, 1.2f);
        _ctx.FillEllipse(new Ellipse(new Vector2(x + 11.5f, cy - 3.5f), 1.9f, 1.9f), _solid);
        _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(x + 1f, cy + 0.5f, x + 15f, cy + 6.5f), RadiusX = 3, RadiusY = 3 }, _solid, 1.2f);
        _ctx.FillEllipse(new Ellipse(new Vector2(x + 4.5f, cy + 3.5f), 1.9f, 1.9f), _solid);
    }

    public void Dispose() { _brand32?.Dispose(); _brand128?.Dispose(); _solid.Dispose(); _round.Dispose(); _regular.Dispose(); _bold.Dispose(); _logo.Dispose(); _dw.Dispose(); }
}
