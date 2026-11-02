using System.Diagnostics.Eventing.Reader;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Lee del registro «Sistema» los errores de discos y controladoras de los últimos 7 días (solo lectura; el registro Sistema lo puede leer un usuario normal) y los atribuye a cada disco con <see cref="DiskEventLog"/>.
/// Se cachea 10 minutos: el catálogo de discos se relee cada 30 s y esto no cambia tan rápido.
/// </summary>
internal static class DiskEventReader
{
    private static readonly object Gate = new();
    private static (DateTime At, IReadOnlyDictionary<int, DiskIncidents> Map, string? Why)? _cache;

    public static (IReadOnlyDictionary<int, DiskIncidents> Map, string? Why) Read()
    {
        lock (Gate)
        {
            if (_cache is { } c && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(10)) return (c.Map, c.Why);
            var (map, why) = ReadNow();
            _cache = (DateTime.UtcNow, map, why);
            return (map, why);
        }
    }

    /// <summary>Eventos crudos + mapa puerto→discos, para el diagnóstico.</summary>
    public static (IReadOnlyList<DiskEventRec> Events, IReadOnlyDictionary<int, IReadOnlyList<int>> Ports, string? Why) ReadRaw()
    {
        var ports = new Dictionary<int, IReadOnlyList<int>>();
        try
        {
            ports = Wmi.Query(@"root\cimv2", "SELECT Index, SCSIPort FROM Win32_DiskDrive")
                .Where(o => o.GetValueOrDefault("SCSIPort") is not null && o.GetValueOrDefault("Index") is not null)
                .GroupBy(o => Wmi.I32(o["SCSIPort"]))
                .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(o => Wmi.I32(o["Index"])).Distinct().ToList());
        }
        catch (Exception) { /* sin puertos, los eventos de controladora no se atribuyen (los de disk sí) */ }

        var events = new List<DiskEventRec>();
        try
        {
            string providers = string.Join(" or ", DiskEventLog.Providers.Select(p => $"@Name='{p}'"));
            string ids = string.Join(" or ", DiskEventLog.EventIds.Select(i => "EventID=" + i));
            string xpath = $"*[System[Provider[{providers}] and ({ids}) and TimeCreated[timediff(@SystemTime) <= {(long)DiskEventLog.Window.TotalMilliseconds}]]]";
            using var reader = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath));
            for (int i = 0; i < 2000 && reader.ReadEvent() is { } e; i++)
                using (e)
                {
                    var strings = e.Properties.Select(p => p.Value as string).Where(s => s is not null).Select(s => s!).ToList();
                    events.Add(new DiskEventRec(e.TimeCreated ?? DateTime.MinValue, e.ProviderName ?? "", e.Id, strings));
                }
        }
        catch (Exception ex) { return (events, ports, "no se pudo leer el registro Sistema (" + ex.GetType().Name + ")"); }
        return (events, ports, null);
    }

    private static (IReadOnlyDictionary<int, DiskIncidents>, string?) ReadNow()
    {
        var (events, ports, why) = ReadRaw();
        return (DiskEventLog.Attribute(events, ports), why);
    }
}
