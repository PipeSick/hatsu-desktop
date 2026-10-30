using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Shell;

namespace WinMac.Dock.Win.Rendering;

public interface IIconSource { IconPixels? Get(DockItemModel item); }

/// <summary>
/// Dibuja un <see cref="DockView"/> con Direct2D. No conoce ventanas ni swap chains: se usa igual en pantalla (DirectComposition)
/// y fuera de pantalla (CI → PNG). Unidades en DIP; el contexto debe tener el DPI configurado.
/// Material «vidrio»: fondo desenfocado (cacheado, sin coste por frame) + tinte + brillo superior + borde especular doble + sombra por capas.
/// </summary>
public sealed class DockPainter : IDisposable
{
    private readonly D2DDevice _dev; private readonly ID2D1DeviceContext _ctx;
    private readonly DockMetrics _m; private readonly GlassStyle _g; private readonly IIconSource _icons;
    private readonly ID2D1PathGeometry _squircle; private readonly IDWriteFactory _dw; private readonly IDWriteTextFormat _glyphFmt, _labelFmt;
    private readonly ID2D1SolidColorBrush _solid;
    private readonly ID2D1LinearGradientBrush _grad;
    private readonly Dictionary<string, ID2D1Bitmap?> _iconCache = new();
    private ID2D1Bitmap? _backdropBlurred;
    public int IconCacheCount => _iconCache.Count;

    public DockPainter(D2DDevice dev, DockMetrics m, GlassStyle g, IIconSource icons)
    {
        _dev = dev; _ctx = dev.Context; _m = m; _g = g; _icons = icons;
        _solid = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _grad = _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, 1) },
            _ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = new Color4(1, 1, 1, 1) }, new GradientStop { Position = 1, Color = new Color4(1, 1, 1, 1) }], Gamma.StandardRgb, ExtendMode.Clamp));
        _dw = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>();
        _glyphFmt = _dw.CreateTextFormat("Segoe UI Semibold", 24f);
        _labelFmt = _dw.CreateTextFormat("Segoe UI", 13f);
        _squircle = _dev.Factory.CreatePathGeometry();
        using var sink = _squircle.Open();
        var pts = Squircle.Unit();
        sink.BeginFigure(new Vector2((float)pts[0].X, (float)pts[0].Y), FigureBegin.Filled);
        foreach (var p in pts.Skip(1)) sink.AddLine(new Vector2((float)p.X, (float)p.Y));
        sink.EndFigure(FigureEnd.Closed); sink.Close();
    }

    /// <summary>Fondo ya desenfocado y saturado, del tamaño de la ventana (DIP). null = sin fondo (nivel Tint).</summary>
    public void SetBackdrop(ID2D1Bitmap? blurred) => _backdropBlurred = blurred;

    // ---------------------------------------------------------------- helpers
    private void Solid(float r, float g, float b, float a) { _solid.Color = new Color4(r, g, b, a); }
    private ID2D1LinearGradientBrush Grad(Vector2 a, Vector2 b, params (float pos, Color4 c)[] stops)
    {
        using var sc = _ctx.CreateGradientStopCollection(stops.Select(s => new GradientStop { Position = s.pos, Color = s.c }).ToArray(), Gamma.StandardRgb, ExtendMode.Clamp);
        return _ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = a, EndPoint = b }, sc);
    }

    private void DrawBmp(ID2D1Bitmap bmp, RawRectF dest, bool smooth) =>
        _ctx.DrawBitmap(bmp, (RawRectF?)dest, 1f, smooth ? BitmapInterpolationMode.Linear : BitmapInterpolationMode.NearestNeighbor, (RawRectF?)null);

    // ---------------------------------------------------------------- pintado
    public void Paint(DockView v, GlassTier tier, ID2D1Bitmap? underlay = null)
    {
        var ctx = _ctx;
        ctx.BeginDraw();
        ctx.Clear(new Color4(0, 0, 0, 0));
        if (underlay is not null) DrawBmp(underlay, new RawRectF(0, 0, (float)v.WindowWidth, (float)v.WindowHeight), false);

        // Desplazamiento de ocultar/mostrar (solo si el host no lo hace con un offset del Visual).
        ctx.Transform = Matrix3x2.CreateTranslation(0, (float)(v.HideT * _m.HideDistance));
        float barLeft = (float)v.BarLeft, barW = (float)v.BarWidth, barH = (float)v.BarHeight;
        float barBottom = (float)(v.WindowHeight - _m.ShadowMargin), barTop = barBottom - barH;
        var bar = new RawRectF(barLeft, barTop, barLeft + barW, barBottom);
        var rr = new RoundedRectangle { Rect = bar, RadiusX = (float)_m.BarRadius, RadiusY = (float)_m.BarRadius };

        DrawBarShadow(bar);
        DrawGlass(rr, bar, tier);
        DrawSeparators(v, barTop, barH);
        foreach (var it in v.Items) if (it.Model.Kind != DockItemKind.Separator) DrawItem(it, barBottom);
        DrawLabel(v, barBottom);

        ctx.Transform = Matrix3x2.Identity;
        ctx.EndDraw();
    }

    private void DrawBarShadow(RawRectF bar)
    {
        // Gaussiana aproximada con capas concéntricas (sin efectos de D2D: barato y válido en cualquier GPU/WARP).
        int n = 14; float spread = (float)_g.ShadowBlur * 1.6f, dy = (float)_g.ShadowOffsetY;
        for (int i = n; i >= 1; i--)
        {
            float t = i / (float)n, g = spread * t;
            float a = (float)_g.ShadowAlpha * (1f - t) * (1f - t) * 0.5f + 0.004f;
            Solid(0, 0, 0, a);
            _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(bar.Left - g, bar.Top - g + dy, bar.Right + g, bar.Bottom + g + dy), RadiusX = (float)_m.BarRadius + g, RadiusY = (float)_m.BarRadius + g }, _solid);
        }
    }

    private void DrawGlass(RoundedRectangle rr, RawRectF bar, GlassTier tier)
    {
        // 1) Material base: fondo desenfocado (si existe) o gris translúcido.
        if (tier != GlassTier.Tint && _backdropBlurred is not null)
        {
            using var brush = _ctx.CreateBitmapBrush(_backdropBlurred, new BitmapBrushProperties { ExtendModeX = ExtendMode.Clamp, ExtendModeY = ExtendMode.Clamp, InterpolationMode = BitmapInterpolationMode.Linear });
            _ctx.FillRoundedRectangle(rr, brush);
        }
        else { Solid(0.80f, 0.84f, 0.90f, 0.55f); _ctx.FillRoundedRectangle(rr, _solid); }

        // 2) Tinte claro (vidrio «lechoso»), más denso abajo.
        using (var tint = Grad(new Vector2(0, bar.Top), new Vector2(0, bar.Bottom),
            (0f, new Color4(1, 1, 1, (float)(_g.TintAlpha + _g.Brightness))), (1f, new Color4(1, 1, 1, (float)(_g.TintAlpha * 0.8)))))
            _ctx.FillRoundedRectangle(rr, tint);

        // 3) Brillo suave en la mitad superior (reflejo).
        float mid = bar.Top + (bar.Bottom - bar.Top) * 0.55f;
        using (var sheen = Grad(new Vector2(0, bar.Top), new Vector2(0, mid), (0f, new Color4(1, 1, 1, (float)_g.TopSheenAlpha)), (1f, new Color4(1, 1, 1, 0))))
        {
            _ctx.PushAxisAlignedClip(new RawRectF(bar.Left, bar.Top, bar.Right, mid), AntialiasMode.PerPrimitive);
            _ctx.FillRoundedRectangle(rr, sheen);
            _ctx.PopAxisAlignedClip();
        }

        // 4) Borde especular exterior (arriba brillante, abajo tenue) + filo interior.
        using (var rim = Grad(new Vector2(0, bar.Top), new Vector2(0, bar.Bottom), (0f, new Color4(1, 1, 1, (float)_g.RimTopAlpha)), (0.5f, new Color4(1, 1, 1, 0.28f)), (1f, new Color4(1, 1, 1, (float)_g.RimBottomAlpha))))
            _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(bar.Left + .5f, bar.Top + .5f, bar.Right - .5f, bar.Bottom - .5f), RadiusX = (float)_m.BarRadius - .5f, RadiusY = (float)_m.BarRadius - .5f }, rim, 1f);
        using (var inner = Grad(new Vector2(0, bar.Top), new Vector2(0, bar.Bottom), (0f, new Color4(1, 1, 1, 0)), (1f, new Color4(1, 1, 1, (float)_g.InnerRimAlpha))))
            _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(bar.Left + 1.5f, bar.Top + 1.5f, bar.Right - 1.5f, bar.Bottom - 1.5f), RadiusX = (float)_m.BarRadius - 1.5f, RadiusY = (float)_m.BarRadius - 1.5f }, inner, 1f);
    }

    private void DrawSeparators(DockView v, float barTop, float barH)
    {
        foreach (var it in v.Items)
        {
            if (it.Model.Kind != DockItemKind.Separator) continue;
            Solid(1, 1, 1, 0.38f);
            _ctx.DrawLine(new Vector2((float)it.X, barTop + barH * .2f), new Vector2((float)it.X, barTop + barH * .8f), _solid, 1f);
        }
    }

    private void DrawItem(ItemView it, float barBottom)
    {
        float s = (float)it.Size, x = (float)it.X, y = barBottom - (float)_m.PadBottom - s - (float)it.Lift;
        if (s < 1) return;

        // sombra de contacto del ícono
        Solid(0, 0, 0, 0.20f);
        _ctx.FillEllipse(new Ellipse(new Vector2(x + s / 2, y + s * 0.97f), s * 0.42f, s * 0.06f), _solid);

        var oldT = _ctx.Transform;
        _ctx.Transform = Matrix3x2.CreateScale(s, s) * Matrix3x2.CreateTranslation(x, y) * oldT;
        DrawPlate(it.Model);
        _ctx.Transform = oldT;

        if (it.Model.Kind == DockItemKind.App) DrawAppGlyph(it, x, y, s);
        else if (it.Model.Kind == DockItemKind.Folder) DrawFolder(x, y, s);
        else if (it.Model.Kind == DockItemKind.Trash) DrawTrash(x, y, s);

        // contorno fino del squircle
        _ctx.Transform = Matrix3x2.CreateScale(s, s) * Matrix3x2.CreateTranslation(x, y) * oldT;
        Solid(1, 1, 1, 0.45f); _ctx.DrawGeometry(_squircle, _solid, 1f / s);
        _ctx.Transform = oldT;

        // indicador de app abierta
        if (it.Model.Running)
        {
            Solid(1, 1, 1, it.Model.Active ? 0.95f : 0.75f);
            _ctx.FillEllipse(new Ellipse(new Vector2(x + s / 2, barBottom - (float)_m.PadBottom + 6.5f), (float)_m.DotSize / 2, (float)_m.DotSize / 2), _solid);
        }
    }

    private void DrawPlate(DockItemModel m)
    {
        // coordenadas unitarias (la transformación ya escala a tamaño de ícono)
        Color4 top, bottom;
        if (m.Kind == DockItemKind.App) { top = new Color4(0.98f, 0.98f, 1f, 1); bottom = new Color4(0.84f, 0.86f, 0.92f, 1); }
        else if (m.Kind == DockItemKind.Folder) { top = new Color4(0.55f, 0.80f, 1f, 1); bottom = new Color4(0.20f, 0.52f, 0.95f, 1); }
        else { top = new Color4(0.90f, 0.93f, 0.97f, 1); bottom = new Color4(0.68f, 0.74f, 0.84f, 1); }
        using var g = Grad(new Vector2(0, 0), new Vector2(0, 1), (0f, top), (1f, bottom));
        _ctx.FillGeometry(_squircle, g);
    }

    private void DrawAppGlyph(ItemView it, float x, float y, float s)
    {
        var bmp = IconFor(it.Model);
        if (bmp is not null)
        {
            float pad = s * 0.14f;
            DrawBmp(bmp, new RawRectF(x + pad, y + pad, x + s - pad, y + s - pad), true);
            return;
        }
        // Reemplazo: placa de color + inicial (ícono real no disponible).
        int h = Math.Abs((it.Model.Path ?? it.Model.Title).GetHashCode());
        float hue = (h % 360) / 360f; var c = Hsv(hue, 0.55f, 0.85f);
        using (var g = Grad(new Vector2(0, y), new Vector2(0, y + s), (0f, new Color4(Math.Min(1, c.R + .15f), Math.Min(1, c.G + .15f), Math.Min(1, c.B + .15f), 1)), (1f, c)))
        {
            var old = _ctx.Transform;
            _ctx.Transform = Matrix3x2.CreateScale(s * .86f, s * .86f) * Matrix3x2.CreateTranslation(x + s * .07f, y + s * .07f) * old;
            _ctx.FillGeometry(_squircle, g);
            _ctx.Transform = old;
        }
        string glyph = it.Model.Title.Length > 0 ? it.Model.Title[..1].ToUpperInvariant() : "?";
        Solid(1, 1, 1, 1);
        using var layout = _dw.CreateTextLayout(glyph, _glyphFmt, 80, 40);
        layout.SetFontSize(s * 0.46f, new TextRange(0, (uint)glyph.Length)); layout.TextAlignment = TextAlignment.Center; layout.ParagraphAlignment = ParagraphAlignment.Center;
        layout.MaxWidth = s; layout.MaxHeight = s;
        _ctx.DrawTextLayout(new Vector2(x, y), layout, _solid);
    }

    private void DrawFolder(float x, float y, float s)
    {
        float l = x + s * .22f, r = x + s * .78f, t = y + s * .30f, b = y + s * .72f;
        Solid(1, 1, 1, 0.55f); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(l, t - s * .06f, l + s * .24f, t + s * .08f), RadiusX = s * .03f, RadiusY = s * .03f }, _solid);
        Solid(1, 1, 1, 0.92f); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(l, t, r, b), RadiusX = s * .05f, RadiusY = s * .05f }, _solid);
    }

    private void DrawTrash(float x, float y, float s)
    {
        float cx = x + s / 2, top = y + s * .30f, bottom = y + s * .72f, w = s * .26f;
        Solid(0.35f, 0.42f, 0.55f, 0.95f);
        _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - w, top + s * .07f, cx + w, bottom), RadiusX = s * .05f, RadiusY = s * .05f }, _solid);
        _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = new RawRectF(cx - w - s * .03f, top, cx + w + s * .03f, top + s * .06f), RadiusX = s * .02f, RadiusY = s * .02f }, _solid);
        Solid(1, 1, 1, 0.55f);
        for (int i = -1; i <= 1; i++) _ctx.DrawLine(new Vector2(cx + i * w * .5f, top + s * .13f), new Vector2(cx + i * w * .5f, bottom - s * .05f), _solid, Math.Max(1f, s * .02f));
    }

    private void DrawLabel(DockView v, float barBottom)
    {
        if (v.HoverIndex < 0 || v.HoverIndex >= v.Items.Length) return;
        var it = v.Items[v.HoverIndex]; if (string.IsNullOrEmpty(it.Model.Title)) return;
        using var layout = _dw.CreateTextLayout(it.Model.Title, _labelFmt, 400, 40);
        var tm = layout.Metrics; float w = tm.Width + 24, h = (float)_m.LabelHeight;
        float top = barBottom - (float)_m.PadBottom - (float)it.Size - (float)it.Lift - (float)_m.LabelGap - h;
        float cx = (float)(it.X + it.Size / 2), left = Math.Clamp(cx - w / 2, 2, (float)v.WindowWidth - w - 2);
        var pill = new RoundedRectangle { Rect = new RawRectF(left, top, left + w, top + h), RadiusX = h / 2, RadiusY = h / 2 };
        Solid(0.10f, 0.11f, 0.14f, 0.62f); _ctx.FillRoundedRectangle(pill, _solid);
        Solid(1, 1, 1, 0.28f); _ctx.DrawRoundedRectangle(pill, _solid, 1f);
        Solid(1, 1, 1, 1); _ctx.DrawTextLayout(new Vector2(left + 12, top + (h - tm.Height) / 2), layout, _solid);
    }

    // ---------------------------------------------------------------- recursos
    private ID2D1Bitmap? IconFor(DockItemModel m)
    {
        string key = m.Path ?? m.Id;
        if (_iconCache.TryGetValue(key, out var b)) return b;
        ID2D1Bitmap? bmp = null;
        try
        {
            var px = _icons.Get(m);
            if (px is not null)
                unsafe
                {
                    fixed (byte* p = px.Bgra)
                        bmp = _ctx.CreateBitmap(new Vortice.Mathematics.SizeI(px.Width, px.Height), (IntPtr)p, (uint)(px.Width * 4),
                            new BitmapProperties1(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
                }
        }
        catch (Exception) { bmp = null; }
        _iconCache[key] = bmp; return bmp;
    }

    private static Color4 Hsv(float h, float s, float v)
    {
        float r = v, g = v, b = v; int i = (int)(h * 6); float f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        switch (i % 6) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break; case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break; case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
        return new Color4(r, g, b, 1);
    }

    public void Dispose()
    {
        foreach (var b in _iconCache.Values) b?.Dispose();
        _solid.Dispose(); _grad.Dispose(); _squircle.Dispose(); _glyphFmt.Dispose(); _labelFmt.Dispose(); _dw.Dispose();
    }
}
