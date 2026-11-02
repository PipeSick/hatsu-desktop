using System.Runtime.InteropServices;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Capture;
using WinMac.Bar.Win.Launcher;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// WinMac Screenshot (docs/26), part of the bar process (no separate app): frozen-frame overlay, region / window / monitor / all monitors, saved in the
/// gallery folder (the Island's watcher then shows the confirmation with its thumbnail, adds it to the gallery and to the history).
/// Opened by its shortcuts, by Spotlight («Capturar…») or by <c>WinMac.Bar.App --capture region|window|screen|all|toolbar</c>.
/// </summary>
public sealed partial class BarHost
{
    public static string CaptureEventName(string mode) => @"Local\WinMac.Bar.Capture." + mode;
    private static readonly string[] CaptureModes = ["region", "window", "screen", "all", "toolbar", "record-region", "record-window", "record-screen", "record-stop", "ws-save", "ws-restore", "ws-side", "ws-grid"];

    private readonly List<GlobalHotkey> _captureKeys = [];
    private readonly Dictionary<string, EventWaitHandle> _captureEvents = [];
    private CaptureOverlay? _overlay;
    private CaptureEditor? _editor;
    private EventWaitHandle? _editEvent;
    private volatile string? _editRequest;
    public static string EditEventName => @"Local\WinMac.Bar.Capture.edit";
    /// <summary>Where <c>--edit &lt;path&gt;</c> leaves the path for the running bar (per user, in LocalAppData).</summary>
    public static string EditRequestFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "edit-request.txt");
    private bool _captureKeysOn;

    private void StartCapture()
    {
        foreach (var m in CaptureModes)
            try { _captureEvents[m] = new EventWaitHandle(false, EventResetMode.AutoReset, CaptureEventName(m)); } catch (Exception e) { _log("[captura] sin evento " + m + ": " + e.Message); }
        try { _editEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EditEventName); } catch (Exception e) { _log("[editor] sin evento: " + e.Message); }
        ApplyCaptureConfig();
    }

    /// <summary>The Island asked to edit a capture (topic <c>capture.edit</c>, only WinMac's modules reach the pipe). Raised on a worker thread.</summary>
    public void RequestEdit(string? path) { if (!string.IsNullOrWhiteSpace(path)) _editRequest = path; }

    /// <summary>Only real image files the user can read; nothing else is ever opened from a request.</summary>
    private static bool Editable(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp")) return false;
            var fi = new FileInfo(path); return fi.Exists && fi.Length is > 0 and < 256L * 1024 * 1024;
        }
        catch (Exception) { return false; }
    }

    public void OpenEditor(string path, string why)
    {
        if (!Editable(path)) { _log("[editor] petición rechazada (no es una imagen local legible)"); return; }
        if (_editor is not null) { _editor.Dispose(); _editor = null; }
        Task.Run(async () =>
        {
            try
            {
                var (px, w, h) = await CaptureEditor.LoadAsync(path).ConfigureAwait(false);
                Post(() =>
                {
                    try
                    {
                        _editor = CaptureEditor.Open(path, px, w, h, CopyImage, () => { _editor = null; _log("[editor] cerrado"); }, _log);
                        _editor.PostToUi = Post;
                        _log($"[editor] abierto ({why}): {Path.GetFileName(path)} {w}x{h}");
                        if (MenuDiagOn)
                        {
                            var o = new Interop.BarNative.POINT(); Interop.BarNative.ClientToScreen(_editor.Handle, ref o);   // screen px, to click like a user
                            _log("[editor] diag " + string.Join(" | ", _editor.Buttons.Select(b => $"{b.Id}@{o.X + (int)((b.Rect.Left + b.Rect.Right) / 2)},{o.Y + (int)((b.Rect.Top + b.Rect.Bottom) / 2)}")));
                        }
                    }
                    catch (Exception e) { _log("[editor] no se pudo abrir: " + e.GetType().Name + ": " + e.Message); }
                });
            }
            catch (Exception e) { _log("[editor] imagen ilegible: " + e.GetType().Name + ": " + e.Message); }
        });
    }

    private void ApplyCaptureConfig()
    {
        if (_cfg.CaptureHotkeys == _captureKeysOn) return;
        foreach (var k in _captureKeys) k.Dispose(); _captureKeys.Clear(); _captureKeysOn = _cfg.CaptureHotkeys;
        if (!_cfg.CaptureHotkeys) { _log("[captura] atajos desactivados"); return; }
        const uint cs = GlobalHotkey.ModCtrl | GlobalHotkey.ModShift;
        _captureKeys.Add(new GlobalHotkey(cs, 0x33, () => Post(() => BeginCapture(CaptureMode.Monitor, "atajo")), _log, "Ctrl+Mayús+3 (pantalla)"));
        _captureKeys.Add(new GlobalHotkey(cs, 0x34, () => Post(() => BeginCapture(CaptureMode.Region, "atajo")), _log, "Ctrl+Mayús+4 (región)"));
        _captureKeys.Add(new GlobalHotkey(cs, 0x35, () => Post(() => BeginCapture(CaptureMode.Window, "atajo")), _log, "Ctrl+Mayús+5 (barra de captura)"));
        _captureKeys.Add(new GlobalHotkey(cs, 0x36, () => Post(() => ToggleRecording(CaptureMode.Region, "atajo")), _log, "Ctrl+Mayús+6 (grabar / detener)"));
    }

    private void CaptureTick()
    {
        RecordTick();
        if (_editRequest is { } req) { _editRequest = null; OpenEditor(req, "Isla"); }
        if (_editEvent?.WaitOne(0) == true)
            try { var p = File.ReadAllText(EditRequestFile).Trim(); File.Delete(EditRequestFile); OpenEditor(p, "orden externa"); } catch (Exception e) { _log("[editor] petición ilegible: " + e.Message); }
        foreach (var (m, ev) in _captureEvents)
            if (ev.WaitOne(0))
            {
                _log("[captura] petición: " + m);
                if (m == "record-stop") { StopRecording("orden externa"); continue; }
                if (m.StartsWith("ws-", StringComparison.Ordinal)) { switch (m) { case "ws-save": SaveWorkspace(null); break; case "ws-restore": RestoreWorkspace(null); break; case "ws-side": ArrangeWindows(false); break; default: ArrangeWindows(true); break; } continue; }
                if (m.StartsWith("record-", StringComparison.Ordinal)) { ToggleRecording(m switch { "record-window" => CaptureMode.Window, "record-screen" => CaptureMode.Monitor, _ => CaptureMode.Region }, "orden externa " + m); continue; }
                BeginCapture(m switch { "window" or "toolbar" => CaptureMode.Window, "screen" => CaptureMode.Monitor, "all" => CaptureMode.AllMonitors, _ => CaptureMode.Region }, "orden externa " + m);
            }
    }

    /// <summary>Freezes the screen and shows the overlay in the given mode (the toolbar lets the user switch). Only one at a time.</summary>
    public void BeginCapture(CaptureMode mode, string why, bool record = false)
    {
        if (_overlay is not null) { _log("[captura] ya hay una captura abierta (" + why + ")"); return; }
        try
        {
            _lp?.Hide(); _spot?.Hide(); _popover?.Hide();   // our own panels are not part of what the user wants to capture
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var (frame, rect) = ScreenGrabber.GrabVirtualScreen(); long tGrab = t0.ElapsedMilliseconds;
            var monitors = ScreenGrabber.Monitors(); var windows = ScreenGrabber.Windows(IntPtr.Zero); long tEnum = t0.ElapsedMilliseconds;
            if (MenuDiagOn) _log($"[captura] etapas: copia {tGrab} ms, ventanas {tEnum - tGrab} ms");
            _overlay = new CaptureOverlay(frame, rect, monitors, windows, mode,
                (t, m, title) => Post(() => { if (record) StartRecording(t, monitors); else SaveCapture(frame, rect, t, m, title); }), () => Post(EndCapture), _log, record);
            _overlay.Show();
            if (MenuDiagOn) _log($"[captura] etapas: superposición visible a los {t0.ElapsedMilliseconds} ms");
            _log($"[captura] abierta ({why}){(record ? " para grabar" : "")}: modo {mode}, pantalla virtual {rect.X},{rect.Y} {rect.W}x{rect.H}, {monitors.Count} monitor(es), {windows.Count} ventana(s)");
            CaptureDiag();
        }
        catch (Exception e) { _log("[captura] no se pudo abrir: " + e.GetType().Name + ": " + e.Message); EndCapture(); }
    }

    /// <summary>SOLO CI (WINMAC_MENU_DIAG=1): dónde están los botones de la barra de captura, para pulsarlos con el ratón.</summary>
    private void CaptureDiag()
    {
        if (!MenuDiagOn || _overlay is null) return;
        _log("[captura] diag " + string.Join(" | ", _overlay.ToolbarButtons.Select(b => $"{b.Mode}@{b.Rect.X + b.Rect.W / 2},{b.Rect.Y + b.Rect.H / 2}")));
    }

    private void EndCapture() { _overlay?.Dispose(); _overlay = null; }

    private void SaveCapture(byte[] frame, CaptureRect frameRect, CaptureRect t, CaptureMode mode, string? title)
    {
        EndCapture();
        var px = CaptureGeometry.Crop(frame, frameRect, t);
        bool jpeg = string.Equals(_cfg.CaptureFormat, "jpg", StringComparison.OrdinalIgnoreCase);
        string folder = string.IsNullOrWhiteSpace(_cfg.CaptureFolder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots") : _cfg.CaptureFolder!;
        string path = Path.Combine(folder, CaptureGeometry.FileName(DateTime.Now, jpeg ? ".jpg" : ".png", n => File.Exists(Path.Combine(folder, n))));
        bool clip = _cfg.CaptureCopyToClipboard;
        Task.Run(async () =>
        {
            try
            {
                await ScreenGrabber.SaveAsync(px, t.W, t.H, path, jpeg).ConfigureAwait(false);
                _log($"[captura] guardada: {Path.GetFileName(path)} ({t.W}x{t.H}, modo {mode}{(title is null ? "" : ", ventana")})");   // el título de la ventana no va al registro
                if (clip) Post(() => CopyImage(px, t.W, t.H));
            }
            catch (Exception e) { _log("[captura] no se pudo guardar: " + e.GetType().Name + ": " + e.Message); }
        });
    }

    /// <summary>Copies BGRA pixels to the clipboard as CF_DIB (bottom-up), which every app that pastes images understands.</summary>
    private void CopyImage(byte[] px, int w, int h)
    {
        int stride = w * 4, header = 40; var mem = GlobalAlloc(0x0002, (UIntPtr)(header + stride * h)); if (mem == IntPtr.Zero) return;
        var p = GlobalLock(mem);
        try
        {
            var bi = new byte[header]; BitConverter.GetBytes(header).CopyTo(bi, 0); BitConverter.GetBytes(w).CopyTo(bi, 4); BitConverter.GetBytes(h).CopyTo(bi, 8);
            BitConverter.GetBytes((short)1).CopyTo(bi, 12); BitConverter.GetBytes((short)32).CopyTo(bi, 14); Marshal.Copy(bi, 0, p, header);
            for (int row = 0; row < h; row++) Marshal.Copy(px, (h - 1 - row) * stride, p + header + row * stride, stride);
        }
        finally { GlobalUnlock(mem); }
        if (!OpenClipboard(IntPtr.Zero)) { GlobalFree(mem); return; }
        try { EmptyClipboard(); if (SetClipboardData(8 /* CF_DIB */, mem) == IntPtr.Zero) GlobalFree(mem); } finally { CloseClipboard(); }
    }

    private void StopCapture()
    {
        foreach (var k in _captureKeys) k.Dispose(); _captureKeys.Clear();
        foreach (var e in _captureEvents.Values) e.Dispose(); _captureEvents.Clear();
        EndCapture();
        _editEvent?.Dispose(); _editEvent = null; _editor?.Dispose(); _editor = null;
        if (_recorder is not null) StopRecording("cierre de la barra");
    }
}
