using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMac.Host.App;

/// <summary>
/// WM-034: oculta la barra de tareas de Windows (Explorer: <c>Shell_TrayWnd</c> en la pantalla principal y <c>Shell_SecondaryTrayWnd</c> en las demás) mientras el ajuste está activo.
/// Sin cerrar, sustituir, inyectar ni parchear Explorer y sin ganchos: solo <c>ShowWindow</c> sobre SUS ventanas (comprobado por proceso: la Barra de WinMac tiene una ventana
/// propia con la misma clase) y la API documentada de AppBar (<c>SHAppBarMessage(ABM_SETSTATE)</c>) para activar temporalmente «Ocultar automáticamente», que es lo que hace que
/// Windows deje de reservar su franja (sin eso las ventanas maximizadas dejarían un hueco). Ese ajuste del usuario se guarda ANTES en <see cref="TaskbarLease"/> y se devuelve tal cual.
/// <para>Nada es permanente: ocultar una ventana no sobrevive a un reinicio del Explorador ni al cierre de sesión. Lo único que persiste es «Ocultar automáticamente», y por eso
/// el registro de recuperación lo restauran: este host al desactivar y al salir; el guardián (<c>--taskbar-guard</c>, proceso aparte) si el host muere o se cuelga;
/// el siguiente arranque si todo se cortó (apagado); y <c>WinMac.exe --restore-taskbar</c> a mano, que no necesita que WinMac arranque.</para>
/// Sin sondeo agresivo: una comprobación barata cada 2 s solo mientras está activo, «TaskbarCreated» para reaccionar enseguida, y un freno si Windows la vuelve a mostrar sin parar.
/// </summary>
internal sealed class TaskbarHider
{
    private readonly string _leasePath; private readonly Action<string> _log; private readonly string _exe;
    private readonly ReshowLimiter _limiter = new();
    private bool _hiddenByUs, _suspended; private DateTime _nextTick, _hideAt = DateTime.MaxValue; private Process? _guard;
    public string Status { get; private set; } = "";

    public TaskbarHider(string exe, Action<string> log, string? leasePath = null) { _exe = exe; _log = log; _leasePath = leasePath ?? TaskbarLease.DefaultPath; }

    /// <summary>Al arrancar el host: si quedó un registro de un WinMac que ya no está (cierre forzado, apagado), primero se restaura.</summary>
    public void RecoverStale()
    {
        var lease = TaskbarLease.Load(_leasePath);
        if (!TaskbarPolicy.StaleOnStart(lease, IsWinMacAlive, Environment.ProcessId)) return;
        _log("[barra de tareas] había quedado oculta por una sesión anterior de WinMac que no terminó bien: se restaura");
        Restore(lease!, _leasePath, _log); TaskbarLease.Delete(_leasePath);
    }

    /// <summary>Cada vuelta del bucle del host. Sin el ajuste activo y sin nada que restaurar no hace nada (ni lee ventanas).</summary>
    public void Tick(bool enabled, DateTime now)
    {
        if (!enabled && !_hiddenByUs) return;
        if (now < _nextTick) return;
        _nextTick = now.AddSeconds(2);
        try { Step(enabled, now); } catch (Exception e) { _log("[barra de tareas] error: " + e.Message); }
    }

    /// <summary>Explorer se reinició (TaskbarCreated): su barra nueva aparece visible; se vuelve a ocultar en cuanto termina de crearse (1,5 s).</summary>
    public void OnTaskbarCreated(bool enabled)
    {
        if (!enabled || _suspended) return;
        _log("[barra de tareas] Explorer recreó la barra de tareas: se volverá a ocultar");
        _hideAt = DateTime.UtcNow.AddSeconds(1.5); _limiter.Reset();
    }

    private void Step(bool enabled, DateTime now)
    {
        if (enabled && _hideAt != DateTime.MaxValue) { if (now < _hideAt) return; _hideAt = DateTime.MaxValue; }
        var bars = ExplorerTaskbars();
        bool leaseExists = File.Exists(_leasePath), anyVisible = bars.Any(IsWindowVisible);
        switch (TaskbarPolicy.Decide(enabled, leaseExists, _hiddenByUs, anyVisible, _suspended))
        {
            case TaskbarStep.Hide:
                if (bars.Count == 0) { Status = "No se encontró la barra de tareas de Explorer"; return; }
                if (_hiddenByUs && !_limiter.Allow(now)) { GiveUp("Windows la volvía a mostrar una y otra vez; se dejó visible para no parpadear. Desactiva y activa el ajuste para reintentar"); return; }
                if (!leaseExists) { new TaskbarLease(Environment.ProcessId, GetAutoHide(bars[0]), DateTime.UtcNow).Save(_leasePath); }   // SIEMPRE antes de tocar nada
                EnsureGuard();
                SetAutoHide(bars[0], true);
                foreach (var h in bars) ShowWindow(h, SW_HIDE);
                if (!_hiddenByUs) _log($"[barra de tareas] oculta en {bars.Count} pantalla(s)");
                _hiddenByUs = true; Status = bars.Count == 1 ? "Oculta" : $"Oculta en {bars.Count} pantallas";
                break;
            case TaskbarStep.Restore: RestoreNow(); break;
            case TaskbarStep.GiveUp: GiveUp("el guardián la restauró porque WinMac no respondía; vuelve a activar el ajuste para ocultarla"); break;
        }
    }

    private void GiveUp(string why)
    {
        _suspended = true; Status = "Suspendida: " + why;
        _log("[barra de tareas] se deja de ocultar en esta sesión: " + why);
        if (TaskbarLease.Load(_leasePath) is { } l) Restore(l, _leasePath, _log);
        else foreach (var h in ExplorerTaskbars()) ShowWindow(h, SW_SHOWNA);
        TaskbarLease.Delete(_leasePath); _hiddenByUs = false;
    }

    /// <summary>Desactivado o al salir: devuelve la barra y el ajuste del usuario al instante. Vuelve a permitir ocultar si se reactiva.</summary>
    public void RestoreNow()
    {
        var lease = TaskbarLease.Load(_leasePath);
        if (lease is not null) Restore(lease, _leasePath, _log);
        else if (_hiddenByUs) foreach (var h in ExplorerTaskbars()) ShowWindow(h, SW_SHOWNA);
        TaskbarLease.Delete(_leasePath);
        if (_hiddenByUs || lease is not null) _log("[barra de tareas] restaurada");
        _hiddenByUs = false; _suspended = false; _limiter.Reset(); Status = "";
    }

    /// <summary>Lo que tocamos de vuelta a su sitio: «Ocultar automáticamente» como estaba (si se sabe) y todas las barras visibles.</summary>
    private static void Restore(TaskbarLease lease, string leasePath, Action<string> log)
    {
        var bars = ExplorerTaskbars();
        if (bars.Count == 0) { log("[barra de tareas] Explorer no está en marcha: al iniciarse, su barra aparece visible"); }
        if (lease.KnowsOriginal && bars.Count > 0) SetAutoHide(bars[0], lease.OriginalAutoHide);
        foreach (var h in bars) ShowWindow(h, SW_SHOWNA);
        _ = leasePath;
    }

    // ───────────────────────────── guardián (proceso aparte)
    private void EnsureGuard()
    {
        if (_guard is { HasExited: false }) return;
        try
        {
            _guard?.Dispose();
            _guard = Process.Start(new ProcessStartInfo(_exe) { ArgumentList = { "--taskbar-guard", Environment.ProcessId.ToString() }, UseShellExecute = false, CreateNoWindow = true });
            _log("[barra de tareas] guardián iniciado (pid " + _guard?.Id + "): devuelve la barra si WinMac se cierra de golpe o deja de responder");
        }
        catch (Exception e) { _log("[barra de tareas] AVISO: no se pudo iniciar el guardián (" + e.Message + "); la barra se restaura al salir y en el próximo arranque, o con «WinMac.exe --restore-taskbar»"); }
    }

    /// <summary>
    /// <c>WinMac.exe --taskbar-guard &lt;pid&gt;</c>: espera al host. Si el host termina sin haber restaurado (el registro sigue ahí y es suyo), restaura. Si el host deja de
    /// responder ~20 s, restaura y borra el registro (el host, al volver, ve que ya no es dueño y no la oculta otra vez). Si el registro desaparece, el host restauró: termina.
    /// </summary>
    public static int RunGuard(int hostPid)
    {
        string path = TaskbarLease.DefaultPath; var log = GuardLog(path);
        Process? host; try { host = Process.GetProcessById(hostPid); } catch (Exception) { host = null; }
        int hung = 0;
        while (host is not null && !host.WaitForExit(2000))
        {
            var lease = TaskbarLease.Load(path);
            if (lease is null || lease.HostPid != hostPid) return 0;   // restaurada por el host (o es de otro host): nada que vigilar
            hung = HostResponds(hostPid) ? 0 : hung + 1;
            if (hung >= 10) { log($"el host {hostPid} no responde desde hace ~20 s: se restaura la barra de tareas"); Restore(lease, path, log); TaskbarLease.Delete(path); return 0; }
        }

        var last = TaskbarLease.Load(path);
        if (last is not null && last.HostPid == hostPid && TaskbarPolicy.GuardShouldRestore(last, hostAlive: false)) { log($"el host {hostPid} terminó sin restaurar la barra de tareas: se restaura"); Restore(last, path, log); TaskbarLease.Delete(path); }
        return 0;
    }

    /// <summary><c>WinMac.exe --restore-taskbar</c>: recuperación manual que no depende de que WinMac pueda arrancar ni de ningún proceso de fondo.</summary>
    public static int RestoreFromCommandLine(bool quiet)
    {
        string path = TaskbarLease.DefaultPath; var lease = TaskbarLease.Load(path);
        Restore(lease ?? new TaskbarLease(0, false, DateTime.MinValue), path, GuardLog(path)); TaskbarLease.Delete(path);
        string text = (lease is { KnowsOriginal: true } ? $"Barra de tareas restaurada («Ocultar automáticamente»: {(lease.OriginalAutoHide ? "activado" : "desactivado")}, como estaba)." : "Barra de tareas visible. WinMac no tenía registro del ajuste «Ocultar automáticamente»: no se cambió.")
            + "\n\nSi aun así no aparece: reinicia el Explorador de Windows o cierra sesión. Para que WinMac no vuelva a ocultarla, desactiva «Ocultar barra de tareas de Windows» en Ajustes › Escritorio y Dock.";
        try { Console.WriteLine(text); } catch (Exception) { }
        if (!quiet) MessageBoxW(IntPtr.Zero, text, "WinMac", 0x40 /* MB_ICONINFORMATION */);   // WinMac.exe no tiene consola: el aviso se ve igual al hacer doble clic en un acceso directo
        return 0;
    }

    private static Action<string> GuardLog(string leasePath) => m =>
    {
        try
        {
            string f = Path.Combine(Path.GetDirectoryName(leasePath)!, "logs", "taskbar-guard.log"); Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            if (File.Exists(f) && new FileInfo(f).Length > 256 * 1024) File.Delete(f);
            File.AppendAllText(f, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {m}{Environment.NewLine}");
        }
        catch (Exception) { }
    };

    private static bool HostResponds(int pid)
    {
        IntPtr win = IntPtr.Zero;
        EnumWindows((h, _) => { GetWindowThreadProcessId(h, out uint p); if (p == (uint)pid && ClassOf(h) == "WinMacHost") { win = h; return false; } return true; }, IntPtr.Zero);
        if (win == IntPtr.Zero) return true;   // sin ventana no se puede saber: no se acusa de colgado
        return SendMessageTimeoutW(win, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out _) != IntPtr.Zero;
    }

    private static bool IsWinMacAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); using var me = Process.GetCurrentProcess(); return !p.HasExited && string.Equals(p.ProcessName, me.ProcessName, StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    // ───────────────────────────── ventanas de Explorer
    /// <summary>Las barras de tareas de Explorer (todas las pantallas). Solo las del proceso del escritorio: nunca la ventana de bandeja propia de la Barra de WinMac.</summary>
    private static List<IntPtr> ExplorerTaskbars()
    {
        var list = new List<IntPtr>(); var shell = GetShellWindow(); if (shell == IntPtr.Zero) return list;
        GetWindowThreadProcessId(shell, out uint shellPid);
        EnumWindows((h, _) => { if (ClassOf(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") { GetWindowThreadProcessId(h, out uint p); if (p == shellPid) list.Add(h); } return true; }, IntPtr.Zero);
        list.Sort((a, b) => (ClassOf(a) == "Shell_TrayWnd" ? 0 : 1).CompareTo(ClassOf(b) == "Shell_TrayWnd" ? 0 : 1));   // la principal primero: es la que recibe ABM_SETSTATE
        return list;
    }

    private static string ClassOf(IntPtr h) { var sb = new char[64]; int n = GetClassNameW(h, sb, sb.Length); return n > 0 ? new string(sb, 0, n) : ""; }

    private static bool GetAutoHide(IntPtr tray) { var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = tray }; return (SHAppBarMessage(ABM_GETSTATE, ref d).ToInt64() & ABS_AUTOHIDE) != 0; }

    private static void SetAutoHide(IntPtr tray, bool on)
    {
        if (GetAutoHide(tray) == on) return;
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = tray, lParam = (IntPtr)(on ? ABS_AUTOHIDE : ABS_ALWAYSONTOP) };
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    private const int SW_HIDE = 0, SW_SHOWNA = 8; private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10, SMTO_ABORTIFHUNG = 2; private const long ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct APPBARDATA { public uint cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, char[] name, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr h, string text, string caption, uint type);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeoutW(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
}
