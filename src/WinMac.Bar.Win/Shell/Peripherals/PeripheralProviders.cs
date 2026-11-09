using System.Runtime.InteropServices;
using WinMac.Bar.Peripherals;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace WinMac.Bar.Win.Shell.Peripherals;

/// <summary>
/// Bluetooth ESTÁNDAR, solo lectura: dispositivos emparejados (clásicos y BLE), con su estado de conexión real y la batería por (1) el servicio estándar Battery Service (0x180F / 0x2A19) de GATT
/// y (2) la propiedad de batería que Windows publica para dispositivos Bluetooth conectados (DEVPKEY de bateria, no documentada: por eso el soporte es EXPERIMENTAL).
/// No se conecta a dispositivos que no lo estén ya (no los despierta), no escribe nada y no pide permisos elevados.
/// </summary>
internal sealed class BluetoothPeripheralProvider : IPeripheralProvider
{
    public string Id => "bt";
    public string DisplayName => "Bluetooth estándar";
    public SupportLevel Support => SupportLevel.Experimental;

    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";   // propiedad de nivel de batería del dispositivo Bluetooth (no documentada)
    private static readonly string[] Props = ["System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress", BatteryKey];

    public async Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct)
    {
        var res = new List<RawReading>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // clásicos
        foreach (var di in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), Props).AsTask(ct))
        {
            ct.ThrowIfCancellationRequested();
            string addr = AddressOf(di); if (!seen.Add(addr)) continue;
            bool connected = di.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
            int? pct = di.Properties.TryGetValue(BatteryKey, out var b) ? ByteOf(b) : null;
            var (kind, conn) = await ClassifyAsync(di.Id, di.Name, ct);
            res.Add(new RawReading(addr, di.Name, kind, conn, connected, connected ? pct : null, ChargeState.Unknown, pct is null ? BatteryCapability.Unknown : BatteryCapability.Supported));
        }
        // BLE: el servicio de batería estándar, solo si el dispositivo ya está conectado
        foreach (var di in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), Props).AsTask(ct))
        {
            ct.ThrowIfCancellationRequested();
            string addr = AddressOf(di); bool connected = di.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
            int? pct = di.Properties.TryGetValue(BatteryKey, out var b) ? ByteOf(b) : null;
            var cap = BatteryCapability.Unknown;
            if (connected && pct is null) (pct, cap) = await GattBatteryAsync(di.Id, ct);
            else if (pct is not null) cap = BatteryCapability.Supported;
            var prev = res.FindIndex(r => r.DeviceId.Equals(addr, StringComparison.OrdinalIgnoreCase));
            if (prev >= 0) { var o = res[prev]; if (o.Percent is null && pct is not null) res[prev] = o with { Percent = connected ? pct : null, Capability = cap }; continue; }
            if (!seen.Add(addr)) continue;
            res.Add(new RawReading(addr, di.Name, GuessKind(di.Name), ConnectionKind.Bluetooth, connected, connected ? pct : null, ChargeState.Unknown, cap));
        }

        return res;
    }

    private static string AddressOf(DeviceInformation di) => di.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) && a is string s && s.Length > 0 ? s.ToUpperInvariant() : di.Id;

    private static int? ByteOf(object? v) => v switch { byte b => b, ushort u => u, int i => i, uint ui => (int)Math.Min(ui, int.MaxValue), _ => null };

    private static async Task<(int? Percent, BatteryCapability Cap)> GattBatteryAsync(string id, CancellationToken ct)
    {
        try
        {
            using var dev = await BluetoothLEDevice.FromIdAsync(id).AsTask(ct);
            if (dev is null || dev.ConnectionStatus != BluetoothConnectionStatus.Connected) return (null, BatteryCapability.Unknown);
            var svc = await dev.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached).AsTask(ct);
            if (svc.Status != GattCommunicationStatus.Success) return (null, BatteryCapability.Unknown);
            if (svc.Services.Count == 0) return (null, BatteryCapability.Unsupported);   // el dispositivo no publica el servicio estándar
            using var s = svc.Services[0];
            var ch = await s.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached).AsTask(ct);
            if (ch.Status != GattCommunicationStatus.Success || ch.Characteristics.Count == 0) return (null, BatteryCapability.Unknown);
            var val = await ch.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct);
            if (val.Status != GattCommunicationStatus.Success || val.Value.Length < 1) return (null, BatteryCapability.Unknown);
            var data = new byte[val.Value.Length]; Windows.Storage.Streams.DataReader.FromBuffer(val.Value).ReadBytes(data);
            return (PeripheralStore.ValidPercent(data[0]), BatteryCapability.Supported);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return (null, BatteryCapability.Unknown); }   // permiso/ocupado: se queda en «desconocido», nunca se inventa
    }

    private static async Task<(DeviceKind, ConnectionKind)> ClassifyAsync(string id, string name, CancellationToken ct)
    {
        try
        {
            using var dev = await BluetoothDevice.FromIdAsync(id).AsTask(ct);
            if (dev is not null)
            {
                var cod = dev.ClassOfDevice;
                var k = cod.MajorClass switch
                {
                    BluetoothMajorClass.AudioVideo => DeviceKind.Headset,
                    BluetoothMajorClass.Peripheral => ((int)cod.MinorClass & 0x10) != 0 ? DeviceKind.Keyboard : ((int)cod.MinorClass & 0x20) != 0 ? DeviceKind.Mouse : GuessKind(name),
                    _ => GuessKind(name),
                };
                return (k, ConnectionKind.Bluetooth);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
        return (GuessKind(name), ConnectionKind.Bluetooth);
    }

    internal static DeviceKind GuessKind(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("ratón") || n.Contains("raton") || n.Contains("keris") || n.Contains("mx master")) return DeviceKind.Mouse;
        if (n.Contains("keyboard") || n.Contains("teclado") || n.Contains("scope") || n.Contains("k380")) return DeviceKind.Keyboard;
        if (n.Contains("headset") || n.Contains("headphone") || n.Contains("auricular") || n.Contains("buds") || n.Contains("airpods") || n.Contains("kitty") || n.Contains("delta")) return DeviceKind.Headset;
        if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("mando")) return DeviceKind.Gamepad;
        return DeviceKind.Other;
    }
}

/// <summary>
/// Baterías HID/USB que Windows expone como dispositivos de la clase Batería (Battery System de HID) por las IOCTL de batería, de solo lectura. Es el mismo canal con el que Windows
/// distingue un SAI/UPS de la batería del equipo: los SAI (por nombre) se EXCLUYEN aquí para no confundirlos con periféricos; cualquier otro dispositivo HID con batería se lista como
/// «Dispositivo HID» con una nota. La mayoría de ratones/teclados inalámbricos NO usan este canal: no aparecerán, y no pasa nada (no se fabrica nada).
/// </summary>
internal sealed class HidBatteryProvider : IPeripheralProvider
{
    public string Id => "hid";
    public string DisplayName => "HID / USB (dispositivos de batería de Windows)";
    public SupportLevel Support => SupportLevel.Experimental;

    private static readonly string[] UpsWords = ["ups", "back-ups", "apc", "cyberpower", "eaton", "tripp", "smart-ups", "sai "];

    public Task<IReadOnlyList<RawReading>> ReadAsync(CancellationToken ct) => Task.Run<IReadOnlyList<RawReading>>(() =>
    {
        var res = new List<RawReading>();
        foreach (var path in BatteryDetailsProvider.DevicePaths() ?? [])
        {
            ct.ThrowIfCancellationRequested();
            if (BatteryDeviceClass.Classify(path) != BatteryDeviceKind.Ups) continue;   // la batería del equipo no es un periférico
            var r = BatteryIoctl.Read(path); if (r is null) continue;
            string name = r.Value.Name ?? "Dispositivo HID con batería";
            if (UpsWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;   // SAI/UPS: fuera
            res.Add(new RawReading(path, name, BluetoothPeripheralProvider.GuessKind(name), path.Contains("usb#", StringComparison.OrdinalIgnoreCase) ? ConnectionKind.Usb : ConnectionKind.Unknown, true,
                r.Value.Percent, r.Value.Charging ? ChargeState.Charging : r.Value.Discharging ? ChargeState.NotCharging : ChargeState.Unknown, r.Value.Percent is null ? BatteryCapability.Unknown : BatteryCapability.Supported,
                "Dispositivo HID con batería que Windows expone; si es un SAI, ignóralo."));
        }

        return res;
    }, ct);
}

internal static class BatteryIoctl
{
    [StructLayout(LayoutKind.Sequential)] private struct QUERY { public uint BatteryTag; public int InformationLevel; public int AtRate; }
    [StructLayout(LayoutKind.Sequential)] private struct WAIT { public uint BatteryTag, Timeout, PowerState, LowCapacity, HighCapacity; }
    [StructLayout(LayoutKind.Sequential)] private struct STATUS { public uint PowerState, Capacity, Voltage; public int Rate; }
    [StructLayout(LayoutKind.Sequential)] private struct INFO { public uint Capabilities; public byte Technology, R1, R2, R3; public uint Chemistry, DesignedCapacity, FullChargedCapacity, DefaultAlert1, DefaultAlert2, CriticalBias, CycleCount; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref uint inBuf, int inSize, out uint outBuf, int outSize, out int ret, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref QUERY inBuf, int inSize, out INFO outBuf, int outSize, out int ret, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref WAIT inBuf, int inSize, out STATUS outBuf, int outSize, out int ret, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, ref QUERY inBuf, int inSize, [Out] char[] outBuf, int outSize, out int ret, IntPtr ov);

    public static (int? Percent, bool Charging, bool Discharging, string? Name)? Read(string path)
    {
        using var h = CreateFileW(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (h.IsInvalid) return null;
        uint wait = 0;
        if (!DeviceIoControl(h, 0x294040 /* IOCTL_BATTERY_QUERY_TAG */, ref wait, 4, out uint tag, 4, out _, IntPtr.Zero) || tag == 0) return null;
        var q = new QUERY { BatteryTag = tag, InformationLevel = 0 };
        if (!DeviceIoControl(h, 0x294044 /* IOCTL_BATTERY_QUERY_INFORMATION */, ref q, Marshal.SizeOf<QUERY>(), out INFO info, Marshal.SizeOf<INFO>(), out _, IntPtr.Zero)) return null;
        var w = new WAIT { BatteryTag = tag };
        if (!DeviceIoControl(h, 0x29404C /* IOCTL_BATTERY_QUERY_STATUS */, ref w, Marshal.SizeOf<WAIT>(), out STATUS st, Marshal.SizeOf<STATUS>(), out _, IntPtr.Zero)) return null;
        int? pct = info.FullChargedCapacity > 0 && st.Capacity != 0xFFFFFFFF && st.Capacity <= info.FullChargedCapacity * 1.02 ? PeripheralStore.ValidPercent((int)Math.Round(100.0 * st.Capacity / info.FullChargedCapacity)) : null;
        string? name = null;
        try
        {
            var qn = new QUERY { BatteryTag = tag, InformationLevel = 4 /* BatteryDeviceName */ }; var buf = new char[64];
            if (DeviceIoControl(h, 0x294044, ref qn, Marshal.SizeOf<QUERY>(), buf, buf.Length * 2, out int got, IntPtr.Zero) && got > 2) name = new string(buf, 0, got / 2).TrimEnd('\0').Trim();
        }
        catch (Exception) { }
        return (pct, (st.PowerState & 0x4) != 0, (st.PowerState & 0x2) != 0, string.IsNullOrWhiteSpace(name) ? null : name);
    }
}
