using Windows.ApplicationModel.Calls;

namespace WinMac.Connect.App;

/// <summary>
/// EXPERIMENTAL, solo lectura (docs/56): observa el estado de llamada que Windows expone con la API pública <c>PhoneCallManager</c>
/// (<c>IsCallActive</c>, <c>CallStateChanged</c>). No contesta, no cuelga, no marca, no toca HFP ni <c>PhoneLineTransportDevice</c>.
/// <para>NO está validado que Windows refleje ahí las llamadas del iPhone (depende de que la pila HFP del sistema esté conectada y de la compilación).
/// Por eso: siempre se REGISTRA (sin contenido) lo que Windows dice, para que la prueba manual dé evidencia; y solo alimenta la Isla si el usuario lo activa
/// con la variable de entorno <c>HATSU_CONNECT_SYSTEM_CALLS=1</c> (y aun así <see cref="CallTracker.OnTelephony"/> exige que este iPhone haya sonado).</para>
/// </summary>
internal sealed class SystemCallObserver : IDisposable
{
    /// <summary>Alimenta la Isla solo si el usuario lo pidió: variable de entorno (avanzado) o el interruptor TEMPORAL de Ajustes › Diagnóstico (se apaga solo, ver <see cref="MaxMinutes"/>).</summary>
    public static bool FeedEnabled => Environment.GetEnvironmentVariable("HATSU_CONNECT_SYSTEM_CALLS") == "1" || TemporaryFeed;
    public static volatile bool TemporaryFeed;
    public const int MaxMinutes = 15;
    private readonly Action<bool> _onActive; private readonly Action<string> _log; private bool _started; private bool _last;

    public SystemCallObserver(Action<bool> onActive, Action<string> log) { _onActive = onActive; _log = log; }

    public void Start()
    {
        try
        {
            _last = PhoneCallManager.IsCallActive;
            PhoneCallManager.CallStateChanged += OnChanged; _started = true;
            _log($"[connect] telefonía del sistema: observador disponible (llamada activa ahora: {_last}; alimenta la Isla: {FeedEnabled})");
        }
        catch (Exception e) { _log("[connect] telefonía del sistema: no disponible (" + e.GetType().Name + "); la llamada sigue dependiendo solo de ANCS"); }
    }

    private void OnChanged(object? sender, object e)
    {
        try
        {
            bool active = PhoneCallManager.IsCallActive, incoming = PhoneCallManager.IsCallIncoming;
            _log($"[connect] telefonía del sistema: activa={active}, entrante={incoming}");   // sin número ni nombre
            if (active == _last) return;
            _last = active;
            if (FeedEnabled) _onActive(active);
        }
        catch (Exception ex) { _log("[connect] telefonía del sistema: " + ex.GetType().Name); }
    }

    public void Dispose() { TemporaryFeed = false; if (_started) { try { PhoneCallManager.CallStateChanged -= OnChanged; } catch { } _started = false; } }
}
