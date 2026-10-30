using System.Text;
using Microsoft.Win32;
using Vortice.Direct2D1;
using Vortice.WIC;
using WinMac.Dock.Imaging;
using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Rendering;

/// <summary>Lee el fondo de pantalla actual (SystemParametersInfo SPI_GETDESKWALLPAPER + estilo de Control Panel\Desktop) y lo carga como bitmap de D2D con WIC. Devuelve null si no hay imagen utilizable (color sólido, presentación, error).</summary>
public sealed class WallpaperSource : IDisposable
{
    public ID2D1Bitmap? Bitmap { get; private set; }
    public WallpaperStyle Style { get; private set; } = WallpaperStyle.Fill;
    public string? Path { get; private set; }
    public double Width => Bitmap?.PixelSize.Width ?? 0;
    public double Height => Bitmap?.PixelSize.Height ?? 0;

    public static string? CurrentPath()
    {
        var sb = new StringBuilder(520);
        return Native.SystemParametersInfoStr(0x73 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0) && sb.Length > 0 && File.Exists(sb.ToString()) ? sb.ToString() : null;
    }

    public bool Load(ID2D1DeviceContext ctx)
    {
        Bitmap?.Dispose(); Bitmap = null;
        try
        {
            Path = CurrentPath(); if (Path is null) return false;
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            Style = WallpaperMapping.FromRegistry(k?.GetValue("WallpaperStyle") as string, k?.GetValue("TileWallpaper") as string);
            using var wic = new IWICImagingFactory();
            using var dec = wic.CreateDecoderFromFileName(Path, FileAccess.Read, DecodeOptions.CacheOnDemand);
            using var frame = dec.GetFrame(0);
            using var conv = wic.CreateFormatConverter();
            conv.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapDitherType.None, null!, 0, BitmapPaletteType.MedianCut);
            Bitmap = ctx.CreateBitmapFromWicBitmap(conv, null);
            return true;
        }
        catch (Exception) { Bitmap = null; return false; }
    }

    public void Dispose() { Bitmap?.Dispose(); Bitmap = null; }
}
