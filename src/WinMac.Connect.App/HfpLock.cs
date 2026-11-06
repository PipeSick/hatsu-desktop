namespace WinMac.Connect.App;

/// <summary>Cerrojo entre procesos de Hatsu (módulo de llamadas y experimentos) para que solo uno toque el transporte telefónico. Se libera solo si el proceso muere.</summary>
internal static class HfpLock
{
    public static FileStream? TryTake()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "hfp.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException) { return null; }   // lo tiene otro proceso
        catch (UnauthorizedAccessException) { return null; }
        catch (Exception) { return null; }
    }
}
