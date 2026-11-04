using WinMac.Host;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>
/// Hatsu Desktop (Fase 1 del rebranding): identidad pública nueva SIN romper lo que ya existe. Son pruebas automáticas (texto y lógica pura en Linux/CI):
/// no sustituyen la validación de una actualización real en Windows (ver docs/46, checklist).
/// </summary>
public class RebrandingTests
{
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "Directory.Build.props"))) return d.FullName;
        throw new InvalidOperationException("No se encontró la raíz del repositorio");
    }

    private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));

    // ── identidad pública
    [Fact]
    public void Product_metadata_says_Hatsu_Desktop()
    {
        var p = Read("src/Directory.Build.props");
        Assert.Contains("<Product>Hatsu Desktop</Product>", p); Assert.Contains("<Company>Hatsu Desktop</Company>", p);
        Assert.Contains("antes WinMac", p);   // trazabilidad
        Assert.Contains("Apache-2.0", p);
    }

    [Theory]
    [InlineData("Host", "Hatsu Desktop")]
    [InlineData("Bar.App", "Hatsu Desktop — Barra superior")]
    [InlineData("Dock.App", "Hatsu Desktop — Dock")]
    [InlineData("Connect.App", "Hatsu Desktop — Connect")]
    public void Executables_show_the_new_name_in_task_manager_and_properties_and_keep_their_file_names(string proj, string title)
    {
        var csproj = Read($"src/WinMac.{proj}/WinMac.{proj}.csproj");
        Assert.Contains($"<AssemblyTitle>{title}</AssemblyTitle>", csproj);
        Assert.Contains(@"<ApplicationIcon>..\..\assets\brand\hatsu-provisional.ico</ApplicationIcon>", csproj);
        Assert.Matches(@"<AssemblyName>WinMac(\.[A-Za-z.]+)?</AssemblyName>", csproj);   // el nombre del archivo NO cambia en esta fase (compatibilidad)
    }

    [Fact]
    public void Provisional_icon_is_a_valid_multi_size_ico_and_is_marked_provisional()
    {
        var b = File.ReadAllBytes(Path.Combine(Root(), "assets", "brand", "hatsu-provisional.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(b, 0)); Assert.Equal(1, BitConverter.ToUInt16(b, 2));   // ICO
        int n = BitConverter.ToUInt16(b, 4); Assert.True(n >= 6);
        var sizes = Enumerable.Range(0, n).Select(i => b[6 + i * 16] == 0 ? 256 : (int)b[6 + i * 16]).ToHashSet();
        foreach (var s in new[] { 16, 32, 48, 256 }) Assert.Contains(s, sizes);   // bandeja, barra de tareas, listas y alta densidad
        Assert.Contains("PROVISIONAL", Read("assets/brand/README.md"));
        Assert.True(File.Exists(Path.Combine(Root(), "assets", "brand", "hatsu-provisional-512.png")));
    }

    [Fact]
    public void Readme_presents_Hatsu_Desktop_as_the_evolution_of_WinMac_and_keeps_the_license()
    {
        var r = Read("README.md");
        Assert.StartsWith("# Hatsu Desktop", r.TrimStart()); Assert.Contains("evolución de WinMac", r); Assert.Contains("Apache-2.0", r);
        Assert.Contains("Apache License", Read("LICENSE")); Assert.Contains("Copyright 2026 WinMac contributors", Read("NOTICE"));   // los avisos de copyright originales se conservan
    }

    [Fact]
    public void Public_texts_of_settings_and_tray_no_longer_say_WinMac_except_paths_and_file_names()
    {
        var states = new Dictionary<string, ModuleState> { ["bar"] = ModuleState.Running, ["dock"] = ModuleState.Running, ["notch"] = ModuleState.Failed, ["connect"] = ModuleState.Disabled };
        var texts = new List<string>();
        foreach (var page in WinMac.Host.SettingsBuilder.Build(new SettingsInput(new HostConfig(), states, true)))
        {
            texts.Add(page.Title);
            foreach (var g in page.Groups) { if (g.Header is not null) texts.Add(g.Header); if (g.Footer is not null) texts.Add(g.Footer); foreach (var r in g.Rows) { texts.Add(r.Title); if (r.Subtitle is not null) texts.Add(r.Subtitle); if (r.Reason is not null) texts.Add(r.Reason); } }
        }

        foreach (var i in TrayPanelModel.Build(states, "2.0")) { texts.Add(i.Text); if (i.Sub is not null) texts.Add(i.Sub); if (i.ActionLabel is not null) texts.Add(i.ActionLabel); }
        var bad = texts.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"WinMac(?!\.exe|\\|\.Bar|\.Dock)")).ToList();
        Assert.True(bad.Count == 0, "Textos públicos con «WinMac»: " + string.Join(" | ", bad.Take(5)));
        Assert.Contains(texts, t => t.Contains("Hatsu Desktop")); Assert.Contains(texts, t => t == "Salir de Hatsu Desktop");
    }

    // ── identificadores internos: contrato (no deben cambiar sin una migración explícita)
    [Theory]
    [InlineData("src/WinMac.Host/HostApp.cs", @"@""Local\WinMac.Host""")]
    [InlineData("src/WinMac.Host/HostApp.cs", @"@""Local\WinMac.Host.Quit""")]
    [InlineData("src/WinMac.Host/HostApp.cs", @"@""Local\WinMac.Host.Reopen""")]
    [InlineData("src/WinMac.Host/HostApp.cs", "\"WinMac\", \"winmac.json\"")]                 // %APPDATA%\WinMac\winmac.json
    [InlineData("src/WinMac.Host/HostApp.cs", "\"WinMac\", \"bar.json\"")]
    [InlineData("src/WinMac.Host/Autostart.cs", "DefaultName = \"WinMac\"")]                    // valor HKCU\...\Run
    [InlineData("src/WinMac.Dock.Win/Shell/DockDefaults.cs", "\"WinMac\", \"dock.json\"")]
    [InlineData("src/WinMac.Dock.App/Program.cs", "\"WinMac.Dock.SingleInstance\"")]
    [InlineData("src/WinMac.Bar.App/Program.cs", "\"WinMac.Bar.SingleInstance\"")]
    [InlineData("src/WinMac.Connect.App/Program.cs", "\"WinMac.Connect.SingleInstance\"")]
    [InlineData("src/WinMac.Ipc/PipeNames.cs", "\"WinMac.\"")]                                   // canal host ↔ módulos ↔ NotchWin
    [InlineData("src/WinMac.Host/Tray.cs", "\"WinMacHost\"")]                                    // clases de ventana que otros componentes buscan
    [InlineData("src/WinMac.Bar.Win/Rendering/BarWindow.cs", "\"WinMacBar\"")]
    [InlineData("src/WinMac.Dock.Win/Rendering/DockHost.cs", "\"WinMacDock\"")]
    [InlineData("src/WinMac.Host.Core/HostConfig.cs", "\"WinMac.Bar.App.exe\"")]
    [InlineData("src/WinMac.Host.Core/HostConfig.cs", @"""NotchWin\NotchWin.App.exe""")]
    public void Internal_persistent_identifiers_are_unchanged(string file, string needle) => Assert.Contains(needle, Read(file));

    [Fact]
    public void NotchWin_pin_is_kept_and_the_ipc_topics_are_untouched()
    {
        var pin = Read("tools/notch-pin.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
        Assert.Matches("^[0-9a-f]{40}$", pin);
        Assert.Contains("\"notch.feed\"", Read("src/WinMac.Bar.Core/Notch/NotchFeed.cs"));
    }

    [Fact]
    public void Package_scripts_use_the_public_name_and_the_uninstaller_still_handles_the_old_install()
    {
        var pkg = Read("tools/Package-WinMac.ps1"); Assert.Contains("HatsuDesktop-$version", pkg); Assert.Contains("'HatsuDesktop/'", pkg); Assert.Contains("Uninstall-HatsuDesktop.ps1", pkg);
        var un = Read("tools/Uninstall-WinMac.ps1");
        Assert.Contains("-Name 'WinMac'", un);                                    // quita la clave de inicio automático de la instalación anterior
        Assert.Contains("'WinMac.Bar.App'", un); Assert.Contains("WinMac.exe", un); // y reconoce sus procesos y su ejecutable
    }

    // ── compatibilidad con configuración antigua
    [Fact]
    public void An_old_config_loads_with_every_preference_kept_and_new_fields_defaulted()
    {
        string json = """
        { "version": 3, "autostart": true, "hideTaskbar": true,
          "modules": { "dock": { "enabled": false }, "connect": { "enabled": true } },
          "bar": { "showSeconds": true, "theme": "Dark", "barOrder": "Volume,Network", "showTaskManager": true, "ncWidgets": "calendar,weather" } }
        """;
        string path = Path.Combine(Path.GetTempPath(), "hatsu-old-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            File.WriteAllText(path, json); var c = HostConfig.Load(path);
            Assert.True(c.Autostart); Assert.True(c.HideTaskbar); Assert.False(c.Modules["dock"].Enabled); Assert.True(c.Modules["connect"].Enabled); Assert.True(c.Modules["bar"].Enabled);
            Assert.True(c.Bar!.ShowSeconds); Assert.True(c.Bar.ShowTaskManager); Assert.Equal("Volume,Network", c.Bar.BarOrder); Assert.Equal(WinMac.Bar.BarThemeMode.Dark, c.Bar.Theme);
            Assert.False(c.Bar.ShowPeripherals); Assert.False(c.Bar.ShowCleanup); Assert.False(c.Bar.PeripheralAlerts);   // lo nuevo, apagado por defecto
            // guardar y volver a cargar no pierde nada y no cambia de carpeta ni de nombre
            HostConfig.Save(path, c); var again = HostConfig.Load(path);
            Assert.Equal(c.Bar!.BarOrder, again.Bar!.BarOrder); Assert.Equal(c.Modules["dock"].Enabled, again.Modules["dock"].Enabled); Assert.True(again.Autostart);
        }
        finally { try { File.Delete(path); } catch (Exception) { } }
    }

    // ── inicio automático: sin duplicados ni accesos huérfanos
    private const string New = @"C:\Apps\HatsuDesktop\WinMac.exe", Old = @"C:\Apps\WinMac\WinMac.exe";

    [Fact] public void Autostart_without_a_value_is_left_alone() => Assert.Null(AutostartMigration.Plan(null, New, _ => true));
    [Fact] public void Autostart_already_pointing_here_is_left_alone() => Assert.Null(AutostartMigration.Plan($"\"{New}\" --background", New, _ => true));
    [Fact] public void Autostart_of_another_live_install_is_not_hijacked() => Assert.Null(AutostartMigration.Plan($"\"{Old}\" --background", New, p => p == Old));
    [Fact]
    public void Orphan_autostart_is_rewritten_to_this_install_and_the_fix_is_idempotent()
    {
        var plan = AutostartMigration.Plan($"\"{Old}\" --background", New, p => p == New);
        Assert.Equal($"\"{New}\" --background", plan);
        Assert.Null(AutostartMigration.Plan(plan, New, p => p == New));   // segunda vez: nada que hacer
    }

    [Theory] [InlineData("")] [InlineData("   ")] [InlineData("notepad")] [InlineData("\"sin cierre")]
    public void Garbled_autostart_values_are_never_touched(string v) => Assert.Null(AutostartMigration.Plan(v, New, _ => false));

    [Fact]
    public void Autostart_value_parsing_handles_quoted_and_plain_paths()
    {
        Assert.Equal(Old, AutostartMigration.ExePathOf($"\"{Old}\" --background")); Assert.Equal(@"C:\x\WinMac.exe", AutostartMigration.ExePathOf(@"C:\x\WinMac.exe --background"));
        Assert.Equal($"\"{New}\" --background", AutostartMigration.ValueFor(New));
    }
}
