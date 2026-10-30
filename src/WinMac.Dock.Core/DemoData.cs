namespace WinMac.Dock;

/// <summary>Datos de demostración (CI y renders fuera de pantalla). Nombres genéricos; sin marcas ni recursos de terceros.</summary>
public static class DemoData
{
    private static readonly string[] Names = ["Archivos", "Navegador", "Correo", "Música", "Fotos", "Notas", "Terminal", "Ajustes", "Mapas", "Calendario"];

    public static IReadOnlyList<DockItemModel> Items(int activeIndex = 2)
    {
        var l = new List<DockItemModel>();
        for (int i = 0; i < Names.Length; i++)
        {
            bool running = i is 0 or 2 or 3 or 6 or 8;
            l.Add(new DockItemModel($"app:demo{i}", DockItemKind.App, Names[i], $@"C:\Demo\{Names[i]}.exe", true, running, running ? 1 : 0, i == activeIndex));
        }
        l.Add(new DockItemModel("app:demo-recent", DockItemKind.Separator, "", null) with { Id = "sep:recents" });
        l.Add(new DockItemModel("app:demo-r1", DockItemKind.App, "Editor", @"C:\Demo\Editor.exe", false, true, 1));
        l.Add(new DockItemModel("sep:system", DockItemKind.Separator, "", null));
        l.Add(new DockItemModel("folder:downloads", DockItemKind.Folder, "Descargas", @"C:\Users\demo\Downloads"));
        l.Add(new DockItemModel("trash", DockItemKind.Trash, "Papelera", "shell:RecycleBinFolder"));
        return l;
    }

    /// <summary>Escena «muchas apps abiertas»: todas las ancladas en ejecución (algunas con varias ventanas, una minimizada) y varias recientes.</summary>
    public static IReadOnlyList<DockItemModel> AllOpen()
    {
        var l = new List<DockItemModel>();
        for (int i = 0; i < Names.Length; i++)
            l.Add(new DockItemModel($"app:demo{i}", DockItemKind.App, Names[i], $@"C:\Demo\{Names[i]}.exe", true, true, 1 + i % 3, i == 1, i == 4));
        l.Add(new DockItemModel("sep:recents", DockItemKind.Separator, "", null));
        foreach (var n in new[] { "Editor", "Hoja", "Visor" }) l.Add(new DockItemModel($"app:demo-{n}", DockItemKind.App, n, $@"C:\Demo\{n}.exe", false, true, 1));
        l.Add(new DockItemModel("sep:system", DockItemKind.Separator, "", null));
        l.Add(new DockItemModel("folder:downloads", DockItemKind.Folder, "Descargas", @"C:\Users\demo\Downloads"));
        l.Add(new DockItemModel("trash", DockItemKind.Trash, "Papelera", "shell:RecycleBinFolder"));
        return l;
    }
}
