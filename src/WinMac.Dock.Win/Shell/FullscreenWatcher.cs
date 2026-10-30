using WinMac.Dock.Win.Interop;

namespace WinMac.Dock.Win.Shell;

/// <summary>Detecta juegos/vídeo a pantalla completa con SHQueryUserNotificationState (documentada) para que el dock se aparte. Consulta barata; llamar ~1 Hz.</summary>
public static class FullscreenWatcher
{
    // QUNS_BUSY=2 (pantalla completa/ocupado), QUNS_RUNNING_D3D_FULL_SCREEN=3, QUNS_PRESENTATION_MODE=4, QUNS_APP=7 (UWP a pantalla completa)
    public static bool ShouldSuppress()
    {
        try { return Native.SHQueryUserNotificationState(out int s) == 0 && s is 2 or 3 or 4 or 7; }
        catch (Exception) { return false; }
    }
}
