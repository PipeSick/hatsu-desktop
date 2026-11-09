namespace WinMac.Bar.Cleanup;

/// <summary>
/// Barandilla de rutas: ninguna limpieza actúa fuera de su raíz autorizada ni dentro de carpetas del usuario o del sistema. Comparación por segmentos (no por prefijo de texto:
/// «C:\Temp2» no está dentro de «C:\Temp») y sin distinguir mayúsculas. Puro: se prueba sin disco.
/// </summary>
public static class PathGuard
{
    private static string Norm(string p) => p.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    /// <summary>Carpeta contenedora, por texto (independiente de la plataforma: las rutas de Windows se prueban también en Linux).</summary>
    public static string? Parent(string path) { string p = path.Replace('/', '\\').TrimEnd('\\'); int i = p.LastIndexOf('\\'); return i <= 0 ? null : p[..i]; }

    public static bool IsInside(string root, string path)
    {
        string r = Norm(root), p = Norm(path);
        return r.Length > 0 && p.Length > r.Length && p.StartsWith(r, StringComparison.Ordinal) && p[r.Length] == '\\' && !p.Contains("\\..\\", StringComparison.Ordinal) && !p.EndsWith("\\..", StringComparison.Ordinal);
    }

    public static bool SameOrInside(string root, string path) => Norm(root) == Norm(path) || IsInside(root, path);

    /// <summary>Carpetas que NUNCA se tocan ni se aceptan como raíz (ni dentro de ellas, ni una carpeta que las contenga): documentos y archivos del usuario, sistema y programas.</summary>
    public static IReadOnlyList<string> ProtectedRoots(IReadOnlyDictionary<string, string> env)
    {
        var l = new List<string>();
        string? Get(string k) => env.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        if (Get("USERPROFILE") is { } up)
            foreach (var sub in new[] { "Documents", "Desktop", "Downloads", "Pictures", "Videos", "Music", "OneDrive", "source", "repos", "Projects", "Dropbox", ".ssh", "AppData\\Roaming" }) l.Add(up + "\\" + sub);
        foreach (var k in new[] { "SystemRoot", "ProgramFiles", "ProgramFiles(x86)", "ProgramData" }) if (Get(k) is { } v) l.Add(v);
        return l;
    }

    /// <summary>¿Es segura esta raíz? No puede ser una unidad ni un nivel superficial, ni estar dentro de una carpeta protegida ni contener una.</summary>
    public static bool IsSafeRoot(string root, IReadOnlyDictionary<string, string> env, out string why)
    {
        string r = Norm(root);
        if (r.Length < 4 || r.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 3) { why = "raíz demasiado superficial"; return false; }
        foreach (var prot in ProtectedRoots(env))
        {
            if (SameOrInside(prot, root)) { why = "está dentro de una carpeta protegida"; return false; }
            if (IsInside(root, prot)) { why = "contiene una carpeta protegida"; return false; }
        }

        why = ""; return true;
    }
}
