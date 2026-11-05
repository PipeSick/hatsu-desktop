using System.Text;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WinMac.Connect.App;

/// <summary>
/// Sondeo DIAGNÓSTICO y de solo lectura de la telefonía (<c>HatsuDesktop.Connect.exe --probe-calls</c>, docs/57). No contesta, no cuelga, no marca, no conecta audio,
/// no registra la app como cliente de la línea (<c>RegisterApp</c>) ni cambia nada del sistema. Solo recoge HECHOS para decidir qué ruta es posible en este PC con este paquete:
/// ¿responde la API de líneas?, ¿hay líneas Bluetooth?, ¿se concede el acceso a <c>PhoneLineTransportDevice</c>?, ¿qué endpoints de audio Hands-Free ve Windows?,
/// ¿qué servicios HFP/PBAP/MAP anuncian los teléfonos emparejados? Sin nombres de dispositivos ni números en la salida.
/// </summary>
internal static class CallProbe
{
    public static async Task<(CallProbeReport Report, string Text)> RunAsync()
    {
        var sb = new StringBuilder(); void L(string s) => sb.AppendLine(s);
        L($"== Hatsu Connect · sondeo de telefonía · {DateTime.Now:yyyy-MM-dd HH:mm} ==");
        L($"Proceso con identidad de paquete: {HasPackageIdentity()}");

        bool pcm = false;
        try { _ = PhoneCallManager.IsCallActive; pcm = true; L("PhoneCallManager: disponible"); } catch (Exception e) { L("PhoneCallManager: no disponible (" + e.GetType().Name + ")"); }

        bool lineApi = false; int btLines = 0; bool granted = false; string? status = null;
        try
        {
            var store = await PhoneCallManager.RequestStoreAsync();
            var def = await store.GetDefaultLineAsync();
            lineApi = true; L($"PhoneCallStore: responde (línea predeterminada: {(def == Guid.Empty ? "ninguna" : "sí")})");
            if (def != Guid.Empty)
            {
                var line = await PhoneLine.FromIdAsync(def);
                L($"  línea predeterminada · transporte={line.Transport} · puede marcar={line.CanDial}");
                if (line.Transport == PhoneLineTransport.Bluetooth) btLines++;
            }
        }
        catch (Exception e) { L("PhoneCallStore/PhoneLine: error " + e.GetType().Name + " 0x" + e.HResult.ToString("X8")); }

        try
        {
            var sel = PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth);
            var devs = await DeviceInformation.FindAllAsync(sel);
            L($"PhoneLineTransportDevice (Bluetooth): {devs.Count} dispositivo(s) listados por Windows");
            foreach (var d in devs.Take(4))
            {
                var t = PhoneLineTransportDevice.FromId(d.Id);
                var acc = await t.RequestAccessAsync();   // solo pide permiso; no registra ni conecta
                status = acc.ToString(); granted |= acc == DeviceAccessStatus.Allowed;
                L($"  RequestAccessAsync → {acc} (registrada: {SafeBool(() => t.IsRegistered())})");
            }
            if (devs.Count == 0) L("  (sin transportes: Windows no lista ninguno para esta app)");
        }
        catch (Exception e) { L("PhoneLineTransportDevice: error " + e.GetType().Name + " 0x" + e.HResult.ToString("X8")); status ??= e.GetType().Name; }

        int hf = 0;
        try
        {
            foreach (var cls in new[] { DeviceClass.AudioRender, DeviceClass.AudioCapture })
            {
                var all = await DeviceInformation.FindAllAsync(cls);
                int n = all.Count(d => d.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Manos libres", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase));
                L($"Endpoints de audio {cls}: {all.Count} (Bluetooth Hands-Free: {n}). Que exista NO prueba que haya audio de llamada.");
                hf += n;
            }
        }
        catch (Exception e) { L("Audio: error " + e.GetType().Name); }

        try
        {
            var phones = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            L($"Teléfonos/dispositivos Bluetooth clásicos emparejados: {phones.Count}");
            int i = 0;
            foreach (var d in phones.Take(8))
            {
                using var bt = await BluetoothDevice.FromIdAsync(d.Id);
                if (bt is null) continue;
                var r = await bt.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
                var ids = r.Services.Select(s => s.ServiceId.Uuid).ToList();
                var caps = PhoneCapabilities.From(ids, r.Error == BluetoothError.Success);
                L($"  #{++i} clase={bt.ClassOfDevice.MajorClass}/{bt.ClassOfDevice.MinorClass} conectado={bt.ConnectionStatus} · HFP-AG={caps.HandsFreeAg} PBAP={caps.Pbap} MAP={caps.Map} A2DP={caps.A2dpSource}");
            }
        }
        catch (Exception e) { L("Bluetooth clásico: error " + e.GetType().Name); }

        L("Audio bidireccional verificado: NO (solo se confirma con una llamada real, a mano: docs/57 §8).");
        var rep = new CallProbeReport(pcm, lineApi, btLines, granted, status, hf, BidirectionalAudioVerified: false);
        L("Botones de llamada que se ofrecerían hoy: " + (CallRoutePlanner.Evaluate(rep, CallControlRoute.PhoneLineTransport, false).AnyButton ? "sí" : "ninguno") + " — " + CallRoutePlanner.Why(rep, CallControlRoute.PhoneLineTransport, false));
        return (rep, sb.ToString());
    }

    private static bool HasPackageIdentity() { try { _ = Windows.ApplicationModel.Package.Current.Id; return true; } catch { return false; } }
    private static string SafeBool(Func<bool> f) { try { return f().ToString(); } catch (Exception e) { return e.GetType().Name; } }
}
