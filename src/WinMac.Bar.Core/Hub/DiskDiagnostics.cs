using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace WinMac.Bar.Hub;

// Lógica PURA del diagnóstico de discos (SMART/NVMe): construye las peticiones que se envían a Windows, interpreta sus respuestas y compara identidades.
// No toca Windows ni hardware: por eso se prueba en Linux. Lo que NO puede probarse aquí (que un SSD real responda) se mide con `--selftest-system` en el PC.

/// <summary>Errores Win32 que aparecen al consultar discos, con una explicación corta. Los demás se muestran con su número.</summary>
public static class Win32Errors
{
    public static string Describe(int code) => code switch
    {
        0 => "0 (correcto)",
        1 => "1 ERROR_INVALID_FUNCTION (el controlador no implementa esta solicitud)",
        2 => "2 ERROR_FILE_NOT_FOUND (la ruta no existe)",
        3 => "3 ERROR_PATH_NOT_FOUND (la ruta no existe)",
        5 => "5 ERROR_ACCESS_DENIED (Windows exige permisos que este proceso no tiene)",
        6 => "6 ERROR_INVALID_HANDLE",
        21 => "21 ERROR_NOT_READY (el dispositivo no está listo o no tiene medio)",
        31 => "31 ERROR_GEN_FAILURE (fallo general del dispositivo)",
        32 => "32 ERROR_SHARING_VIOLATION (otro proceso lo tiene abierto en exclusiva)",
        55 => "55 ERROR_DEV_NOT_EXIST (el controlador dice que el dispositivo no está disponible para esta solicitud)",
        50 => "50 ERROR_NOT_SUPPORTED (la solicitud no está soportada)",
        87 => "87 ERROR_INVALID_PARAMETER (el controlador rechazó los parámetros de la solicitud)",
        122 => "122 ERROR_INSUFFICIENT_BUFFER",
        234 => "234 ERROR_MORE_DATA (la respuesta no cabe en el búfer)",
        1117 => "1117 ERROR_IO_DEVICE (error de E/S del dispositivo)",
        1167 => "1167 ERROR_DEVICE_NOT_CONNECTED",
        _ => code.ToString(CultureInfo.InvariantCulture) + " (código sin descripción en Hatsu Desktop: búscalo como «System Error Code»)",
    };
}

/// <summary>Qué se le preguntó a Windows y qué respondió: una línea por cada <c>DeviceIoControl</c>.</summary>
public sealed record IoctlTrace(string Target, string Access, string Operation, bool Ok, int Win32Error, int BytesReturned, IReadOnlyList<string> Validation)
{
    public string Format()
    {
        var sb = new StringBuilder();
        sb.Append(Target).Append(" · acceso ").Append(Access).Append(" · ").Append(Operation).Append(" → ").Append(Ok ? "OK" : "FALLÓ");
        if (!Ok) sb.Append(" · error Win32 ").Append(Win32Errors.Describe(Win32Error));
        sb.Append(" · ").Append(BytesReturned.ToString(CultureInfo.InvariantCulture)).Append(" bytes");
        if (Validation.Count > 0) sb.Append(" · validación: ").Append(string.Join("; ", Validation));
        return sb.ToString();
    }
}

/// <summary>Nombre de cada tipo de bus de almacenamiento (STORAGE_BUS_TYPE).</summary>
public static class StorageBus
{
    public static string Name(int t) => t switch
    {
        0 => "desconocido", 1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "1394", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC",
        14 => "virtual", 15 => "virtual en archivo", 16 => "Espacios de almacenamiento", 17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => "tipo " + t.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>Las peticiones de solo lectura que se envían con IOCTL_STORAGE_QUERY_PROPERTY. Los números son los de STORAGE_PROPERTY_ID de winioctl.h.</summary>
public static class StorageRequests
{
    public const uint DeviceProperty = 0, AdapterProperty = 1, AdapterProtocolSpecificProperty = 49, DeviceProtocolSpecificProperty = 50, DeviceTemperatureProperty = 52;
    public const uint ProtocolTypeNvme = 3, NvmeDataTypeIdentify = 1, NvmeDataTypeLogPage = 2, NvmeIdentifyController = 1, NvmeLogHealthInfo = 2;

    /// <summary>STORAGE_PROPERTY_QUERY (8 bytes) + 1 de AdditionalParameters con relleno = 12.</summary>
    public const int PropertyQueryLength = 12;

    /// <summary>Cabecera de STORAGE_PROPERTY_QUERY (8) + STORAGE_PROTOCOL_SPECIFIC_DATA (40): lo que precede a los datos en una consulta o respuesta de protocolo.</summary>
    public const int ProtocolDescriptorHeader = 8, ProtocolSpecificBlock = 40, ProtocolPrefix = 48;

    public static byte[] PropertyQuery(uint propertyId)
    {
        var b = new byte[PropertyQueryLength];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0, 4), propertyId);   // PropertyId; QueryType = PropertyStandardQuery (0)
        return b;
    }

    /// <summary>Consulta específica de protocolo (NVMe): el resultado llega tras los 48 bytes del descriptor.</summary>
    public static byte[] ProtocolQuery(uint propertyId, uint protocolType, uint dataType, uint requestValue, uint requestSubValue, int dataLength)
    {
        var b = new byte[ProtocolPrefix + dataLength];
        Put(b, 0, propertyId); Put(b, 4, 0);                                      // PropertyId, QueryType = PropertyStandardQuery
        Put(b, 8, protocolType); Put(b, 12, dataType); Put(b, 16, requestValue); Put(b, 20, requestSubValue);
        Put(b, 24, ProtocolSpecificBlock);                                        // ProtocolDataOffset (desde el inicio de STORAGE_PROTOCOL_SPECIFIC_DATA)
        Put(b, 28, (uint)dataLength);                                             // ProtocolDataLength
        return b;
    }

    private static void Put(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset, 4), value);
}

public sealed record TemperatureParse(double? Celsius, IReadOnlyList<string> Problems);

/// <summary>Datos del registro de salud NVMe (log 0x02). Todo es opcional: lo que la respuesta no trae o no es plausible queda null y el motivo en <see cref="Problems"/>.</summary>
public sealed record NvmeHealth(double? TempC, long? Hours, double? WearPercent, double? ReadBytes, double? WrittenBytes, IReadOnlyList<string> Problems)
{
    public bool HasAny => TempC is not null || Hours is not null || WearPercent is not null || ReadBytes is not null || WrittenBytes is not null;
    public static NvmeHealth Failed(string problem) => new(null, null, null, null, null, [problem]);
}

public sealed record DeviceDescriptor(string? Vendor, string? Product, string? Revision, string? Serial, int BusType, IReadOnlyList<string> Problems);
public sealed record AdapterDescriptor(int BusType, uint MaxTransferBytes, IReadOnlyList<string> Problems);
public sealed record NvmeIdentify(int Vid, string? Serial, string? Model, string? Firmware, string? NvmeVersion, int Lpa, IReadOnlyList<string> Problems);
public sealed record DeviceNumberInfo(int DeviceType, int DeviceNumber, int PartitionNumber, IReadOnlyList<string> Problems);
public sealed record VolumeExtents(IReadOnlyList<int> Disks, IReadOnlyList<string> Problems);

/// <summary>Interpreta las respuestas de Windows. Ninguna inventa datos: si algo no cuadra, queda null y se explica el motivo.</summary>
public static class StorageResponse
{
    private static uint U32(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(o, 4));
    private static ulong U64(ReadOnlySpan<byte> r, int o) => BinaryPrimitives.ReadUInt64LittleEndian(r.Slice(o, 8));
    private static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>STORAGE_TEMPERATURE_DATA_DESCRIPTOR: InfoCount en +12 y el primer STORAGE_TEMPERATURE_INFO en +24 (Temperature, en °C, a +2).</summary>
    public static TemperatureParse ParseTemperature(ReadOnlySpan<byte> r)
    {
        const int first = 24, infoSize = 16;
        if (r.Length < first + infoSize) return new TemperatureParse(null, ["respuesta de " + Num(r.Length) + " bytes; se esperaban al menos " + Num(first + infoSize)]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(12, 2));
        if (count < 1) return new TemperatureParse(null, ["el disco informa 0 sensores de temperatura"]);
        short t = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(first + 2, 2));
        if (t is <= 0 or >= 150) return new TemperatureParse(null, ["temperatura fuera de rango plausible (" + Num(t) + " °C): se descarta"]);
        return new TemperatureParse(t, []);
    }

    /// <summary>
    /// STORAGE_PROTOCOL_DATA_DESCRIPTOR + registro de salud NVMe (log 0x02). Los datos empiezan en 8 + ProtocolDataOffset (normalmente 48). Temperatura compuesta en K (bytes 1-2), % usado (byte 5),
    /// unidades leídas (32) y escritas (48) de 512 000 bytes, horas de encendido (128).
    /// </summary>
    public static NvmeHealth ParseNvmeHealth(ReadOnlySpan<byte> r)
    {
        const int logLength = 144;   // hasta las horas de encendido inclusive
        if (r.Length < StorageRequests.ProtocolPrefix + logLength) return NvmeHealth.Failed("respuesta de " + Num(r.Length) + " bytes; se esperaban al menos " + Num(StorageRequests.ProtocolPrefix + logLength));
        uint dataOffset = U32(r, 24), dataLength = U32(r, 28);
        if (dataOffset < StorageRequests.ProtocolSpecificBlock || StorageRequests.ProtocolDescriptorHeader + (long)dataOffset + logLength > r.Length)
            return NvmeHealth.Failed("el descriptor indica ProtocolDataOffset=" + Num(dataOffset) + " y ProtocolDataLength=" + Num(dataLength) + ", que no caben en la respuesta de " + Num(r.Length) + " bytes");
        if (dataLength < logLength) return NvmeHealth.Failed("el descriptor indica ProtocolDataLength=" + Num(dataLength) + " (menos de los " + Num(logLength) + " bytes que hacen falta)");

        int d = StorageRequests.ProtocolDescriptorHeader + (int)dataOffset;
        var problems = new List<string>();
        int kelvin = r[d + 1] | (r[d + 2] << 8);
        double? temp = null;
        if (kelvin is > 273 and < 423) temp = kelvin - 273.15; else problems.Add("temperatura compuesta fuera de rango plausible (" + Num(kelvin) + " K): se descarta");
        int wearRaw = r[d + 5];
        if (wearRaw > 100) problems.Add("porcentaje usado " + Num(wearRaw) + " (> 100): se limita a 100");
        double? wear = Math.Min(wearRaw, 100);
        ulong read = U64(r, d + 32), written = U64(r, d + 48), hours = U64(r, d + 128);
        long? h = null;
        if (hours is > 0 and < 2_000_000) h = (long)hours; else problems.Add("horas de encendido fuera de rango plausible (" + hours.ToString(CultureInfo.InvariantCulture) + "): se descartan");
        double? readBytes = read > 0 ? read * 512_000.0 : null;
        double? writtenBytes = written > 0 ? written * 512_000.0 : null;
        if (temp is null && h is null && readBytes is null && writtenBytes is null)
        {
            // Un registro sin ningún dato verosímil (p. ej. todo ceros) no puede dar un «% usado» fiable: ese byte valdría 0 y el panel mostraría un 0 % inventado.
            problems.Add("el registro llegó sin datos útiles (todo en cero o fuera de rango): se descarta entero, incluido el % usado");
            return new NvmeHealth(null, null, null, null, null, problems);
        }

        return new NvmeHealth(temp, h, wear, readBytes, writtenBytes, problems);
    }

    /// <summary>STORAGE_DEVICE_DESCRIPTOR: desplazamientos a fabricante (+12), producto (+16), revisión (+20) y serie (+24) —cadenas ASCII terminadas en cero— y tipo de bus (+28).</summary>
    public static DeviceDescriptor ParseDeviceDescriptor(ReadOnlySpan<byte> r)
    {
        if (r.Length < 36) return new DeviceDescriptor(null, null, null, null, -1, ["respuesta de " + Num(r.Length) + " bytes; se esperaban al menos 36"]);
        var problems = new List<string>();
        string? vendor = AsciiAt(r, 12, "fabricante", problems), product = AsciiAt(r, 16, "producto", problems), revision = AsciiAt(r, 20, "revisión", problems), serial = AsciiAt(r, 24, "serie", problems);
        return new DeviceDescriptor(vendor, product, revision, serial, (int)U32(r, 28), problems);
    }

    private static string? AsciiAt(ReadOnlySpan<byte> r, int fieldOffset, string name, List<string> problems)
    {
        uint off = U32(r, fieldOffset);
        if (off == 0) return null;
        if (off >= r.Length) { problems.Add(name + ": desplazamiento " + Num(off) + " fuera de la respuesta de " + Num(r.Length) + " bytes"); return null; }
        int start = (int)off, end = start;
        while (end < r.Length && r[end] != 0) end++;
        string s = Encoding.ASCII.GetString(r.Slice(start, end - start)).Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>STORAGE_ADAPTER_DESCRIPTOR: transferencia máxima (+8) y tipo de bus del adaptador (+24, un byte).</summary>
    public static AdapterDescriptor ParseAdapterDescriptor(ReadOnlySpan<byte> r)
    {
        if (r.Length < 32) return new AdapterDescriptor(-1, 0, ["respuesta de " + Num(r.Length) + " bytes; se esperaban al menos 32"]);
        return new AdapterDescriptor(r[24], U32(r, 8), []);
    }

    /// <summary>Respuesta de NVMe Identify Controller (CNS 1): VID, serie, modelo, firmware, versión NVMe y LPA (byte 261, atributos de las páginas de registro).</summary>
    public static NvmeIdentify ParseNvmeIdentify(ReadOnlySpan<byte> r)
    {
        const int need = 262;
        if (r.Length < StorageRequests.ProtocolPrefix + need) return new NvmeIdentify(-1, null, null, null, null, -1, ["respuesta de " + Num(r.Length) + " bytes; se esperaban al menos " + Num(StorageRequests.ProtocolPrefix + need)]);
        uint dataOffset = U32(r, 24);
        if (dataOffset < StorageRequests.ProtocolSpecificBlock || StorageRequests.ProtocolDescriptorHeader + (long)dataOffset + need > r.Length)
            return new NvmeIdentify(-1, null, null, null, null, -1, ["el descriptor indica ProtocolDataOffset=" + Num(dataOffset) + ", que no cabe en la respuesta de " + Num(r.Length) + " bytes"]);
        int d = StorageRequests.ProtocolDescriptorHeader + (int)dataOffset;
        int vid = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(d, 2));
        uint ver = U32(r, d + 80), tertiary = ver & 0xFF;
        string version = Num(ver >> 16) + "." + Num((ver >> 8) & 0xFF) + (tertiary == 0 ? "" : "." + Num(tertiary));
        return new NvmeIdentify(vid, AsciiTrim(r.Slice(d + 4, 20)), AsciiTrim(r.Slice(d + 24, 40)), AsciiTrim(r.Slice(d + 64, 8)), version, r[d + 261], []);
    }

    private static string? AsciiTrim(ReadOnlySpan<byte> s)
    {
        var text = Encoding.ASCII.GetString(s).Trim('\0', ' ');
        return text.Length == 0 ? null : text;
    }

    /// <summary>STORAGE_DEVICE_NUMBER (IOCTL_STORAGE_GET_DEVICE_NUMBER): tipo de dispositivo, número de disco y número de partición (-1 = disco entero).</summary>
    public static DeviceNumberInfo ParseDeviceNumber(ReadOnlySpan<byte> r)
    {
        if (r.Length < 12) return new DeviceNumberInfo(-1, -1, -1, ["respuesta de " + Num(r.Length) + " bytes; se esperaban 12"]);
        return new DeviceNumberInfo((int)U32(r, 0), (int)U32(r, 4), (int)U32(r, 8), []);
    }

    /// <summary>VOLUME_DISK_EXTENTS (IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS): cuántos extents y, desde +8, 24 bytes por extent con el número de disco al inicio.</summary>
    public static VolumeExtents ParseVolumeExtents(ReadOnlySpan<byte> r)
    {
        if (r.Length < 8) return new VolumeExtents([], ["respuesta de " + Num(r.Length) + " bytes; se esperaban al menos 8"]);
        uint count = U32(r, 0);
        if (count == 0) return new VolumeExtents([], ["el volumen no declara ningún extent en discos"]);
        if (8L + count * 24L > r.Length) return new VolumeExtents([], ["el volumen declara " + Num(count) + " extents, que no caben en la respuesta de " + Num(r.Length) + " bytes"]);
        var disks = new List<int>();
        for (int i = 0; i < count; i++) disks.Add((int)U32(r, 8 + i * 24));
        return new VolumeExtents(disks, []);
    }
}

/// <summary>Escapado de cadenas para consultas WQL (para ASSOCIATORS OF con claves que llevan barras invertidas).</summary>
public static class Wql
{
    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

/// <summary>De la ruta del controlador (ImagePath del servicio) al archivo real del driver.</summary>
public static class DriverFiles
{
    public static string? ResolveImagePath(string? imagePath, string systemRoot)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string p = imagePath.Trim().Trim('"'), root = systemRoot.TrimEnd('\\');
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) return p[4..];
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) return root + "\\" + p[12..];
        if (p.StartsWith("%SystemRoot%", StringComparison.OrdinalIgnoreCase)) return root + p[12..];
        if (p.Length > 1 && p[1] == ':') return p;
        return root + "\\" + p.TrimStart('\\');   // relativa a la carpeta de Windows (p. ej. System32\drivers\stornvme.sys)
    }
}

/// <summary>Qué fuente dio cada métrica: la primera que la entregó, en el orden en que se prueban.</summary>
public sealed record Metric(double? Value, string Source);

public static class MetricPick
{
    public static Metric First(params (string Source, double? Value)[] candidates)
    {
        foreach (var (source, value) in candidates) if (value is not null) return new Metric(value, source);
        return new Metric(null, "ninguna fuente lo entregó");
    }
}

/// <summary>Lo que una fuente (WMI, IOCTL…) dice del disco que ella llama «número N».</summary>
public sealed record DriveIdentity(string Source, string? Model, string? Serial, ulong SizeBytes);

/// <summary>Un hallazgo al comparar fuentes. <see cref="Inconsistent"/> = las fuentes no describen el mismo disco bajo el mismo número.</summary>
public sealed record IdentityFinding(bool Inconsistent, string Text);

public enum SerialMatch { Same, Different, NotComparable }

/// <summary>
/// Comprueba, en vez de suponer, que «disco N» designa el mismo dispositivo en MSFT_PhysicalDisk, MSFT_Disk, Win32_DiskDrive y la ruta PhysicalDriveN.
/// El discriminante es el número de serie (dos SSD del mismo modelo y capacidad solo se distinguen por él); los formatos de serie pueden variar entre APIs,
/// por eso se comparan solo letras y dígitos y se acepta que uno contenga al otro.
/// </summary>
public static class DiskIdentity
{
    public static string NormalizeSerial(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    public static SerialMatch CompareSerial(string? a, string? b)
    {
        string x = NormalizeSerial(a), y = NormalizeSerial(b);
        if (x.Length < 4 || y.Length < 4) return SerialMatch.NotComparable;
        return x == y || x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal) ? SerialMatch.Same : SerialMatch.Different;
    }

    /// <summary>null = no comparable (falta un tamaño). Tolerancia del 1 %: las APIs redondean la capacidad de forma distinta.</summary>
    public static bool? SizesAgree(ulong a, ulong b)
    {
        if (a == 0 || b == 0) return null;
        ulong max = Math.Max(a, b), diff = a > b ? a - b : b - a;
        return diff <= max / 100;
    }

    private static bool? ModelsAgree(string? a, string? b)
    {
        string x = NormalizeSerial(a), y = NormalizeSerial(b);   // mismas reglas: solo letras y dígitos, sin distinguir mayúsculas
        if (x.Length == 0 || y.Length == 0) return null;
        return x == y || x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal);
    }

    /// <summary>Compara cada fuente con la primera. <paramref name="missing"/> son las fuentes que no tienen ninguna entrada con ese número.</summary>
    public static IReadOnlyList<IdentityFinding> Compare(int number, IReadOnlyList<DriveIdentity> present, IReadOnlyList<string> missing)
    {
        var f = new List<IdentityFinding>();
        foreach (var m in missing) f.Add(new IdentityFinding(false, m + ": no tiene ninguna entrada con el número " + number.ToString(CultureInfo.InvariantCulture) + " (AUSENTE)"));
        if (present.Count < 2) { f.Add(new IdentityFinding(false, "solo una fuente conoce este número: no hay con qué compararla")); return f; }

        var reference = present[0];
        foreach (var other in present.Skip(1))
        {
            string pair = reference.Source + " <-> " + other.Source;
            switch (CompareSerial(reference.Serial, other.Serial))
            {
                case SerialMatch.Same: f.Add(new IdentityFinding(false, pair + ": el número de serie coincide")); break;
                case SerialMatch.Different:
                    f.Add(new IdentityFinding(true, pair + ": los números de serie DIFIEREN («" + reference.Serial + "» frente a «" + other.Serial + "»): el número podría no designar el mismo disco en ambas fuentes, o Windows formatea el serie de forma distinta"));
                    break;
                default: f.Add(new IdentityFinding(false, pair + ": serie no comparable (falta en una de las fuentes o es demasiado corta)")); break;
            }

            bool? size = SizesAgree(reference.SizeBytes, other.SizeBytes);
            if (size == false) f.Add(new IdentityFinding(true, pair + ": el tamaño DIFIERE (" + reference.SizeBytes.ToString(CultureInfo.InvariantCulture) + " frente a " + other.SizeBytes.ToString(CultureInfo.InvariantCulture) + " bytes)"));
            else if (size == true) f.Add(new IdentityFinding(false, pair + ": el tamaño coincide (±1 %)"));

            if (ModelsAgree(reference.Model, other.Model) == false) f.Add(new IdentityFinding(true, pair + ": el modelo DIFIERE («" + reference.Model + "» frente a «" + other.Model + "»)"));
        }

        return f;
    }
}
