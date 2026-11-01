using System.Diagnostics;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>Los servicios reales del panel multifunción. Se crean una vez; nada consulta hasta que el panel está abierto.</summary>
public static class HubServicesFactory
{
    public static (HubServices Services, IDisposable Owner) CreateReal()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) }; http.DefaultRequestHeaders.UserAgent.ParseAdd("WinMac/1.0 (panel del tiempo)");
        var sys = new SystemMonitor(); var disks = new DiskMonitor();
        var services = new HubServices(sys, new NetworkMonitor(), disks, new OpenMeteoSource(http), new WinLocation(), new AppIcons(), new SpeedTestEngine(http, Stopwatch.GetTimestamp, Stopwatch.Frequency));
        return (services, new Owner(sys, disks, http));
    }

    private sealed class Owner(SystemMonitor sys, DiskMonitor disks, HttpClient http) : IDisposable { public void Dispose() { sys.Dispose(); disks.Dispose(); http.Dispose(); } }
}
