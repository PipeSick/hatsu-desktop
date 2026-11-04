namespace WinMac.Bar.Launcher;

/// <summary>One global shortcut of WinMac as it is now: the keys, what it does, which module owns it, and whether Windows accepted it.</summary>
public sealed record HotkeyInfo(string Keys, string Purpose, string Module, bool Registered, string? Problem = null);

/// <summary>
/// Shortcut checks (docs/27): duplicates inside WinMac, combinations Windows itself reserves, and the result of registering each one. Windows does not say WHO
/// holds a shortcut it rejects, and a shortcut of another app that is only active in its own window cannot be detected at all: the text says so.
/// </summary>
public static class HotkeyAudit
{
    /// <summary>Known system combinations (documented Windows shortcuts). WinMac never registers over them.</summary>
    public static readonly (string Keys, string Owner)[] Windows =
    [
        ("Win+Mayús+S", "Recortes de Windows"), ("Ctrl+Mayús+Esc", "Administrador de tareas"), ("Alt+Espacio", "Menú de ventana de Windows"),
        ("Win+L", "Bloquear Windows"), ("Win+D", "Mostrar escritorio"), ("Win+Impr Pant", "Captura de Windows"), ("Alt+Tab", "Cambiar de ventana"),
        ("Ctrl+Alt+Supr", "Seguridad de Windows"), ("Win+Espacio", "Cambiar idioma de entrada"),
    ];

    public static IReadOnlyList<HotkeyInfo> Check(IReadOnlyList<HotkeyInfo> keys)
    {
        var result = new List<HotkeyInfo>();
        foreach (var k in keys)
        {
            string? problem = k.Problem;
            var dup = keys.FirstOrDefault(o => !ReferenceEquals(o, k) && string.Equals(o.Keys, k.Keys, StringComparison.OrdinalIgnoreCase));
            if (dup is not null) problem = $"también lo usa «{dup.Purpose}» ({dup.Module})";
            else if (Windows.FirstOrDefault(w => string.Equals(w.Keys, k.Keys, StringComparison.OrdinalIgnoreCase)) is { Keys: not null } w)
                problem ??= $"coincide con «{w.Owner}» de Windows" + (k.Registered ? " (Hatsu Desktop lo usa mientras está abierto)" : "");
            else if (!k.Registered) problem ??= "Windows no lo aceptó: otro programa ya lo tiene registrado (no se puede saber cuál)";
            result.Add(k with { Problem = problem });
        }

        return result;
    }

    /// <summary>Modifier choices for the capture shortcuts (number keys 3–6).</summary>
    public static readonly string[] CaptureModifierIds = ["CtrlShift", "CtrlAlt", "AltShift"];
    public static readonly string[] CaptureModifierNames = ["Ctrl+Mayús", "Ctrl+Alt", "Alt+Mayús"];

    public static string CaptureModifierName(string? id) => CaptureModifierNames[Math.Max(0, Array.IndexOf(CaptureModifierIds, id ?? "CtrlShift"))];
}
