using WinMac;
using Xunit;

namespace WinMac.Host.Core.Tests;

public sealed class AppFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hatsu-appfolder-" + Guid.NewGuid().ToString("N"));
    public AppFolderTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (Exception) { } }

    private string Legacy(params string[] parts) => Path.Combine([_root, AppFolder.Legacy, .. parts]);
    private string Now(params string[] parts) => Path.Combine([_root, AppFolder.Current, .. parts]);
    private void Put(string path, string text = "x") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

    [Fact]
    public void The_new_folder_is_Hatsu_and_the_old_one_was_WinMac()
    {
        Assert.Equal("Hatsu", AppFolder.Name); Assert.Equal("WinMac", AppFolder.Legacy);
    }

    [Fact]
    public void Settings_and_extensions_are_copied_but_logs_caches_and_locks_are_not_and_the_old_folder_is_left_intact()
    {
        Put(Legacy("winmac.json"), "{\"a\":1}"); Put(Legacy("extensions", "e1", "manifest.json"), "m"); Put(Legacy("logs", "host.log"), "log"); Put(Legacy("weather-cache", "w.json")); Put(Legacy("icon-cache-launcher", "i.png")); Put(Legacy("hfp.lock")); Put(Legacy("host.log"));
        int n = AppFolder.MigrateUnder(_root);
        Assert.Equal(2, n); Assert.Equal("{\"a\":1}", File.ReadAllText(Now("winmac.json"))); Assert.True(File.Exists(Now("extensions", "e1", "manifest.json")));
        Assert.False(Directory.Exists(Now("logs"))); Assert.False(Directory.Exists(Now("weather-cache"))); Assert.False(Directory.Exists(Now("icon-cache-launcher"))); Assert.False(File.Exists(Now("hfp.lock"))); Assert.False(File.Exists(Now("host.log")));
        Assert.True(File.Exists(Now(AppFolder.MarkerFile)));
        Assert.True(File.Exists(Legacy("winmac.json"))); Assert.True(File.Exists(Legacy("logs", "host.log")));   // nada se mueve ni se borra
    }

    [Fact]
    public void It_runs_once_and_never_overwrites_what_the_new_folder_already_has()
    {
        Put(Legacy("a.json"), "viejo"); Put(Now("a.json"), "nuevo");
        Assert.Equal(0, AppFolder.MigrateUnder(_root)); Assert.Equal("nuevo", File.ReadAllText(Now("a.json")));
        Put(Legacy("b.json"), "otro"); Assert.Equal(0, AppFolder.MigrateUnder(_root)); Assert.False(File.Exists(Now("b.json")));   // con la marca puesta no vuelve a copiar
    }

    [Fact]
    public void Without_an_old_folder_nothing_is_created()
    {
        Assert.Equal(0, AppFolder.MigrateUnder(_root)); Assert.False(Directory.Exists(Now())); Assert.Equal(0, AppFolder.MigrateUnder(""));
    }

    [Fact]
    public void No_source_file_builds_a_data_path_with_the_old_name_any_more()
    {
        var bad = new List<string>(); var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || f.Contains("Tests") || f.EndsWith("AppFolder.cs")) continue;
            foreach (var line in File.ReadLines(f))
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"SpecialFolder\.(Local)?ApplicationData\)\s*,\s*""WinMac""")) bad.Add(Path.GetFileName(f) + ": " + line.Trim());
        }

        Assert.Empty(bad);   // todas las rutas de datos salen de AppFolder.Name (que migra la carpeta antigua)
    }
}
