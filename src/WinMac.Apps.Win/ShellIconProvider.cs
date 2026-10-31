namespace WinMac.Apps.Win;

/// <summary>
/// Extrae el icono ORIGINAL de una aplicación con IShellItemImageFactory (API documentada del Shell), en orden de preferencia:
///   1. icono alternativo configurado (.ico: icono; .png/.bmp/.jpg: miniatura con alfa; ejecutable: su icono);
///   2. app empaquetada (MSIX/UWP): «shell:AppsFolder\AUMID» → el icono/mosaico de la propia app (el .exe del paquete suele dar uno genérico);
///   3. ejecutable Win32.
/// Pide el tamaño máximo (256) y, si no se puede, baja. NO recorta, no deforma ni mete el icono en un cuadrado: devuelve los píxeles tal cual, con su alfa.
/// </summary>
public sealed class ShellIconProvider : IIconProvider
{
    private static readonly int[] Sizes = [256, 128, 64, 48, 32];
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".png", ".bmp", ".jpg", ".jpeg", ".gif" };

    public string Stamp(IconRequest r)
    {
        try
        {
            var parts = new List<string>();
            foreach (var p in new[] { r.OverridePath, r.App.ExePath }) if (!string.IsNullOrEmpty(p) && File.Exists(Environment.ExpandEnvironmentVariables(p))) { var f = new FileInfo(Environment.ExpandEnvironmentVariables(p)); parts.Add($"{f.Length}-{f.LastWriteTimeUtc.Ticks}"); }
            if (r.App.Aumid is not null) parts.Add(r.App.Aumid);
            return string.Join("|", parts);
        }
        catch (Exception) { return ""; }
    }

    public IconBitmap? Load(IconRequest r) => StaRunner.Run(() => LoadCore(r));

    internal static IconBitmap? LoadCore(IconRequest r)
    {
        var candidates = new List<(string Name, int Flags)>();
        if (!string.IsNullOrWhiteSpace(r.OverridePath) && File.Exists(Environment.ExpandEnvironmentVariables(r.OverridePath)))
            candidates.Add((r.OverridePath!, ImageExt.Contains(Path.GetExtension(r.OverridePath)) ? ShellNative.SIIGBF_THUMBNAILONLY | ShellNative.SIIGBF_BIGGERSIZEOK : ShellNative.SIIGBF_ICONONLY | ShellNative.SIIGBF_BIGGERSIZEOK));
        if (r.App.Aumid is not null) candidates.Add((@"shell:AppsFolder\" + r.App.Aumid, ShellNative.SIIGBF_ICONONLY | ShellNative.SIIGBF_BIGGERSIZEOK));
        if (!string.IsNullOrWhiteSpace(r.App.ExePath) && File.Exists(Environment.ExpandEnvironmentVariables(r.App.ExePath))) candidates.Add((r.App.ExePath!, ShellNative.SIIGBF_ICONONLY | ShellNative.SIIGBF_BIGGERSIZEOK));

        foreach (var (name, flags) in candidates)
            foreach (int size in Sizes.Where(s => s <= Math.Max(32, r.Size)))
            {
                IconBitmap? b = null;
                try { b = ShellNative.ShellImage(name, size, flags); } catch (Exception) { }
                if (IconValidator.IsValid(b)) return b;     // el primer tamaño (el mayor) que el Shell entrega y que es un icono válido
            }
        return null;                                         // sin icono válido: el llamador usa su reemplazo
    }
}
