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
    private static readonly string[] CaptureModes = ["region", "window", "screen", "all", "toolbar"];

    private readonly List<GlobalHotkey> _captureKeys = [];
    private readonly Dictionary<string, EventWaitHandle> _captureEvents = [];
    private CaptureOverlay? _overlay;
    private bool _captureKeysOn;

    private void StartCapture()
    {
        foreach (var m in CaptureModes)
            try { _captureEvents[m] = new EventWaitHandle(false, EventResetMode.AutoReset, CaptureEventName(m)); } catch (Exception e) { _log("[captura] sin evento " + m + ": " + e.Message); }
        ApplyCaptureConfig();
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
    }

    private void CaptureTick()
    {
        foreach (var (m, ev) in _captureEvents)
            if (ev.WaitOne(0))
                BeginCapture(m switch { "window" or "toolbar" => CaptureMode.Window, "screen" => CaptureMode.Monitor, "all" => CaptureMode.AllMonitors, _ => CaptureMode.Region }, "orden externa " + m);
    }

    /// <summary>Freezes the screen and shows the overlay in the given mode (the toolbar lets the user switch). Only one at a time.</summary>
    public void BeginCapture(CaptureMode mode, string why)
    {
        if (_overlay is not null) return;
        try
        {
            _lp?.Hide(); _spot?.Hide(); _popover?.Hide();   // our own panels are not part of what the user wants to capture
            var (frame, rect) = ScreenGrabber.GrabVirtualScreen();
            var monitors = ScreenGrabber.Monitors(); var windows = ScreenGrabber.Windows(IntPtr.Zero);
            _overlay = new CaptureOverlay(frame, rect, monitors, windows, mode, (t, m, title) => Post(() => SaveCapture(frame, rect, t, m, title)), () => Post(EndCapture), _log);
            _overlay.Show();
            _log($"[captura] abierta ({why}): modo {mode}, pantalla virtual {rect.X},{rect.Y} {rect.W}x{rect.H}, {monitors.Count} monitor(es), {windows.Count} ventana(s)");
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
    }
}
