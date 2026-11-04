namespace WinMac.Bar.Tray;

public enum TrayMenuPhase { Waiting, MenuOpen, Undetected, Finished }

/// <summary>Quién tiene el primer plano: el propio panel, la app a la que se reenvió el clic, u otra cosa.</summary>
public enum ForegroundOwner { Panel, Target, Other }

/// <summary>Lo que el adaptador de Windows observa de la interacción (sin interpretar nada).</summary>
/// <param name="MenuWindow">Hay un menú clásico de Windows (clase «#32768») visible de otro proceso.</param>
/// <param name="MenuMode">El hilo de la app (o el del primer plano) está en modo menú (GUI_INMENUMODE / GUI_POPUPMENUMODE de GetGUIThreadInfo): lo cubre cualquier menú que use el bucle de menús de Windows.</param>
/// <param name="NewPopup">La app abrió una ventana emergente nueva (sin barra de título, de herramientas o tipo popup) mientras tenía el foco: menús propios (Qt, Electron, XAML…).</param>
public readonly record struct TrayMenuSignals(bool MenuWindow, bool MenuMode, bool NewPopup, ForegroundOwner Foreground)
{
    public bool AnyMenu => MenuWindow || MenuMode || NewPopup;
}

/// <summary>
/// Ciclo de vida del desplegable «Apps en segundo plano» durante un clic derecho (It7, corregido en It8). El panel NO se cierra al reenviar el clic y se mantiene visible mientras la
/// interacción esté activa; después recupera el comportamiento habitual.
/// <list type="bullet">
/// <item><b>Esperando</b> (hasta <see cref="AppearTimeoutMs"/>): aún no hay señal de menú.</item>
/// <item><b>Menú abierto</b>: se mantiene mientras haya CUALQUIER señal; termina cuando faltan todas durante <see cref="GoneGraceMs"/> (el margen cubre el paso entre submenús).</item>
/// <item><b>Sin detectar</b>: si pasa la espera sin ninguna señal (un menú que Windows no deja observar), NO se cierra por tiempo —cerrarlo mientras el menú seguía abierto era el fallo de la iteración 7—:
/// el panel sigue mientras el primer plano sea el panel o la app, y termina cuando el usuario pasa a otra cosa (<see cref="ForegroundOwner.Other"/> durante el margen).</item>
/// </list>
/// Límite de seguridad: <see cref="MaxOpenMs"/> sobre el menú detectado, por si una señal quedara pegada.
/// </summary>
public sealed class TrayMenuWatch
{
    public const int AppearTimeoutMs = 2500, GoneGraceMs = 600, MaxOpenMs = 600_000;
    private int? _goneSince, _otherSince;
    public TrayMenuPhase Phase { get; private set; } = TrayMenuPhase.Waiting;

    public TrayMenuPhase Step(int elapsedMs, TrayMenuSignals s)
    {
        switch (Phase)
        {
            case TrayMenuPhase.Waiting:
                if (s.AnyMenu) Phase = TrayMenuPhase.MenuOpen;
                else if (elapsedMs >= AppearTimeoutMs) Phase = s.Foreground == ForegroundOwner.Other ? TrayMenuPhase.Finished : TrayMenuPhase.Undetected;
                break;
            case TrayMenuPhase.MenuOpen:
                if (elapsedMs >= MaxOpenMs) { Phase = TrayMenuPhase.Finished; break; }
                if (s.AnyMenu) _goneSince = null;
                else { _goneSince ??= elapsedMs; if (elapsedMs - _goneSince >= GoneGraceMs) Phase = TrayMenuPhase.Finished; }
                break;
            case TrayMenuPhase.Undetected:
                if (s.AnyMenu) { Phase = TrayMenuPhase.MenuOpen; _goneSince = null; break; }   // el menú apareció tarde
                if (s.Foreground != ForegroundOwner.Other) _otherSince = null;
                else { _otherSince ??= elapsedMs; if (elapsedMs - _otherSince >= GoneGraceMs) Phase = TrayMenuPhase.Finished; }
                break;
        }

        return Phase;
    }
}
