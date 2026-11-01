using System.Buffers.Binary;
using System.Text;
using WinMac.Bar.Hub;
using Xunit;

namespace WinMac.Bar.Core.Tests;

// Lógica PURA del diagnóstico SMART/NVMe: formato de las peticiones, interpretación y validación de respuestas, errores Win32 y comparación de identidades.
// IMPORTANTE: estas pruebas usan búferes sintéticos para fijar los DESPLAZAMIENTOS documentados de las estructuras de Windows y las reglas de validación.
// NO demuestran que ningún SSD real responda a estas consultas: eso solo se mide con `--selftest-system` en el PC.
public class DiskDiagnosticsTests
{
    private static void W16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o, 2), v);
    private static void W32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o, 4), v);
    private static void W64(byte[] b, int o, ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o, 8), v);
    private static uint R32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
    private static void Text(byte[] b, int o, string s) { var bytes = Encoding.ASCII.GetBytes(s); bytes.CopyTo(b, o); b[o + bytes.Length] = 0; }

    // ── errores Win32 y trazas
    [Fact]
    public void Win32_errors_are_named_when_known_and_fall_back_to_the_number()
    {
        Assert.Contains("ERROR_ACCESS_DENIED", Win32Errors.Describe(5));
        Assert.Contains("ERROR_INVALID_FUNCTION", Win32Errors.Describe(1));
        Assert.Contains("ERROR_NOT_SUPPORTED", Win32Errors.Describe(50));
        Assert.Contains("ERROR_INVALID_PARAMETER", Win32Errors.Describe(87));
        Assert.StartsWith("0 ", Win32Errors.Describe(0));
        string unknown = Win32Errors.Describe(9999);
        Assert.StartsWith("9999 ", unknown); Assert.Contains("System Error Code", unknown);   // un código sin nombre se muestra con su número, nunca se inventa una descripción
    }

    [Fact]
    public void A_trace_line_carries_every_field_and_shows_the_error_only_when_the_call_failed()
    {
        var ok = new IoctlTrace(@"\\.\PhysicalDrive1", "0 (solo consulta)", "operación A", true, 0, 512, []).Format();
        Assert.Contains(@"\\.\PhysicalDrive1", ok); Assert.Contains("acceso 0 (solo consulta)", ok); Assert.Contains("operación A", ok); Assert.Contains("→ OK", ok); Assert.Contains("512 bytes", ok);
        Assert.DoesNotContain("error Win32", ok); Assert.DoesNotContain("validación", ok);

        var bad = new IoctlTrace(@"\\.\PhysicalDrive0", "GENERIC_READ", "operación B", false, 5, 0, ["fallo uno", "fallo dos"]).Format();
        Assert.Contains("FALLÓ", bad); Assert.Contains("error Win32 5 ERROR_ACCESS_DENIED", bad); Assert.Contains("0 bytes", bad); Assert.Contains("validación: fallo uno; fallo dos", bad);
    }

    [Fact]
    public void Storage_bus_types_have_names_and_unknown_ones_show_their_number()
    {
        Assert.Equal("NVMe", StorageBus.Name(17)); Assert.Equal("SATA", StorageBus.Name(11)); Assert.Equal("USB", StorageBus.Name(7));
        Assert.Equal("Espacios de almacenamiento", StorageBus.Name(16)); Assert.Equal("tipo 99", StorageBus.Name(99));
    }

    // ── peticiones
    [Fact]
    public void Property_ids_are_the_ones_documented_in_winioctl_h()
    {
        // STORAGE_PROPERTY_ID: StorageDevicePowerProperty = 12 (NO es la temperatura; el commit a3a16ee la pedía con ese número), StorageDeviceTemperatureProperty = 52.
        Assert.Equal(0u, StorageRequests.DeviceProperty); Assert.Equal(1u, StorageRequests.AdapterProperty);
        Assert.Equal(49u, StorageRequests.AdapterProtocolSpecificProperty); Assert.Equal(50u, StorageRequests.DeviceProtocolSpecificProperty); Assert.Equal(52u, StorageRequests.DeviceTemperatureProperty);
    }

    [Fact]
    public void A_property_query_is_a_standard_query_of_twelve_bytes_with_only_the_property_id_set()
    {
        var q = StorageRequests.PropertyQuery(StorageRequests.DeviceTemperatureProperty);
        Assert.Equal(StorageRequests.PropertyQueryLength, q.Length); Assert.Equal(52u, R32(q, 0)); Assert.Equal(0u, R32(q, 4));   // QueryType = PropertyStandardQuery
        Assert.All(q.Skip(8), x => Assert.Equal((byte)0, x));
    }

    [Fact]
    public void A_protocol_query_places_the_nvme_request_and_the_data_window_at_the_documented_offsets()
    {
        var q = StorageRequests.ProtocolQuery(StorageRequests.DeviceProtocolSpecificProperty, StorageRequests.ProtocolTypeNvme, StorageRequests.NvmeDataTypeLogPage, StorageRequests.NvmeLogHealthInfo, 0, 512);
        Assert.Equal(StorageRequests.ProtocolPrefix + 512, q.Length);
        Assert.Equal(50u, R32(q, 0)); Assert.Equal(0u, R32(q, 4));
        Assert.Equal(3u, R32(q, 8)); Assert.Equal(2u, R32(q, 12)); Assert.Equal(2u, R32(q, 16)); Assert.Equal(0u, R32(q, 20));   // NVMe, página de registro, registro de salud 0x02, sin sub-valor
        Assert.Equal(40u, R32(q, 24)); Assert.Equal(512u, R32(q, 28));                                                         // ProtocolDataOffset y ProtocolDataLength

        var id = StorageRequests.ProtocolQuery(StorageRequests.DeviceProtocolSpecificProperty, StorageRequests.ProtocolTypeNvme, StorageRequests.NvmeDataTypeIdentify, StorageRequests.NvmeIdentifyController, 0, 4096);
        Assert.Equal(StorageRequests.ProtocolPrefix + 4096, id.Length); Assert.Equal(1u, R32(id, 12)); Assert.Equal(1u, R32(id, 16)); Assert.Equal(4096u, R32(id, 28));
    }

    // ── temperatura
    private static byte[] TemperatureResponse(ushort infoCount, short celsius)
    {
        var r = new byte[24 + 16];
        W16(r, 12, infoCount); W16(r, 24 + 2, unchecked((ushort)celsius));
        return r;
    }

    [Fact]
    public void Temperature_is_read_from_the_first_sensor_and_implausible_or_missing_data_is_reported_not_invented()
    {
        var good = StorageResponse.ParseTemperature(TemperatureResponse(1, 41));
        Assert.NotNull(good.Celsius); Assert.Equal(41.0, good.Celsius.GetValueOrDefault()); Assert.Empty(good.Problems);

        Assert.Contains("0 sensores", Assert.Single(StorageResponse.ParseTemperature(TemperatureResponse(0, 41)).Problems));
        var zero = StorageResponse.ParseTemperature(TemperatureResponse(1, 0)); Assert.Null(zero.Celsius); Assert.Contains("fuera de rango", Assert.Single(zero.Problems));
        var hot = StorageResponse.ParseTemperature(TemperatureResponse(1, 200)); Assert.Null(hot.Celsius); Assert.Contains("fuera de rango", Assert.Single(hot.Problems));

        var shortResponse = StorageResponse.ParseTemperature(new byte[10]); Assert.Null(shortResponse.Celsius); Assert.Contains("se esperaban al menos 40", Assert.Single(shortResponse.Problems));
    }

    // ── registro de salud NVMe
    private static byte[] NvmeResponse(uint dataOffset = 40, uint dataLength = 512, int total = 48 + 512)
    {
        var r = new byte[total];
        W32(r, 24, dataOffset); W32(r, 28, dataLength);
        return r;
    }

    [Fact]
    public void The_nvme_health_parser_reads_each_field_from_its_documented_offset_of_a_synthetic_log()
    {
        var r = NvmeResponse(); int d = 8 + 40;
        W16(r, d + 1, 310);            // temperatura compuesta en kelvin
        r[d + 5] = 7;                  // porcentaje usado
        W64(r, d + 32, 1000);          // unidades de 512 000 bytes leídas
        W64(r, d + 48, 2000);          // escritas
        W64(r, d + 128, 1234);         // horas de encendido
        var h = StorageResponse.ParseNvmeHealth(r);
        Assert.True(h.HasAny); Assert.Empty(h.Problems);
        Assert.Equal(36.85, h.TempC.GetValueOrDefault(), 2); Assert.Equal(7.0, h.WearPercent.GetValueOrDefault()); Assert.Equal(1234L, h.Hours.GetValueOrDefault());
        Assert.Equal(512_000_000.0, h.ReadBytes.GetValueOrDefault()); Assert.Equal(1_024_000_000.0, h.WrittenBytes.GetValueOrDefault());
    }

    [Fact]
    public void A_log_that_is_too_short_or_whose_descriptor_does_not_fit_is_rejected_with_the_reason()
    {
        var tooShort = StorageResponse.ParseNvmeHealth(new byte[100]);
        Assert.False(tooShort.HasAny); Assert.Contains("se esperaban al menos 192", Assert.Single(tooShort.Problems));

        var farOffset = StorageResponse.ParseNvmeHealth(NvmeResponse(dataOffset: 1000)); Assert.False(farOffset.HasAny); Assert.Contains("ProtocolDataOffset=1000", Assert.Single(farOffset.Problems));
        var nearOffset = StorageResponse.ParseNvmeHealth(NvmeResponse(dataOffset: 8)); Assert.False(nearOffset.HasAny); Assert.Contains("ProtocolDataOffset=8", Assert.Single(nearOffset.Problems));
        var shortData = StorageResponse.ParseNvmeHealth(NvmeResponse(dataLength: 100)); Assert.False(shortData.HasAny); Assert.Contains("ProtocolDataLength=100", Assert.Single(shortData.Problems));
    }

    [Fact]
    public void A_log_with_no_plausible_data_is_discarded_whole_so_a_zero_percent_used_is_never_invented()
    {
        var r = NvmeResponse(); r[8 + 40 + 5] = 250;   // todo lo demás en cero: temperatura 0 K, 0 horas, 0 datos leídos/escritos
        var h = StorageResponse.ParseNvmeHealth(r);
        Assert.False(h.HasAny); Assert.Null(h.WearPercent); Assert.Null(h.TempC); Assert.Null(h.Hours);
        Assert.Contains(h.Problems, p => p.Contains("sin datos útiles")); Assert.Contains(h.Problems, p => p.Contains("temperatura compuesta fuera de rango"));
    }

    [Fact]
    public void A_genuine_zero_percent_used_is_kept_when_the_rest_of_the_log_is_plausible_and_values_above_100_are_clamped_with_a_note()
    {
        var fresh = NvmeResponse(); int d = 8 + 40;
        W16(fresh, d + 1, 305); W64(fresh, d + 128, 12);   // disco casi nuevo: 0 % usado es un dato real
        var a = StorageResponse.ParseNvmeHealth(fresh); Assert.True(a.HasAny); Assert.Equal(0.0, a.WearPercent.GetValueOrDefault()); Assert.NotNull(a.WearPercent);

        var worn = NvmeResponse(); W16(worn, d + 1, 305); worn[d + 5] = 255;
        var b = StorageResponse.ParseNvmeHealth(worn); Assert.Equal(100.0, b.WearPercent.GetValueOrDefault()); Assert.Contains(b.Problems, p => p.Contains("> 100"));
    }

    // ── otras respuestas
    [Fact]
    public void Volume_extents_list_every_disk_the_volume_spans_and_reject_inconsistent_responses()
    {
        var one = new byte[32]; W32(one, 0, 1); W32(one, 8, 1);
        Assert.Equal(new[] { 1 }, StorageResponse.ParseVolumeExtents(one).Disks.ToArray());

        var two = new byte[56]; W32(two, 0, 2); W32(two, 8, 0); W32(two, 8 + 24, 2);
        Assert.Equal(new[] { 0, 2 }, StorageResponse.ParseVolumeExtents(two).Disks.ToArray());

        var none = StorageResponse.ParseVolumeExtents(new byte[32]); Assert.Empty(none.Disks); Assert.Contains("ningún extent", Assert.Single(none.Problems));
        var lying = new byte[32]; W32(lying, 0, 3); var l = StorageResponse.ParseVolumeExtents(lying); Assert.Empty(l.Disks); Assert.Contains("no caben", Assert.Single(l.Problems));
        Assert.Contains("al menos 8", Assert.Single(StorageResponse.ParseVolumeExtents(new byte[4]).Problems));
    }

    [Fact]
    public void The_device_number_response_gives_type_disk_and_partition_and_a_short_response_is_reported()
    {
        var r = new byte[12]; W32(r, 0, 7); W32(r, 4, 5); W32(r, 8, 0xFFFFFFFF);
        var n = StorageResponse.ParseDeviceNumber(r); Assert.Equal(7, n.DeviceType); Assert.Equal(5, n.DeviceNumber); Assert.Equal(-1, n.PartitionNumber); Assert.Empty(n.Problems);
        Assert.Contains("se esperaban 12", Assert.Single(StorageResponse.ParseDeviceNumber(new byte[8]).Problems));
    }

    [Fact]
    public void The_device_descriptor_reads_the_strings_at_their_offsets_and_flags_offsets_outside_the_response()
    {
        var r = new byte[128];
        W32(r, 12, 64); W32(r, 16, 80); W32(r, 20, 100); W32(r, 24, 110); W32(r, 28, 17);
        Text(r, 64, "ACME"); Text(r, 80, "TESTDISK 1TB"); Text(r, 100, "1.0"); Text(r, 110, "SN-1234");
        var d = StorageResponse.ParseDeviceDescriptor(r);
        Assert.Equal("ACME", d.Vendor); Assert.Equal("TESTDISK 1TB", d.Product); Assert.Equal("1.0", d.Revision); Assert.Equal("SN-1234", d.Serial); Assert.Equal(17, d.BusType); Assert.Empty(d.Problems);

        W32(r, 12, 0); W32(r, 24, 5000);   // desplazamiento 0 = el campo no existe (sin problema); uno fuera de la respuesta = problema y sin valor
        var e = StorageResponse.ParseDeviceDescriptor(r);
        Assert.Null(e.Vendor); Assert.Null(e.Serial); Assert.Contains("fuera de la respuesta", Assert.Single(e.Problems));
        Assert.Contains("al menos 36", Assert.Single(StorageResponse.ParseDeviceDescriptor(new byte[20]).Problems));
    }

    [Fact]
    public void The_adapter_descriptor_gives_the_bus_and_the_maximum_transfer()
    {
        var r = new byte[32]; W32(r, 8, 131072); r[24] = 17;
        var a = StorageResponse.ParseAdapterDescriptor(r); Assert.Equal(17, a.BusType); Assert.Equal(131072u, a.MaxTransferBytes); Assert.Empty(a.Problems);
        Assert.Contains("al menos 32", Assert.Single(StorageResponse.ParseAdapterDescriptor(new byte[16]).Problems));
    }

    [Fact]
    public void The_nvme_identify_response_gives_vendor_serial_model_firmware_version_and_lpa_and_rejects_short_ones()
    {
        var r = new byte[48 + 4096]; W32(r, 24, 40); int d = 48;
        W16(r, d, 0x1234); Text(r, d + 4, "SN0001"); Text(r, d + 24, "TEST MODEL"); Text(r, d + 64, "FW1.2"); W32(r, d + 80, 0x00020100); r[d + 261] = 0x0F;
        var id = StorageResponse.ParseNvmeIdentify(r);
        Assert.Equal(0x1234, id.Vid); Assert.Equal("SN0001", id.Serial); Assert.Equal("TEST MODEL", id.Model); Assert.Equal("FW1.2", id.Firmware); Assert.Equal("2.1", id.NvmeVersion); Assert.Equal(0x0F, id.Lpa); Assert.Empty(id.Problems);
        var bad = StorageResponse.ParseNvmeIdentify(new byte[100]); Assert.Equal(-1, bad.Vid); Assert.Contains("se esperaban al menos", Assert.Single(bad.Problems));
    }

    // ── controladores, WQL y métricas
    [Fact]
    public void Driver_paths_from_the_service_registry_resolve_to_real_files()
    {
        const string root = @"C:\Windows\";
        Assert.Null(DriverFiles.ResolveImagePath(null, root)); Assert.Null(DriverFiles.ResolveImagePath("  ", root));
        Assert.Equal(@"C:\Windows\System32\drivers\stornvme.sys", DriverFiles.ResolveImagePath(@"\SystemRoot\System32\drivers\stornvme.sys", root));
        Assert.Equal(@"C:\Windows\System32\drivers\stornvme.sys", DriverFiles.ResolveImagePath(@"System32\drivers\stornvme.sys", root));
        Assert.Equal(@"C:\Windows\System32\drivers\x.sys", DriverFiles.ResolveImagePath(@"\??\C:\Windows\System32\drivers\x.sys", root));
        Assert.Equal(@"C:\Windows\system32\drivers\x.sys", DriverFiles.ResolveImagePath(@"%SystemRoot%\system32\drivers\x.sys", root));
        Assert.Equal(@"C:\Drivers\x.sys", DriverFiles.ResolveImagePath("\"C:\\Drivers\\x.sys\"", root));
    }

    [Fact]
    public void Wql_quoting_escapes_backslashes_and_quotes_in_association_keys()
    {
        Assert.Equal(@"""a\\b\""c""", Wql.Quote(@"a\b""c"));
    }

    [Fact]
    public void The_metric_source_is_the_first_one_that_delivered_a_value_and_zero_counts_as_a_value()
    {
        var m = MetricPick.First(("WMI", (double?)null), ("registro NVMe", 0.0), ("propiedad", 5.0));
        Assert.Equal(0.0, m.Value.GetValueOrDefault()); Assert.NotNull(m.Value); Assert.Equal("registro NVMe", m.Source);
        var none = MetricPick.First(("WMI", (double?)null), ("registro NVMe", (double?)null));
        Assert.Null(none.Value); Assert.Equal("ninguna fuente lo entregó", none.Source);
    }

    // ── identidad: ¿«disco N» es el mismo disco en todas las fuentes?
    [Fact]
    public void Serial_numbers_are_compared_by_letters_and_digits_so_formatting_differences_do_not_matter()
    {
        Assert.Equal("E8238FA6BF53", DiskIdentity.NormalizeSerial("e823_8fa6.bf53 ")); Assert.Equal("", DiskIdentity.NormalizeSerial(null));
        Assert.Equal(SerialMatch.Same, DiskIdentity.CompareSerial("0026_B768_5E0A_2F6F.", "0026B7685E0A2F6F"));
        Assert.Equal(SerialMatch.Same, DiskIdentity.CompareSerial("XXABCD1234YY", "ABCD1234"));
        Assert.Equal(SerialMatch.Different, DiskIdentity.CompareSerial("AAAA1111", "BBBB2222"));
        Assert.Equal(SerialMatch.NotComparable, DiskIdentity.CompareSerial("abc", "abcdef")); Assert.Equal(SerialMatch.NotComparable, DiskIdentity.CompareSerial(null, "ABCD1234"));
    }

    [Fact]
    public void Sizes_agree_within_one_percent_and_a_missing_size_is_not_comparable()
    {
        Assert.True(DiskIdentity.SizesAgree(1000, 1005) == true); Assert.True(DiskIdentity.SizesAgree(1000, 1100) == false);
        Assert.Null(DiskIdentity.SizesAgree(0, 1000)); Assert.Null(DiskIdentity.SizesAgree(1000, 0));
    }

    private static DriveIdentity Id(string source, string? model, string? serial, ulong size) => new(source, model, serial, size);

    [Fact]
    public void A_source_without_an_entry_for_the_number_is_reported_as_absent_without_calling_the_identity_inconsistent()
    {
        // El caso del disco 0 que aparece en MSFT_PhysicalDisk pero no en MSFT_Disk: la ausencia se dice, y las demás fuentes se siguen comparando.
        var f = DiskIdentity.Compare(0, [Id("MSFT_PhysicalDisk", "TEST SSD 1TB", "SER00001", 1_000_000_000_000), Id("Win32_DiskDrive", "TEST SSD 1TB", "SER00001", 1_000_000_000_000)], ["MSFT_Disk (Number)"]);
        Assert.Contains(f, x => x.Text.Contains("AUSENTE") && x.Text.Contains("MSFT_Disk")); Assert.DoesNotContain(f, x => x.Inconsistent);
        Assert.Contains(f, x => x.Text.Contains("el número de serie coincide")); Assert.Contains(f, x => x.Text.Contains("el tamaño coincide"));
    }

    [Fact]
    public void Different_serials_sizes_or_models_under_the_same_number_are_flagged_as_inconsistent_separately()
    {
        var serial = DiskIdentity.Compare(1, [Id("A", "TEST SSD", "AAAA1111", 1000), Id("B", "TEST SSD", "BBBB2222", 1000)], []);
        Assert.Contains(serial, x => x.Inconsistent && x.Text.Contains("números de serie DIFIEREN")); Assert.DoesNotContain(serial, x => x.Text.Contains("tamaño DIFIERE"));

        var size = DiskIdentity.Compare(1, [Id("A", "TEST SSD", "AAAA1111", 1_000_000), Id("B", "TEST SSD", "AAAA1111", 500_000)], []);
        Assert.Contains(size, x => x.Inconsistent && x.Text.Contains("tamaño DIFIERE"));

        var model = DiskIdentity.Compare(1, [Id("A", "MODEL ONE", null, 1000), Id("B", "MODEL TWO", null, 1000)], []);
        Assert.Contains(model, x => x.Inconsistent && x.Text.Contains("modelo DIFIERE"));
    }

    [Fact]
    public void A_source_that_does_not_know_the_size_adds_no_size_finding_and_a_lone_source_has_nothing_to_compare_with()
    {
        var f = DiskIdentity.Compare(2, [Id("A", "TEST SSD", "AAAA1111", 1_000_000), Id("ruta PhysicalDrive2", "TEST SSD", "AAAA1111", 0)], []);
        Assert.DoesNotContain(f, x => x.Text.Contains("tamaño")); Assert.DoesNotContain(f, x => x.Inconsistent);

        var lone = Assert.Single(DiskIdentity.Compare(3, [Id("A", "TEST SSD", "AAAA1111", 1000)], [])); Assert.False(lone.Inconsistent); Assert.Contains("solo una fuente", lone.Text);
    }
}
