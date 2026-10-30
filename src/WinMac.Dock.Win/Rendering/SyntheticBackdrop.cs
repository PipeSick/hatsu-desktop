using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace WinMac.Dock.Win.Rendering;

/// <summary>Fondo procedural con zonas claras/oscuras y colores saturados, para ver el efecto del vidrio en renders de CI. No es un fondo real de escritorio.</summary>
public static class SyntheticBackdrop
{
    public static ID2D1Bitmap1 Create(D2DDevice dev, int pw, int ph)
    {
        var ctx = dev.Context; var prev = ctx.Target;
        var bmp = GlassBackdrop.CreateTarget(ctx, pw, ph, 1.0);
        try
        {
            ctx.Target = bmp; ctx.BeginDraw();
            using (var sc = ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = new Color4(0.16f, 0.32f, 0.78f, 1) }, new GradientStop { Position = 0.55f, Color = new Color4(0.55f, 0.25f, 0.70f, 1) }, new GradientStop { Position = 1, Color = new Color4(0.95f, 0.55f, 0.35f, 1) }], Gamma.StandardRgb, ExtendMode.Clamp))
            using (var br = ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(pw, ph) }, sc))
                ctx.FillRectangle(new RawRectF(0, 0, pw, ph), br);
            using var solid = ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            var rnd = new Random(7);
            for (int i = 0; i < 26; i++)
            {
                solid.Color = new Color4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), 0.55f);
                float r = 20 + (float)rnd.NextDouble() * 90;
                ctx.FillEllipse(new Ellipse(new Vector2((float)(rnd.NextDouble() * pw), (float)(rnd.NextDouble() * ph)), r, r), solid);
            }
            solid.Color = new Color4(1, 1, 1, 0.9f); ctx.FillRectangle(new RawRectF(pw * 0.62f, 0, pw * 0.74f, ph), solid); // franja clara (comprueba contraste del borde)
            solid.Color = new Color4(0, 0, 0, 0.7f); ctx.FillRectangle(new RawRectF(pw * 0.10f, 0, pw * 0.20f, ph), solid);  // franja oscura
            ctx.EndDraw();
            return bmp;
        }
        finally { ctx.Target = prev; }
    }
}
