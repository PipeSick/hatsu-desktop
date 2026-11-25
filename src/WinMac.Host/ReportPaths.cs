using WinMac.Host.App;

namespace WinMac.Host.App;

/// <summary>Rutas de los informes y registros según haya paquete MSIX o no (ver <see cref="PackagePaths"/>).</summary>
internal static class ReportPaths
{
    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string ReportsDir => PackagePaths.ReportsDir(Local, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), PackageIdentity.FullName);

    /// <summary>Dónde están DE VERDAD los registros (host.log, bar.log…) en disco, para decírselo al usuario.</summary>
    public static string RealLogsDir => PackagePaths.RealLogsDir(Local, PackageIdentity.FullName);
}
