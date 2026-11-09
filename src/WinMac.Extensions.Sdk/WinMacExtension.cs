using WinMac.Ipc;

namespace WinMac.Extensions;

/// <summary>
/// WinMac extension API v1 (docs/30). An extension is a separate process that WinMac starts with <c>--hosted --pipe &lt;name&gt;</c> and a token of its
/// own; with this class it can show activities and short notices in the Island — only what its manifest grants. Everything else is ignored by WinMac.
/// </summary>
public sealed class WinMacExtension : IAsyncDisposable
{
    public const int ApiVersion = 1;
    private readonly ModuleHostLink _link;

    private WinMacExtension(ModuleHostLink link) { _link = link; _link.ShutdownRequested += () => ShutdownRequested?.Invoke(); }

    /// <summary>Null when not started by WinMac (then the extension should just exit).</summary>
    public static WinMacExtension? Connect(string[] args, string id, string version)
    {
        var link = ModuleHostLink.FromArgs(args, "ext-" + id, version);
        if (link is null) return null;
        var e = new WinMacExtension(link); link.Start(); return e;
    }

    /// <summary>WinMac asks the extension to close (disabled, uninstalled or WinMac is exiting). Exit promptly.</summary>
    public event Action? ShutdownRequested;

    public bool IsConnected => _link.IsConnected;

    /// <summary>Shows or updates an activity (permission «activity»). <paramref name="progress"/> 0–1 or null; ids are local to the extension.</summary>
    public void ShowActivity(string id, string title, string? subtitle = null, double? progress = null, bool paused = false) =>
        _link.Event("ext.activity", new { op = "upsert", id, title, subtitle, progress, state = paused ? "paused" : "active" });

    /// <summary>Ends an activity (it goes to the Island's history).</summary>
    public void FinishActivity(string id, bool failed = false) => _link.Event("ext.activity", new { op = "finish", id, state = failed ? "failed" : "completed" });

    /// <summary>A short notice in the Island (permission «notify»; at most one every 5 seconds).</summary>
    public void Notify(string title, string? body = null) => _link.Event("ext.notify", new { title, body });

    public ValueTask DisposeAsync() => _link.DisposeAsync();
}
