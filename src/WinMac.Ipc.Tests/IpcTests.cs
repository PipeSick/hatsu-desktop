using System.IO.Pipes;
using System.Text.Json;
using WinMac.Ipc;
using Xunit;

internal static class T
{
    public static string Pipe() => "winmac-test-" + Guid.NewGuid().ToString("N");
    public static async Task<bool> Until(Func<bool> cond, int ms = 5000) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { if (cond()) return true; await Task.Delay(15); } return cond(); }
    public static IpcServer Server(string pipe, string token = "tok", Action<ServerOptions>? _ = null) { var s = new IpcServer(new ServerOptions(pipe, token)); s.Start(); return s; }
    public static IpcClient Client(string pipe, string module = "m1", string token = "tok") { var c = new IpcClient(new ClientOptions(pipe, token, module) { MaxBackoff = TimeSpan.FromMilliseconds(300) }); c.Start(); return c; }

    /// <summary>Cliente «crudo» para probar el servidor sin la librería cliente.</summary>
    public static async Task<NamedPipeClientStream> Raw(string pipe) { var p = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await p.ConnectAsync(3000); return p; }
    public static Task SendHello(Stream s, string token = "tok", int min = 1, int max = 1, string module = "raw", string? extra = null)
    {
        string json = $"{{\"v\":1,\"t\":\"hello\",\"future\":123,\"data\":{{\"module\":\"{module}\",\"moduleVersion\":\"1\",\"pid\":1,\"protoMin\":{min},\"protoMax\":{max},\"token\":\"{token}\",\"unknown\":true}}}}";
        return WriteRaw(s, json);
    }
    public static async Task WriteRaw(Stream s, string json) { var b = System.Text.Encoding.UTF8.GetBytes(json); var buf = new byte[4 + b.Length]; BitConverter.GetBytes((uint)b.Length).CopyTo(buf, 0); b.CopyTo(buf, 4); await s.WriteAsync(buf); await s.FlushAsync(); }
}

/// <summary>Stream que entrega como mucho 1 byte por lectura (simula tramas que llegan partidas).</summary>
internal sealed class DripStream(byte[] data) : Stream
{
    private int _pos;
    public override int Read(byte[] buffer, int offset, int count) => count == 0 || _pos >= data.Length ? 0 : (buffer[offset] = data[_pos++]) * 0 + 1;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Yield(); if (buffer.Length == 0 || _pos >= data.Length) return 0; buffer.Span[0] = data[_pos++]; return 1; }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => data.Length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}

public class FrameCodecTests
{
    private static async Task<byte[]> Frame(Envelope e) { var ms = new MemoryStream(); await Protocol.WriteAsync(ms, e); return ms.ToArray(); }

    [Fact]
    public async Task Frames_roundtrip_even_when_the_bytes_arrive_one_at_a_time()
    {
        var sent = Envelope.Of(MsgType.Pub, "audio.volume", new { percent = 42, muted = false });
        var got = await Protocol.ReadAsync(new DripStream(await Frame(sent)));
        Assert.NotNull(got); Assert.Equal("pub", got!.T); Assert.Equal("audio.volume", got.Topic); Assert.Equal(42, got.Data!.Value.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task Clean_end_of_stream_between_frames_returns_null_and_two_frames_read_in_order()
    {
        var a = await Frame(Envelope.Of(MsgType.Evt, "a")); var b = await Frame(Envelope.Of(MsgType.Evt, "b"));
        var ms = new MemoryStream([.. a, .. b]);
        Assert.Equal("a", (await Protocol.ReadAsync(ms))!.Topic); Assert.Equal("b", (await Protocol.ReadAsync(ms))!.Topic); Assert.Null(await Protocol.ReadAsync(ms));
    }

    [Fact]
    public async Task Oversized_zero_truncated_and_invalid_frames_are_rejected_without_crashing()
    {
        await Assert.ThrowsAsync<ProtocolException>(() => Protocol.WriteAsync(new MemoryStream(), Envelope.Of(MsgType.Pub, "x", new { blob = new string('x', Protocol.MaxFrameBytes) })));
        var huge = new byte[4]; BitConverter.GetBytes((uint)Protocol.MaxFrameBytes + 1).CopyTo(huge, 0);
        await Assert.ThrowsAsync<ProtocolException>(() => Protocol.ReadAsync(new MemoryStream(huge)));
        await Assert.ThrowsAsync<ProtocolException>(() => Protocol.ReadAsync(new MemoryStream(new byte[4])));                  // longitud 0
        await Assert.ThrowsAsync<ProtocolException>(() => Protocol.ReadAsync(new MemoryStream([1, 0])));                         // cabecera truncada
        var f = await Frame(Envelope.Of(MsgType.Evt, "x"));
        await Assert.ThrowsAsync<ProtocolException>(() => Protocol.ReadAsync(new MemoryStream(f[..^3])));                        // cuerpo truncado
        var bad = System.Text.Encoding.UTF8.GetBytes("{ no es json"); var framed = new byte[4 + bad.Length]; BitConverter.GetBytes((uint)bad.Length).CopyTo(framed, 0); bad.CopyTo(framed, 4);
        await Assert.ThrowsAsync<ProtocolException>(() => Protocol.ReadAsync(new MemoryStream(framed)));
    }

    [Fact]
    public async Task Unknown_fields_are_ignored()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("{\"v\":7,\"t\":\"evt\",\"topic\":\"z\",\"brandNew\":{\"a\":1},\"data\":{\"k\":1}}");
        var framed = new byte[4 + json.Length]; BitConverter.GetBytes((uint)json.Length).CopyTo(framed, 0); json.CopyTo(framed, 4);
        var e = await Protocol.ReadAsync(new MemoryStream(framed)); Assert.Equal("z", e!.Topic); Assert.Equal(7, e.V);
    }

    [Theory]
    [InlineData(1, 1, 1)] [InlineData(1, 5, 1)] [InlineData(0, 1, 1)]
    public void Version_negotiation_picks_the_highest_common(int min, int max, int expected) => Assert.Equal(expected, Protocol.Negotiate(min, max));

    [Theory]
    [InlineData(2, 3)] [InlineData(5, 9)]
    public void Version_negotiation_fails_without_overlap(int min, int max) => Assert.Null(Protocol.Negotiate(min, max));
}

public class HandshakeTests
{
    [Fact]
    public async Task A_wrong_token_is_dropped_without_a_single_byte_back_and_never_registers()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe); bool connected = false; server.ModuleConnected += (_, _) => connected = true;
        using var raw = await T.Raw(pipe); await T.SendHello(raw, token: "WRONG");
        Assert.Null(await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)));   // cierre sin respuesta
        Assert.False(connected); Assert.Empty(server.ConnectedModules);
    }

    [Fact]
    public async Task A_first_message_that_is_not_hello_is_dropped()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe);
        using var raw = await T.Raw(pipe); await Protocol.WriteAsync(raw, Envelope.Of(MsgType.Sub, "x"));
        Assert.Null(await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task A_silent_client_is_dropped_after_the_handshake_timeout()
    {
        var pipe = T.Pipe(); await using var server = new IpcServer(new ServerOptions(pipe, "tok") { HandshakeTimeout = TimeSpan.FromMilliseconds(300) }); server.Start();
        using var raw = await T.Raw(pipe);
        Assert.Null(await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Incompatible_protocol_range_gets_an_error_and_a_close()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe);
        using var raw = await T.Raw(pipe); await T.SendHello(raw, min: 2, max: 3);
        var r = await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(MsgType.Err, r!.T); Assert.Equal("version", r.Data!.Value.GetProperty("code").GetString());
        Assert.Null(await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Unknown_fields_and_unknown_message_types_are_tolerated_and_the_connection_keeps_working()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe);
        using var raw = await T.Raw(pipe); await T.SendHello(raw);                                   // incluye campos desconocidos en el sobre y en los datos
        var ack = await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)); Assert.Equal(MsgType.HelloAck, ack!.T); Assert.Equal(1, ack.Data!.Value.GetProperty("proto").GetInt32());
        await T.WriteRaw(raw, "{\"v\":1,\"t\":\"tipo-del-futuro\",\"data\":{}}");                    // se ignora
        await Protocol.WriteAsync(raw, new Envelope { T = MsgType.Ping });
        Assert.Equal(MsgType.Pong, (await Protocol.ReadAsync(raw).WaitAsync(TimeSpan.FromSeconds(3)))!.T);
    }

    [Fact]
    public async Task A_valid_client_registers_and_ping_pong_reaches_the_server()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe); var seen = new List<string>(); server.MessageReceived += (m, e) => { lock (seen) seen.Add(m + ":" + e.T); };
        await using var client = T.Client(pipe, "bar"); Assert.True(await T.Until(() => server.IsConnected("bar")));
        Assert.True(server.Send("bar", MsgType.Ping)); Assert.False(server.Send("nadie", MsgType.Ping));
        Assert.True(await T.Until(() => { lock (seen) return seen.Contains("bar:pong"); }));
    }
}

public class StateTests
{
    [Fact]
    public async Task A_late_subscriber_receives_the_retained_value_immediately()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe);
        await using var pub = T.Client(pipe, "pub"); Assert.True(await T.Until(() => pub.IsConnected)); pub.Publish("audio.volume", new { percent = 30 });
        Assert.True(await T.Until(() => { server.Publish("probe", new { }); return true; }));
        await Task.Delay(100);
        await using var late = T.Client(pipe, "late"); var got = new TaskCompletionSource<JsonElement?>(); late.Subscribe("audio.volume", d => got.TrySetResult(d));
        var data = await got.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(30, data!.Value.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task Events_are_not_retained_and_reach_only_current_subscribers()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe);
        await using var a = T.Client(pipe, "a"); await using var b = T.Client(pipe, "b"); Assert.True(await T.Until(() => server.IsConnected("a") && server.IsConnected("b")));
        var gotA = new List<string>(); a.Subscribe("lifecycle.x", _ => { lock (gotA) gotA.Add("a"); }); await Task.Delay(150);
        b.Event("lifecycle.x", new { n = 1 }); Assert.True(await T.Until(() => { lock (gotA) return gotA.Count == 1; }));
        await using var c = T.Client(pipe, "c"); var gotC = new List<string>(); c.Subscribe("lifecycle.x", _ => { lock (gotC) gotC.Add("c"); }); await Task.Delay(300);
        lock (gotC) Assert.Empty(gotC);   // no se retuvo
    }

    [Fact]
    public async Task A_subscriber_that_never_reads_does_not_block_the_publisher_and_its_queue_is_bounded()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe);
        using var slow = await T.Raw(pipe); await T.SendHello(slow, module: "slow"); await Protocol.ReadAsync(slow);          // handshake OK…
        await Protocol.WriteAsync(slow, new Envelope { T = MsgType.Sub, Topic = "firehose" });                              // …se suscribe… y no vuelve a leer
        Assert.True(await T.Until(() => server.IsConnected("slow"))); await Task.Delay(100);
        var sw = System.Diagnostics.Stopwatch.StartNew(); var payload = new string('x', 2000);
        for (int i = 0; i < 20000; i++) server.Publish("firehose", new { i, payload });
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"el publicador esperó al suscriptor lento ({sw.Elapsed})");
        Assert.True(server.DroppedMessages > 0, "la cola del suscriptor lento debía descartar lo antiguo");
    }

    [Fact]
    public async Task An_unhealthy_module_does_not_answer_heartbeats_and_a_healthy_one_does()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe); int pongs = 0; server.MessageReceived += (_, e) => { if (e.T == MsgType.Pong) Interlocked.Increment(ref pongs); };
        await using var client = T.Client(pipe, "bar"); bool healthy = false; client.HealthCheck = () => healthy; Assert.True(await T.Until(() => server.IsConnected("bar")));
        for (int i = 0; i < 5; i++) { server.Send("bar", MsgType.Ping); await Task.Delay(40); }
        await Task.Delay(300); Assert.Equal(0, pongs);                                                    // interfaz bloqueada: ni un pong
        healthy = true; server.Send("bar", MsgType.Ping); Assert.True(await T.Until(() => pongs == 1));
        client.HealthCheck = () => throw new InvalidOperationException("falla la comprobación"); server.Send("bar", MsgType.Ping); await Task.Delay(300); Assert.Equal(1, pongs);   // una comprobación que falla cuenta como «no sano»
    }

    [Fact]
    public async Task Shutdown_and_bye_flow_between_host_and_module()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe); var seen = new List<string>(); server.MessageReceived += (m, e) => { lock (seen) seen.Add(e.T); };
        await using var client = T.Client(pipe, "dock"); bool shutdown = false; client.ShutdownRequested += () => shutdown = true;
        Assert.True(await T.Until(() => server.IsConnected("dock"))); server.Broadcast(MsgType.Shutdown); Assert.True(await T.Until(() => shutdown));
        await client.SayByeAsync(TimeSpan.FromSeconds(2)); Assert.True(await T.Until(() => { lock (seen) return seen.Contains("bye"); }));
    }
}

public class ConnectionLifecycleTests
{
    [Fact]
    public async Task The_client_notices_a_dead_server_within_3_seconds_and_reconnects_when_it_returns()
    {
        var pipe = T.Pipe(); var server = T.Server(pipe); await using var client = T.Client(pipe, "bar");
        int connects = 0, disconnects = 0; client.Connected += () => Interlocked.Increment(ref connects); client.Disconnected += () => Interlocked.Increment(ref disconnects);
        Assert.True(await T.Until(() => client.IsConnected));
        await server.DisposeAsync();
        Assert.True(await T.Until(() => disconnects >= 1, 3000), "no detectó la caída del servidor en 3 s");
        await using var again = T.Server(pipe);
        Assert.True(await T.Until(() => again.IsConnected("bar"), 8000), "no se reconectó al volver el servidor");
        Assert.True(connects >= 1 && client.IsConnected);
    }

    [Fact]
    public async Task The_client_waits_patiently_when_the_host_does_not_exist_yet_and_connects_when_it_appears()
    {
        var pipe = T.Pipe(); await using var client = T.Client(pipe, "dock"); await Task.Delay(500); Assert.False(client.IsConnected);
        await using var server = T.Server(pipe); Assert.True(await T.Until(() => server.IsConnected("dock"), 8000));
    }

    [Fact]
    public async Task The_server_frees_a_client_that_died_and_publishes_state_again_after_a_reconnect()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe); var gone = new List<string>(); server.ModuleDisconnected += m => { lock (gone) gone.Add(m); };
        var client = T.Client(pipe, "bar"); Assert.True(await T.Until(() => server.IsConnected("bar")));
        await client.DisposeAsync();
        Assert.True(await T.Until(() => { lock (gone) return gone.Contains("bar"); }) && !server.IsConnected("bar"));
    }

    [Fact]
    public async Task A_second_connection_with_the_same_module_id_replaces_the_first()
    {
        var pipe = T.Pipe(); await using var server = T.Server(pipe); int connects = 0; server.ModuleConnected += (_, _) => Interlocked.Increment(ref connects);
        await using var one = T.Client(pipe, "bar"); Assert.True(await T.Until(() => connects == 1));
        await using var two = T.Client(pipe, "bar"); Assert.True(await T.Until(() => connects == 2)); Assert.Single(server.ConnectedModules);
    }
}

public class ModuleLinkTests
{
    [Fact]
    public void Without_hosted_or_without_token_there_is_no_link_so_the_module_runs_standalone()
    {
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, null);
        Assert.Null(ModuleHostLink.FromArgs([], "dock", "1")); Assert.Null(ModuleHostLink.FromArgs(["--hosted", "--pipe", "x"], "dock", "1"));
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, "t"); Assert.Null(ModuleHostLink.FromArgs(["--hosted"], "dock", "1")); Assert.Null(ModuleHostLink.FromArgs(["--pipe", "x"], "dock", "1"));
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, null);
    }

    [Fact]
    public void The_token_is_removed_from_the_environment_so_child_processes_never_inherit_it()
    {
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, "secreto");
        try
        {
            var link = ModuleHostLink.FromArgs(["--hosted", "--pipe", T.Pipe()], "dock", "1"); Assert.NotNull(link);
            Assert.Null(Environment.GetEnvironmentVariable(ModuleHostLink.TokenEnvVar));
        }
        finally { Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, null); }
    }

    [Fact]
    public async Task A_hosted_module_announces_ready_and_exits_on_shutdown()
    {
        var pipe = T.Pipe(); await using var server = new IpcServer(new ServerOptions(pipe, "secreto")); server.Start(); var ready = new TaskCompletionSource<string>();
        server.MessageReceived += (m, e) => { if (e.T == MsgType.Evt && e.Topic == Topics.ModuleReady) ready.TrySetResult(m); };
        Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, "secreto");
        try
        {
            await using var link = ModuleHostLink.FromArgs(["--hosted", "--pipe", pipe], "bar", "9")!; var shutdown = new TaskCompletionSource(); link.ShutdownRequested += () => shutdown.TrySetResult(); link.Start();
            Assert.Equal("bar", await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            server.Send("bar", MsgType.Shutdown); await shutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { Environment.SetEnvironmentVariable(ModuleHostLink.TokenEnvVar, null); }
    }
}
