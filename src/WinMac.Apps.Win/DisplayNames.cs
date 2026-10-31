using System.Collections.Concurrent;
using System.Diagnostics;

namespace WinMac.Apps.Win;

/// <summary>Nombre legible de una app, con caché: empaquetadas → nombre que muestra el Shell para «shell:AppsFolder\AUMID»; Win32 → descripción del archivo (FileVersionInfo) o nombre del ejecutable.</summary>
public static class DisplayNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Resolve(AppIdentity id) => Cache.GetOrAdd(id.Id, _ => ResolveCore(id));

    private static string ResolveCore(AppIdentity id)
    {
        try
        {
            if (id.Aumid is not null)
            {
                var n = StaRunner.Run(() => ShellNative.ShellDisplayName(@"shell:AppsFolder\" + id.Aumid));
                if (!string.IsNullOrWhiteSpace(n)) return n!;
            }
            if (id.ExePath is not null)
            {
                string path = Environment.ExpandEnvironmentVariables(id.ExePath);
                try { var d = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d)) return d.Trim(); } catch (Exception) { }
                return AppIdentity.StemTitle(path);
            }
        }
        catch (Exception) { }
        return id.DisplayName;
    }
}
