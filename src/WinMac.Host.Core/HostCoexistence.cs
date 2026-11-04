namespace WinMac.Host;

public enum CoexistenceKind { NoOther, SameInstall, OtherInstall }

/// <summary>
/// Convivencia entre instalaciones (MSI, ZIP portable, WinMac antiguo): el host ya impide DOS hosts a la vez (exclusión mutua por usuario). Esto decide qué hace una segunda copia al arrancar:
/// si el host vivo es de ESTA misma carpeta, solo pide reabrir módulos; si es de OTRA (WinMac antiguo o una copia portable), nunca lo toca por su cuenta: en uso interactivo lo PREGUNTA
/// (adopción explícita = <c>--replace-running</c>) y en segundo plano (inicio automático) se cierra sin hacer nada. Puro: se prueba sin Windows.
/// </summary>
public static class HostCoexistence
{
    public static CoexistenceKind Classify(string selfExe, IEnumerable<string?> otherRunningPaths)
    {
        string self = Norm(selfExe); bool any = false;
        foreach (var p in otherRunningPaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            any = true; if (!string.Equals(Norm(p), self, StringComparison.OrdinalIgnoreCase)) return CoexistenceKind.OtherInstall;
        }
        return any ? CoexistenceKind.SameInstall : CoexistenceKind.NoOther;
    }

    /// <summary>La ruta de la otra copia (para mostrarla al usuario), o null si es la misma instalación o no hay.</summary>
    public static string? OtherPath(string selfExe, IEnumerable<string?> otherRunningPaths) =>
        otherRunningPaths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && !string.Equals(Norm(p!), Norm(selfExe), StringComparison.OrdinalIgnoreCase));

    /// <summary>¿Se pregunta al usuario? Solo si hay OTRA instalación viva y el arranque es interactivo (no el inicio automático ni las pruebas).</summary>
    public static bool ShouldAsk(CoexistenceKind kind, bool background, bool noTray) => kind == CoexistenceKind.OtherInstall && !background && !noTray;

    private static string Norm(string p) { var s = p.Trim().Replace('/', '\\'); return s.TrimEnd('\\'); }

    /// <summary>
    /// Un valor de inicio automático pertenece a ESTA instalación si apunta a su ejecutable. Lo usa la desinstalación del MSI: solo quita lo suyo, nunca el inicio automático de otra copia.
    /// </summary>
    public static bool AutostartBelongsTo(string? storedValue, string installExe)
    {
        var exe = AutostartMigration.ExePathOf(storedValue); return exe is not null && string.Equals(Norm(exe), Norm(installExe), StringComparison.OrdinalIgnoreCase);
    }
}
