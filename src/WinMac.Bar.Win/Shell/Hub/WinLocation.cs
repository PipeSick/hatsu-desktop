using Windows.Devices.Geolocation;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Ubicación del equipo por el servicio de Windows. SOLO se usa si el usuario activó «ubicación automática» y Windows concede el permiso; no se pide ni se lee nada en otro caso.
/// Devuelve null si el permiso se niega o el servicio de ubicación está desactivado (el panel lo explica).
/// </summary>
internal sealed class WinLocation : ILocationProvider
{
    public async Task<GeoPoint?> GetAsync(CancellationToken ct)
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
            var pos = await new Geolocator { DesiredAccuracyInMeters = 5000 }.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            var p = pos.Coordinate.Point.Position; return new GeoPoint("Mi ubicación", null, p.Latitude, p.Longitude);
        }
        catch (Exception) { return null; }   // servicio desactivado, sin dispositivo de ubicación o tiempo agotado
    }
}
