using WinMac.Apps;

namespace WinMac.Dock;

/// <summary>Operaciones puras sobre las apps ancladas (cada una devuelve una configuración NUEVA; la original no se modifica). Sin E/S: el llamador decide cuándo guardar.</summary>
public static class DockPinning
{
    private static int IndexOf(DockConfig c, AppIdentity id) => c.Pinned.FindIndex(p => p.ToIdentity()?.SameApp(id) == true);
    private static int IndexOfId(DockConfig c, string id)
    {
        int i = c.Pinned.FindIndex(p => string.Equals(p.StableId, id, StringComparison.Ordinal)); if (i >= 0) return i;
        return c.Pinned.FindIndex(p => p.ToIdentity() is { } x && (string.Equals(x.Id, id, StringComparison.Ordinal) || (x.ExePath is not null && string.Equals(AppIdentity.NormPath(x.ExePath), AppIdentity.NormPath(id.StartsWith("exe:") ? id[4..] : id), StringComparison.Ordinal))));
    }

    public static bool IsPinned(DockConfig c, AppIdentity id) => IndexOf(c, id) >= 0;

    /// <summary>Ancla una app. Si ya estaba anclada (misma app), no hace nada (sin duplicados).</summary>
    public static DockConfig Pin(DockConfig c, AppIdentity id, string? title = null, int? index = null)
    {
        if (IndexOf(c, id) >= 0) return c;
        var list = new List<PinnedApp>(c.Pinned);
        var entry = new PinnedApp { Id = id.Id, Path = id.ExePath, Aumid = id.Aumid, Title = string.IsNullOrWhiteSpace(title) ? id.DisplayName : title };
        list.Insert(Math.Clamp(index ?? list.Count, 0, list.Count), entry);
        return c with { Pinned = list };
    }

    /// <summary>Desancla por Id estable (o por ruta del ejecutable). Si no estaba anclada, no hace nada.</summary>
    public static DockConfig Unpin(DockConfig c, string id)
    {
        int i = IndexOfId(c, id); if (i < 0) return c;
        var list = new List<PinnedApp>(c.Pinned); list.RemoveAt(i); return c with { Pinned = list };
    }

    public static DockConfig Move(DockConfig c, string id, int newIndex)
    {
        int i = IndexOfId(c, id); if (i < 0) return c;
        var list = new List<PinnedApp>(c.Pinned); var it = list[i]; list.RemoveAt(i); list.Insert(Math.Clamp(newIndex, 0, list.Count), it);
        return c with { Pinned = list };
    }

    public static DockConfig MoveBy(DockConfig c, string id, int delta) { int i = IndexOfId(c, id); return i < 0 ? c : Move(c, id, i + delta); }

    /// <summary>Reordena según una lista de Id (resultado de arrastrar). Los Id desconocidos se ignoran; los anclados que falten quedan al final en su orden.</summary>
    public static DockConfig ReorderTo(DockConfig c, IReadOnlyList<string> orderedIds)
    {
        var rest = new List<PinnedApp>(c.Pinned); var list = new List<PinnedApp>();
        foreach (var id in orderedIds) { int i = rest.FindIndex(p => string.Equals(p.StableId, id, StringComparison.Ordinal)); if (i >= 0) { list.Add(rest[i]); rest.RemoveAt(i); } }
        list.AddRange(rest); return c with { Pinned = list };
    }

    public static DockConfig SetIconOverride(DockConfig c, string id, string? path)
    {
        int i = IndexOfId(c, id); if (i < 0) return c;
        var list = new List<PinnedApp>(c.Pinned); list[i] = list[i] with { IconOverride = string.IsNullOrWhiteSpace(path) ? null : path }; return c with { Pinned = list };
    }

    public static IReadOnlyList<string> Order(DockConfig c) => c.Pinned.Select(p => p.StableId).ToList();
}
