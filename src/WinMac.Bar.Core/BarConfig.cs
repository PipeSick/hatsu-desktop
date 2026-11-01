using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Bar;

public enum BarBackground { Transparent, Glass }
public enum BarThemeMode { System, Light, Dark }
public enum BarTheme { Light, Dark }

/// <summary>Configuración de la barra superior (bar.json). La carga es tolerante: un archivo ausente o corrupto devuelve los valores por defecto.</summary>
public sealed record BarConfig
{
    public int Version { get; init; } = 1;
    /// <summary>Aspecto provisional (D5 de docs/12): transparente como en Tahoe, o con vidrio desenfocado.</summary>
    public BarBackground Background { get; init; } = BarBackground.Glass;
    public BarThemeMode Theme { get; init; } = BarThemeMode.System;
    /// <summary>Reservar el borde superior de la pantalla (AppBar) para que las ventanas maximizadas queden debajo. false = superpuesta.</summary>
    public bool ReserveSpace { get; init; } = true;
    /// <summary>"es" o "en" (nombres de día y mes del reloj). Tablas propias: no dependen de ICU ni de la cultura del sistema.</summary>
    public string Language { get; init; } = "es";
    public bool Use24Hour { get; init; } = true;
    public bool ShowSeconds { get; init; } = false;
    public bool ShowDate { get; init; } = true;
    public bool ShowBatteryPercent { get; init; } = true;
    public bool ShowBattery { get; init; } = true;
    public bool ShowVolume { get; init; } = true;
    public bool ShowNetwork { get; init; } = true;
    /// <summary>Fracción central del ancho que se deja libre (p. ej. para una app tipo «notch»). 0 = sin hueco.</summary>
    public double CenterGap { get; init; } = 0.30;

    // ── Control Center, paneles y menús (la barra «real»)
    public bool ShowBluetooth { get; init; } = true;
    public bool ShowControlCenter { get; init; } = true;
    /// <summary>EXPERIMENTAL: mostrar junto al nombre los menús de la app activa (solo apps Win32 con barra de menús estándar). Apagado por defecto.</summary>
    public bool AppMenus { get; init; } = false;
    /// <summary>Pedir confirmación antes de reiniciar, apagar o cerrar sesión. «Forzar salida» la pide siempre.</summary>
    public bool ConfirmDestructive { get; init; } = true;
    /// <summary>Archivo .ics para los eventos del calendario desplegable (solo lectura). null = sin eventos.</summary>
    public string? CalendarIcsPath { get; init; }
    // Controles del Control Center que se muestran (un control sin respaldo en el equipo se muestra deshabilitado con su motivo, o se oculta aquí).
    /// <summary>Wi-Fi: al tocar una red se despliega con señal, potencia (dBm), banda (2,4 / 5 / 6 GHz) y seguridad, con la acción de conectar dentro. Apagado por defecto (la lista conecta con un toque).</summary>
    public bool WifiDetails { get; init; } = false;
    public bool CcWifi { get; init; } = true;
    public bool CcBluetooth { get; init; } = true;
    public bool CcSound { get; init; } = true;
    public bool CcBrightness { get; init; } = true;
    public bool CcPower { get; init; } = true;
    public bool CcAppearance { get; init; } = true;
    public bool CcDisplay { get; init; } = true;
    public bool CcNotifications { get; init; } = true;

    /// <summary>Formato de los mensajes entre el host y la barra (camelCase), el mismo patrón que los ajustes de la Isla.</summary>
    public static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Corrige lo inválido (archivo editado a mano o versión futura).</summary>
    public BarConfig Sanitize() => this with
    {
        Language = Language == "en" ? "en" : "es",
        CenterGap = double.IsFinite(CenterGap) ? Math.Clamp(CenterGap, 0, 0.6) : 0.30,
        CalendarIcsPath = string.IsNullOrWhiteSpace(CalendarIcsPath) ? null : CalendarIcsPath.Trim().Trim('"'),
    };

    /// <summary>Aplica un parche JSON (camelCase, solo los campos presentes): lo desconocido o de otro tipo se ignora campo a campo.</summary>
    public BarConfig ApplyPatch(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return this;
        try
        {
            var node = System.Text.Json.JsonSerializer.SerializeToNode(this, Wire)!.AsObject();
            foreach (var p in patch.EnumerateObject())
            {
                if (!node.TryGetPropertyValue(p.Name, out var cur) && !KnownNullable.Contains(p.Name)) continue;   // campo desconocido
                if (p.Name.Equals("version", StringComparison.OrdinalIgnoreCase)) continue;
                bool ok = (cur?.GetValueKind(), p.Value.ValueKind) switch
                {
                    (JsonValueKind.True or JsonValueKind.False, JsonValueKind.True or JsonValueKind.False) => true,
                    (JsonValueKind.Number, JsonValueKind.Number) => true,
                    (JsonValueKind.String, JsonValueKind.String) => true,
                    (null or JsonValueKind.Null or JsonValueKind.String, JsonValueKind.String or JsonValueKind.Null) when KnownNullable.Contains(p.Name) => true,   // texto opcional: se puede poner y quitar
                    _ => false,
                };
                if (!ok) continue;
                // campo por campo: un valor inválido (p. ej. un tema que no existe) descarta SOLO ese campo
                var previous = node.TryGetPropertyValue(p.Name, out var old) ? old?.DeepClone() : null;
                node[p.Name] = System.Text.Json.Nodes.JsonNode.Parse(p.Value.GetRawText());
                try { _ = System.Text.Json.JsonSerializer.Deserialize<BarConfig>(node, Wire); }
                catch (JsonException) { node[p.Name] = previous; }
            }

            return (System.Text.Json.JsonSerializer.Deserialize<BarConfig>(node, Wire) ?? this).Sanitize();
        }
        catch (Exception) { return this; }   // un parche ilegible nunca estropea la configuración
    }

    private static readonly HashSet<string> KnownNullable = new(StringComparer.OrdinalIgnoreCase) { "calendarIcsPath" };

    /// <summary>Lo que se envía al módulo (camelCase).</summary>
    public string ToWireJson() => System.Text.Json.JsonSerializer.Serialize(this, Wire);

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static BarConfig FromJson(string json) => JsonSerializer.Deserialize<BarConfig>(json, Json) ?? new BarConfig();

    public static BarConfig LoadOrDefault(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new BarConfig(); }
        catch (Exception) { return new BarConfig(); }
    }

    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp"; File.WriteAllText(tmp, ToJson()); File.Move(tmp, path, overwrite: true); return true;
        }
        catch (Exception) { return false; }
    }
}
