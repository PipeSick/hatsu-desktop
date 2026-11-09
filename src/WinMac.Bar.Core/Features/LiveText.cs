using System.Text;

namespace WinMac.Bar.Features;

/// <summary>Lógica pura de Live Text (OCR local): armado del texto a partir de las líneas reconocidas, elección de idioma y vista previa.</summary>
public static class LiveText
{
    /// <summary>Une las líneas. <paramref name="keepBreaks"/> conserva los saltos de línea; si no, se unen con un espacio (y se juntan palabras partidas con guion al final de línea).</summary>
    public static string Join(IReadOnlyList<string> lines, bool keepBreaks)
    {
        var clean = lines.Select(l => (l ?? "").TrimEnd()).Where(l => l.Trim().Length > 0).ToList();
        if (clean.Count == 0) return "";
        if (keepBreaks) return string.Join(Environment.NewLine, clean);
        var sb = new StringBuilder();
        for (int i = 0; i < clean.Count; i++)
        {
            var t = clean[i].Trim();
            if (sb.Length > 0 && sb[^1] == '-' && sb.Length > 1 && char.IsLetter(sb[^2]) && t.Length > 0 && char.IsLower(t[0])) sb.Length--;   // «informa-» + «ción»
            else if (sb.Length > 0) sb.Append(' ');
            sb.Append(t);
        }

        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"[ \t]{2,}", " ");
    }

    /// <summary>Idioma de reconocimiento: el preferido si está instalado; si no, el mismo idioma base (es-CL → es-ES); si no, el primero de los perfiles del usuario; si no, el primero disponible. Null = no hay ninguno.</summary>
    public static string? PickLanguage(IReadOnlyList<string> available, string? preferred, IReadOnlyList<string> profile)
    {
        if (available.Count == 0) return null;
        string? Exact(string? t) => string.IsNullOrWhiteSpace(t) ? null : available.FirstOrDefault(a => string.Equals(a, t, StringComparison.OrdinalIgnoreCase));
        string Base(string t) => t.Split('-')[0].ToLowerInvariant();
        string? Same(string? t) => string.IsNullOrWhiteSpace(t) ? null : available.FirstOrDefault(a => Base(a) == Base(t));
        if (Exact(preferred) is { } e) return e;
        if (Same(preferred) is { } s) return s;
        foreach (var p in profile) { if (Exact(p) is { } pe) return pe; if (Same(p) is { } ps) return ps; }
        return available[0];
    }

    public const string NoLanguages = "Windows no tiene idiomas de reconocimiento de texto instalados. Añade el idioma que necesites en Configuración › Hora e idioma › Idioma y región; el reconocimiento funciona sin Internet una vez instalado.";

    /// <summary>Primeras líneas para el panel de resultado.</summary>
    public static (string Text, int MoreLines) Preview(string text, int maxLines = 14, int maxChars = 1200)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var shown = string.Join('\n', lines.Take(maxLines)); if (shown.Length > maxChars) shown = shown[..maxChars] + "…";
        return (shown, Math.Max(0, lines.Length - maxLines));
    }
}
