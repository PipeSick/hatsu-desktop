namespace WinMac.Dock;

/// <summary>Combina apps ancladas + apps en ejecución + carpeta/papelera en la lista lógica de ítems. Mantiene un orden estable de «recientes».</summary>
public sealed class DockModelBuilder
{
    private readonly DockConfig _cfg;
    private readonly Func<string, string> _title;
    private readonly List<string> _firstSeen = new();

    public DockModelBuilder(DockConfig cfg, Func<string, string>? titleResolver = null)
    {
        _cfg = cfg; _title = titleResolver ?? DefaultTitle;
    }

    public static string Norm(string path) => path.Replace('/', '\\').Trim().ToUpperInvariant();

    public static string DefaultTitle(string path)
    {
        string file = path.Replace('/', '\\'); int i = file.LastIndexOf('\\'); if (i >= 0) file = file[(i + 1)..];
        int dot = file.LastIndexOf('.'); if (dot > 0) file = file[..dot];
        return file.Length == 0 ? path : char.ToUpperInvariant(file[0]) + file[1..];
    }

    public IReadOnlyList<DockItemModel> Build(IReadOnlyList<RunningApp> running, string? downloadsPath = null)
    {
        var byPath = new Dictionary<string, List<WindowInfo>>();
        foreach (var r in running)
        {
            string k = Norm(r.ExePath);
            if (!byPath.TryGetValue(k, out var l)) byPath[k] = l = [];
            l.AddRange(r.Windows);
            if (!_firstSeen.Contains(k)) _firstSeen.Add(k);
        }
        _firstSeen.RemoveAll(k => !byPath.ContainsKey(k));

        var items = new List<DockItemModel>();
        var pinnedKeys = new HashSet<string>();
        foreach (var p in _cfg.Pinned)
        {
            string k = Norm(p.Path); pinnedKeys.Add(k);
            byPath.TryGetValue(k, out var w);
            items.Add(Make(k, p.Path, p.Title ?? _title(p.Path), true, w));
        }

        var recents = _cfg.ShowRecents
            ? _firstSeen.Where(k => !pinnedKeys.Contains(k)).Take(Math.Max(0, _cfg.MaxRecents)).ToList()
            : [];
        if (recents.Count > 0)
        {
            items.Add(new DockItemModel("sep:recents", DockItemKind.Separator, "", null));
            foreach (var k in recents)
            {
                string original = running.First(r => Norm(r.ExePath) == k).ExePath;
                items.Add(Make(k, original, _title(original), false, byPath[k]));
            }
        }

        items.Add(new DockItemModel("sep:system", DockItemKind.Separator, "", null));
        items.Add(new DockItemModel("folder:downloads", DockItemKind.Folder, "Descargas", downloadsPath ?? _cfg.DownloadsPath));
        items.Add(new DockItemModel("trash", DockItemKind.Trash, "Papelera", "shell:RecycleBinFolder"));
        return items;
    }

    private static DockItemModel Make(string key, string path, string title, bool pinned, List<WindowInfo>? w)
    {
        int n = w?.Count ?? 0;
        return new DockItemModel("app:" + key, DockItemKind.App, title, path, pinned, n > 0, n,
            w?.Any(x => x.Active) ?? false, n > 0 && w!.All(x => x.Minimized));
    }
}
