namespace WinMac.Host;

public enum AutostartState
{
    /// <summary>No hay valor: el inicio automático está desactivado. NUNCA se crea uno sin que el usuario lo pida.</summary>
    NotConfigured,
    /// <summary>El valor existe pero no se entiende: no se toca.</summary>
    Unreadable,
    /// <summary>Ya apunta a esta instalación.</summary>
    Current,
    /// <summary>Apunta a un archivo que ya no existe (carpeta borrada o movida).</summary>
    Orphan,
    /// <summary>Apunta al ejecutable antiguo (<c>WinMac.exe</c>) de ESTA MISMA carpeta: se extrajo la versión nueva encima de la antigua (actualización en el sitio).</summary>
    UpgradeInPlace,
    /// <summary>Apunta a OTRA instalación que sigue existiendo (p. ej. el WinMac antiguo en otra carpeta). Solo cambia si el usuario lo decide.</summary>
    OtherInstall,
}

/// <param name="StoredExe">La ruta del ejecutable guardada, si se entendió.</param>
public sealed record AutostartAssessment(AutostartState State, string? StoredExe);

/// <summary>
/// Inicio automático de Hatsu Desktop (antes WinMac): sigue siendo la MISMA clave de usuario (<c>HKCU\…\Run</c>, valor «WinMac»: identificador interno persistente), así que una instalación
/// antigua se actualiza sin crear una segunda entrada. Qué se toca y cuándo:
/// <list type="bullet">
/// <item><b>Automático y seguro</b> (al arrancar): solo si el valor existe y es <i>huérfano</i> (apunta a algo que ya no existe) o es el <c>WinMac.exe</c> de ESTA carpeta (actualización en el sitio).</item>
/// <item><b>Solo con consentimiento</b> (botón de Ajustes con confirmación, o <c>--adopt-autostart</c>): si apunta a OTRA instalación que sigue viva. Una ejecución de prueba nunca lo cambia.</item>
/// <item><b>Nunca</b>: crear el valor si no existía (desactivado sigue desactivado), tocar un valor ilegible, ni borrar carpetas.</item>
/// </list>
/// Idempotente. Conserva los argumentos del valor (si no había, usa <c>--background</c>). Puro: se prueba sin registro.
/// </summary>
public static class AutostartMigration
{
    public const string Args = "--background";
    /// <summary>Nombres del ejecutable del host en versiones anteriores a Hatsu Desktop.</summary>
    public static readonly string[] LegacyHostNames = ["WinMac.exe"];

    /// <summary>Ruta del ejecutable de un valor «"C:\…\X.exe" args» (con comillas) o «C:\ruta con espacios\X.exe args» (sin ellas: hasta el primer «.exe»). Null si no se entiende.</summary>
    public static string? ExePathOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string v = value.Trim();
        if (v.StartsWith('"')) { int end = v.IndexOf('"', 1); return end > 1 ? v[1..end] : null; }
        int i = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return i > 0 ? v[..(i + 4)] : null;
    }

    /// <summary>Argumentos del valor guardado (lo que va después del ejecutable); null si no hay.</summary>
    public static string? ArgsOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || ExePathOf(value) is not { } exe) return null;
        string v = value.Trim(); string rest = v.StartsWith('"') ? v[(v.IndexOf('"', 1) + 1)..] : v[exe.Length..];
        rest = rest.Trim(); return rest.Length == 0 ? null : rest;
    }

    public static string ValueFor(string exe, string? args = null) => $"\"{exe}\" {(string.IsNullOrWhiteSpace(args) ? Args : args)}";

    // Separadores de Windows y de Unix por igual (el host solo corre en Windows, pero las pruebas también corren en Linux).
    private static string DirOf(string p) { int i = p.LastIndexOfAny(['\\', '/']); return i < 0 ? "" : p[..i].TrimEnd('\\', '/'); }
    private static string NameOf(string p) { int i = p.LastIndexOfAny(['\\', '/']); return i < 0 ? p : p[(i + 1)..]; }

    private static string Full(string p) { try { return Path.GetFullPath(p); } catch (Exception) { return p; } }

    public static AutostartAssessment Assess(string? stored, string currentExe, Func<string, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new(AutostartState.NotConfigured, null);
        var old = ExePathOf(stored);
        if (old is null || string.IsNullOrWhiteSpace(currentExe)) return new(AutostartState.Unreadable, null);
        string o = Full(old), c = Full(currentExe);
        if (string.Equals(o, c, StringComparison.OrdinalIgnoreCase)) return new(AutostartState.Current, old);
        if (!exists(old)) return new(AutostartState.Orphan, old);
        bool sameDir = string.Equals(DirOf(o), DirOf(c), StringComparison.OrdinalIgnoreCase);
        if (sameDir && LegacyHostNames.Contains(NameOf(o), StringComparer.OrdinalIgnoreCase)) return new(AutostartState.UpgradeInPlace, old);
        return new(AutostartState.OtherInstall, old);
    }

    /// <summary>Lo que se hace SOLO, sin preguntar: reparar un valor huérfano o actualizar en el sitio. Devuelve el valor nuevo o null si no hay nada que hacer.</summary>
    public static string? Plan(string? stored, string currentExe, Func<string, bool> exists) =>
        Assess(stored, currentExe, exists).State is AutostartState.Orphan or AutostartState.UpgradeInPlace ? ValueFor(currentExe, ArgsOf(stored)) : null;

    /// <summary>Adopción EXPLÍCITA (el usuario lo pidió): además de lo automático, cambia el valor que apunta a otra instalación viva. Nunca crea uno nuevo.</summary>
    public static string? PlanAdopt(string? stored, string currentExe, Func<string, bool> exists) =>
        Assess(stored, currentExe, exists).State is AutostartState.Orphan or AutostartState.UpgradeInPlace or AutostartState.OtherInstall ? ValueFor(currentExe, ArgsOf(stored)) : null;
}
