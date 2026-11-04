using WinMac.Bar.Cleanup;
using WinMac.Bar.Win.Shell.Cleanup;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// It8: Cleanup dentro de la barra. Desactivado por defecto; sin servicios ni tareas en segundo plano: cada paso (analizar, limpiar) lo lanza el usuario desde el panel, corre en un hilo propio
/// y es cancelable. Nada se borra sin la confirmación del usuario (y la Papelera con otra aparte). El progreso va a la Isla como actividad de extensión si el ajuste lo permite.
/// </summary>
public sealed partial class BarHost
{
    private CleanupSession? _clean;

    private CleanupSession EnsureClean()
    {
        if (_clean is not null) return _clean;
        var s = new CleanupSession(CleanupCatalog.Build(CleanupCatalog.CurrentEnv()), new RealFileSystem(), new ShellRecycleBin(), CleanupCatalog.CurrentEnv());
        s.Changed += () => Post(OnCleanChanged);
        return _clean = s;
    }

    private void CleanupTick() { if (!_cfg.ShowCleanup && _clean is not null && _clean.Phase is not (CleanupPhase.Scanning or CleanupPhase.Cleaning)) { _clean = null; } }

    private void OnCleanChanged()
    {
        var s = _clean; if (s is null) return;
        if (_cfg.CleanupIsland && NotchActivity is not null)
        {
            try
            {
                var p = s.Phase == CleanupPhase.Ready && !s.WasCancelled ? CleanupIsland.ScanFinished(s) : CleanupIsland.Payload(s);
                if (p is not null) NotchActivity(p);
            }
            catch (Exception e) { _log("[limpieza] Isla: " + e.GetType().Name); }
        }

        if (_pkind == PanelKind.Cleanup && _popover is { IsOpen: true }) RebuildCleanupPanel();
    }

    private CleanupView CleanupViewNow()
    {
        var s = _clean; if (s is null) return new CleanupView(CleanupPhase.Idle, [], 0, "", null, false, null, false);
        return new CleanupView(s.Phase, s.Rows(), s.SelectedBytes, s.Status, s.Fraction, s.WasCancelled, s.Phase == CleanupPhase.Done ? s.SummaryLine() : null, s.RecycleBinSelected);
    }

    private void RebuildCleanupPanel() { _pctx = WithModules(_pctx with { Now = DateTime.Now }); _popover?.SetModel(PanelBuilder.Build(PanelKind.Cleanup, _pctx)); }

    private void CleanupAction(PanelEvent e)
    {
        if (e.Kind != PanelEventKind.Click) return;
        var s = EnsureClean();
        if (e.Id == PanelBuilder.CleanScan) { RunClean(() => s.Scan(p => _log($"[limpieza] {p.Phase}: {p.Done} archivos"))); }
        else if (e.Id == PanelBuilder.CleanCancel) s.Cancel();
        else if (e.Id == PanelBuilder.CleanReset) { s.Reset(); NotchActivity?.Invoke(new { op = "remove", activity = new { id = CleanupIsland.Id } }); }
        else if (e.Id.StartsWith(PanelBuilder.CleanTogglePrefix, StringComparison.Ordinal)) s.Toggle(e.Id[PanelBuilder.CleanTogglePrefix.Length..]);
        else if (e.Id == PanelBuilder.CleanRun)
        {
            if (s.Phase != CleanupPhase.Ready || s.SelectedBytes <= 0) return;
            var picked = s.Rows().Where(r => r.Checked && r.Selectable).ToList();
            string list = string.Join("\n", picked.Select(r => $"• {r.Category.Title}: {CleanupText.Size(r.Bytes)}"));
            if (!Confirm("¿Limpiar lo seleccionado?", $"Se eliminarán estos archivos (revisados otra vez justo antes de borrar):\n{list}\n\nLo que esté en uso o haya cambiado se omitirá.", "Limpiar", danger: true, ConfirmIcon.Trash)) return;
            s.Confirm();
            if (s.RecycleBinSelected)
            {
                if (!Confirm("¿Vaciar la Papelera de reciclaje?", "Los elementos de la Papelera se eliminarán de forma definitiva y no se podrán recuperar.", "Vaciar Papelera", danger: true, ConfirmIcon.Trash)) { s.Toggle("recycle-bin"); return; }
                s.ConfirmRecycleBin();
            }

            RunClean(() => s.Clean(p => _log($"[limpieza] {p.Phase}: {p.Done}")));
        }
    }

    private void RunClean(Action work)
    {
        var t = new Thread(() => { try { work(); } catch (Exception e) { _log("[limpieza] " + e.GetType().Name + ": " + e.Message); } }) { IsBackground = true, Name = "WinMac Cleanup" };
        t.Start();
    }
}
