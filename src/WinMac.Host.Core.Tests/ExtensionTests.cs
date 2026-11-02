using System.Text.Json;
using WinMac.Host.Extensions;
using Xunit;

namespace WinMac.Host.Tests;

// Extensiones (docs/30): manifiesto, permisos, intermediario y catálogo con carpetas temporales reales.
public class ExtensionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winmac-ext-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private const string Good = """{ "id": "pomodoro", "name": "Pomodoro", "version": "1.0.0", "api": 1, "entry": "Demo.exe", "args": ["pomodoro"], "permissions": ["activity", "notify"] }""";

    private string MakeSource(string json, string id = "pomodoro")
    {
        var d = Path.Combine(_root, "src-" + Guid.NewGuid().ToString("N")[..6], id); Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "extension.json"), json); File.WriteAllText(Path.Combine(d, "Demo.exe"), "MZ");
        return d;
    }

    [Theory]
    [InlineData("""{ "id": "Bad Id", "name": "x", "version": "1.0.0", "api": 1, "entry": "a.exe" }""")]
    [InlineData("""{ "id": "abc", "name": "x", "version": "1", "api": 1, "entry": "a.exe" }""")]
    [InlineData("""{ "id": "abc", "name": "x", "version": "1.0.0", "api": 2, "entry": "a.exe" }""")]
    [InlineData("""{ "id": "abc", "name": "x", "version": "1.0.0", "api": 1, "entry": "..\\..\\evil.exe" }""")]
    [InlineData("""{ "id": "abc", "name": "x", "version": "1.0.0", "api": 1, "entry": "run.bat" }""")]
    [InlineData("""{ "id": "abc", "name": "x", "version": "1.0.0", "api": 1, "entry": "a.exe", "permissions": ["filesystem"] }""")]
    public void InvalidManifestsAreRejected(string json) => Assert.Null(ExtensionManifest.Parse(json, _root).Manifest);

    [Fact]
    public void ValidManifestParses()
    {
        var (m, err) = ExtensionManifest.Parse(Good, _root);
        Assert.Null(err); Assert.Equal(ExtPermission.Activity | ExtPermission.Notify, m!.Permissions); Assert.Equal(["pomodoro"], m.Args);
    }

    private static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void BrokerNamespacesIdsAndOnlyAllowsGrantedTopics()
    {
        var m = ExtensionManifest.Parse(Good, _root).Manifest!; var b = new ExtensionBroker(); var t = DateTime.UtcNow;
        var f = b.Translate(m, ExtensionBroker.ActivityTopic, J("""{ "op": "upsert", "id": "t1", "title": "Concentración", "progress": 0.5 }"""), t, out _);
        Assert.Equal("notch.activity", f!.Value.Topic);
        Assert.Contains("ext:pomodoro:t1", JsonSerializer.Serialize(f.Value.Payload));
        Assert.Null(b.Translate(m with { Permissions = ExtPermission.None }, ExtensionBroker.ActivityTopic, J("""{ "op": "upsert", "id": "t1", "title": "x" }"""), t, out _));
        Assert.Null(b.Translate(m, "config.bar.set", J("""{ "x": 1 }"""), t, out _));   // never WinMac's own topics
        Assert.Null(b.Translate(m, ExtensionBroker.ActivityTopic, J("""{ "op": "upsert", "id": "../x", "title": "x" }"""), t, out _));
    }

    [Fact]
    public void FloodsAreLimitedAndAbusersAreStopped()
    {
        var m = ExtensionManifest.Parse(Good, _root).Manifest!; var b = new ExtensionBroker(); var t = DateTime.UtcNow; bool kill = false;
        Assert.NotNull(b.Translate(m, ExtensionBroker.NotifyTopic, J("""{ "title": "Fin" }"""), t, out _));
        Assert.Null(b.Translate(m, ExtensionBroker.NotifyTopic, J("""{ "title": "Otra" }"""), t.AddSeconds(1), out _));   // 1 aviso cada 5 s
        for (int i = 0; i < ExtensionBroker.MaxInvalid && !kill; i++) b.Translate(m, "algo.raro", J("{}"), t.AddSeconds(10 + i), out kill);
        Assert.True(kill);
    }

    [Fact]
    public void CatalogInstallsDisabledTogglesAndUninstallsOnlyItsFolder()
    {
        var cat = new ExtensionCatalog(Path.Combine(_root, "extensions"));
        var (id, err) = cat.Install(MakeSource(Good)); Assert.Null(err); Assert.Equal("pomodoro", id);
        var e = Assert.Single(cat.List()); Assert.False(e.Enabled); Assert.NotNull(e.Manifest);
        cat.SetEnabled("pomodoro", true); Assert.True(cat.List()[0].Enabled);
        var neighbour = Path.Combine(_root, "extensions-keep.txt"); File.WriteAllText(neighbour, "x");
        Assert.True(cat.Uninstall("pomodoro")); Assert.Empty(cat.List()); Assert.True(File.Exists(neighbour));
        Assert.False(cat.Uninstall("..\\..")); Assert.NotNull(cat.Install(_root).Error);
    }
}
