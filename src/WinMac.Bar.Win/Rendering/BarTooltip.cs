using System.Runtime.InteropServices;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// It7: sugerencia (tooltip) de los módulos de la barra. Usa el control nativo de Windows (tooltips_class32) en modo manual: la barra decide cuándo
/// mostrarla (tras una pausa sobre el módulo) y la oculta al salir, al pulsar o al arrastrar. Es un control nativo: su aspecto (claro/oscuro) lo decide Windows.
/// Si no se puede crear, la barra sigue funcionando sin sugerencias.
/// </summary>
internal sealed class BarTooltip : IDisposable
{
    private const uint WS_POPUP = 0x80000000, TTS_ALWAYSTIP = 1, TTS_NOPREFIX = 2, WS_EX_TOPMOST = 8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint TTM_ADDTOOLW = 0x432, TTM_TRACKACTIVATE = 0x411, TTM_TRACKPOSITION = 0x412, TTM_UPDATETIPTEXTW = 0x439, TTM_GETBUBBLESIZE = 0x41E, TTF_TRACK = 0x20, TTF_ABSOLUTE = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct TOOLINFO { public uint cbSize, uFlags; public IntPtr hwnd; public UIntPtr uId; public int l, t, r, b; public IntPtr hinst, lpszText, lParam, reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct ICCEX { public uint size, icc; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string? name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, ref TOOLINFO l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("comctl32.dll")] private static extern bool InitCommonControlsEx(ref ICCEX i);

    private IntPtr _tip; private TOOLINFO _ti; private IntPtr _text; private readonly IntPtr _owner; private readonly Action<string> _log;
    private string? _shown;

    public BarTooltip(IntPtr owner, Action<string> log)
    {
        _owner = owner; _log = log;
        try
        {
            var icc = new ICCEX { size = 8, icc = 0x4 /* ICC_BAR_CLASSES (tooltips) */ }; InitCommonControlsEx(ref icc);
            _tip = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "tooltips_class32", null, WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX, 0, 0, 0, 0, owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_tip == IntPtr.Zero) { _log("[bar] sugerencias: no se pudo crear el control nativo; la barra sigue sin ellas."); return; }
            _text = Marshal.StringToHGlobalUni(" ");
            _ti = new TOOLINFO { cbSize = (uint)Marshal.SizeOf<TOOLINFO>(), uFlags = TTF_TRACK | TTF_ABSOLUTE, hwnd = owner, uId = (UIntPtr)1, lpszText = _text };
            SendMessageW(_tip, TTM_ADDTOOLW, IntPtr.Zero, ref _ti);
        }
        catch (Exception e) { _log("[bar] sugerencias: " + e.GetType().Name + ": " + e.Message); _tip = IntPtr.Zero; }
    }

    /// <summary>Muestra <paramref name="text"/> centrada bajo (<paramref name="cxPx"/>, <paramref name="topPx"/>) en píxeles de pantalla.</summary>
    public void Show(string text, int cxPx, int topPx)
    {
        if (_tip == IntPtr.Zero) return;
        try
        {
            if (_shown != text)
            {
                var nt = Marshal.StringToHGlobalUni(text); _ti.lpszText = nt; SendMessageW(_tip, TTM_UPDATETIPTEXTW, IntPtr.Zero, ref _ti);
                var old = _text; _text = nt; Marshal.FreeHGlobal(old); _shown = text;
            }
            SendMessageW(_tip, TTM_TRACKPOSITION, IntPtr.Zero, (IntPtr)((topPx << 16) | (cxPx & 0xFFFF)));
            SendMessageW(_tip, TTM_TRACKACTIVATE, (IntPtr)1, ref _ti);
            int size = (int)SendMessageW(_tip, TTM_GETBUBBLESIZE, IntPtr.Zero, ref _ti); int w = size & 0xFFFF;
            SendMessageW(_tip, TTM_TRACKPOSITION, IntPtr.Zero, (IntPtr)((topPx << 16) | (Math.Max(0, cxPx - w / 2) & 0xFFFF)));
        }
        catch (Exception e) { _log("[bar] sugerencia: " + e.GetType().Name + ": " + e.Message); }
    }

    public void Hide()
    {
        if (_tip == IntPtr.Zero || _shown is null) return;
        try { SendMessageW(_tip, TTM_TRACKACTIVATE, IntPtr.Zero, ref _ti); } catch (Exception) { }
        _shown = null;
    }

    public void Dispose()
    {
        if (_tip != IntPtr.Zero) { try { DestroyWindow(_tip); } catch (Exception) { } _tip = IntPtr.Zero; }
        if (_text != IntPtr.Zero) { Marshal.FreeHGlobal(_text); _text = IntPtr.Zero; }
    }
}
