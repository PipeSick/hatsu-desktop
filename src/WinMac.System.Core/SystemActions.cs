namespace WinMac.SystemControl;

public enum SystemActionKind { About, Settings, Store, ForceQuit, Sleep, Restart, Shutdown, LogOff, Lock }

/// <summary>Lo que el menú Apple muestra de cada acción y si necesita confirmación. Windows ejecuta estas acciones sin administrador.</summary>
public sealed record SystemActionInfo(SystemActionKind Kind, string Label, bool Destructive, string? ConfirmTitle = null, string? ConfirmText = null, string? SettingsUri = null);

public static class SystemActions
{
    public static readonly IReadOnlyList<SystemActionInfo> All =
    [
        new(SystemActionKind.About, "Acerca de este PC", false, SettingsUri: "ms-settings:about"),
        new(SystemActionKind.Settings, "Configuración del sistema…", false, SettingsUri: "ms-settings:"),
        new(SystemActionKind.Store, "Microsoft Store…", false, SettingsUri: "ms-windows-store:"),
        new(SystemActionKind.ForceQuit, "Forzar salida de la app activa…", true, "Forzar salida", "Se cerrará de inmediato «{0}» y se perderá lo que no esté guardado. ¿Continuar?"),
        new(SystemActionKind.Sleep, "Suspender", false),
        new(SystemActionKind.Restart, "Reiniciar…", true, "Reiniciar", "Se reiniciará el equipo. Las apps abiertas se cerrarán. ¿Continuar?"),
        new(SystemActionKind.Shutdown, "Apagar…", true, "Apagar", "Se apagará el equipo. Las apps abiertas se cerrarán. ¿Continuar?"),
        new(SystemActionKind.LogOff, "Cerrar sesión…", true, "Cerrar sesión", "Se cerrará tu sesión y las apps abiertas se cerrarán. ¿Continuar?"),
        new(SystemActionKind.Lock, "Bloquear pantalla", false),
    ];

    public static SystemActionInfo Get(SystemActionKind k) => All.First(a => a.Kind == k);

    /// <summary>¿Hay que pedir confirmación? Las destructivas siempre, salvo que el usuario la haya desactivado en los ajustes (no aplica a Forzar salida).</summary>
    public static bool NeedsConfirmation(SystemActionKind k, bool confirmDestructive) =>
        Get(k).Destructive && (confirmDestructive || k == SystemActionKind.ForceQuit);
}

/// <summary>Salvaguardas de «Forzar salida»: nunca matar el shell de Windows, procesos críticos ni los propios módulos de WinMac.</summary>
public static class ForceQuitGuard
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "csrss", "winlogon", "wininit", "services", "lsass", "smss", "dwm", "svchost", "system", "fontdrvhost", "sihost", "ctfmon",
        "winmac", "winmac.bar.app", "winmac.dock.app", "notchwin.app", "taskmgr",
    };

    /// <summary>Acepta el nombre con o sin «.exe» (<c>Process.ProcessName</c> viene sin extensión y puede llevar puntos, p. ej. «WinMac.Bar.App»).</summary>
    public static bool IsAllowed(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var n = processName.Trim(); if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return !Protected.Contains(n);
    }
}
