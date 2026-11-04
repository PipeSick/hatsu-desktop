using WinMac.SystemControl;

namespace WinMac.Bar;

/// <summary>Icono del diálogo de confirmación (los mismos nombres que <c>WinMac.Dock.ConfirmKind</c>: el anfitrión los convierte por nombre).</summary>
public enum ConfirmIcon { Warning, Power, Restart, SignOut, Trash, ForceQuit, Info }

public static class ConfirmIcons
{
    /// <summary>Icono de cada acción del menú Apple (solo cambia el aspecto del diálogo; la acción y su confirmación son las de siempre).</summary>
    public static ConfirmIcon ForAction(SystemActionKind k) => k switch
    {
        SystemActionKind.Shutdown => ConfirmIcon.Power,
        SystemActionKind.Restart => ConfirmIcon.Restart,
        SystemActionKind.LogOff => ConfirmIcon.SignOut,
        SystemActionKind.ForceQuit => ConfirmIcon.ForceQuit,
        _ => ConfirmIcon.Warning,
    };
}
