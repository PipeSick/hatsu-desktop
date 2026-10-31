using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Host;

public sealed record ModuleConfig
{
    public bool Enabled { get; init; } = true;
    /// <summary>Ruta del ejecutable; null = el de fábrica junto a WinMac.exe (solo se cambia en pruebas).</summary>
    public string? Path { get; init; }
    public List<string>? Args { get; init; }
}

/// <summary>
/// Configuración MÍNIMA del host (<c>winmac.json</c>), solo lectura en F1. La configuración central completa (y la migración de dock.json/bar.json) es F3.
/// Carga tolerante: un archivo ausente o corrupto devuelve los valores de fábrica.
/// </summary>
public sealed record HostConfig
{
    public int Version { get; init; } = 1;
    public bool Autostart { get; init; } = false;
    public Dictionary<string, ModuleConfig> Modules { get; init; } = new()
    {
        ["bar"] = new ModuleConfig(), ["dock"] = new ModuleConfig(), ["notch"] = new ModuleConfig { Enabled = false },   // NotchWin llega en F4; desactivado por defecto
    };

    /// <summary>Orden de arranque y ejecutables de fábrica (junto a WinMac.exe).</summary>
    public static readonly (string Id, string Exe)[] Known = [("bar", "WinMac.Bar.App.exe"), ("dock", "WinMac.Dock.App.exe"), ("notch", @"NotchWin\WinMac.Notch.exe")];

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static HostConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new HostConfig();
            var c = JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(path), Json) ?? new HostConfig();
            var mods = new Dictionary<string, ModuleConfig>(new HostConfig().Modules); foreach (var (k, v) in c.Modules) mods[k.ToLowerInvariant()] = v;   // lo no indicado conserva el valor de fábrica
            return c with { Modules = mods };
        }
        catch (Exception) { return new HostConfig(); }
    }

    /// <summary>Módulos en orden de arranque con su ruta resuelta y si están habilitados.</summary>
    public IEnumerable<(ModuleSpec Spec, bool Enabled)> Resolve(string baseDir)
    {
        foreach (var (id, exe) in Known)
        {
            Modules.TryGetValue(id, out var m); m ??= new ModuleConfig();
            string path = m.Path ?? System.IO.Path.Combine(baseDir, exe);
            yield return (new ModuleSpec(id, path, (IReadOnlyList<string>?)m.Args ?? []), m.Enabled);
        }
    }
}
