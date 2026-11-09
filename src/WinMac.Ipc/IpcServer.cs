using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinMac.Ipc;

public sealed record ServerOptions(string PipeName, string Token)
{
    public string Instance { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>Solo Windows: valida el PID del cliente (p. ej. «es un proceso que lancé yo»). Si devuelve false se rechaza la conexión sin responder.</summary>
    public Func<uint, bool>? PeerValidator { get; init; }
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public int DataQueueCapacity { get; init; } = 64;
    /// <summary>
    /// Opcional: valida cada <c>hello</c> por (módulo, token, PID del cliente) en lugar del token único. Lo usa el canal de EXTENSIONES: un token por extensión,
    /// ligado al proceso que lanzó el host, para que ninguna pueda hacerse pasar por otra.
    /// </summary>
    public Func<string, string, uint?, bool>? HelloValidator { get; init; }
    /// <summary>false: los mensajes de un cliente NO se reenvían a los demás (solo llegan al host). Canal de extensiones: no se hablan entre ellas.</summary>
    public bool Relay { get; init; } = true;
}

/// <summary>
/// Servidor del canal (lo aloja el host). Un canal por módulo con su Id; último valor retenido por tópico; sin puertos de red (Named Pipe, solo el usuario actual).
/// Seguridad: el pipe se crea con <see cref="PipeOptions.CurrentUserOnly"/>, el primer mensaje debe ser un <c>hello</c> con el token correcto en ≤ HandshakeTimeout
/// (si no, se cierra SIN responder) y, en Windows, el PID del cliente puede validarse.
/// </summary>
public sealed class IpcServer : IAsyncDisposable
{
    private sealed class Session(string module, int pid, NamedPipeServerStream pipe, int cap)
    {
        public readonly string Module = module; public readonly int Pid = pid; public readonly NamedPipeServerStream Pipe = pipe;
        public readonly OutQueue Out = new(cap); public readonly HashSet<string> Subs = []; public readonly CancellationTokenSource Cts = new();
    }

    private readonly ServerOptions _opt; private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new(); private readonly ConcurrentDictionary<string, Envelope> _retained = new();
    private Task? _accept; private long _seq;

    public IpcServer(ServerOptions options) { _opt = options; }

    /// <summary>Un módulo completó el <i>hello</i> (id, pid).</summary>
    public event Action<string, int>? ModuleConnected;
    public event Action<string>? ModuleDisconnected;
    /// <summary>Mensaje recibido de un módulo ya autenticado (para el supervisor: <c>pong</c>, <c>bye</c>, <c>evt module.ready</c>…).</summary>
    public event Action<string, Envelope>? MessageReceived;

    public long DroppedMessages => _sessions.Values.Sum(s => s.Out.Dropped) + _droppedClosed;
    private long _droppedClosed;

    public void Start() => _accept ??= Task.Run(() => AcceptLoopAsync(_cts.Token));
    public bool IsConnected(string module) => _sessions.ContainsKey(module);
    public IReadOnlyCollection<string> ConnectedModules => _sessions.Keys.ToArray();

    /// <summary>Publica un valor de ESTADO: se retiene y se envía a los suscriptores (y a los que se suscriban después).</summary>
    public void Publish(string topic, object data) => Distribute(Envelope.Of(MsgType.Pub, topic, data), exceptModule: null, retain: true);

    /// <summary>Evento puntual (NO se retiene) para los módulos suscritos al tópico: órdenes del host como «abrir la ventana de ajustes». Un módulo que se conecte después no lo recibe.</summary>
    public void Emit(string topic, object? data = null) => Distribute(Envelope.Of(MsgType.Evt, topic, data), exceptModule: null, retain: false);

    /// <summary>Mensaje de control a un módulo (ping, shutdown). Devuelve false si no está conectado.</summary>
    public bool Send(string module, string type)
    {
        if (!_sessions.TryGetValue(module, out var s)) return false;
        s.Out.Enqueue(new Envelope { T = type }, control: true); return true;
    }

    public void Broadcast(string type) { foreach (var s in _sessions.Values) s.Out.Enqueue(new Envelope { T = type }, control: true); }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(_opt.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            catch (Exception) { pipe.Dispose(); await Task.Delay(100, CancellationToken.None).ConfigureAwait(false); continue; }
            _ = Task.Run(() => HandleAsync(pipe, ct));
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        Session? session = null;
        try
        {
            using (var hs = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                hs.CancelAfter(_opt.HandshakeTimeout);
                var first = await Protocol.ReadAsync(pipe, hs.Token).ConfigureAwait(false);
                if (first is not { T: MsgType.Hello, Data: not null }) return;                         // cierre silencioso
                var hello = first.Data.Value.Deserialize<HelloData>(Protocol.Json);
                if (hello is null) return;
                if (_opt.HelloValidator is { } hv)
                {
                    uint? cpid = OperatingSystem.IsWindows() ? PeerInfo.ClientPid(pipe) : null;
                    if (!hv(hello.Module, hello.Token, cpid)) { await Task.Delay(300, hs.Token).ConfigureAwait(false); if (!hv(hello.Module, hello.Token, cpid)) return; }
                }
                else if (!TokenOk(hello.Token)) return;                                                  // token incorrecto: sin respuesta
                if (_opt.PeerValidator is { } validate && OperatingSystem.IsWindows())
                {
                    uint? pid = PeerInfo.ClientPid(pipe);
                    if (pid is null) return;
                    if (!validate(pid.Value)) { await Task.Delay(300, hs.Token).ConfigureAwait(false); if (!validate(pid.Value)) return; }   // pequeña carrera: el host registra el PID justo tras lanzar
                }
                if (Protocol.Negotiate(hello.ProtoMin, hello.ProtoMax) is not int proto)
                { await Protocol.WriteAsync(pipe, Envelope.Of(MsgType.Err, data: new { code = "version", message = $"el servidor admite protocolo {Protocol.ProtoMin}-{Protocol.ProtoMax}" }), hs.Token).ConfigureAwait(false); return; }
                await Protocol.WriteAsync(pipe, Envelope.Of(MsgType.HelloAck, data: new HelloAckData(proto, _opt.Instance)), hs.Token).ConfigureAwait(false);
                session = new Session(hello.Module, hello.Pid, pipe, _opt.DataQueueCapacity);
            }
            if (_sessions.TryRemove(session.Module, out var old)) { old.Cts.Cancel(); old.Pipe.Dispose(); }   // un módulo = una conexión
            _sessions[session.Module] = session;
            ModuleConnected?.Invoke(session.Module, session.Pid);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Cts.Token);
            var writer = WriteLoopAsync(session, linked.Token);
            try { await ReadLoopAsync(session, linked.Token).ConfigureAwait(false); } finally { session.Cts.Cancel(); await writer.ConfigureAwait(false); }
        }
        catch (Exception) { /* conexión rota o protocolo inválido: se cierra */ }
        finally
        {
            if (session is not null)
            {
                _droppedClosed += session.Out.Dropped;
                if (_sessions.TryGetValue(session.Module, out var cur) && ReferenceEquals(cur, session)) { _sessions.TryRemove(session.Module, out _); ModuleDisconnected?.Invoke(session.Module); }
            }
            pipe.Dispose();
        }
    }

    private async Task ReadLoopAsync(Session s, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var e = await Protocol.ReadAsync(s.Pipe, ct).ConfigureAwait(false);
            if (e is null) return;
            switch (e.T)
            {
                case MsgType.Sub when e.Topic is { Length: > 0 } t:
                    lock (s.Subs) s.Subs.Add(t);
                    if (_retained.TryGetValue(t, out var last)) s.Out.Enqueue(last, control: false);
                    break;
                case MsgType.Unsub when e.Topic is { Length: > 0 } t: lock (s.Subs) s.Subs.Remove(t); break;
                case MsgType.Pub when e.Topic is { Length: > 0 } && _opt.Relay: Distribute(e, s.Module, retain: true); break;
                case MsgType.Evt when e.Topic is { Length: > 0 } && _opt.Relay: Distribute(e, s.Module, retain: false); break;
                case MsgType.Ping: s.Out.Enqueue(new Envelope { T = MsgType.Pong }, control: true); break;
                default: break;   // los tipos desconocidos se ignoran (compatibilidad hacia delante)
            }
            try { MessageReceived?.Invoke(s.Module, e); } catch (Exception) { /* un suscriptor defectuoso no tumba la conexión */ }
        }
    }

    private async Task WriteLoopAsync(Session s, CancellationToken ct)
    {
        try { while (!ct.IsCancellationRequested) await Protocol.WriteAsync(s.Pipe, await s.Out.DequeueAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false); }
        catch (Exception) { s.Cts.Cancel(); }
    }

    private void Distribute(Envelope e, string? exceptModule, bool retain)
    {
        e.Seq = Interlocked.Increment(ref _seq);
        if (retain && e.Topic is not null) _retained[e.Topic] = e;
        foreach (var s in _sessions.Values)
        {
            if (s.Module == exceptModule) continue;
            bool sub; lock (s.Subs) sub = s.Subs.Contains(e.Topic ?? "");
            if (sub) s.Out.Enqueue(e, control: false);
        }
    }

    private bool TokenOk(string? t) => t is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(t), Encoding.UTF8.GetBytes(_opt.Token));

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var s in _sessions.Values) { s.Cts.Cancel(); try { s.Pipe.Dispose(); } catch (Exception) { } }
        if (_accept is not null) { try { await _accept.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch (Exception) { } }
        _cts.Dispose();
    }
}
