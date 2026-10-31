using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WinMac.Apps;
using WinMac.Dock.Win.Rendering;

namespace WinMac.Dock.Win.Shell;

/// <summary>
/// Miniaturas de ventanas para la vista previa. Captura con PrintWindow(PW_RENDERFULLCONTENT) (API documentada; funciona con Chromium/Electron y la mayoría de apps UWP)
/// en UN hilo de trabajo: el Dock nunca espera a una captura. Se omiten ventanas minimizadas (PrintWindow no las pinta; se conserva la última captura buena),
/// ventanas que no responden (IsHungAppWindow) y capturas completamente negras (fallo típico de contenido protegido o acelerado por hardware).
/// LIMITACIÓN: es una instantánea (se refresca cada ~1,5 s mientras se pasa el ratón), no una miniatura en vivo de DWM; sin Windows.Graphics.Capture.
/// </summary>
public sealed class WindowThumbnails : IThumbnailSource, IDisposable
{
    public const int MaxW = 480, MaxH = 300;
    private const double RefreshSeconds = 1.5;
    private readonly ConcurrentDictionary<long, IconBitmap> _cache = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastTry = new();
    private readonly HashSet<long> _inflight = new();
    private readonly BlockingCollection<long> _queue = new(new ConcurrentQueue<long>(), 32);
    private readonly Thread _worker; private volatile bool _stop; private int _dirty;
    public Action<string>? Log { get; set; }
    public int Captured { get; private set; }
    public int Failed { get; private set; }

    public WindowThumbnails()
    {
        _worker = new Thread(Work) { IsBackground = true, Name = "WinMac.Thumbnails", Priority = ThreadPriority.BelowNormal }; _worker.Start();
    }

    public IconBitmap? Get(long hwnd) => _cache.TryGetValue(hwnd, out var b) ? b : null;
    /// <summary>Proporción ancho/alto de la última captura (0 si no hay).</summary>
    public double Aspect(long hwnd) => _cache.TryGetValue(hwnd, out var b) && b.Height > 0 ? (double)b.Width / b.Height : 0;
    public bool TakeDirty() => Interlocked.Exchange(ref _dirty, 0) != 0;

    /// <summary>Pide (sin esperar) una captura nueva si la última tiene más de ~1,5 s. Las minimizadas no se capturan.</summary>
    public void Request(long hwnd, bool minimized)
    {
        if (minimized || _stop) return;
        var now = DateTime.UtcNow;
        if (_lastTry.TryGetValue(hwnd, out var t) && (now - t).TotalSeconds < RefreshSeconds) return;
        lock (_inflight) { if (!_inflight.Add(hwnd)) return; }
        _lastTry[hwnd] = now;
        if (!_queue.TryAdd(hwnd)) lock (_inflight) _inflight.Remove(hwnd);
    }

    private void Work()
    {
        foreach (var hwnd in _queue.GetConsumingEnumerable())
        {
            try
            {
                var bmp = Capture(new IntPtr(hwnd));
                if (bmp is not null) { _cache[hwnd] = bmp; Captured++; Interlocked.Exchange(ref _dirty, 1); } else Failed++;
            }
            catch (Exception e) { Failed++; Log?.Invoke($"Miniatura de {hwnd:X}: {e.GetType().Name}: {e.Message}"); }
            finally { lock (_inflight) _inflight.Remove(hwnd); }
            if (_cache.Count > 48) Prune();
        }
    }

    private void Prune()
    {
        foreach (var k in _cache.Keys.ToArray()) if (!Win.IsWindow(new IntPtr(k))) { _cache.TryRemove(k, out _); _lastTry.TryRemove(k, out _); }
    }

    /// <summary>Captura una ventana a un máximo de <see cref="MaxW"/>×<see cref="MaxH"/> (sin ampliar), sin los bordes invisibles de redimensión. Null si no se pudo o salió en negro.</summary>
    public static IconBitmap? Capture(IntPtr hwnd, int maxW = MaxW, int maxH = MaxH)
    {
        if (!Win.IsWindow(hwnd) || Win.IsIconic(hwnd) || Win.IsHungAppWindow(hwnd)) return null;
        if (!Win.GetWindowRect(hwnd, out var r)) return null;
        int w = r.Right - r.Left, h = r.Bottom - r.Top; if (w < 16 || h < 16 || w > 16384 || h > 16384) return null;
        int cx = 0, cy = 0, cw = w, ch = h;
        if (Win.DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var fb, Marshal.SizeOf<Win.RECT>()) == 0)
        {
            cx = Math.Clamp(fb.Left - r.Left, 0, w - 1); cy = Math.Clamp(fb.Top - r.Top, 0, h - 1);
            cw = Math.Clamp(fb.Right - fb.Left, 1, w - cx); ch = Math.Clamp(fb.Bottom - fb.Top, 1, h - cy);
        }
        IntPtr dc = Win.CreateCompatibleDC(IntPtr.Zero); if (dc == IntPtr.Zero) return null;
        var bi = new Win.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr bmp = Win.CreateDIBSection(dc, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0), old = IntPtr.Zero;
        if (bmp == IntPtr.Zero || bits == IntPtr.Zero) { Win.DeleteDC(dc); return null; }
        try
        {
            old = Win.SelectObject(dc, bmp);
            if (!Win.PrintWindow(hwnd, dc, 2 /* PW_RENDERFULLCONTENT */)) return null;
            var src = new byte[w * h * 4]; Marshal.Copy(bits, src, 0, src.Length);
            return Downscale(src, w, cx, cy, cw, ch, maxW, maxH);
        }
        finally { if (old != IntPtr.Zero) Win.SelectObject(dc, old); Win.DeleteObject(bmp); Win.DeleteDC(dc); }
    }

    /// <summary>Reduce con promedio por bloques (sin ampliar) y fuerza alfa opaco. Null si la imagen es totalmente negra. Pública para pruebas.</summary>
    public static IconBitmap? Downscale(byte[] src, int stride, int cx, int cy, int cw, int ch, int maxW, int maxH)
    {
        double k = Math.Min(1.0, Math.Min((double)maxW / cw, (double)maxH / ch));
        int tw = Math.Max(1, (int)Math.Round(cw * k)), th = Math.Max(1, (int)Math.Round(ch * k));
        var dst = new byte[tw * th * 4]; long nonBlack = 0;
        for (int y = 0; y < th; y++)
        {
            int y0 = cy + y * ch / th, y1 = Math.Max(y0 + 1, cy + (y + 1) * ch / th);
            for (int x = 0; x < tw; x++)
            {
                int x0 = cx + x * cw / tw, x1 = Math.Max(x0 + 1, cx + (x + 1) * cw / tw); long b = 0, g = 0, rr = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++) { int i = (yy * stride + x0) * 4; for (int xx = x0; xx < x1; xx++, i += 4) { b += src[i]; g += src[i + 1]; rr += src[i + 2]; n++; } }
                int o = (y * tw + x) * 4; dst[o] = (byte)(b / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(rr / n); dst[o + 3] = 255;
                if (dst[o] != 0 || dst[o + 1] != 0 || dst[o + 2] != 0) nonBlack++;
            }
        }
        return nonBlack == 0 ? null : new IconBitmap(tw, th, dst);
    }

    public void Dispose() { _stop = true; _queue.CompleteAdding(); }

    private static class Win
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    }
}
