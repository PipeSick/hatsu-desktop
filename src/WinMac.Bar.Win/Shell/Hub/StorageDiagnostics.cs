using System.Globalization;
using Microsoft.Win32.SafeHandles;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Informe técnico de `--selftest-system` para entender por qué un disco entrega temperatura, desgaste y horas y otro del mismo modelo no. SOLO LECTURA: consultas WMI, consultas de propiedades
/// al disco (IOCTL_STORAGE_QUERY_PROPERTY), al volumen y al Administrador de dispositivos. No requiere administrador, no escribe en ningún disco, no cambia ninguna configuración.
/// El panel Discos NO muestra nada de esto: aquí está el detalle (rutas, niveles de acceso, errores Win32, bytes, origen de cada métrica).
/// </summary>
internal static class StorageDiagnostics
{
    private const string StorageNs = @"root\Microsoft\Windows\Storage", Cim = @"root\cimv2";

    private sealed class Data
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Phys { get; init; } = [];
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Disks { get; init; } = [];
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Drives { get; init; } = [];
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Parts { get; init; } = [];
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rel { get; init; } = [];
        public IReadOnlyList<IReadOnlyDictionary<string, object?>> Pools { get; init; } = [];
    }

    /// <summary>Lo que se averiguó de un disco durante las consultas (la primera respuesta válida de cada tipo).</summary>
    private sealed class Probe
    {
        public DeviceNumberInfo? Number;
        public DeviceDescriptor? Device;
        public AdapterDescriptor? Adapter;
        public TemperatureParse? Temp;
        public NvmeIdentify? Identify;
        public NvmeHealth? LogViaDevice;
        public NvmeHealth? LogViaAdapter;
    }

    public static IReadOnlyList<string> Run()
    {
        var lines = new List<string> { "Diagnóstico SMART/NVMe por disco (solo lectura, sin administrador; no se escribe nada en ningún disco ni se cambia ninguna configuración):" };
        try { Build(lines); }
        catch (Exception e) { lines.Add("  el diagnóstico de discos se interrumpió: " + e.GetType().Name + ": " + e.Message); }
        return lines;
    }

    private static void Build(List<string> lines)
    {
        var data = new Data
        {
            Phys = TryQuery(StorageNs, "SELECT * FROM MSFT_PhysicalDisk", lines, "MSFT_PhysicalDisk"),
            Disks = TryQuery(StorageNs, "SELECT * FROM MSFT_Disk", lines, "MSFT_Disk"),
            Drives = TryQuery(Cim, "SELECT * FROM Win32_DiskDrive", lines, "Win32_DiskDrive"),
            Parts = TryQuery(StorageNs, "SELECT DiskNumber, DriveLetter FROM MSFT_Partition", lines, "MSFT_Partition"),
            Rel = TryQuery(StorageNs, "SELECT * FROM MSFT_StorageReliabilityCounter", lines, "MSFT_StorageReliabilityCounter"),
            Pools = TryQuery(StorageNs, "SELECT * FROM MSFT_StoragePool", lines, "MSFT_StoragePool"),
        };

        // ── Volúmenes: cada letra preguntada al propio volumen, y comparada con lo que dice WMI
        lines.Add("  Volúmenes → disco (se le pregunta al propio volumen con IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS y se compara con MSFT_Partition):");
        var lettersByDisk = new Dictionary<int, List<string>>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            string letter = d.Name.TrimEnd('\\');
            var (trace, nums) = StorageQuery.VolumeDisks(letter);
            foreach (int n in nums) { if (!lettersByDisk.TryGetValue(n, out var list)) lettersByDisk[n] = list = []; list.Add(letter); }
            var wmiNums = data.Parts.Where(p => string.Equals(LetterOf(p), letter.TrimEnd(':'), StringComparison.OrdinalIgnoreCase)).Select(p => Wmi.I32(p.GetValueOrDefault("DiskNumber"))).Distinct().OrderBy(x => x).ToList();
            string verdict = !trace.Ok || wmiNums.Count == 0 ? "no comparable" : nums.OrderBy(x => x).SequenceEqual(wmiNums) ? "COINCIDEN" : "DIFIEREN";
            lines.Add($"    {letter} → el volumen dice disco(s) [{string.Join(",", nums)}] · MSFT_Partition dice [{string.Join(",", wmiNums)}] · {verdict}");
            lines.Add("        " + trace.Format());
        }

        // ── Números de disco a examinar: los que conoce cualquier fuente, y los que Windows abre aunque WMI no los liste
        var known = new SortedSet<int>(lettersByDisk.Keys);
        foreach (var r in data.Phys) AddNumber(known, Wmi.I32(r.GetValueOrDefault("DeviceId")));
        foreach (var r in data.Disks) AddNumber(known, Wmi.I32(r.GetValueOrDefault("Number")));
        foreach (var r in data.Drives) AddNumber(known, Wmi.I32(r.GetValueOrDefault("Index")));
        int top = Math.Min((known.Count == 0 ? 0 : known.Max) + 3, 31);
        var numbers = new SortedSet<int>(known);
        var absent = new List<string>();
        for (int n = 0; n <= top; n++)
        {
            if (known.Contains(n)) continue;
            using var probe = StorageQuery.Open($@"\\.\PhysicalDrive{n}", StorageQuery.QueryAccess, out int err);
            if (probe is null) absent.Add($"PhysicalDrive{n} (error {err})"); else numbers.Add(n);
        }

        lines.Add($"  Rutas \\\\.\\PhysicalDriveN sondeadas de 0 a {top}. Sin dispositivo: {(absent.Count == 0 ? "ninguna" : string.Join(", ", absent))}");

        var summary = new List<string>();
        foreach (int n in numbers) Block(n, !known.Contains(n), data, lettersByDisk.GetValueOrDefault(n), lines, summary);

        // ── Espacios de almacenamiento
        var userPools = data.Pools.Where(p => Show(p, "IsPrimordial") == "False").ToList();
        lines.Add("  Grupos de Espacios de almacenamiento (no primordiales): " + (userPools.Count == 0 ? "ninguno" : string.Join(", ", userPools.Select(p => "«" + Show(p, "FriendlyName") + "»"))));

        lines.Add("  RESUMEN (una línea por disco):");
        foreach (var s in summary) lines.Add("    " + s);
        lines.Add("Fin del diagnóstico de discos. Pega todo el texto de «Diagnóstico SMART/NVMe por disco» hasta aquí.");
    }

    private static void Block(int n, bool extra, Data data, List<string>? letters, List<string> lines, List<string> summary)
    {
        string path = $@"\\.\PhysicalDrive{n}";
        lines.Add($"  ── {path}{(extra ? " (Windows lo abre, pero ninguna fuente de WMI lo lista)" : "")} ── volúmenes en este disco: {(letters is null ? "ninguno detectado" : string.Join(", ", letters))}");

        var phys = Find(data.Phys, "DeviceId", n); var disk = Find(data.Disks, "Number", n); var drive = Find(data.Drives, "Index", n);
        var p = new Probe();

        // 1) Consultas al propio dispositivo, con cada nivel de acceso por separado
        lines.Add("    Consultas al dispositivo (cada una es una llamada DeviceIoControl independiente):");
        foreach (uint access in new[] { StorageQuery.QueryAccess, StorageQuery.ReadAccess })
        {
            using var h = StorageQuery.Open(path, access, out int err);
            if (h is null) { lines.Add("      " + new IoctlTrace(path, StorageQuery.AccessName(access), "abrir el disco", false, err, 0, Array.Empty<string>()).Format()); continue; }
            lines.Add("      " + new IoctlTrace(path, StorageQuery.AccessName(access), "abrir el disco", true, 0, 0, Array.Empty<string>()).Format());
            foreach (var op in new[] { StorageOp.DeviceNumber, StorageOp.DeviceDescriptor, StorageOp.AdapterDescriptor, StorageOp.Temperature }) RunOne(h, path, access, op, n, p, lines);
            bool nvme = Wmi.I32(phys?.GetValueOrDefault("BusType"), -1) == 17 || p.Device?.BusType == 17 || p.Adapter?.BusType == 17 || (Opt(drive, "PNPDeviceID")?.Contains("NVME", StringComparison.OrdinalIgnoreCase) ?? false);
            if (!nvme) { lines.Add("      (no se detecta NVMe en este disco: se omiten las consultas NVMe)"); continue; }
            foreach (var op in new[] { StorageOp.NvmeIdentify, StorageOp.NvmeHealthViaDevice, StorageOp.NvmeHealthViaAdapter }) RunOne(h, path, access, op, n, p, lines);
        }

        // 2) Lo que dice cada fuente de WMI sobre «el disco N»
        lines.Add("    Identidad según cada fuente:");
        var present = new List<DriveIdentity>(); var missing = new List<string>();
        if (phys is null) missing.Add("MSFT_PhysicalDisk (DeviceId)");
        else
        {
            present.Add(new DriveIdentity("MSFT_PhysicalDisk.DeviceId", Opt(phys, "FriendlyName"), Opt(phys, "SerialNumber"), Wmi.U64(phys.GetValueOrDefault("Size"))));
            lines.Add($"      MSFT_PhysicalDisk DeviceId={n}: modelo «{Show(phys, "FriendlyName")}» · serie «{Show(phys, "SerialNumber")}» · {Size(phys)} · bus {StorageBus.Name(Wmi.I32(phys.GetValueOrDefault("BusType"), -1))} · salud {Show(phys, "HealthStatus")} · firmware {Show(phys, "FirmwareVersion")}");
            lines.Add($"        UniqueId {Show(phys, "UniqueId")} · CanPool {Show(phys, "CanPool")} · CannotPoolReason {Show(phys, "CannotPoolReason")} · Usage {Show(phys, "Usage")} · ObjectId {Show(phys, "ObjectId")}");
        }

        if (disk is null) missing.Add("MSFT_Disk (Number)");
        else
        {
            present.Add(new DriveIdentity("MSFT_Disk.Number", Opt(disk, "FriendlyName"), Opt(disk, "SerialNumber"), Wmi.U64(disk.GetValueOrDefault("Size"))));
            lines.Add($"      MSFT_Disk Number={n}: modelo «{Show(disk, "FriendlyName")}» · serie «{Show(disk, "SerialNumber")}» · {Size(disk)} · bus {StorageBus.Name(Wmi.I32(disk.GetValueOrDefault("BusType"), -1))} · estilo {Show(disk, "PartitionStyle")} · sin conexión {Show(disk, "IsOffline")} · sistema {Show(disk, "IsSystem")}");
            lines.Add($"        UniqueId {Show(disk, "UniqueId")} · Path {Show(disk, "Path")} · Location {Show(disk, "Location")}");
        }

        if (drive is null) missing.Add("Win32_DiskDrive (Index)");
        else
        {
            present.Add(new DriveIdentity("Win32_DiskDrive.Index", Opt(drive, "Model"), Opt(drive, "SerialNumber"), Wmi.U64(drive.GetValueOrDefault("Size"))));
            lines.Add($"      Win32_DiskDrive Index={n}: DeviceID «{Show(drive, "DeviceID")}» · modelo «{Show(drive, "Model")}» · serie «{Show(drive, "SerialNumber")}» · {Size(drive)} · interfaz {Show(drive, "InterfaceType")} · firmware {Show(drive, "FirmwareRevision")}");
            lines.Add($"        PNPDeviceID {Show(drive, "PNPDeviceID")} · SCSI bus {Show(drive, "SCSIBus")} puerto {Show(drive, "SCSIPort")} destino {Show(drive, "SCSITargetId")} LUN {Show(drive, "SCSILogicalUnit")} · particiones {Show(drive, "Partitions")}");
        }

        if (p.Device is { } dev && (dev.Product is not null || dev.Serial is not null))
            present.Add(new DriveIdentity(path + " (StorageDeviceProperty)", dev.Product, dev.Serial, 0));
        else missing.Add(path + " (StorageDeviceProperty)");

        // 3) ¿«N» designa el mismo disco en todas las fuentes? (se comprueba, no se supone)
        var findings = DiskIdentity.Compare(n, present, missing);
        bool inconsistent = findings.Any(f => f.Inconsistent);
        lines.Add("    Comprobación de identidad (¿el número " + n + " designa el mismo disco en todas las fuentes?):");
        foreach (var f in findings) lines.Add("      " + (f.Inconsistent ? "[!!] " : "[ok] ") + f.Text);

        // 4) Controlador y driver
        string? pnp = Opt(drive, "PNPDeviceID");
        lines.Add("    Cadena de dispositivos y drivers (Administrador de dispositivos, solo lectura):");
        if (pnp is null) lines.Add("      sin Win32_DiskDrive para este número: no se puede recorrer la cadena");
        else foreach (var l in DeviceTree.Describe(pnp)) lines.Add("      " + l);

        // 5) WMI de fiabilidad (directa y por asociación) y métricas finales con su origen
        var (wmiTemp, wmiWear, wmiHours) = Reliability(n, phys, data, lines);
        var temp = MetricPick.First(("WMI (MSFT_StorageReliabilityCounter)", wmiTemp), ("IOCTL registro NVMe 0x02 (vía dispositivo)", p.LogViaDevice?.TempC), ("IOCTL registro NVMe 0x02 (vía adaptador)", p.LogViaAdapter?.TempC), ("IOCTL propiedad de temperatura (52)", p.Temp?.Celsius));
        var wear = MetricPick.First(("WMI (MSFT_StorageReliabilityCounter)", wmiWear), ("IOCTL registro NVMe 0x02 (vía dispositivo)", p.LogViaDevice?.WearPercent), ("IOCTL registro NVMe 0x02 (vía adaptador)", p.LogViaAdapter?.WearPercent));
        var hours = MetricPick.First(("WMI (MSFT_StorageReliabilityCounter)", wmiHours), ("IOCTL registro NVMe 0x02 (vía dispositivo)", p.LogViaDevice?.Hours), ("IOCTL registro NVMe 0x02 (vía adaptador)", p.LogViaAdapter?.Hours));
        lines.Add("    Métricas finales (primero WMI y después el propio disco; OJO: el panel solo usa la vía «por dispositivo», no la «por adaptador», que aquí se prueba para diagnóstico):");
        lines.Add($"      temperatura {Fmt(temp, " °C")} · origen: {temp.Source}");
        lines.Add($"      desgaste    {Fmt(wear, " %")} · origen: {wear.Source}");
        lines.Add($"      horas       {Fmt(hours, " h")} · origen: {hours.Source}");

        string model = Opt(drive, "Model") ?? Opt(phys, "FriendlyName") ?? p.Device?.Product ?? "modelo desconocido";
        summary.Add($"PhysicalDrive{n} · {model} · serie {Opt(drive, "SerialNumber") ?? Opt(phys, "SerialNumber") ?? "n/d"} · volúmenes {(letters is null ? "ninguno" : string.Join(",", letters))} · temp {Fmt(temp, " °C")} ({temp.Source}) · desgaste {Fmt(wear, " %")} · horas {Fmt(hours, " h")} · identidad {(inconsistent ? "INCONSISTENTE" : "coherente")}");
    }

    /// <summary>Ejecuta una consulta, la interpreta y escribe su línea (traza + validación + lo que dice).</summary>
    private static void RunOne(SafeFileHandle h, string path, uint access, StorageOp op, int number, Probe p, List<string> lines)
    {
        var step = StorageQuery.Run(h, path, access, op);
        IReadOnlyList<string> problems = Array.Empty<string>(); string? detail = null;
        if (step.Trace.Ok) (detail, problems) = Interpret(op, number, step.Data, p);
        lines.Add("      " + (step.Trace with { Validation = problems }).Format());
        if (detail is not null) lines.Add("          → " + detail);
    }

    private static (string? Detail, IReadOnlyList<string> Problems) Interpret(StorageOp op, int number, byte[] data, Probe p)
    {
        switch (op)
        {
            case StorageOp.DeviceNumber:
            {
                var r = StorageResponse.ParseDeviceNumber(data); p.Number ??= r;
                var problems = r.Problems.ToList();
                if (problems.Count == 0 && r.DeviceNumber != number) problems.Add("el dispositivo abierto como PhysicalDrive" + number + " dice ser el disco " + r.DeviceNumber);
                return ($"tipo de dispositivo {r.DeviceType}, número de disco {r.DeviceNumber}, partición {r.PartitionNumber}", problems);
            }
            case StorageOp.DeviceDescriptor:
            {
                var r = StorageResponse.ParseDeviceDescriptor(data); p.Device ??= r;
                return ($"fabricante «{r.Vendor ?? "n/d"}» · producto «{r.Product ?? "n/d"}» · revisión «{r.Revision ?? "n/d"}» · serie «{r.Serial ?? "n/d"}» · bus {StorageBus.Name(r.BusType)}", r.Problems);
            }
            case StorageOp.AdapterDescriptor:
            {
                var r = StorageResponse.ParseAdapterDescriptor(data); p.Adapter ??= r;
                return ($"bus del adaptador {StorageBus.Name(r.BusType)} · transferencia máxima {r.MaxTransferBytes / 1024} KB", r.Problems);
            }
            case StorageOp.Temperature:
            {
                var r = StorageResponse.ParseTemperature(data); p.Temp ??= r;
                return (r.Celsius is { } c ? $"temperatura {c.ToString("0.##", CultureInfo.InvariantCulture)} °C" : "sin temperatura utilizable", r.Problems);
            }
            case StorageOp.NvmeIdentify:
            {
                var r = StorageResponse.ParseNvmeIdentify(data); p.Identify ??= r;
                return ($"VID 0x{r.Vid:X4} · modelo «{r.Model ?? "n/d"}» · serie «{r.Serial ?? "n/d"}» · firmware «{r.Firmware ?? "n/d"}» · NVMe {r.NvmeVersion ?? "n/d"} · LPA 0x{r.Lpa:X2}", r.Problems);
            }
            case StorageOp.NvmeHealthViaDevice:
            case StorageOp.NvmeHealthViaAdapter:
            {
                var r = StorageResponse.ParseNvmeHealth(data);
                if (op == StorageOp.NvmeHealthViaDevice) p.LogViaDevice ??= r; else p.LogViaAdapter ??= r;
                return (HealthText(r), r.Problems);
            }
            default: return (null, Array.Empty<string>());
        }
    }

    private static string HealthText(NvmeHealth r) =>
        $"temperatura {Num(r.TempC, " °C")} · desgaste {Num(r.WearPercent, " %")} · horas {(r.Hours is { } h ? h.ToString(CultureInfo.InvariantCulture) : "n/d")} · leído {Bytes(r.ReadBytes)} · escrito {Bytes(r.WrittenBytes)}";

    /// <summary>Fiabilidad según WMI, de dos maneras: consulta directa por DeviceId y asociación con el MSFT_PhysicalDisk. Devuelve lo que WMI entregó (null si no).</summary>
    private static (double? Temp, double? Wear, double? Hours) Reliability(int n, IReadOnlyDictionary<string, object?>? phys, Data data, List<string> lines)
    {
        lines.Add("    Fiabilidad según WMI (temperatura / desgaste / horas):");
        var direct = Find(data.Rel, "DeviceId", n);
        lines.Add("      consulta directa a MSFT_StorageReliabilityCounter con DeviceId=" + n + ": " + (direct is null ? "SIN fila para este número" : $"fila encontrada → temperatura {Show(direct, "Temperature")} · desgaste {Show(direct, "Wear")} · horas {Show(direct, "PowerOnHours")}"));
        IReadOnlyDictionary<string, object?>? assoc = null;
        string? oid = Opt(phys, "ObjectId");
        if (oid is null) lines.Add("      por asociación con MSFT_PhysicalDisk: sin MSFT_PhysicalDisk (o sin ObjectId) para este número");
        else
        {
            try
            {
                assoc = Wmi.Query(StorageNs, "ASSOCIATORS OF {MSFT_PhysicalDisk.ObjectId=" + Wql.Quote(oid) + "} WHERE ResultClass = MSFT_StorageReliabilityCounter").FirstOrDefault();
                lines.Add("      por asociación con MSFT_PhysicalDisk (ObjectId): " + (assoc is null ? "SIN fila" : $"fila encontrada → temperatura {Show(assoc, "Temperature")} · desgaste {Show(assoc, "Wear")} · horas {Show(assoc, "PowerOnHours")}"));
            }
            catch (Exception e) { lines.Add("      por asociación con MSFT_PhysicalDisk (ObjectId): la consulta falló (" + e.GetType().Name + ": " + Short(e.Message) + ")"); }
        }

        var row = direct ?? assoc;
        double? temp = Positive(row, "Temperature"), wear = Positive(row, "Wear", allowZero: true), hours = Positive(row, "PowerOnHours", allowZero: true);
        return (temp, wear, hours);
    }

    private static double? Positive(IReadOnlyDictionary<string, object?>? row, string key, bool allowZero = false)
    {
        if (row is null || !row.TryGetValue(key, out var v) || v is null) return null;
        long x = Wmi.I64(v);
        return x > 0 || (allowZero && x == 0) ? x : null;
    }

    // ── utilidades de formato y de consulta
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> TryQuery(string scope, string wql, List<string> lines, string title)
    {
        try { return Wmi.Query(scope, wql); }
        catch (Exception e) { lines.Add($"  {title}: la consulta falló ({e.GetType().Name}: {Short(e.Message)})"); return Array.Empty<IReadOnlyDictionary<string, object?>>(); }
    }

    private static IReadOnlyDictionary<string, object?>? Find(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string key, int n) => rows.FirstOrDefault(r => Wmi.I32(r.GetValueOrDefault(key)) == n);

    private static void AddNumber(SortedSet<int> set, int n) { if (n >= 0) set.Add(n); }

    private static string LetterOf(IReadOnlyDictionary<string, object?> row)
    {
        object? v = row.GetValueOrDefault("DriveLetter");
        int code = v switch { char c => c, string s when s.Length > 0 => s[0], null => 0, _ => Wmi.I32(v, 0) };
        bool isLetter = code is >= 65 and <= 90 or >= 97 and <= 122;
        return isLetter ? ((char)code).ToString() : "";
    }

    private static string? Opt(IReadOnlyDictionary<string, object?>? row, string key)
    {
        if (row is null || !row.TryGetValue(key, out var v) || v is null) return null;
        string s = v is Array a ? string.Join(",", a.Cast<object?>().Select(x => x?.ToString() ?? "")) : v.ToString() ?? "";
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    private static string Show(IReadOnlyDictionary<string, object?>? row, string key) => Opt(row, key) ?? "n/d";

    private static string Size(IReadOnlyDictionary<string, object?> row)
    {
        ulong bytes = Wmi.U64(row.GetValueOrDefault("Size"));
        return bytes > 0 ? Units.FormatSize(bytes, 1) : "tamaño n/d";
    }

    private static string Fmt(Metric m, string unit) => m.Value is { } v ? v.ToString("0.##", CultureInfo.InvariantCulture) + unit : "no disponible";
    private static string Num(double? v, string unit) => v is { } x ? x.ToString("0.##", CultureInfo.InvariantCulture) + unit : "n/d";
    private static string Bytes(double? v) => v is { } x ? Units.FormatSize(x, 1) : "n/d";
    private static string Short(string m) { m = m.Replace('\r', ' ').Replace('\n', ' ').Trim(); return m.Length > 120 ? m[..120] + "…" : m; }
}
