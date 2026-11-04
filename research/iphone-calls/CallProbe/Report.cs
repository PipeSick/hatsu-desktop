using System.Text;

namespace CallProbe;

/// <summary>Escribe en pantalla y en un archivo (una app empaquetada no siempre tiene consola visible). Nunca escribe números ni nombres reales salvo con --show-numbers.</summary>
internal static class Report
{
    private static StreamWriter? _file;
    public static bool ShowNumbers { get; set; }

    public static void Open(string? path)
    {
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _file = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void Line(string text = "") { Console.WriteLine(text); _file?.WriteLine(text); }
    public static void Stamp(string text) => Line($"{DateTime.Now:HH:mm:ss.fff}  {text}");
    public static void Title(string text) { Line(); Line("== " + text + " =="); }
    public static void Item(string label, object? value) => Line($"  {label}: {value}");
    public static string Mask(string? s) => ShowNumbers || string.IsNullOrEmpty(s) ? s ?? "" : (s.Length <= 2 ? "**" : new string('*', s.Length - 2) + s[^2..]);
    public static string Addr(ulong a) => ShowNumbers ? a.ToString("X12") : "…" + (a & 0xFFFF).ToString("X4");

    /// <summary>Dirección Bluetooth que llega como texto hexadecimal (p. ej. una propiedad del dispositivo): se enmascara igual que las demás.</summary>
    public static string AddrText(object? raw) => raw is string s && ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var a) ? Addr(a) : "(n/d)";

    /// <summary>Identificadores de dispositivo: solo el final, salvo --show-numbers.</summary>
    public static string Id(string? id) => string.IsNullOrEmpty(id) ? "" : ShowNumbers ? id : "…" + id[^Math.Min(8, id.Length)..];

    /// <summary>Quita del texto de argumentos el nombre de usuario de las rutas (aparecía en los informes).</summary>
    public static string ScrubPath(string text) => ShowNumbers ? text : System.Text.RegularExpressions.Regex.Replace(text, @"(?i)([A-Z]:\\Users\\)[^\\]+", "$1<usuario>");
}
