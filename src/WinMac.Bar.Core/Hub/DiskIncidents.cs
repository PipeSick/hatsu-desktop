using System.Globalization;
using System.Text.RegularExpressions;

namespace WinMac.Bar.Hub;

/// <summary>Un evento del registro «Sistema» tal cual lo entrega Windows: proveedor, id, hora y los textos de inserción (donde va la ruta del dispositivo, p. ej. «\Device\Harddisk0\DR0» o «\Device\RaidPort2»).</summary>
public sealed record DiskEventRec(DateTime Time, string Provider, int EventId, IReadOnlyList<string> Strings);

/// <summary>Errores que Windows registró de UN disco físico en los últimos días. Es lo que Windows anotó, no una medida del disco: un disco «Saludable» puede tener errores de E/S registrados.</summary>
public sealed record DiskIncidents(int IoErrors, int ControllerErrors, int Resets, DateTime Last)
{
    public int Total => IoErrors + ControllerErrors + Resets;

    /// <summary>Texto corto para el panel: «3 errores de E/S · 1 reinicio de la controladora · último 31/10 01:09».</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (IoErrors > 0) parts.Add(IoErrors + (IoErrors == 1 ? " error de E/S" : " errores de E/S"));
        if (ControllerErrors > 0) parts.Add(ControllerErrors + (ControllerErrors == 1 ? " error de la controladora" : " errores de la controladora"));
        if (Resets > 0) parts.Add(Resets + (Resets == 1 ? " reinicio de la controladora" : " reinicios de la controladora"));
        parts.Add("último " + Last.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture));
        return string.Join(" · ", parts);
    }
}

/// <summary>
/// Atribuye eventos del registro «Sistema» a discos físicos SIN suponer nada: un evento solo cuenta para un disco si su ruta lo nombra.
/// «\Device\HarddiskN\…» (proveedor disk) es el disco N; «\Device\RaidPortN» (stornvme/storahci/storport) es el puerto SCSI N, y solo se atribuye si Windows dice qué disco cuelga de ese puerto
/// (Win32_DiskDrive.SCSIPort) y es uno solo. Lo que no se puede atribuir se descarta, no se reparte.
/// </summary>
public static partial class DiskEventLog
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>Proveedores y eventos que se leen. disk: 7 sector defectuoso, 51 error en paginación, 153 E/S reintentada, 157 disco retirado por sorpresa. Controladoras: 11 error de la controladora, 129 reinicio del dispositivo.</summary>
    public static readonly IReadOnlyList<string> Providers = ["disk", "stornvme", "storahci", "storport"];
    public static readonly IReadOnlyList<int> EventIds = [7, 11, 51, 129, 153, 157];

    [GeneratedRegex(@"\\Device\\Harddisk(\d+)\\", RegexOptions.IgnoreCase)] private static partial Regex HarddiskRx();
    [GeneratedRegex(@"\\Device\\RaidPort(\d+)\b", RegexOptions.IgnoreCase)] private static partial Regex RaidPortRx();

    /// <param name="portToDisks">Puerto SCSI → números de disco que cuelgan de él (de Win32_DiskDrive).</param>
    public static IReadOnlyDictionary<int, DiskIncidents> Attribute(IEnumerable<DiskEventRec> events, IReadOnlyDictionary<int, IReadOnlyList<int>> portToDisks)
    {
        var acc = new Dictionary<int, (int Io, int Ctl, int Rst, DateTime Last)>();
        foreach (var e in events)
        {
            bool isDisk = string.Equals(e.Provider, "disk", StringComparison.OrdinalIgnoreCase);
            int? disk = null;
            foreach (var s in e.Strings)
            {
                if (string.IsNullOrEmpty(s)) continue;
                if (isDisk) { var m = HarddiskRx().Match(s + "\\"); if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)) { disk = n; break; } }
                else
                {
                    var m = RaidPortRx().Match(s);
                    if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && portToDisks.TryGetValue(port, out var list) && list.Count == 1) { disk = list[0]; break; }
                }
            }
            if (disk is not { } d) continue;

            (int io, int ctl, int rst) = (isDisk, e.EventId) switch
            {
                (true, 7 or 51 or 153 or 157) => (1, 0, 0),
                (false, 11) => (0, 1, 0),
                (false, 129) => (0, 0, 1),
                _ => (0, 0, 0),
            };
            if (io + ctl + rst == 0) continue;
            var cur = acc.GetValueOrDefault(d);
            acc[d] = (cur.Io + io, cur.Ctl + ctl, cur.Rst + rst, e.Time > cur.Last ? e.Time : cur.Last);
        }
        return acc.ToDictionary(kv => kv.Key, kv => new DiskIncidents(kv.Value.Io, kv.Value.Ctl, kv.Value.Rst, kv.Value.Last));
    }
}
