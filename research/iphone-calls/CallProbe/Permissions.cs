using CallProbe.Logic;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Calls;
using Windows.Devices.Enumeration;

namespace CallProbe;

/// <summary>ETAPA 2 — Permisos. RequestAccessAsync solo CONSULTA (no registra nada). Se ejecuta SIN paquete y CON paquete para comparar: ese contraste es la prueba que faltaba.</summary>
internal static class Permissions
{
    public const string FeatureId = "com.microsoft.windows.applicationmodel.phonelinetransportdevice_v1";

    public static async Task RunAsync(string? nameFilter)
    {
        Report.Title("ETAPA 2 — Permisos (consulta, sin registrar)");
        bool packaged = Native.HasPackageIdentity();
        Report.Item("Identidad de paquete", packaged ? "SÍ — " + Native.PackageFullName() : "NO");
        string? sig = Native.SignatureKind(); Report.Item("Tipo de firma del paquete", sig ?? "(sin paquete)");
        Report.Item("Windows", Native.WindowsBuild());
        try
        {   // Se pasa un token vacío a propósito: solo se LEE el estado de la función; no puede desbloquear nada.
            var r = LimitedAccessFeatures.TryUnlockFeature(FeatureId, "", ""); Report.Item("Estado de la función de acceso limitado " + FeatureId.Split('.').Last(), r.Status);
        }
        catch (Exception e) { Report.Item("Función de acceso limitado", e.GetType().Name + ": " + e.Message); }

        var devs = await DeviceInformation.FindAllAsync(PhoneLineTransportDevice.GetDeviceSelector(PhoneLineTransport.Bluetooth));
        if (devs.Count == 0) { Report.Line("  Ningún transporte Bluetooth de telefonía. ¿El iPhone está emparejado y conectado como teléfono (HFP)?"); return; }
        foreach (var d in devs)
        {
            if (nameFilter is not null && !d.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) continue;
            Report.Line($"  · «{Report.Mask(d.Name)}»");
            try
            {
                var t = PhoneLineTransportDevice.FromId(d.Id); if (t is null) { Report.Line("      FromId devolvió null"); continue; }
                Report.Item("    IsRegistered() antes", t.IsRegistered());
                var status = await t.RequestAccessAsync(); Report.Item("    RequestAccessAsync()", status);
                try { Report.Item("    AudioRoutingStatus", t.AudioRoutingStatus); } catch (Exception e) { Report.Item("    AudioRoutingStatus", e.GetType().Name); }
                try { Report.Item("    InBandRingingEnabled", t.InBandRingingEnabled); } catch (Exception e) { Report.Item("    InBandRingingEnabled", e.GetType().Name); }
                var i = AccessInterpreter.Interpret(packaged, status.ToString(), registeredAfterRegister: null, sig);
                Report.Item("    Interpretación", i.Verdict); Report.Line("      " + i.Explanation); Report.Line("      Siguiente: " + i.NextStep);
            }
            catch (Exception e) { Report.Line($"      Excepción: {e.GetType().Name}: {e.Message}"); }
        }

        Report.Line();
        Report.Line("  Recordatorio: ejecutar esta etapa SIN paquete y luego CON paquete (Run-Probe.ps1). Un DeniedBySystem sin paquete es lo esperado y no prueba nada sobre una app empaquetada.");
    }
}
