using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Host.App;

/// <summary>
/// ¿Se ejecuta este proceso con identidad de paquete MSIX? (Fase 3.2). Un MSIX de escritorio «de plena confianza» sigue siendo un proceso Win32 normal, pero Windows
/// redirige HKCU\Run (no se honra) y, salvo que el manifiesto lo desactive, las escrituras de registro/AppData: por eso el inicio automático usa <c>StartupTask</c> aquí.
/// Fuera de un paquete (MSI, zip, CI) devuelve false y no cambia nada.
/// </summary>
internal static class PackageIdentity
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? name);
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700, ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>Nombre completo del paquete, o null si no se ejecuta empaquetado.</summary>
    public static string? FullName
    {
        get
        {
            try
            {
                int len = 0; int rc = GetCurrentPackageFullName(ref len, null);
                if (rc == APPMODEL_ERROR_NO_PACKAGE) return null;
                if (rc != ERROR_INSUFFICIENT_BUFFER || len <= 0) return null;
                var sb = new StringBuilder(len); return GetCurrentPackageFullName(ref len, sb) == 0 ? sb.ToString() : null;
            }
            catch (Exception) { return null; }
        }
    }

    public static bool IsPackaged => FullName is not null;

    /// <summary>¿Windows inició este proceso por la tarea de inicio de sesión del paquete (StartupTask)? Solo tiene sentido empaquetado; cualquier fallo = false (arranque normal).</summary>
    public static bool IsStartupTaskActivation()
    {
        if (!IsPackaged) return false;
        try { return Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()?.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask; }
        catch (Exception) { return false; }
    }
}
