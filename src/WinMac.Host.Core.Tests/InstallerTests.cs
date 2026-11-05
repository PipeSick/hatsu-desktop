using System.Xml.Linq;
using Xunit;

namespace WinMac.Host.Core.Tests;

/// <summary>Comprobaciones ESTRUCTURALES del instalador (corren en cualquier sistema). La instalación real se prueba en CI con tools/ci/Test-Msi.ps1.</summary>
public class InstallerTests
{
    private static string Root() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "LICENSE"))) d = d.Parent; return d!.FullName; }
    private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), rel));
    private static XDocument Wxs() => XDocument.Parse(Read(@"installer/Package.wxs"));
    private static IEnumerable<XElement> All(string name) => Wxs().Descendants().Where(e => e.Name.LocalName == name);
    private const string UpgradeCode = "ae38cbf7-6543-463e-9067-50e88f162fa1";

    [Fact] public void The_upgrade_code_is_the_documented_stable_one_in_both_places()
    {
        Assert.Equal(UpgradeCode, All("Package").Single().Attribute("UpgradeCode")!.Value, ignoreCase: true);
        Assert.Contains(UpgradeCode, Read("installer/Build-Msi.ps1")); Assert.Contains(UpgradeCode, Read("docs/48-fase3-instalador-onboarding.md"));
        Assert.Contains(UpgradeCode, Read("tools/ci/Test-Msi.ps1"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void The_product_identity_is_Hatsu_Desktop_and_never_WinMac()
    {
        var p = All("Package").Single(); Assert.Equal("Hatsu Desktop", p.Attribute("Name")!.Value); Assert.Equal("Hatsu Desktop", p.Attribute("Manufacturer")!.Value);
        Assert.DoesNotContain("WinMac", string.Concat(All("Shortcut").Select(s => s.Attribute("Name")!.Value)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(All("Directory").Select(d => d.Attribute("Name")?.Value ?? ""), n => n.Contains("WinMac", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public void It_installs_per_user_without_administrator_and_upgrades_in_place()
    {
        Assert.Equal("perUser", All("Package").Single().Attribute("Scope")!.Value); Assert.Contains("LocalAppDataFolder", Read("installer/Package.wxs"));
        Assert.DoesNotContain("ProgramFiles", Read("installer/Package.wxs")); Assert.DoesNotContain("ALLUSERS", Read("installer/Package.wxs"));
        var mu = All("MajorUpgrade").Single(); Assert.NotEmpty(mu.Attribute("DowngradeErrorMessage")!.Value);
    }

    [Fact] public void It_adds_nothing_that_the_user_did_not_ask_for()
    {
        var wxs = Read("installer/Package.wxs");
        Assert.Empty(All("ServiceInstall")); Assert.Empty(All("ServiceControl"));                                   // sin servicios
        Assert.DoesNotContain(All("RegistryValue"), r => (r.Attribute("Key")?.Value ?? "").Contains(@"CurrentVersion\Run", StringComparison.OrdinalIgnoreCase));   // sin inicio automático
        Assert.All(All("RegistryValue"), r => Assert.StartsWith(@"Software\Hatsu Desktop", r.Attribute("Key")!.Value));  // solo su propia clave
        Assert.Empty(All("ScheduledTask")); Assert.DoesNotContain("--hide-taskbar", wxs); Assert.DoesNotContain("install-autostart", wxs);
    }

    [Fact] public void Shortcuts_start_menu_always_desktop_only_when_chosen()
    {
        var features = All("Feature").ToDictionary(f => f.Attribute("Id")!.Value);
        Assert.Equal("1", features["Main"].Attribute("Level")!.Value); Assert.Equal("2", features["DesktopShortcutFeature"].Attribute("Level")!.Value);   // Level 2 > INSTALLLEVEL 1: apagado por defecto
        Assert.Contains(features["Main"].Descendants(), e => e.Name.LocalName == "ComponentRef" && e.Attribute("Id")!.Value == "StartMenuShortcut");
        Assert.Contains(features["DesktopShortcutFeature"].Descendants(), e => e.Name.LocalName == "ComponentRef" && e.Attribute("Id")!.Value == "DesktopShortcut");
        Assert.All(All("Shortcut"), s => { Assert.Equal("HatsuDesktop.ico", s.Attribute("Icon")!.Value); Assert.EndsWith("HatsuDesktop.exe", s.Attribute("Target")!.Value); });
    }

    [Fact] public void Uninstall_is_scoped_and_never_deletes_user_data()
    {
        var wxs = Read("installer/Package.wxs");
        var ca = All("CustomAction").Single(c => c.Attribute("Id")!.Value == "HatsuUninstallCleanup"); Assert.Contains("--uninstall-cleanup", ca.Attribute("ExeCommand")!.Value);
        Assert.Contains("UPGRADINGPRODUCTCODE", wxs); Assert.DoesNotContain(All("RemoveFile"), r => (r.Attribute("Directory")?.Value ?? "") != "INSTALLFOLDER");   // solo archivos de su propia carpeta
        Assert.DoesNotContain("AppData", wxs.Replace("LocalAppDataFolder", "")); Assert.DoesNotContain("WinMac", wxs.Replace("%APPDATA%\\WinMac", "").Replace("%LOCALAPPDATA%\\WinMac", "").Replace("antiguo", ""), StringComparison.Ordinal);
        Assert.Contains("--uninstall-cleanup", Read("src/WinMac.Host/Program.cs"));
    }

    [Fact] public void User_run_scripts_with_non_ascii_text_carry_a_UTF8_BOM_so_Windows_PowerShell_5_1_reads_them()
    {
        // sin BOM, PowerShell 5.1 lee el archivo como ANSI y un carácter como «…» o «—» pasa a ser una comilla tipográfica que rompe la sintaxis (visto en CI)
        var bytes = File.ReadAllBytes(Path.Combine(Root(), "tools", "Diagnose-Install.ps1")); Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..], "Diagnose-Install.ps1 debe empezar con BOM UTF-8");
        Assert.Contains("SOLO LECTURA", File.ReadAllText(Path.Combine(Root(), "tools", "Diagnose-Install.ps1")));
        foreach (var banned in new[] { "Set-ItemProperty", "New-ItemProperty", "Remove-Item", "Remove-ItemProperty", "New-Item", "Set-Content", "Invoke-WebRequest", "Invoke-RestMethod", "Disable-", "Add-MpPreference", "Set-MpPreference" })
            Assert.DoesNotContain(banned, File.ReadAllText(Path.Combine(Root(), "tools", "Diagnose-Install.ps1")));   // solo lectura: ni escribe, ni descarga, ni toca protecciones (Start-Process solo lanza citool.exe -lp con tiempo límite, para no colgarse)
    }

    [Fact] public void The_signing_requirements_document_identifies_the_policy_and_rules_out_evasion()
    {
        var d = File.ReadAllText(Path.Combine(Root(), "docs", "50-requisitos-distribucion-smart-app-control.md"));
        Assert.Contains("{0283ac0f-fff1-49ae-ada1-8a933130cad6}", d); Assert.Contains("VerifiedAndReputableDesktop", d); Assert.Contains("Smart App Control en modo aplicado", d.Replace("**Smart App Control está activado (modo aplicado)**", "Smart App Control en modo aplicado"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lo que NO se hará (elusión)", d); Assert.Contains("no garantiza", d); Assert.Contains("No se genera ni se pide instalar ningún MSI nuevo", d);
        var ps = File.ReadAllText(Path.Combine(Root(), "tools", "Diagnose-Install.ps1")); Assert.Contains("0283ac0f-fff1-49ae-ada1-8a933130cad6", ps); Assert.Contains("1283ac0f-fff1-49ae-ada1-8a933130cad6", ps); Assert.Contains("citool.exe", ps);
        Assert.Contains("-Require", File.ReadAllText(Path.Combine(Root(), "tools", "ci", "Verify-Signatures.ps1")));
    }

    [Fact] public void The_license_rtf_and_the_icon_are_the_single_brand_source()
    {
        Assert.StartsWith("{\\rtf1", Read("installer/License.rtf")); Assert.Contains(@"assets\brand\HatsuDesktop.ico", Read("installer/Build-Msi.ps1"));
        Assert.Contains("Apache License", Read("installer/License.rtf"));
    }

    [Fact] public void CI_builds_the_msi_installs_it_for_real_and_keeps_the_claims_honest()
    {
        var ci = Read(".github/workflows/ci.yml"); Assert.Contains("Build-Msi.ps1", ci); Assert.Contains("Test-Msi.ps1", ci); Assert.Contains("capturas-onboarding", ci);
        Assert.Contains("NO es una prueba en un Windows físico", ci); Assert.DoesNotContain("softprops/action-gh-release", ci); Assert.DoesNotContain("gh release", ci);   // nada se publica
        var t = Read("tools/ci/Test-Msi.ps1"); foreach (var s in new[] { "/i", "/x", "2.0.1", "downgrade", "--replace-running", "NO borra la configuración", "NO quita el inicio automático de otra copia" }) Assert.Contains(s, t);
    }

    [Fact] public void The_experimental_MSIX_manifest_is_a_full_trust_package_with_a_startup_task_and_no_evasion()
    {
        var t = File.ReadAllText(Path.Combine(Root(), "installer", "msix", "AppxManifest.xml.in"));
        var x = System.Xml.Linq.XDocument.Parse(t.Replace("@@IDENTITY_NAME@@", "A.B").Replace("@@PUBLISHER@@", "CN=T").Replace("@@PUBLISHER_DISPLAY@@", "T").Replace("@@VERSION@@", "2.0.1.0").Replace("@@VIRTUALIZATION@@", "").Replace("@@CAPABILITIES@@", ""));
        Assert.Contains("runFullTrust", t); Assert.Contains("windows.startupTask", t); Assert.Contains("HatsuDesktopStartup", t);
        Assert.Equal("HatsuDesktopStartup", WinMac_Autostart_TaskId());   // el código y el manifiesto usan el mismo identificador
        Assert.DoesNotContain("allowElevation", t); Assert.DoesNotContain("packagedServices", t);   // nada de elevación ni servicios
        var build = File.ReadAllText(Path.Combine(Root(), "installer", "msix", "Build-Msix.ps1"));
        Assert.Contains("Remove-Item $pfx", build);   // la clave privada de prueba no sobrevive al script
        Assert.NotNull(x.Root);
    }

    private static string WinMac_Autostart_TaskId() => System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(Root(), "src", "WinMac.Host", "Autostart.cs")), "StartupTaskId = \"([^\"]+)\"").Groups[1].Value;

    [Fact] public void The_Store_identity_is_exactly_the_one_reserved_in_Partner_Center_and_the_Store_package_is_never_signed_by_us()
    {
        var j = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "installer", "msix", "store-identity.json"))).RootElement;
        Assert.Equal("PipeSick.HatsuDesktop", j.GetProperty("identityName").GetString());
        Assert.Equal("CN=ACC0FCB8-4D92-4E20-9731-7254D5E43289", j.GetProperty("publisher").GetString());
        Assert.Equal("PipeSick", j.GetProperty("publisherDisplayName").GetString());
        Assert.Equal("PipeSick.HatsuDesktop_83tr6qzbbarj8", j.GetProperty("packageFamilyName").GetString());
        Assert.Equal("9NJ0XLP6NXVJ", j.GetProperty("storeId").GetString());
        var build = File.ReadAllText(Path.Combine(Root(), "installer", "msix", "Build-Msix.ps1"));
        Assert.Contains("store-unsigned", build); Assert.Contains("citest-signed", build); Assert.Contains("Identidad incorrecta", build);   // el artefacto de la Store es el sin firmar y se verifica su identidad
        var tpl = File.ReadAllText(Path.Combine(Root(), "installer", "msix", "AppxManifest.xml.in"));
        Assert.DoesNotContain("PipeSick", tpl); Assert.DoesNotContain("CiTest", tpl);   // la identidad sale de un único archivo
        foreach (var cap in new[] { "userNotificationListener", "runFullTrust" }) Assert.Contains(cap, tpl);
        foreach (var cap in new[] { "bluetooth", "location", "webcam" }) Assert.Contains(cap, build);
        Assert.DoesNotContain("allowElevation", tpl + build);
    }

    [Fact] public void A_StartupTask_launch_without_arguments_becomes_a_background_launch_and_nothing_else_changes()
    {
        Assert.Equal(["--background"], WinMac.Host.LaunchArguments.Effective([], true));
        Assert.Equal(["--config", "x", "--background"], WinMac.Host.LaunchArguments.Effective(["--config", "x"], true));
        Assert.Equal(["--background"], WinMac.Host.LaunchArguments.Effective(["--background"], true));   // sin duplicar
        Assert.Empty(WinMac.Host.LaunchArguments.Effective([], false));   // MSI/zip/Start: ni se toca
        Assert.Equal(["--diagnose"], WinMac.Host.LaunchArguments.Effective(["--diagnose"], false));
    }

    [Fact] public void The_privacy_policy_names_every_network_host_in_the_code_in_both_languages_and_has_no_open_verification_marks()
    {
        var srcDir = Path.Combine(Root(), "src");
        var hosts = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("Tests") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "https?://([a-zA-Z0-9.-]+\\.[a-z]{2,})").Select(m => m.Groups[1].Value))
            .Where(h => !h.EndsWith("microsoft.com") && !h.EndsWith("w3.org") && !h.EndsWith("github.com") && !h.Contains("example") && !h.EndsWith("schemas.microsoft.com")).Distinct().ToList();
        var es = File.ReadAllText(Path.Combine(Root(), "docs", "privacy", "POLITICA-DE-PRIVACIDAD.md")); var en = File.ReadAllText(Path.Combine(Root(), "docs", "privacy", "PRIVACY-POLICY.en.md"));
        Assert.NotEmpty(hosts);
        foreach (var h in hosts) { Assert.Contains(h, es); Assert.Contains(h, en); }
        foreach (var h in new[] { "music.163.com", "lrclib.net" }) { Assert.Contains(h, es); Assert.Contains(h, en); }   // letras de Hatsu Notch (código incluido en el paquete, no en este árbol)
        foreach (var d in new[] { es, en }) { Assert.DoesNotContain("VERIFICAR", d); Assert.DoesNotContain("VERIFY", d); Assert.Contains("Hatsu Notch", d); Assert.Contains("DPAPI", d); }
        Assert.DoesNotContain("telemetría", es.Replace("ni telemetría", "").Replace("sin telemetría", "").Replace("telemetría, ni analítica", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void The_approved_privacy_manifest_matches_the_policy_files_so_Pages_publishes_exactly_what_was_approved()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), "docs", "privacy", "APPROVED.sha256")).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToList();
        Assert.Equal(2, lines.Count);
        foreach (var l in lines)
        {
            var parts = l.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bytes = File.ReadAllBytes(Path.Combine(Root(), parts[1])); var lf = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));   // un checkout con CRLF (Windows) no debe cambiar el hash
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lf)).ToLowerInvariant();
            Assert.Equal(parts[0], actual);   // si editas una política, vuelve a revisarla y regenera este manifiesto a propósito
        }
    }
}
