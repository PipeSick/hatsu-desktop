using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using WinMac.Dock;

namespace WinMac.Dock.Win.Rendering;

/// <summary>Fondo procedural con zonas claras/oscuras y colores saturados, para ver el efecto del vidrio en renders de CI. No es un fondo real de escritorio.</summary>
public static class SyntheticBackdrop
{
    /// <param name="wDip">Ancho de la ventana en DIP.</param>
    /// <param name="hDip">Alto de la ventana en DIP.</param>
    /// <param name="scale">Escala (DPI/96). El bitmap tiene wDip*scale píxeles y se dibuja en DIP con el DPI actual del contexto.</param>
    public static ID2D1Bitmap1 Create(D2DDevice dev, double wDip, double hDip, double scale, DockTheme theme = DockTheme.Light)
    {
        bool dark = theme == DockTheme.Dark; float pw = (float)wDip, ph = (float)hDip;  // coordenadas en DIP
        var ctx = dev.Context; var prev = ctx.Target;
        var bmp = GlassBackdrop.CreateTarget(ctx, (int)Math.Ceiling(wDip * scale), (int)Math.Ceiling(hDip * scale), scale);
        try
        {
            ctx.Target = bmp; ctx.BeginDraw();
            using (var sc = ctx.CreateGradientStopCollection([new GradientStop { Position = 0, Color = dark ? new Color4(0.04f, 0.07f, 0.20f, 1) : new Color4(0.45f, 0.70f, 0.98f, 1) }, new GradientStop { Position = 0.55f, Color = dark ? new Color4(0.20f, 0.08f, 0.32f, 1) : new Color4(0.85f, 0.72f, 0.95f, 1) }, new GradientStop { Position = 1, Color = dark ? new Color4(0.35f, 0.14f, 0.18f, 1) : new Color4(1.0f, 0.82f, 0.62f, 1) }], Gamma.StandardRgb, ExtendMode.Clamp))
            using (var br = ctx.CreateLinearGradientBrush(new LinearGradientBrushProperties { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(pw, ph) }, sc))
                ctx.FillRectangle(new RawRectF(0, 0, pw, ph), br);
            using var solid = ctx.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            var rnd = new Random(7);
            for (int i = 0; i < 26; i++)
            {
                solid.Color = new Color4((float)rnd.NextDouble() * (dark ? 0.6f : 1f), (float)rnd.NextDouble() * (dark ? 0.6f : 1f), (float)rnd.NextDouble() * (dark ? 0.8f : 1f), 0.55f);
                float r = 20 + (float)rnd.NextDouble() * 90;
                ctx.FillEllipse(new Ellipse(new Vector2((float)(rnd.NextDouble() * pw), (float)(rnd.NextDouble() * ph)), r, r), solid);
            }
            solid.Color = new Color4(1, 1, 1, dark ? 0.55f : 0.9f); ctx.FillRectangle(new RawRectF(pw * 0.62f, 0, pw * 0.74f, ph), solid); // franja clara (comprueba contraste del borde)
            solid.Color = new Color4(0, 0, 0, 0.7f); ctx.FillRectangle(new RawRectF(pw * 0.10f, 0, pw * 0.20f, ph), solid);  // franja oscura
            ctx.EndDraw();
            return bmp;
        }
        finally { ctx.Target = prev; }
    }
}
