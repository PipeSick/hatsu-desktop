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

/// <summary>Alineación horizontal del Dock en la pantalla. El Dock sigue siendo horizontal y mantiene su aspecto; solo cambia dónde se coloca.</summary>
public enum DockAlignment { Left, Center, Right }

public sealed record DockConfig
{
    public int Version { get; init; } = 4;
    public List<PinnedApp> Pinned { get; init; } = [];
    public bool MagnificationEnabled { get; init; } = true;
    public bool AutoHide { get; init; } = false;
    /// <summary>WM-001: reservar el espacio del Dock en el área de trabajo (AppBar de Windows) para que no tape las ventanas maximizadas. Sin efecto con autoocultación.</summary>
    public bool ReserveSpace { get; init; } = true;
    public double HideDelaySeconds { get; init; } = 0.6;
    public double ShowDelaySeconds { get; init; } = 0.12;
    public bool ShowRecents { get; init; } = true;
    /// <summary>Máximo de apps abiertas no ancladas que se muestran; 0 = sin límite (todas las apps abiertas aparecen). Antes de la versión 3 el valor por defecto era 3 y dejaba apps fuera.</summary>
    public int MaxRecents { get; init; } = 0;
    /// <summary>macOS no minimiza al hacer clic sobre la app activa; en Windows muchos usuarios lo esperan. Por defecto, estilo macOS.</summary>
    public bool ClickActiveMinimizes { get; init; } = false;
    /// <summary>Vista previa de las ventanas de una app al pasar el ratón sobre su ícono.</summary>
    public bool PreviewsEnabled { get; init; } = true;
    public double PreviewDelaySeconds { get; init; } = 0.35;
    /// <summary>Lado del ícono en reposo (DIP). 60 = tamaño aprobado; el resto de medidas escalan en proporción (<see cref="DockMetrics.ForIconSize"/>). Rango 32–128.</summary>
    public double IconSize { get; init; } = DockSettings.DefaultIconSize;
    public DockAlignment Alignment { get; init; } = DockAlignment.Center;
    /// <summary>Distancia (DIP) al borde lateral de la pantalla cuando la alineación es izquierda o derecha.</summary>
    public double EdgeMargin { get; init; } = DockSettings.DefaultEdgeMargin;
    /// <summary>Cuánto se sube el Dock sobre su posición normal (justo encima de la barra de tareas), en DIP. 0 = normal. Rango 0–400.</summary>
    public double BottomOffset { get; init; } = 0;
    public string? DownloadsPath { get; init; }
    public GlassTier GlassTier { get; init; } = GlassTier.WallpaperBlur;
    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static DockConfig FromJson(string json)
    {
        var c = JsonSerializer.Deserialize<DockConfig>(json, Json) ?? new DockConfig();
        if (c.Version < 4)
        {
            // WM-019: an IconSize saved by an older version is mapped to the equivalent new preset; when it was not saved, the old default (60) becomes the new one
            bool saved; using (var d = JsonDocument.Parse(json)) saved = d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.EnumerateObject().Any(p => p.Name.Equals("IconSize", StringComparison.OrdinalIgnoreCase));
            c = c with { IconSize = saved ? DockSettings.MigrateIconSize(c.IconSize) : DockSettings.DefaultIconSize };
        }

        return Migrate(c);
    }

    /// <summary>v&lt;3: el 3 guardado era el valor por defecto antiguo (no una elección del usuario) y ocultaba apps abiertas; pasa a «sin límite». Otros valores se respetan.</summary>
    private static DockConfig Migrate(DockConfig c)
    {
        if (c.Version < 3) c = c with { Version = 3, MaxRecents = c.MaxRecents == 3 ? 0 : c.MaxRecents };
        if (c.Version < 4) c = c with { Version = 4, ReserveSpace = true };   // WM-001 (the icon size was already mapped in FromJson)
        return c;
    }

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
