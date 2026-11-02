using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using WinMac.Bar.Capture;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// WinMac screen recording (docs/26): records a region, a window's area or a monitor to MP4 (H.264) with Windows' own encoder (Media Foundation through
/// <c>MediaStreamSource</c> + <c>MediaTranscoder</c>; no extra library). Frames come from the same documented GDI copy as screenshots.
/// VIDEO ONLY: no audio and never the microphone. Only started by an explicit user action (shortcut, toolbar, Spotlight), always with a visible indicator,
/// and stopped by the user. Content an app protects comes out black, as Windows intends.
/// </summary>
internal sealed class ScreenRecorder : IDisposable
{
    public const int Fps = 15;
    private readonly CaptureRect _rect; private readonly Action<string> _log;
    private readonly Stopwatch _clock = new();
    private MediaStreamSource? _mss; private Windows.Storage.Streams.IRandomAccessStream? _stream;
    private Task? _transcode; private volatile bool _stop; private long _frames;

    private ScreenRecorder(CaptureRect rect, string path, Action<string> log) { _rect = rect; Path = path; _log = log; }

    public string Path { get; private set; }
    public CaptureRect Rect => _rect;
    public TimeSpan Elapsed => _clock.Elapsed;
    public long Frames => Interlocked.Read(ref _frames);

    /// <summary>H.264 needs even dimensions: the area is trimmed by at most one pixel on each axis.</summary>
    public static CaptureRect EvenRect(CaptureRect r) => CaptureGeometry.EvenRect(r);

    /// <summary>«Grabación 2026-11-02 a las 17.20.05.mp4».</summary>
    public static string FileName(DateTime local) => CaptureGeometry.RecordingName(local);

    public static async Task<ScreenRecorder> StartAsync(CaptureRect area, string folder, Action<string> log)
    {
        var rect = EvenRect(area);
        Directory.CreateDirectory(folder);
        var rec = new ScreenRecorder(rect, System.IO.Path.Combine(folder, FileName(DateTime.Now)), log);
        await rec.BeginAsync().ConfigureAwait(false);
        return rec;
    }

    private async Task BeginAsync()
    {
        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_rect.W, (uint)_rect.H);
        input.FrameRate.Numerator = Fps; input.FrameRate.Denominator = 1;
        _mss = new MediaStreamSource(new VideoStreamDescriptor(input)) { BufferTime = TimeSpan.Zero, IsLive = true };
        _mss.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        _mss.SampleRequested += OnSample;

        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(System.IO.Path.GetDirectoryName(Path)!);
        var file = await folder.CreateFileAsync(System.IO.Path.GetFileName(Path), Windows.Storage.CreationCollisionOption.GenerateUniqueName);
        Path = file.Path;
        _stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Audio = null;   // never audio: no microphone, no system sound
        profile.Video.Width = (uint)_rect.W; profile.Video.Height = (uint)_rect.H;
        profile.Video.FrameRate.Numerator = Fps; profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp((long)_rect.W * _rect.H * Fps / 8, 1_000_000, 16_000_000);

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prep = await transcoder.PrepareMediaStreamSourceTranscodeAsync(_mss, _stream, profile);
        if (!prep.CanTranscode)
        {
            _stream.Dispose(); try { await file.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete); } catch (Exception) { }   // our own empty file
            throw new InvalidOperationException("Windows no puede codificar vídeo en este equipo (" + prep.FailureReason + ")");
        }

        _clock.Start();
        _transcode = prep.TranscodeAsync().AsTask();
    }

    private FramePool? _pool; private ScreenGrabber.AreaGrabber? _grabber;
    /// <summary>Frame buffers created during the recording (bounded by the pool) and frames skipped because all were still with the encoder.</summary>
    public (int Buffers, long Skipped) PoolStats => _pool is { } p ? (p.Allocated, p.Starved) : (0, 0);

    /// <summary>
    /// Media Foundation asks for the next frame (its own thread): wait for its time, copy the area into a pooled buffer and hand it over; the buffer comes back
    /// to the pool when the encoder has processed the sample. No allocation per frame (measured in CI: two 3 MB arrays per frame before). A null sample ends the file.
    /// </summary>
    private void OnSample(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e)
    {
        if (_stop) { e.Request.Sample = null; return; }
        try
        {
            long n = Interlocked.Read(ref _frames);
            var due = TimeSpan.FromSeconds(n / (double)Fps) - _clock.Elapsed;
            if (due > TimeSpan.Zero) Thread.Sleep(due);
            if (_stop) { e.Request.Sample = null; return; }
            _pool ??= new FramePool(_rect.W * _rect.H * 4, 4);
            _grabber ??= new ScreenGrabber.AreaGrabber(_rect);
            var buf = _pool.Rent(TimeSpan.FromSeconds(1));
            if (buf is null) { _log("[grabación] el codificador va retrasado: no se pudo reservar fotograma"); _stop = true; e.Request.Sample = null; return; }
            _grabber.CopyInto(buf);
            var sample = MediaStreamSample.CreateFromBuffer(buf.AsBuffer(), TimeSpan.FromSeconds(n / (double)Fps));
            sample.Duration = TimeSpan.FromSeconds(1.0 / Fps);
            var pool = _pool;
            sample.Processed += (_, _) => pool.Return(buf);
            e.Request.Sample = sample;
            Interlocked.Increment(ref _frames);
        }
        catch (Exception ex) { _log("[grabación] fotograma: " + ex.GetType().Name + ": " + ex.Message); e.Request.Sample = null; _stop = true; }
    }

    /// <summary>Ends the file (the encoder finishes the MP4) and returns its path and length.</summary>
    public async Task<(string Path, TimeSpan Length, long Frames)> StopAsync()
    {
        _stop = true; _clock.Stop();
        if (_transcode is { } t)
        {
            var done = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
            if (done != t) _log("[grabación] el codificador tardó en cerrar el archivo");
            else await t.ConfigureAwait(false);
        }
        _stream?.Dispose(); _stream = null;
        Release();
        return (Path, TimeSpan.FromSeconds(Frames / (double)Fps), Frames);
    }

    /// <summary>Everything native and every reference to the pooled frames goes now, not when a finalizer runs.</summary>
    private void Release()
    {
        if (_mss is not null) { _mss.SampleRequested -= OnSample; _mss = null; }
        _transcode = null;
        try { _grabber?.Dispose(); } catch (Exception) { }
        _grabber = null; _pool = null;
    }

    public void Dispose() { _stop = true; _stream?.Dispose(); _stream = null; Release(); }
}
