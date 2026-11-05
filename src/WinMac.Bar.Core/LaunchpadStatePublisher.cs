namespace WinMac.Bar;

/// <summary>
/// Cuenta al Dock (por el canal del host) cuándo Launchpad está abierto y en qué monitor. Un mensaje al abrir y al cerrar, y un latido cada
/// <see cref="HeartbeatMs"/> ms mientras siga abierto: así, si la Barra se cierra o falla con Launchpad abierto, el Dock deja de recibir latidos y su estado caduca solo.
/// Eventos (no retenidos): un estado «abierto» viejo nunca queda guardado en el host.
/// </summary>
public sealed class LaunchpadStatePublisher(Action<object> publish, Func<long> nowMs)
{
    public const string Topic = "launchpad.state";   // el mismo nombre que LaunchpadPin.Topic en el Dock (una prueba lo comprueba)
    public const long HeartbeatMs = 1500;
    private bool _open; private long _at;

    public void Tick(bool open, PxRect monitor)
    {
        long now = nowMs();
        if (open == _open && !(open && now - _at >= HeartbeatMs)) return;
        _open = open; _at = now;
        publish(new { open, x = monitor.X, y = monitor.Y, w = monitor.W, h = monitor.H });
    }
}
