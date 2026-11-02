using System.Runtime.InteropServices;
using System.Text;
using WinMac.Bar.Tray;

namespace WinMac.Bar.Win.Shell.Tray;

/// <summary>
/// Recibe los iconos de la bandeja de Windows SIN quitárselos a Explorer. Shell_NotifyIcon busca la ventana de clase «Shell_TrayWnd» más arriba en el orden Z y le envía
/// los datos (WM_COPYDATA). WinMac crea la suya, siempre encima, y REENVÍA cada mensaje a la de Explorer al instante, devolviendo su respuesta: la bandeja de Windows
/// sigue igual y, si WinMac se cierra o falla, Windows vuelve a usar la de Explorer sin perder nada. Al arrancar difunde «TaskbarCreated» (el aviso estándar de que
/// la barra de tareas se ha reiniciado) para que las apps vuelvan a registrar sus iconos. Misma técnica que barras de tareas alternativas de código abierto (RetroBar).
/// <para>Corre en su propio hilo con su bucle de mensajes: si la barra está ocupada, las apps que llaman a Shell_NotifyIcon no esperan.</para>
/// <para>Solo reenvía WM_COPYDATA (iconos, AppBar, posición de iconos), WM_COMMAND y los mensajes privados (WM_USER…0x7FFF); NO los difundidos a todas las ventanas,
/// que Explorer ya recibe por su cuenta.</para>
/// </summary>
public sealed class TrayHost : IDisposable
{
    private const uint WM_DESTROY = 0x2, WM_CLOSE = 0x10, WM_TIMER = 0x113, WM_COMMAND = 0x111, WM_COPYDATA = 0x4A, WM_USER = 0x400;
    private const uint WS_POPUP = 0x80000000, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SMTO_ABORTIFHUNG = 0x2;
    private static readonly IntPtr HWND_TOPMOST = new(-1), HWND_BROADCAST = new(0xFFFF);

    private readonly Action<string> _log; private readonly WndProc _proc; private Thread? _thread; private IntPtr _hwnd, _explorer; private uint _taskbarCreated;
    private readonly ManualResetEventSlim _ready = new(); private volatile bool _primary; private int _messages;
    private bool _warnedNotPrimary;

    /// <summary>Un mensaje de Shell_NotifyIcon ya reenviado a Explorer. Se produce en el hilo de la bandeja.</summary>
    public event Action<TrayMessage>? Message;

    public TrayHost(Action<string> log) { _log = log; _proc = WndProcImpl; }

    /// <summary>Las apps encuentran la ventana de WinMac (y no la de Explorer) al llamar a Shell_NotifyIcon.</summary>
    public bool IsPrimary => _primary;
    public bool ExplorerFound => _explorer != IntPtr.Zero;
    public int MessagesSeen => _messages;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "WinMac bandeja" };
        _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Loop()
    {
        try
        {
            var inst = GetModuleHandle(null);
            var cls = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = inst, lpszClassName = "Shell_TrayWnd" };   // las clases son por proceso: no choca con la de Explorer
            RegisterClassEx(ref cls);
            _hwnd = CreateWindowEx(WS_EX_TOPMOST | WS_EX_TOOLWINDOW, "Shell_TrayWnd", "", WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) { _log("[bandeja] no se pudo crear la ventana de bandeja (" + Marshal.GetLastWin32Error() + ")"); return; }
            _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
            _explorer = FindExplorerTray();
            Reassert();
            SetTimer(_hwnd, (UIntPtr)1, 3000, IntPtr.Zero);
            _log($"[bandeja] lista: {(ExplorerFound ? "bandeja de Explorer encontrada" : "SIN bandeja de Explorer")}, {(IsPrimary ? "las apps encuentran la de WinMac" : "las apps aún encuentran la de Explorer")}");
            PostMessage(HWND_BROADCAST, _taskbarCreated, IntPtr.Zero, IntPtr.Zero);   // las apps vuelven a registrar sus iconos (pasan por aquí y se reenvían)
        }
        catch (Exception e) { _log("[bandeja] no arrancó: " + e.GetType().Name + ": " + e.Message); return; }
        finally { _ready.Set(); }

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
    }

    /// <summary>Ventana encima de la de Explorer (para que Shell_NotifyIcon la encuentre primero) y comprobación real con FindWindow.</summary>
    private void Reassert()
    {
        if (_explorer == IntPtr.Zero || !IsWindow(_explorer)) _explorer = FindExplorerTray();
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        bool primary = FindWindowW("Shell_TrayWnd", null) == _hwnd;
        if (!primary && !_warnedNotPrimary) { _warnedNotPrimary = true; _log("[bandeja] Windows sigue encontrando la bandeja de Explorer primero: los iconos nuevos no llegarán a WinMac"); }
        if (primary) _warnedNotPrimary = false;
        _primary = primary;
    }

    private IntPtr FindExplorerTray()
    {
        IntPtr found = IntPtr.Zero; uint self = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (h == _hwnd) return true;
            var c = new StringBuilder(64); GetClassNameW(h, c, 64);
            if (c.ToString() != "Shell_TrayWnd") return true;
            GetWindowThreadProcessId(h, out uint pid); if (pid == self) return true;
            found = h; return false;
        }, IntPtr.Zero);
        return found;
    }

    private IntPtr Forward(uint msg, IntPtr w, IntPtr l, IntPtr fallback)
    {
        if (_explorer == IntPtr.Zero || !IsWindow(_explorer)) _explorer = FindExplorerTray();
        if (_explorer == IntPtr.Zero) return fallback;
        return SendMessageTimeoutW(_explorer, msg, w, l, SMTO_ABORTIFHUNG, 4000, out var res) == IntPtr.Zero ? fallback : res;
    }

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            switch (msg)
            {
                case WM_COPYDATA:
                {
                    var result = Forward(msg, w, l, new IntPtr(1));   // primero Explorer: su bandeja y las AppBar siguen igual, y la app recibe SU respuesta
                    var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(l);
                    if (cds.dwData == (UIntPtr)TrayProtocol.CopyDataNotify && cds.cbData > 0 && cds.lpData != IntPtr.Zero)
                    {
                        var bytes = new byte[cds.cbData]; Marshal.Copy(cds.lpData, bytes, 0, bytes.Length);
                        if (TrayProtocol.Parse(bytes) is { } m) { Interlocked.Increment(ref _messages); Message?.Invoke(m); }
                    }
                    return result;
                }
                case WM_TIMER: Reassert(); return IntPtr.Zero;
                case WM_CLOSE: DestroyWindow(h); return IntPtr.Zero;
                case WM_DESTROY: KillTimer(h, (UIntPtr)1); PostQuitMessage(0); return IntPtr.Zero;
                case WM_COMMAND: return Forward(msg, w, l, IntPtr.Zero);
            }

            if (msg >= WM_USER && msg < 0x8000) return Forward(msg, w, l, IntPtr.Zero);   // mensajes privados de la clase Shell_TrayWnd
        }
        catch (Exception e) { _log("[bandeja] error al atender un mensaje: " + e.GetType().Name); }
        return DefWindowProcW(h, msg, w, l);
    }

    public void Dispose()
    {
        var h = _hwnd; if (h != IntPtr.Zero) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);   // al desaparecer, Windows vuelve a encontrar la de Explorer
        _thread?.Join(TimeSpan.FromSeconds(2)); _hwnd = IntPtr.Zero; _ready.Dispose();
    }

    // ── P/Invoke propio (este componente es independiente de la barra)
    private delegate IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l);
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX { public uint cbSize, style; public WndProc lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] private struct COPYDATASTRUCT { public UIntPtr dwData; public int cbData; public IntPtr lpData; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr p);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string? cls, string? name);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string s);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr fn);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr h, UIntPtr id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}

/// <summary>Píxeles de un icono de bandeja (HICON de otra app: los iconos son objetos de USER compartidos en la sesión) como BGRA premultiplicado.</summary>
public static class TrayIconPixels
{
    [StructLayout(LayoutKind.Sequential)] private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr h, out ICONINFO info);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(IntPtr h, int size, out BITMAP b);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);

    /// <returns>(ancho, alto, BGRA premultiplicado) o null si el icono ya no existe.</returns>
    public static (int W, int H, byte[] Bgra)? Read(uint hicon)
    {
        if (hicon == 0 || !GetIconInfo((IntPtr)hicon, out var info)) return null;
        IntPtr dc = IntPtr.Zero;
        try
        {
            IntPtr src = info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask; if (src == IntPtr.Zero || GetObjectW(src, Marshal.SizeOf<BITMAP>(), out var bmp) == 0) return null;
            int w = bmp.bmWidth, h = info.hbmColor != IntPtr.Zero ? bmp.bmHeight : bmp.bmHeight / 2; if (w <= 0 || h <= 0 || w > 256 || h > 256) return null;
            dc = GetDC(IntPtr.Zero);
            var bih = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var px = new byte[w * h * 4];
            if (info.hbmColor == IntPtr.Zero) return null;   // iconos monocromos antiguos: sin color que mostrar
            if (GetDIBits(dc, info.hbmColor, 0, (uint)h, px, ref bih, 0) == 0) return null;
            bool alpha = false; for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { alpha = true; break; }
            if (!alpha)
            {
                var mask = new byte[w * h * 4]; var mh = bih;
                if (info.hbmMask != IntPtr.Zero && GetDIBits(dc, info.hbmMask, 0, (uint)h, mask, ref mh, 0) != 0) for (int i = 0; i < w * h; i++) px[i * 4 + 3] = (byte)(mask[i * 4] == 0 ? 255 : 0);
                else for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            }
            for (int i = 0; i < w * h; i++) { int a = px[i * 4 + 3]; if (a == 255) continue; px[i * 4] = (byte)(px[i * 4] * a / 255); px[i * 4 + 1] = (byte)(px[i * 4 + 1] * a / 255); px[i * 4 + 2] = (byte)(px[i * 4 + 2] * a / 255); }
            return (w, h, px);
        }
        catch (Exception) { return null; }
        finally
        {
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor); if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }
}
