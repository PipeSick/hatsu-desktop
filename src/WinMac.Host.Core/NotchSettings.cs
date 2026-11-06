using System.Text.Json;

namespace WinMac.Host;

/// <summary>Una pestaña del notch expandido: cuál es y si el usuario la quiere visible.</summary>
public sealed record NotchSection(string Id, bool Enabled);

/// <summary>
/// Ajustes CENTRALES de la Isla (NotchWin). El host (<c>winmac.json</c>, sección <c>notch</c>) es la única fuente de verdad: el módulo los recibe por el tópico retenido
/// <see cref="Topic"/> y pide cambios con el evento <see cref="SetTopic"/> (un parche con solo los campos que cambian, en camelCase). Lo local de NotchWin (rutas de datos,
/// historial, precios) NO está aquí, y nunca se guarda ningún secreto.
/// Los valores nunca se confían: <see cref="Sanitize"/> y <see cref="ApplyPatch"/> descartan lo desconocido o fuera de rango y conservan el valor anterior.
/// </summary>
public sealed record NotchSettings
{
    public const string Topic = "config.notch", SetTopic = "config.notch.set", CommandTopic = "notch.cmd", RectsTopic = "notch.rects";   // RectsTopic: espacio que ocupa la Isla (lo publica ella; lo lee la Barra)

    public static readonly string[] Positions = ["TopCenter", "TopLeft", "TopRight"];
    public static readonly string[] Players = ["Spotify", "Feishin", "Custom"];
    /// <summary>Mismo orden que NotchWin (NotchSections.AllIds): el Centro de actividades de WinMac 2.0 primero (docs/25).</summary>
    public static readonly string[] SectionIds = ["activity", "captures", "clipboard", "downloads", "history", "music", "agents", "timer", "calendar", "notifications", "shelf", "camera", "system"];
    private const string CameraId = "camera";   // se abre la webcam: apagada por defecto

    public string Position { get; init; } = "TopCenter";
    public int GlassLevel { get; init; } = 60;
    public IReadOnlyList<NotchSection> Sections { get; init; } = DefaultSections();
    public bool MusicEnabled { get; init; } = true;
    public bool AgentsEnabled { get; init; } = true;
    public string MusicPlayer { get; init; } = "Spotify";
    public string? CustomPlayerPath { get; init; }
    public bool LyricsEnabled { get; init; }
    public bool LyricsInPill { get; init; }
    public int PollIntervalSeconds { get; init; } = 30;
    /// <summary>Shelf+: cuántos elementos guarda la estantería de la Isla (1–100). El más antiguo sale primero.</summary>
    public int ShelfMaxItems { get; init; } = 20;
    // Lo que la Isla cerrada muestra un momento (lo detecta la barra y lo reenvía; ver docs/21-isla-informativa.md)
    public bool PillVolume { get; init; } = true;
    public bool PillBrightness { get; init; } = true;
    public bool PillNotifications { get; init; } = true;
    public int PillHudSeconds { get; init; } = 2;
    public int PillNotifySeconds { get; init; } = 5;
    public bool PillAnimations { get; init; } = true;
    // Portapapeles (docs/28): sin persistencia por defecto; las contraseñas y claves detectables no se guardan; apps excluidas (gestores de contraseñas)
    public int ClipboardLimit { get; init; } = 50;
    public bool ClipboardPersist { get; init; }
    public bool ClipboardSkipSecrets { get; init; } = true;
    public bool ClipboardExclusionsOn { get; init; } = true;
    /// <summary>Procesos excluidos separados por comas (sin .exe). Null = la lista por defecto (gestores de contraseñas). Texto, no lista: el record compara por valor.</summary>
    public string? ClipboardExcludedApps { get; init; }
    public static readonly string DefaultExcludedApps = "KeePass, KeePassXC, 1Password, Bitwarden, LastPass, Dashlane, NordPass, ProtonPass, Enpass";

    /// <summary>Nombres de proceso válidos (letras, números, punto, guion, guion bajo y espacio), sin «.exe», sin repetir, hasta 64.</summary>
    public static IReadOnlyList<string> ParseApps(string? text) =>
        (text ?? DefaultExcludedApps).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? a[..^4] : a)
            .Where(a => a.Length is > 0 and <= 64 && a.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' '))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();

    /// <summary>
    /// Versión Store (MSIX): en instalaciones NUEVAS el módulo de agentes de IA (lee archivos de sesión de otras herramientas en tu carpeta de usuario) empieza DESACTIVADO,
    /// pero sigue completo y se activa a mano en Ajustes. Lo fija el host al arrancar con identidad de paquete. NUNCA toca lo ya guardado: solo sustituye al valor de fábrica
    /// cuando no hay ajustes de la Isla (ni propios ni importados de NotchWin); fuera de la Store el valor de fábrica no cambia.
    /// </summary>
    public static bool AgentsOffByDefault { get; set; }

    /// <summary>Valor de fábrica de los ajustes cuando no hay ninguno guardado (ver <see cref="AgentsOffByDefault"/>).</summary>
    public static NotchSettings Factory => FactoryFor(AgentsOffByDefault);

    public static NotchSettings FactoryFor(bool agentsOff) => agentsOff
        ? new NotchSettings { AgentsEnabled = false, Sections = DefaultSections().Select(s => s.Id == "agents" ? s with { Enabled = false } : s).ToList() }
        : new NotchSettings();

    public static IReadOnlyList<NotchSection> DefaultSections() => SectionIds.Select(id => new NotchSection(id, id != CameraId)).ToList();

    /// <summary>Conserva el orden y el estado guardados, descarta ids desconocidos o repetidos y añade al final los que falten (con su valor por defecto).</summary>
    public static IReadOnlyList<NotchSection> NormalizeSections(IEnumerable<NotchSection>? saved)
    {
        var result = new List<NotchSection>(); var seen = new HashSet<string>();
        foreach (var s in saved ?? []) if (s is not null && SectionIds.Contains(s.Id) && seen.Add(s.Id)) result.Add(new NotchSection(s.Id, s.Enabled));
        foreach (var id in SectionIds.Where(id => !seen.Contains(id))) result.Add(new NotchSection(id, id != CameraId));
        return result;
    }

    /// <summary>Corrige cualquier valor inválido (archivo editado a mano o versión futura).</summary>
    public NotchSettings Sanitize() => this with
    {
        Position = Positions.FirstOrDefault(p => string.Equals(p, Position, StringComparison.OrdinalIgnoreCase)) ?? "TopCenter",
        GlassLevel = Math.Clamp(GlassLevel, 0, 100),
        Sections = NormalizeSections(Sections),
        MusicPlayer = Players.FirstOrDefault(p => string.Equals(p, MusicPlayer, StringComparison.OrdinalIgnoreCase)) ?? "Spotify",
        CustomPlayerPath = string.IsNullOrWhiteSpace(CustomPlayerPath) ? null : CustomPlayerPath.Trim().Trim('"'),
        PollIntervalSeconds = Math.Clamp(PollIntervalSeconds, 5, 3600),
        ShelfMaxItems = Math.Clamp(ShelfMaxItems, 1, 100),
        PillHudSeconds = Math.Clamp(PillHudSeconds, 1, 10),
        PillNotifySeconds = Math.Clamp(PillNotifySeconds, 2, 30),
        ClipboardLimit = Math.Clamp(ClipboardLimit, 5, 500),
        ClipboardExcludedApps = ClipboardExcludedApps is null ? null : string.Join(", ", ParseApps(ClipboardExcludedApps)),
    };

    /// <summary>Aplica un parche JSON (camelCase, solo los campos presentes). Lo desconocido o inválido se ignora campo a campo.</summary>
    public NotchSettings ApplyPatch(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return this;
        var n = this;
        foreach (var p in patch.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name.ToLowerInvariant())
            {
                case "position" when v.ValueKind == JsonValueKind.String: n = n with { Position = v.GetString()! }; break;
                case "glasslevel" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int g): n = n with { GlassLevel = g }; break;
                case "musicenabled" when IsBool(v): n = n with { MusicEnabled = v.GetBoolean() }; break;
                case "agentsenabled" when IsBool(v): n = n with { AgentsEnabled = v.GetBoolean() }; break;
                case "musicplayer" when v.ValueKind == JsonValueKind.String: n = n with { MusicPlayer = v.GetString()! }; break;
                case "customplayerpath" when v.ValueKind is JsonValueKind.String or JsonValueKind.Null: n = n with { CustomPlayerPath = v.GetString() }; break;
                case "lyricsenabled" when IsBool(v): n = n with { LyricsEnabled = v.GetBoolean() }; break;
                case "lyricsinpill" when IsBool(v): n = n with { LyricsInPill = v.GetBoolean() }; break;
                case "pollintervalseconds" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int s): n = n with { PollIntervalSeconds = s }; break;
                case "shelfmaxitems" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int sh): n = n with { ShelfMaxItems = sh }; break;
                case "pillvolume" when IsBool(v): n = n with { PillVolume = v.GetBoolean() }; break;
                case "pillbrightness" when IsBool(v): n = n with { PillBrightness = v.GetBoolean() }; break;
                case "pillnotifications" when IsBool(v): n = n with { PillNotifications = v.GetBoolean() }; break;
                case "pillanimations" when IsBool(v): n = n with { PillAnimations = v.GetBoolean() }; break;
                case "pillhudseconds" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int hs): n = n with { PillHudSeconds = hs }; break;
                case "pillnotifyseconds" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int ns): n = n with { PillNotifySeconds = ns }; break;
                case "clipboardlimit" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int cl): n = n with { ClipboardLimit = cl }; break;
                case "clipboardpersist" when IsBool(v): n = n with { ClipboardPersist = v.GetBoolean() }; break;
                case "clipboardskipsecrets" when IsBool(v): n = n with { ClipboardSkipSecrets = v.GetBoolean() }; break;
                case "clipboardexclusionson" when IsBool(v): n = n with { ClipboardExclusionsOn = v.GetBoolean() }; break;
                case "sections" when v.ValueKind == JsonValueKind.Array:
                    var list = new List<NotchSection>();
                    foreach (var item in v.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                            && item.TryGetProperty("enabled", out var en) && IsBool(en)) list.Add(new NotchSection(id.GetString()!, en.GetBoolean()));
                    n = n with { Sections = list }; break;
            }
        }
        return n.Sanitize();
    }
    private static bool IsBool(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False;

    // ── Operaciones de la interfaz de ajustes (puras, para poder probarlas)
    public NotchSettings MoveSection(int index, int delta)
    {
        var l = Sections.ToList(); int to = index + delta;
        if (index < 0 || index >= l.Count || to < 0 || to >= l.Count) return this;
        (l[index], l[to]) = (l[to], l[index]); return this with { Sections = l };
    }
    public NotchSettings ToggleSection(int index)
    {
        if (index < 0 || index >= Sections.Count) return this;
        var l = Sections.ToList(); l[index] = l[index] with { Enabled = !l[index].Enabled }; return this with { Sections = l };
    }

    /// <summary>Lo que se envía por el canal (camelCase).</summary>
    public object ToWire() => new
    {
        position = Position, glassLevel = GlassLevel, sections = Sections.Select(s => new { id = s.Id, enabled = s.Enabled }).ToArray(), musicEnabled = MusicEnabled, agentsEnabled = AgentsEnabled,
        musicPlayer = MusicPlayer, customPlayerPath = CustomPlayerPath, lyricsEnabled = LyricsEnabled, lyricsInPill = LyricsInPill, pollIntervalSeconds = PollIntervalSeconds, shelfMaxItems = ShelfMaxItems,
        pillVolume = PillVolume, pillBrightness = PillBrightness, pillNotifications = PillNotifications, pillHudSeconds = PillHudSeconds, pillNotifySeconds = PillNotifySeconds, pillAnimations = PillAnimations,
        clipboardLimit = ClipboardLimit, clipboardPersist = ClipboardPersist, clipboardSkipSecrets = ClipboardSkipSecrets,
        clipboardExcludedApps = ClipboardExclusionsOn ? ParseApps(ClipboardExcludedApps).ToArray() : Array.Empty<string>(),
    };

    /// <summary>
    /// Importa los ajustes centrales del <c>settings.json</c> que NotchWin ya tenía (migración única y NO destructiva: el archivo original no se toca).
    /// Solo se leen los campos centrales; rutas de datos y cualquier clave/secreto (p. ej. <c>AnthropicAdminApiKey</c>) se ignoran a propósito. Devuelve null si no hay nada utilizable.
    /// </summary>
    public static NotchSettings? ImportFromNotchWin(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement; if (r.ValueKind != JsonValueKind.Object) return null;
            var n = new NotchSettings();
            foreach (var p in r.EnumerateObject())
            {
                var v = p.Value;
                switch (p.Name.ToLowerInvariant())
                {
                    case "notchposition": n = n with { Position = EnumName(v, Positions) ?? n.Position }; break;
                    case "musicplayer": n = n with { MusicPlayer = EnumName(v, Players) ?? n.MusicPlayer }; break;
                    case "customplayerpath" when v.ValueKind == JsonValueKind.String: n = n with { CustomPlayerPath = v.GetString() }; break;
                    case "glasslevel" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int g): n = n with { GlassLevel = g }; break;
                    case "musicmoduleenabled" when IsBool(v): n = n with { MusicEnabled = v.GetBoolean() }; break;
                    case "aiagentsmoduleenabled" when IsBool(v): n = n with { AgentsEnabled = v.GetBoolean() }; break;
                    case "lyricsenabled" when IsBool(v): n = n with { LyricsEnabled = v.GetBoolean() }; break;
                    case "lyricsinpill" when IsBool(v): n = n with { LyricsInPill = v.GetBoolean() }; break;
                    case "pollintervalseconds" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int s): n = n with { PollIntervalSeconds = s }; break;
                    case "shelfmaxitems" when v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int sh): n = n with { ShelfMaxItems = sh }; break;
                    case "notchsections" when v.ValueKind == JsonValueKind.Array:
                        var list = new List<NotchSection>();
                        foreach (var item in v.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.String && item.TryGetProperty("Enabled", out var en) && IsBool(en)) list.Add(new NotchSection(id.GetString()!, en.GetBoolean()));
                        n = n with { Sections = list }; break;
                }
            }
            return n.Sanitize();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>NotchWin guardaba los enums como número (orden de declaración) o como texto.</summary>
    private static string? EnumName(JsonElement v, string[] names) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) && i >= 0 && i < names.Length ? names[i]
        : v.ValueKind == JsonValueKind.String ? names.FirstOrDefault(x => string.Equals(x, v.GetString(), StringComparison.OrdinalIgnoreCase)) : null;
}
