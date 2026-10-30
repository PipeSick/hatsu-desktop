namespace WinMac.Dock.Imaging;

public enum WallpaperStyle { Fill, Fit, Stretch, Tile, Center, Span }

public readonly record struct RectD(double X, double Y, double W, double H);

/// <summary>
/// Mapea el estilo de fondo de Windows (Control Panel\Desktop: WallpaperStyle/TileWallpaper) y calcula qué parte de la imagen queda
/// detrás de una región de pantalla. Es lo que permite el vidrio «WallpaperBlur» SIN captura continua.
/// </summary>
public static class WallpaperMapping
{
    public static WallpaperStyle FromRegistry(string? wallpaperStyle, string? tileWallpaper)
    {
        if (tileWallpaper == "1") return WallpaperStyle.Tile;
        return wallpaperStyle switch { "0" => WallpaperStyle.Center, "2" => WallpaperStyle.Stretch, "6" => WallpaperStyle.Fit, "10" => WallpaperStyle.Fill, "22" => WallpaperStyle.Span, _ => WallpaperStyle.Fill };
    }

    /// <summary>Rectángulo (en píxeles de la imagen) correspondiente a <paramref name="screenRegion"/> (píxeles de pantalla del monitor, origen 0,0).</summary>
    public static RectD SourceRectFor(WallpaperStyle style, double imgW, double imgH, double monW, double monH, RectD screenRegion)
    {
        switch (style)
        {
            case WallpaperStyle.Stretch: case WallpaperStyle.Span:
                return new RectD(screenRegion.X / monW * imgW, screenRegion.Y / monH * imgH, screenRegion.W / monW * imgW, screenRegion.H / monH * imgH);
            case WallpaperStyle.Fit:
            {
                double s = Math.Min(monW / imgW, monH / imgH), ox = (monW - imgW * s) / 2, oy = (monH - imgH * s) / 2;
                return new RectD((screenRegion.X - ox) / s, (screenRegion.Y - oy) / s, screenRegion.W / s, screenRegion.H / s);
            }
            case WallpaperStyle.Center: case WallpaperStyle.Tile:
            {
                double ox = (monW - imgW) / 2, oy = (monH - imgH) / 2;
                return new RectD(screenRegion.X - ox, screenRegion.Y - oy, screenRegion.W, screenRegion.H);
            }
            default: // Fill: cubrir el monitor recortando
            {
                double s = Math.Max(monW / imgW, monH / imgH), ox = (monW - imgW * s) / 2, oy = (monH - imgH * s) / 2;
                return new RectD((screenRegion.X - ox) / s, (screenRegion.Y - oy) / s, screenRegion.W / s, screenRegion.H / s);
            }
        }
    }
}
