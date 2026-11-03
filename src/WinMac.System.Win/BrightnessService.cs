using System.Runtime.InteropServices;

namespace WinMac.SystemControl.Win;

/// <summary>
/// Brillo por métodos compatibles: monitores externos por DDC/CI (API de configuración de monitores, <c>dxva2</c>, documentada) y panel interno de portátiles por WMI
/// (<c>root\wmi</c>). Cada pantalla se muestra solo si responde; las que no soportan el control quedan fuera con su motivo. El panel interno NO se ha podido probar en un portátil.
/// Las llamadas pueden tardar cientos de ms: se hacen siempre fuera del hilo de interfaz.
/// </summary>
public sealed partial class BrightnessService : IBrightnessService
{
    private const uint McCapsBrightness = 0x2;

    private sealed record Ddc(string Id, string Name, int Percent);

    public BrightnessState Read()
    {
        var monitors = new List<MonitorBrightness>(); string? reason = null;
        try { monitors.AddRange(ReadInternal()); } catch (Exception) { /* sin panel interno: normal en sobremesa */ }
        try
        {
            int idx = 0; int unsupported = 0;
            foreach (var m in Enumerate(read: true, ref unsupported)) { monitors.Add(new MonitorBrightness(m.Id, m.Name, m.Percent, "DDC/CI")); idx++; }
            if (idx == 0 && monitors.Count == 0) reason = unsupported > 0 ? "Los monitores conectados no admiten control de brillo por DDC/CI (actívalo en el menú del monitor)" : "No se encontraron pantallas con control de brillo";
        }
        catch (Exception e) { reason = "Brillo: " + e.GetType().Name; }
        // WM-016: when nothing is controllable, both answers are given: the internal panel (WMI) and the external monitors (DDC/CI)
        return monitors.Count > 0 ? new BrightnessState(true, monitors) : BrightnessState.None((InternalReason is { } ir ? "Pantalla integrada: " + ir + ". " : "") + (reason ?? "Sin pantallas controlables"));
    }

    public ServiceResult Set(string monitorId, int percent) => ServiceResult.Guard(() =>
    {
        percent = Math.Clamp(percent, 0, 100);
        if (monitorId.StartsWith("wmi:", StringComparison.Ordinal)) return SetInternal(percent);
        int unsupported = 0; bool done = false;
        foreach (var m in Enumerate(read: false, ref unsupported, onlyId: monitorId, apply: percent)) { done = true; _ = m; }
        return done ? ServiceResult.Ok : ServiceResult.Unavailable("Esa pantalla ya no responde al control de brillo");
    }, "Brillo");

    // ───────── DDC/CI: se enumeran los monitores físicos de cada HMONITOR; los handles se liberan siempre.
    private static IEnumerable<Ddc> Enumerate(bool read, ref int unsupported, string? onlyId = null, int apply = -1)
    {
        var handles = new List<IntPtr>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (m, _, _, _) => { handles.Add(m); return true; }, IntPtr.Zero);
        var result = new List<Ddc>(); int screen = 0;
        foreach (var hm in handles)
        {
            screen++;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out uint n) || n == 0) { unsupported++; continue; }
            var phys = new Native.PHYSICAL_MONITOR[n];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hm, n, phys)) { unsupported++; continue; }
            try
            {
                for (int i = 0; i < n; i++)
                {
                    string id = $"ddc:{screen}.{i}", name = $"Pantalla {screen}" + (string.IsNullOrWhiteSpace(phys[i].Description) || phys[i].Description.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) ? "" : " · " + phys[i].Description);
                    if (onlyId is not null && id != onlyId) continue;
                    if (!Native.GetMonitorCapabilities(phys[i].Handle, out uint caps, out _) || (caps & McCapsBrightness) == 0) { unsupported++; continue; }
                    if (!Native.GetMonitorBrightness(phys[i].Handle, out uint min, out uint cur, out uint max) || max <= min) { unsupported++; continue; }
                    if (apply >= 0 && !Native.SetMonitorBrightness(phys[i].Handle, (uint)(min + (max - min) * apply / 100.0))) throw new InvalidOperationException("El monitor rechazó el cambio de brillo");
                    result.Add(new Ddc(id, name, read ? (int)Math.Round((cur - min) * 100.0 / (max - min)) : apply));
                }
            }
            finally { Native.DestroyPhysicalMonitors(n, phys); }
        }

        return result;
    }

    // ───────── Panel interno (WM-016): WMI root\wmi con System.Management. Vacío si el equipo no tiene panel con brillo controlable.
    /// <summary>Why the internal panel is not available (null when it is): shown in the log once, so a laptop without brightness says why.</summary>
    public static string? InternalReason { get; private set; }

    private static IEnumerable<MonitorBrightness> ReadInternal()
    {
        try
        {
            using var q = new System.Management.ManagementObjectSearcher(new System.Management.ManagementScope(@"\\.\root\wmi"), new System.Management.ObjectQuery("SELECT CurrentBrightness, InstanceName FROM WmiMonitorBrightness WHERE Active = TRUE"));
            foreach (System.Management.ManagementObject o in q.Get())
                using (o)
                {
                    InternalReason = null;
                    return [new MonitorBrightness("wmi:0", "Pantalla integrada", Convert.ToInt32(o["CurrentBrightness"]), "WMI")];
                }

            InternalReason = "Sin panel interno con brillo por WMI";
        }
        catch (System.Management.ManagementException e) { InternalReason = "WMI: " + e.ErrorCode; }   // NotSupported on desktops and VMs: normal
        catch (COMException e) { InternalReason = $"WMI no disponible (0x{e.HResult:X8})"; }
        return [];
    }

    private static ServiceResult SetInternal(int percent)
    {
        try
        {
            using var q = new System.Management.ManagementObjectSearcher(new System.Management.ManagementScope(@"\\.\root\wmi"), new System.Management.ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"));
            foreach (System.Management.ManagementObject m in q.Get())
                using (m)
                {
                    using var args = m.GetMethodParameters("WmiSetBrightness");
                    args["Timeout"] = (uint)1; args["Brightness"] = (byte)Math.Clamp(percent, 0, 100);   // the method's own types (uint32, uint8)
                    m.InvokeMethod("WmiSetBrightness", args, null);
                    return ServiceResult.Ok;
                }

            return ServiceResult.Unavailable("La pantalla integrada no admite control de brillo");
        }
        catch (System.Management.ManagementException e) { return ServiceResult.Unavailable("La pantalla integrada rechazó el cambio (" + e.ErrorCode + ")"); }
    }
}
