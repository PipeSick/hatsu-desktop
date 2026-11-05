using System.Text;
using System.Text.RegularExpressions;

namespace WinMac.Connect;

/// <summary>Estado de un hallazgo del diagnóstico. «Ausente» NO se usa: que algo no aparezca no prueba incompatibilidad (queda «No comprobado» o «Detectado: ninguno»).</summary>
public enum DiagState { Detected, Available, AccessDenied, NotSupported, NotChecked, Error }

public sealed record DiagFinding(string Area, DiagState State, string Detail);

/// <summary>Redacta lo sensible de un informe antes de guardarlo, copiarlo o exportarlo. Local: nada sale del PC.</summary>
public static class DiagRedactor
{
    private static readonly Regex Mac = new(@"\b([0-9A-Fa-f]{2}[:\-]){5}([0-9A-Fa-f]{2})\b", RegexOptions.Compiled);
    private static readonly Regex Mac12 = new(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{10}([0-9A-Fa-f]{2})(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"(?<![\w])\+?\d[\d \-().]{5,}\d(?![\w])", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);

    public static string Redact(string text, string? userProfile = null, string? userName = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!string.IsNullOrEmpty(userProfile)) text = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(userName) && userName.Length >= 3) text = text.Replace(userName, "<usuario>", StringComparison.OrdinalIgnoreCase);
        text = Mac.Replace(text, m => "**:**:**:**:**:" + m.Groups[2].Value.ToUpperInvariant());   // solo el último byte basta para distinguir dos dispositivos
        text = Ipv4.Replace(text, "<ip>");
        text = Phone.Replace(text, m =>
        {
            string d = new(m.Value.Where(char.IsDigit).ToArray());
            return d.Length < 7 || m.Value.Contains("**") ? m.Value : "<número>…" + d[^2..];   // nunca el número completo
        });
        return text;
    }
}

/// <summary>Compone el informe: un RESUMEN legible arriba (líneas «RESUMEN|…» que la interfaz extrae) y los detalles debajo. Puro.</summary>
public static class DiagReport
{
    public const string SummaryTag = "RESUMEN|";

    public static string Label(DiagState s) => s switch
    {
        DiagState.Detected => "Detectado", DiagState.Available => "Disponible", DiagState.AccessDenied => "Acceso denegado",
        DiagState.NotSupported => "No compatible", DiagState.NotChecked => "No comprobado", _ => "Error de ejecución",
    };

    public static string Compose(string title, IReadOnlyList<string> header, IReadOnlyList<DiagFinding> findings, IReadOnlyList<string> details)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"== {title} ==");
        foreach (var h in header) sb.AppendLine(h);
        sb.AppendLine();
        foreach (var f in findings) sb.AppendLine($"{SummaryTag}{f.Area}: {Label(f.State)}{(string.IsNullOrWhiteSpace(f.Detail) ? "" : " — " + f.Detail)}");
        sb.AppendLine();
        sb.AppendLine("Leyenda: Detectado = visto ahora · Disponible = puede usarse · Acceso denegado = existe pero Windows lo niega · No compatible = confirmado que no sirve · No comprobado = no se probó · Error de ejecución = falló la prueba.");
        sb.AppendLine("Que algo no aparezca NO prueba que el teléfono sea incompatible: puede estar desconectado, apagado o no emparejado.");
        sb.AppendLine("Privacidad: sin notificaciones, mensajes, audio, historial ni números completos; direcciones Bluetooth y rutas anonimizadas. Nada se envía fuera del PC.");
        sb.AppendLine();
        foreach (var d in details) sb.AppendLine(d);
        return sb.ToString();
    }

    /// <summary>Las líneas de resumen de un informe ya compuesto (sin la etiqueta).</summary>
    public static IReadOnlyList<string> SummaryOf(string report) =>
        report.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith(SummaryTag, StringComparison.Ordinal)).Select(l => l[SummaryTag.Length..]).ToList();
}
