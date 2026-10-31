using WinMac.Apps;

namespace WinMac.Dock;

/// <summary>
/// Combina apps ancladas + apps en ejecución + carpeta/papelera en la lista lógica de ítems, usando la IDENTIDAD estable de cada app (no solo la ruta).
/// Evita duplicados: una app anclada y en ejecución es UN ítem; las abiertas pero no ancladas aparecen como «recientes temporales», con orden estable.
/// </summary>
public sealed class DockModelBuilder
{
    private readonly Func<AppIdentity, string> _title;
    private readonly List<string> _firstSeen = new();
    public DockConfig Config { get; set; }

    public DockModelBuilder(DockConfig cfg, Func<AppIdentity, string>? titleResolver = null)
    {
        Config = cfg; _title = titleResolver ?? (i => i.DisplayName);
    }

    public static string Norm(string path) => AppIdentity.NormPath(path);
    public static string DefaultTitle(string path) => AppIdentity.StemTitle(path);

    private sealed class Group { public required AppIdentity Identity; public List<WindowInfo> Windows = new(); }

    public IReadOnlyList<DockItemModel> Build(IReadOnlyList<RunningApp> running, string? downloadsPath = null)
    {
        var cfg = Config; var groups = new List<Group>();
        foreach (var r in running)
        {
            var g = groups.FirstOrDefault(x => string.Equals(x.Identity.Id, r.Identity.Id, StringComparison.Ordinal));
            if (g is null) { g = new Group { Identity = r.Identity }; groups.Add(g); }
            g.Windows.AddRange(r.Windows);
            if (!_firstSeen.Contains(r.Identity.Id)) _firstSeen.Add(r.Identity.Id);
        }
        _firstSeen.RemoveAll(k => !groups.Any(g => g.Identity.Id == k));

        var items = new List<DockItemModel>(); var consumed = new HashSet<string>(); var seenPinned = new List<AppIdentity>();
        foreach (var p in cfg.Pinned)
        {
            var id = p.ToIdentity(); if (id is null || seenPinned.Any(x => x.SameApp(id))) continue;   // entrada corrupta o duplicada
            seenPinned.Add(id);
            var g = groups.FirstOrDefault(x => id.SameApp(x.Identity)); if (g is not null) consumed.Add(g.Identity.Id);
            items.Add(Make(id, string.IsNullOrWhiteSpace(p.Title) ? _title(id) : p.Title!, pinned: true, g?.Windows, p.IconOverride));
        }

        var recents = cfg.ShowRecents
            ? _firstSeen.Where(k => !consumed.Contains(k)).Take(Math.Max(0, cfg.MaxRecents)).Select(k => groups.First(g => g.Identity.Id == k)).ToList()
            : [];
        if (recents.Count > 0)
        {
            items.Add(new DockItemModel("sep:recents", DockItemKind.Separator, "", null));
            foreach (var g in recents) items.Add(Make(g.Identity, _title(g.Identity), pinned: false, g.Windows, null));
        }

        items.Add(new DockItemModel("sep:system", DockItemKind.Separator, "", null));
        items.Add(new DockItemModel("folder:downloads", DockItemKind.Folder, "Descargas", downloadsPath ?? cfg.DownloadsPath));
        items.Add(new DockItemModel("trash", DockItemKind.Trash, "Papelera", "shell:RecycleBinFolder"));
        return items;
    }

    private static DockItemModel Make(AppIdentity id, string title, bool pinned, List<WindowInfo>? w, string? iconOverride)
    {
        int n = w?.Count ?? 0;
        return new DockItemModel("app:" + id.Id, DockItemKind.App, title, id.LaunchTarget, pinned, n > 0, n,
            w?.Any(x => x.Active) ?? false, n > 0 && w!.All(x => x.Minimized), id, w?.ToList(), iconOverride);
    }
}
