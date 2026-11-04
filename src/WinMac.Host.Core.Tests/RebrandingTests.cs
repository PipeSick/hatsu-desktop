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
    [InlineData("Host", "Hatsu Desktop", "HatsuDesktop")]
    [InlineData("Bar.App", "Hatsu Desktop — Barra superior", "HatsuDesktop.Bar")]
    [InlineData("Dock.App", "Hatsu Desktop — Dock", "HatsuDesktop.Dock")]
    [InlineData("Connect.App", "Hatsu Desktop — Connect", "HatsuDesktop.Connect")]
    public void Executables_have_the_public_file_name_description_and_the_Flux_icon(string proj, string title, string assembly)
    {
        var csproj = Read($"src/WinMac.{proj}/WinMac.{proj}.csproj");
        Assert.Contains($"<AssemblyTitle>{title}</AssemblyTitle>", csproj);
        Assert.Contains($"<AssemblyName>{assembly}</AssemblyName>", csproj);                                        // HatsuDesktop*.exe (Fase 2)
        Assert.Contains(@"<ApplicationIcon>..\..\assets\brand\HatsuDesktop.ico</ApplicationIcon>", csproj);      // UNA fuente de marca
        Assert.DoesNotContain("hatsu-provisional", csproj);
    }

    [Fact]
    public void Project_folders_namespaces_and_non_public_assemblies_keep_their_internal_names()
    {
        // el rebranding de ejecutables NO renombra carpetas de proyecto, espacios de nombres ni ensamblados de librería (contrato y compatibilidad)
        foreach (var p in new[] { "WinMac.Host", "WinMac.Bar.App", "WinMac.Dock.App", "WinMac.Connect.App", "WinMac.Host.Core", "WinMac.Ipc", "WinMac.Bar.Core" }) Assert.True(Directory.Exists(Path.Combine(Root(), "src", p)), p);
        Assert.Contains("<RootNamespace>WinMac.Host.App</RootNamespace>", Read("src/WinMac.Host/WinMac.Host.csproj"));
        Assert.Contains("namespace WinMac.Ipc;", Read("src/WinMac.Ipc/PipeNames.cs"));
    }

    [Fact]
    public void Flux_brand_kit_has_one_source_with_a_manifest_that_matches_every_file()
    {
        var dir = Path.Combine(Root(), "assets", "brand"); var m = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))).RootElement;
        Assert.False(File.Exists(Path.Combine(dir, "hatsu-provisional.ico")));   // el icono provisional de la Fase 1 ya no está
        int n = 0;
        foreach (var f in m.GetProperty("files").EnumerateObject())
        {
            var path = Path.Combine(dir, f.Name.Replace('/', Path.DirectorySeparatorChar)); Assert.True(File.Exists(path), f.Name);
            Assert.Equal(f.Value.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());   // nadie editó a mano un derivado
            n++;
        }

        Assert.True(n >= 15);
        Assert.Equal(m.GetProperty("source").GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(dir, "source", "flux-master-1024.original.png")))).ToLowerInvariant());
        Assert.True(File.Exists(Path.Combine(dir, "source", "make_brand.py")));
    }

    [Fact]
    public void Flux_ico_has_every_required_size_as_png_frames_and_the_pngs_are_rgba()
    {
        var b = File.ReadAllBytes(Path.Combine(Root(), "assets", "brand", "HatsuDesktop.ico"));
        Assert.Equal(0, BitConverter.ToUInt16(b, 0)); Assert.Equal(1, BitConverter.ToUInt16(b, 2));
        int n = BitConverter.ToUInt16(b, 4); var sizes = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            int e = 6 + i * 16, w = b[e] == 0 ? 256 : b[e]; sizes.Add(w); int len = BitConverter.ToInt32(b, e + 8), off = BitConverter.ToInt32(b, e + 12);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, b[off..(off + 4)]);                                  // fotograma PNG
            Assert.Equal(w, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(off + 16))); Assert.Equal(6, b[off + 25]);   // IHDR: ancho y tipo de color 6 = RGBA
            Assert.True(len > 100);
        }

        foreach (var s in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }) Assert.Contains(s, sizes);                  // bandeja a 100–200 %, barra de tareas, listas y alta densidad
        foreach (var png in new[] { 16, 24, 32, 48, 64, 128, 256, 512, 1024 })
        {
            var p = File.ReadAllBytes(Path.Combine(Root(), "assets", "brand", $"hatsu-flux-{png}.png"));
            Assert.Equal(png, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(16))); Assert.Equal(6, p[25]);
        }
    }

    [Theory]
    [InlineData(32)]
    [InlineData(128)]
    public void Embedded_logo_pixels_have_transparent_corners_and_no_dark_halo_outside_the_tile(int size)
    {
        var px = File.ReadAllBytes(Path.Combine(Root(), "assets", "brand", "embedded", $"hatsu-flux-{size}.bgra")); Assert.Equal(size * size * 4, px.Length);
        byte A(int x, int y) => px[(y * size + x) * 4 + 3];
        Assert.Equal(0, A(0, 0)); Assert.Equal(0, A(size - 1, 0)); Assert.Equal(0, A(0, size - 1)); Assert.Equal(0, A(size - 1, size - 1));   // esquinas transparentes (tile redondeado)
        Assert.Equal(255, A(size / 2, size / 2));                                                                                            // centro opaco
        for (int i = 0; i < px.Length; i += 4)   // premultiplicado: ningún color puede superar su alfa (si no, habría halos al componer)
            Assert.True(px[i] <= px[i + 3] + 1 && px[i + 1] <= px[i + 3] + 1 && px[i + 2] <= px[i + 3] + 1);
        // (el tile Flux recortado deja solo ≈0,75 px de margen a 32 px: el halo se vigila en la prueba de píxeles del master, no aquí)
    }

    [Fact]
    public void Brand_readme_documents_the_corrections_and_keeps_the_approved_palette()
    {
        var r = Read("assets/brand/README.md");
        Assert.Contains("Flux", r); Assert.Contains("halo", r); Assert.Contains("#7C3AED", r); Assert.Contains("#0B0618", r); Assert.Contains("no es vectorial", r, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("src/WinMac.Host.Core/HostConfig.cs", "\"HatsuDesktop.Bar.exe\"")]
    [InlineData("src/WinMac.Host.Core/HostConfig.cs", "\"HatsuDesktop.Dock.exe\"")]
    [InlineData("src/WinMac.Host.Core/HostConfig.cs", "\"HatsuDesktop.Connect.exe\"")]
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
        var pkg = Read("tools/Package-WinMac.ps1"); Assert.Contains("HatsuDesktop-$version", pkg); Assert.Contains("'HatsuDesktop/'", pkg); Assert.Contains("Uninstall-HatsuDesktop.ps1", pkg); Assert.Contains("HatsuDesktop.ico", pkg); Assert.Contains("Migrate-FromWinMac.ps1", pkg);
        var un = Read("tools/Uninstall-WinMac.ps1");
        Assert.Contains("-Name 'WinMac'", un);                                    // quita la clave de inicio automático (la misma de WinMac)
        Assert.Contains("'WinMac.Bar.App'", un); Assert.Contains("'HatsuDesktop.Bar'", un); Assert.Contains("'WinMac.exe'", un); Assert.Contains("'HatsuDesktop.exe'", un);   // reconoce procesos y ejecutable de ambas versiones
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

/// <summary>Fase 2: transición del inicio automático y de los ejecutables renombrados.</summary>
public class AutostartTransitionTests
{
    private const string New = @"C:\Apps\Hatsu Desktop\HatsuDesktop.exe", OldOther = @"C:\Old Apps\WinMac\WinMac.exe";
    private static readonly string NewDir = @"C:\Apps\Hatsu Desktop";

    [Fact] public void Disabled_autostart_stays_disabled_in_every_path()
    {
        Assert.Equal(AutostartState.NotConfigured, AutostartMigration.Assess(null, New, _ => true).State); Assert.Equal(AutostartState.NotConfigured, AutostartMigration.Assess("  ", New, _ => true).State);
        Assert.Null(AutostartMigration.Plan(null, New, _ => true)); Assert.Null(AutostartMigration.PlanAdopt(null, New, _ => true));   // ni siquiera con adopción explícita se CREA
    }

    [Fact] public void Already_pointing_here_is_current_and_untouched()
    { Assert.Equal(AutostartState.Current, AutostartMigration.Assess($"\"{New}\" --background", New, _ => true).State); Assert.Null(AutostartMigration.PlanAdopt($"\"{New}\" --background", New, _ => true)); }

    [Fact] public void Old_path_that_still_exists_is_another_install_and_is_NOT_changed_automatically()
    {
        var v = $"\"{OldOther}\" --background";
        Assert.Equal(AutostartState.OtherInstall, AutostartMigration.Assess(v, New, p => p == OldOther).State);
        Assert.Null(AutostartMigration.Plan(v, New, p => p == OldOther));                          // una ejecución de prueba nunca lo cambia
        Assert.Equal($"\"{New}\" --background", AutostartMigration.PlanAdopt(v, New, p => p == OldOther));   // solo la adopción explícita
    }

    [Fact] public void Old_path_that_was_deleted_is_an_orphan_and_is_repaired_automatically()
    { Assert.Equal(AutostartState.Orphan, AutostartMigration.Assess($"\"{OldOther}\" --background", New, p => p == New).State); Assert.Equal($"\"{New}\" --background", AutostartMigration.Plan($"\"{OldOther}\" --background", New, p => p == New)); }

    [Fact] public void Extracting_the_new_version_over_the_old_one_upgrades_in_place()
    {
        string stale = NewDir + @"\WinMac.exe";   // el ejecutable antiguo de ESTA carpeta (sigue existiendo junto a HatsuDesktop.exe)
        Assert.Equal(AutostartState.UpgradeInPlace, AutostartMigration.Assess($"\"{stale}\" --background", New, _ => true).State);
        Assert.Equal($"\"{New}\" --background", AutostartMigration.Plan($"\"{stale}\" --background", New, _ => true));
        Assert.Equal(AutostartState.OtherInstall, AutostartMigration.Assess($"\"{NewDir}\\OtraCosa.exe\" --background", New, _ => true).State);   // otro programa en la misma carpeta: no es la versión antigua
    }

    [Fact] public void Arguments_are_preserved_and_default_to_background()
    {
        Assert.Equal($"\"{New}\" --background --prueba", AutostartMigration.PlanAdopt($"\"{OldOther}\" --background --prueba", New, _ => true));
        Assert.Equal($"\"{New}\" --background", AutostartMigration.PlanAdopt($"\"{OldOther}\"", New, _ => true));
        Assert.Equal("--background --x", AutostartMigration.ArgsOf($"\"{OldOther}\" --background --x")); Assert.Null(AutostartMigration.ArgsOf($"\"{OldOther}\""));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Hatsu Desktop\\HatsuDesktop.exe\" --background", "C:\\Program Files\\Hatsu Desktop\\HatsuDesktop.exe")]
    [InlineData("C:\\Program Files\\WinMac\\WinMac.exe --background", "C:\\Program Files\\WinMac\\WinMac.exe")]   // sin comillas y con espacios: hasta el primer .exe
    [InlineData("C:\\x\\WinMac.exe", "C:\\x\\WinMac.exe")]
    public void Paths_with_spaces_are_parsed_quoted_or_not(string value, string exe) => Assert.Equal(exe, AutostartMigration.ExePathOf(value));

    [Theory] [InlineData("notepad")] [InlineData("\"sin cierre")] [InlineData("\"\"")]
    public void Unreadable_values_are_never_touched(string v)
    { Assert.Equal(AutostartState.Unreadable, AutostartMigration.Assess(v, New, _ => false).State); Assert.Null(AutostartMigration.Plan(v, New, _ => false)); Assert.Null(AutostartMigration.PlanAdopt(v, New, _ => false)); }

    [Fact] public void Repeating_any_plan_is_idempotent_and_reverting_to_the_old_package_is_a_plain_overwrite()
    {
        var v1 = AutostartMigration.PlanAdopt($"\"{OldOther}\" --background", New, _ => true)!;
        Assert.Null(AutostartMigration.PlanAdopt(v1, New, _ => true)); Assert.Null(AutostartMigration.Plan(v1, New, _ => true));                    // segunda vez: nada
        // reversión: el paquete antiguo, al activarlo, escribe SU valor; el nuevo lo trata entonces como «otra instalación viva» y no lo pisa solo
        Assert.Equal(AutostartState.OtherInstall, AutostartMigration.Assess($"\"{OldOther}\" --background", New, _ => true).State);
    }

    [Fact] public void Two_packages_extracted_in_different_places_never_hijack_each_other()
    {
        string a = @"C:\A\HatsuDesktop.exe", b = @"C:\B\HatsuDesktop.exe"; var stored = $"\"{a}\" --background";
        Assert.Null(AutostartMigration.Plan(stored, b, _ => true));                       // A existe: B no se lo lleva por su cuenta
        Assert.Equal($"\"{b}\" --background", AutostartMigration.PlanAdopt(stored, b, _ => true));   // pero el usuario puede decidirlo
    }

    // ── ajustes: la adopción solo se ofrece cuando hay otra instalación viva y exige acción del usuario
    [Fact] public void Settings_offers_adoption_only_when_another_live_install_is_started_by_windows()
    {
        var states = new Dictionary<string, ModuleState>();
        bool Has(SettingsInput i) => SettingsBuilder.Build(i).SelectMany(p => p.Groups).SelectMany(g => g.Rows).Any(r => r.Id == "autostart.adopt");
        Assert.False(Has(new SettingsInput(new HostConfig(), states, true)));
        var row = SettingsBuilder.Build(new SettingsInput(new HostConfig(), states, true) { AutostartForeignPath = OldOther }).SelectMany(p => p.Groups).SelectMany(g => g.Rows).Single(r => r.Id == "autostart.adopt");
        Assert.Equal(RowKind.Button, row.Kind); Assert.Contains(OldOther, row.Subtitle); Assert.Contains("lo confirmas", row.Subtitle); Assert.Contains("no se borra", row.Subtitle);
    }

    [Fact] public void The_adopt_action_runs_the_host_callback_only_and_reports_its_result()
    {
        var cfg = new ConfigService(Path.Combine(Path.GetTempPath(), "hatsu-adopt.json"), new HostConfig()); int calls = 0;
        var a = new SettingsActions(cfg, () => { }, _ => { }) { AdoptAutostart = () => { calls++; return true; } };
        Assert.True(a.Apply(new SettingsEdit("autostart.adopt", "button"))); Assert.Equal(1, calls);
        Assert.False(new SettingsActions(cfg, () => { }, _ => { }).Apply(new SettingsEdit("autostart.adopt", "button")));   // sin callback: no hace nada
    }

    // ── nombres de ejecutables: nadie puede quedarse con los antiguos por descuido
    [Fact] public void Source_strings_do_not_name_the_old_executables_except_where_legacy_support_needs_them()
    {
        var allowed = new[] { "AutostartMigration.cs", "SecurityDiagnostics.cs", "WindowArranger.cs", "SystemActions.cs" };
        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(".Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !allowed.Contains(Path.GetFileName(f))))
            foreach (var (l, i) in File.ReadLines(f).Select((l, i) => (l, i)))
                if (!l.TrimStart().StartsWith("//") && System.Text.RegularExpressions.Regex.IsMatch(l, "\"[^\"]*(?<![A-Za-z.])WinMac(\\.Bar\\.App|\\.Dock\\.App|\\.Connect\\.App)?\\.exe")) offenders.Add(Path.GetFileName(f) + ":" + (i + 1));
        Assert.True(offenders.Count == 0, "Cadenas con el nombre antiguo del ejecutable: " + string.Join(", ", offenders));
    }

    [Fact] public void Both_naming_generations_are_covered_by_process_lists_in_scripts_and_guards()
    {
        foreach (var name in new[] { "HatsuDesktop", "HatsuDesktop.Bar", "HatsuDesktop.Dock", "WinMac", "WinMac.Bar.App", "WinMac.Dock.App" })
            Assert.Contains($"'{name}'", Read("tools/Uninstall-WinMac.ps1"));
        var guard = Read("src/WinMac.System.Core/SystemActions.cs");
        foreach (var name in new[] { "\"hatsudesktop\"", "\"hatsudesktop.bar\"", "\"hatsudesktop.dock\"", "\"hatsudesktop.connect\"", "\"winmac\"", "\"winmac.bar.app\"", "\"winmac.dock.app\"" }) Assert.Contains(name, guard);
        var arr = Read("src/WinMac.Bar.Win/Workspaces/WindowArranger.cs"); Assert.Contains("\"HatsuDesktop.Bar\"", arr); Assert.Contains("\"WinMac.Bar.App\"", arr);
    }

    [Fact] public void Single_instance_and_quit_names_are_shared_by_old_and_new_so_a_new_exe_can_close_an_old_instance()
    {
        var host = Read("src/WinMac.Host/HostApp.cs");
        Assert.Contains(@"@""Local\WinMac.Host""", host); Assert.Contains(@"@""Local\WinMac.Host.Quit""", host); Assert.Contains("--replace-running", host); Assert.Contains("AbandonedMutexException", host);
        Assert.Contains("\"--adopt-autostart\"", Read("src/WinMac.Host/Program.cs"));
    }

    private static string Read(string rel) { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) { var f = Path.Combine(d.FullName, rel.Replace('/', Path.DirectorySeparatorChar)); if (File.Exists(f)) return File.ReadAllText(f); } throw new FileNotFoundException(rel); }
    private static string Root() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent) if (File.Exists(Path.Combine(d.FullName, "src", "Directory.Build.props"))) return d.FullName; throw new InvalidOperationException(); }
}
