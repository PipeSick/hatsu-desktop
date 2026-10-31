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
