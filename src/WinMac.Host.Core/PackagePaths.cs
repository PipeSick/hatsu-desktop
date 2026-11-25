namespace WinMac.Host;

/// <summary>
/// Con el paquete de la Store (MSIX) Windows redirige las escrituras en AppData: el programa «ve» <c>AppData\Local\Hatsu\logs</c>, pero en disco el archivo está en
/// <c>AppData\Local\Packages\&lt;familia&gt;\LocalCache\Local\Hatsu\logs</c>. Cualquier OTRO proceso (el Bloc de notas moderno, el Explorador) no ve la ruta virtual:
/// «no se encuentra la ruta». Por eso los informes que el usuario debe abrir se guardan fuera de AppData cuando hay paquete. Puro: sin Windows.
/// </summary>
public static class PackagePaths
{
    /// <summary>De «PipeSick.HatsuDesktop_2.0.10.0_x64__83tr6qzbbarj8» saca la familia «PipeSick.HatsuDesktop_83tr6qzbbarj8»; null si no tiene ese formato.</summary>
    public static string? FamilyName(string? packageFullName)
    {
        if (string.IsNullOrWhiteSpace(packageFullName)) return null;
        var parts = packageFullName.Split('_');
        return parts.Length == 5 && parts[0].Length > 0 && parts[4].Length > 0 ? parts[0] + "_" + parts[4] : null;
    }

    /// <summary>Carpeta real (en disco) de los registros con paquete; sin paquete es la de siempre.</summary>
    public static string RealLogsDir(string localAppData, string? packageFullName, string appFolder = "Hatsu") =>
        FamilyName(packageFullName) is { } fam ? Path.Combine(localAppData, "Packages", fam, "LocalCache", "Local", appFolder, "logs") : Path.Combine(localAppData, appFolder, "logs");

    /// <summary>Dónde se guardan los informes que el usuario abrirá: con paquete, Documentos\Hatsu Desktop (visible para todos los procesos); sin paquete, la carpeta de registros.</summary>
    public static string ReportsDir(string localAppData, string documents, string? packageFullName, string appFolder = "Hatsu") =>
        FamilyName(packageFullName) is not null ? Path.Combine(documents, "Hatsu Desktop") : Path.Combine(localAppData, appFolder, "logs");
}
