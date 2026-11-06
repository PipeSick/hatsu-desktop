using System.Runtime.InteropServices;

namespace WinMac.Apps.Win;

/// <summary>
/// Lee la lista de «Todas las aplicaciones» de Windows: la carpeta virtual <c>FOLDERID_AppsFolder</c> (la misma que usa el menú Inicio), con API documentada del Shell
/// (<c>SHGetKnownFolderItem</c> + <c>IEnumShellItems</c>). Cubre los accesos directos del menú Inicio (del usuario y de todos) y las apps empaquetadas MSIX/UWP.
/// Solo lectura: no toca el registro ni el menú Inicio. Corre en un hilo STA propio (si el Shell se cuelga, no se lleva la interfaz).
/// </summary>
public static class AppsFolderReader
{
    private static readonly Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    private static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    private const uint SigdnNormalDisplay = 0, SigdnParentRelativeParsing = 0x80018001;

    public static IReadOnlyList<InstalledApp> Read(Action<string>? log = null) => StaRunner.Run(() => ReadCore(log)) ?? [];

    private static IReadOnlyList<InstalledApp> ReadCore(Action<string>? log)
    {
        var list = new List<InstalledApp>();
        var iidItem = typeof(IShellItemLocal).GUID;
        int hr = SHGetKnownFolderItem(FolderIdAppsFolder, 0, IntPtr.Zero, ref iidItem, out var folder);
        if (hr != 0 || folder is null) { log?.Invoke($"[apps] AppsFolder no disponible (0x{hr:X8})"); return list; }
        try
        {
            var bhid = BhidEnumItems; var iidEnum = typeof(IEnumShellItems).GUID;
            hr = folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out var enumObj);
            if (hr != 0 || enumObj is not IEnumShellItems items) { log?.Invoke($"[apps] no se pudo enumerar AppsFolder (0x{hr:X8})"); return list; }
            try
            {
                while (items.Next(1, out var item, out uint fetched) == 0 && fetched == 1 && item is not null)
                {
                    try
                    {
                        string? name = Name(item, SigdnNormalDisplay), id = Name(item, SigdnParentRelativeParsing);
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                            list.Add(new InstalledApp(id, name, id.Contains('!') && !id.Contains('\\') ? AppKind.Packaged : AppKind.Win32));   // AUMID: «Familia_editor!App»
                    }
                    finally { Marshal.ReleaseComObject(item); }
                    if (list.Count > 5000) break;   // tope defensivo
                }
            }
            finally { Marshal.ReleaseComObject(items); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        return list;
    }

    private static string? Name(IShellItemLocal item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out var p) != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); } finally { Marshal.FreeCoTaskMem(p); }
    }

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderItem([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, int flags, IntPtr token, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemLocal? item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemLocal
    {
        [PreserveSig] int BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
        [PreserveSig] int GetParent(out IShellItemLocal? parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attrs);
        [PreserveSig] int Compare(IShellItemLocal other, uint hint, out int order);
    }

    [ComImport, Guid("70629033-E363-4A28-A567-0DB78006E6D7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItemLocal? item, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems? copy);
    }
}

/// <summary>
/// Abre una app del índice: <c>explorer.exe shell:AppsFolder\Id</c>, igual que el menú Inicio (sirve para Win32 y empaquetadas, respeta «Ejecutar como» y la
/// redirección de Windows). WinMac no hereda privilegios a la app abierta: la lanza el Explorador del usuario.
/// </summary>
public static class AppLauncher
{
    public static string? Launch(InstalledApp app)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + app.LaunchTarget + "\"") { UseShellExecute = false });
            return null;
        }
        catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
    }

    /// <summary>
    /// «Ejecutar como administrador» (Ctrl+Mayús+Intro en Spotlight y Launchpad, y el menú de clic derecho de Launchpad). Usa el verbo <c>runas</c> del Shell sobre el mismo destino que el menú Inicio:
    /// Windows muestra su propio aviso de Control de cuentas de usuario (UAC); Hatsu nunca eleva nada por su cuenta. Devuelve null si se abrió O si el usuario canceló el aviso (no es un error);
    /// un texto con el motivo si Windows no puede abrir esa app elevada (p. ej. algunas apps de la Tienda).
    /// </summary>
    public static string? LaunchAsAdmin(InstalledApp app)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(app.LaunchTarget) { UseShellExecute = true, Verb = "runas" });
            return null;
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223) { return null; }   // ERROR_CANCELLED: el usuario dijo «No» en UAC
        catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
    }
}
