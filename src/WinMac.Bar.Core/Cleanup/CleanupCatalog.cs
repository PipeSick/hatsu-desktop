namespace WinMac.Bar.Cleanup;

/// <summary>
/// Categorías de la limpieza (conservadoras y documentadas). Solo carpetas del PERFIL del usuario que las apps recrean solas; nada de documentos, descargas, fotos ni proyectos,
/// nada del registro, nada que exija permisos elevados. Lo que no cumple eso aparece como «solo revisión», con el motivo.
/// </summary>
public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupCategory> Build(IReadOnlyDictionary<string, string> env)
    {
        string Get(string k) => env.TryGetValue(k, out var v) ? v : "";
        string local = Get("LOCALAPPDATA"), temp = Get("TEMP");
        var d1 = TimeSpan.FromDays(1); var d7 = TimeSpan.FromDays(7); var d14 = TimeSpan.FromDays(14);
        string[] R(params string[] p) => p.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        string J(string a, string b) => string.IsNullOrWhiteSpace(a) ? "" : a.TrimEnd('\\') + "\\" + b;
        return
        [
            new("user-temp", "Archivos temporales del usuario", "Archivos que las apps dejan en la carpeta temporal y no vuelven a usar. Solo los que llevan más de 24 horas sin modificarse; los que siguen en uso se omiten.", CleanupKind.Folder, R(temp), d1),
            new("inet-cache", "Caché de internet de Windows", "Copias de páginas e imágenes de WebView/Internet Explorer; Windows las vuelve a crear si hacen falta.", CleanupKind.Folder, R(J(local, "Microsoft\\Windows\\INetCache")), d1),
            new("shader-cache", "Caché de sombreadores gráficos", "Sombreadores DirectX precompilados; se regeneran solos la próxima vez que una app los pida.", CleanupKind.Folder, R(J(local, "D3DSCache")), d1),
            new("crash-dumps", "Volcados de errores de apps", "Volcados de memoria de cierres inesperados con más de 7 días; solo sirven para diagnosticar.", CleanupKind.Folder, R(J(local, "CrashDumps")), d7),
            new("wer-reports", "Informes de errores de Windows", "Informes de error ya enviados o en cola con más de 7 días, del propio usuario.", CleanupKind.Folder, R(J(local, "Microsoft\\Windows\\WER\\ReportArchive"), J(local, "Microsoft\\Windows\\WER\\ReportQueue")), d7),
            new("winmac-logs", "Registros antiguos de Hatsu Desktop", "Registros (.log) de Hatsu Desktop con más de 14 días. Los actuales no se tocan.", CleanupKind.Folder, R(J(local, "WinMac\\logs")), d14, [".log", ".txt"]),
            new("recycle-bin", "Papelera de reciclaje", "Elementos que ya borraste. Vaciarla es definitivo: se pide una confirmación aparte.", CleanupKind.RecycleBin, [], TimeSpan.Zero),
            new("setup-leftovers", "Restos de instalaciones y actualizaciones de Windows", "", CleanupKind.ReviewOnly, [], TimeSpan.Zero, WhyNotCleanable: "Requiere permisos de administrador (C:\\Windows\\Temp, SoftwareDistribution). Hatsu Desktop no eleva privilegios: usa «Liberador de espacio en disco» de Windows."),
            new("orphans", "Restos de aplicaciones desinstaladas", "", CleanupKind.ReviewOnly, [], TimeSpan.Zero, WhyNotCleanable: "No se puede demostrar de forma fiable que una carpeta esté huérfana: borrarla por su nombre podría destruir datos de una app instalada. Revisión manual."),
        ];
    }

    public static IReadOnlyDictionary<string, string> CurrentEnv()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in new[] { "USERPROFILE", "LOCALAPPDATA", "TEMP", "SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramData" }) if (Environment.GetEnvironmentVariable(k) is { Length: > 0 } v) d[k] = v;
        return d;
    }
}
