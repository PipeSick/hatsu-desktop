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
    private static readonly Regex DateLike = new(@"^\d{4}-\d{2}-\d{2}", RegexOptions.Compiled), VersionLike = new(@"^\d+(\.\d+){2,}$", RegexOptions.Compiled);
    private static readonly Regex Ipv4 = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);

    public static string Redact(string text, string? userProfile = null, string? userName = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!string.IsNullOrEmpty(userProfile)) text = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(userName) && userName.Length >= 3) text = text.Replace(userName, "<usuario>", StringComparison.OrdinalIgnoreCase);
        text = Mac.Replace(text, m => "**:**:**:**:**:" + m.Groups[2].Value.ToUpperInvariant());   // solo el último byte basta para distinguir dos dispositivos
        text = Ipv4.Replace(text, m => m.Value.Split('.').All(o => int.Parse(o) <= 255) && !m.Value.StartsWith("10.0.") ? "<ip>" : m.Value);   // «10.0.26100.0» es una versión de Windows, no una IP
        text = Phone.Replace(text, m =>
        {
            string d = new(m.Value.Where(char.IsDigit).ToArray());
            if (d.Length < 7 || m.Value.Contains("**")) return m.Value;
            if (DateLike.IsMatch(m.Value) || VersionLike.IsMatch(m.Value)) return m.Value;   // fechas y versiones («2026-11-05 16», «10.0.26100.0») no son teléfonos
            return "<número>…" + d[^2..];   // nunca el número completo
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

/// <summary>Identificadores para el informe: correlacionables entre sí DENTRO del informe, pero no reversibles ni útiles fuera de él.</summary>
public static class DiagIds
{
    /// <summary>Etiqueta corta estable de un identificador (id de dispositivo, ContainerId, dirección): 6 hex de SHA-256. Vacío → «-».</summary>
    public static string Tag(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "-";
        var h = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id.Trim().ToLowerInvariant()));
        return "#" + Convert.ToHexString(h, 0, 3).ToLowerInvariant();
    }

    /// <summary>Número de teléfono → solo los dos últimos dígitos.</summary>
    public static string MaskNumber(string? n)
    {
        var d = new string((n ?? "").Where(char.IsDigit).ToArray());
        return d.Length == 0 ? "(sin número)" : "<número>…" + (d.Length >= 2 ? d[^2..] : d);
    }

    /// <summary>Nombre de contacto/dispositivo → solo la longitud (el nombre es contenido personal).</summary>
    public static string MaskName(string? n) => string.IsNullOrWhiteSpace(n) ? "(sin nombre)" : $"({n.Trim().Length} caracteres)";

    /// <summary>Nombre del HRESULT cuando es un error Win32 conocido relevante para la telefonía y el Bluetooth. Documentado en la lista oficial de códigos de error del sistema; «desconocido» si no.</summary>
    public static string HResultName(int hr) => unchecked((uint)hr) switch
    {
        0x80070005 => "E_ACCESSDENIED / ERROR_ACCESS_DENIED (5)",
        0x8007139F => "ERROR_INVALID_STATE (5023): el recurso no está en el estado correcto para la operación",
        0x800706D9 => "EPT_S_NOT_REGISTERED (1753): no hay servidor RPC escuchando (servicio no disponible)",
        0x800706BA => "RPC_S_SERVER_UNAVAILABLE (1722)",
        0x80070490 => "ERROR_NOT_FOUND (1168): elemento no encontrado",
        0x8000000E => "E_ILLEGAL_METHOD_CALL: llamada no permitida en el estado actual",
        0x80004005 => "E_FAIL",
        0x8007048F => "ERROR_DEVICE_NOT_CONNECTED (1167)",
        0x80070015 => "ERROR_NOT_READY (21)",
        0x8007001F => "ERROR_GEN_FAILURE (31)",
        _ => "código sin nombre conocido",
    };
}
