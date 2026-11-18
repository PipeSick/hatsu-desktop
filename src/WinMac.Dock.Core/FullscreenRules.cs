namespace WinMac.Dock;

/// <summary>
/// Cuándo el Dock (y la Barra) se apartan por una app a pantalla completa. Puro: el lector de Windows reúne los datos y esta regla decide.
/// Windows marca como «pantalla completa» (estado 3) o «ocupado» (2, 7) a apps que no lo están (Steam, ChatGPT…) y a veces deja el estado pegado cuando la ventana se minimiza,
/// así que ningún estado basta por sí solo: la ventana en primer plano tiene que cubrir el monitor entero, no estar minimizada, no ser el escritorio y estar en ESTE monitor.
/// El modo presentación (4) lo activa el usuario a propósito y vale para todos los monitores.
/// </summary>
public static class FullscreenRules
{
    public const int Busy = 2, D3DFullScreen = 3, Presentation = 4, App = 7;

    public static bool ShouldSuppress(int state, bool hasForeground, bool minimized, bool isShell, bool coversItsMonitor, bool onThisMonitor)
    {
        if (state == Presentation) return true;
        if (state is not (Busy or D3DFullScreen or App)) return false;
        if (!hasForeground || minimized || isShell || !onThisMonitor) return false;
        return coversItsMonitor;
    }
}
