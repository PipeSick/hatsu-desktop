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
