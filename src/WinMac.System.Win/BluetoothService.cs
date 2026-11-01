using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Bluetooth por WinRT público: estado y conmutación de la radio (<c>Windows.Devices.Radios</c>) y lista de dispositivos emparejados con su estado de conexión.
/// Windows no ofrece API pública para conectar o desconectar un dispositivo concreto: esa acción se deja a Configuración (el panel lo indica).
/// </summary>
public sealed class BluetoothService : IBluetoothService
{
    public BluetoothState Read()
    {
        try
        {
            var radios = RadioHelper.Of(RadioKind.Bluetooth);
            if (radios.Count == 0) return BluetoothState.None("No hay adaptador Bluetooth");
            bool on = radios.Any(r => r.State == RadioState.On);
            return new BluetoothState(true, on, on ? Devices() : []);
        }
        catch (UnauthorizedAccessException e) { return BluetoothState.None(e.Message); }
        catch (Exception e) { return BluetoothState.None("Bluetooth: " + e.GetType().Name); }
    }

    private static IReadOnlyList<BluetoothDeviceInfo> Devices()
    {
        var list = new List<BluetoothDeviceInfo>();
        try
        {
            var found = RadioHelper.Await(DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)));
            foreach (var d in found.Take(20))
            {
                bool connected = false;
                try { using var dev = RadioHelper.Await(BluetoothDevice.FromIdAsync(d.Id), 4000); connected = dev?.ConnectionStatus == BluetoothConnectionStatus.Connected; }
                catch (Exception) { /* sin estado: se muestra como no conectado */ }
                list.Add(new BluetoothDeviceInfo(d.Id, string.IsNullOrWhiteSpace(d.Name) ? "Dispositivo Bluetooth" : d.Name, connected));
            }
        }
        catch (Exception) { /* la lista es opcional: la radio sigue controlable */ }
        return list.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
    }

    public ServiceResult SetRadio(bool on) => RadioHelper.SetState(RadioKind.Bluetooth, on, "Bluetooth");
}
