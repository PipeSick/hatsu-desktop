using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Host.App;

/// <summary>
/// WM-036: <c>WinMac.exe --diagnose-security</c>. Diagnóstico de SOLO LECTURA de por qué Windows pudo bloquear un módulo (p. ej. el Dock): no cambia ni desactiva nada.
/// <list type="bullet">
/// <item>Estado de Smart App Control (registro, solo lectura).</item>
/// <item>Eventos de Integridad de código (Microsoft-Windows-CodeIntegrity/Operational: 3033, 3034, 3076, 3077, 3089) y del antivirus (1116, 1117) que nombran la carpeta de WinMac.
/// Se leen con <c>wevtutil</c>, que viene con Windows; si el registro no es legible sin administrador, se dice.</item>
/// <item>Qué ejecutables del paquete tienen firma Authenticode (ninguno, hoy: la beta no está firmada).</item>
/// </list>
/// El informe va a <c>%LOCALAPPDATA%\WinMac\logs\security-report.txt</c> para compartirlo. No incluye datos personales: solo rutas de WinMac, códigos y fechas.
/// </summary>
internal static class SecurityDiagnostics
{
    public static int Run(bool quiet)
    {
        string dir = AppContext.BaseDirectory.TrimEnd('\\'); var sb = new StringBuilder();
        sb.AppendLine($"Hatsu Desktop — diagnóstico de seguridad · {DateTime.Now:yyyy-MM-dd HH:mm} · Windows {Environment.OSVersion.Version}");
        sb.AppendLine("Solo lectura: no se cambió ni desactivó nada.");
        sb.AppendLine($"Carpeta: {dir}");
        sb.AppendLine();
        sb.AppendLine("Smart App Control: " + SmartAppControl());
        sb.AppendLine();
        sb.AppendLine("Firma de los ejecutables:");
        foreach (var exe in new[] { "WinMac.exe", "WinMac.Bar.App.exe", "WinMac.Dock.App.exe", "WinMac.Connect.App.exe", @"NotchWin\NotchWin.App.exe" })
        {
            var p = Path.Combine(dir, exe);
            sb.AppendLine($"  {exe}: {(File.Exists(p) ? Signed(p) ? "firmado" : "sin firma" : "no está")}");
        }
        sb.AppendLine();
        int hits = 0;
        hits += Events(sb, "Integridad de código (bloqueos y avisos de Control de aplicaciones)", "Microsoft-Windows-CodeIntegrity/Operational", "3033 or EventID=3034 or EventID=3076 or EventID=3077 or EventID=3089", dir);
        hits += Events(sb, "Antivirus (historial de protección)", "Microsoft-Windows-Windows Defender/Operational", "1116 or EventID=1117", dir);
        sb.AppendLine();
        sb.AppendLine(hits == 0
            ? "No hay eventos recientes que nombren la carpeta de Hatsu Desktop. Si un módulo no arrancó, mira host.log (el motivo con su código de Windows)."
            : "Hay eventos que nombran archivos de Hatsu Desktop: el archivo indicado es el que Windows bloqueó. No desactives la protección: comparte este informe.");
        sb.AppendLine("Por qué puede pasar: la beta no está firmada digitalmente; Smart App Control bloquea los ejecutables sin firma ni reputación, y cada beta tiene archivos nuevos (sin reputación).");

        string report = sb.ToString();
        string outFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "logs", "security-report.txt");
        try { Directory.CreateDirectory(Path.GetDirectoryName(outFile)!); File.WriteAllText(outFile, report); } catch (Exception) { outFile = "(no se pudo guardar)"; }
        try { Console.WriteLine(report); } catch (Exception) { }
        if (!quiet) MessageBoxW(IntPtr.Zero, (hits == 0 ? "No se encontraron bloqueos recientes de archivos de Hatsu Desktop." : $"Se encontraron {hits} evento(s) de seguridad sobre archivos de Hatsu Desktop.") + "\n\nInforme: " + outFile, "Hatsu Desktop — diagnóstico de seguridad", 0x40);
        return 0;
    }

    private static string SmartAppControl()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState", null);
            return v switch { 0 => "desactivado", 1 => "activado (bloquea ejecutables sin firma ni reputación)", 2 => "en evaluación (Windows decide si lo activa)", null => "sin dato (Windows sin Smart App Control o no legible)", _ => "estado " + v };
        }
        catch (Exception e) { return "no legible (" + e.GetType().Name + ")"; }
    }

    private static int Events(StringBuilder sb, string title, string log, string ids, string dir)
    {
        sb.AppendLine(title + ":");
        string query = $"*[System[(EventID={ids}) and TimeCreated[timediff(@SystemTime) <= 1209600000]]]";   // últimos 14 días
        try
        {
            var psi = new ProcessStartInfo("wevtutil.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "qe", log, "/q:" + query, "/c:200", "/rd:true", "/f:text" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string text = p.StandardOutput.ReadToEnd(), err = p.StandardError.ReadToEnd(); p.WaitForExit(15000);
            if (p.ExitCode != 0) { sb.AppendLine("  no se pudo leer este registro" + (err.Contains("denied", StringComparison.OrdinalIgnoreCase) || err.Contains("denegado", StringComparison.OrdinalIgnoreCase) ? " sin administrador (Visor de eventos › Registros de aplicaciones y servicios › Microsoft › Windows › CodeIntegrity › Operational)" : ": " + err.Trim())); return 0; }
            int n = 0;
            foreach (var ev in text.Split("Event[", StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ev.Contains(dir, StringComparison.OrdinalIgnoreCase) && !ev.Contains("WinMac", StringComparison.OrdinalIgnoreCase) && !ev.Contains("NotchWin", StringComparison.OrdinalIgnoreCase)) continue;
                string Field(string name) => ev.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith(name, StringComparison.OrdinalIgnoreCase))?.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "";
                string desc = ev.Split("Description:", 2).ElementAtOrDefault(1)?.Trim().Split('\n').FirstOrDefault()?.Trim() ?? "";
                sb.AppendLine($"  {Field("Date")} · evento {Field("Event ID")} · {desc}"); n++;
                if (n >= 20) break;
            }
            if (n == 0) sb.AppendLine("  ninguno sobre Hatsu Desktop en los últimos 14 días");
            return n;
        }
        catch (Exception e) { sb.AppendLine("  no disponible: " + e.GetType().Name); return 0; }
    }

    /// <summary>¿Tiene el archivo una firma Authenticode válida? (WinVerifyTrust, solo comprobación).</summary>
    private static bool Signed(string path)
    {
        var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(file, pFile, false);
            var data = new WINTRUST_DATA { cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /* WTD_UI_NONE */, fdwRevocationChecks = 0, dwUnionChoice = 1 /* WTD_CHOICE_FILE */, pFile = pFile, dwStateAction = 0, dwProvFlags = 0x1000 /* WTD_CACHE_ONLY_URL_RETRIEVAL */ };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        catch (Exception) { return false; }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
    [StructLayout(LayoutKind.Sequential)] private struct WINTRUST_DATA
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice; public IntPtr pFile;
        public uint dwStateAction; public IntPtr hWVTStateData, pwszURLReference; public uint dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
    }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WINTRUST_DATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr h, string text, string caption, uint type);
}
