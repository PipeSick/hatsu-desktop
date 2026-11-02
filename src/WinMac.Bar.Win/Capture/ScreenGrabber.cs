using System.Runtime.InteropServices;
using System.Text;
using WinMac.Bar.Capture;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// Copies the screen with documented GDI (<c>BitBlt</c> from the screen DC, physical pixels: the bar process is Per-Monitor V2) and lists monitors and
/// windows for the capture overlay. Content that an app protects (<c>SetWindowDisplayAffinity</c>) comes out black, as Windows intends: it is not bypassed.
/// </summary>
internal static class ScreenGrabber
{
    /// <summary>The whole virtual screen as BGRA top-down (the frozen frame of a capture session).</summary>
    public static (byte[] Px, CaptureRect Rect) GrabVirtualScreen()
    {
        var r = new CaptureRect(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));   // SM_X/Y/CX/CYVIRTUALSCREEN
        return (Grab(r), r);
    }

    public static byte[] Grab(CaptureRect r)
    {
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = r.W, biHeight = -r.H, biPlanes = 1, biBitCount = 32 };
        IntPtr bits; IntPtr bmp = CreateDIBSection(screen, ref bi, 0, out bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            if (!BitBlt(mem, 0, 0, r.W, r.H, screen, r.X, r.Y, 0x00CC0020 | 0x40000000 /* SRCCOPY | CAPTUREBLT */)) throw new InvalidOperationException("BitBlt falló: " + Marshal.GetLastWin32Error());
            var px = new byte[r.W * r.H * 4];
            Marshal.Copy(bits, px, 0, px.Length);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;   // GDI leaves alpha undefined: the screen is opaque
            return px;
        }
        finally { SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen); }
    }

    /// <summary>
    /// Repeated copies of one area (recording): ONE memory DC and ONE bottom-up DIB section for the whole recording (Media Foundation wants uncompressed RGB
    /// bottom-up, so no flip is needed), copied into the caller's buffer. Created and released on the encoder's thread. Must be disposed.
    /// </summary>
    internal sealed class AreaGrabber : IDisposable
    {
        private readonly CaptureRect _r; private IntPtr _mem, _bmp, _old, _bits; private readonly IntPtr _screen;

        public AreaGrabber(CaptureRect r)
        {
            _r = r; _screen = GetDC(IntPtr.Zero); _mem = CreateCompatibleDC(_screen);
            var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = r.W, biHeight = r.H /* positive: bottom-up */, biPlanes = 1, biBitCount = 32 };
            _bmp = CreateDIBSection(_screen, ref bi, 0, out _bits, IntPtr.Zero, 0);
            if (_bmp == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("No se pudo crear el búfer de captura"); }
            _old = SelectObject(_mem, _bmp);
        }

        public void CopyInto(byte[] dest)
        {
            if (!BitBlt(_mem, 0, 0, _r.W, _r.H, _screen, _r.X, _r.Y, 0x00CC0020 | 0x40000000)) throw new InvalidOperationException("BitBlt falló: " + Marshal.GetLastWin32Error());
            Marshal.Copy(_bits, dest, 0, Math.Min(dest.Length, _r.W * _r.H * 4));
        }

        public void Dispose()
        {
            if (_mem != IntPtr.Zero) { if (_old != IntPtr.Zero) SelectObject(_mem, _old); DeleteDC(_mem); _mem = IntPtr.Zero; }
            if (_bmp != IntPtr.Zero) { DeleteObject(_bmp); _bmp = IntPtr.Zero; }
            if (_screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screen);
        }
    }

    public static IReadOnlyList<CaptureMonitor> Monitors()
    {
        var list = new List<CaptureMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
        {
            var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(h, ref mi))
            {
                double scale = GetDpiForMonitor(h, 0, out uint dx, out _) == 0 && dx >= 48 ? dx / 96.0 : 1.0;
                list.Add(new CaptureMonitor(new CaptureRect(mi.rcMonitor.L, mi.rcMonitor.T, mi.rcMonitor.R - mi.rcMonitor.L, mi.rcMonitor.B - mi.rcMonitor.T), scale, (mi.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Visible top-level windows front to back, with their visible bounds (DWM extended frame, without the invisible resize border). Ours, cloaked and tool windows are skipped.</summary>
    public static IReadOnlyList<CaptureWindow> Windows(IntPtr exclude)
    {
        var list = new List<CaptureWindow>(); uint me = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (h == exclude || !IsWindowVisible(h) || IsIconic(h)) return true;
            GetWindowThreadProcessId(h, out uint pid); if (pid == me) return true;
            if ((GetWindowLongPtr(h, -20).ToInt64() & 0x80) != 0) return true;   // WS_EX_TOOLWINDOW
            if (DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out int cloaked, 4) == 0 && cloaked != 0) return true;
            RECT r; if (DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out r, Marshal.SizeOf<RECT>()) != 0) GetWindowRect(h, out r);
            if (r.R - r.L < 40 || r.B - r.T < 30) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            var cls = new StringBuilder(64); GetClassName(h, cls, 64);
            if (cls.ToString() is "Progman" or "WorkerW" && sb.Length == 0) { list.Add(new CaptureWindow(h, "Escritorio", new CaptureRect(r.L, r.T, r.R - r.L, r.B - r.T))); return true; }
            if (sb.Length == 0) return true;
            list.Add(new CaptureWindow(h, sb.ToString(), new CaptureRect(r.L, r.T, r.R - r.L, r.B - r.T)));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Encodes BGRA pixels as PNG or JPEG with Windows' own encoder (WinRT BitmapEncoder; no extra library).</summary>
    public static async Task SaveAsync(byte[] bgra, int w, int h, string path, bool jpeg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var folder = await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var file = await folder.CreateFileAsync(Path.GetFileName(path), global::Windows.Storage.CreationCollisionOption.GenerateUniqueName);
        using var stream = await file.OpenAsync(global::Windows.Storage.FileAccessMode.ReadWrite);
        var enc = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(jpeg ? global::Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId : global::Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
        enc.SetPixelData(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Ignore, (uint)w, (uint)h, 96, 96, bgra);
        await enc.FlushAsync();
    }

    private delegate bool MonitorProc(IntPtr h, IntPtr dc, IntPtr r, IntPtr l);
    private delegate bool WindowProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr h, ref MONITORINFO mi);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr h, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowProc f, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT v, int size);
}
