using WinMac.Bar.Capture;
using WinMac.Bar.Win.Capture;

namespace WinMac.Bar.Win.Rendering;

/// <summary>
/// WinMac screen recording (docs/26), part of the bar process. Only on an explicit user action: the shortcut (Ctrl+Mayús+6, which also stops it), Spotlight or
/// <c>WinMac.Bar.App --record region|window|screen|stop</c>; the user picks the area in the same overlay as screenshots. Video only (never the microphone).
/// While it records there is always a visible indicator (click = stop) and an activity in the Island with the elapsed time; nothing records hidden.
/// </summary>
public sealed partial class BarHost
{
    private ScreenRecorder? _recorder; private RecordingIndicator? _recIndicator; private bool _recStarting; private DateTime _recLastActivity;

    public bool IsRecording => _recorder is not null;

    public static string RecordingsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");

    /// <summary>Shortcut / Spotlight / command: stop if recording, otherwise let the user choose what to record.</summary>
    public void ToggleRecording(CaptureMode mode, string why)
    {
        if (_recorder is not null) { StopRecording(why); return; }
        if (_recStarting) return;
        BeginCapture(mode, why, record: true);
    }

    private void StartRecording(CaptureRect area, IReadOnlyList<CaptureMonitor> monitors)
    {
        EndCapture();
        if (_recorder is not null || _recStarting) return;
        _recStarting = true; var t0 = System.Diagnostics.Stopwatch.StartNew();
        _log($"[grabación] preparando {area.W}x{area.H}");
        var mon = CaptureGeometry.MonitorAt(monitors, area.X + area.W / 2, area.Y + area.H / 2) ?? monitors.FirstOrDefault(m => m.Primary) ?? new CaptureMonitor(area, 1, true);
        Task.Run(async () =>
        {
            try
            {
                // SOLO CI (diagnóstico activado + archivo marcador): simula un fallo de inicialización para comprobar que no queda nada retenido
                if (MenuDiagOn && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "ci-record-fail"))) throw new InvalidOperationException("fallo simulado de inicialización (solo pruebas del CI)");
                var rec = await ScreenRecorder.StartAsync(area, RecordingsFolder, _log).ConfigureAwait(false);
                Post(() =>
                {
                    _recStarting = false; _recorder = rec;
                    try { _recIndicator = new RecordingIndicator(() => Post(() => StopRecording("indicador")), _log); _recIndicator.Show(mon); }
                    catch (Exception e) { _log("[grabación] sin indicador visible, se detiene: " + e.Message); StopRecording("sin indicador"); return; }   // never record without a visible indicator
                    _log($"[grabación] iniciada: {Path.GetFileName(rec.Path)} ({rec.Rect.W}x{rec.Rect.H}, {ScreenRecorder.Fps} fps, sin audio) en {t0.ElapsedMilliseconds} ms");
                    if (MenuDiagOn) _log($"[grabación] diag indicador@{_recIndicator.Bounds.X + _recIndicator.Bounds.W / 2},{_recIndicator.Bounds.Y + _recIndicator.Bounds.H / 2}");
                    RecordingActivity();
                });
            }
            catch (Exception e) { Post(() => _recStarting = false); _log("[grabación] no se pudo iniciar: " + e.GetType().Name + ": " + e.Message); }
        });
    }

    private void RecordTick()
    {
        if (_recorder is not { } r) return;
        _recIndicator?.Update(r.Elapsed);
        if (DateTime.UtcNow - _recLastActivity >= TimeSpan.FromSeconds(1)) RecordingActivity();
    }

    private void RecordingActivity()
    {
        if (_recorder is not { } r) return;
        _recLastActivity = DateTime.UtcNow;
        var t = r.Elapsed; string el = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        NotchActivity?.Invoke(new { op = "upsert", activity = new { id = "grabacion", kind = "recording", title = "Grabando la pantalla", subtitle = $"{el} · {r.Rect.W} × {r.Rect.H} · sin audio", source = "WinMac", state = "active" } });
    }

    public void StopRecording(string why)
    {
        if (_recorder is not { } r) return;
        _recorder = null; _recIndicator?.Dispose(); _recIndicator = null;
        _log("[grabación] deteniendo (" + why + ")");
        Task.Run(async () =>
        {
            try
            {
                var pool = r.PoolStats;
                var (path, length, frames) = await r.StopAsync().ConfigureAwait(false);
                _log($"[grabación] búferes de fotograma: {pool.Buffers} (máx. 4), saltados: {pool.Skipped}");
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                string? poster = await PosterAsync(path, r.Thumb).ConfigureAwait(false);
                _log($"[grabación] guardada: {Path.GetFileName(path)} ({length.TotalSeconds:0.0} s, {frames} fotogramas, {bytes} B){(poster is null ? "" : ", fotograma de muestra listo")}");
                if (MenuDiagOn && poster is not null) _log("[grabación] muestra " + poster);
                NotchActivity?.Invoke(new { op = "upsert", activity = new { id = "grabacion", kind = "recording", title = "Grabación guardada", subtitle = Path.GetFileName(path) + $" · {length:mm\\:ss}", source = "WinMac", state = "completed", path, thumbnail = poster } });
                NotchActivity?.Invoke(new { op = "finish", activity = new { id = "grabacion", state = "completed" } });
            }
            catch (Exception e) { _log("[grabación] no se pudo cerrar el archivo: " + e.GetType().Name + ": " + e.Message); NotchActivity?.Invoke(new { op = "finish", activity = new { id = "grabacion", state = "failed" } }); }
            finally
            {
                r.Dispose();
                if (MenuDiagOn)   // SOLO CI: separar memoria gestionada de la nativa (Media Foundation, WinRT) para saber qué crece entre grabaciones
                {
                    var gi = GC.GetGCMemoryInfo();
                    _log($"[grabación] memoria: montón gestionado {gi.HeapSizeBytes / 1048576.0:0.0} MB, comprometido {gi.TotalCommittedBytes / 1048576.0:0.0} MB, en uso {GC.GetTotalMemory(false) / 1048576.0:0.0} MB, GC gen2 {GC.CollectionCount(2)}");
                }
            }   // frames come from a bounded pool (FramePool): nothing per frame is left for the GC to find
        });
    }

    /// <summary>
    /// The recording's still for the Island, from a frame kept while recording (scaled to 320 px) and encoded as JPEG. It used to decode the finished MP4 with
    /// Windows.Media.Editing, which left tens of MB of native memory waiting for finalizers after each recording (measured in CI). Saved in WinMac's own data folder.
    /// </summary>
    private static async Task<string?> PosterAsync(string video, (byte[] Px, int W, int H)? thumb)
    {
        if (thumb is not { } t) return null;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "posters"); Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Path.GetFileNameWithoutExtension(video) + ".jpg");
            if (File.Exists(path)) File.Delete(path);
            await ScreenGrabber.SaveAsync(t.Px.Length == t.W * t.H * 4 ? t.Px : t.Px[..(t.W * t.H * 4)], t.W, t.H, path, jpeg: true).ConfigureAwait(false);
            return path;
        }
        catch (Exception) { return null; }
    }
}
