using System.Text.Json;
using System.Text.Json.Serialization;
using WinMac.Apps;

namespace WinMac.Dock;

/// <summary>
/// App anclada. Se identifica por <see cref="Id"/> estable (ver <see cref="AppIdentity"/>), no solo por ruta: una app empaquetada se guarda con su AUMID y se activa con shell:AppsFolder.
/// Compatible con la configuración antigua (solo Path/Title): el Id se deduce de la ruta.
/// </summary>
public sealed record PinnedApp
{
    public PinnedApp() { }
    public PinnedApp(string path, string? title = null) { Path = path; Title = title; }
    public string? Id { get; init; }
    public string? Path { get; init; }          // Win32: ejecutable. Empaquetada: .exe del paquete (informativo; NO se usa para activar)
    public string? Aumid { get; init; }
    public string? Title { get; init; }
    public string? IconOverride { get; init; }  // icono alternativo configurable: .png/.ico/.bmp o un ejecutable
    public AppIdentity? ToIdentity() => AppIdentity.From(Id, Path, Aumid, Title);
    [JsonIgnore] public string StableId => ToIdentity()?.Id ?? Id ?? "";
}

public sealed record DockConfig
{
    public int Version { get; init; } = 3;
    public List<PinnedApp> Pinned { get; init; } = [];
    public bool MagnificationEnabled { get; init; } = true;
    public bool AutoHide { get; init; } = false;
    public double HideDelaySeconds { get; init; } = 0.6;
    public double ShowDelaySeconds { get; init; } = 0.12;
    public bool ShowRecents { get; init; } = true;
    /// <summary>Máximo de apps abiertas no ancladas que se muestran; 0 = sin límite (todas las apps abiertas aparecen). Antes de la versión 3 el valor por defecto era 3 y dejaba apps fuera.</summary>
    public int MaxRecents { get; init; } = 0;
    /// <summary>macOS no minimiza al hacer clic sobre la app activa; en Windows muchos usuarios lo esperan. Por defecto, estilo macOS.</summary>
    public bool ClickActiveMinimizes { get; init; } = false;
    public string? DownloadsPath { get; init; }
    public GlassTier GlassTier { get; init; } = GlassTier.WallpaperBlur;
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static DockConfig FromJson(string json) => Migrate(JsonSerializer.Deserialize<DockConfig>(json, Json) ?? new DockConfig());

    /// <summary>v&lt;3: el 3 guardado era el valor por defecto antiguo (no una elección del usuario) y ocultaba apps abiertas; pasa a «sin límite». Otros valores se respetan.</summary>
    private static DockConfig Migrate(DockConfig c) => c.Version >= 3 ? c : c with { Version = 3, MaxRecents = c.MaxRecents == 3 ? 0 : c.MaxRecents };

    /// <summary>Carga tolerante: un archivo corrupto o ausente devuelve la configuración por defecto, nunca lanza.</summary>
    public static DockConfig LoadOrDefault(string path, Func<DockConfig> fallback)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : fallback(); }
        catch (Exception) { return fallback(); }
    }

    /// <summary>Escritura atómica (tmp + reemplazo) para no dejar un archivo a medias. Devuelve false si no se pudo guardar (no lanza).</summary>
    public bool TrySave(string path)
    {
        try { Save(path); return true; } catch (Exception) { return false; }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }
}
