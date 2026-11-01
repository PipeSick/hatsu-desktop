using Windows.Devices.Radios;
using Windows.Foundation;

namespace WinMac.SystemControl.Win;

/// <summary>Radios de Windows (Wi-Fi y Bluetooth) por <c>Windows.Devices.Radios</c>: API pública, sin administrador. Las llamadas WinRT asíncronas se esperan con tiempo límite desde hilos de trabajo.</summary>
internal static class RadioHelper
{
    public static T Await<T>(IAsyncOperation<T> op, int ms = 8000)
    {
        var t = op.AsTask();
        if (!t.Wait(ms)) throw new TimeoutException("Windows no respondió a tiempo");
        return t.Result;
    }

    public static IReadOnlyList<Radio> Of(RadioKind kind)
    {
        var access = Await(Radio.RequestAccessAsync());
        if (access != RadioAccessStatus.Allowed) throw new UnauthorizedAccessException("Windows no permite a esta app controlar las radios (" + access + ")");
        return Await(Radio.GetRadiosAsync()).Where(r => r.Kind == kind).ToList();
    }

    public static ServiceResult SetState(RadioKind kind, bool on, string what) => ServiceResult.Guard(() =>
    {
        var radios = Of(kind);
        if (radios.Count == 0) return ServiceResult.Unavailable($"{what}: no hay adaptador");
        foreach (var r in radios)
        {
            var res = Await(r.SetStateAsync(on ? RadioState.On : RadioState.Off));
            if (res == RadioAccessStatus.DeniedByUser || res == RadioAccessStatus.DeniedBySystem) return ServiceResult.Denied($"{what}: Windows denegó el cambio ({res})");
            if (res != RadioAccessStatus.Allowed) return ServiceResult.Failed($"{what}: resultado {res}");
        }

        return ServiceResult.Ok;
    }, what);
}
