using WinMac.Bar.Cleanup;

namespace WinMac.Bar;

/// <summary>Estado de la sesión de Cleanup que el panel necesita.</summary>
public sealed record CleanupView(CleanupPhase Phase, IReadOnlyList<CleanupRow> Rows, long SelectedBytes, string Status, double? Fraction, bool WasCancelled, string? Summary, bool BinSelected);

public static partial class PanelBuilder
{
    public const string CleanScan = "clean:scan", CleanRun = "clean:run", CleanCancel = "clean:cancel", CleanReset = "clean:reset", CleanTogglePrefix = "clean:toggle:", CleanSettings = "clean:settings";

    public static PanelModel CleanupPanel(PanelContext c)
    {
        var w = new List<Widget>(); var v = c.Clean;
        w.Add(new Widget(WidgetKind.Header, "clean-title", "Limpieza"));
        var phase = v?.Phase ?? CleanupPhase.Idle;
        if (phase == CleanupPhase.Idle)
        {
            w.Add(new Widget(WidgetKind.Note, "clean-intro", "Analiza primero y no borra nada hasta que lo confirmes. Solo trata archivos temporales y cachés que se recrean solos; nunca tus documentos, fotos, descargas ni proyectos."));
            w.Add(new Widget(WidgetKind.Note, "clean-permanent", CleanupText.PermanentDeletion));
            w.Add(new Widget(WidgetKind.Button, CleanScan, "Analizar", Glyph: "sparkles"));
        }
        else if (v is not null)
        {
            bool busy = phase is CleanupPhase.Scanning or CleanupPhase.Cleaning;
            if (phase is CleanupPhase.Ready)
            {   // resumen arriba: cuánto se puede recuperar y cuánto está marcado
                long total = v.Rows.Where(r => r.Selectable && r.Analyzed).Sum(r => r.Bytes);
                w.Add(new Widget(WidgetKind.StatCards, "clean-summary-cards", "", Data: new StatCardsData(new[]
                {
                    new StatCard("Se puede liberar", CleanupText.Size(total), total > 0 ? "good" : null),
                    new StatCard("Seleccionado", CleanupText.Size(v.SelectedBytes), v.SelectedBytes > 0 ? "good" : null),
                })));
            }
            foreach (var r in v.Rows)
            {
                string size = r.Category.Kind == CleanupKind.ReviewOnly ? "Solo revisión" : r.Analyzed ? (r.Bytes > 0 ? CleanupText.Size(r.Bytes) : "Nada que limpiar") : "…";
                string reason = r.Category.Kind == CleanupKind.ReviewOnly ? r.Category.WhyNotCleanable ?? "" : r.Category.Reason;
                w.Add(new Widget(WidgetKind.CleanRow, CleanTogglePrefix + r.Category.Id, r.Category.Title, Enabled: !busy || !r.Selectable,
                    Data: new CleanRowData(r.Category.Id, r.Note is { Length: > 0 } n && r.Category.Kind != CleanupKind.ReviewOnly ? reason + " · " + n : reason, size, r.Checked, !r.Selectable || busy, r.Category.Kind == CleanupKind.RecycleBin ? "Definitivo" : null)));
            }

            if (phase is CleanupPhase.Scanning or CleanupPhase.Cleaning)
            {
                w.Add(new Widget(WidgetKind.MeterRow, "clean-progress", v.Status, v.Fraction is { } f ? $"{(int)Math.Round(f * 100)} %" : "", Value: v.Fraction ?? 0, Data: new MeterData("blue")));
                w.Add(new Widget(WidgetKind.Button, CleanCancel, "Cancelar"));
            }
            else
            {
                w.Add(new Widget(WidgetKind.Note, "clean-status", v.Status));
                if (phase == CleanupPhase.Done && v.Summary is { } sum) w.Add(new Widget(WidgetKind.Note, "clean-summary", sum));
                if (phase == CleanupPhase.Ready)
                    w.Add(new Widget(WidgetKind.Button, CleanRun, v.SelectedBytes > 0 ? $"Limpiar lo seleccionado ({CleanupText.Size(v.SelectedBytes)})…" : "Nada seleccionado", Enabled: v.SelectedBytes > 0, Glyph: "sparkles"));
                w.Add(new Widget(WidgetKind.Button, phase == CleanupPhase.Done ? CleanReset : CleanScan, phase == CleanupPhase.Done ? "Cerrar resultado" : "Volver a analizar"));
            }
        }

        return new PanelModel(PanelKind.Cleanup, "Limpieza", w);
    }
}
