using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace WinMac.Connect.App;

/// <summary>
/// Las operaciones del transporte telefónico de Windows, SÍNCRONAS y sin límite de tiempo propio. Solo se llaman desde el <see cref="StageWorker"/> de un <see cref="TransportSequence"/>
/// (hilo dedicado, una a la vez, con tope por etapa): ni el hilo del módulo ni el de la interfaz pueden quedar colgados por ellas.
/// </summary>
internal sealed class WinRtTransportOps : ITransportOps
{
    private readonly PhoneLineTransportDevice _t;
    public WinRtTransportOps(PhoneLineTransportDevice t) { _t = t; }

    public AccessResult RequestAccess()
    {
        var a = _t.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
        return a == DeviceAccessStatus.Allowed ? AccessResult.Allowed : a == DeviceAccessStatus.DeniedByUser ? AccessResult.DeniedByUser : a == DeviceAccessStatus.DeniedBySystem ? AccessResult.DeniedBySystem : AccessResult.Other;
    }

    public bool IsRegistered() => _t.IsRegistered();
    public void RegisterApp() => _t.RegisterApp();
    public bool Connect() => _t.ConnectAsync().AsTask().GetAwaiter().GetResult();
    public void UnregisterApp() => _t.UnregisterApp();

    /// <summary>Un identificador de transporte → sus operaciones (FromId es local y rápido, pero igualmente se llama con tope por quien lo use).</summary>
    public static WinRtTransportOps FromId(string id) => new(PhoneLineTransportDevice.FromId(id));
    public string DeviceId => _t.DeviceId;
}
