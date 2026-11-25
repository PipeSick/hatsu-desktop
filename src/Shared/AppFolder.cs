using System.Text;

namespace WinMac;

/// <summary>
/// Carpeta de datos de la app en %APPDATA% y %LOCALAPPDATA%. Se llamaba <c>WinMac</c> (nombre anterior del proyecto) y ahora es <c>Hatsu</c>.
/// <para>El primer proceso que toca <see cref="Name"/> migra lo que hubiera en la carpeta antigua: COPIA (nunca mueve ni borra), sin pisar nada que ya exista en la nueva, y deja
/// una marca para no repetirlo. Se omiten los registros y las cachés (se regeneran). Así actualizar no pierde ajustes ni extensiones y volver a una versión anterior sigue funcionando.</para>
/// <para>Este archivo se compila (como <c>internal</c>) dentro de cada proyecto que lo necesita, para que Barra, Dock, Connect y host usen la misma regla sin depender unos de otros.</para>
/// </summary>
internal static class AppFolder
{
    public const string Current = "Hatsu", Legacy = "WinMac";
    public const string MarkerFile = ".migrado-de-winmac";

    /// <summary>El nombre de la carpeta. Leerlo dispara (una vez por proceso) la migración, así que cualquier ruta construida con él ya la tiene hecha.</summary>
    public static string Name => Current;

    static AppFolder()
    {
        try { MigrateStandardFolders(); } catch (Exception) { /* una migración fallida nunca debe impedir arrancar: se reintenta en el próximo inicio (no hay marca) */ }
    }

    private static void MigrateStandardFolders()
    {
        Mutex? gate = null; bool owned = false;
        try { gate = new Mutex(false, @"Local\HatsuDesktop.AppFolderMigration"); owned = gate.WaitOne(TimeSpan.FromSeconds(8)); } catch (Exception) { }
        try
        {
            if (!owned) return;   // otro proceso la está haciendo (o no hay mutex): se confía en que termine; el siguiente arranque lo comprueba
            MigrateUnder(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            MigrateUnder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
        finally { try { if (owned) gate?.ReleaseMutex(); } catch (Exception) { } gate?.Dispose(); }
    }

    /// <summary>Migra <c>parent\WinMac</c> → <c>parent\Hatsu</c>. Devuelve cuántos archivos se copiaron (0 si no había nada, ya estaba hecho o falló antes de copiar).</summary>
    internal static int MigrateUnder(string parent)
    {
        if (string.IsNullOrEmpty(parent)) return 0;
        string legacy = Path.Combine(parent, Legacy), target = Path.Combine(parent, Current), marker = Path.Combine(target, MarkerFile);
        if (!Directory.Exists(legacy) || File.Exists(marker)) return 0;
        int copied = Copy(legacy, target);
        Directory.CreateDirectory(target);
        File.WriteAllText(marker, "Copiado desde " + Legacy + " el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ". La carpeta anterior no se tocó.", Encoding.UTF8);   // solo si no hubo excepción
        return copied;
    }

    private static bool Skip(string name, bool isDir) =>
        isDir ? name.Equals("logs", StringComparison.OrdinalIgnoreCase) || name.Contains("cache", StringComparison.OrdinalIgnoreCase)
              : name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || name.Equals(MarkerFile, StringComparison.OrdinalIgnoreCase);

    private static int Copy(string from, string to)
    {
        Directory.CreateDirectory(to); int n = 0;
        foreach (var file in Directory.EnumerateFiles(from))
        {
            if (Skip(Path.GetFileName(file), false)) continue;
            var dest = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(dest)) continue;   // nunca pisa lo que ya hay en la carpeta nueva
            File.Copy(file, dest, overwrite: false); n++;
        }

        foreach (var dir in Directory.EnumerateDirectories(from))
        {
            if (Skip(Path.GetFileName(dir), true)) continue;
            n += Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        return n;
    }
}
