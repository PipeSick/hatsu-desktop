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
    /// <summary>Ver <see cref="IpcClient.HealthCheck"/>: devolver false mientras la interfaz del módulo esté bloqueada.</summary>
    public Func<bool>? HealthCheck { get => _client.HealthCheck; set => _client.HealthCheck = value; }

    public static ModuleHostLink? FromArgs(string[] args, string module, string version)
    {
        if (!args.Contains("--hosted")) return null;
        int i = Array.IndexOf(args, "--pipe"); string? token = Environment.GetEnvironmentVariable(TokenEnvVar);
        Environment.SetEnvironmentVariable(TokenEnvVar, null);   // el token no debe heredarse: las apps que lance este módulo (p. ej. el Dock) no deben recibirlo
        if (i < 0 || i + 1 >= args.Length || string.IsNullOrEmpty(token)) return null;
        return new ModuleHostLink(new IpcClient(new ClientOptions(args[i + 1], token, module) { ModuleVersion = version }), module);
    }

    /// <summary>Se suscribe a un tópico del host (estado retenido o evento): se recibe el último valor retenido al conectar y se vuelve a pedir tras cada reconexión.</summary>
    public void Subscribe(string topic, Action<System.Text.Json.JsonElement?> handler) => _client.Subscribe(topic, handler);
    /// <summary>Evento puntual hacia el host (p. ej. una petición de cambio de ajustes); no se retiene.</summary>
    public void Event(string topic, object? data = null) => _client.Event(topic, data);

    /// <summary>Publica un valor de ESTADO del módulo (se retiene en el host y llega a los demás módulos suscritos, también tras una reconexión). Ej.: el espacio que ocupa la Isla.</summary>
    public void Publish(string topic, object data) => _client.Publish(topic, data);

    public void Start() => _client.Start();
    public Task ByeAsync(TimeSpan timeout) => _client.SayByeAsync(timeout);
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
