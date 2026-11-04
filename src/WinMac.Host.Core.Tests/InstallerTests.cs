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
        foreach (var banned in new[] { "Set-ItemProperty", "New-ItemProperty", "Remove-Item", "Remove-ItemProperty", "New-Item", "Set-Content", "Start-Process", "Invoke-WebRequest", "Invoke-RestMethod", "Disable-", "Add-MpPreference", "Set-MpPreference" })
            Assert.DoesNotContain(banned, File.ReadAllText(Path.Combine(Root(), "tools", "Diagnose-Install.ps1")));   // solo lectura: ni escribe, ni descarga, ni toca protecciones
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
}
