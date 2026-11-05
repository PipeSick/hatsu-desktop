using System.Text.Json;
using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Fase 3.2 (Store): agentes de IA desactivados de fábrica solo en instalaciones nuevas, sin tocar lo guardado; rebranding visible a «Hatsu Notch».</summary>
public class Phase32Tests
{
    [Fact] public void Store_factory_turns_agents_off_but_keeps_every_other_default_and_the_module_stays_activatable()
    {
        var off = NotchSettings.FactoryFor(true); var normal = NotchSettings.FactoryFor(false);
        Assert.False(off.AgentsEnabled); Assert.False(off.Sections.Single(s => s.Id == "agents").Enabled);
        Assert.True(normal.AgentsEnabled); Assert.True(normal.Sections.Single(s => s.Id == "agents").Enabled);   // fuera de la Store nada cambia
        Assert.Equal(normal.Sections.Where(s => s.Id != "agents"), off.Sections.Where(s => s.Id != "agents"));   // el resto de secciones idéntico (cámara sigue apagada)
        Assert.Equal(normal with { AgentsEnabled = false, Sections = off.Sections }, off);
        // totalmente funcional: se activa a mano por el mismo canal de ajustes (parche) y por la pestaña
        var on = off.ApplyPatch(JsonDocument.Parse("{\"agentsEnabled\":true}").RootElement);
        Assert.True(on.AgentsEnabled);
        var idx = off.Sections.ToList().FindIndex(s => s.Id == "agents"); Assert.True(off.ToggleSection(idx).Sections[idx].Enabled);
    }

    [Fact] public void Saved_notch_settings_are_never_overwritten_by_the_Store_factory()
    {
        var saved = NotchSettings.FactoryFor(false) with { AgentsEnabled = true };   // preferencia ya guardada: activado
        var cfg = new HostConfig { Notch = saved };
        var json = JsonSerializer.Serialize(cfg); var back = JsonSerializer.Deserialize<HostConfig>(json)!;
        Assert.True(back.Notch!.Sanitize().AgentsEnabled);
        Assert.True(back.Notch.Sections.Single(s => s.Id == "agents").Enabled);
        // el valor de fábrica solo se usa cuando Notch es null
        Assert.Null(new HostConfig().Notch);
    }

    [Fact] public void Visible_texts_say_Hatsu_Notch_and_never_the_old_names()
    {
        string root = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(root, "installer", "Package.wxs"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("raíz del repositorio no encontrada");
        var visible = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"([^\"\\\\\\n]*(?:\\\\.[^\"\\\\\\n]*)*)\"").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value))
                if (m.Contains("Dynamic Island") || m.Contains("La Isla (NotchWin)")) visible.Add(f + ": " + m);
        Assert.Empty(visible);   // "Dynamic Island" es marca de Apple; el nombre del componente es Hatsu Notch
        var settings = File.ReadAllText(Path.Combine(root, "src", "WinMac.Host.Core", "Settings", "SettingsModel.cs"));
        Assert.Contains("new SettingsPage(\"notch\", \"Hatsu Notch\"", settings);
    }

    [Fact] public void Cleanup_really_deletes_files_without_the_Recycle_Bin_so_the_warning_stays_true()
    {
        string root = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(root, "installer", "Package.wxs"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("raíz no encontrada");
        var fs = File.ReadAllText(Path.Combine(root, "src", "WinMac.Bar.Win", "Shell", "Cleanup", "RealFileSystem.cs"));
        Assert.Contains("File.Delete(path)", fs);
        var deleteMethod = fs[fs.IndexOf("File.Delete(path)", StringComparison.Ordinal)..];
        Assert.DoesNotContain("SHFileOperation", fs); Assert.DoesNotContain("FOF_ALLOWUNDO", fs);   // si algún día va a la Papelera, hay que actualizar aviso y política de privacidad
        var dialog = File.ReadAllText(Path.Combine(root, "src", "WinMac.Bar.Win", "Rendering", "BarHost.Cleanup.cs")); Assert.Contains("CleanupText.PermanentDeletion", dialog);
        foreach (var f in new[] { "POLITICA-DE-PRIVACIDAD.md", "PRIVACY-POLICY.en.md" }) Assert.Contains("Papelera", File.ReadAllText(Path.Combine(root, "docs", "privacy", f)).Replace("Recycle Bin", "Papelera"));
    }

    [Fact] public void Live_files_do_not_reference_the_old_repository_name_except_the_allowed_compatibility_spots()
    {
        string root = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(root, "installer", "Package.wxs"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("raíz no encontrada");
        var allowed = new[] { "tools/site/verify_site.py" };   // acepta ambos nombres en el contacto de las políticas (GitHub redirige tras renombrar)
        var live = new[] { ".github", "installer", "tools" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !allowed.Any(a => f.Replace('\\', '/').EndsWith(a))).ToList();
        var hits = live.Where(f => { try { return File.ReadAllText(f).Contains("win11-macos"); } catch (Exception) { return false; } }).Select(f => Path.GetRelativePath(root, f)).ToList();
        Assert.Empty(hits);
    }

    [Fact] public void Launchpad_dock_topic_is_the_same_string_on_both_sides_and_the_dock_never_hooks_the_windows_key()
    {
        string root = AppContext.BaseDirectory; while (!File.Exists(Path.Combine(root, "installer", "Package.wxs"))) root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("raíz no encontrada");
        string Read(string rel) => File.ReadAllText(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("Topic = \"launchpad.state\"", Read("src/WinMac.Bar.Core/LaunchpadStatePublisher.cs")); Assert.Contains("Topic = \"launchpad.state\"", Read("src/WinMac.Dock.Core/LaunchpadPin.cs"));
        // no se intercepta la tecla Windows otra vez: el único gancho sigue siendo el de la Barra
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", "WinMac.Dock.Win"), "*.cs", SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "WinMac.Dock.App"), "*.cs")).Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "WinMac.Dock.Core"), "*.cs")))
            if (!f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) Assert.DoesNotContain("SetWindowsHookEx", File.ReadAllText(f));
    }
}
