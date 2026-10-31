using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Apps.Win;

/// <summary>
/// Identidad de la app a la que pertenece una ventana, con APIs documentadas:
///   · proceso empaquetado → GetApplicationUserModelId(proceso) → identidad «Packaged» (AUMID);
///   · ventana alojada por ApplicationFrameHost (UWP) → AUMID de la ventana (SHGetPropertyStoreForWindow, System.AppUserModel.ID);
///   · cualquier otra → identidad Win32 por ejecutable (un AUMID explícito de una app Win32 NO cambia su identidad: se ancla por ruta).
/// Devuelve null para ventanas que no se pueden atribuir (p. ej. un marco de ApplicationFrameHost sin AUMID).
/// </summary>
public static class WindowIdentityResolver
{
    private const int APPMODEL_ERROR_NO_APPLICATION = 15703;
    private static readonly Dictionary<uint, string?> ProcessCache = new();

    public static string? ProcessAumid(uint pid)
    {
        lock (ProcessCache) if (ProcessCache.TryGetValue(pid, out var c)) return c;
        string? result = null; IntPtr h = ShellNative.OpenProcess(ShellNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try { uint len = 512; var sb = new StringBuilder((int)len); int rc = ShellNative.GetApplicationUserModelId(h, ref len, sb); if (rc == 0 && sb.Length > 0) result = sb.ToString(); else if (rc != APPMODEL_ERROR_NO_APPLICATION) result = null; }
            finally { ShellNative.CloseHandle(h); }
        }
        lock (ProcessCache) { if (ProcessCache.Count > 512) ProcessCache.Clear(); ProcessCache[pid] = result; }
        return result;
    }

    public static string? WindowAumid(IntPtr hwnd)
    {
        var iid = ShellNative.IID_IPropertyStore;
        if (ShellNative.SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null) return null;
        try
        {
            var key = new ShellNative.PROPERTYKEY { fmtid = ShellNative.PKEY_AppUserModel_ID, pid = 5 };
            if (store.GetValue(ref key, out var pv) != 0) return null;
            try { return pv.vt == 31 /* VT_LPWSTR */ && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : null; }
            finally { ShellNative.PropVariantClear(ref pv); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static AppIdentity? Resolve(IntPtr hwnd, uint pid, string? exePath)
    {
        try
        {
            var procAumid = ProcessAumid(pid);
            if (procAumid is not null) return AppIdentity.FromAumid(procAumid, null, exePath);
            bool isFrameHost = exePath is not null && Path.GetFileName(exePath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
            if (isFrameHost) { var w = WindowAumid(hwnd); return w is null ? null : AppIdentity.FromAumid(w); }
            return exePath is null ? null : AppIdentity.FromExe(exePath);
        }
        catch (Exception) { return exePath is null ? null : AppIdentity.FromExe(exePath); }
    }
}
