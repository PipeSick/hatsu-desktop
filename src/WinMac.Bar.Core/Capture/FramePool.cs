namespace WinMac.Bar.Capture;

/// <summary>
/// A bounded pool of equally sized frame buffers for screen recording (docs/26, docs/34). The encoder holds each frame until it has processed it, so buffers are
/// returned from its thread; at most <see cref="Max"/> exist at any time, whatever the recording length. Thread-safe; pure and tested.
/// </summary>
public sealed class FramePool(int bytes, int max = 4)
{
    private readonly Stack<byte[]> _free = new();
    private readonly SemaphoreSlim _slots = new(max, max);
    private readonly object _lock = new();

    public int Bytes { get; } = bytes;
    public int Max { get; } = max;
    /// <summary>Buffers created so far (never more than <see cref="Max"/>).</summary>
    public int Allocated { get; private set; }
    /// <summary>Buffers not yet given back (still with the encoder).</summary>
    public int Outstanding { get { lock (_lock) return Allocated - _free.Count; } }

    /// <summary>Frames that had to be skipped because every buffer was still with the encoder.</summary>
    public long Starved { get; private set; }

    /// <summary>A buffer, or null if none came back within <paramref name="timeout"/> (the caller skips that frame).</summary>
    public byte[]? Rent(TimeSpan timeout)
    {
        if (!_slots.Wait(timeout)) { lock (_lock) Starved++; return null; }
        lock (_lock)
        {
            if (_free.Count > 0) return _free.Pop();
            Allocated++; return new byte[Bytes];
        }
    }

    public void Return(byte[] b)
    {
        if (b.Length != Bytes) return;
        lock (_lock) _free.Push(b);
        _slots.Release();
    }
}
