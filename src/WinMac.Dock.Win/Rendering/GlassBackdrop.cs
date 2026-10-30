using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DCommon;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinMac.Dock.Imaging;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// Material «vidrio» de nivel WallpaperBlur: recorta del fondo la región que queda tras la ventana del dock y la desenfoca/satura UNA vez.
/// El resultado se cachea y el painter solo lo recorta con la forma de la barra: coste por frame ≈ 0 (sin captura continua).
/// Usa solo efectos documentados de Direct2D (GaussianBlur, Saturation). Se recalcula si cambia el fondo, el monitor o el DPI.
/// </summary>
public static class GlassBackdrop
{
    public static ID2D1Bitmap1 CreateTarget(ID2D1DeviceContext ctx, int pw, int ph, double scale, bool readable = false)
    {
        var opts = readable ? BitmapOptions.CannotDraw | BitmapOptions.CpuRead : BitmapOptions.Target;
        return ctx.CreateBitmap(new SizeI(pw, ph), IntPtr.Zero, 0,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), (float)(96 * scale), (float)(96 * scale), opts));
    }

    /// <param name="raw">Imagen de fondo completa (píxeles).</param>
    /// <param name="src">Región de <paramref name="raw"/> (px de imagen) que queda tras la ventana del dock.</param>
    public static ID2D1Bitmap1 Build(D2DDevice dev, ID2D1Bitmap raw, RectD src, double winW, double winH, double scale, GlassStyle g)
    {
        var ctx = dev.Context; int pw = (int)Math.Ceiling(winW * scale), ph = (int)Math.Ceiling(winH * scale);
        var previous = ctx.Target;
        var cropped = CreateTarget(ctx, pw, ph, scale);
        var result = CreateTarget(ctx, pw, ph, scale);
        try
        {
            ctx.Target = cropped; ctx.BeginDraw(); ctx.Clear(new Color4(0, 0, 0, 1));
            ctx.DrawBitmap(raw, (RawRectF?)new RawRectF(0, 0, (float)winW, (float)winH), 1f, BitmapInterpolationMode.Linear,
                (RawRectF?)new RawRectF((float)src.X, (float)src.Y, (float)(src.X + src.W), (float)(src.Y + src.H)));
            ctx.EndDraw();

            using var blur = new GaussianBlur(ctx) { StandardDeviation = (float)g.BlurSigma, BorderMode = BorderMode.Hard };
            blur.SetInput(0, cropped, true);
            using var sat = new Saturation(ctx) { Value = (float)g.Saturation };
            sat.SetInputEffect(0, blur, true);

            ctx.Target = result; ctx.BeginDraw(); ctx.Clear(new Color4(0, 0, 0, 0));
            ctx.DrawImage(sat, Vortice.Direct2D1.InterpolationMode.Linear, CompositeMode.SourceOver);
            ctx.EndDraw();
            return result;
        }
        catch { result.Dispose(); throw; }
        finally { ctx.Target = previous; cropped.Dispose(); }
    }
}
