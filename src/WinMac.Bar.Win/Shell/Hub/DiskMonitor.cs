using System.Diagnostics;
using WinMac.Bar.Hub;
using WinMac.SystemControl;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Discos físicos (MSFT_PhysicalDisk), particiones y volúmenes (DriveInfo), E/S por disco físico (contadores PDH crudos) y expulsión segura (verbo «Expulsar» del Shell, el mismo del Explorador).
/// La temperatura, el desgaste y las horas de uso salen de MSFT_StorageReliabilityCounter, que Windows suele reservar a administradores: si no se pueden leer se dice, y NUNCA se copian de un disco a otro.
/// </summary>
internal sealed class DiskMonitor : IDiskMonitor, IDisposable
{
    private PdhQuery? _io; private IntPtr? _read, _write;

    public DisksSnapshot ReadCatalog()
    {
        var volumes = new List<VolumeRec>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                volumes.Add(new VolumeRec(d.Name, d.VolumeLabel, d.DriveFormat, (ulong)d.TotalSize, (ulong)d.AvailableFreeSpace, d.DriveType == DriveType.Removable));
            }
            catch (IOException) { /* unidad que se desconectó a mitad de la lectura */ }
            catch (UnauthorizedAccessException) { }
        }

        var physical = new List<PhysicalRec>(); var partitions = new List<PartitionRec>(); string? reason = null;
        const string ns = @"root\Microsoft\Windows\Storage";
        try
        {
            var (smart, smartWhy) = ReadReliability(ns);
            IReadOnlyDictionary<int, DiskIncidents> incidents; try { incidents = DiskEventReader.Read().Map; } catch (Exception) { incidents = new Dictionary<int, DiskIncidents>(); }   // errores que Windows registró de cada disco (7 días)
            foreach (var o in Wmi.Query(ns, "SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk"))
            {
                int number = Wmi.I32(o["DeviceId"]); if (number < 0) continue;
                int hs = Wmi.I32(o["HealthStatus"], 5); string? health = hs switch { 0 => "Saludable", 1 => "Advertencia", 2 => "Con fallos", _ => null };
                var rel = smart.GetValueOrDefault(number); string bus = BusName(Wmi.I32(o["BusType"]));
                var sq = rel?.Temp is null || rel.Hours is null || rel.Wear is null ? StorageQuery.Read(number, bus == "NVMe") : null;   // lo que WMI no entrega, se le pide al propio disco
                double? temp = rel?.Temp ?? sq?.TempC, wear = sq?.WearPercent ?? rel?.Wear; long? hours = rel?.Hours ?? sq?.Hours;   // desgaste: manda el registro del propio disco (WMI puede dar «0» sin dato real)
                string? why = temp is null && hours is null && wear is null ? (sq?.Why ?? smartWhy) : null;
                physical.Add(new PhysicalRec(number, Wmi.Str(o["FriendlyName"]), Wmi.I32(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", _ => null }, bus, Wmi.U64(o["Size"]),
                    new SmartInfo(health, temp, hours, wear, sq?.ReadBytes, sq?.WrittenBytes, why, incidents.GetValueOrDefault(number))));
            }

            foreach (var o in Wmi.Query(ns, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
            {
                int code = o["DriveLetter"] switch { char c => c, string s when s.Length > 0 => s[0], null => 0, var v => Wmi.I32(v, 0) };
                if (code is >= 65 and <= 90 or >= 97 and <= 122) partitions.Add(new PartitionRec(Wmi.I32(o["DiskNumber"]), ((char)code).ToString()));
            }
        }
        catch (Exception e) { reason = "no se pudo leer la información de discos físicos (" + e.GetType().Name + ": " + Short(e.Message) + ")"; }

        // Volúmenes sin partición conocida por la vía moderna: se prueba la vía clásica (Win32_LogicalDiskToPartition → DiskIndex), que cubre más configuraciones de disco.
        foreach (var v in volumes)
        {
            string letter = v.Letter.Trim().TrimEnd('\\', '/').TrimEnd(':').ToUpperInvariant(); if (letter.Length != 1 || partitions.Any(p => string.Equals(p.DriveLetter, letter, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var row = Wmi.Query(@"root\cimv2", $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{letter}:'}} WHERE AssocClass = Win32_LogicalDiskToPartition").FirstOrDefault();
                if (row is not null && row.TryGetValue("DiskIndex", out var di) && di is not null) partitions.Add(new PartitionRec(Wmi.I32(di), letter));
            }
            catch (Exception) { /* sin esta vía el volumen queda sin disco físico, y se dice */ }
        }

        var snap = DiskCatalog.Build(physical, partitions, volumes);
        return reason is null ? snap : snap with { Reason = reason };
    }

    /// <summary>Lo que Windows dice de los discos, tal cual (para diagnosticar una asociación que falla): discos, discos físicos, particiones con letra y la vía clásica de cada volumen.</summary>
    public static IReadOnlyList<string> Diagnose()
    {
        var lines = new List<string>(); const string ns = @"root\Microsoft\Windows\Storage";
        void Try(string title, Action a) { try { a(); } catch (Exception e) { lines.Add($"{title}: {e.GetType().Name}: {Short(e.Message)}"); } }
        Try("MSFT_Disk", () => { foreach (var o in Wmi.Query(ns, "SELECT Number, FriendlyName, Size, BusType, PartitionStyle, IsOffline FROM MSFT_Disk")) lines.Add($"MSFT_Disk #{Wmi.Str(o["Number"])} «{Wmi.Str(o["FriendlyName"])}» {Wmi.U64(o["Size"]) >> 30} GB bus {Wmi.Str(o["BusType"])} estilo {Wmi.Str(o["PartitionStyle"])} sin conexión {Wmi.Str(o["IsOffline"])}"); });
        Try("MSFT_PhysicalDisk", () => { foreach (var o in Wmi.Query(ns, "SELECT DeviceId, FriendlyName, Size, BusType, MediaType FROM MSFT_PhysicalDisk")) lines.Add($"MSFT_PhysicalDisk id «{Wmi.Str(o["DeviceId"])}» «{Wmi.Str(o["FriendlyName"])}» {Wmi.U64(o["Size"]) >> 30} GB bus {Wmi.Str(o["BusType"])} medio {Wmi.Str(o["MediaType"])}"); });
        Try("MSFT_Partition", () => { foreach (var o in Wmi.Query(ns, "SELECT DiskNumber, PartitionNumber, DriveLetter, Size FROM MSFT_Partition")) lines.Add($"MSFT_Partition disco {Wmi.Str(o["DiskNumber"])} nº {Wmi.Str(o["PartitionNumber"])} letra «{(o["DriveLetter"] is char c && c != 0 ? c.ToString() : Wmi.Str(o["DriveLetter"]))}» {Wmi.U64(o["Size"]) >> 30} GB"); });
        Try("Win32_LogicalDiskToPartition", () => { foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed)) { string l = d.Name.TrimEnd('\\'); foreach (var o in Wmi.Query(@"root\cimv2", $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{l}'}} WHERE AssocClass = Win32_LogicalDiskToPartition")) lines.Add($"{l} → partición «{Wmi.Str(o["DeviceID"])}» disco {Wmi.Str(o["DiskIndex"])}"); } });
        return lines;
    }

    private static string Short(string m) { m = m.Replace('\r', ' ').Replace('\n', ' ').Trim(); return m.Length > 90 ? m[..90] + "…" : m; }

    private sealed record Reliability(double? Temp, long? Hours, double? Wear);

    /// <summary>Temperatura, desgaste y horas: Windows suele reservarlas a administradores. Si no se pueden leer, el motivo REAL queda en el texto (no se inventa nada).</summary>
    private static (Dictionary<int, Reliability> Map, string? Why) ReadReliability(string ns)
    {
        var map = new Dictionary<int, Reliability>(); string? why = null;
        try
        {
            foreach (var o in Wmi.Query(ns, "SELECT DeviceId, Temperature, Wear, PowerOnHours FROM MSFT_StorageReliabilityCounter"))
            {
                int n = Wmi.I32(o["DeviceId"]); if (n < 0) continue;
                double? temp = o["Temperature"] is { } t && Wmi.I64(t) > 0 ? Wmi.I64(t) : null; long? hours = o["PowerOnHours"] is { } h ? Wmi.I64(h) : null; double? wear = o["Wear"] is { } w ? Wmi.I64(w) : null;
                if (temp is null && hours is null && wear == 0) wear = null;   // fila vacía (temperatura 0, sin horas): su «desgaste 0» no es un dato
                map[n] = new Reliability(temp, hours, wear);
            }

            if (map.Count == 0) why = "Windows no entrega temperatura, desgaste ni horas de este disco";
        }
        catch (System.Management.ManagementException e) when (e.ErrorCode == System.Management.ManagementStatus.AccessDenied) { why = "Temperatura, desgaste y horas requieren abrir WinMac como administrador"; }
        catch (Exception e) { why = "No se pudieron leer temperatura, desgaste y horas (" + e.GetType().Name + ")"; }
        return (map, why);
    }

    private static string BusName(int t) => t switch { 7 => "USB", 17 => "NVMe", 11 => "SATA", 3 => "ATA", 8 => "RAID", 1 => "SCSI", 6 => "Fibre Channel", 9 => "iSCSI", 10 => "SAS", 12 => "SD", 13 => "MMC", _ => "" };

    public IReadOnlyList<DiskIo> ReadIo()
    {
        if (_io is null)
        {
            _io = new PdhQuery(); _read = _io.Add(@"\PhysicalDisk(*)\Disk Read Bytes/sec"); _write = _io.Add(@"\PhysicalDisk(*)\Disk Write Bytes/sec");
        }

        if (_read is null || _write is null) throw new InvalidOperationException("Este equipo no expone contadores de E/S de disco");
        if (!_io.Collect()) throw new InvalidOperationException("No se pudieron muestrear los contadores de disco");
        var reads = PdhQuery.Raw(_read.Value).Where(x => x.Name != "_Total").ToDictionary(x => Number(x.Name), x => x.Value); var writes = PdhQuery.Raw(_write.Value).Where(x => x.Name != "_Total").ToDictionary(x => Number(x.Name), x => x.Value);
        return reads.Where(kv => kv.Key >= 0).Select(kv => new DiskIo(kv.Key, (ulong)Math.Max(0, kv.Value), (ulong)Math.Max(0, writes.GetValueOrDefault(kv.Key)))).ToList();   // la instancia es «0 C:»: el número es el del disco físico
    }

    private static int Number(string instance) { int sp = instance.IndexOf(' '); return int.TryParse(sp < 0 ? instance : instance[..sp], out int n) ? n : -1; }

    /// <summary>Expulsión segura con el verbo «Expulsar» del Shell (el del Explorador). Solo unidades externas; si está en uso, Windows no la suelta y se avisa: nunca se fuerza.</summary>
    public ServiceResult Eject(string volumeLetter) => ServiceResult.Guard(() =>
    {
        string letter = volumeLetter.TrimEnd('\\', '/').ToUpperInvariant();
        var snap = ReadCatalog(); var vol = snap.Volumes.FirstOrDefault(v => v.Letter == letter);
        if (vol is null) return ServiceResult.Failed("Esa unidad ya no está conectada");
        if (!(vol.IsRemovable || snap.DiskOf(vol)?.IsExternal == true)) return ServiceResult.Unavailable("Solo se pueden expulsar unidades externas");
        string? error = null; var t = new Thread(() =>   // el Shell necesita un hilo STA
        {
            try { dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!; shell.Namespace(17).ParseName(letter + "\\").InvokeVerb("Eject"); }
            catch (Exception e) { error = e.GetType().Name; }
        }) { IsBackground = true }; t.SetApartmentState(ApartmentState.STA); t.Start();
        if (!t.Join(TimeSpan.FromSeconds(15))) return ServiceResult.Failed("Windows no respondió a tiempo a la expulsión");
        if (error is not null) return ServiceResult.Failed("Windows no pudo expulsar la unidad (" + error + ")");
        Thread.Sleep(1500);   // Windows retira la unidad un instante después
        bool still = DriveInfo.GetDrives().Any(d => string.Equals(d.Name.TrimEnd('\\'), letter, StringComparison.OrdinalIgnoreCase) && d.IsReady);
        return still ? ServiceResult.Failed("La unidad sigue en uso: cierra lo que la use e inténtalo de nuevo") : ServiceResult.Ok;
    }, "Expulsar");

    public void Dispose() { _io?.Dispose(); _io = null; }
}
