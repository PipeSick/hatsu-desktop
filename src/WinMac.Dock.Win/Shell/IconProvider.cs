using System.Runtime.InteropServices;
using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>Píxeles BGRA con alfa PREMULTIPLICADO, filas de arriba abajo.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>Obtiene el ícono real de un ejecutable con IShellItemImageFactory (API documentada del Shell). Devuelve null si falla; el painter usa un ícono de reemplazo.</summary>
public static class IconProvider
{
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap); }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPels, biYPels; public uint biClrUsed, biClrImportant; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] private static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int cb, out BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);

    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;

    public static IconPixels? Load(string path, int size = 128)
    {
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(Environment.ExpandEnvironmentVariables(path), IntPtr.Zero, ref iid, out var f) != 0) return null;
            try
            {
                if (f.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY, out IntPtr hbm) != 0 || hbm == IntPtr.Zero) return null;
                try { return ToPixels(hbm); } finally { DeleteObject(hbm); }
            }
            finally { Marshal.ReleaseComObject(f); }
        }
        catch (Exception) { return null; }
    }

    private static IconPixels? ToPixels(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bmp) == 0 || bmp.bmWidth <= 0 || bmp.bmHeight <= 0) return null;
        int w = bmp.bmWidth, h = bmp.bmHeight;
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var px = new byte[w * h * 4]; IntPtr dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0) == 0) return null; } finally { ReleaseDC(IntPtr.Zero, dc); }
        bool anyAlpha = false; for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
        for (int i = 0; i < px.Length; i += 4)
        {
            if (!anyAlpha) { px[i + 3] = 255; continue; }
            byte a = px[i + 3]; px[i] = (byte)(px[i] * a / 255); px[i + 1] = (byte)(px[i + 1] * a / 255); px[i + 2] = (byte)(px[i + 2] * a / 255);
        }
        return new IconPixels(w, h, px);
    }
}
