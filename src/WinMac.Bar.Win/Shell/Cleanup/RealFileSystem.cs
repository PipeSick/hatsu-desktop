using System.Runtime.InteropServices;
using WinMac.Bar.Cleanup;

namespace WinMac.Bar.Win.Shell.Cleanup;

/// <summary>
/// Sistema de archivos real para Cleanup. No sigue enlaces (marca cualquier reparse point), no cambia permisos ni eleva privilegios, y abre el archivo SIN compartir antes de borrarlo
/// para no tocar uno que otro proceso tiene abierto. Los errores de acceso o de uso se devuelven como resultado, no como excepción.
/// </summary>
internal sealed class RealFileSystem : IFileSystem
{
    public IEnumerable<FsEntry> Children(string dir)
    {
        var di = new DirectoryInfo(dir);
        foreach (var fi in di.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false }))
        {
            FsEntry? e = null;
            try { e = ToEntry(fi); } catch (Exception) { /* desapareció o sin permiso: se ignora */ }
            if (e is not null) yield return e;
        }
    }

    private static FsEntry ToEntry(FileSystemInfo fi)
    {
        bool dir = fi.Attributes.HasFlag(FileAttributes.Directory), reparse = fi.Attributes.HasFlag(FileAttributes.ReparsePoint) || fi.LinkTarget is not null;
        return new FsEntry(fi.FullName, dir, reparse, fi is FileInfo f && !dir ? f.Length : 0, fi.LastWriteTimeUtc);
    }

    public FsEntry? Stat(string path)
    {
        try
        {
            FileSystemInfo fi = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return fi.Exists ? ToEntry(fi) : null;
        }
        catch (Exception) { return null; }
    }

    public DeleteOutcome DeleteFile(string path)
    {
        try
        {
            // abrir sin compartir: si otro proceso lo tiene abierto, falla y NO se borra (no se fuerza nada)
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None)) { }
            File.Delete(path);
            return File.Exists(path) ? DeleteOutcome.Locked : DeleteOutcome.Deleted;
        }
        catch (FileNotFoundException) { return DeleteOutcome.Vanished; }
        catch (DirectoryNotFoundException) { return DeleteOutcome.Vanished; }
        catch (UnauthorizedAccessException) { return DeleteOutcome.Denied; }
        catch (IOException) { return DeleteOutcome.Locked; }
        catch (Exception) { return DeleteOutcome.Denied; }
    }

    public bool DeleteEmptyDirectory(string path)
    {
        try { Directory.Delete(path, recursive: false); return true; }   // falla si no está vacío: nunca borra contenido
        catch (Exception) { return false; }
    }
}

/// <summary>Papelera por la API del shell (SHQueryRecycleBinW / SHEmptyRecycleBinW): WinMac no recorre ni borra a mano $Recycle.Bin.</summary>
internal sealed class ShellRecycleBin : IRecycleBin
{
    [StructLayout(LayoutKind.Sequential)] private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? root, uint flags);
    private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

    public (long Bytes, long Items)? Query()
    {
        var i = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref i) == 0 ? (i.i64Size, i.i64NumItems) : null;
    }

    public bool Empty() { int hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND); return hr == 0 || hr == 1 /* S_FALSE: ya estaba vacía */; }
}
