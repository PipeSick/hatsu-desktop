using System.Globalization;

namespace WinMac.Bar.Cleanup;

public enum CleanupPhase { Idle, Scanning, Ready, Cleaning, Done }

public sealed record CleanupRow(CleanupCategory Category, long Bytes, int Files, bool Checked, bool Selectable, bool Analyzed, string? Note);

public static class CleanupText
{
    public static string Size(long b) => b switch
    {
        < 1024 => b + " B",
        < 1024 * 1024 => (b / 1024.0).ToString("0", CultureInfo.GetCultureInfo("es-ES")) + " KB",
        < 1024L * 1024 * 1024 => (b / 1048576.0).ToString("0.#", CultureInfo.GetCultureInfo("es-ES")) + " MB",
        _ => (b / 1073741824.0).ToString("0.##", CultureInfo.GetCultureInfo("es-ES")) + " GB",
    };

    public static string Skipped(IReadOnlyDictionary<SkipReason, int> s)
    {
        var parts = new List<string>();
        void A(SkipReason r, string t) { if (s.GetValueOrDefault(r) is > 0 and var n) parts.Add($"{n} {t}"); }
        A(SkipReason.Locked, "en uso"); A(SkipReason.Denied, "sin permiso"); A(SkipReason.Changed, "cambiaron tras el análisis"); A(SkipReason.TooRecent, "demasiado recientes");
        A(SkipReason.Vanished, "ya no existían"); A(SkipReason.Reparse, "enlaces (no se siguen)"); A(SkipReason.OutOfScope, "fuera del ámbito");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Una sesión de Cleanup: analizar → elegir → confirmar → limpiar. Nada se borra sin <see cref="Confirm"/>, y la Papelera exige además <see cref="ConfirmRecycleBin"/>. En memoria; no hay servicios
/// ni tareas en segundo plano: cada paso lo lanza el usuario. Las operaciones largas se ejecutan en el hilo que las llama (el anfitrión usa uno propio) y son cancelables.
/// </summary>
public sealed class CleanupSession
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<CleanupCategory> _cats; private readonly IFileSystem _fs; private readonly IRecycleBin _bin; private readonly IReadOnlyDictionary<string, string> _env; private readonly Func<DateTime> _now;
    private readonly Dictionary<string, ScanResult> _scans = new(); private readonly Dictionary<string, CleanResult> _results = new(); private readonly HashSet<string> _checked = new();
    private (long Bytes, long Items)? _binInfo; private bool _confirmed, _binConfirmed; private long _binFreed; private bool? _binEmptied;
    private CleanupCancel? _cancel;

    public CleanupSession(IReadOnlyList<CleanupCategory> cats, IFileSystem fs, IRecycleBin bin, IReadOnlyDictionary<string, string> env, Func<DateTime>? now = null)
    { _cats = cats; _fs = fs; _bin = bin; _env = env; _now = now ?? (() => DateTime.UtcNow); }

    public CleanupPhase Phase { get; private set; }
    public string Status { get; private set; } = "";
    public double? Fraction { get; private set; }
    public bool WasCancelled { get; private set; }
    public event Action? Changed;

    private void Notify() { try { Changed?.Invoke(); } catch (Exception) { } }

    public IReadOnlyList<CleanupRow> Rows()
    {
        lock (_gate)
            return _cats.Select(c =>
            {
                if (c.Kind == CleanupKind.ReviewOnly) return new CleanupRow(c, 0, 0, false, false, false, c.WhyNotCleanable);
                if (c.Kind == CleanupKind.RecycleBin) return new CleanupRow(c, _binInfo?.Bytes ?? 0, (int)Math.Min(int.MaxValue, _binInfo?.Items ?? 0), _checked.Contains(c.Id), _binInfo is { Bytes: > 0 }, _binInfo is not null, _binInfo is null && Phase != CleanupPhase.Idle ? "Windows no informa el tamaño" : null);
                _scans.TryGetValue(c.Id, out var s);
                return new CleanupRow(c, s?.TotalBytes ?? 0, s?.Items.Count ?? 0, _checked.Contains(c.Id), s is { TotalBytes: > 0 }, s is not null, s?.Failed == true ? s.Error : s?.Truncated == true ? "Hay más archivos de los que se listan" : null);
            }).ToList();
    }

    public long SelectedBytes { get { lock (_gate) return Rows().Where(r => r.Checked && r.Selectable).Sum(r => r.Bytes); } }
    public bool RecycleBinSelected { get { lock (_gate) return _checked.Contains("recycle-bin") && _binInfo is { Bytes: > 0 }; } }
    public CleanResult? ResultFor(string id) { lock (_gate) return _results.GetValueOrDefault(id); }
    public long FreedBytes { get { lock (_gate) return _results.Values.Sum(r => r.FreedBytes) + _binFreed; } }
    public bool? RecycleBinEmptied { get { lock (_gate) return _binEmptied; } }

    public void Toggle(string id)
    {
        lock (_gate)
        {
            if (Phase is CleanupPhase.Scanning or CleanupPhase.Cleaning) return;
            var row = Rows().FirstOrDefault(r => r.Category.Id == id); if (row is null || !row.Selectable) return;
            if (!_checked.Remove(id)) _checked.Add(id);
        }
        Notify();
    }

    public void Cancel() { lock (_gate) _cancel?.Cancel(); }

    /// <summary>Confirmación explícita del usuario de que quiere limpiar LO SELECCIONADO. Sin ella, <see cref="Clean"/> no borra nada.</summary>
    public void Confirm() { lock (_gate) _confirmed = true; }
    public void ConfirmRecycleBin() { lock (_gate) _binConfirmed = true; }

    /// <summary>Analiza todas las categorías (no borra nada). Marca por defecto lo que se puede limpiar, salvo la Papelera (hay que marcarla a propósito).</summary>
    public void Scan(Action<CleanupProgress>? external = null)
    {
        CancellationToken ct;
        lock (_gate) { if (Phase is CleanupPhase.Scanning or CleanupPhase.Cleaning) return; _cancel = new CleanupCancel(); ct = _cancel.Token; Phase = CleanupPhase.Scanning; WasCancelled = false; _scans.Clear(); _results.Clear(); _checked.Clear(); _confirmed = _binConfirmed = false; _binFreed = 0; _binEmptied = null; Fraction = 0; Status = "Analizando…"; }
        Notify();
        var folders = _cats.Where(c => c.Kind == CleanupKind.Folder).ToList(); int i = 0;
        try
        {
            foreach (var c in folders)
            {
                ct.ThrowIfCancellationRequested();
                lock (_gate) { Status = "Analizando " + c.Title.ToLowerInvariant(); Fraction = (double)i / (folders.Count + 1); }
                Notify();
                ScanResult r;
                try { r = CleanupScanner.Scan(c, _fs, _env, _now(), p => { external?.Invoke(p); }, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { r = ScanResult.Empty(c.Id, "No se pudo analizar (" + e.GetType().Name + ")"); }
                lock (_gate) { _scans[c.Id] = r; if (r.TotalBytes > 0) _checked.Add(c.Id); }
                i++;
            }

            lock (_gate) { Status = "Consultando la Papelera"; }
            try { var q = _bin.Query(); lock (_gate) _binInfo = q; } catch (Exception) { lock (_gate) _binInfo = null; }
            lock (_gate) { Phase = CleanupPhase.Ready; Fraction = 1; Status = Rows().Where(r => r.Category.Kind != CleanupKind.RecycleBin).Sum(r => r.Bytes) is > 0 and var b ? $"Espacio recuperable encontrado: {CleanupText.Size(b)}" : "No hay nada que limpiar"; }
        }
        catch (OperationCanceledException) { lock (_gate) { Phase = CleanupPhase.Ready; WasCancelled = true; Status = "Análisis cancelado"; Fraction = null; } }
        Notify();
    }

    /// <summary>Limpia lo seleccionado. Exige <see cref="Confirm"/> (y <see cref="ConfirmRecycleBin"/> si la Papelera está marcada). Revalida cada archivo antes de borrarlo.</summary>
    public void Clean(Action<CleanupProgress>? external = null)
    {
        CancellationToken ct; List<CleanupCategory> todo; bool bin;
        lock (_gate)
        {
            if (Phase != CleanupPhase.Ready || !_confirmed) return;                                    // analizar primero, confirmar después
            bin = _checked.Contains("recycle-bin") && _binInfo is { Bytes: > 0 };
            if (bin && !_binConfirmed) return;                                                          // la Papelera, además, con su propia confirmación
            todo = _cats.Where(c => c.Kind == CleanupKind.Folder && _checked.Contains(c.Id) && _scans.ContainsKey(c.Id)).ToList();
            _cancel = new CleanupCancel(); ct = _cancel.Token; Phase = CleanupPhase.Cleaning; WasCancelled = false; Fraction = 0; Status = "Limpiando archivos seleccionados";
        }

        Notify(); int i = 0; int total = todo.Count + (bin ? 1 : 0);
        try
        {
            foreach (var c in todo)
            {
                ct.ThrowIfCancellationRequested();
                lock (_gate) { Status = "Limpiando " + c.Title.ToLowerInvariant(); Fraction = total == 0 ? 1 : (double)i / total; }
                Notify();
                var r = CleanupExecutor.Run(c, _scans[c.Id], _fs, _env, _now(), true, p => external?.Invoke(p), ct);
                lock (_gate) _results[c.Id] = r;
                if (r.Cancelled) throw new OperationCanceledException();
                i++;
            }

            if (bin)
            {
                ct.ThrowIfCancellationRequested();
                lock (_gate) { Status = "Vaciando la Papelera"; }
                Notify();
                long before = _binInfo?.Bytes ?? 0; bool ok = false; try { ok = _bin.Empty(); } catch (Exception) { }
                long after = 0; try { after = _bin.Query()?.Bytes ?? (ok ? 0 : before); } catch (Exception) { after = ok ? 0 : before; }
                lock (_gate) { _binEmptied = ok; _binFreed = ok ? Math.Max(0, before - after) : 0; }
            }
        }
        catch (OperationCanceledException) { lock (_gate) WasCancelled = true; }
        lock (_gate)
        {
            Phase = CleanupPhase.Done; Fraction = 1; _confirmed = _binConfirmed = false;
            int skipped = _results.Values.Sum(r => r.SkippedCount); int deleted = _results.Values.Sum(r => r.Deleted);
            Status = WasCancelled ? $"Limpieza cancelada: se liberaron {CleanupText.Size(FreedBytes)}" : skipped > 0 || _binEmptied == false ? $"Limpieza parcial: se liberaron {CleanupText.Size(FreedBytes)}" : $"Limpieza completada: {CleanupText.Size(FreedBytes)} liberados";
        }

        Notify();
    }

    public void Reset() { lock (_gate) { if (Phase is CleanupPhase.Scanning or CleanupPhase.Cleaning) return; Phase = CleanupPhase.Idle; _scans.Clear(); _results.Clear(); _checked.Clear(); _binInfo = null; _confirmed = _binConfirmed = false; Status = ""; Fraction = null; WasCancelled = false; } Notify(); }

    public string SummaryLine()
    {
        lock (_gate)
        {
            long freed = FreedBytes; int skipped = _results.Values.Sum(r => r.SkippedCount); var sk = new Dictionary<SkipReason, int>();
            foreach (var r in _results.Values) foreach (var (k, v) in r.Skipped) sk[k] = sk.GetValueOrDefault(k) + v;
            string detail = CleanupText.Skipped(sk);
            return $"Liberado: {CleanupText.Size(freed)} ({_results.Values.Sum(r => r.Deleted)} archivos" + (_binEmptied == true ? ", Papelera vaciada" : "") + ")" + (skipped > 0 ? $". Omitidos: {detail}." : ".") + (_binEmptied == false ? " La Papelera no se pudo vaciar." : "");
        }
    }

    private sealed class CleanupCancel { private readonly CancellationTokenSource _c = new(); public CancellationToken Token => _c.Token; public void Cancel() => _c.Cancel(); }
}

/// <summary>Progreso de Cleanup como actividad de la Isla (API interna de actividades: tópico notch.activity). Es una actividad de tipo extensión: no sustituye música, letras ni notificaciones.</summary>
public static class CleanupIsland
{
    public const string Id = "winmac-cleanup";

    public static object? Payload(CleanupSession s)
    {
        switch (s.Phase)
        {
            case CleanupPhase.Scanning: return Upsert("Analizando archivos temporales", s.Status, s.Fraction);
            case CleanupPhase.Cleaning: return Upsert("Limpiando archivos seleccionados", s.Status, s.Fraction);
            case CleanupPhase.Done: return new { op = "finish", activity = new { id = Id, state = s.WasCancelled ? "cancelled" : s.Status.Contains("parcial", StringComparison.OrdinalIgnoreCase) ? "failed" : "completed" } };
            default: return null;
        }
    }

    /// <summary>Resumen breve al terminar el análisis, para la Isla (una sola actividad finalizada con el resultado).</summary>
    public static object? ScanFinished(CleanupSession s) => s.Phase == CleanupPhase.Ready && !s.WasCancelled
        ? new { op = "upsert", activity = new { id = Id, kind = "extension", title = s.Status, subtitle = "WinMac Cleanup", source = "WinMac Cleanup", progress = (double?)null, state = "paused" } }
        : null;

    private static object Upsert(string title, string sub, double? progress) =>
        new { op = "upsert", activity = new { id = Id, kind = "extension", title, subtitle = sub, source = "WinMac Cleanup", progress = progress is { } p ? (double?)Math.Clamp(p, 0, 1) : null, state = "active" } };
}
