using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Dibujo del Centro de Notificaciones: encabezado con día y fecha, secciones con su acción, cabeceras de grupo y tarjetas (icono real de la app o su inicial, app, hora, título, texto,
/// «×» y tarjetas apiladas del grupo plegado). Todo vectorial y en DIP; sin sombras ni desenfoques por tarjeta (el único desenfoque es el del fondo, hecho una vez al abrir).
/// </summary>
public sealed partial class PanelPainter
{
    private IDWriteTextFormat? _ncDay, _ncCardBody;

    private void EnsureNcFonts()
    {
        EnsureFonts();
        _ncDay ??= _dw.CreateTextFormat("Segoe UI Semibold", 22);
        _ncCardBody ??= _dw.CreateTextFormat("Segoe UI", 12);
    }

    private void DisposeNc() { _ncDay?.Dispose(); _ncDay = null; _ncCardBody?.Dispose(); _ncCardBody = null; }

    private void DrawNcWidget(WidgetFrame f, bool hover)
    {
        EnsureNcFonts(); var w = f.W; double x = f.X, y = f.Y, wd = f.Width, h = f.Height;
        switch (w.Kind)
        {
            case WidgetKind.NcHeader:
            {
                TextAlign(w.Text, _ncDay!, x + 4, y + 2, wd - 50, 30, Fg());
                TextAlign(w.Sub ?? "", _body, x + 4, y + 32, wd - 50, 20, Fg(0.62f));
                double cx = x + wd - 16, cy = y + 16; FillCircle(cx, cy, 12, Fill(hover ? 0.22f : 0.12f)); CloseGlyph(cx, cy, 4.2f, Fg(0.85f));
                break;
            }
            case WidgetKind.NcSection:
            {
                TextAlign(w.Text, _bodyBold, x + 4, y, wd - 110, h, Fg(0.9f));
                if (w.Data is NcSectionData { ActionLabel: { } label })
                {
                    double tw = MeasureText(label, _small) + 18, px = x + wd - tw;
                    FillRR(px, y + 3, x + wd, y + h - 3, (float)((h - 6) / 2), Fill(hover ? 0.2f : 0.11f)); TextAlign(label, _small, px, y, tw, h, Fg(0.85f), TextAlignment.Center);
                }
                break;
            }
            case WidgetKind.NotifGroup:
            {
                if (w.Data is not NotifGroupData g) break;
                if (hover) FillRR(x, y, x + wd, y + h, 8, Fill(0.07f));
                AppIcon(g.IconKey, g.AppName, x + 4, y + (h - 18) / 2, 18);
                double nameW = Math.Min(MeasureText(g.AppName, _bodyBold) + 4, wd - 150);
                TextAlign(g.AppName, _bodyBold, x + 28, y, nameW, h, Fg());
                TextAlign("· " + g.Count, _small, x + 30 + nameW, y, 40, h, Fg(0.55f));
                double chx = x + 30 + nameW + 34; if (g.Expanded) { Line(chx - 3.5, y + h / 2 - 2, chx, y + h / 2 + 2, Fg(0.55f), 1.5f); Line(chx, y + h / 2 + 2, chx + 3.5, y + h / 2 - 2, Fg(0.55f), 1.5f); } else Glyph("chevron", chx, y + h / 2, 4.5f, Fg(0.55f));
                if (g.ClearLabel is { } cl) { double tw = MeasureText(cl, _small) + 18, px = x + wd - tw; FillRR(px, y + 4, x + wd, y + h - 4, (float)((h - 8) / 2), Fill(0.11f)); TextAlign(cl, _small, px, y, tw, h, Fg(0.85f), TextAlignment.Center); }
                break;
            }
            case WidgetKind.NotifCard: DrawNotifCard(f, hover); break;
            case WidgetKind.NcEmpty:
            {
                double cx = x + wd / 2; FillCircle(cx, y + 30, 22, Fill(0.08f)); Glyph(w.Glyph ?? "bell", cx, y + 30, 10, Fg(0.6f));
                TextAlign(w.Text, _bodyBold, x, y + 58, wd, 20, Fg(0.9f), TextAlignment.Center);
                if (!string.IsNullOrEmpty(w.Sub)) TextAlign(w.Sub!, _small, x + 10, y + 80, wd - 20, h - 82, Fg(0.6f), TextAlignment.Center, vcenter: false, wrap: true);
                break;
            }
        }
    }

    private void DrawNotifCard(WidgetFrame f, bool hover)
    {
        if (f.W.Data is not NotifCardData d) return;
        double x = f.X, y = f.Y, wd = f.Width, stack = d.Stacked > 0 ? 8 : 0, h = f.Height - stack;
        if (d.Stacked > 0)   // grupo plegado: los bordes de las tarjetas de debajo
        {
            FillRR(x + 16, y + h - 6, x + wd - 16, y + h + 8, 12, Fill(0.05f));
            FillRR(x + 8, y + h - 8, x + wd - 8, y + h + 4, 13, Fill(0.08f));
        }

        FillRR(x, y, x + wd, y + h, 14, Fill(hover ? 0.15f : 0.11f));
        Brush(Fill(0.08f)); _ctx.DrawRoundedRectangle(new RoundedRectangle { Rect = R(x + 0.5, y + 0.5, x + wd - 0.5, y + h - 0.5), RadiusX = 14, RadiusY = 14 }, _solid, 1f);
        double tx = x + 46, tw = wd - 46 - 12, ty = y + 10;
        AppIcon(d.IconKey, d.AppName, x + 12, y + 12, 24);
        if (d.ShowApp) { TextAlign(d.AppName, _boldTiny, tx, ty, tw - 70, 16, Fg(0.6f)); ty += 18; }
        TextAlign(d.When, _small, x + wd - 12 - 64 - (d.CanDismiss ? 18 : 0), y + 10, 64, 16, Fg(0.5f), TextAlignment.Trailing);
        TextAlign(d.Primary, _bodyBold, tx, ty, tw - (d.ShowApp ? 0 : 70), 19, Fg());
        ty += 19;
        if (d.Secondary is { } body)
        {
            double maxH = Math.Max(16, y + h - 8 - ty);
            using var l = _dw.CreateTextLayout(body, _ncCardBody!, (float)Math.Max(1, tw), (float)maxH);
            l.WordWrapping = WordWrapping.Wrap; using var ell = _dw.CreateEllipsisTrimmingSign(l); l.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Word }, ell);
            Brush(Fg(0.78f)); _ctx.DrawTextLayout(new Vector2((float)tx, (float)ty), l, _solid);
        }

        if (d.Stacked > 0) TextAlign($"+{d.Stacked}", _boldTiny, x + wd - 46, y + h - 22, 34, 14, Fg(0.5f), TextAlignment.Trailing);
        if (d.CanDismiss) { double cx = x + wd - 15, cy = y + 17; FillCircle(cx, cy, 9, Fill(hover ? 0.2f : 0.09f)); CloseGlyph(cx, cy, 3.2f, Fg(hover ? 0.9f : 0.5f)); }   // visible siempre (también sin ratón), más marcada al pasar por encima
    }

    /// <summary>Icono real de la app (el que entregó Windows) o, si no hay, un cuadrado con su inicial y un color estable por app.</summary>
    private void AppIcon(string? key, string name, double x, double y, double size)
    {
        var bmp = IconFor(key);
        if (bmp is not null) { _ctx.DrawBitmap(bmp, R(x, y, x + size, y + size), 1f, BitmapInterpolationMode.Linear, null); return; }
        uint hash = 2166136261; foreach (char ch in name) hash = (hash ^ ch) * 16777619;
        Color4[] palette = [new(0.04f, 0.52f, 1f, 1), new(0.19f, 0.70f, 0.35f, 1), new(1f, 0.58f, 0f, 1), new(0.69f, 0.32f, 0.87f, 1), new(1f, 0.27f, 0.36f, 1), new(0.35f, 0.78f, 0.98f, 1)];
        FillRR(x, y, x + size, y + size, (float)(size * 0.24), palette[hash % (uint)palette.Length]);
        string initial = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();
        TextAlign(initial, size > 20 ? _bodyBold : _boldTiny, x, y, size, size, new Color4(1, 1, 1, 1), TextAlignment.Center);
    }

    private void CloseGlyph(double cx, double cy, float s, Color4 c) { Line(cx - s, cy - s, cx + s, cy + s, c, 1.5f); Line(cx + s, cy - s, cx - s, cy + s, c, 1.5f); }
}
