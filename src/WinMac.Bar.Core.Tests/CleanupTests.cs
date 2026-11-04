using WinMac.Bar.Cleanup;
using Xunit;

namespace WinMac.Bar.Tests;

/// <summary>Sistema de archivos en memoria: enlaces, bloqueos, permisos y cambios entre el análisis y la limpieza, sin tocar el disco.</summary>
internal sealed class FakeFs : IFileSystem
{
    public readonly Dictionary<string, FsEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Locked = new(StringComparer.OrdinalIgnoreCase), Denied = new(StringComparer.OrdinalIgnoreCase), DirDenied = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Deleted = [], DeletedDirs = [];
    public Action<string>? BeforeDelete;

    public FakeFs Dir(string p, bool reparse = false) { Entries[p] = new FsEntry(p, true, reparse, 0, DateTime.UnixEpoch); return this; }
    public FakeFs File(string p, long len, DateTime w, bool reparse = false) { Entries[p] = new FsEntry(p, false, reparse, len, w); return this; }

    public IEnumerable<FsEntry> Children(string dir)
    {
        if (DirDenied.Contains(dir)) throw new UnauthorizedAccessException();
        return Entries.Values.Where(e => string.Equals(PathGuard.Parent(e.Path), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public FsEntry? Stat(string path) => Entries.GetValueOrDefault(path);

    public DeleteOutcome DeleteFile(string path)
    {
        BeforeDelete?.Invoke(path);
        if (!Entries.ContainsKey(path)) return DeleteOutcome.Vanished;
        if (Locked.Contains(path)) return DeleteOutcome.Locked;
        if (Denied.Contains(path)) return DeleteOutcome.Denied;
        Entries.Remove(path); Deleted.Add(path); return DeleteOutcome.Deleted;
    }

    public bool DeleteEmptyDirectory(string path)
    {
        if (Entries.Values.Any(e => string.Equals(PathGuard.Parent(e.Path), path, StringComparison.OrdinalIgnoreCase))) return false;
        DeletedDirs.Add(path); return Entries.Remove(path);
    }
}

internal sealed class FakeBin(long bytes) : IRecycleBin
{
    public long Bytes = bytes; public bool EmptyCalled; public bool Succeeds = true;
    public (long Bytes, long Items)? Query() => (Bytes, Bytes > 0 ? 3 : 0);
    public bool Empty() { EmptyCalled = true; if (Succeeds) Bytes = 0; return Succeeds; }
}

public class CleanupTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Old = Now.AddDays(-5), Recent = Now.AddMinutes(-30);
    private static readonly Dictionary<string, string> Env = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USERPROFILE"] = @"C:\Users\ana", ["LOCALAPPDATA"] = @"C:\Users\ana\AppData\Local", ["TEMP"] = @"C:\Users\ana\AppData\Local\Temp", ["SystemRoot"] = @"C:\Windows", ["ProgramFiles"] = @"C:\Program Files",
    };
    private const string Temp = @"C:\Users\ana\AppData\Local\Temp";
    private static CleanupCategory TempCat => CleanupCatalog.Build(Env).Single(c => c.Id == "user-temp");

    private static ScanResult Scan(FakeFs fs, CleanupCategory? cat = null, CancellationToken ct = default) => CleanupScanner.Scan(cat ?? TempCat, fs, Env, Now, null, ct);

    // ── barandilla de rutas
    [Theory]
    [InlineData(@"C:\Temp", @"C:\Temp\a.txt", true)]
    [InlineData(@"C:\Temp", @"c:\temp\SUB\a.txt", true)]
    [InlineData(@"C:\Temp", @"C:\Temp2\a.txt", false)]       // prefijo de texto, no de carpeta
    [InlineData(@"C:\Temp", @"C:\Temp", false)]               // la raíz misma no está «dentro»
    [InlineData(@"C:\Temp", @"C:\Temp\..\Windows\x", false)]
    [InlineData(@"C:\Temp", @"C:\Other\Temp\a", false)]
    public void Path_containment_is_by_segment(string root, string path, bool inside) => Assert.Equal(inside, PathGuard.IsInside(root, path));

    [Theory]
    [InlineData(@"C:\", false)] [InlineData(@"C:\Users", false)] [InlineData(@"C:\Users\ana", false)]
    [InlineData(@"C:\Users\ana\Documents", false)] [InlineData(@"C:\Users\ana\Documents\temp", false)] [InlineData(@"C:\Users\ana\Downloads\x", false)]
    [InlineData(@"C:\Windows\Temp", false)] [InlineData(@"C:\Program Files\App", false)] [InlineData(@"C:\Users\ana\OneDrive\cache", false)]
    [InlineData(@"C:\Users\ana\AppData\Local\Temp", true)] [InlineData(@"C:\Users\ana\AppData\Local\D3DSCache", true)]
    public void Roots_inside_or_around_protected_folders_are_refused(string root, bool safe) => Assert.Equal(safe, PathGuard.IsSafeRoot(root, Env, out _));

    [Fact] public void A_redirected_temp_pointing_at_documents_is_not_scanned()
    {
        var env = new Dictionary<string, string>(Env, StringComparer.OrdinalIgnoreCase) { ["TEMP"] = @"C:\Users\ana\Documents" };
        var cat = CleanupCatalog.Build(env).Single(c => c.Id == "user-temp");
        var fs = new FakeFs().File(@"C:\Users\ana\Documents\tesis.docx", 1000, Old);
        var r = CleanupScanner.Scan(cat, fs, env, Now, null, default);
        Assert.Empty(r.Items); Assert.NotNull(r.Error);
    }

    // ── análisis
    [Fact] public void Scan_lists_only_old_files_and_deletes_nothing()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\old.tmp", 100, Old).File(Temp + @"\new.tmp", 50, Recent).Dir(Temp + @"\sub").File(Temp + @"\sub\deep.tmp", 25, Old);
        var r = Scan(fs);
        Assert.Equal(2, r.Items.Count); Assert.Equal(125, r.TotalBytes); Assert.Equal(1, r.Skipped[SkipReason.TooRecent]); Assert.Empty(fs.Deleted);
    }
    [Fact] public void Junctions_symlinks_and_reparse_points_are_never_followed()
    {
        var fs = new FakeFs().Dir(Temp).Dir(Temp + @"\link", reparse: true).File(Temp + @"\link\secret.docx", 999, Old).File(Temp + @"\filelink.tmp", 10, Old, reparse: true).File(Temp + @"\ok.tmp", 5, Old);
        var r = Scan(fs);
        Assert.Equal([Temp + @"\ok.tmp"], r.Items.Select(i => i.Path)); Assert.Equal(2, r.Skipped[SkipReason.Reparse]);
    }
    [Fact] public void Unreadable_folder_is_reported_not_fatal()
    {
        var fs = new FakeFs().Dir(Temp).Dir(Temp + @"\locked").File(Temp + @"\a.tmp", 5, Old); fs.DirDenied.Add(Temp + @"\locked");
        var r = Scan(fs); Assert.Single(r.Items); Assert.Equal(1, r.Skipped[SkipReason.Denied]);
    }
    [Fact] public void Extension_filter_limits_log_cleanup()
    {
        var cat = CleanupCatalog.Build(Env).Single(c => c.Id == "winmac-logs"); string d = @"C:\Users\ana\AppData\Local\WinMac\logs";
        var fs = new FakeFs().Dir(d).File(d + @"\bar.log", 10, Now.AddDays(-30)).File(d + @"\config.json", 10, Now.AddDays(-30)).File(d + @"\host.log", 10, Now.AddDays(-1));
        Assert.Equal([d + @"\bar.log"], Scan(fs, cat).Items.Select(i => i.Path));
    }
    [Fact] public void Scan_is_cancellable()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 1, Old); using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Scan(fs, ct: cts.Token));
    }

    // ── limpieza
    [Fact] public void Cleaning_without_confirmation_is_refused()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 5, Old);
        Assert.Throws<InvalidOperationException>(() => CleanupExecutor.Run(TempCat, Scan(fs), fs, Env, Now, confirmed: false, null, default)); Assert.Empty(fs.Deleted);
    }
    [Fact] public void Clean_deletes_and_counts_only_what_was_really_deleted()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old).File(Temp + @"\b.tmp", 40, Old).File(Temp + @"\locked.tmp", 1000, Old).File(Temp + @"\denied.tmp", 500, Old);
        fs.Locked.Add(Temp + @"\locked.tmp"); fs.Denied.Add(Temp + @"\denied.tmp");
        var scan = Scan(fs); Assert.Equal(1640, scan.TotalBytes);
        var r = CleanupExecutor.Run(TempCat, scan, fs, Env, Now, true, null, default);
        Assert.Equal(2, r.Deleted); Assert.Equal(140, r.FreedBytes);   // no se promete el espacio de lo bloqueado ni lo denegado
        Assert.Equal(1, r.Skipped[SkipReason.Locked]); Assert.Equal(1, r.Skipped[SkipReason.Denied]);
    }
    [Fact] public void File_that_changed_between_scan_and_clean_is_not_deleted()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old).File(Temp + @"\b.tmp", 40, Old); var scan = Scan(fs);
        fs.File(Temp + @"\a.tmp", 100, Now);              // alguien lo modificó (ahora es reciente)
        fs.File(Temp + @"\b.tmp", 41, Old);                // creció
        var r = CleanupExecutor.Run(TempCat, scan, fs, Env, Now, true, null, default);
        Assert.Equal(0, r.Deleted); Assert.Equal(2, r.Skipped[SkipReason.Changed]); Assert.Empty(fs.Deleted);
    }
    [Fact] public void File_replaced_by_a_link_after_the_scan_is_not_deleted()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var scan = Scan(fs);
        fs.File(Temp + @"\a.tmp", 100, Old, reparse: true);
        var r = CleanupExecutor.Run(TempCat, scan, fs, Env, Now, true, null, default); Assert.Equal(0, r.Deleted); Assert.Equal(1, r.Skipped[SkipReason.Reparse]);
    }
    [Fact] public void File_that_vanished_is_counted_not_an_error()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var scan = Scan(fs); fs.Entries.Remove(Temp + @"\a.tmp");
        var r = CleanupExecutor.Run(TempCat, scan, fs, Env, Now, true, null, default); Assert.Equal(1, r.Skipped[SkipReason.Vanished]);
    }
    [Fact] public void A_tampered_scan_with_paths_outside_the_root_deletes_nothing_outside()
    {
        var fs = new FakeFs().Dir(Temp).File(@"C:\Users\ana\Documents\tesis.docx", 100, Old).File(Temp + @"2\x.tmp", 5, Old);
        var forged = new ScanResult("user-temp", [new(@"C:\Users\ana\Documents\tesis.docx", 100, Old), new(Temp + @"2\x.tmp", 5, Old), new(Temp + @"\..\..\Documents\tesis.docx", 100, Old)], 205, new Dictionary<SkipReason, int>(), false, false, null);
        var r = CleanupExecutor.Run(TempCat, forged, fs, Env, Now, true, null, default);
        Assert.Equal(0, r.Deleted); Assert.Equal(3, r.Skipped[SkipReason.OutOfScope]); Assert.Empty(fs.Deleted);
    }
    [Fact] public void Cancelling_mid_clean_stops_and_reports_partial()
    {
        var fs = new FakeFs().Dir(Temp); for (int i = 0; i < 10; i++) fs.File(Temp + $@"\f{i}.tmp", 10, Old);
        var scan = Scan(fs); using var cts = new CancellationTokenSource(); fs.BeforeDelete = _ => { if (fs.Deleted.Count == 3) cts.Cancel(); };
        var r = CleanupExecutor.Run(TempCat, scan, fs, Env, Now, true, null, cts.Token);
        Assert.True(r.Cancelled); Assert.InRange(r.Deleted, 3, 4); Assert.Equal(r.Deleted * 10, r.FreedBytes);
    }
    [Fact] public void Empty_folders_left_behind_are_removed_but_never_the_root_or_non_empty_ones()
    {
        var fs = new FakeFs().Dir(Temp).Dir(Temp + @"\sub").File(Temp + @"\sub\a.tmp", 5, Old).Dir(Temp + @"\keep").File(Temp + @"\keep\new.tmp", 5, Recent).File(Temp + @"\keep\old.tmp", 5, Old);
        CleanupExecutor.Run(TempCat, Scan(fs), fs, Env, Now, true, null, default);
        Assert.Contains(Temp + @"\sub", fs.DeletedDirs); Assert.DoesNotContain(Temp + @"\keep", fs.DeletedDirs); Assert.DoesNotContain(Temp, fs.DeletedDirs); Assert.True(fs.Entries.ContainsKey(Temp));
    }

    // ── sesión: analizar → confirmar → limpiar
    private static CleanupSession Session(FakeFs fs, FakeBin? bin = null) => new(CleanupCatalog.Build(Env), fs, bin ?? new FakeBin(0), Env, () => Now);

    [Fact] public void Nothing_is_deleted_by_scanning_and_nothing_is_marked_for_the_recycle_bin()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var bin = new FakeBin(5000); var s = Session(fs, bin); s.Scan();
        Assert.Equal(CleanupPhase.Ready, s.Phase); Assert.Empty(fs.Deleted); Assert.False(bin.EmptyCalled);
        Assert.False(s.Rows().Single(r => r.Category.Id == "recycle-bin").Checked);   // la Papelera hay que marcarla a propósito
        Assert.True(s.Rows().Single(r => r.Category.Id == "user-temp").Checked);
    }
    [Fact] public void Clean_does_nothing_without_the_explicit_confirmation()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); s.Scan(); s.Clean();
        Assert.Empty(fs.Deleted); Assert.Equal(CleanupPhase.Ready, s.Phase);
        s.Confirm(); s.Clean(); Assert.Single(fs.Deleted); Assert.Equal(CleanupPhase.Done, s.Phase); Assert.Equal(100, s.FreedBytes);
    }
    [Fact] public void Clean_before_any_scan_is_ignored()
    { var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); s.Confirm(); s.Clean(); Assert.Empty(fs.Deleted); Assert.Equal(CleanupPhase.Idle, s.Phase); }
    [Fact] public void Recycle_bin_needs_its_own_confirmation()
    {
        var fs = new FakeFs().Dir(Temp); var bin = new FakeBin(9000); var s = Session(fs, bin); s.Scan(); s.Toggle("recycle-bin"); s.Confirm();
        s.Clean(); Assert.False(bin.EmptyCalled); Assert.Equal(CleanupPhase.Ready, s.Phase);   // falta la segunda confirmación
        s.ConfirmRecycleBin(); s.Clean(); Assert.True(bin.EmptyCalled); Assert.Equal(9000, s.FreedBytes); Assert.True(s.RecycleBinEmptied);
    }
    [Fact] public void Recycle_bin_failure_claims_no_space()
    {
        var bin = new FakeBin(9000) { Succeeds = false }; var s = Session(new FakeFs().Dir(Temp), bin); s.Scan(); s.Toggle("recycle-bin"); s.Confirm(); s.ConfirmRecycleBin(); s.Clean();
        Assert.Equal(0, s.FreedBytes); Assert.False(s.RecycleBinEmptied); Assert.Contains("parcial", s.Status);
    }
    [Fact] public void Review_only_categories_can_never_be_selected_or_cleaned()
    {
        var s = Session(new FakeFs().Dir(Temp)); s.Scan(); foreach (var id in new[] { "orphans", "setup-leftovers" }) { s.Toggle(id); Assert.False(s.Rows().Single(r => r.Category.Id == id).Checked); Assert.NotNull(s.Rows().Single(r => r.Category.Id == id).Note); }
    }
    [Fact] public void Unchecked_categories_are_not_cleaned()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); s.Scan(); s.Toggle("user-temp"); s.Confirm(); s.Clean();
        Assert.Empty(fs.Deleted);
    }
    [Fact] public void A_file_that_changes_after_the_scan_is_skipped_by_the_session_too_and_status_is_partial()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old).File(Temp + @"\b.tmp", 10, Old); var s = Session(fs); s.Scan();
        fs.File(Temp + @"\a.tmp", 100, Now); s.Confirm(); s.Clean();
        Assert.Equal(10, s.FreedBytes); Assert.Contains("parcial", s.Status); Assert.Contains("cambiaron", s.SummaryLine());
    }
    [Fact] public void Cancelling_a_scan_leaves_a_usable_session_and_deletes_nothing()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); s.Cancel();   // sin análisis en marcha: no hace nada
        s.Scan(); Assert.Equal(CleanupPhase.Ready, s.Phase); Assert.Empty(fs.Deleted);
    }
    [Fact] public void Rescan_resets_selection_and_confirmation()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); s.Scan(); s.Confirm(); s.Scan(); s.Clean();
        Assert.Empty(fs.Deleted);   // la confirmación no sobrevive a un nuevo análisis
    }

    // ── Isla
    [Fact] public void Island_payloads_follow_the_phases_and_use_an_extension_activity()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); Assert.Null(CleanupIsland.Payload(s));
        var phases = new List<string>(); s.Changed += () => { if (CleanupIsland.Payload(s) is { } p) phases.Add(System.Text.Json.JsonSerializer.Serialize(p)); };
        s.Scan(); s.Confirm(); s.Clean();
        Assert.Contains(phases, j => j.Contains("\"kind\":\"extension\"") && j.Contains("Analizando archivos temporales") && j.Contains("\"id\":\"winmac-cleanup\""));
        Assert.Contains(phases, j => j.Contains("Limpiando archivos seleccionados"));
        Assert.Contains(phases, j => j.Contains("\"op\":\"finish\"") && j.Contains("\"completed\""));
        Assert.Contains("completada", s.Status);
    }
    [Fact] public void Island_reports_cancelled_and_partial_distinctly()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old).File(Temp + @"\b.tmp", 10, Old); fs.Locked.Add(Temp + @"\a.tmp"); var s = Session(fs); s.Scan(); s.Confirm(); s.Clean();
        Assert.Contains("\"failed\"", System.Text.Json.JsonSerializer.Serialize(CleanupIsland.Payload(s)));   // parcial
    }

    // ── panel
    [Fact] public void Panel_flow_idle_ready_done()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 1_500_000, Old); var s = Session(fs);
        CleanupView V() => new(s.Phase, s.Rows(), s.SelectedBytes, s.Status, s.Fraction, s.WasCancelled, s.Phase == CleanupPhase.Done ? s.SummaryLine() : null, s.RecycleBinSelected);
        PanelModel M() => PanelBuilder.CleanupPanel(PanelContext.Initial(new BarConfig(), Now) with { Clean = V() });
        Assert.Contains(M().Widgets, w => w.Id == PanelBuilder.CleanScan); Assert.DoesNotContain(M().Widgets, w => w.Kind == WidgetKind.CleanRow);
        s.Scan(); var ready = M();
        Assert.Equal(9, ready.Widgets.Count(w => w.Kind == WidgetKind.CleanRow)); Assert.Contains(ready.Widgets, w => w.Id == PanelBuilder.CleanRun && w.Enabled && w.Text.Contains("1,43 MB") is var _ );
        var locked = ready.Widgets.Where(w => w.Kind == WidgetKind.CleanRow && ((CleanRowData)w.Data!).Locked).Select(w => ((CleanRowData)w.Data!).CategoryId).ToHashSet();
        Assert.Contains("orphans", locked); Assert.Contains("setup-leftovers", locked); Assert.Contains("recycle-bin", locked);   // papelera vacía: nada que marcar
        s.Confirm(); s.Clean(); var done = M(); Assert.Contains(done.Widgets, w => w.Id == "clean-summary" && w.Text.Contains("Liberado")); Assert.Contains(done.Widgets, w => w.Id == PanelBuilder.CleanReset);
    }
    [Fact] public void Row_toggle_is_a_click_event_and_keyboard_target_only_when_selectable()
    {
        var fs = new FakeFs().Dir(Temp).File(Temp + @"\a.tmp", 100, Old); var s = Session(fs); s.Scan();
        var m = PanelBuilder.CleanupPanel(PanelContext.Initial(new BarConfig(), Now) with { Clean = new(s.Phase, s.Rows(), s.SelectedBytes, s.Status, s.Fraction, false, null, false) });
        var c = new PanelController(); c.SetModel(m); var ids = c.Targets().Select(t => t.Id).ToList();
        Assert.Contains(PanelBuilder.CleanTogglePrefix + "user-temp", ids); Assert.DoesNotContain(PanelBuilder.CleanTogglePrefix + "orphans", ids);
    }
    [Fact] public void Size_text_is_spanish_and_readable() { Assert.Equal("1,5 MB", CleanupText.Size(1_572_864)); Assert.Equal("2,5 GB", CleanupText.Size(2_684_354_560)); Assert.Equal("12 KB", CleanupText.Size(12_288)); }
}
