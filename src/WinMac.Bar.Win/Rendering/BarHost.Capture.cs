using System.Runtime.InteropServices;
using WinMac.Bar.Capture;
using WinMac.Bar.Win.Capture;
using WinMac.Bar.Launcher;
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
    private static readonly string[] CaptureModes = ["region", "window", "screen", "all", "toolbar", "record-region", "record-window", "record-screen", "record-stop", "ws-save", "ws-restore", "ws-side", "ws-grid", "diag-gc", "region-delay3", "region-delay5", "region-delay10", "window-delay5", "screen-delay5"];

    private readonly List<GlobalHotkey> _captureKeys = [];
    private readonly Dictionary<string, EventWaitHandle> _captureEvents = [];
    private CaptureOverlay? _overlay;
    /// <summary>Otro módulo (Live Text) usa el mismo capturador: si hay un destino, la región elegida se le entrega en vez de guardarse como captura. Se anula al cerrar o cancelar.</summary>
    private Action<byte[], CaptureRect, CaptureRect>? _captureSink;
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

    public void OpenEditor(string path, string why) => OpenEditor(path, why, null);

    public void OpenEditor(string path, string why, string? tool)
    {
        if (!Editable(path)) { _log("[editor] petición rechazada (no es una imagen local legible)"); return; }
        if (_editor is not null) { _editor.Dispose(); _editor = null; }
        CollapseIsland();
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
                        if (tool is not null) _editor.SelectTool(tool);
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

    private string? _captureModsOn;
    private readonly List<(GlobalHotkey Key, HotkeyInfo Info)> _captureKeyInfo = [];

    private void ApplyCaptureConfig()
    {
        if (_cfg.CaptureHotkeys == _captureKeysOn && (!_cfg.CaptureHotkeys || _cfg.CaptureModifiers == _captureModsOn)) return;
        foreach (var k in _captureKeys) k.Dispose(); _captureKeys.Clear(); _captureKeyInfo.Clear(); _captureKeysOn = _cfg.CaptureHotkeys; _captureModsOn = _cfg.CaptureModifiers;
        if (!_cfg.CaptureHotkeys) { _log("[captura] atajos desactivados"); ReportHotkeys(); return; }
        uint mods = _cfg.CaptureModifiers switch { "CtrlAlt" => GlobalHotkey.ModCtrl | GlobalHotkey.ModAlt, "AltShift" => GlobalHotkey.ModAlt | GlobalHotkey.ModShift, _ => GlobalHotkey.ModCtrl | GlobalHotkey.ModShift };
        string m = HotkeyAudit.CaptureModifierName(_cfg.CaptureModifiers);
        void Add(uint vk, string digit, string purpose, Action run)
        {
            var k = new GlobalHotkey(mods, vk, () => Post(run), _log, $"{m}+{digit} ({purpose})");
            _captureKeys.Add(k); _captureKeyInfo.Add((k, new HotkeyInfo($"{m}+{digit}", purpose, "Barra · Capturas", k.Registered)));
        }
        Add(0x33, "3", "Capturar pantalla", () => BeginCapture(CaptureMode.Monitor, "atajo"));
        Add(0x34, "4", "Capturar región", () => BeginCapture(CaptureMode.Region, "atajo"));
        Add(0x35, "5", "Barra de captura", () => BeginCapture(CaptureMode.Window, "atajo"));
        Add(0x36, "6", "Grabar / detener", () => ToggleRecording(CaptureMode.Region, "atajo"));
        ReportHotkeys();
    }

    /// <summary>The real state of every WinMac shortcut, sent to the host so Settings can show it (docs/27). Also after Spotlight's shortcut changes.</summary>
    internal void ReportHotkeys()
    {
        var list = _captureKeyInfo.Select(k => k.Info).ToList();
        if (SpotlightHotkeyInfo() is { } s) list.Add(s);
        if (_cfg.LaunchpadOnWinKey) list.Add(new HotkeyInfo("Win (sola)", "Launchpad", "Barra · Launchpad", true));
        SyncLegacyHotkeys(list);
        if (_binder is not null) list.AddRange(_binder.Infos());
        var checkedList = HotkeyAudit.Check(list);
        foreach (var k in checkedList.Where(k => k.Problem is not null)) _log($"[atajos] {k.Keys} ({k.Purpose}): {k.Problem}");
        ModuleEvent?.Invoke("bar.hotkeys", new { items = checkedList.Select(k => new { keys = k.Keys, purpose = k.Purpose, module = k.Module, registered = k.Registered, problem = k.Problem }).ToArray() });
    }

    private DateTime _hkReportAgain = DateTime.UtcNow.AddSeconds(4);

    private void CaptureTick()
    {
        if (_hkReportAgain != default && DateTime.UtcNow > _hkReportAgain) { _hkReportAgain = default; ReportHotkeys(); }   // once more when the link to the host is surely up
        RecordTick();
        _features?.Pump();
        if (_editRequest is { } req) { _editRequest = null; OpenEditor(req, "Hatsu Notch"); }
        if (_editEvent?.WaitOne(0) == true)
            try { var p = File.ReadAllText(EditRequestFile).Trim(); File.Delete(EditRequestFile); OpenEditor(p, "orden externa"); } catch (Exception e) { _log("[editor] petición ilegible: " + e.Message); }
        foreach (var (m, ev) in _captureEvents)
            if (ev.WaitOne(0))
            {
                _log("[captura] petición: " + m);
                if (m == "record-stop") { StopRecording("orden externa"); continue; }
                if (m == "diag-gc")
                {
                    // SOLO DIAGNÓSTICO (WINMAC_MENU_DIAG): ¿la memoria que crece es basura aún no recogida o algo la retiene? No se usa en el funcionamiento normal.
                    if (!MenuDiagOn) continue;
                    long before = GC.GetTotalMemory(false); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); long after = GC.GetTotalMemory(true);
                    _log($"[diag] GC completo: en uso {before / 1048576.0:0.0} → {after / 1048576.0:0.0} MB, montón {GC.GetGCMemoryInfo().HeapSizeBytes / 1048576.0:0.0} MB");
                    continue;
                }
                if (m.StartsWith("ws-", StringComparison.Ordinal)) { switch (m) { case "ws-save": SaveWorkspace(null); break; case "ws-restore": RestoreWorkspace(null); break; case "ws-side": ArrangeWindows(false); break; default: ArrangeWindows(true); break; } continue; }
                if (m.StartsWith("record-", StringComparison.Ordinal)) { ToggleRecording(m switch { "record-window" => CaptureMode.Window, "record-screen" => CaptureMode.Monitor, _ => CaptureMode.Region }, "orden externa " + m); continue; }
                int dl = m.IndexOf("-delay", StringComparison.Ordinal); int delay = dl > 0 && int.TryParse(m[(dl + 6)..], out var dsec) ? dsec : -1; string mm = dl > 0 ? m[..dl] : m;
                BeginCapture(mm switch { "window" or "toolbar" => CaptureMode.Window, "screen" => CaptureMode.Monitor, "all" => CaptureMode.AllMonitors, _ => CaptureMode.Region }, "orden externa " + m, delay);
            }

        CountdownTick();
        _quickLook?.Tick();
    }

    // ── WM-002: cuenta atrás antes de capturar (para menús abiertos)
    private CaptureCountdown? _countdown; private RecordingIndicator? _countPill; private static readonly System.Diagnostics.Stopwatch _cdClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Starts the countdown: a small pill (excluded from capture, never activated so the menu stays open), then the screen is frozen as usual.</summary>
    private void StartCountdown(CaptureMode mode, string why, int seconds)
    {
        _lp?.Hide(); _spot?.Hide(); _popover?.Hide();
        _countdown = new CaptureCountdown(mode, seconds, _cdClock.Elapsed.TotalSeconds);
        try
        {
            var mons = ScreenGrabber.Monitors(); WinMac.Bar.Win.Interop.BarNative.GetCursorPos(out var cp);
            var mon = CaptureGeometry.MonitorAt(mons, cp.X, cp.Y) ?? mons.FirstOrDefault(m => m.Primary) ?? mons[0];
            _countPill = new RecordingIndicator(() => Post(() => CancelCountdown("clic en el indicador")), _log, _countdown.Text(_cdClock.Elapsed.TotalSeconds), "Cancelar", 0x0A9EFF /* ámbar (BGR) */, "WinMacCountdown");
            _countPill.Show(mon);
            if (MenuDiagOn) _log($"[captura] diag cuenta@{_countPill.Bounds.X + _countPill.Bounds.W / 2},{_countPill.Bounds.Y + _countPill.Bounds.H / 2}");
        }
        catch (Exception e) { _log("[captura] sin indicador de cuenta atrás: " + e.Message); }   // the countdown still runs: the capture is what was asked
        _log($"[captura] cuenta atrás de {_countdown.Seconds} s ({why}), modo {mode}");
    }

    private void CancelCountdown(string why)
    {
        if (_countdown is null) return;
        _countdown.Cancel(); _countdown = null; _countPill?.Dispose(); _countPill = null;
        _log("[captura] cuenta atrás cancelada (" + why + ")");
    }

    private void CountdownTick()
    {
        if (_countdown is not { } c) return;
        double now = _cdClock.Elapsed.TotalSeconds;
        if (!c.Due(now)) { _countPill?.SetText(c.Text(now)); return; }
        _countdown = null;
        var pill = _countPill; _countPill = null; pill?.Hide();
        // The pill is excluded from capture anyway; it is also hidden and gone from the desktop composition before the copy. No focus change happens.
        Task.Delay(120).ContinueWith(_ => Post(() => { pill?.Dispose(); BeginCapture(c.Mode, "tras la cuenta atrás", record: false, waited: true, delay: 0); }));
    }

    /// <summary>Freezes the screen and shows the overlay in the given mode (the toolbar lets the user switch). Only one at a time.</summary>
    /// <summary>Sends a command to the Island (topic notch.cmd over WinMac's pipe); null when the bar runs on its own.</summary>
    public Action<object>? NotchCommand { get; set; }

    /// <summary>The Island is open (its reserved space is taller than the collapsed pill).</summary>
    private bool IslandOpen() => Holes.Any(h => h.H > 80);

    /// <summary>Closes the open Island first: it is «always on top» and would cover the overlay or the editor and take their clicks. True if it had to.</summary>
    private bool CollapseIsland()
    {
        if (!IslandOpen() || NotchCommand is null) return false;
        NotchCommand(new { cmd = "collapse" }); _log("[captura] la Isla estaba abierta: se cierra antes"); return true;
    }

    public void BeginCapture(CaptureMode mode, string why, bool record = false) => BeginCapture(mode, why, record, waited: false, delay: -1);

    /// <param name="delay">Seconds of countdown (WM-002); -1 = the configured one; 0 = none. Recording never waits.</param>
    public void BeginCapture(CaptureMode mode, string why, int delay) => BeginCapture(mode, why, false, waited: false, delay: delay);

    private void BeginCapture(CaptureMode mode, string why, bool record, bool waited, int delay)
    {
        if (_countdown is not null) { CancelCountdown("se pidió otra captura: " + why); return; }   // the same shortcut again cancels the countdown
        if (_overlay is not null) { _log("[captura] ya hay una captura abierta (" + why + ")"); return; }
        _quickLook?.HideForCapture();   // the floating thumbnail is excluded from capture too; it also leaves so it never covers the area
        int secs = record ? 0 : delay < 0 ? _cfg.CaptureDelaySeconds : delay;
        if (secs > 0) { StartCountdown(mode, why, secs); return; }
        if (!waited && CollapseIsland()) { Task.Delay(450).ContinueWith(_ => Post(() => BeginCapture(mode, why, record, waited: true, delay: 0))); return; }   // its closing animation must not be in the frozen frame
        try
        {
            _lp?.Hide(); _spot?.Hide(); _popover?.Hide();   // our own panels are not part of what the user wants to capture
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var (frame, rect) = ScreenGrabber.GrabVirtualScreen(); long tGrab = t0.ElapsedMilliseconds;
            var monitors = ScreenGrabber.Monitors(); var windows = ScreenGrabber.Windows(IntPtr.Zero); long tEnum = t0.ElapsedMilliseconds;
            if (MenuDiagOn) _log($"[captura] etapas: copia {tGrab} ms, ventanas {tEnum - tGrab} ms");
            _overlay = new CaptureOverlay(frame, rect, monitors, windows, mode,
                (t, m, title) => Post(() => { var sink = _captureSink; if (sink is not null) { _captureSink = null; EndCapture(); sink(frame, rect, t); } else if (record) StartRecording(t, monitors); else SaveCapture(frame, rect, t, m, title); }), () => Post(EndCapture), _log, record);
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

    private void EndCapture() { _overlay?.Dispose(); _overlay = null; _captureSink = null; }

    /// <summary>Abre el capturador existente para elegir una región y entregarla a <paramref name="sink"/> (sin guardar nada). Devuelve false si ya hay una captura abierta.</summary>
    internal bool BeginRegionPick(Action<byte[], CaptureRect, CaptureRect> sink, string why)
    {
        if (_overlay is not null || _countdown is not null) return false;
        _captureSink = sink; BeginCapture(CaptureMode.Region, why, delay: 0);
        return true;   // si la Isla se está cerrando, la captura se abre unos instantes después y conserva el destino; si no se puede abrir, EndCapture lo anula
    }

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
                Post(() => ShowQuickLook(path, px, t));
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
        EndCapture(); CancelCountdown("cierre de la barra"); _quickLook?.Dispose();
        _editEvent?.Dispose(); _editEvent = null; _editor?.Dispose(); _editor = null;
        if (_recorder is not null) StopRecording("cierre de la barra");
    }
}
