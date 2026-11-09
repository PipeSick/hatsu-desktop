namespace WinMac.Host;

/// <summary>
/// Argumentos efectivos del host (Fase 3.2). Empaquetado como MSIX, Windows inicia la <c>StartupTask</c> SIN argumentos (el manifiesto no puede pasarlos), así que el host
/// añade <c>--background</c> él mismo cuando detecta esa activación. Fuera de un paquete (MSI, zip) nunca se llama con <paramref name="startupTaskActivation"/> = true: el
/// inicio automático clásico (HKCU\Run) ya lleva <c>--background</c> en su valor y no cambia nada.
/// </summary>
public static class LaunchArguments
{
    public const string Background = "--background";

    public static string[] Effective(string[] args, bool startupTaskActivation)
        => startupTaskActivation && !args.Contains(Background, StringComparer.Ordinal) ? [.. args, Background] : args;
}
