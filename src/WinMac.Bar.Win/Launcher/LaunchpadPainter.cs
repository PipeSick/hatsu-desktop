using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice;
using WinMac.Apps;
using WinMac.Bar.Launcher;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Bar.Win.Launcher;

/// <summary>
/// Dibuja Launchpad: fondo de pantalla desenfocado y oscurecido (como en macOS), campo de búsqueda, cuadrícula de iconos REALES con su nombre, resalte de la casilla,
/// icono arrastrado, puntos de página y menú contextual. Sin icono todavía: mosaico de color con la inicial (el icono llega en cuanto el Shell lo entrega).
/// </summary>
internal sealed class LaunchpadPainter : IDisposable
{
    private readonly ID2D1DeviceContext _ctx; private readonly IDWriteFactory _dw;
    private readonly IDWriteTextFormat _label, _search, _menu, _initial;
    private readonly ID2D1SolidColorBrush _solid;
    private readonly Dictionary<string, (IconBitmap Src, ID2D1Bitmap? Bmp)> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    public ID2D1Bitmap? Backdrop { get; set; }
    public Func<InstalledApp, IconBitmap?>? Icons { get; set; }

    public LaunchpadPainter(D2DDevice dev)
    {
        _ctx = dev.Context; _solid = _ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _dw = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _label = _dw.CreateTextFormat("Segoe UI", 12.5f); _label.TextAlignment = TextAlignment.Center; _label.WordWrapping = WordWrapping.NoWrap;
        _search = _dw.CreateTextFormat("Segoe UI", 14); _search.ParagraphAlignment = ParagraphAlignment.Center;
        _menu = _dw.CreateTextFormat("Segoe UI", 13); _menu.ParagraphAlignment = ParagraphAlignment.Center;
        _initial = _dw.CreateTextFormat("Segoe UI Semibold", 30); _initial.TextAlignment = TextAlignment.Center; _initial.ParagraphAlignment = ParagraphAlignment.Center;
    }

    private void Brush(Color4 c) => _solid.Color = c;
    private static RawRectF R(double x, double y, double w, double h) => new((float)x, (float)y, (float)(x + w), (float)(y + h));
    private void FillRR(LpRect r, float rad, Color4 c) { Brush(c); _ctx.FillRoundedRectangle(new RoundedRectangle { Rect = R(r.X, r.Y, r.W, r.H), RadiusX = rad, RadiusY = rad }, _solid); }

    public void Paint(LaunchpadController c, float opacity)
    {
        var g = c.Geometry;
        _ctx.BeginDraw(); _ctx.Clear(new Color4(0, 0, 0, 0));
        // WM-018: the desktop is only dimmed; the blurred wallpaper (glass) is drawn inside the compact panel, with a hairline border, like the Golden Gate Launchpad mock-up
        Brush(new Color4(0, 0, 0, 0.28f * opacity)); _ctx.FillRectangle(R(0, 0, g.Width, g.Height), _solid);
        var pn = g.Panel; var prr = new RoundedRectangle { Rect = R(pn.X, pn.Y, pn.W, pn.H), RadiusX = 22, RadiusY = 22 };
        if (Backdrop is not null)
        {
            using var geo = _ctx.Factory.CreateRoundedRectangleGeometry(prr);
            _ctx.PushLayer(new LayerParameters1 { ContentBounds = R(pn.X, pn.Y, pn.W, pn.H), GeometricMask = geo, MaskAntialiasMode = AntialiasMode.PerPrimitive, MaskTransform = System.Numerics.Matrix3x2.Identity, Opacity = 1 }, null!);
            _ctx.DrawBitmap(Backdrop, R(0, 0, g.Width, g.Height), opacity, BitmapInterpolationMode.Linear, null);
            _ctx.PopLayer();
        }

        Brush(new Color4(0.08f, 0.08f, 0.12f, (Backdrop is null ? 0.92f : 0.42f) * opacity)); _ctx.FillRoundedRectangle(prr, _solid);
        Brush(new Color4(1, 1, 1, 0.16f * opacity)); _ctx.DrawRoundedRectangle(prr, _solid, 1);

        // Campo de búsqueda
        var s = g.Search; FillRR(s, (float)(s.H / 2), new Color4(1, 1, 1, 0.16f * opacity));
        Brush(new Color4(1, 1, 1, 0.22f * opacity)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(s.X, s.Y, s.W, s.H), RadiusX = (float)(s.H / 2), RadiusY = (float)(s.H / 2) }, _solid, 1);
        DrawMagnifier(s.X + 16, s.Cy, opacity);
        string text = c.Searching ? c.Query : "Buscar aplicaciones…"; Brush(new Color4(1, 1, 1, (c.Searching ? 0.95f : 0.55f) * opacity));
        using (var l = _dw.CreateTextLayout(text, _search, (float)(s.W - 44), (float)s.H)) _ctx.DrawTextLayout(new Vector2((float)(s.X + 32), (float)s.Y), l, _solid);
        if (c.Searching && c.PageItems.Count == 0) { Brush(new Color4(1, 1, 1, 0.6f * opacity)); using var l = _dw.CreateTextLayout("Sin resultados", _search, (float)g.Width, 40); l.TextAlignment = TextAlignment.Center; _ctx.DrawTextLayout(new Vector2(0, (float)(g.Panel.Y + g.Panel.H / 2 - 20)), l, _solid); }

        // Cuadrícula
        var items = c.PageItems;
        for (int i = 0; i < items.Count && i < g.Cells.Count; i++)
        {
            var app = items[i]; var cell = g.Cells[i]; var ir = g.IconRect(cell);
            bool dragged = c.Drag is { } d && string.Equals(d.App.Id, app.Id, StringComparison.OrdinalIgnoreCase);
            if (i == c.Selected || (i == c.Hover && c.Drag is null) || (c.Drag is not null && i == c.Hover && !dragged))
                FillRR(new LpRect(ir.X - 10, ir.Y - 8, ir.W + 20, ir.H + 38), 14, new Color4(1, 1, 1, (i == c.Selected ? 0.20f : 0.10f) * opacity));
            DrawIcon(app, ir, (dragged ? 0.3f : 1f) * opacity);
            Brush(new Color4(1, 1, 1, 0.95f * opacity * (dragged ? 0.3f : 1f)));
            using var l = _dw.CreateTextLayout(app.Name, _label, (float)(cell.W - 8), 18);
            using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ell);
            _ctx.DrawTextLayout(new Vector2((float)(cell.X + 4), (float)(ir.Y + ir.H + 8)), l, _solid);
        }

        if (c.Drag is { } dr) DrawIcon(dr.App, new LpRect(dr.X - g.Icon / 2, dr.Y - g.Icon / 2, g.Icon, g.Icon), 0.92f * opacity);

        // Puntos de página
        int pages = c.PageCount;
        if (pages > 1)
            for (int p = 0; p < pages; p++) { var r = g.Dot(p, pages); Brush(new Color4(1, 1, 1, (p == c.Page ? 0.95f : 0.35f) * opacity)); _ctx.FillEllipse(new Ellipse(new Vector2((float)r.Cx, (float)r.Cy), 4, 4), _solid); }

        // Menú contextual
        if (c.Menu is { } m)
        {
            FillRR(new LpRect(m.Rect.X, m.Rect.Y + 2, m.Rect.W, m.Rect.H), 10, new Color4(0, 0, 0, 0.25f));
            FillRR(m.Rect, 10, new Color4(0.16f, 0.16f, 0.18f, 0.97f));
            for (int i = 0; i < m.Items.Count; i++)
            {
                var r = m.ItemRect(i); if (i == c.MenuHover) FillRR(r, 6, new Color4(0.04f, 0.52f, 1f, 1f));
                Brush(new Color4(1, 1, 1, 0.95f)); using var l = _dw.CreateTextLayout(m.Items[i].Text, _menu, (float)(r.W - 20), (float)r.H); _ctx.DrawTextLayout(new Vector2((float)(r.X + 10), (float)r.Y), l, _solid);
            }
        }

        _ctx.EndDraw();
    }

    private void DrawMagnifier(double x, double cy, float a)
    {
        Brush(new Color4(1, 1, 1, 0.7f * a));
        _ctx.DrawEllipse(new Ellipse(new Vector2((float)x, (float)cy - 1), 5, 5), _solid, 1.6f);
        _ctx.DrawLine(new Vector2((float)x + 3.6f, (float)cy + 2.6f), new Vector2((float)x + 7.5f, (float)cy + 6.5f), _solid, 1.8f);
    }

    private void DrawIcon(InstalledApp app, LpRect r, float a)
    {
        var px = Icons?.Invoke(app);
        if (px is not null && BitmapFor(app.Id, px) is { } bmp)
        {
            var (x, y, w, h) = IconLayout.AspectFit(px.Width, px.Height, r.X, r.Y, r.W, r.H);
            _ctx.DrawBitmap(bmp, R(x, y, w, h), a, BitmapInterpolationMode.Linear, null); return;
        }

        double hue = FallbackIcon.Hue(app.Id) * 360; var (cr, cg, cb) = Hsv(hue, 0.55, 0.85);
        FillRR(r, (float)(r.W * 0.22), new Color4((float)cr, (float)cg, (float)cb, a));
        Brush(new Color4(1, 1, 1, a)); using var l = _dw.CreateTextLayout(FallbackIcon.Initial(app.Name), _initial, (float)r.W, (float)r.H); _ctx.DrawTextLayout(new Vector2((float)r.X, (float)r.Y), l, _solid);
    }

    private ID2D1Bitmap? BitmapFor(string id, IconBitmap px)
    {
        if (_bitmaps.TryGetValue(id, out var e) && ReferenceEquals(e.Src, px)) return e.Bmp;
        e.Bmp?.Dispose();
        ID2D1Bitmap? bmp = null;
        try
        {
            unsafe
            {
                fixed (byte* p = px.Bgra)
                    bmp = _ctx.CreateBitmap(new SizeI(px.Width, px.Height), (IntPtr)p, (uint)(px.Width * 4),
                        new BitmapProperties1(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96));
            }
        }
        catch (Exception) { bmp = null; }
        _bitmaps[id] = (px, bmp); return bmp;
    }

    private static (double R, double G, double B) Hsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360 / 60; int i = (int)h; double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        return i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
    }

    public void Dispose()
    {
        foreach (var e in _bitmaps.Values) e.Bmp?.Dispose(); _bitmaps.Clear();
        _label.Dispose(); _search.Dispose(); _menu.Dispose(); _initial.Dispose(); _solid.Dispose(); _dw.Dispose();
    }
}
