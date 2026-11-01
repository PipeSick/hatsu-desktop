using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinMac.Bar.Hub;

namespace WinMac.Bar.Win.Shell.Hub;

/// <summary>
/// Cadena de dispositivos de un disco (disco → controlador → puente…) con el Administrador de dispositivos (cfgmgr32, solo lectura, sin administrador) y, de cada servicio,
/// el archivo del controlador (ImagePath en el registro, solo lectura) con su empresa y versión. Sirve para ver si dos SSD del mismo modelo cuelgan de controladores o drivers distintos.
/// </summary>
internal static class DeviceTree
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint devInst, StringBuilder buffer, uint bufferLen, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_Registry_PropertyW(uint devInst, uint property, out uint regDataType, byte[]? buffer, ref uint length, uint flags);

    private const uint CM_DRP_DEVICEDESC = 1, CM_DRP_HARDWAREID = 2, CM_DRP_SERVICE = 5, CM_DRP_MFG = 12, CM_DRP_LOCATION_INFORMATION = 14;

    /// <summary>Líneas con cada nivel de la cadena, empezando por el dispositivo cuyo identificador PnP se da (p. ej. Win32_DiskDrive.PNPDeviceID).</summary>
    public static IReadOnlyList<string> Describe(string pnpDeviceId)
    {
        var lines = new List<string>();
        try
        {
            uint node;
            uint rc = CM_Locate_DevNodeW(out node, pnpDeviceId, 0);
            if (rc != 0) { lines.Add("no se pudo localizar el dispositivo en el Administrador de dispositivos (CM_Locate_DevNode devolvió " + rc + ")"); return lines; }
            for (int level = 0; level < 5; level++)
            {
                lines.Add($"nivel {level}: {Id(node)}");
                string? desc = Prop(node, CM_DRP_DEVICEDESC), mfg = Prop(node, CM_DRP_MFG), hw = Prop(node, CM_DRP_HARDWAREID, 2), loc = Prop(node, CM_DRP_LOCATION_INFORMATION), svc = Prop(node, CM_DRP_SERVICE);
                lines.Add($"    descripción «{desc ?? "n/d"}» · fabricante «{mfg ?? "n/d"}» · ubicación «{loc ?? "n/d"}»");
                lines.Add($"    ids de hardware: {hw ?? "n/d"}");
                lines.Add($"    servicio (controlador): {svc ?? "ninguno"}" + (DriverFile(svc) is { } file ? " → " + file : ""));
                uint parent;
                if (CM_Get_Parent(out parent, node, 0) != 0) break;
                node = parent;
            }
        }
        catch (Exception e) { lines.Add("no se pudo recorrer la cadena de dispositivos (" + e.GetType().Name + ": " + e.Message + ")"); }
        return lines;
    }

    private static string Id(uint node)
    {
        var sb = new StringBuilder(512);
        return CM_Get_Device_IDW(node, sb, 512, 0) == 0 ? sb.ToString() : "(identificador no disponible)";
    }

    private static string? Prop(uint node, uint property, int take = 1)
    {
        uint len = 0;
        CM_Get_DevNode_Registry_PropertyW(node, property, out _, null, ref len, 0);   // con búfer nulo devuelve CR_BUFFER_SMALL y el tamaño necesario
        if (len == 0 || len > 8192) return null;
        var buf = new byte[len];
        if (CM_Get_DevNode_Registry_PropertyW(node, property, out _, buf, ref len, 0) != 0) return null;
        string text = Encoding.Unicode.GetString(buf, 0, (int)Math.Min(len, (uint)buf.Length));
        var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : string.Join(" | ", parts.Take(take));
    }

    private static string? DriverFile(string? service)
    {
        if (string.IsNullOrWhiteSpace(service)) return null;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service);
            string? path = DriverFiles.ResolveImagePath(key?.GetValue("ImagePath") as string, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (path is null) return "sin ImagePath en el registro";
            if (!File.Exists(path)) return "archivo no encontrado: " + path;
            var v = FileVersionInfo.GetVersionInfo(path);
            return $"{path} · empresa «{v.CompanyName}» · versión {v.FileVersion}";
        }
        catch (Exception e) { return "no se pudo leer el driver (" + e.GetType().Name + ")"; }
    }
}
