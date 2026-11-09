using System.Runtime.InteropServices;

namespace WinMac.Bar.Win.Capture;

/// <summary>Moves a file to the Recycle Bin (recoverable, as Explorer does) with SHFileOperation; no confirmation dialog of its own.</summary>
internal static class RecycleBin
{
    public static bool Send(string path)
    {
        if (!File.Exists(path)) return false;
        var op = new SHFILEOPSTRUCT { wFunc = 3 /* FO_DELETE */, pFrom = path + "\0\0", fFlags = 0x40 /* FOF_ALLOWUNDO */ | 0x10 /* FOF_NOCONFIRMATION */ | 0x4 /* FOF_SILENT */ | 0x400 /* FOF_NOERRORUI */ };
        return SHFileOperationW(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo; public ushort fFlags; [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);
}

/// <summary>
/// Windows' own share sheet for a desktop window (IDataTransferManagerInterop, the documented way for Win32 apps): shares the capture file.
/// </summary>
internal static class WindowsShare
{
    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DtmIid = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    public static bool Show(IntPtr hwnd, string path, Action<string> log)
    {
        try
        {
            var interop = global::Windows.ApplicationModel.DataTransfer.DataTransferManager.As<IDataTransferManagerInterop>();
            var iid = DtmIid;
            var dtm = global::Windows.ApplicationModel.DataTransfer.DataTransferManager.FromAbi(interop.GetForWindow(hwnd, ref iid));
            dtm.DataRequested += async (_, e) =>
            {
                var deferral = e.Request.GetDeferral();
                try
                {
                    var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                    e.Request.Data.Properties.Title = Path.GetFileName(path);
                    e.Request.Data.SetStorageItems([file]);
                }
                catch (Exception ex) { log("[quicklook] compartir: " + ex.GetType().Name + ": " + ex.Message); }
                finally { deferral.Complete(); }
            };
            interop.ShowShareUIForWindow(hwnd);
            return true;
        }
        catch (Exception e) { log("[quicklook] compartir no disponible: " + e.GetType().Name + ": " + e.Message); return false; }
    }
}
