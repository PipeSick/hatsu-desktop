using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Apps.Win;

/// <summary>P/Invoke y COM mínimos, solo APIs DOCUMENTADAS del Shell (IShellItemImageFactory, IShellItem, IPropertyStore) y kernel32 (identidad de paquete).</summary>
internal static class ShellNative
{
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap); }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint mask, out uint attrs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int order);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint c);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
        [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        [PreserveSig] int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] internal struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] internal struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr p; public IntPtr p2; }   // 16 bytes en x86, 24 en x64
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPels, biYPels; public uint biClrUsed, biClrImportant; }

    internal const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4, SIIGBF_THUMBNAILONLY = 0x8;
    internal const uint SIGDN_NORMALDISPLAY = 0x0;
    internal static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    internal static readonly Guid PKEY_AppUserModel_ID = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");   // pid 5

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName")] private static extern int SHCreateImageFactory(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHCreateItemFromParsingName")] private static extern int SHCreateShellItem(string path, IntPtr bc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll")] internal static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] internal static extern int PropVariantClear(ref PROPVARIANT pv);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int cb, out BITMAP bmp);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[]? bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetApplicationUserModelId(IntPtr hProcess, ref uint length, StringBuilder id);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Imagen del Shell (icono o miniatura) como BGRA premultiplicado, respetando el tamaño que devuelva. Null si el Shell no puede.</summary>
    internal static IconBitmap? ShellImage(string parsingName, int size, int flags)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateImageFactory(Environment.ExpandEnvironmentVariables(parsingName), IntPtr.Zero, ref iid, out var f) != 0 || f is null) return null;
        try
        {
            if (f.GetImage(new SIZE { cx = size, cy = size }, flags, out IntPtr hbm) != 0 || hbm == IntPtr.Zero) return null;
            try { return ToBitmap(hbm); } finally { DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(f); }
    }

    internal static string? ShellDisplayName(string parsingName)
    {
        var iid = typeof(IShellItem).GUID;
        if (SHCreateShellItem(parsingName, IntPtr.Zero, ref iid, out var it) != 0 || it is null) return null;
        try { return it.GetDisplayName(SIGDN_NORMALDISPLAY, out var name) == 0 ? name : null; } finally { Marshal.ReleaseComObject(it); }
    }

    private static IconBitmap? ToBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bmp) == 0 || bmp.bmWidth <= 0 || bmp.bmHeight <= 0) return null;
        int w = bmp.bmWidth, h = bmp.bmHeight;
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var px = new byte[w * h * 4]; IntPtr dc = GetDC(IntPtr.Zero);
        try { if (GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0) == 0) return null; } finally { ReleaseDC(IntPtr.Zero, dc); }
        bool anyAlpha = false; for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
        for (int i = 0; i < px.Length; i += 4)
        {
            if (!anyAlpha) { px[i + 3] = 255; continue; }                       // imagen sin canal alfa: opaca
            byte a = px[i + 3]; px[i] = (byte)(px[i] * a / 255); px[i + 1] = (byte)(px[i + 1] * a / 255); px[i + 2] = (byte)(px[i + 2] * a / 255);   // → premultiplicado
        }
        return new IconBitmap(w, h, px);
    }
}
