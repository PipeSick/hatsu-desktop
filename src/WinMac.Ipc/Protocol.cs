using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Ipc;

/// <summary>Tipos de mensaje del protocolo v1. Los tipos desconocidos se ignoran (compatibilidad hacia delante).</summary>
public static class MsgType
{
    public const string Hello = "hello", HelloAck = "hello_ack", Err = "err", Sub = "sub", Unsub = "unsub", Pub = "pub", Evt = "evt", Ping = "ping", Pong = "pong", Shutdown = "shutdown", Bye = "bye";
}

public static class Topics
{
    public const string ModuleReady = "module.ready";
}

public sealed class ProtocolException(string message) : Exception(message);

/// <summary>Sobre de todos los mensajes. Los campos desconocidos al deserializar se ignoran.</summary>
public sealed class Envelope
{
    [JsonPropertyName("v")] public int V { get; set; } = Protocol.Version;
    [JsonPropertyName("t")] public string T { get; set; } = "";
    [JsonPropertyName("topic")] public string? Topic { get; set; }
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }

    public static Envelope Of(string type, string? topic = null, object? data = null) => new()
    {
        T = type, Topic = topic, Data = data is null ? null : JsonSerializer.SerializeToElement(data, Protocol.Json),
    };
}

public sealed record HelloData(string Module, string ModuleVersion, int Pid, int ProtoMin, int ProtoMax, string Token);
public sealed record HelloAckData(int Proto, string Instance);

public static class Protocol
{
    public const int Version = 1, ProtoMin = 1, ProtoMax = 1;
    public const int MaxFrameBytes = 1 << 20;   // 1 MiB: nada de lo que se comparte se acerca; protege de tramas corruptas u hostiles
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Tramas: <c>uint32 little-endian con la longitud</c> + JSON UTF-8.</summary>
    public static async Task WriteAsync(Stream s, Envelope e, CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(e, Json);
        if (body.Length > MaxFrameBytes) throw new ProtocolException($"trama demasiado grande ({body.Length} bytes)");
        var buf = new byte[4 + body.Length]; BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)body.Length); body.CopyTo(buf, 4);
        await s.WriteAsync(buf, ct).ConfigureAwait(false); await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Lee una trama. Devuelve null si el otro extremo cerró limpiamente entre tramas. Lanza <see cref="ProtocolException"/> ante longitud inválida, trama truncada o JSON inválido.</summary>
    public static async Task<Envelope?> ReadAsync(Stream s, CancellationToken ct = default)
    {
        var head = new byte[4];
        int got = await ReadFullAsync(s, head, ct).ConfigureAwait(false);
        if (got == 0) return null;
        if (got < 4) throw new ProtocolException("cabecera truncada");
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (len == 0 || len > MaxFrameBytes) throw new ProtocolException($"longitud de trama inválida ({len})");
        var body = new byte[len];
        if (await ReadFullAsync(s, body, ct).ConfigureAwait(false) < len) throw new ProtocolException("trama truncada");
        try { return JsonSerializer.Deserialize<Envelope>(body, Json) ?? throw new ProtocolException("trama vacía"); }
        catch (JsonException ex) { throw new ProtocolException("JSON inválido: " + ex.Message); }
    }

    private static async Task<int> ReadFullAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        int total = 0;
        while (total < buf.Length) { int n = await s.ReadAsync(buf.AsMemory(total), ct).ConfigureAwait(false); if (n == 0) break; total += n; }
        return total;
    }

    /// <summary>Versión acordada: la mayor común a ambos rangos, o null si no se solapan.</summary>
    public static int? Negotiate(int clientMin, int clientMax) { int lo = Math.Max(clientMin, ProtoMin), hi = Math.Min(clientMax, ProtoMax); return lo <= hi ? hi : null; }
}
