using System.Runtime.InteropServices;

namespace WinMac.Host.App;

/// <summary>
/// Ventana oculta del host + icono de bandeja con un menú mínimo (estado de los módulos y «Salir de WinMac»). Si Explorer se reinicia (<c>TaskbarCreated</c>) el icono se vuelve a crear.
/// El host no tiene más interfaz. Si la bandeja no está disponible (p. ej. un runner sin escritorio) se registra y el host sigue funcionando.
/// </summary>
internal sealed class Tray : IDisposable
{
    private const uint WM_TRAY = Native.WM_APP + 1; private const int IDI_APPLICATION = 32512;
    private readonly Native.WndProc _proc; private readonly Func<IReadOnlyDictionary<string, ModuleState>> _states; private readonly Action _quit; private readonly Action<string> _open; private readonly Action _settings; private readonly Action<string> _log;
    private readonly bool _showIcon; private IntPtr _hwnd; private uint _taskbarCreated; private bool _added;
    public IntPtr Handle => _hwnd;

    /// <summary>Explorer se reinició (WM-034: su barra de tareas nueva aparece visible).</summary>
    public Action? TaskbarCreated { get; set; }

    public Tray(Func<IReadOnlyDictionary<string, ModuleState>> states, Action quit, Action<string> open, Action settings, Action<string> log, bool showIcon)
    {
        _states = states; _quit = quit; _open = open; _settings = settings; _log = log; _showIcon = showIcon; _proc = WndProcImpl;
        var cls = new Native.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(), lpfnWndProc = _proc, hInstance = Native.GetModuleHandle(null), lpszClassName = "WinMacHost" };
        Native.RegisterClassEx(ref cls);
        _hwnd = Native.CreateWindowEx(Native.WS_EX_TOOLWINDOW, "WinMacHost", "WinMac", Native.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("no se pudo crear la ventana oculta del host: " + Marshal.GetLastWin32Error());
        _taskbarCreated = Native.RegisterWindowMessageW("TaskbarCreated");
        if (_showIcon) AddIcon();
    }

    private void AddIcon()
    {
        var d = Data(Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP);
        _added = Native.Shell_NotifyIconW(Native.NIM_ADD, ref d);
        _log(_added ? "[host] icono de bandeja creado" : "[host] no se pudo crear el icono de la bandeja (el host sigue funcionando)");
    }

    private Native.NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<Native.NOTIFYICONDATAW>(), hWnd = _hwnd, uID = 1, uFlags = flags, uCallbackMessage = WM_TRAY,
        hIcon = Native.LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION), szTip = "WinMac", szInfo = "", szInfoTitle = "",
    };

    private IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        if (_taskbarCreated != 0 && msg == _taskbarCreated) { if (_showIcon) AddIcon(); try { TaskbarCreated?.Invoke(); } catch (Exception) { } return IntPtr.Zero; }   // Explorer se reinició
        switch (msg)
        {
            case WM_TRAY when (uint)(long)l is Native.WM_RBUTTONUP or Native.WM_CONTEXTMENU: ShowMenu(); return IntPtr.Zero;
            case Native.WM_ENDSESSION when w != IntPtr.Zero: _quit(); return IntPtr.Zero;   // cierre de sesión o apagado: cierre ordenado
        }
        return Native.DefWindowProc(h, msg, w, l);
    }

    private static string Label(string id) => id switch { "bar" => "Barra superior", "dock" => "Dock", "notch" => "Isla", _ => id };
    private static string Text(ModuleState s) => s switch
    {
        ModuleState.Running => "en ejecución", ModuleState.Starting => "iniciando…", ModuleState.Stopping => "cerrando…", ModuleState.Stopped => "detenido", ModuleState.Crashed => "reiniciando…",
        ModuleState.Failed => "falló (no se reintenta)", ModuleState.NotInstalled => "no instalada", _ => "desactivado",
    };

    private TrayPanel? _panel;
    private static readonly string Version = typeof(Tray).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false) is [System.Reflection.AssemblyInformationalVersionAttribute v, ..] ? v.InformationalVersion.Split('+')[0] : "2.0";

    /// <summary>WM-011: the dark WinMac panel; the classic menu stays only as a fallback if the panel cannot be created (no GPU device, for example).</summary>
    private void ShowMenu()
    {
        try
        {
            var states = _states();
            _panel ??= new TrayPanel(a => RunPanelAction(a, states), _log);
            Native.GetCursorPos(out var p);
            _panel.Show(TrayPanelModel.Build(states, Version), p.X, p.Y);
            return;
        }
        catch (Exception e) { _log("[host] panel de la bandeja no disponible, menú clásico: " + e.GetType().Name + ": " + e.Message); _panel?.Close(); }
        ShowClassicMenu();
    }

    private void RunPanelAction(string a, IReadOnlyDictionary<string, ModuleState> states)
    {
        if (a == "quit") _quit();
        else if (a == "settings") _settings();
        else if (a == "restart-all") { foreach (var (id, st) in states) if (st is not (ModuleState.Disabled or ModuleState.NotInstalled)) _open(id); }
        else if (a.IndexOf(':') is var i and > 0) _open(a[(i + 1)..]);   // restart:/open:/enable: — the host's handler restarts, opens or enables as fits
    }

    private void ShowClassicMenu()
    {
        IntPtr menu = Native.CreatePopupMenu();
        try
        {
            var states = _states().ToList();
            foreach (var (id, st) in states) Native.AppendMenuW(menu, 0x1 /* GRAYED */, UIntPtr.Zero, $"{Label(id)}: {Text(st)}");
            var reopen = states.Where(k => k.Value is ModuleState.Stopped or ModuleState.Failed).Concat(states.Where(k => k.Value == ModuleState.Disabled)).Select(k => k.Key).ToList();   // «Abrir» o «Activar» solo donde hace falta
            if (reopen.Count > 0) Native.AppendMenuW(menu, 0x800 /* SEPARATOR */, UIntPtr.Zero, null);
            for (int i = 0; i < reopen.Count; i++) Native.AppendMenuW(menu, 0, (UIntPtr)(uint)(100 + i), $"{(states.First(k => k.Key == reopen[i]).Value == ModuleState.Disabled ? "Activar" : "Abrir")} {Label(reopen[i])}");
            Native.AppendMenuW(menu, 0x800 /* SEPARATOR */, UIntPtr.Zero, null);
            Native.AppendMenuW(menu, 0, (UIntPtr)2, "Ajustes de WinMac…");
            Native.AppendMenuW(menu, 0, (UIntPtr)1, "Salir de WinMac");
            Native.GetCursorPos(out var p); Native.SetForegroundWindow(_hwnd);   // requisito documentado de TrackPopupMenu
            int cmd = Native.TrackPopupMenuEx(menu, 0x100 | 0x80 | 0x20 | 0x2 /* RETURNCMD|NONOTIFY|BOTTOMALIGN|RIGHTBUTTON */, p.X, p.Y, _hwnd, IntPtr.Zero);
            Native.PostMessage(_hwnd, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 1) _quit();
            else if (cmd == 2) _settings();
            else if (cmd >= 100 && cmd - 100 < reopen.Count) _open(reopen[cmd - 100]);
        }
        catch (Exception e) { _log("[host] el menú de la bandeja falló: " + e.Message); }
        finally { Native.DestroyMenu(menu); }
    }

    public void RemoveIcon()
    {
        if (!_added) return;
        var d = Data(0); Native.Shell_NotifyIconW(Native.NIM_DELETE, ref d); _added = false;
    }

    public void Dispose() { _panel?.Dispose(); RemoveIcon(); if (_hwnd != IntPtr.Zero) { Native.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; } }
}
