namespace WinMac.Host;

public enum ModuleState { Disabled, Starting, Running, Stopping, Stopped, Crashed, Failed, NotInstalled }

/// <summary>Qué lanzar. <see cref="Args"/> se añade antes de <c>--hosted --pipe &lt;nombre&gt;</c>, que pone el lanzador.</summary>
public sealed record ModuleSpec(string Id, string ExePath, IReadOnlyList<string> Args);

public interface IModuleProcess
{
    int Id { get; }
    /// <summary>Se completa con el código de salida cuando el proceso termina.</summary>
    Task<int> Exited { get; }
    /// <summary>Petición cortés de cierre (p. ej. WM_CLOSE a sus ventanas). No fuerza nada.</summary>
    void RequestClose();
    /// <summary>Último recurso, solo sobre NUESTROS módulos.</summary>
    void Kill();
}

public interface IProcessLauncher
{
    /// <summary>Lanza el módulo. Lanza excepción si no se puede (p. ej. el ejecutable no existe).</summary>
    IModuleProcess Start(ModuleSpec spec, string pipeName, string token);
}

/// <summary>Lo que el supervisor necesita del canal (lo implementa <see cref="IpcHostBus"/> sobre el servidor real; en pruebas, un doble).</summary>
public interface IHostBus
{
    event Action<string>? Ready;         // el módulo anunció module.ready
    event Action<string>? Pong;
    event Action<string>? Bye;           // el módulo avisa de que su cierre es voluntario
    event Action<string>? Disconnected;
    bool Send(string module, string type);   // ping / shutdown
    /// <summary>Estado retenido para los módulos suscritos (p. ej. los ajustes de la Isla).</summary>
    void Publish(string topic, object data);
    /// <summary>Orden puntual (no retenida) a los módulos suscritos.</summary>
    void Emit(string topic, object? data = null);
    /// <summary>Evento de un módulo hacia el host (topic + datos), p. ej. una petición de cambio de ajustes.</summary>
    event Action<string, string, System.Text.Json.JsonElement?>? Event;
}

public interface IClock
{
    DateTime UtcNow { get; }
    Task Delay(TimeSpan delay, CancellationToken ct);
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public Task Delay(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}

/// <summary>Tiempos del supervisor. Los valores por defecto son los aprobados en docs/13–14; las pruebas los reducen.</summary>
public sealed record SupervisorPolicy
{
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int MissedBeats { get; init; } = 3;                                   // 3 latidos sin respuesta = módulo colgado
    public TimeSpan StopGrace { get; init; } = TimeSpan.FromSeconds(3);          // tiempo para cerrar tras «shutdown»
    public TimeSpan CloseGrace { get; init; } = TimeSpan.FromSeconds(1);         // tiempo extra tras la petición cortés de cierre
    public TimeSpan BackoffStart { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan BackoffMax { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxCrashes { get; init; } = 5;                                    // 5 caídas…
    public TimeSpan CrashWindow { get; init; } = TimeSpan.FromMinutes(2);        // …en 2 minutos = cortacircuito
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromSeconds(60);       // si aguantó esto, la espera vuelve a empezar
    public TimeSpan StartStagger { get; init; } = TimeSpan.FromMilliseconds(250);
}
