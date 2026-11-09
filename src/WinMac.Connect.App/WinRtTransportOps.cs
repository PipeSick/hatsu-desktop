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
    public void RegisterAppForUser() => _t.RegisterAppForUser(CurrentUser());
    public void UnregisterAppForUser() => _t.UnregisterAppForUser(CurrentUser());
    /// <summary>El usuario de la sesión (API pública Windows.System.User). Si Windows no lo entrega, la excepción llega a la etapa con su HRESULT.</summary>
    private static Windows.System.User CurrentUser() => Windows.System.User.FindAllAsync(Windows.System.UserType.LocalUser, Windows.System.UserAuthenticationStatus.LocallyAuthenticated).AsTask().GetAwaiter().GetResult().FirstOrDefault() ?? throw new InvalidOperationException("Windows no entregó el usuario de la sesión");

    /// <summary>Un identificador de transporte → sus operaciones (FromId es local y rápido, pero igualmente se llama con tope por quien lo use).</summary>
    public static WinRtTransportOps FromId(string id) => new(PhoneLineTransportDevice.FromId(id));
    public string DeviceId => _t.DeviceId;
}
