using Vortice.Direct2D1;

namespace WinMac.Dock.Win.Rendering;

/// <summary>
/// Hatsu Desktop: el icono Flux como mapa de bits para las ventanas dibujadas con Direct2D (barra superior, panel principal). Los píxeles vienen de assets/brand/embedded
/// (generados desde la misma fuente que el .ico de los ejecutables). Si el recurso faltara, devuelve null y el llamador dibuja su alternativa de texto.
/// </summary>
public static class BrandIcon
{
    public static readonly int[] Sizes = [32, 128];

    /// <summary>Tamaño incrustado más cercano por encima de lo que se va a dibujar (en píxeles de dispositivo).</summary>
    public static int PickSize(double devicePixels) => devicePixels <= 32 ? 32 : 128;

    public static byte[]? Bgra(int size)
    {
        try
        {
            using var s = typeof(BrandIcon).Assembly.GetManifestResourceStream($"brand.hatsu-flux-{size}.bgra"); if (s is null) return null;
            var b = new byte[s.Length]; int n = 0; while (n < b.Length) { int r = s.Read(b, n, b.Length - n); if (r <= 0) return null; n += r; }
            return b.Length == size * size * 4 ? b : null;
        }
        catch (Exception) { return null; }
    }

    public static unsafe ID2D1Bitmap? Create(ID2D1DeviceContext ctx, int size)
    {
        var px = Bgra(size); if (px is null) return null;
        try { fixed (byte* p = px) return ctx.CreateBitmap(new Vortice.Mathematics.SizeI(size, size), (IntPtr)p, (uint)(size * 4), new BitmapProperties1(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96)); }
        catch (Exception) { return null; }
    }
}
