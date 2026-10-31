using System.IO.Pipes;
using System.Text.Json;

namespace WinMac.Ipc;

public sealed record ClientOptions(string PipeName, string Token, string Module)
{
    public string ModuleVersion { get; init; } = "0";
    public int ProtoMin { get; init; } = Protocol.ProtoMin;
    public int ProtoMax { get; init; } = Protocol.ProtoMax;
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Cliente del canal (lo usa cada módulo). Se conecta en segundo plano, se reconecta con espera creciente si el host cae o aún no existe, y NUNCA bloquea al módulo:
/// publicar sin conexión solo encola (acotado). Responde solo a los <c>ping</c> del host.
/// </summary>
public sealed class IpcClient : IAsyncDisposable
{
    private readonly ClientOptions _opt; private readonly CancellationTokenSource _cts = new(); private readonly OutQueue _out = new(64);
    private readonly Dictionary<string, Action<JsonElement?>> _subs = []; private readonly Dictionary<string, Envelope> _retainedOut = [];
    private Task? _loop; private volatile bool _stopping; private volatile bool _connected;

    public IpcClient(ClientOptions options) { _opt = options; }

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action? ShutdownRequested;
    public bool IsConnected => _connected;

    /// <summary>Si devuelve false el cliente NO contesta a los latidos (el host lo dará por colgado). El módulo lo usa para decir «mi interfaz ha dejado de avanzar»; el hilo del canal, solo, no demuestra nada.</summary>
    public Func<bool>? HealthCheck { get; set; }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Publica un valor de estado (se retiene en el servidor y se vuelve a enviar tras una reconexión).</summary>
    public void Publish(string topic, object data)
    {
        var e = Envelope.Of(MsgType.Pub, topic, data); lock (_retainedOut) _retainedOut[topic] = e; _out.Enqueue(e, control: false);
    }

    /// <summary>Evento puntual (no se retiene).</summary>
    public void Event(string topic, object? data = null) => _out.Enqueue(Envelope.Of(MsgType.Evt, topic, data), control: false);

    public void Subscribe(string topic, Action<JsonElement?> handler)
    {
        lock (_subs) _subs[topic] = handler;
        _out.Enqueue(new Envelope { T = MsgType.Sub, Topic = topic }, control: false);
    }

    /// <summary>Avisa al host de que el cierre es voluntario (no debe reiniciarnos) y espera a que salga el mensaje.</summary>
    public async Task SayByeAsync(TimeSpan timeout)
    {
        _stopping = true; _out.Enqueue(new Envelope { T = MsgType.Bye }, control: true);
        var end = DateTime.UtcNow + timeout;
        while (_connected && !_out.IsEmpty && DateTime.UtcNow < end) await Task.Delay(10).ConfigureAwait(false);
        await Task.Delay(30).ConfigureAwait(false);   // margen para el vaciado del pipe
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromMilliseconds(200);
        while (!ct.IsCancellationRequested && !_stopping)
        {
            bool wasConnected = false;
            try
            {
                using var pipe = new NamedPipeClientStream(".", _opt.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(500, ct).ConfigureAwait(false);
                await Protocol.WriteAsync(pipe, Envelope.Of(MsgType.Hello, data: new HelloData(_opt.Module, _opt.ModuleVersion, Environment.ProcessId, _opt.ProtoMin, _opt.ProtoMax, _opt.Token)), ct).ConfigureAwait(false);
                using var hs = CancellationTokenSource.CreateLinkedTokenSource(ct); hs.CancelAfter(TimeSpan.FromSeconds(3));
                var ack = await Protocol.ReadAsync(pipe, hs.Token).ConfigureAwait(false);
                if (ack is not { T: MsgType.HelloAck }) throw new ProtocolException(ack?.T == MsgType.Err ? "el servidor rechazó la conexión (versión)" : "sin respuesta del servidor");
                wasConnected = true; _connected = true; backoff = TimeSpan.FromMilliseconds(200);
                // tras (re)conectar: volver a suscribirse y a publicar el último estado
                lock (_subs) foreach (var t in _subs.Keys) _out.Enqueue(new Envelope { T = MsgType.Sub, Topic = t }, control: false);
                lock (_retainedOut) foreach (var e in _retainedOut.Values) _out.Enqueue(e, control: false);
                try { Connected?.Invoke(); } catch (Exception) { }
                using var link = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var writer = Task.Run(async () =>
                {
                    try { while (!link.Token.IsCancellationRequested) await Protocol.WriteAsync(pipe, await _out.DequeueAsync(link.Token).ConfigureAwait(false), link.Token).ConfigureAwait(false); }
                    catch (Exception) { link.Cancel(); }
                });
                try { await ReadLoopAsync(pipe, link.Token).ConfigureAwait(false); } finally { link.Cancel(); await writer.ConfigureAwait(false); }
            }
            catch (Exception) { /* sin host, host caído o protocolo inválido: se reintenta */ }
            finally { if (wasConnected) { _connected = false; try { Disconnected?.Invoke(); } catch (Exception) { } } }
            if (_stopping || ct.IsCancellationRequested) break;
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromMilliseconds(Math.Min(_opt.MaxBackoff.TotalMilliseconds, backoff.TotalMilliseconds * 2));
        }
    }

    private async Task ReadLoopAsync(Stream pipe, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var e = await Protocol.ReadAsync(pipe, ct).ConfigureAwait(false);
            if (e is null) return;
            switch (e.T)
            {
                case MsgType.Ping: { bool ok = true; try { ok = HealthCheck?.Invoke() ?? true; } catch (Exception) { ok = false; } if (ok) _out.Enqueue(new Envelope { T = MsgType.Pong }, control: true); break; }
                case MsgType.Shutdown: _ = Task.Run(() => ShutdownRequested?.Invoke()); break;
                case MsgType.Pub or MsgType.Evt when e.Topic is not null:
                    Action<JsonElement?>? h; lock (_subs) _subs.TryGetValue(e.Topic, out h);
                    if (h is not null) _ = Task.Run(() => { try { h(e.Data); } catch (Exception) { } });
                    break;
                default: break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping = true; _cts.Cancel();
        if (_loop is not null) { try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception) { } }
        _cts.Dispose();
    }
}
