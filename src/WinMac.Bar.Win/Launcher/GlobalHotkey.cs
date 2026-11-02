using System.Runtime.InteropServices;

namespace WinMac.Bar.Win.Launcher;

/// <summary>
/// Atajo global con <c>RegisterHotKey</c> (API documentada; ningún gancho). Hilo propio con su bucle de mensajes. Si otro programa ya tiene el atajo (p. ej. PowerToys Run con
/// Alt+Espacio), Windows lo rechaza y se dice en el registro: no se le quita a nadie. Se libera al cerrar (y Windows lo libera solo si el proceso muere).
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4, ModNoRepeat = 0x4000;
    private const int WM_HOTKEY = 0x312, WM_QUIT = 0x12;
    private Thread? _thread; private uint _threadId;

    public bool Registered { get; private set; }

    public GlobalHotkey(uint mods, uint vk, Action pressed, Action<string> log, string name)
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            Registered = RegisterHotKey(IntPtr.Zero, 1, mods | ModNoRepeat, vk);
            int err = Marshal.GetLastWin32Error();
            ready.Set();
            if (!Registered) { log($"[spotlight] el atajo {name} no está libre (error {err}); otro programa lo usa"); return; }
            while (GetMessage(out var m, IntPtr.Zero, 0, 0) > 0) if (m.message == WM_HOTKEY) pressed();
            UnregisterHotKey(IntPtr.Zero, 1);
        }) { IsBackground = true, Name = "WinMac atajo " + name };
        _thread.Start(); ready.Wait(TimeSpan.FromSeconds(3));
        if (Registered) log($"[spotlight] atajo {name} registrado");
    }

    public void Dispose()
    {
        if (_thread is null) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2)); _thread = null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, int msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
