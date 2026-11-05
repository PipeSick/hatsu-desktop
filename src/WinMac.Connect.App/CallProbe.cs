using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WinMac.Connect.App;

/// <summary>
/// Diagnóstico DE SOLO LECTURA de Bluetooth y telefonía (<c>HatsuDesktop.Connect.exe --probe-calls [--out archivo]</c>; lo lanza el botón de Ajustes › Hatsu Connect › Diagnóstico).
/// No contesta, no cuelga, no marca, no conecta ni desconecta Bluetooth, no registra la app como cliente de la línea (<c>RegisterApp</c>), no conecta audio ni cambia nada del sistema.
/// Solo recoge HECHOS de esta instalación (docs/57–58). Sin nombres de dispositivos, contenido, audio, historial ni números; los identificadores se anonimizan (<see cref="DiagRedactor"/>).
/// </summary>
internal static class CallProbe
{
    public static async Task<(CallProbeReport Report, string Text)> RunAsync(string connectLogPath)
    {
        var F = new List<DiagFinding>(); var D = new List<string>(); void L(string s) => D.Add(s);
        string hatsu = typeof(CallProbe).Assembly.GetName().Version?.ToString(3) ?? "?";
        var header = new[] { $"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm}", $"Windows: {Environment.OSVersion.VersionString}", $"Hatsu Desktop (Connect): {hatsu}", $"Identidad de paquete (Store/MSIX): {(HasPackageIdentity() ? "sí" : "no (ejecución sin empaquetar)")}" };

        bool pcm = false;
        try { _ = PhoneCallManager.IsCallActive; pcm = true; F.Add(new("Estado de llamadas del sistema (PhoneCallManager)", DiagState.Available, "responde; llamada activa ahora: " + PhoneCallManager.IsCallActive)); }
        catch (Exception e) { F.Add(new("Estado de llamadas del sistema (PhoneCallManager)", Classify(e), Describe(e))); }

        bool lineApi = false; int btLines = 0;
        try
        {
            var store = await PhoneCallManager.RequestStoreAsync();
            var def = await store.GetDefaultLineAsync();
            lineApi = true;
            if (def == Guid.Empty) F.Add(new("Líneas telefónicas del sistema", DiagState.Detected, "ninguna línea predeterminada (no prueba incompatibilidad)"));
            else
            {
                var line = await PhoneLine.FromIdAsync(def);
                if (line.Transport == PhoneLineTransport.Bluetooth) btLines++;
                F.Add(new("Líneas telefónicas del sistema", DiagState.Detected, $"línea predeterminada con transporte {line.Transport}"));
            }
        }
        catch (Exception e) { F.Add(new("Líneas telefónicas del sistema", Classify(e), Describe(e))); }

        bool granted = false; string? status = null;
        try
        {
            var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
            if (devs.Count == 0) F.Add(new("Transporte telefónico Bluetooth", DiagState.NotChecked, "Windows no lista ninguno para esta app (teléfono desconectado, sin HFP o sin permiso: no es concluyente)"));
            else
            {
                int ok = 0, denied = 0;
                foreach (var d in devs.Take(4))
                {
                    var acc = await PhoneLineTransportDevice.FromId(d.Id).RequestAccessAsync();   // solo pide permiso: no registra ni conecta
                    status = acc.ToString(); if (acc == DeviceAccessStatus.Allowed) { ok++; granted = true; } else denied++;
                    L($"  RequestAccessAsync → {acc}");
                }
                F.Add(new("Transporte telefónico Bluetooth", ok > 0 ? DiagState.Available : DiagState.AccessDenied, $"{devs.Count} listado(s); acceso concedido: {ok}, denegado: {denied}" + (status is null ? "" : $" ({status})")));
            }
        }
        catch (Exception e) { status = e.GetType().Name; F.Add(new("Transporte telefónico Bluetooth", Classify(e), Describe(e))); }

        int hf = 0;
        try
        {
            foreach (var cls in new[] { DeviceClass.AudioRender, DeviceClass.AudioCapture })
            {
                var all = await DeviceInformation.FindAllAsync(cls);
                hf += all.Count(d => d.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Manos libres", StringComparison.OrdinalIgnoreCase));
                L($"  Endpoints {cls}: {all.Count}");
            }
            F.Add(new("Endpoints de audio Hands-Free", hf > 0 ? DiagState.Detected : DiagState.NotChecked, hf > 0 ? $"{hf} visible(s); que existan NO prueba que haya audio de llamada" : "ninguno visible ahora (un teléfono desconectado no los muestra)"));
        }
        catch (Exception e) { F.Add(new("Endpoints de audio Hands-Free", Classify(e), Describe(e))); }

        try
        {
            var phones = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            int i = 0, connected = 0;
            foreach (var d in phones.Take(8))
            {
                BluetoothDevice? bt = null;
                try
                {
                    bt = await BluetoothDevice.FromIdAsync(d.Id);
                    if (bt is null) { L($"  #{++i} no se pudo abrir (desconectado o quitado)"); continue; }
                    bool isPhone = bt.ClassOfDevice.MajorClass == BluetoothMajorClass.Phone;
                    if (bt.ConnectionStatus == BluetoothConnectionStatus.Connected) connected++;
                    var r = await bt.GetRfcommServicesAsync(BluetoothCacheMode.Cached);   // caché: no abre conexiones nuevas
                    var caps = PhoneCapabilities.From(r.Services.Select(s => s.ServiceId.Uuid), r.Error == BluetoothError.Success);
                    L($"  #{++i} {(isPhone ? "teléfono" : "otro dispositivo")} · {bt.ConnectionStatus} · HFP-AG={caps.HandsFreeAg} PBAP={caps.Pbap} MAP={caps.Map} A2DP={caps.A2dpSource} (servicios en caché; id {DiagRedactor.Redact(bt.DeviceId)})");
                }
                catch (Exception e) { L($"  #{++i} error {e.GetType().Name} 0x{e.HResult:X8}"); }
                finally { bt?.Dispose(); }
            }
            F.Add(new("Dispositivos Bluetooth emparejados", phones.Count > 0 ? DiagState.Detected : DiagState.NotChecked, $"{phones.Count} emparejado(s), {connected} conectado(s) ahora; servicios de cada uno en el detalle"));
        }
        catch (Exception e) { F.Add(new("Dispositivos Bluetooth emparejados", Classify(e), Describe(e))); }

        F.Add(new("Observador de llamadas del sistema", DiagState.NotChecked, "se activa a mano desde Ajustes (temporal, solo lectura); líneas recientes del registro abajo"));
        F.Add(new("Contestar / rechazar / colgar desde Hatsu", DiagState.NotSupported, "sin mecanismo con audio del PC verificado (docs/57)"));
        F.Add(new("Audio bidireccional PC ↔ teléfono", DiagState.NotChecked, "solo se confirma con una llamada real, a mano"));

        D.Add(""); D.Add("Registro reciente del observador (sin contenido):");
        try { if (File.Exists(connectLogPath)) foreach (var l in File.ReadLines(connectLogPath).Where(l => l.Contains("telefonía del sistema") || l.Contains("llamada")).TakeLast(30)) D.Add("  " + l); else D.Add("  (sin registro todavía)"); }
        catch (Exception e) { D.Add("  (no legible: " + e.GetType().Name + ")"); }

        var rep = new CallProbeReport(pcm, lineApi, btLines, granted, status, hf, BidirectionalAudioVerified: false);
        var text = DiagReport.Compose("Hatsu Connect · diagnóstico de Bluetooth y llamadas", header, F, D);
        return (rep, DiagRedactor.Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName));
    }

    private static DiagState Classify(Exception e) => e is UnauthorizedAccessException || e.HResult == unchecked((int)0x80070005) ? DiagState.AccessDenied : e is PlatformNotSupportedException or NotImplementedException ? DiagState.NotSupported : DiagState.Error;
    private static string Describe(Exception e) => $"{e.GetType().Name} 0x{e.HResult:X8}";
    private static bool HasPackageIdentity() { try { _ = Windows.ApplicationModel.Package.Current.Id; return true; } catch { return false; } }
}
