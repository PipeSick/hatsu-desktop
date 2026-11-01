using System.Runtime.InteropServices;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>Iconos REALES de las aplicaciones (el del ejecutable, vía el Shell) como BGRA premultiplicado, con caché por ruta. Si Windows no entrega uno, devuelve null y el panel dibuja una inicial.</summary>
internal sealed class AppIcons : IAppIcons
{
    private readonly Dictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase); private readonly object _gate = new();

    public byte[]? GetBgra(string exePath, int size)
    {
        string key = exePath + "|" + size; lock (_gate) if (_cache.TryGetValue(key, out var hit)) return hit;
        var px = Extract(exePath, size); lock (_gate) { if (_cache.Count > 128) _cache.Clear(); _cache[key] = px; }
        return px;
    }

    private static byte[]? Extract(string path, int size)
    {
        IntPtr icon = IntPtr.Zero; IntPtr hdc = IntPtr.Zero; var info = default(HubNative.ICONINFO);
        try
        {
            var fi = new HubNative.SHFILEINFO { szDisplayName = "", szTypeName = "" };
            if (HubNative.SHGetFileInfoW(path, 0, ref fi, (uint)Marshal.SizeOf<HubNative.SHFILEINFO>(), 0x100 /* SHGFI_ICON */) == IntPtr.Zero || fi.hIcon == IntPtr.Zero) return null; icon = fi.hIcon;
            if (!HubNative.GetIconInfo(icon, out info) || info.hbmColor == IntPtr.Zero) return null;
            if (HubNative.GetObjectW(info.hbmColor, Marshal.SizeOf<HubNative.BITMAP>(), out var bmp) == 0 || bmp.bmWidth <= 0 || bmp.bmHeight <= 0) return null;
            int w = bmp.bmWidth, h = bmp.bmHeight; hdc = HubNative.GetDC(IntPtr.Zero);
            var bih = new HubNative.BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<HubNative.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h /* de arriba abajo */, biPlanes = 1, biBitCount = 32 };
            var color = new byte[w * h * 4]; if (HubNative.GetDIBits(hdc, info.hbmColor, 0, (uint)h, color, ref bih, 0) == 0) return null;
            bool hasAlpha = false; for (int i = 3; i < color.Length; i += 4) if (color[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha && info.hbmMask != IntPtr.Zero)   // iconos antiguos sin canal alfa: la transparencia viene de la máscara
            {
                var mask = new byte[w * h * 4]; var mh = bih; if (HubNative.GetDIBits(hdc, info.hbmMask, 0, (uint)h, mask, ref mh, 0) != 0)
                    for (int i = 0; i < w * h; i++) color[i * 4 + 3] = (byte)(mask[i * 4] == 0 ? 255 : 0);
                else for (int i = 3; i < color.Length; i += 4) color[i] = 255;
            }
            else if (!hasAlpha) for (int i = 3; i < color.Length; i += 4) color[i] = 255;
            for (int i = 0; i < w * h; i++)   // premultiplicar: Direct2D espera alfa premultiplicado
            {
                int a = color[i * 4 + 3]; if (a == 255) continue;
                color[i * 4] = (byte)(color[i * 4] * a / 255); color[i * 4 + 1] = (byte)(color[i * 4 + 1] * a / 255); color[i * 4 + 2] = (byte)(color[i * 4 + 2] * a / 255);
            }

            return Resize(color, w, h, size);
        }
        catch (Exception) { return null; }
        finally
        {
            if (hdc != IntPtr.Zero) HubNative.ReleaseDC(IntPtr.Zero, hdc);
            if (info.hbmColor != IntPtr.Zero) HubNative.DeleteObject(info.hbmColor); if (info.hbmMask != IntPtr.Zero) HubNative.DeleteObject(info.hbmMask);
            if (icon != IntPtr.Zero) HubNative.DestroyIcon(icon);
        }
    }

    /// <summary>Remuestreo del vecino más cercano al lado pedido (los iconos del Shell son 32×32; así el panel recibe siempre el mismo tamaño).</summary>
    internal static byte[] Resize(byte[] src, int w, int h, int size)
    {
        if (w == size && h == size) return src; var dst = new byte[size * size * 4];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) Buffer.BlockCopy(src, ((y * h / size) * w + (x * w / size)) * 4, dst, (y * size + x) * 4, 4);
        return dst;
    }
}
