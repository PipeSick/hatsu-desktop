namespace WinMac.Bar.Features;

/// <summary>Identificadores estables de los módulos y validación de sus ajustes (pura).</summary>
public static class FeatureConfig
{
    public const string MissionControl = "mission", Snap = "snap", LiveText = "livetext", SpotActions = "spotactions", Gestures = "gestures", Shelf = "shelf";
    public static readonly string[] SnapModifiers = ["Shift", "Ctrl", "Alt"];

    /// <summary>Texto de atajo válido (canónico) o <paramref name="fallback"/>. Vacío = «sin atajo» solo si el valor por defecto también es vacío o el usuario lo dejó vacío a propósito.</summary>
    public static string NormalizeHotkey(string? value, string fallback)
    {
        if (value is null) return fallback;
        if (value.Trim().Length == 0) return "";                      // el usuario lo quitó a propósito: el módulo sigue activo sin atajo
        return HotkeySpec.TryParse(value, out var s) ? s.Format() : fallback;
    }

    /// <summary>Acción de gesto/atajo conocida; lo desconocido pasa a «none».</summary>
    public static string NormalizeAction(string? a)
    {
        if (string.IsNullOrWhiteSpace(a)) return "none"; a = a.Trim();
        if (a.StartsWith(Launchers.Prefix, StringComparison.Ordinal)) return a.Length <= Launchers.Prefix.Length + 12 && a[Launchers.Prefix.Length..].All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9') && a.Length > Launchers.Prefix.Length ? a : "none";
        return GestureActions.All.Any(x => x.Id == a) ? a : "none";   // las acciones que ya no existen (p. ej. «dock») pasan a «Nada»
    }
}

/// <summary>Acciones que los gestos, los atajos y Spotlight pueden disparar. Cada una declara el módulo que la ofrece (si ese módulo está apagado, la acción no se ofrece ni se ejecuta).</summary>
public static class GestureActions
{
    public sealed record Def(string Id, string Name, string? Module);
    public static readonly Def[] All =
    [
        new("none", "Nada", null),
        new("mission", "Mission Control", FeatureConfig.MissionControl),
        new("spotlight", "Abrir Spotlight", null),
        new("launchpad", "Abrir Launchpad", null),
        new("livetext", "Live Text (reconocer texto)", FeatureConfig.LiveText),
        new("snapnext", "Siguiente zona de Snap", FeatureConfig.Snap),
        new("desktopprev", "Escritorio anterior (Win+Ctrl+←)", null),
        new("desktopnext", "Escritorio siguiente (Win+Ctrl+→)", null),
        new("capture", "Capturar región", null),
    ];
}
