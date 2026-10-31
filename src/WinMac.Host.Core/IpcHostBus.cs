using WinMac.Ipc;

namespace WinMac.Host;

/// <summary>Adapta el <see cref="IpcServer"/> real a lo que necesita el supervisor.</summary>
public sealed class IpcHostBus : IHostBus
{
    private readonly IpcServer _server;
    public IpcHostBus(IpcServer server)
    {
        _server = server;
        server.MessageReceived += (m, e) =>
        {
            if (e.T == MsgType.Pong) Pong?.Invoke(m);
            else if (e.T == MsgType.Bye) Bye?.Invoke(m);
            else if (e.T == MsgType.Evt && e.Topic == Topics.ModuleReady) Ready?.Invoke(m);
            else if (e.T == MsgType.Evt && e.Topic is { Length: > 0 }) Event?.Invoke(m, e.Topic, e.Data);
        };
        server.ModuleDisconnected += m => Disconnected?.Invoke(m);
    }
    public event Action<string>? Ready; public event Action<string>? Pong; public event Action<string>? Bye; public event Action<string>? Disconnected;
    public event Action<string, string, System.Text.Json.JsonElement?>? Event;
    public void Publish(string topic, object data) => _server.Publish(topic, data);
    public void Emit(string topic, object? data = null) => _server.Emit(topic, data);
    public bool Send(string module, string type) => _server.Send(module, type);
}
