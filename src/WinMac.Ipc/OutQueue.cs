namespace WinMac.Ipc;

/// <summary>
/// Cola de salida de una conexión: los mensajes de CONTROL (ping, shutdown, bye) tienen prioridad y nunca se descartan; los de DATOS están acotados y,
/// si el receptor es lento, se descarta lo más antiguo. Así quien publica nunca espera a un suscriptor lento.
/// </summary>
internal sealed class OutQueue(int dataCapacity)
{
    private readonly object _lock = new(); private readonly Queue<Envelope> _control = new(), _data = new(); private TaskCompletionSource? _waiter;
    public long Dropped { get; private set; }

    public void Enqueue(Envelope e, bool control)
    {
        TaskCompletionSource? w;
        lock (_lock)
        {
            if (control) _control.Enqueue(e); else { _data.Enqueue(e); while (_data.Count > dataCapacity) { _data.Dequeue(); Dropped++; } }
            w = _waiter; _waiter = null;
        }
        w?.TrySetResult();
    }

    public async Task<Envelope> DequeueAsync(CancellationToken ct)
    {
        while (true)
        {
            Task wait;
            lock (_lock)
            {
                if (_control.TryDequeue(out var c)) return c;
                if (_data.TryDequeue(out var d)) return d;
                _waiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); wait = _waiter.Task;
            }
            await wait.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    public bool IsEmpty { get { lock (_lock) return _control.Count == 0 && _data.Count == 0; } }
}
