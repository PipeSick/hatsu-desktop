using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation.Metadata;
using Windows.Media.Devices;
using Microsoft.Win32;
using System.Diagnostics;

namespace CallProbe;

/// <summary>ETAPA 1 — Descubrimiento. Solo lee: no empareja, no registra, no abre canales, no toca llamadas. Cada llamada de Windows lleva tiempo máximo (la etapa 1 se colgó una vez sin él).</summary>
internal static class Discovery
{
    private static readonly Dictionary<Guid, string> Uuids = new()
    {
        [Guid.Parse("0000111e-0000-1000-8000-00805f9b34fb")] = "HFP Hands-Free (HF)", [Guid.Parse("0000111f-0000-1000-8000-00805f9b34fb")] = "HFP Audio Gateway (AG)",
        [Guid.Parse("00001108-0000-1000-8000-00805f9b34fb")] = "HSP Headset", [Guid.Parse("00001112-0000-1000-8000-00805f9b34fb")] = "HSP Audio Gateway",
        [Guid.Parse("00001132-0000-1000-8000-00805f9b34fb")] = "MAP Message Access Server", [Guid.Parse("00001133-0000-1000-8000-00805f9b34fb")] = "MAP Message Notification Server",
        [Guid.Parse("0000112f-0000-1000-8000-00805f9b34fb")] = "PBAP Phonebook Server", [Guid.Parse("00001130-0000-1000-8000-00805f9b34fb")] = "PBAP Phonebook Client",
    };

    public static async Task RunAsync(string? nameFilter, bool lines)
    {
        Report.Title("ETAPA 1 — Descubrimiento (solo lectura)");
        Report.Item("Windows", Native.WindowsBuild());
        Report.Item("Identidad de paquete", Native.HasPackageIdentity() ? "SÍ — " + Native.PackageFullName() : "NO (proceso sin paquete)");
        Report.Item("Tipo de firma del paquete", Native.SignatureKind() ?? "(sin paquete)");
        Report.Item("Contrato CallsPhoneContract v5 (transporte, Windows 10 1903+)", ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsPhoneContract", 5));
        Report.Item("Contrato CallsPhoneContract v6 (PhoneCall: contestar, colgar, audio)", ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsPhoneContract", 6));
        Report.Item("Tipo PhoneCall presente", ApiInformation.IsTypePresent("Windows.ApplicationModel.Calls.PhoneCall"));
        Report.Item("Phone Link en ejecución (puede disputar el transporte)", Native.PhoneLinkRunning() ? "SÍ" : "no");

        await AdapterAsync(); await PairedAsync(nameFilter); await TransportsAsync(lines); await AudioAsync(); RegistryAndServices();
    }

    private static async Task AdapterAsync()
    {
        Report.Title("Adaptador Bluetooth");
        var a = (await Steps.Run("BluetoothAdapter.GetDefaultAsync", async () => await BluetoothAdapter.GetDefaultAsync())).Value;
        if (a is null) { Report.Line("  No hay adaptador Bluetooth utilizable por la API de Windows."); return; }
        Report.Item("Classic (BR/EDR)", a.IsClassicSupported); Report.Item("Low Energy", a.IsLowEnergySupported); Report.Item("Rol central", a.IsCentralRoleSupported); Report.Item("Rol periférico", a.IsPeripheralRoleSupported);
        Report.Item("LE Secure Connections", a.AreLowEnergySecureConnectionsSupported); Report.Item("Classic Secure Connections", a.AreClassicSecureConnectionsSupported);
        var radios = (await Steps.Run("Radio.GetRadiosAsync", async () => (await Radio.GetRadiosAsync()).ToList())).Value ?? [];
        foreach (var r in radios.Where(r => r.Kind == RadioKind.Bluetooth)) Report.Item("Radio Bluetooth", r.State);
    }

    private static async Task PairedAsync(string? filter)
    {
        Report.Title("Dispositivos Bluetooth emparejados y sus servicios RFCOMM (SDP)");
        var infos = (await Steps.Run("DeviceInformation.FindAllAsync(emparejados)", async () => (await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true))).ToList())).Value ?? [];
        foreach (var info in infos)
        {
            if (filter is not null && !info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var dev = (await Steps.Run("BluetoothDevice.FromIdAsync", async () => await BluetoothDevice.FromIdAsync(info.Id))).Value; if (dev is null) continue;
            Report.Line($"  · {Report.Mask(dev.Name)}  dirección {Report.Addr(dev.BluetoothAddress)}  conexión {dev.ConnectionStatus}");   // (el «nivel de protección» de DeviceInformation.Pairing es el solicitado, no el real: se omite para no confundir)
            var svcs = (await Steps.Run("GetRfcommServicesAsync(sin caché)", async () => await dev.GetRfcommServicesAsync(BluetoothCacheMode.Uncached))).Value;
            if (svcs is null) continue;
            if (svcs.Error != BluetoothError.Success) { Report.Line("      SDP: " + svcs.Error); continue; }
            foreach (var s in svcs.Services) { Report.Line($"      servicio {(Uuids.TryGetValue(s.ServiceId.Uuid, out var n) ? n : s.ServiceId.Uuid.ToString())}"); s.Dispose(); }
            dev.Dispose();
        }
    }

    private static async Task TransportsAsync(bool lines)
    {
        Report.Title("Transportes de telefonía de Windows (PhoneLineTransportDevice)");
        const string addr = "System.DeviceInterface.Bluetooth.DeviceAddress";
        var all = (await Steps.Run("FindAllAsync(transportes, todos)", async () => (await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(), [addr])).Count)).Value; Report.Item("Transportes encontrados (todos)", all);
        var bt = (await Steps.Run("FindAllAsync(transportes Bluetooth)", async () => (await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth), [addr])).ToList())).Value ?? [];
        Report.Item("Transportes Bluetooth", bt.Count);
        foreach (var d in bt) Report.Line($"  · «{Report.Mask(d.Name)}»  id {Report.Id(d.Id)}  dirección {Report.AddrText(d.Properties.GetValueOrDefault(addr))}");
        Report.Line("  (Encontrarlos no requiere paquete: según Klangbruecke FINDINGS §2 enumerar y FromId funcionan sin identidad. El permiso se pide en la etapa 2.)");

        var store = await Steps.Run("PhoneCallManager.RequestStoreAsync", async () => await PhoneCallManager.RequestStoreAsync()); 
        Report.Item("PhoneCallManager.RequestStoreAsync", store.Ok ? "OK" : store.Status.ToString());
        if (!lines) { Report.Line("  Las líneas telefónicas NO se enumeran por defecto: solo existen cuando el transporte está registrado, y el vigilante de líneas (PhoneLineWatcher) fue lo que dejó colgada la etapa 1 (ver informe). Usa --lines para probarlo con tiempo máximo."); return; }
        if (!store.Ok || store.Value is null) return;
        var found = new List<Guid>(); var done = new TaskCompletionSource();
        var w = (await Steps.Run("PhoneCallStore.RequestLineWatcher", () => Task.FromResult(store.Value.RequestLineWatcher()), 8)).Value; if (w is null) return;
        w.LineAdded += (_, a) => found.Add(a.LineId); w.EnumerationCompleted += (_, _) => done.TrySetResult(); w.Stopped += (_, _) => done.TrySetResult();
        await Steps.Run("PhoneLineWatcher.Start", () => w.Start(), 8);
        await Task.WhenAny(done.Task, Task.Delay(4000));
        _ = Steps.Run("PhoneLineWatcher.Stop (sin esperar)", () => w.Stop(), 8);   // Stop() puede bloquearse si la enumeración no terminó: no se espera
        foreach (var id in found.ToList()) { var l = (await Steps.Run("PhoneLine.FromIdAsync", async () => await PhoneLine.FromIdAsync(id), 8)).Value; if (l is not null) Report.Line($"  · línea «{Report.Mask(l.DisplayName)}» transporte {l.Transport}, CanDial={l.CanDial}, red {l.NetworkState}"); }
        if (found.Count == 0) Report.Line("  Sin líneas (normal si ningún transporte está registrado y conectado).");
    }

    private static async Task AudioAsync()
    {
        Report.Title("Audio de Windows: dispositivos predeterminados y puntos «Hands-Free»");
        foreach (var (label, role) in new[] { ("Reproducción (comunicaciones)", AudioDeviceRole.Communications), ("Reproducción (predeterminado)", AudioDeviceRole.Default) }) await DescribeAsync(label, MediaDevice.GetDefaultAudioRenderId(role));
        foreach (var (label, role) in new[] { ("Captura (comunicaciones)", AudioDeviceRole.Communications), ("Captura (predeterminado)", AudioDeviceRole.Default) }) await DescribeAsync(label, MediaDevice.GetDefaultAudioCaptureId(role));
        foreach (var cls in new[] { DeviceClass.AudioRender, DeviceClass.AudioCapture })
        {
            var list = (await Steps.Run($"FindAllAsync({cls})", async () => (await DeviceInformation.FindAllAsync(cls)).ToList())).Value ?? [];
            foreach (var d in list.Where(d => d.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Headset", StringComparison.OrdinalIgnoreCase))) Report.Line($"  · {cls}: {Report.Mask(d.Name)} (habilitado={d.IsEnabled})");
        }
    }

    private static async Task DescribeAsync(string label, string id)
    {
        var d = string.IsNullOrEmpty(id) ? null : (await Steps.Run("CreateFromIdAsync", async () => await DeviceInformation.CreateFromIdAsync(id), 8)).Value;
        Report.Item(label, d is null ? "(ninguno)" : Report.Mask(d.Name));
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
