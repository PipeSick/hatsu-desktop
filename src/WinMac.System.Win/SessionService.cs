using System.Diagnostics;
using System.Runtime.InteropServices;
using WinMac.SystemControl;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Acciones de sesión y de sistema sin administrador: bloquear, suspender, cerrar sesión, reiniciar y apagar (<c>LockWorkStation</c>, <c>SetSuspendState</c>, <c>ExitWindowsEx</c> sin forzar,
/// de modo que las apps pueden avisar de lo no guardado), abrir Configuración/Tienda y forzar la salida de la app activa con salvaguardas. La CONFIRMACIÓN la pide quien llama.
/// </summary>
public sealed class SessionService : ISessionService
{
    public ServiceResult Run(SystemActionKind action) => ServiceResult.Guard(() =>
    {
        switch (action)
        {
            case SystemActionKind.About or SystemActionKind.Settings or SystemActionKind.Store:
                return Shell(SystemActions.Get(action).SettingsUri!);
            case SystemActionKind.Lock:
                return Native.LockWorkStation() ? ServiceResult.Ok : ServiceResult.Failed("No se pudo bloquear la pantalla");
            case SystemActionKind.Sleep:
                // hibernate:false → suspender. Si Windows tiene la hibernación activada y no hay suspensión disponible, puede hibernar: es el comportamiento de Windows.
                return Native.SetSuspendState(false, false, false) ? ServiceResult.Ok : ServiceResult.Failed("Windows no pudo suspender el equipo");
            case SystemActionKind.LogOff: return Exit(0x0u);
            case SystemActionKind.Restart: return Exit(0x2u);
            case SystemActionKind.Shutdown: return Exit(0x8u);
            case SystemActionKind.ForceQuit: return ForceQuitForeground();
            default: return ServiceResult.Unavailable("Acción desconocida");
        }
    }, "Acción del sistema");

    private static ServiceResult Shell(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); return ServiceResult.Ok; }
        catch (Exception e) { return ServiceResult.Failed($"No se pudo abrir {uri}: {e.Message}"); }
    }

    private static ServiceResult Exit(uint flags)
    {
        if (!EnableShutdownPrivilege()) return ServiceResult.Denied("Windows no concedió el permiso de apagado a este usuario");
        return Native.ExitWindowsEx(flags, 0x80000000 /* motivo planificado */) ? ServiceResult.Ok : ServiceResult.Failed($"Windows rechazó la operación (error {Marshal.GetLastWin32Error()})");
    }

    private static bool EnableShutdownPrivilege()
    {
        if (!Native.OpenProcessToken(Native.GetCurrentProcess(), 0x28 /* ADJUST_PRIVILEGES | QUERY */, out var token)) return false;
        try
        {
            if (!Native.LookupPrivilegeValueW(null, "SeShutdownPrivilege", out long luid)) return false;
            var tp = new Native.TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 /* SE_PRIVILEGE_ENABLED */ };
            return Native.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally { Native.CloseHandle(token); }
    }

    /// <summary>Nombre del proceso de la ventana en primer plano (para el texto de confirmación); null si no hay o está protegido.</summary>
    public static string? ForegroundProcessName()
    {
        try
        {
            var h = Native.GetForegroundWindow(); if (h == IntPtr.Zero) return null;
            Native.GetWindowThreadProcessId(h, out uint pid); if (pid == (uint)Environment.ProcessId) return null;
            using var p = Process.GetProcessById((int)pid); return ForceQuitGuard.IsAllowed(p.ProcessName) ? p.ProcessName : null;
        }
        catch (Exception) { return null; }
    }

    public ServiceResult ForceQuitForeground() => ServiceResult.Guard(() =>
    {
        var h = Native.GetForegroundWindow(); if (h == IntPtr.Zero) return ServiceResult.Unavailable("No hay una app activa");
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid == (uint)Environment.ProcessId) return ServiceResult.Unavailable("La app activa es WinMac");
        using var p = Process.GetProcessById((int)pid);
        if (!ForceQuitGuard.IsAllowed(p.ProcessName)) return ServiceResult.Denied($"«{p.ProcessName}» es parte de Windows o de WinMac: no se cierra a la fuerza");
        p.Kill(); return ServiceResult.Ok;   // solo el proceso de la ventana activa, no su árbol
    }, "Forzar salida");

    /// <summary>Abre el Centro de notificaciones NATIVO de Windows (atajo oficial Win+N). WinMac no reproduce su contenido: leerlo exige identidad de paquete.</summary>
    public ServiceResult OpenNotificationCenter() => ServiceResult.Guard(() =>
    {
        const byte VkLwin = 0x5B, VkN = 0x4E; const uint KeyUp = 0x2;
        Native.keybd_event(VkLwin, 0, 0, UIntPtr.Zero); Native.keybd_event(VkN, 0, 0, UIntPtr.Zero);
        Native.keybd_event(VkN, 0, KeyUp, UIntPtr.Zero); Native.keybd_event(VkLwin, 0, KeyUp, UIntPtr.Zero);
        return ServiceResult.Ok;
    }, "Centro de notificaciones");
}
