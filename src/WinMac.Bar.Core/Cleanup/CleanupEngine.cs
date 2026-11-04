namespace WinMac.Bar.Cleanup;

public sealed record CleanupProgress(string CategoryId, string Phase, int Done, long Bytes);

/// <summary>
/// Análisis: recorre cada raíz SIN seguir enlaces (junctions, symlinks y cualquier reparse point se omiten por completo), solo archivos más antiguos que el mínimo de la categoría y
/// dentro de la raíz autorizada. Es incremental (callback de progreso), cancelable y no borra nada.
/// </summary>
public static class CleanupScanner
{
    public const int MaxItems = 200_000;

    public static ScanResult Scan(CleanupCategory cat, IFileSystem fs, IReadOnlyDictionary<string, string> env, DateTime nowUtc, Action<CleanupProgress>? progress, CancellationToken ct)
    {
        var items = new List<CleanItem>(); var skipped = new Dictionary<SkipReason, int>(); long total = 0; bool truncated = false; string? error = null;
        void Skip(SkipReason r) => skipped[r] = skipped.GetValueOrDefault(r) + 1;
        foreach (var root in cat.Roots)
        {
            if (!PathGuard.IsSafeRoot(root, env, out var why)) { error = $"raíz no segura ({why})"; continue; }
            var stack = new Stack<string>(); stack.Push(root); int visited = 0;
            while (stack.Count > 0 && !truncated)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop(); List<FsEntry> kids;
                try { kids = fs.Children(dir).ToList(); }
                catch (UnauthorizedAccessException) { Skip(SkipReason.Denied); continue; }
                catch (IOException) { Skip(SkipReason.Locked); continue; }
                foreach (var k in kids)
                {
                    if (k.IsReparse) { Skip(SkipReason.Reparse); continue; }                       // ni se sigue ni se borra
                    if (!PathGuard.IsInside(root, k.Path)) { Skip(SkipReason.OutOfScope); continue; }
                    if (k.IsDirectory) { stack.Push(k.Path); continue; }
                    if (cat.Extensions is { } ex && !ex.Any(e => k.Path.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                    if (nowUtc - k.LastWriteUtc < cat.MinAge) { Skip(SkipReason.TooRecent); continue; }
                    if (items.Count >= MaxItems) { truncated = true; break; }
                    items.Add(new CleanItem(k.Path, k.Length, k.LastWriteUtc)); total += k.Length;
                    if (++visited % 200 == 0) progress?.Invoke(new CleanupProgress(cat.Id, "Analizando " + cat.Title, items.Count, total));
                }
            }
        }

        progress?.Invoke(new CleanupProgress(cat.Id, "Analizando " + cat.Title, items.Count, total));
        return new ScanResult(cat.Id, items, total, skipped, truncated, false, error);
    }
}

/// <summary>
/// Limpieza: solo con confirmación explícita (<c>confirmed</c>), solo lo analizado, y REVALIDANDO cada archivo justo antes de borrarlo (sigue existiendo, no es un enlace, sigue dentro de la raíz,
/// no cambió de tamaño ni de fecha desde el análisis, sigue siendo lo bastante antiguo). Lo bloqueado o sin permiso se omite y se cuenta; el espacio liberado es el de los archivos
/// EFECTIVAMENTE borrados. Cancelable entre archivos.
/// </summary>
public static class CleanupExecutor
{
    public static CleanResult Run(CleanupCategory cat, ScanResult scan, IFileSystem fs, IReadOnlyDictionary<string, string> env, DateTime nowUtc, bool confirmed, Action<CleanupProgress>? progress, CancellationToken ct)
    {
        if (!confirmed) throw new InvalidOperationException("La limpieza exige confirmación explícita.");
        var skipped = new Dictionary<SkipReason, int>(); int deleted = 0; long freed = 0; bool cancelled = false;
        void Skip(SkipReason r) => skipped[r] = skipped.GetValueOrDefault(r) + 1;
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string? root = cat.Roots.FirstOrDefault(r => scan.Items.Count > 0 && PathGuard.IsInside(r, scan.Items[0].Path));
        foreach (var item in scan.Items)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            if (!cat.Roots.Any(r => PathGuard.IsInside(r, item.Path) && PathGuard.IsSafeRoot(r, env, out _))) { Skip(SkipReason.OutOfScope); continue; }
            var now = fs.Stat(item.Path);
            if (now is null) { Skip(SkipReason.Vanished); continue; }
            if (now.IsReparse || now.IsDirectory) { Skip(SkipReason.Reparse); continue; }
            if (now.Length != item.Length || now.LastWriteUtc != item.LastWriteUtc) { Skip(SkipReason.Changed); continue; }
            if (nowUtc - now.LastWriteUtc < cat.MinAge) { Skip(SkipReason.TooRecent); continue; }
            switch (fs.DeleteFile(item.Path))
            {
                case DeleteOutcome.Deleted: deleted++; freed += item.Length; { var d = PathGuard.Parent(item.Path); if (d is not null) dirs.Add(d); } break;
                case DeleteOutcome.Locked: Skip(SkipReason.Locked); break;
                case DeleteOutcome.Denied: Skip(SkipReason.Denied); break;
                default: Skip(SkipReason.Vanished); break;
            }

            if (deleted % 100 == 0) progress?.Invoke(new CleanupProgress(cat.Id, "Limpiando " + cat.Title, deleted, freed));
        }

        // carpetas que quedaron vacías (nunca la raíz, nunca un enlace)
        if (!cancelled)
            foreach (var d in dirs.OrderByDescending(x => x.Length))
                foreach (var r in cat.Roots.Where(r => PathGuard.IsInside(r, d))) { try { if (fs.Stat(d) is { IsReparse: false, IsDirectory: true }) fs.DeleteEmptyDirectory(d); } catch (Exception) { } }
        progress?.Invoke(new CleanupProgress(cat.Id, "Limpiando " + cat.Title, deleted, freed));
        return new CleanResult(cat.Id, deleted, freed, skipped, cancelled);
    }
}
