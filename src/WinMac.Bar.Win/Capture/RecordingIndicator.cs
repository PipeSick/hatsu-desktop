using System.Runtime.InteropServices;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// The always-visible recording indicator (docs/26): «● Grabando 00:12 · Detener» on top of the recorded monitor. A click stops the recording.
/// It is excluded from capture (<c>SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)</c>, documented, Windows 10 2004+) so it does not appear in the video,
/// while the user always sees it. Plain GDI: it is tiny and repaints once a second.
/// </summary>
internal sealed class RecordingIndicator : IDisposable
{
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_PAINT = 0xF, WM_LBUTTONUP = 0x202, WM_SETCURSOR = 0x20;
    private readonly BarNative.WndProc _proc; private readonly Action _stop; private readonly Action<string> _log;
    private IntPtr _hwnd; private string _text = "Grabando 00:00"; private float _s = 1;
    private readonly string _action = "■ Detener", _cls = "WinMacRecording"; private readonly uint _dot = 0x303BFF;

    public RecordingIndicator(Action stop, Action<string> log) { _stop = stop; _log = log; _proc = WndProcImpl; }

    /// <summary>
    /// WM-002: the same pill for the capture countdown («● Captura en 5 s · Cancelar», amber dot). Excluded from capture too, never activated (taking the focus
    /// would close the menu the user is about to capture), and a click cancels.
    /// </summary>
    public RecordingIndicator(Action click, Action<string> log, string text, string action, uint dotBgr, string windowClass) : this(click, log)
    {
        _text = text; _action = action; _dot = dotBgr; _cls = windowClass;
    }

    public void SetText(string text)
    {
        if (text == _text || _hwnd == IntPtr.Zero) return;
        _text = text; BarNative.InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    public void Hide() { if (_hwnd != IntPtr.Zero) BarNative.ShowWindow(_hwnd, 0); }

    public CaptureRect Bounds { get; private set; }

    public void Show(CaptureMonitor mon)
    {
        _s = (float)mon.Scale; int w = (int)(220 * _s), h = (int)(34 * _s);
        int x = mon.Bounds.Right - w - (int)(16 * _s), y = mon.Bounds.Y + (int)(44 * _s);
        Bounds = new CaptureRect(x, y, w, h);
        _hwnd = InstanceWindows.Create(_cls, 32649 /* IDC_HAND */, _proc, WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "WinMac grabando", WS_POPUP, x, y, w, h);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear el indicador de grabación");
        if (!SetWindowDisplayAffinity(_hwnd, 0x11 /* WDA_EXCLUDEFROMCAPTURE */)) _log("[grabación] el indicador no se puede excluir de la captura en este Windows (aparecerá en el vídeo)");
        var rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, h, h); BarNative.SetWindowRgn(_hwnd, rgn, false);
        BarNative.ShowWindow(_hwnd, 4 /* SW_SHOWNOACTIVATE */);
    }

    public void Update(TimeSpan elapsed)
    {
        var t = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
        SetText("Grabando " + t);
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch ((int)msg)
            {
                case WM_PAINT: Paint(h); return IntPtr.Zero;
                case WM_LBUTTONUP: _stop(); return IntPtr.Zero;
            }
        }
        catch (Exception e) { _log("[grabación] indicador: " + e.Message); }
        return BarNative.DefWindowProc(h, msg, w, l);
    }

    private void Paint(IntPtr h)
    {
        var ps = new PAINTSTRUCT(); var dc = BeginPaint(h, ref ps);
        try
        {
            BarNative.GetClientRect(h, out var rc);
            var bg = CreateSolidBrush(0x1E1C1C); var r = new BarNative.RECT { Left = 0, Top = 0, Right = rc.Right, Bottom = rc.Bottom }; FillRect(dc, ref r, bg); DeleteObject(bg);
            int d = (int)(12 * _s), cy = rc.Bottom / 2, x0 = (int)(14 * _s);
            var red = CreateSolidBrush(_dot); var oldB = SelectObject(dc, red); var pen = GetStockObject(8 /* NULL_PEN */); var oldP = SelectObject(dc, pen);
            Ellipse(dc, x0, cy - d / 2, x0 + d, cy + d / 2); SelectObject(dc, oldB); SelectObject(dc, oldP); DeleteObject(red);
            var font = CreateFontW(-(int)(13 * _s), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI"); var oldF = SelectObject(dc, font);
            SetBkMode(dc, 1); SetTextColor(dc, 0xFFFFFF);
            var tr = new BarNative.RECT { Left = x0 + d + (int)(8 * _s), Top = 0, Right = rc.Right, Bottom = rc.Bottom };
            DrawTextW(dc, _text, -1, ref tr, 0x20 | 0x4 /* SINGLELINE | VCENTER */);
            SetTextColor(dc, 0xD6D1D1);
            var sr = new BarNative.RECT { Left = 0, Top = 0, Right = rc.Right - (int)(14 * _s), Bottom = rc.Bottom };
            DrawTextW(dc, _action, -1, ref sr, 0x20 | 0x4 | 0x2 /* RIGHT */);
            SelectObject(dc, oldF); DeleteObject(font);
        }
        finally { EndPaint(h, ref ps); }
    }

    public void Dispose() { if (_hwnd != IntPtr.Zero) { BarNative.DestroyWindow(_hwnd); InstanceWindows.Forget(_hwnd); _hwnd = IntPtr.Zero; } }

    [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public BarNative.RECT rcPaint; public int fRestore, fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref BarNative.RECT r, IntPtr b);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr dc, string s, int n, ref BarNative.RECT r, uint f);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint c);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr dc, int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int m);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint c);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(int h, int w, int e, int o, int weight, uint i, uint u, uint s, uint cs, uint op, uint cp, uint q, uint pf, string face);
}
