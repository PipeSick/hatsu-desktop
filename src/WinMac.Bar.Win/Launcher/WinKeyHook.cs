using System.Runtime.InteropServices;
using WinMac.Bar.Launcher;

namespace WinMac.Bar.Win.Launcher;

/// <summary>
/// Gancho de teclado de bajo nivel (<c>WH_KEYBOARD_LL</c>, API documentada; NO inyecta nada en otros procesos) en un hilo propio con su bucle de mensajes, para que una barra
/// ocupada nunca retrase el teclado. Deja pasar TODAS las teclas; solo al soltar una pulsación aislada de Windows envía antes una tecla neutra (VK 0xE8, sin asignar)
/// para que Windows no abra el menú Inicio, y pide abrir Launchpad (ver <see cref="WinKeyTap"/>).
/// Recuperación: el gancho muere con el proceso (si la barra se cierra o falla, la tecla Windows vuelve a abrir Inicio sin hacer nada); se quita al desactivar el ajuste.
/// </summary>
internal sealed class WinKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105, WM_QUIT = 0x12;
    private const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly WinKeyTap _tap = new();
    private readonly Action _open; private readonly Action<string> _log;
    private readonly HookProc _kbProc, _mouseProc;
    private Thread? _thread; private uint _threadId; private IntPtr _kb, _mouse; private volatile bool _running;

    public WinKeyHook(Action open, Action<string> log) { _open = open; _log = log; _kbProc = KeyboardProc; _mouseProc = MouseProc; }

    public bool Enabled { get => _tap.Enabled; set => _tap.Enabled = value; }
    public bool Running => _running;

    public void Start()
    {
        if (_thread is not null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            IntPtr mod = GetModuleHandle(null);
            _kb = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, mod, 0);
            _mouse = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
            _running = _kb != IntPtr.Zero;
            ready.Set();
            if (!_running) { _log($"[launchpad] no se pudo instalar el gancho de teclado (error {Marshal.GetLastWin32Error()}): la tecla Windows abre el menú Inicio"); return; }
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref msg); DispatchMessage(ref msg); }
            if (_kb != IntPtr.Zero) UnhookWindowsHookEx(_kb);
            if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
            _kb = _mouse = IntPtr.Zero; _running = false;
        }) { IsBackground = true, Name = "Hatsu Desktop tecla Windows" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
        if (_running) _log("[launchpad] tecla Windows: gancho instalado (los atajos con Windows no se tocan)");
    }

    private IntPtr KeyboardProc(int code, IntPtr w, IntPtr l)
    {
        try
        {
            if (code >= 0)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
                int m = (int)w; bool down = m is WM_KEYDOWN or WM_SYSKEYDOWN, up = m is WM_KEYUP or WM_SYSKEYUP;
                if (down || up)
                {
                    var action = _tap.OnKey((int)k.vkCode, down, (k.flags & LLKHF_INJECTED) != 0, k.time);
                    if (action == WinKeyAction.MaskAndOpen)
                    {
                        SendMask();          // Windows ve «Windows + tecla neutra»: no abre Inicio. La tecla Windows soltada pasa igual (no se bloquea nada)
                        _open();             // la barra abre Launchpad en su hilo (si no puede, abre Inicio con Ctrl+Esc)
                    }
                }
            }
        }
        catch (Exception) { /* nunca se retiene una tecla por un error nuestro */ }
        return CallNextHookEx(IntPtr.Zero, code, w, l);
    }

    private IntPtr MouseProc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && (int)w is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN) _tap.OnMouse();
        return CallNextHookEx(IntPtr.Zero, code, w, l);
    }

    private static void SendMask()
    {
        var inputs = new INPUT[2];
        inputs[0].type = 1; inputs[0].ki.wVk = WinKeyTap.VkMask;
        inputs[1].type = 1; inputs[1].ki.wVk = WinKeyTap.VkMask; inputs[1].ki.dwFlags = 2 /* KEYEVENTF_KEYUP */;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>Plan B si Launchpad no puede abrirse: el menú Inicio de siempre (Ctrl+Esc, el atajo documentado de Windows).</summary>
    public static void OpenStartMenu()
    {
        var inputs = new INPUT[4];
        inputs[0].type = 1; inputs[0].ki.wVk = 0x11;
        inputs[1].type = 1; inputs[1].ki.wVk = 0x1B;
        inputs[2].type = 1; inputs[2].ki.wVk = 0x1B; inputs[2].ki.dwFlags = 2;
        inputs[3].type = 1; inputs[3].ki.wVk = 0x11; inputs[3].ki.dwFlags = 2;
        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_thread is null) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2)); _thread = null;
    }

    private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; [FieldOffset(8)] public MOUSEINPUT mi; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
