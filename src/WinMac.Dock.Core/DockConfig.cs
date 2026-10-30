using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Dock;

public sealed record PinnedApp(string Path, string? Title = null);

public sealed record DockConfig
{
    public List<PinnedApp> Pinned { get; init; } = [];
    public bool MagnificationEnabled { get; init; } = true;
    public bool AutoHide { get; init; } = false;
    public double HideDelaySeconds { get; init; } = 0.6;
    public double ShowDelaySeconds { get; init; } = 0.12;
    public bool ShowRecents { get; init; } = true;
    public int MaxRecents { get; init; } = 3;
    /// <summary>macOS no minimiza al hacer clic sobre la app activa; en Windows muchos usuarios lo esperan. Por defecto, estilo macOS.</summary>
    public bool ClickActiveMinimizes { get; init; } = false;
    public string? DownloadsPath { get; init; }
    public GlassTier GlassTier { get; init; } = GlassTier.WallpaperBlur;
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static DockConfig FromJson(string json) => JsonSerializer.Deserialize<DockConfig>(json, Json) ?? new DockConfig();

    /// <summary>Carga tolerante: un archivo corrupto o ausente devuelve la configuración por defecto, nunca lanza.</summary>
    public static DockConfig LoadOrDefault(string path, Func<DockConfig> fallback)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : fallback(); }
        catch (Exception) { return fallback(); }
    }

    /// <summary>Escritura atómica (tmp + reemplazo) para no dejar un archivo a medias.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }
}
