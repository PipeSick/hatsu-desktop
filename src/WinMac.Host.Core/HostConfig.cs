using System.Text.Json;
using System.Text.Json.Serialization;
using WinMac.Bar;

namespace WinMac.Host;

public sealed record ModuleConfig
{
    public bool Enabled { get; init; } = true;
    /// <summary>Ruta del ejecutable; null = el de fábrica junto a WinMac.exe (solo se cambia en pruebas).</summary>
    public string? Path { get; init; }
    public List<string>? Args { get; init; }
}

/// <summary>
/// Configuración central del host (<c>winmac.json</c>): módulos activados, inicio automático y, desde F3a, los ajustes de la Isla (<see cref="Notch"/>).
/// Es la ÚNICA fuente de verdad de lo que contiene. dock.json y bar.json siguen aparte (no se migran aquí). Carga tolerante: un archivo ausente o corrupto devuelve los valores de fábrica.
/// </summary>
public sealed record HostConfig
{
    public int Version { get; init; } = BetaMigration.Version;
    public bool Autostart { get; init; } = false;
    /// <summary>WM-034: ocultar la barra de tareas de Windows mientras WinMac está abierto. APAGADO por defecto; se restaura al desactivarlo, al salir y, si WinMac falla, desde el guardián.</summary>
    public bool HideTaskbar { get; init; } = false;
    public Dictionary<string, ModuleConfig> Modules { get; init; } = new()
    {
        ["bar"] = new ModuleConfig(), ["dock"] = new ModuleConfig(), ["notch"] = new ModuleConfig(),   // habilitada por defecto cuando viene incluida; si falta el ejecutable el host la marca «no instalada» y sigue
    };

    /// <summary>Ajustes de la Isla. Null = aún no hay (se importan una vez de NotchWin o se usan los de fábrica).</summary>
    public NotchSettings? Notch { get; init; }

    /// <summary>Ajustes de la Barra superior. Null = aún no hay (se importa una vez de bar.json o se usan los de fábrica).</summary>
    public BarConfig? Bar { get; init; }

    /// <summary>Orden de arranque y ejecutables de fábrica (junto a WinMac.exe).</summary>
    public static readonly (string Id, string Exe, bool Optional)[] Known = [("bar", "WinMac.Bar.App.exe", false), ("dock", "WinMac.Dock.App.exe", false), ("notch", @"NotchWin\NotchWin.App.exe", true)];

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static HostConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new HostConfig();
            var c = JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(path), Json) ?? new HostConfig();
            var mods = new Dictionary<string, ModuleConfig>(new HostConfig().Modules); foreach (var (k, v) in c.Modules) mods[k.ToLowerInvariant()] = v;   // lo no indicado conserva el valor de fábrica
            return c with { Modules = mods, Notch = c.Notch?.Sanitize(), Bar = c.Bar?.Sanitize() };
        }
        catch (Exception) { return new HostConfig(); }
    }

    /// <summary>Módulos en orden de arranque con su ruta resuelta, si están habilitados y si están disponibles (un módulo OPCIONAL sin ejecutable es «no instalado», no un fallo).</summary>
    public IEnumerable<(ModuleSpec Spec, bool Enabled, bool Available)> Resolve(string baseDir, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        foreach (var (id, exe, optional) in Known)
        {
            Modules.TryGetValue(id, out var m); m ??= new ModuleConfig();
            string path = m.Path ?? System.IO.Path.Combine(baseDir, exe);
            yield return (new ModuleSpec(id, path, (IReadOnlyList<string>?)m.Args ?? []), m.Enabled, !optional || exists(path));
        }
    }

    /// <summary>Guardado atómico (archivo temporal + reemplazo): un corte a mitad nunca deja el archivo a medias.</summary>
    public static void Save(string path, HostConfig cfg)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Json));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }
}
