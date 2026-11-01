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
        dynamic? storage = Wmi.Connect(@"root\Microsoft\Windows\Storage");
        if (storage is null) reason = "Windows no expone la información de discos físicos";
        else
        {
            try
            {
                var smart = ReadReliability(storage);
                foreach (dynamic o in storage.ExecQuery("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk"))
                {
                    int number = int.TryParse((string)o.DeviceId, out int n) ? n : -1; if (number < 0) continue;
                    string health = (int)o.HealthStatus switch { 0 => "Saludable", 1 => "Advertencia", 2 => "Con fallos", _ => "Desconocido" };
                    var rel = smart.GetValueOrDefault(number);
                    physical.Add(new PhysicalRec(number, (string?)o.FriendlyName ?? "", ((int)o.MediaType) switch { 3 => "HDD", 4 => "SSD", _ => null }, BusName((int)o.BusType), (ulong)o.Size,
                        new SmartInfo((int)o.HealthStatus is 0 or 1 or 2 ? health : null, rel?.Temp, rel?.Hours, rel?.Wear, null, null, rel is null ? "Temperatura, desgaste y horas requieren permisos de administrador o el dispositivo no las entrega" : null)));
                }

                foreach (dynamic o in storage.ExecQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
                {
                    int code = (int)(ushort)o.DriveLetter; if (code is >= 65 and <= 90 or >= 97 and <= 122) partitions.Add(new PartitionRec((int)(uint)o.DiskNumber, ((char)code).ToString()));
                }
            }
            catch (Exception e) { reason = "No se pudo leer la información de discos físicos: " + e.GetType().Name; }
        }

        var snap = DiskCatalog.Build(physical, partitions, volumes);
        return reason is null ? snap : snap with { Reason = reason };
    }

    private sealed record Reliability(double? Temp, long? Hours, double? Wear);

    private static Dictionary<int, Reliability> ReadReliability(dynamic storage)
    {
        var map = new Dictionary<int, Reliability>();
        try
        {
            foreach (dynamic o in storage.ExecQuery("SELECT DeviceId, Temperature, Wear, PowerOnHours FROM MSFT_StorageReliabilityCounter"))
            {
                if (!int.TryParse((string)o.DeviceId, out int n)) continue;
                map[n] = new Reliability(o.Temperature is null || (int)o.Temperature <= 0 ? null : (int)o.Temperature, o.PowerOnHours is null ? null : (long)(uint)o.PowerOnHours, o.Wear is null ? null : (int)o.Wear);
            }
        }
        catch (Exception) { /* acceso denegado (requiere administrador): los discos quedan con «no disponible» y su motivo */ }
        return map;
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
