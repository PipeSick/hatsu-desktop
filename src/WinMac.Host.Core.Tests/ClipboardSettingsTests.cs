using System.Text.Json;
using Xunit;

namespace WinMac.Host.Tests;

// Ajustes del portapapeles (docs/28): lista de apps excluidas editable y su envío a la Isla.
public class ClipboardSettingsTests
{
    [Fact]
    public void ExcludedAppsAreSanitisedAndDefaultWhenEmpty()
    {
        Assert.Equal(["KeePassXC", "Mi App", "tool.v2"], NotchSettings.ParseApps("KeePassXC.exe, Mi App; tool.v2, keepassxc, ../evil, rm -rf *"));
        Assert.Contains("Bitwarden", NotchSettings.ParseApps(null));
    }

    [Fact]
    public void TheIslandGetsTheListOnlyWhileExclusionsAreOn()
    {
        string Wire(NotchSettings n) => JsonSerializer.Serialize(n.ToWire());
        var on = new NotchSettings { ClipboardExcludedApps = "Foo, Bar" }.Sanitize();
        Assert.Contains("\"clipboardExcludedApps\":[\"Foo\",\"Bar\"]", Wire(on));
        Assert.Contains("\"clipboardExcludedApps\":[]", Wire(on with { ClipboardExclusionsOn = false }));
        Assert.True(ConfigService.SameNotch(on, on with { }));   // a text field: value equality, no spurious republish
    }
}

public class HotkeySettingsTests
{
    [Fact]
    public void ReportedShortcutsAppearWithTheirStateAndModule()
    {
        HotkeyStatus.Set(JsonDocument.Parse("""{ "items": [ { "keys": "Ctrl+Mayús+4", "purpose": "Capturar región", "module": "Barra · Capturas", "registered": true }, { "keys": "Ctrl+Mayús+6", "purpose": "Grabar / detener", "module": "Barra · Capturas", "registered": false, "problem": "Windows no lo aceptó" } ] }""").RootElement);
        var bar = SettingsBuilder.Build(new SettingsInput(new HostConfig(), new Dictionary<string, ModuleState>(), false)).Single(p => p.Id == "bar");
        var rows = bar.Groups.Single(g => g.Header == "Atajos de teclado").Rows;
        Assert.Contains(rows, r => r.Title.Contains("Ctrl+Mayús+4") && r.Title.Contains("✓"));
        Assert.Contains(rows, r => r.Title.Contains("Ctrl+Mayús+6") && r.Title.Contains("✗") && r.Title.Contains("Barra · Capturas"));
        Assert.Contains(rows, r => r.Id == "bar.keysreset");
        HotkeyStatus.Set(JsonDocument.Parse("{}").RootElement);
    }
}
