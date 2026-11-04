namespace WinMac.Bar.Tray;

public enum TrayMenuPhase { Waiting, MenuOpen, Finished }

/// <summary>
/// It7: ciclo de vida del desplegable «Apps en segundo plano» durante un clic derecho. El panel NO se cierra al reenviar el clic (cerrarlo antes devolvía el foco a otra ventana
/// justo cuando la app iba a mostrar su menú, y el menú quedaba sin foco, sin cerrarse al pulsar fuera). Se mantiene visible mientras el menú de la app existe:
/// espera a que aparezca (<see cref="AppearTimeoutMs"/>), lo sigue mientras esté visible (con un margen para el paso entre submenús) y termina al cerrarse.
/// Si la app no muestra ningún menú, el panel se cierra igual tras la espera. Puro: el adaptador de Windows solo dice si hay un menú visible.
/// </summary>
public sealed class TrayMenuWatch
{
    public const int AppearTimeoutMs = 1500, GoneGraceMs = 250, MaxOpenMs = 180_000;
    private int? _goneSince;
    public TrayMenuPhase Phase { get; private set; } = TrayMenuPhase.Waiting;

    public TrayMenuPhase Step(int elapsedMs, bool menuVisible)
    {
        switch (Phase)
        {
            case TrayMenuPhase.Waiting:
                if (menuVisible) Phase = TrayMenuPhase.MenuOpen;
                else if (elapsedMs >= AppearTimeoutMs) Phase = TrayMenuPhase.Finished;
                break;
            case TrayMenuPhase.MenuOpen:
                if (elapsedMs >= MaxOpenMs) { Phase = TrayMenuPhase.Finished; break; }
                if (menuVisible) _goneSince = null;
                else { _goneSince ??= elapsedMs; if (elapsedMs - _goneSince >= GoneGraceMs) Phase = TrayMenuPhase.Finished; }
                break;
        }

        return Phase;
    }
}
