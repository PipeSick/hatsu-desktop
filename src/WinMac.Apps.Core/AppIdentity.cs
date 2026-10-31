namespace WinMac.Apps;

public enum AppKind { Win32, Packaged }

/// <summary>
/// Identidad ESTABLE de una aplicación. No depende solo de la ruta del ejecutable: las apps empaquetadas (MSIX/UWP) se identifican por su AppUserModelID (AUMID)
/// y se activan con «shell:AppsFolder\&lt;AUMID&gt;», no ejecutando su .exe.
///   Id Win32     = "exe:"   + ruta normalizada (mayúsculas, sin barra final, variables de entorno expandidas)
///   Id empaquetada = "aumid:" + AUMID en minúsculas
/// </summary>
public sealed record AppIdentity(string Id, AppKind Kind, string DisplayName, string? ExePath = null, string? Aumid = null)
{
    public static string NormPath(string path) => Environment.ExpandEnvironmentVariables(path).Replace('/', '\\').Trim().TrimEnd('\\').ToUpperInvariant();

    public static string StemTitle(string path)
    {
        string file = path.Replace('/', '\\'); int i = file.LastIndexOf('\\'); if (i >= 0) file = file[(i + 1)..];
        int dot = file.LastIndexOf('.'); if (dot > 0) file = file[..dot];
        return file.Length == 0 ? path : char.ToUpperInvariant(file[0]) + file[1..];
    }

    public static AppIdentity FromExe(string path, string? displayName = null) =>
        new("exe:" + NormPath(path), AppKind.Win32, displayName ?? StemTitle(path), path);

    public static AppIdentity FromAumid(string aumid, string? displayName = null, string? exePath = null) =>
        new("aumid:" + aumid.Trim().ToLowerInvariant(), AppKind.Packaged, displayName ?? aumid, exePath, aumid.Trim());

    /// <summary>Recompone una identidad a partir de lo guardado en la configuración (Id puede faltar en configuraciones antiguas).</summary>
    public static AppIdentity? From(string? id, string? path, string? aumid, string? title)
    {
        if (!string.IsNullOrWhiteSpace(aumid)) return FromAumid(aumid!, title, path);
        if (!string.IsNullOrWhiteSpace(id) && id!.StartsWith("aumid:", StringComparison.Ordinal)) return FromAumid(id[6..], title, path);
        if (!string.IsNullOrWhiteSpace(path)) return FromExe(path!, title);
        return null;
    }

    /// <summary>Misma aplicación: mismo Id, o mismo ejecutable (una app anclada por ruta que se ejecuta con otra forma de identidad).</summary>
    public bool SameApp(AppIdentity? other) =>
        other is not null && (string.Equals(Id, other.Id, StringComparison.Ordinal) ||
            (ExePath is not null && other.ExePath is not null && string.Equals(NormPath(ExePath), NormPath(other.ExePath), StringComparison.Ordinal)));

    /// <summary>Destino de activación: empaquetada → shell:AppsFolder\AUMID; Win32 → ruta del ejecutable.</summary>
    public string? LaunchTarget => Aumid is not null ? @"shell:AppsFolder\" + Aumid : ExePath is not null ? Environment.ExpandEnvironmentVariables(ExePath) : null;
}
