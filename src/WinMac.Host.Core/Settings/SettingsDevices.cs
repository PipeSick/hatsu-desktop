using System.Text.Json;
using WinMac.Bar.Notch;
using WinMac.Connect;

namespace WinMac.Host;

/// <summary>
/// Settings V2: lo último que el host sabe del iPhone (lo publica el módulo Connect) y de los periféricos (lo publica la Barra). Solo lectura y en memoria: no se guarda nada ni se lee nada nuevo.
/// Los eventos llegan desde el hilo del canal; <see cref="Changed"/> avisa para redibujar.
/// </summary>
public sealed class SettingsDevices
{
    private readonly object _l = new();
    private PhoneState? _phone; private IReadOnlyList<DeviceInfo> _devices = []; private bool _received;
    public event Action? Changed;
    public PhoneState? Phone { get { lock (_l) return _phone; } }
    public IReadOnlyList<DeviceInfo> Devices { get { lock (_l) return _devices; } }
    /// <summary>La Barra ya envió al menos una lista (aunque vacía): permite distinguir «aún no hay datos» de «no hay periféricos».</summary>
    public bool DevicesReceived { get { lock (_l) return _received; } }

    /// <summary>Datos de MUESTRA para las capturas del CI y las pruebas (no se usa en la aplicación real).</summary>
    public void Seed(PhoneState? phone, IReadOnlyList<DeviceInfo> devices) { lock (_l) { _phone = phone; _devices = devices; _received = true; } }

    public void OnEvent(string topic, JsonElement? data)
    {
        bool changed = false;
        try
        {
            if (topic == DevicesFeed.Topic) { var list = DevicesFeed.Parse(data); lock (_l) { changed = !_received || !_devices.SequenceEqual(list); _devices = list; _received = true; } }
            else if (topic == ConnectTopics.State) { var s = ConnectWire.ParseState(data); lock (_l) { changed = !Equals(_phone, s); _phone = s; } }
        }
        catch (Exception) { }
        if (changed) Changed?.Invoke();
    }
}
