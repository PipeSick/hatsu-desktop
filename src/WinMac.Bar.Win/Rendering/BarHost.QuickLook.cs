using System.Runtime.InteropServices;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Capture;
using WinMac.Bar.Win.Interop;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// Quick Look (section 11): after a capture is saved, the floating thumbnail; a click opens the preview with real actions that reuse the editor, the clipboard
/// copy, Windows' share sheet and the Recycle Bin. Only after a capture the user made; it never opens the big view by itself. Settings: «Miniatura flotante al guardar».
/// </summary>
public sealed partial class BarHost
{
    private QuickLookWindow? _quickLook;

    private void ShowQuickLook(string path, byte[] px, CaptureRect area)
    {
        if (!_cfg.CaptureQuickLook) return;
        try
        {
            _quickLook?.Dispose();
            var mons = ScreenGrabber.Monitors();
            var mon = CaptureGeometry.MonitorAt(mons, area.X + area.W / 2, area.Y + area.H / 2) ?? mons.First();
            CaptureRect? dock = null;
            var dh = FindWindowW("WinMacDock", null);
            if (dh != IntPtr.Zero && IsWindowVisible(dh) && GetWindowRect(dh, out var r)) dock = new CaptureRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            QuickLookWindow? me = null;
            me = new QuickLookWindow(path, px, area.W, area.H, mon, OnQuickLookAction, () => { if (ReferenceEquals(_quickLook, me)) _quickLook = null; }, _log)
            {
                ShowInCaptures = MenuDiagOn && Environment.GetEnvironmentVariable("WINMAC_QUICKLOOK_IN_CAPTURES") == "1",
            };
            _quickLook = me; me.PreviewOpened = QuickLookDiag;
            me.ShowThumbnail(dock);
            _log($"[quicklook] miniatura {me.ThumbBounds.W}x{me.ThumbBounds.H} en {me.ThumbBounds.X},{me.ThumbBounds.Y}");
            if (MenuDiagOn) _log($"[quicklook] diag miniatura@{me.ThumbBounds.X + me.ThumbBounds.W / 2},{me.ThumbBounds.Y + me.ThumbBounds.H / 2}");
        }
        catch (Exception e) { _log("[quicklook] sin miniatura: " + e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>SOLO CI: posiciones de los botones de la vista previa, para pulsarlos como un usuario.</summary>
    public void QuickLookDiag()
    {
        if (!MenuDiagOn || _quickLook is not { PreviewOpen: true } q) return;
        var o = new BarNative.POINT(); BarNative.ClientToScreen(q.PreviewHandle, ref o);
        _log("[quicklook] diag " + string.Join(" | ", q.Buttons.Select(b => $"{b.Id}@{o.X + (int)((b.Rect.Left + b.Rect.Right) / 2)},{o.Y + (int)((b.Rect.Top + b.Rect.Bottom) / 2)}")));
    }

    private void OnQuickLookAction(string id, QuickLookWindow q)
    {
        var path = q.Path;
        switch (id)
        {
            case "copy": CopyImage(q.Pixels, q.Size.W, q.Size.H); q.SetStatus("Imagen copiada al portapapeles"); break;
            case "edit": q.Dispose(); OpenEditor(path, "Quick Look"); break;
            case "annotate": q.Dispose(); OpenEditor(path, "Quick Look", "pen"); break;
            case "crop": q.Dispose(); OpenEditor(path, "Quick Look", "crop"); break;
            case "open": BarNative.ShellExecuteW(IntPtr.Zero, "open", path, null, null, 1); q.Dispose(); break;
            case "share": if (!WindowsShare.Show(q.PreviewHandle, path, _log)) q.SetStatus("Compartir no está disponible en este Windows"); break;
            case "saveas": SaveCopyAs(q); break;
            case "delete":
                if (RecycleBin.Send(path)) { _log("[quicklook] captura enviada a la papelera"); q.Dispose(); }
                else q.SetStatus("No se pudo mover a la papelera");
                break;
        }
    }

    /// <summary>«Guardar como…»: a copy of the file where the user chooses (the dialog on its own STA thread, so the bar keeps running).</summary>
    private void SaveCopyAs(QuickLookWindow q)
    {
        var owner = q.PreviewHandle; var src = q.Path; var folder = Path.GetDirectoryName(src) ?? "";
        var t = new Thread(() =>
        {
            var dest = SaveDialog.Ask(owner, folder, Path.GetFileName(src), "Guardar una copia de la captura");
            Post(() =>
            {
                if (dest is null) return;
                try { File.Copy(src, dest, overwrite: true); q.SetStatus("Guardada en " + Path.GetFileName(dest)); _log("[quicklook] copia guardada"); }
                catch (Exception e) { q.SetStatus("No se pudo guardar la copia"); _log("[quicklook] guardar como: " + e.GetType().Name + ": " + e.Message); }
            });
        }) { IsBackground = true, Name = "Guardar como (Quick Look)" };
        t.SetApartmentState(ApartmentState.STA); t.Start();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out BarNative.RECT r);
}
