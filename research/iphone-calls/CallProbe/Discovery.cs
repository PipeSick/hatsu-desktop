using System.Diagnostics;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation.Metadata;
using Windows.Media.Devices;
using Microsoft.Win32;

namespace CallProbe;

/// <summary>ETAPA 1 — Descubrimiento. Solo lee: no empareja, no registra, no abre canales, no toca llamadas.</summary>
internal static class Discovery
{
    private static readonly Dictionary<Guid, string> Uuids = new()
    {
        [Guid.Parse("0000111e-0000-1000-8000-00805f9b34fb")] = "HFP Hands-Free (HF)", [Guid.Parse("0000111f-0000-1000-8000-00805f9b34fb")] = "HFP Audio Gateway (AG)",
        [Guid.Parse("00001108-0000-1000-8000-00805f9b34fb")] = "HSP Headset", [Guid.Parse("00001112-0000-1000-8000-00805f9b34fb")] = "HSP Audio Gateway",
        [Guid.Parse("00001132-0000-1000-8000-00805f9b34fb")] = "MAP Message Access Server", [Guid.Parse("00001133-0000-1000-8000-00805f9b34fb")] = "MAP Message Notification Server",
        [Guid.Parse("0000112f-0000-1000-8000-00805f9b34fb")] = "PBAP Phonebook Server", [Guid.Parse("00001130-0000-1000-8000-00805f9b34fb")] = "PBAP Phonebook Client",
    };

    public static async Task RunAsync(string? nameFilter)
    {
        Report.Title("ETAPA 1 — Descubrimiento (solo lectura)");
        Report.Item("Windows", Native.WindowsBuild());
        Report.Item("Identidad de paquete", Native.HasPackageIdentity() ? "SÍ — " + Native.PackageFullName() : "NO (proceso sin paquete)");
        Report.Item("Tipo de firma del paquete", Native.SignatureKind() ?? "(sin paquete)");
        Report.Item("Contrato CallsPhoneContract v5 (transporte, Windows 10 1903+)", ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsPhoneContract", 5));
        Report.Item("Contrato CallsPhoneContract v6 (PhoneCall: contestar, colgar, audio)", ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsPhoneContract", 6));
        Report.Item("Tipo PhoneCall presente", ApiInformation.IsTypePresent("Windows.ApplicationModel.Calls.PhoneCall"));
        Report.Item("Phone Link en ejecución (puede disputar el transporte)", string.Join(", ", new[] { "PhoneExperienceHost", "YourPhone", "PhoneLink" }.Where(n => Process.GetProcessesByName(n).Length > 0).DefaultIfEmpty("no")));

        await AdapterAsync(); await PairedAsync(nameFilter); await TransportsAsync(); await AudioAsync(); RegistryAndServices();
    }

    private static async Task AdapterAsync()
    {
        Report.Title("Adaptador Bluetooth");
        try
        {
            var a = await BluetoothAdapter.GetDefaultAsync();
            if (a is null) { Report.Line("  No hay adaptador Bluetooth utilizable por la API de Windows."); return; }
            Report.Item("Classic (BR/EDR)", a.IsClassicSupported); Report.Item("Low Energy", a.IsLowEnergySupported); Report.Item("Rol central", a.IsCentralRoleSupported); Report.Item("Rol periférico", a.IsPeripheralRoleSupported);
            Report.Item("LE Secure Connections", a.AreLowEnergySecureConnectionsSupported); Report.Item("Classic Secure Connections", a.AreClassicSecureConnectionsSupported);
            foreach (var r in (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.Bluetooth)) Report.Item("Radio " + r.Name, r.State);
        }
        catch (Exception e) { Report.Line("  Error: " + e.GetType().Name + ": " + e.Message); }
    }

    private static async Task PairedAsync(string? filter)
    {
        Report.Title("Dispositivos Bluetooth emparejados y sus servicios RFCOMM (SDP)");
        try
        {
            foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
            {
                if (filter is not null && !info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                using var dev = await BluetoothDevice.FromIdAsync(info.Id); if (dev is null) continue;
                Report.Line($"  · {Report.Mask(dev.Name)}  dirección {Report.Addr(dev.BluetoothAddress)}  conexión {dev.ConnectionStatus}  cifrado/autenticado {info.Pairing.ProtectionLevel}");
                var svcs = await dev.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
                if (svcs.Error != BluetoothError.Success) { Report.Line("      SDP: " + svcs.Error); continue; }
                foreach (var s in svcs.Services) { Report.Line($"      servicio {(Uuids.TryGetValue(s.ServiceId.Uuid, out var n) ? n : s.ServiceId.Uuid.ToString())}"); s.Dispose(); }
            }
        }
        catch (Exception e) { Report.Line("  Error: " + e.GetType().Name + ": " + e.Message); }
    }

    private static async Task TransportsAsync()
    {
        Report.Title("Transportes de telefonía de Windows (PhoneLineTransportDevice)");
        try
        {
            const string addr = "System.DeviceInterface.Bluetooth.DeviceAddress";
            var all = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(), [addr]);
            Report.Item("Transportes encontrados (todos)", all.Count);
            var bt = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth), [addr]);
            Report.Item("Transportes Bluetooth", bt.Count);
            foreach (var d in bt) Report.Line($"  · «{Report.Mask(d.Name)}»  id …{d.Id[^Math.Min(10, d.Id.Length)..]}  dirección {d.Properties.GetValueOrDefault(addr)}");
            Report.Line("  (Encontrarlos no requiere paquete: según Klangbruecke FINDINGS §2 enumerar y FromId funcionan sin identidad. El permiso se pide en la etapa 2.)");
        }
        catch (Exception e) { Report.Line("  Error: " + e.GetType().Name + ": " + e.Message); }
        try
        {
            var store = await PhoneCallManager.RequestStoreAsync(); Report.Item("PhoneCallManager.RequestStoreAsync", "OK");
            var done = new TaskCompletionSource(); var lines = new List<Guid>(); var w = store.RequestLineWatcher();
            w.LineAdded += (_, a) => lines.Add(a.LineId); w.EnumerationCompleted += (_, _) => done.TrySetResult(); w.Stopped += (_, _) => done.TrySetResult(); w.Start();
            await Task.WhenAny(done.Task, Task.Delay(5000)); w.Stop();
            foreach (var id in lines) { var l = await PhoneLine.FromIdAsync(id); if (l is not null) Report.Line($"  · línea «{Report.Mask(l.DisplayName)}» transporte {l.Transport}, CanDial={l.CanDial}, red {l.NetworkState}"); }
            if (lines.Count == 0) Report.Line("  Sin líneas (normal si ningún transporte está registrado y conectado).");
        }
        catch (Exception e) { Report.Item("PhoneCallManager.RequestStoreAsync", e.GetType().Name + ": " + e.Message); }
    }

    private static async Task AudioAsync()
    {
        Report.Title("Audio de Windows: dispositivos predeterminados y puntos «Hands-Free»");
        foreach (var (label, role) in new[] { ("Reproducción (comunicaciones)", AudioDeviceRole.Communications), ("Reproducción (predeterminado)", AudioDeviceRole.Default) })
            await DescribeAsync(label, MediaDevice.GetDefaultAudioRenderId(role));
        foreach (var (label, role) in new[] { ("Captura (comunicaciones)", AudioDeviceRole.Communications), ("Captura (predeterminado)", AudioDeviceRole.Default) })
            await DescribeAsync(label, MediaDevice.GetDefaultAudioCaptureId(role));
        foreach (var cls in new[] { DeviceClass.AudioRender, DeviceClass.AudioCapture })
        {
            try { foreach (var d in await DeviceInformation.FindAllAsync(cls)) if (d.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Headset", StringComparison.OrdinalIgnoreCase)) Report.Line($"  · {cls}: {Report.Mask(d.Name)} (habilitado={d.IsEnabled})"); }
            catch (Exception e) { Report.Line("  Error: " + e.GetType().Name); }
        }
    }

    private static async Task DescribeAsync(string label, string id)
    {
        try { var d = string.IsNullOrEmpty(id) ? null : await DeviceInformation.CreateFromIdAsync(id); Report.Item(label, d is null ? "(ninguno)" : Report.Mask(d.Name)); }
        catch (Exception e) { Report.Item(label, e.GetType().Name); }
    }

    private static void RegistryAndServices()
    {
        Report.Title("Registro y servicios (solo lectura)");
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Bluetooth\Audio\Hfp\HandsFree");
            if (k is null) Report.Line("  Clave HFP HandsFree: no existe."); else foreach (var n in k.GetValueNames()) Report.Item("HandsFree\\" + n, k.GetValue(n));
        }
        catch (Exception e) { Report.Line("  Clave HFP: " + e.GetType().Name); }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try { using var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\phoneCall"); Report.Item((hive == Registry.CurrentUser ? "HKCU" : "HKLM") + " consentimiento «Llamadas telefónicas»", k?.GetValue("Value") ?? "(sin valor)"); }
            catch (Exception e) { Report.Line("  Consentimiento: " + e.GetType().Name); }
        }

        foreach (var svc in new[] { "BTAGService", "bthserv" })
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("sc.exe", $"query {svc}") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!; string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(4000);
                Report.Item("Servicio " + svc, o.Contains("RUNNING") ? "en ejecución" : o.Contains("STOPPED") ? "detenido" : "estado desconocido");
            }
            catch (Exception e) { Report.Item("Servicio " + svc, e.GetType().Name); }
        }
    }
}
