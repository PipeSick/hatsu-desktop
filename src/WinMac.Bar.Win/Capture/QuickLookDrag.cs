using System.Runtime.InteropServices;

namespace WinMac.Bar.Win.Capture;

/// <summary>
/// Arrastrar la miniatura de una captura a otra aplicación (un chat, un correo, el Explorador…). Se entrega el ARCHIVO guardado con el objeto de datos del propio Shell de Windows
/// (el mismo que usa el Explorador al arrastrar un archivo), así que el destino recibe la imagen como cualquier archivo arrastrado. API documentada, sin objetos COM propios.
/// </summary>
internal static class QuickLookDrag
{
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe"), BhidDataObject = new("b8c0bd9f-ed24-455c-83e6-d5390c4fe8c4"), IidDataObject = new("0000010e-0000-0000-c000-000000000046");

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [DllImport("shell32.dll")] private static extern int SHDoDragDrop(IntPtr hwnd, IntPtr dataObject, IntPtr dropSource, uint okEffect, out uint effect);
    [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);

    /// <summary>Inicia el arrastre (bloquea hasta soltar). Devuelve el efecto (0 = no se soltó en ningún destino) o null si no se pudo iniciar, con el motivo en <paramref name="error"/>.</summary>
    public static uint? Start(IntPtr hwnd, string path, out string? error)
    {
        error = null; IntPtr data = IntPtr.Zero;
        try
        {
            if (!File.Exists(path)) { error = "el archivo ya no existe"; return null; }
            OleInitialize(IntPtr.Zero);   // S_FALSE si el hilo ya lo tenía: no es un error
            var iid = IidShellItem; int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item); if (hr != 0) { error = $"0x{hr:X8}"; return null; }
            var bhid = BhidDataObject; var riid = IidDataObject; hr = item.BindToHandler(IntPtr.Zero, ref bhid, ref riid, out data); if (hr != 0 || data == IntPtr.Zero) { error = $"0x{hr:X8}"; return null; }
            hr = SHDoDragDrop(hwnd, data, IntPtr.Zero, 1 /* DROPEFFECT_COPY */ | 4 /* DROPEFFECT_LINK */, out uint effect);
            return hr == 0 || hr == 0x00040100 /* DRAGDROP_S_DROP */ || hr == 0x00040101 /* DRAGDROP_S_CANCEL */ ? effect : (uint?)null;
        }
        catch (Exception e) { error = e.GetType().Name; return null; }
        finally { if (data != IntPtr.Zero) Marshal.Release(data); }
    }
}
