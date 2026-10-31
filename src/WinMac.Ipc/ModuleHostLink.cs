namespace WinMac.Ipc;

/// <summary>
/// Enlace de un módulo (Dock, Barra) con el host de WinMac. Solo existe si el módulo se lanzó con <c>--hosted --pipe &lt;nombre&gt;</c> y el token en la variable de entorno
/// <c>WINMAC_TOKEN</c>; sin eso <see cref="FromArgs"/> devuelve null y el módulo funciona exactamente como siempre (independiente).
/// Si el host desaparece, el módulo SIGUE funcionando y el cliente reintenta conectar.
/// </summary>
public sealed class ModuleHostLink : IAsyncDisposable
{
    public const string TokenEnvVar = "WINMAC_TOKEN";
    private readonly IpcClient _client; private readonly string _module;

    private ModuleHostLink(IpcClient client, string module) { _client = client; _module = module; _client.Connected += () => _client.Event(Topics.ModuleReady, new { module, pid = Environment.ProcessId }); _client.ShutdownRequested += () => ShutdownRequested?.Invoke(); }

    /// <summary>El host pidió cerrar: el módulo debe terminar por su camino normal (se dispara en un hilo de trabajo).</summary>
    public event Action? ShutdownRequested;
    public bool IsConnected => _client.IsConnected;

    public static ModuleHostLink? FromArgs(string[] args, string module, string version)
    {
        if (!args.Contains("--hosted")) return null;
        int i = Array.IndexOf(args, "--pipe"); string? token = Environment.GetEnvironmentVariable(TokenEnvVar);
        if (i < 0 || i + 1 >= args.Length || string.IsNullOrEmpty(token)) return null;
        return new ModuleHostLink(new IpcClient(new ClientOptions(args[i + 1], token, module) { ModuleVersion = version }), module);
    }

    public void Start() => _client.Start();
    public Task ByeAsync(TimeSpan timeout) => _client.SayByeAsync(timeout);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
