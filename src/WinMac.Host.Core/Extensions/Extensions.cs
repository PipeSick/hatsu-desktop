using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinMac.Host.Extensions;

/// <summary>What an extension may do. Nothing is granted that is not in its manifest, and the user sees the list before enabling it.</summary>
[Flags]
public enum ExtPermission { None = 0, Activity = 1, Notify = 2 }

/// <summary>
/// <c>extension.json</c> (docs/30): <c>{ "id": "pomodoro", "name": "…", "version": "1.0.0", "api": 1, "entry": "Demo.exe", "args": ["pomodoro"],
/// "permissions": ["activity", "notify"], "description": "…" }</c>. The entry must be an .exe INSIDE the extension's folder.
/// </summary>
public sealed partial record ExtensionManifest(string Id, string Name, string Version, int Api, string Entry, IReadOnlyList<string> Args, ExtPermission Permissions, string? Description)
{
    public const int ApiVersion = 1;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{2,39}$")] private static partial Regex IdRx();
    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")] private static partial Regex SemVer();

    public static (ExtensionManifest? Manifest, string? Error) Parse(string json, string folder)
    {
        try
        {
            using var d = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var e = d.RootElement;
            string? S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var id = S("id"); var name = S("name"); var ver = S("version"); var entry = S("entry");
            if (id is null || !IdRx().IsMatch(id)) return (null, "id no válido (minúsculas, números y guiones, 3–40)");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 60) return (null, "nombre no válido");
            if (ver is null || !SemVer().IsMatch(ver)) return (null, "versión no válida (x.y.z)");
            int api = e.TryGetProperty("api", out var a) && a.TryGetInt32(out int ai) ? ai : 0;
            if (api != ApiVersion) return (null, $"API {api} no compatible (esta versión de Hatsu Desktop admite la {ApiVersion})");
            if (entry is null || !entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || entry.Contains("..") || Path.IsPathRooted(entry)) return (null, "entrada no válida (un .exe dentro de su carpeta)");
            var full = Path.GetFullPath(Path.Combine(folder, entry));
            if (!full.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return (null, "la entrada sale de su carpeta");
            var args = e.TryGetProperty("args", out var ar) && ar.ValueKind == JsonValueKind.Array
                ? ar.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length is > 0 and <= 100).Take(8).ToList() : [];
            var perms = ExtPermission.None;
            if (e.TryGetProperty("permissions", out var pa) && pa.ValueKind == JsonValueKind.Array)
                foreach (var p in pa.EnumerateArray())
                    perms |= p.GetString() switch { "activity" => ExtPermission.Activity, "notify" => ExtPermission.Notify, _ => throw new FormatException("permiso desconocido: " + p.GetString()) };
            var desc = S("description"); if (desc is { Length: > 300 }) desc = desc[..300];
            return (new ExtensionManifest(id, name.Trim(), ver, api, entry, args, perms, desc), null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) { return (null, "manifiesto ilegible: " + ex.Message); }
    }

    public static string Describe(ExtPermission p) => p == ExtPermission.None ? "sin permisos" :
        string.Join(", ", new[] { (ExtPermission.Activity, "mostrar actividades en la Isla"), (ExtPermission.Notify, "mostrar avisos en la Isla") }.Where(x => p.HasFlag(x.Item1)).Select(x => x.Item2));
}

/// <summary>
/// Validates every message from an extension and translates it to WinMac's internal APIs. Only what the manifest grants; sizes, rates and values are checked;
/// ids are namespaced («ext:&lt;id&gt;:…») so an extension can never touch the Island's own activities or another extension's. Pure and tested.
/// </summary>
public sealed class ExtensionBroker
{
    public const string ActivityTopic = "ext.activity", NotifyTopic = "ext.notify";
    public const int MaxPayloadBytes = 8 * 1024, MaxPerSecond = 10, MaxInvalid = 20;
    private static readonly TimeSpan NotifyGap = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, (DateTime Window, int Count, int Invalid, DateTime LastNotify)> _stats = [];

    /// <summary>What to forward (topic + payload on WinMac's bus), or null to drop. <paramref name="kill"/> = the extension misbehaves and must be stopped.</summary>
    public (string Topic, object Payload)? Translate(ExtensionManifest m, string topic, JsonElement? data, DateTime now, out bool kill)
    {
        kill = false;
        var st = _stats.TryGetValue(m.Id, out var s) ? s : (now, 0, 0, DateTime.MinValue);
        if (now - st.Item1 >= TimeSpan.FromSeconds(1)) st = (now, 0, st.Item3, st.Item4);
        st.Item2++;
        (string, object)? result = null; bool invalid = false;
        if (st.Item2 > MaxPerSecond) invalid = true;
        else if (data is not { ValueKind: JsonValueKind.Object } d || d.GetRawText().Length > MaxPayloadBytes) invalid = true;
        else if (topic == ActivityTopic && m.Permissions.HasFlag(ExtPermission.Activity)) { result = Activity(m, d); invalid = result is null; }
        else if (topic == NotifyTopic && m.Permissions.HasFlag(ExtPermission.Notify))
        {
            if (now - st.Item4 < NotifyGap) result = null;   // too frequent: dropped, not counted as invalid
            else { result = Notify(m, d); invalid = result is null; if (result is not null) st.Item4 = now; }
        }
        else if (topic is "module.ready" or "pong") result = null;
        else invalid = true;   // unknown topic or not granted
        if (invalid) st.Item3++;
        _stats[m.Id] = st;
        kill = st.Item3 >= MaxInvalid;
        return result;
    }

    private static string? Str(JsonElement e, string n, int max) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } s && s.Length <= max ? s : null;

    private static (string, object)? Activity(ExtensionManifest m, JsonElement d)
    {
        var op = Str(d, "op", 10); var id = Str(d, "id", 40);
        if (op is not ("upsert" or "finish" or "remove") || id is null || !Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,40}$")) return null;
        string fid = "ext:" + m.Id + ":" + id;
        if (op != "upsert") return ("notch.activity", new { op, activity = new { id = fid, state = Str(d, "state", 12) is "failed" or "cancelled" ? Str(d, "state", 12) : "completed" } });
        var title = Str(d, "title", 80); if (string.IsNullOrWhiteSpace(title)) return null;
        double? progress = d.TryGetProperty("progress", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var pv) && pv is >= 0 and <= 1 ? pv : null;
        return ("notch.activity", new { op, activity = new { id = fid, kind = "extension", title, subtitle = Str(d, "subtitle", 120), source = m.Name, progress, state = Str(d, "state", 12) is "paused" ? "paused" : "active" } });
    }

    private static (string, object)? Notify(ExtensionManifest m, JsonElement d)
    {
        var title = Str(d, "title", 80); if (string.IsNullOrWhiteSpace(title)) return null;
        return ("notch.feed", new { kind = "notification", app = m.Name, title, body = Str(d, "body", 160) ?? "" });
    }

    /// <summary>A fresh random token for one launch of one extension.</summary>
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}

/// <summary>Installed extension: its folder, manifest (or the error that keeps it out) and whether the user enabled it.</summary>
public sealed record InstalledExtension(string Folder, ExtensionManifest? Manifest, string? Error, bool Enabled);

/// <summary>
/// Extensions on disk (<c>%LOCALAPPDATA%\WinMac\extensions\&lt;id&gt;\</c>) and their on/off state (<c>extensions.json</c>). Install copies a folder in
/// (disabled); uninstall deletes only that extension's own folder. Nothing outside the extensions folder is ever written or deleted.
/// </summary>
public sealed class ExtensionCatalog(string root)
{
    public string Root { get; } = root;
    private string StateFile => Path.Combine(Root, "extensions.json");

    public IReadOnlyList<InstalledExtension> List()
    {
        var on = State();
        if (!Directory.Exists(Root)) return [];
        var list = new List<InstalledExtension>();
        foreach (var dir in Directory.EnumerateDirectories(Root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Take(100))
        {
            var mf = Path.Combine(dir, "extension.json");
            if (!File.Exists(mf)) { list.Add(new(dir, null, "falta extension.json", false)); continue; }
            var (m, err) = ExtensionManifest.Parse(File.ReadAllText(mf), dir);
            if (m is not null && !string.Equals(Path.GetFileName(dir), m.Id, StringComparison.Ordinal)) (m, err) = (null, "la carpeta debe llamarse como su id");
            if (m is not null && !File.Exists(Path.Combine(dir, m.Entry))) (m, err) = (null, "no está su ejecutable");
            list.Add(new(dir, m, err, m is not null && on.Contains(m.Id)));
        }

        return list;
    }

    private HashSet<string> State()
    {
        try { return File.Exists(StateFile) ? (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StateFile)) ?? []).ToHashSet() : []; }
        catch (JsonException) { return []; }
    }

    public void SetEnabled(string id, bool enabled)
    {
        var s = State(); if (enabled) s.Add(id); else s.Remove(id);
        Directory.CreateDirectory(Root); File.WriteAllText(StateFile, JsonSerializer.Serialize(s.OrderBy(x => x).ToList()));
    }

    /// <summary>Copies a folder that contains an extension.json into the catalog (replacing an older copy of the SAME id). Installed disabled.</summary>
    public (string? Id, string? Error) Install(string source)
    {
        try
        {
            if (!Path.IsPathFullyQualified(source) || !File.Exists(Path.Combine(source, "extension.json"))) return (null, "la carpeta no contiene extension.json");
            var (m, err) = ExtensionManifest.Parse(File.ReadAllText(Path.Combine(source, "extension.json")), source);
            if (m is null) return (null, err);
            var target = Path.Combine(Root, m.Id);
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\', '/'), Path.GetFullPath(target).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return (m.Id, null);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);   // only that extension's own folder
            CopyDir(source, target, 0);
            SetEnabled(m.Id, false);
            return (m.Id, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (null, e.Message); }
    }

    public bool Uninstall(string id)
    {
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{2,39}$")) return false;
        var target = Path.Combine(Root, id);
        SetEnabled(id, false);
        if (!Directory.Exists(target)) return false;
        Directory.Delete(target, recursive: true); return true;
    }

    private static void CopyDir(string from, string to, int depth)
    {
        if (depth > 4) return;
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)), depth + 1);
    }
}
