using System.Data.OleDb;
using WinMac.Bar.Launcher;

namespace WinMac.Bar.Win.Launcher;

/// <summary>
/// Archivos para Spotlight sin indexar nada por nuestra cuenta: (1) los recientes de Windows (accesos de <c>%APPDATA%\Microsoft\Windows\Recent</c>), y (2) el índice de
/// Windows Search (proveedor OLE DB documentado <c>Search.CollatorDSO</c>), limitado a la carpeta del usuario y a 6 resultados, con tope de tiempo. Si el servicio no está
/// (p. ej. desactivado), solo recientes. Respeta los permisos: Windows Search solo devuelve lo que el usuario puede leer.
/// </summary>
internal static class FileSearch
{
    public static IReadOnlyList<(string Name, string LinkPath, DateTime Modified)> Recent()
    {
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            return new DirectoryInfo(dir).EnumerateFiles("*.lnk").OrderByDescending(f => f.LastWriteTimeUtc).Take(300)
                .Select(f => (Path.GetFileNameWithoutExtension(f.Name), f.FullName, f.LastWriteTimeUtc)).ToList();
        }
        catch (Exception) { return []; }
    }

    public static IReadOnlyList<(string Name, string Path)> Folders()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<(string, string)>
        {
            ("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), ("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Descargas", Path.Combine(home, "Downloads")), ("Imágenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Música", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)), ("Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)), ("Carpeta personal", home),
        };
        return list.Where(f => !string.IsNullOrEmpty(f.Item2) && Directory.Exists(f.Item2)).ToList();
    }

    /// <summary>Windows Search. null = no disponible (el motivo va al registro una vez).</summary>
    public static IReadOnlyList<SpotItem>? Indexed(string q, Action<string> log)
    {
        q = q.Trim(); if (q.Length < 2) return [];
        string like = q.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
        string scope = "file:" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace('\\', '/');
        string sql = $"SELECT TOP 6 System.ItemPathDisplay, System.ItemNameDisplay, System.ItemFolderPathDisplay FROM SystemIndex WHERE SCOPE='{scope.Replace("'", "''")}' AND System.ItemNameDisplay LIKE '%{like}%' ORDER BY System.DateModified DESC";
        try
        {
            using var cn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            cn.Open();
            using var cmd = new OleDbCommand(sql, cn) { CommandTimeout = 2 };
            using var r = cmd.ExecuteReader();
            var list = new List<SpotItem>();
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                string path = r.GetString(0), name = r.IsDBNull(1) ? Path.GetFileName(path) : r.GetString(1), folder = r.IsDBNull(2) ? "" : r.GetString(2);
                list.Add(new SpotItem(Directory.Exists(path) ? SpotKind.Folder : SpotKind.File, name, folder, path));
            }

            return list;
        }
        catch (Exception e) { log("[spotlight] Windows Search no disponible: " + e.GetType().Name + " " + e.Message.Split('\n')[0]); return null; }
    }
}
