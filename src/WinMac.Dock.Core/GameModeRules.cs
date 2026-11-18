namespace WinMac.Dock;

/// <summary>Modo juego del Dock: qué ejecutables cuentan como juego y cómo se guarda la lista. Puro: el lector de Windows solo pregunta por la ventana en primer plano.</summary>
public static class GameModeRules
{
    public const string SteamFolder = @"\steamapps\common\";
    public const int MaxApps = 64;

    /// <summary>«C:\Juegos\EldenRing.EXE», «eldenring» o «EldenRing.exe» → «eldenring.exe». Vacío o con caracteres no válidos → null.</summary>
    public static string? Normalize(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        string n = exe.Trim().Trim('"'); int cut = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/')); if (cut >= 0) n = n[(cut + 1)..];
        if (n.Length == 0 || n.Length > 96 || n.Any(c => char.IsControl(c) || "<>:\"|?*".Contains(c))) return null;
        n = n.ToLowerInvariant(); return n.EndsWith(".exe", StringComparison.Ordinal) ? n : n + ".exe";
    }

    /// <summary>Texto separado por comas, punto y coma o saltos de línea → lista normalizada, sin repetidos y como mucho <see cref="MaxApps"/>.</summary>
    public static List<string> ParseList(string? text) =>
        (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(Normalize).OfType<string>().Distinct().Take(MaxApps).ToList();

    public static string FormatList(IEnumerable<string>? apps) => string.Join(", ", apps ?? []);

    /// <summary>¿Este ejecutable es un juego? Por la lista del usuario o, con <paramref name="steam"/>, por estar en una carpeta de juegos de Steam.</summary>
    public static bool IsGame(string? exePath, IEnumerable<string>? userApps, bool steam = true)
    {
        if (string.IsNullOrEmpty(exePath)) return false;
        if (Normalize(exePath) is { } name && userApps is not null && userApps.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
        return steam && exePath.Contains(SteamFolder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Siguiente estado del modo juego. <paramref name="foregroundIsGame"/> null = la ventana en primer plano no dice nada (el escritorio, la barra de tareas o el propio Dock): se queda como estaba, así pasar el puntero al Dock no lo apaga.</summary>
    public static bool Next(bool? foregroundIsGame, bool current) => foregroundIsGame ?? current;
}
