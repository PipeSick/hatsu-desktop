namespace WinMac.Host;

/// <summary>
/// Hatsu Desktop (antes WinMac): el inicio automático sigue siendo la MISMA clave de usuario (<c>HKCU\…\Run</c>, valor «WinMac», identificador interno persistente), para que una
/// instalación anterior se actualice sin duplicados. Al cambiar de carpeta (el paquete nuevo ya no se llama «WinMac») el valor podría quedar apuntando a un ejecutable que ya no
/// existe (un acceso huérfano). Regla: si el valor existe, apunta a un archivo que YA NO existe y es distinto del ejecutable actual, se reescribe con el actual. Cualquier otro caso
/// (no hay valor; ya apunta aquí; apunta a otra instalación que sigue existiendo) NO se toca: no se secuestra el inicio automático de otra copia. Idempotente. Puro.
/// </summary>
public static class AutostartMigration
{
    public const string Args = "--background";

    /// <summary>La ruta del ejecutable de un valor «"C:\…\WinMac.exe" --background» (o sin comillas), o null si no se entiende.</summary>
    public static string? ExePathOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string v = value.Trim();
        if (v.StartsWith('"')) { int end = v.IndexOf('"', 1); return end > 1 ? v[1..end] : null; }
        int sp = v.IndexOf(' '); string p = sp > 0 ? v[..sp] : v;
        return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : null;
    }

    public static string ValueFor(string exe) => $"\"{exe}\" {Args}";

    /// <returns>El valor nuevo que hay que escribir, o null si no hay que cambiar nada.</returns>
    public static string? Plan(string? stored, string currentExe, Func<string, bool> exists)
    {
        var old = ExePathOf(stored);
        if (old is null || string.IsNullOrWhiteSpace(currentExe)) return null;
        if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(currentExe), StringComparison.OrdinalIgnoreCase)) return null;
        if (exists(old)) return null;
        return ValueFor(currentExe);
    }
}
